# PulseMouse v0.3 Windows release gate

> Status: source candidate, not a verified production release.

## Build

```powershell
dotnet restore PulseMouse.slnx
dotnet build PulseMouse.slnx -c Release
dotnet run --project tests/PulseMouse.Core.SmokeTests
dotnet run --project src/PulseMouse.App
```

## Manual tests (required)

- [ ] v0.1.2 and v0.1.3 configuration migrates without loss
- [ ] middle click in Chrome still works normally when not invoking a gesture
- [ ] stationary middle hold arms rhythm input; double tap launches notepad
- [ ] middle pressed + 100 px right + release opens configured notepad
- [ ] middle pressed + 100 px upward + release opens explorer
- [ ] unintended diagonal movement does not trigger commands
- [ ] gesture disabled causes no gesture execution
- [ ] X1 / X2 side-button modes still work when those buttons exist
- [ ] all hook failures, input cancellation and pause recover safely
- [ ] workflow confirmation denies unauthorized execution
- [ ] malformed workflow JSON rejected and no process started
- [ ] overlay does not steal focus; no click interception
- [ ] settings survive restart and tray exit unhooks global mouse
- [ ] perform 30-minute run with CPU, memory, crash diagnostics

## Known boundaries

- Mouse middle events are forwarded to foreground application, so browser autoscroll may activate. Production input suppression needs separate design and cross-app testing.
- Auto mode first observed X1/X2 can lock subsequent auto mode; manual reset/re-selection may be necessary.
- Current gesture classifier accepts only a single dominant cardinal direction, not multi-stroke paths.
- Workflow commands execute local application launches/URLs/folders; do not import untrusted JSON.
- AI button opens a configured HTTPS browser page only. No Agent or computer control integration is implemented.
- No autoupdater, signing, packaging or telemetry shipped.
- .NET SDK unavailable in authoring runtime: Windows compilation and UI smoke tests are unverified.
