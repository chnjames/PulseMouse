# Development guide

## Prerequisites

- Windows 10 or Windows 11
- .NET 10 SDK

## Verify a change

```powershell
dotnet restore PulseMouse.slnx
dotnet build PulseMouse.slnx -c Release --no-restore
dotnet run --project tests/PulseMouse.Core.SmokeTests -c Release --no-build
```

`build-windows.ps1` additionally creates a self-contained, single-file x64 build under `publish/win-x64/`.

## Project layout

| Path | Purpose |
| --- | --- |
| `src/PulseMouse.Core` | Deterministic input, context-resolution, validation, migration, and model logic. |
| `src/PulseMouse.Windows` | Windows hooks, target inspection, context capture, activation, and app discovery. |
| `src/PulseMouse.Actions` | Action execution and shortcut injection. |
| `src/PulseMouse.Infrastructure` | Atomic settings persistence. |
| `src/PulseMouse.App` | WPF shell, tray, feedback UI, localization, and orchestration. |
| `tests/PulseMouse.Core.SmokeTests` | Fast executable smoke tests for Core behavior and migrations. |
| `tools/IconGen` | Regenerates the application icon and preview artwork. |

## Change boundaries

- Keep Core free of WPF and Win32 dependencies so it remains deterministic and testable.
- Treat global hooks and injected input as high-risk: make a small change, run smoke tests, then perform the relevant manual checks.
- Add new visible UI copy in Chinese and English through `Loc.cs`.
- Increment `AppSettings.CurrentSchemaVersion` and provide migration coverage whenever persistent schema changes.
- Keep release binaries out of Git. Attach them to a GitHub Release only after manual validation.
