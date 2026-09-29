# AGENTS.md

This repository is a small .NET console service that exposes a local HTTP API for communicating with TRK fuel devices over a serial port.

## Project shape

- `Program.cs`: starts the ASP.NET Core Kestrel API on `http://localhost:8088/` and wires OpenAPI and CORS.
- `ApiEndpoints.cs`: maps the HTTP endpoints and documents them in the OpenAPI schema.
- `MultiTrkManager.cs`: manages the serial port, scans for devices, polls status, and sends raw commands.
- `BlueSkyProtocol.cs`: protocol helpers for frame building, CRC calculation, and BCD conversion.
- `TrkDevice.cs`: per-device state model and bitmask-based status flags.
- `Logger.cs`: logging entry point used across the app.

## Core conventions

- Target framework is `net10.0` and the app is a console executable.
- Serial settings are intentionally fixed: `9600`, `Parity.Even`, `8` data bits, `StopBits.One`, with `DtrEnable` and `RtsEnable` enabled.
- Device addresses are `byte` values in the range `1..16`.
- All protocol packets should go through `BlueSkyProtocol` helpers instead of hand-built byte arrays.
- Keep protocol logic conservative: validate framing, CRC, and response length before trusting a serial reply.
- Sequential serial access is protected by `_portLock` in `MultiTrkManager`; avoid introducing parallel port operations that would race with reads/writes.

## HTTP API behavior

`Program.cs` exposes request endpoints such as:

- `/connect?port=COM6`
- `/scan`
- `/status`
- `/status_all`
- `/cmd/b9_set_preset_volume?addr=1&liters=...`
- `/cmd/b5_set_preset_amount?addr=1&amount=...`
- `/cmd/c3_start?addr=1`
- `/cmd/ca_stop?addr=1`

The HTTP API also exposes these documented protocol commands (all command routes require `addr=1..16`):

- `/cmd/d5_status`, `/cmd/d9_read_volume`
- `/cmd/b6_read_price`, `/cmd/b2_set_price?price=...`
- `/cmd/b5_set_preset_amount?amount=...`, `/cmd/b9_set_preset_volume?liters=...`
- `/cmd/c3_start`, `/cmd/ca_stop`, `/cmd/ba_pause`, `/cmd/b3_resume`
- `/cmd/c5_read_total_counters`, `/cmd/c7_read_shift_counters`, `/cmd/ea_clear_shift_counters`
- `/cmd/a1_select_nozzle?nozzle=...`, `/cmd/a7_read_preset`, `/cmd/a8_read_card_id`
- `/cmd/a9_read_error`, `/cmd/ab_clear_error`, `/cmd/d7_read_device_id`, `/cmd/aa_clear_preset_flag`
- `/cmd/d6_read_solenoid`, `/cmd/d2_set_solenoid?state=...`
- `/disconnect`

Command reads verify the response address, command, frame length, CRC, and documented payload length. Write endpoints report success only for the documented acknowledgment type. Numeric query values use invariant decimal notation; preset amount and volume are scaled by `100` before BCD encoding.

When editing request handling, preserve the `addr` query parameter pattern and keep JSON responses consistent with the current API contract.

Swagger UI is available at `http://localhost:8088/swagger`; the OpenAPI document is at `/openapi/v1.json`. Set `FUEL_SERVER_URL` to override the default listen URL.

## State and protocol rules

- `TrkDevice.CurrentStateByte` is a bitmask; its derived properties in `TrkDevice.cs` define nozzle and fill-state behavior.
- Status polling is command `0xD5`; volume payload reads use command `0xD9`.
- Do not change the frame format or CRC behavior without checking the device protocol expectations.
- Prefer small, protocol-aware fixes over broad refactors.

## Build and validation

This project has no dedicated test suite. Use the normal .NET build flow:

```bash
dotnet build
```

For execution and manual verification:

```bash
dotnet run
```

## Common pitfalls

- Serial reads are short and timing-sensitive; do not add long blocking waits inside the port lock.
- Device responses may be absent or partial; the code intentionally ignores communication faults to keep the service alive.
- BCD conversions and numeric scaling are easy to get wrong; changes to volume/amount handling should preserve the existing `* 100` scaling model.
- `Stop()` clears device state and closes the port; maintain that behavior if you change connection lifecycle logic.
