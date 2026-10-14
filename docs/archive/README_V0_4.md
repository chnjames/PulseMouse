# PulseMouse v0.4 UI + Pulse Capsule

This package is an incremental update to v0.3.1. It retains global mouse hooks, rhythm engine, gesture engine, action runner, and saved JSON settings.

**What changed:** dark dropdown/grid styling, a custom Windows title bar, bottom-center capsule in response to activation/typing/completion, and exe/lnk/folder selection within command mapping.

**Run on Windows with .NET 10 SDK:**

```powershell
dotnet build PulseMouse.slnx -c Release
dotnet run --project tests/PulseMouse.Core.SmokeTests -c Release
dotnet run --project src/PulseMouse.App -c Release
```

**Testing:** See `docs/V0_4_ACCEPTANCE.md`.

**Status:** source candidate; not built on Windows in the delivery environment. Do not treat this package as a verified release build. Main UI remains a scrollable dashboard; expanded application discovery, full navigation, animations, AI agent, and comprehensive scrolling profiler work are planned for subsequent changes.
