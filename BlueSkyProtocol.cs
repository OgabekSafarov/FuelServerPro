using System;
using System.Globalization;

namespace FuelServerPro
{
    public static class BlueSkyProtocol
    {
        public const byte StartByte = 0xF5;

        public static byte CalculateCRC(byte[] frame, int length)
        {
            byte crc = 0;
            for (int i = 0; i < length; i++)
            {
                crc ^= frame[i];
            }

            return (byte)(crc & 0x7F);
        }

        public static bool TryValidateFrame(byte[]? frame)
        {
            if (frame == null || frame.Length < 5 || frame[0] != StartByte || (frame[2] & 0xF0) != 0xA0)
            {
                return false;
            }

            int length = frame[2] & 0x0F;
            if (length < 2)
            {
                return false;
            }

            int expectedLength = length + 3;
            if (frame.Length != expectedLength)
            {
                return false;
            }

            byte expectedCrc = CalculateCRC(frame, frame.Length - 1);
            return frame[frame.Length - 1] == expectedCrc;
        }

        public static bool TryGetSingleFrame(byte[] buffer, out byte[] frame, out int bytesConsumed)
        {
            frame = Array.Empty<byte>();
            bytesConsumed = 0;

            if (buffer == null || buffer.Length < 5)
            {
                return false;
            }

            for (int start = 0; start <= buffer.Length - 5; start++)
            {
                if (buffer[start] != StartByte || (buffer[start + 2] & 0xF0) != 0xA0)
                {
                    continue;
                }

                int expectedLength = (buffer[start + 2] & 0x0F) + 3;
                if (expectedLength < 5 || start + expectedLength > buffer.Length)
                {
                    continue;
                }

                var candidate = new byte[expectedLength];
                Array.Copy(buffer, start, candidate, 0, expectedLength);
                if (!TryValidateFrame(candidate))
                {
                    continue;
                }

                frame = candidate;
                bytesConsumed = start + expectedLength;
                return true;
            }

            return false;
        }

        public static byte[] ToBCD(long value, int byteCount)
        {
            if (value < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "BCD values cannot be negative.");
            }

            if (byteCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(byteCount));
            }

            string digits = value.ToString(CultureInfo.InvariantCulture).PadLeft(byteCount * 2, '0');
            if (digits.Length > byteCount * 2)
            {
                throw new OverflowException($"Value does not fit in {byteCount} BCD bytes.");
            }

            byte[] bytes = new byte[byteCount];
            for (int i = 0; i < byteCount; i++)
            {
                int high = digits[i * 2] - '0';
                int low = digits[i * 2 + 1] - '0';
                bytes[i] = (byte)((high << 4) | low);
            }

            return bytes;
        }

        public static bool TryFromBCD(byte[]? data, int offset, int length, out long value)
        {
            value = 0;
            if (data == null || offset < 0 || length <= 0 || offset > data.Length - length)
            {
                return false;
            }

            try
            {
                for (int i = offset; i < offset + length; i++)
                {
                    int high = data[i] >> 4;
                    int low = data[i] & 0x0F;
                    if (high > 9 || low > 9)
                    {
                        value = 0;
                        return false;
                    }

                    value = checked(value * 100 + high * 10 + low);
                }

                return true;
            }
            catch (OverflowException)
            {
                value = 0;
                return false;
            }
        }

        public static long FromBCD(byte[] data, int offset, int length)
        {
            return TryFromBCD(data, offset, length, out long value) ? value : 0;
        }

        public static byte[] GetData(byte[] frame)
        {
            if (!TryValidateFrame(frame))
            {
                throw new ArgumentException("Frame is invalid.", nameof(frame));
            }

            int dataLength = frame.Length - 5;
            var data = new byte[dataLength];
            Array.Copy(frame, 3, data, 0, dataLength);
            return data;
        }

        public static bool IsResponseTo(byte[]? frame, byte address, byte command)
        {
            return TryValidateFrame(frame) && frame![1] == address && frame[^2] == command;
        }

        public static byte[] BuildFrame(byte address, byte command, byte[]? data = null)
        {
            data ??= Array.Empty<byte>();

            if (address is < 1 or > 16)
            {
                throw new ArgumentOutOfRangeException(nameof(address), "Device address must be between 1 and 16.");
            }

            int payloadLength = data.Length + 2;
            if (payloadLength > 0x0F)
            {
                throw new ArgumentOutOfRangeException(nameof(data), "Protocol payload exceeds the frame length limit.");
            }

            byte lengthByte = (byte)(0xA0 | payloadLength);

            int totalLength = 3 + data.Length + 2;
            byte[] frame = new byte[totalLength];

            frame[0] = StartByte;
            frame[1] = address;
            frame[2] = lengthByte;

            if (data.Length > 0)
            {
                Array.Copy(data, 0, frame, 3, data.Length);
            }

            frame[3 + data.Length] = command;
            frame[totalLength - 1] = CalculateCRC(frame, totalLength - 1);

            return frame;
        }

        public static bool IsStatusFrame(byte[]? frame)
        {
            return TryValidateFrame(frame) && frame![^2] == 0xD5 && frame.Length == 6;
        }

        public static bool IsVolumeFrame(byte[]? frame)
        {
            return TryValidateFrame(frame) && frame![^2] == 0xD9 && frame.Length == 13;
        }
    }
}
