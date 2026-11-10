# PulseMouse

<p align="center">
  <img src="docs/assets/pulsemouse-logo-1024.png" width="128" alt="PulseMouse logo: a mint dot and dash on a dark rounded tile">
</p>

**Mouse-first shortcuts for Windows.** Hold the right mouse button, tap a short rhythm with the left button, and release to run a frequent action—without moving the pointer or opening a menu.

[简体中文](README.zh-CN.md) · [Architecture](docs/ARCHITECTURE.md) · [Release checklist](docs/RELEASE_CHECKLIST.md) · [Changelog](CHANGELOG.md)

> **Status — v0.9.0 pre-release.** The application builds and its Core smoke tests pass. Global mouse-hook behavior, injected input, target-window activation, and display scaling still require the manual checks listed in the release checklist before a stable release.

## Why PulseMouse?

Launchers and menus are great when you need to discover an action. PulseMouse is for the small set of actions you already know by heart: copy, screenshot, switch window, open a tool, or send a shortcut. Its rhythm input is designed to keep your attention and cursor where they already are.

![PulseMouse dashboard showing live rhythm input, action bindings, and input modes](docs/assets/screenshots/01-dashboard.jpg)

## Screenshots

| Command library | Direction gestures |
| --- | --- |
| ![The command library maps dot and dash rhythms to actions](docs/assets/screenshots/02-commands.jpg) | ![The direction-gesture editor maps middle-button directions to actions](docs/assets/screenshots/03-gestures.jpg) |
| **Workflow builder** | **Settings and safety controls** |
| ![The workflow builder shows confirmation-first multi-step automation](docs/assets/screenshots/04-workflows.jpg) | ![Settings show Context Pulse, on-screen feedback, and backup controls](docs/assets/screenshots/05-settings.jpg) |

All screenshots above are captured from the running `v0.9.0` application. The [concept cover](docs/assets/pulsemouse-cover.png) is retained as brand artwork only; it is not a product UI screenshot.

## Highlights

- **Fast chord input** — hold right-click, tap `.` (short press) or `-` (long press) with left-click, then release to execute immediately.
- **Context Pulse** — give the same rhythm a different meaning in Chrome, Explorer, Excel, or another app. Global mappings remain the fallback; pinned mappings stay the same everywhere.
- **Safe native fallback** — ordinary right-clicks and right-dragging are replayed; excluded, fullscreen, and elevated targets are passed through when appropriate.
- **Useful actions** — launch an application or folder, open an HTTPS URL, send a shortcut, run a confirmed workflow, or repeat the last successful action.
- **Feedback without focus theft** — an on-screen capsule, reachable-rhythm hints, a state-aware tray icon, diagnostics, and local usage statistics.
- **Local by design** — settings and statistics stay in `%LOCALAPPDATA%\PulseMouse\settings.json`; there is no account or telemetry.

## Quick start

1. Start PulseMouse and confirm the dashboard reports that the hook is installed.
2. Create three to eight mappings you use every day. Keep early rhythms short.
3. In the default automatic mode, use a side button when one is detected; otherwise hold right-click and tap with left-click.
4. Release the trigger button to execute. Scroll during a right-click chord to cancel it.

### Rhythm rules

| Input | Meaning |
| --- | --- |
| Quick press, up to 300 ms | `.` dot |
| Hold, 500–1600 ms | `-` dash |
| 300–500 ms, below 35 ms, or over 1600 ms | rejected |

A mapping contains up to five pulses. In right-click chord mode, releasing right-click commits the sequence immediately. Side-button and classic middle-button modes commit after a 750 ms pause.

## Context Pulse

PulseMouse records the window under the pointer when the trigger starts. Resolution is deterministic:

1. A pinned global mapping wins.
2. An app-specific mapping wins when context switching is enabled.
3. Otherwise the global mapping runs.

The desktop and taskbar intentionally use global mappings only. Shortcut actions can activate the captured target window before sending input; if that window is gone or cannot be activated, the action is not sent.

## Supported actions

| Action | Notes |
| --- | --- |
| Application | `.exe`, `.lnk`, or a command name; environment variables are expanded. |
| Folder | Opens an existing folder. |
| URL | HTTP and HTTPS only. |
| Shortcut | Examples: `Ctrl+C`, `Alt+Tab`, `Shift+Win+S`. |
| Workflow | 1–12 application, folder, or URL steps; confirmation is on by default. |
| Repeat last action | Replays the most recent successful non-workflow action. |
| Direction gesture | Hold middle-click and drag up, down, left, or right. |

## Build from source

Requirements: Windows 10/11 and the .NET 10 SDK.

```powershell
dotnet restore PulseMouse.slnx
dotnet build PulseMouse.slnx -c Release --no-restore
dotnet run --project tests/PulseMouse.Core.SmokeTests -c Release --no-build
```

For a self-contained x64 build, run `./build-windows.ps1`. It writes output to `publish/win-x64/`, which is intentionally excluded from Git.

## Safety and limitations

- PulseMouse uses a global mouse hook and `SendInput`. Unsigned builds may trigger security warnings.
- Do not use it with games or anti-cheat-protected software.
- A non-elevated instance cannot reliably inject input into elevated applications.
- Touchpads generally cannot perform the right-click + left-click chord comfortably.
- This pre-release has no installer, code signature, or automatic updater yet.

See [SECURITY.md](SECURITY.md) for responsible disclosure guidance and [docs/RELEASE_CHECKLIST.md](docs/RELEASE_CHECKLIST.md) for manual validation requirements.

## Documentation

- [Architecture](docs/ARCHITECTURE.md)
- [Development guide](docs/DEVELOPMENT.md)
- [Product roadmap](docs/PRODUCT_ROADMAP.md)
- [v0.9 implementation specification](docs/V0_9_CONTEXT_PULSE_SPEC.md)
- [Changelog](CHANGELOG.md)

## Contributing

Issues and pull requests are welcome. Please read [CONTRIBUTING.md](CONTRIBUTING.md) first.

## License

Released under the [MIT License](LICENSE).
