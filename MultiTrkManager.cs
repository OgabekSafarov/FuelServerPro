using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace FuelServerPro
{
    public class MultiTrkManager
    {
        private readonly object _portLock = new();
        private SerialPort? _serialPort;
        private CancellationTokenSource? _cts;
        private volatile bool _isRunning;

        public ConcurrentDictionary<byte, TrkDevice> Devices { get; } = new();
        public bool IsPortOpen => _serialPort?.IsOpen == true;
        public string? ConnectedPortName => _serialPort?.IsOpen == true ? _serialPort.PortName : null;

        public bool ConnectPort(string portName)
        {
            if (string.IsNullOrWhiteSpace(portName))
            {
                return false;
            }

            Stop();

            try
            {
                _serialPort = new SerialPort(portName, 9600, Parity.Even, 8, StopBits.One)
                {
                    ReadTimeout = 200,
                    WriteTimeout = 200,
                    DtrEnable = true,
                    RtsEnable = true
                };

                lock (_portLock)
                {
                    _serialPort.Open();
                }

                _isRunning = true;
                var cancellation = new CancellationTokenSource();
                _cts = cancellation;

                Logger.Log($"[PORT] {portName} ochildi. Qidirish boshlandi...");

                _ = Task.Run(async () =>
                {
                    try
                    {
                        ScanDevices();
                        await PollingLoop(cancellation.Token);
                    }
                    catch (OperationCanceledException)
                    {
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"[PORT XATO] {ex.Message}");
                    }
                });

                return true;
            }
            catch (Exception ex)
            {
                Stop();
                Logger.Log($"[PORT XATO] {ex.Message}");
                return false;
            }
        }

        public void Stop()
        {
            _isRunning = false;
            var cancellation = _cts;
            _cts = null;
            cancellation?.Cancel();

            lock (_portLock)
            {
                if (_serialPort != null)
                {
                    if (_serialPort.IsOpen)
                    {
                        _serialPort.Close();
                    }

                    _serialPort.Dispose();
                    _serialPort = null;
                }
            }

            Devices.Clear();
        }

        public List<byte> ScanDevices()
        {
            var found = new List<byte>();

            for (byte address = 1; address <= 16; address++)
            {
                byte[]? response = ExecRawCmd(address, 0xD5, timeoutMs: 250);
                if (BlueSkyProtocol.IsStatusFrame(response))
                {
                    var device = Devices.GetOrAdd(address, a => new TrkDevice(a));
                    device.CurrentStateByte = response![3];
                    device.IsConnected = true;
                    found.Add(address);
                    Logger.Log($"Topildi: TRK Manzil {address}");
                }

                Thread.Sleep(20);
            }

            return found;
        }

        public bool RefreshDeviceStatus(byte address)
        {
            byte[]? response = ExecRawCmd(address, 0xD5);
            if (!BlueSkyProtocol.IsStatusFrame(response) || !Devices.TryGetValue(address, out var device))
            {
                return false;
            }

            device.CurrentStateByte = response![3];
            device.IsConnected = true;
            return true;
        }

        public bool ReadVolume(byte address, out long volume, out long amount)
        {
            volume = 0;
            amount = 0;

            if (!Devices.TryGetValue(address, out var device))
            {
                return false;
            }

            byte[]? data = ReadRawData(address, 0xD9, 8);
            if (data == null ||
                !BlueSkyProtocol.TryFromBCD(data, 0, 4, out long parsedVolume) ||
                !BlueSkyProtocol.TryFromBCD(data, 4, 4, out long parsedAmount))
            {
                return false;
            }

            device.CurrentVolume = volume = parsedVolume;
            device.CurrentAmount = amount = parsedAmount;
            return true;
        }

        public bool ReadPrice(byte address, out long price)
        {
            price = 0;
            byte[]? data = ReadRawData(address, 0xB6, 3);
            if (data == null || !BlueSkyProtocol.TryFromBCD(data, 0, 3, out price))
            {
                return false;
            }

            if (Devices.TryGetValue(address, out var device))
            {
                device.CurrentPrice = price;
            }

            return true;
        }

        public bool ReadCounters(byte address, byte command, out long volume, out long amount)
        {
            volume = 0;
            amount = 0;
            if (command is not (0xC5 or 0xC7))
            {
                return false;
            }

            byte[]? data = ReadRawData(address, command, 12);
            if (data == null ||
                !BlueSkyProtocol.TryFromBCD(data, 0, 6, out long parsedVolume) ||
                !BlueSkyProtocol.TryFromBCD(data, 6, 6, out long parsedAmount))
            {
                return false;
            }

            volume = parsedVolume;
            amount = parsedAmount;
            if (Devices.TryGetValue(address, out var device))
            {
                if (command == 0xC5)
                {
                    device.TotalVolume = volume;
                    device.TotalAmount = amount;
                }
                else
                {
                    device.ShiftVolume = volume;
                    device.ShiftAmount = amount;
                }
            }

            return true;
        }

        public bool SetPrice(byte address, long price)
        {
            return ExecuteAcknowledgedCommand(address, 0xB2, BlueSkyProtocol.ToBCD(price, 3), statusResponse: true);
        }

        public bool SetPresetVolume(byte address, decimal liters)
        {
            long scaledValue = checked((long)(liters * 100m));
            return ExecuteAcknowledgedCommand(address, 0xB9, BlueSkyProtocol.ToBCD(scaledValue, 4));
        }

        public bool SetPresetAmount(byte address, long amount)
        {
            long scaledValue = checked(amount * 100);
            return ExecuteAcknowledgedCommand(address, 0xB5, BlueSkyProtocol.ToBCD(scaledValue, 4));
        }

        public bool StartPump(byte address)
        {
            return ExecuteAcknowledgedCommand(address, 0xC3, statusResponse: true);
        }

        public bool StopPump(byte address)
        {
            return ExecuteAcknowledgedCommand(address, 0xCA);
        }

        public bool PausePump(byte address)
        {
            return ExecuteAcknowledgedCommand(address, 0xBA);
        }

        public bool ResumePump(byte address)
        {
            return ExecuteAcknowledgedCommand(address, 0xB3);
        }

        public bool ClearShiftCounters(byte address)
        {
            return ExecuteAcknowledgedCommand(address, 0xEA);
        }

        public bool SelectNozzle(byte address, byte nozzleAddress)
        {
            return ExecuteAcknowledgedCommand(address, 0xA1, [nozzleAddress], statusResponse: true);
        }

        public byte[]? ReadPreset(byte address)
        {
            return ReadRawData(address, 0xA7, 5);
        }

        public byte[]? ReadCardId(byte address)
        {
            byte[]? data = ReadRawData(address, 0xA8, 4);
            if (data != null && Devices.TryGetValue(address, out var device))
            {
                device.CardId = data;
            }

            return data;
        }

        public bool ReadErrorCode(byte address, out byte errorCode)
        {
            errorCode = 0;
            byte[]? data = ReadRawData(address, 0xA9, 1);
            if (data == null)
            {
                return false;
            }

            errorCode = data[0];
            if (Devices.TryGetValue(address, out var device))
            {
                device.ErrorCode = errorCode;
            }

            return true;
        }

        public bool ClearErrorFlag(byte address)
        {
            return ExecuteAcknowledgedCommand(address, 0xAB, statusResponse: true);
        }

        public bool ClearPresetFlag(byte address)
        {
            return ExecuteAcknowledgedCommand(address, 0xAA);
        }

        public byte[]? ReadDeviceId(byte address)
        {
            byte[]? data = ReadRawData(address, 0xD7, 4);
            if (data != null && Devices.TryGetValue(address, out var device))
            {
                device.DeviceId = data;
            }

            return data;
        }

        public byte[]? ReadSolenoid(byte address)
        {
            byte[]? data = ReadRawData(address, 0xD6, 2);
            if (data != null && Devices.TryGetValue(address, out var device))
            {
                device.SolenoidData = data;
            }

            return data;
        }

        public bool SetSolenoid(byte address, byte state)
        {
            return ExecuteAcknowledgedCommand(address, 0xD2, [state], statusResponse: true);
        }

        public byte[]? ReadRawData(byte address, byte command, int expectedLength, byte[]? requestData = null)
        {
            byte[]? response = ExecRawCmd(address, command, requestData);
            if (response == null)
            {
                return null;
            }

            byte[] data = BlueSkyProtocol.GetData(response);
            return data.Length == expectedLength ? data : null;
        }

        public bool ExecuteAcknowledgedCommand(byte address, byte command, byte[]? data = null, bool statusResponse = false)
        {
            byte[]? response = ExecRawCmd(address, command, data);
            if (response == null)
            {
                return false;
            }

            byte[] responseData = BlueSkyProtocol.GetData(response);
            return statusResponse
                ? responseData.Length == 1 && responseData[0] == 0x59
                : responseData.Length == 0;
        }

        private async Task PollingLoop(CancellationToken token)
        {
            while (_isRunning && !token.IsCancellationRequested)
            {
                try
                {
                    var addresses = Devices.Keys.ToList();
                    if (addresses.Count == 0)
                    {
                        await Task.Delay(1000, token);
                        continue;
                    }

                    foreach (var address in addresses)
                    {
                        if (!_isRunning || token.IsCancellationRequested)
                        {
                            break;
                        }

                        bool hasAnswer = RefreshDeviceStatus(address);

                        if (!hasAnswer && Devices.TryGetValue(address, out var disconnectedDevice))
                        {
                            disconnectedDevice.IsConnected = false;
                        }

                        if (hasAnswer && Devices.TryGetValue(address, out var deviceInfo) && (!deviceInfo.IsNozzleHanged || deviceInfo.IsFilling))
                        {
                            ReadVolume(address, out _, out _);
                        }

                        await Task.Delay(30, token);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Logger.Log($"[POLLING XATO] {ex.Message}");
                }
            }
        }

        public byte[]? ExecRawCmd(byte address, byte command, byte[]? data = null, int timeoutMs = 300)
        {
            byte[] request = BlueSkyProtocol.BuildFrame(address, command, data);
            return SendAndReceive(request, timeoutMs);
        }

        private byte[]? SendAndReceive(byte[] request, int timeoutMs)
        {
            lock (_portLock)
            {
                try
                {
                    if (_serialPort == null || !_serialPort.IsOpen)
                    {
                        return null;
                    }

                    _serialPort.DiscardInBuffer();
                    _serialPort.Write(request, 0, request.Length);

                    var readBuffer = new List<byte>();
                    var stopAt = Stopwatch.StartNew();

                    while (stopAt.ElapsedMilliseconds < timeoutMs)
                    {
                        if (_serialPort.BytesToRead <= 0)
                        {
                            Thread.Sleep(10);
                            continue;
                        }

                        int bytesToRead = _serialPort.BytesToRead;
                        var chunk = new byte[Math.Min(bytesToRead, 64)];
                        int read = _serialPort.Read(chunk, 0, chunk.Length);
                        if (read <= 0)
                        {
                            break;
                        }

                        readBuffer.AddRange(chunk.Take(read));

                        while (BlueSkyProtocol.TryGetSingleFrame(readBuffer.ToArray(), out var frame, out var consumed))
                        {
                            readBuffer.RemoveRange(0, consumed);
                            if (BlueSkyProtocol.IsResponseTo(frame, request[1], request[^2]))
                            {
                                return frame;
                            }
                        }

                        if (readBuffer.Count > 256)
                        {
                            readBuffer.Clear();
                            break;
                        }
                    }

                    return null;
                }
                catch (Exception ex)
                {
                    Logger.Log($"[SERIAL XATO] {ex.Message}");
                    return null;
                }
            }
        }
    }
}
