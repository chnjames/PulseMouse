# Contributing to PulseMouse

Thanks for helping improve PulseMouse.

## Before opening a change

- Discuss substantial features in an issue first.
- Keep user-facing text available in Chinese and English through `Loc.cs`.
- Preserve native mouse behavior whenever a gesture or chord is not intentionally activated.
- Never add network telemetry, tracking, or cloud sync without an explicit product decision and documentation update.

## Local verification

```powershell
dotnet restore PulseMouse.slnx
dotnet build PulseMouse.slnx -c Release --no-restore
dotnet run --project tests/PulseMouse.Core.SmokeTests -c Release --no-build
```

Global hook behavior, injected input, target-window activation, scaling, and tray behavior also need the relevant checks in [docs/RELEASE_CHECKLIST.md](docs/RELEASE_CHECKLIST.md).

## Pull requests

Use a focused title, explain the user-visible change, and include screenshots for UI changes. Do not commit `bin/`, `obj/`, `publish/`, exported settings, or personal logs.
