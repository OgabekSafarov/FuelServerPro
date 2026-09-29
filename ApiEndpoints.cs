using System;
using System.Linq;
using System.Threading;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace FuelServerPro
{
    internal static class ApiEndpoints
    {
        private static readonly string[] SupportedMethods = [HttpMethods.Get, HttpMethods.Post];

        public static void MapTrkApi(this WebApplication app)
        {
            app.MapGet("/", () => Results.Redirect("/swagger"))
                .ExcludeFromDescription();

            app.MapGet("/swagger", () => Results.Content(SwaggerUiHtml, "text/html; charset=utf-8"))
                .ExcludeFromDescription();
            app.MapGet("/swagger/index.html", () => Results.Content(SwaggerUiHtml, "text/html; charset=utf-8"))
                .ExcludeFromDescription();

            Describe(app.MapMethods("/connect", SupportedMethods, ([FromQuery(Name = "port")] string? port, MultiTrkManager manager) =>
            {
                string portName = string.IsNullOrWhiteSpace(port) ? "COM6" : port;
                return manager.ConnectPort(portName)
                    ? Success(new { success = true, port = portName })
                    : Failure("Could not open the serial port.", StatusCodes.Status503ServiceUnavailable);
            }), "Connect", "Connect to the dispenser serial port.", "Opens the requested RS-485 port, defaulting to COM6.");

            Describe(app.MapMethods("/disconnect", SupportedMethods, (MultiTrkManager manager) =>
            {
                manager.Stop();
                return Success(new { success = true });
            }), "Disconnect", "Disconnect the serial port.", "Stops polling, closes the serial port, and clears device state.");

            Describe(app.MapMethods("/scan", SupportedMethods, (MultiTrkManager manager) =>
            {
                if (!manager.IsPortOpen)
                {
                    return Failure("Serial port is not connected.", StatusCodes.Status503ServiceUnavailable);
                }

                return Success(new { success = true, found = manager.ScanDevices() });
            }), "ScanDevices", "Scan addresses 1 through 16.", "Queries each configured dispenser address on the connected bus.");

            Describe(app.MapMethods("/status_all", SupportedMethods, (MultiTrkManager manager) =>
            {
                var devices = manager.Devices.Values.OrderBy(device => device.Address).Select(ToStatusResponse).ToList();
                return Success(new { success = true, devices });
            }), "GetAllStatus", "Get cached status for all discovered dispensers.", "Returns each device state and the latest volume and amount values.");

            Describe(app.MapMethods("/status", SupportedMethods, ([FromQuery(Name = "addr")] int? addr, MultiTrkManager manager) =>
            {
                if (!TryGetAddress(addr, out byte address))
                {
                    return Failure("addr must be a device address from 1 to 16.", StatusCodes.Status400BadRequest);
                }

                if (!manager.Devices.TryGetValue(address, out TrkDevice? device) || !manager.RefreshDeviceStatus(address))
                {
                    return Failure("Device not found or did not respond.", StatusCodes.Status404NotFound);
                }

                if (device.IsFilling)
                {
                    manager.ReadVolume(address, out _, out _);
                }

                return Success(ToStatusResponse(device));
            }), "GetStatus", "Refresh one dispenser status.", "Reads command 0xD5 and refreshes volume and amount while filling.");

            Describe(app.MapMethods("/cmd/d5_status", SupportedMethods, ([FromQuery(Name = "addr")] int? addr, MultiTrkManager manager) =>
            {
                if (!TryGetAddress(addr, out byte address)) return InvalidAddress();
                return manager.RefreshDeviceStatus(address) && manager.Devices.TryGetValue(address, out TrkDevice? device)
                    ? Success(new { success = true, device = ToStatusResponse(device) })
                    : Failure("No valid status response.", StatusCodes.Status504GatewayTimeout);
            }), "ReadStatus", "Read dispenser state (0xD5).", "Returns the decoded state bits from the one-byte status payload.");

            Describe(app.MapMethods("/cmd/d9_read_volume", SupportedMethods, ([FromQuery(Name = "addr")] int? addr, MultiTrkManager manager) =>
            {
                if (!TryGetAddress(addr, out byte address)) return InvalidAddress();
                return manager.ReadVolume(address, out long volume, out long amount)
                    ? Success(new { success = true, volume = volume / 100.0, amount = amount / 100.0, rawVolume = volume, rawAmount = amount })
                    : Failure("No valid volume response.", StatusCodes.Status504GatewayTimeout);
            }), "ReadVolumeAndAmount", "Read volume and amount (0xD9).", "Reads and decodes two four-byte BCD values.");

            Describe(app.MapMethods("/cmd/b6_read_price", SupportedMethods, ([FromQuery(Name = "addr")] int? addr, MultiTrkManager manager) =>
            {
                if (!TryGetAddress(addr, out byte address)) return InvalidAddress();
                return manager.ReadPrice(address, out long price)
                    ? Success(new { success = true, price })
                    : Failure("No valid price response.", StatusCodes.Status504GatewayTimeout);
            }), "ReadPrice", "Read unit price (0xB6).", "Reads the three-byte BCD price.");

            Describe(app.MapMethods("/cmd/b2_set_price", SupportedMethods, ([FromQuery(Name = "addr")] int? addr, [FromQuery(Name = "price")] long? price, MultiTrkManager manager) =>
            {
                if (!TryGetAddress(addr, out byte address)) return InvalidAddress();
                if (price is < 0 or > 99999) return Failure("price must be an integer from 0 to 99999.", StatusCodes.Status400BadRequest);
                return ExecuteWrite(() => manager.SetPrice(address, price!.Value), "Price command was not acknowledged.", new { success = true, price });
            }), "SetPrice", "Set unit price (0xB2).", "Writes a three-byte BCD price; the documented maximum is 99999.");

            Describe(app.MapMethods("/cmd/b9_set_preset_volume", SupportedMethods, ([FromQuery(Name = "addr")] int? addr, [FromQuery(Name = "liters")] decimal? liters, MultiTrkManager manager) =>
            {
                if (!TryGetAddress(addr, out byte address)) return InvalidAddress();
                if (liters is null || liters < 0 || liters * 100m != decimal.Truncate(liters.Value * 100m))
                    return Failure("liters must be a non-negative number with at most two decimal places.", StatusCodes.Status400BadRequest);
                return ExecuteWrite(() => manager.SetPresetVolume(address, liters.Value), "Volume preset command was not acknowledged.", new { success = true, liters });
            }), "SetPresetVolume", "Set volume preset (0xB9).", "Sets a four-byte BCD volume in hundredths of a liter.");

            Describe(app.MapMethods("/cmd/b5_set_preset_amount", SupportedMethods, ([FromQuery(Name = "addr")] int? addr, [FromQuery(Name = "amount")] long? amount, MultiTrkManager manager) =>
            {
                if (!TryGetAddress(addr, out byte address)) return InvalidAddress();
                if (amount is < 0 or > 999999) return Failure("amount must be an integer from 0 to 999999.", StatusCodes.Status400BadRequest);
                return ExecuteWrite(() => manager.SetPresetAmount(address, amount!.Value), "Amount preset command was not acknowledged.", new { success = true, amount });
            }), "SetPresetAmount", "Set amount preset (0xB5).", "Sets a four-byte BCD amount scaled by 100.");

            Describe(app.MapMethods("/cmd/c3_start", SupportedMethods, ([FromQuery(Name = "addr")] int? addr, MultiTrkManager manager) =>
            {
                if (!TryGetAddress(addr, out byte address)) return InvalidAddress();
                return ExecuteWrite(() => manager.StartPump(address), "Start was rejected or timed out.", new { success = true }, StatusCodes.Status409Conflict);
            }), "StartPump", "Start dispensing (0xC3).", "Starts dispensing when the preset is accepted by the device.");

            Describe(app.MapMethods("/cmd/ca_stop", SupportedMethods, ([FromQuery(Name = "addr")] int? addr, MultiTrkManager manager) =>
            {
                if (!TryGetAddress(addr, out byte address)) return InvalidAddress();
                return ExecuteWrite(() => manager.StopPump(address), "Stop was not acknowledged.", new { success = true });
            }), "StopPump", "Stop dispensing (0xCA).", "Sends the stop command to the selected dispenser.");

            Describe(app.MapMethods("/cmd/ba_pause", SupportedMethods, ([FromQuery(Name = "addr")] int? addr, MultiTrkManager manager) =>
            {
                if (!TryGetAddress(addr, out byte address)) return InvalidAddress();
                return ExecuteWrite(() => manager.PausePump(address), "Pause was not acknowledged.", new { success = true });
            }), "PausePump", "Pause dispensing (0xBA).", "Pauses the current dispensing operation.");

            Describe(app.MapMethods("/cmd/b3_resume", SupportedMethods, ([FromQuery(Name = "addr")] int? addr, MultiTrkManager manager) =>
            {
                if (!TryGetAddress(addr, out byte address)) return InvalidAddress();
                return ExecuteWrite(() => manager.ResumePump(address), "Resume was not acknowledged.", new { success = true });
            }), "ResumePump", "Resume dispensing (0xB3).", "Resumes a paused dispensing operation.");

            Describe(app.MapMethods("/cmd/c5_read_total_counters", SupportedMethods, ([FromQuery(Name = "addr")] int? addr, MultiTrkManager manager) => ReadCounters(addr, 0xC5, manager)),
                "ReadTotalCounters", "Read total counters (0xC5).", "Reads six-byte BCD volume and amount totals.");
            Describe(app.MapMethods("/cmd/c7_read_shift_counters", SupportedMethods, ([FromQuery(Name = "addr")] int? addr, MultiTrkManager manager) => ReadCounters(addr, 0xC7, manager)),
                "ReadShiftCounters", "Read shift counters (0xC7).", "Reads six-byte BCD volume and amount for the current shift.");

            Describe(app.MapMethods("/cmd/ea_clear_shift_counters", SupportedMethods, ([FromQuery(Name = "addr")] int? addr, MultiTrkManager manager) =>
            {
                if (!TryGetAddress(addr, out byte address)) return InvalidAddress();
                return ExecuteWrite(() => manager.ClearShiftCounters(address), "Counter clear was not acknowledged.", new { success = true });
            }), "ClearShiftCounters", "Clear shift counters (0xEA).", "Clears the shift counters on the selected dispenser.");

            Describe(app.MapMethods("/cmd/a1_select_nozzle", SupportedMethods, ([FromQuery(Name = "addr")] int? addr, [FromQuery(Name = "nozzle")] int? nozzle, MultiTrkManager manager) =>
            {
                if (!TryGetAddress(addr, out byte address)) return InvalidAddress();
                if (!TryGetAddress(nozzle, out byte nozzleAddress)) return Failure("nozzle must be an address from 1 to 16.", StatusCodes.Status400BadRequest);
                return ExecuteWrite(() => manager.SelectNozzle(address, nozzleAddress), "Nozzle selection was rejected or timed out.", new { success = true, nozzle = nozzleAddress }, StatusCodes.Status409Conflict);
            }), "SelectNozzle", "Select a nozzle (0xA1).", "Sends the selected hose address as the one-byte request payload.");

            Describe(app.MapMethods("/cmd/a7_read_preset", SupportedMethods, ([FromQuery(Name = "addr")] int? addr, MultiTrkManager manager) =>
            {
                if (!TryGetAddress(addr, out byte address)) return InvalidAddress();
                byte[]? preset = manager.ReadPreset(address);
                if (preset == null || !BlueSkyProtocol.TryFromBCD(preset, 1, 4, out long presetValue))
                    return Failure("No valid preset response.", StatusCodes.Status504GatewayTimeout);
                return Success(new { success = true, presetType = preset[0], value = presetValue, rawValue = presetValue / 100.0 });
            }), "ReadPreset", "Read preset (0xA7).", "Returns the one-byte preset type and four-byte BCD value.");

            Describe(app.MapMethods("/cmd/a8_read_card_id", SupportedMethods, ([FromQuery(Name = "addr")] int? addr, MultiTrkManager manager) =>
            {
                if (!TryGetAddress(addr, out byte address)) return InvalidAddress();
                byte[]? cardId = manager.ReadCardId(address);
                return cardId != null
                    ? Success(new { success = true, cardId = Convert.ToHexString(cardId) })
                    : Failure("No valid card ID response.", StatusCodes.Status504GatewayTimeout);
            }), "ReadCardId", "Read card ID (0xA8).", "Returns the four-byte card identifier as hexadecimal.");

            Describe(app.MapMethods("/cmd/a9_read_error", SupportedMethods, ([FromQuery(Name = "addr")] int? addr, MultiTrkManager manager) =>
            {
                if (!TryGetAddress(addr, out byte address)) return InvalidAddress();
                return manager.ReadErrorCode(address, out byte errorCode)
                    ? Success(new { success = true, errorCode })
                    : Failure("No valid error-code response.", StatusCodes.Status504GatewayTimeout);
            }), "ReadErrorCode", "Read error code (0xA9).", "Returns the one-byte device error code; zero indicates no error.");

            Describe(app.MapMethods("/cmd/ab_clear_error", SupportedMethods, ([FromQuery(Name = "addr")] int? addr, MultiTrkManager manager) =>
            {
                if (!TryGetAddress(addr, out byte address)) return InvalidAddress();
                return ExecuteWrite(() => manager.ClearErrorFlag(address), "Error flag clear was rejected or timed out.", new { success = true }, StatusCodes.Status409Conflict);
            }), "ClearErrorFlag", "Clear the error flag (0xAB).", "Clears the dispenser error flag and checks its status acknowledgment.");

            Describe(app.MapMethods("/cmd/d7_read_device_id", SupportedMethods, ([FromQuery(Name = "addr")] int? addr, MultiTrkManager manager) =>
            {
                if (!TryGetAddress(addr, out byte address)) return InvalidAddress();
                byte[]? deviceId = manager.ReadDeviceId(address);
                return deviceId != null
                    ? Success(new { success = true, deviceId = Convert.ToHexString(deviceId) })
                    : Failure("No valid device ID response.", StatusCodes.Status504GatewayTimeout);
            }), "ReadDeviceId", "Read device ID (0xD7).", "Returns the four-byte device identifier as hexadecimal.");

            Describe(app.MapMethods("/cmd/aa_clear_preset_flag", SupportedMethods, ([FromQuery(Name = "addr")] int? addr, MultiTrkManager manager) =>
            {
                if (!TryGetAddress(addr, out byte address)) return InvalidAddress();
                return ExecuteWrite(() => manager.ClearPresetFlag(address), "Preset flag clear was not acknowledged.", new { success = true });
            }), "ClearPresetFlag", "Clear keyboard preset flag (0xAA).", "Clears the preset-ready flag set from the dispenser keypad.");

            Describe(app.MapMethods("/cmd/d6_read_solenoid", SupportedMethods, ([FromQuery(Name = "addr")] int? addr, MultiTrkManager manager) =>
            {
                if (!TryGetAddress(addr, out byte address)) return InvalidAddress();
                byte[]? solenoid = manager.ReadSolenoid(address);
                return solenoid != null
                    ? Success(new { success = true, data = Convert.ToHexString(solenoid), firstByte = solenoid[0], secondByte = solenoid[1] })
                    : Failure("No valid solenoid response.", StatusCodes.Status504GatewayTimeout);
            }), "ReadSolenoid", "Read solenoid state (0xD6).", "Returns both response bytes for the solenoid valve state.");

            Describe(app.MapMethods("/cmd/d2_set_solenoid", SupportedMethods, ([FromQuery(Name = "addr")] int? addr, [FromQuery(Name = "state")] int? state, MultiTrkManager manager) =>
            {
                if (!TryGetAddress(addr, out byte address)) return InvalidAddress();
                if (state is < 0 or > byte.MaxValue) return Failure("state must be a byte from 0 to 255.", StatusCodes.Status400BadRequest);
                return ExecuteWrite(() => manager.SetSolenoid(address, (byte)state!.Value), "Solenoid write was rejected or timed out.", new { success = true, state }, StatusCodes.Status409Conflict);
            }), "SetSolenoid", "Write solenoid state (0xD2).", "Writes one state byte and checks the status acknowledgment.");
        }

        private static IResult ReadCounters(int? candidateAddress, byte command, MultiTrkManager manager)
        {
            if (!TryGetAddress(candidateAddress, out byte address)) return InvalidAddress();
            if (!manager.ReadCounters(address, command, out long volume, out long amount))
                return Failure("No valid counter response.", StatusCodes.Status504GatewayTimeout);
            return Success(new { success = true, volume = volume / 100.0, amount = amount / 100.0, rawVolume = volume, rawAmount = amount });
        }

        private static object ToStatusResponse(TrkDevice device)
        {
            return new
            {
                address = device.Address,
                isConnected = device.IsConnected,
                stateByte = device.CurrentStateByte,
                isNozzleOff = device.IsNozzleOff,
                isNozzleHanged = device.IsNozzleHanged,
                isFilling = device.IsFilling,
                isPaused = device.IsPaused,
                isRemoteControl = device.IsRemoteControl,
                isPresetReady = device.IsPresetReady,
                hasError = device.HasError,
                currentVolume = device.CurrentVolume / 100.0,
                currentAmount = device.CurrentAmount / 100.0,
                price = device.CurrentPrice
            };
        }

        private static bool TryGetAddress(int? value, out byte address)
        {
            if (value is >= 1 and <= 16)
            {
                address = (byte)value.Value;
                return true;
            }

            address = 0;
            return false;
        }

        private static IResult InvalidAddress()
        {
            return Failure("addr must be a device address from 1 to 16.", StatusCodes.Status400BadRequest);
        }

        private static IResult ExecuteWrite(Func<bool> operation, string failureMessage, object successBody, int failureStatus = StatusCodes.Status504GatewayTimeout)
        {
            try
            {
                return operation() ? Success(successBody) : Failure(failureMessage, failureStatus);
            }
            catch (ArgumentOutOfRangeException ex)
            {
                return Failure(ex.Message, StatusCodes.Status400BadRequest);
            }
            catch (OverflowException)
            {
                return Failure("Value exceeds the protocol field capacity.", StatusCodes.Status400BadRequest);
            }
        }

        private static IResult Success(object body)
        {
            return Results.Json(body);
        }

        private static IResult Failure(string error, int statusCode)
        {
            return Results.Json(new { success = false, error }, statusCode: statusCode);
        }

        private static RouteHandlerBuilder Describe(RouteHandlerBuilder endpoint, string name, string summary, string description)
        {
            return endpoint
                .WithName(name)
                .WithTags("TRK")
                .WithSummary(summary)
                .WithDescription(description)
                .Produces(StatusCodes.Status200OK)
                .Produces(StatusCodes.Status400BadRequest)
                .Produces(StatusCodes.Status504GatewayTimeout);
        }

        private const string SwaggerUiHtml = """
            <!doctype html>
            <html lang="en">
            <head>
              <meta charset="utf-8" />
              <meta name="viewport" content="width=device-width, initial-scale=1" />
              <title>FuelServerPro API</title>
              <link rel="stylesheet" href="https://cdn.jsdelivr.net/npm/swagger-ui-dist@5/swagger-ui.css" />
              <style>body{margin:0}#swagger-ui{max-width:1400px;margin:0 auto}</style>
            </head>
            <body>
              <div id="swagger-ui"></div>
              <script src="https://cdn.jsdelivr.net/npm/swagger-ui-dist@5/swagger-ui-bundle.js"></script>
              <script>window.onload=()=>SwaggerUIBundle({url:'/openapi/v1.json',dom_id:'#swagger-ui',deepLinking:true});</script>
            </body>
            </html>
            """;
    }
}
