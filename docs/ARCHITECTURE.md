# Architecture

PulseMouse is a Windows-only WPF desktop application. Its design keeps deterministic input rules separate from Win32 integration and UI effects so the riskiest behavior can be tested without a live global hook.

## Projects

| Project | Responsibility | Dependencies |
| --- | --- | --- |
| `PulseMouse.Core` | Rhythm recognition, chord and middle-button decisions, gestures, context resolution, pin policy, validation, templates, settings migration, and shared models. | None |
| `PulseMouse.Windows` | Global low-level mouse hook, target inspection, context snapshots, target-window activation, and application discovery. | Core |
| `PulseMouse.Actions` | Application, folder, URL, workflow, and shortcut execution. | Core |
| `PulseMouse.Infrastructure` | Atomic settings read/write. | Core |
| `PulseMouse.App` | WPF shell, tray, feedback capsule, localization, controller, and view models. | All projects above |
| `tests/PulseMouse.Core.SmokeTests` | Executable smoke tests for Core behavior and settings migration. | Core |
| `tools/IconGen` | Rebuilds the application icon and previews from `AppIcon.cs`. | App icon source |

## Execution flow

```text
WH_MOUSE_LL callback on a dedicated thread
  ├─ native/replayed input → pass through
  ├─ right-click chord / side button / middle decision → input events
  └─ capture ContextSnapshot under the pointer
             ↓ UI dispatcher
RhythmEngine → ContextResolver → ExecutionRequest
             ↓                     ↓
        reachable hints       CommandExecutor
                                   ↓
                  ActionRunner / KeySender / WindowActivator
                                   ↓
                            ExecutionResult
                                   ↓
                     ExecutionEffects → feedback, history, logs
```

The callback never starts processes, writes settings, or renders UI. It decides only whether the current mouse message is swallowed, replayed, or passed through, then posts a small event to the UI thread.

## Input modes

- **Right-click chord:** PulseMouse temporarily swallows right-click. If no left-click rhythm follows, it replays a normal right-click. Dragging, an 8-second hold, or an unsafe target releases the native behavior.
- **Side button:** The selected X1/X2 button is used as input and commits after a pause.
- **Middle button:** `MiddleDecision` delays the choice: a short stationary press is replayed as a native click, a drag becomes a directional gesture, and a stationary hold passes through for native auto-scroll/pan. Classic middle mode remains available.

Synthetic input carries a process-specific `ExtraInfo` marker so the hook can ignore its own replayed events. The application also detects when injected input is blocked and returns right-click to native behavior.

## Context Pulse

At trigger-down, `ContextTarget` captures the root window handle, process id, process name, and desktop state. The snapshot remains fixed for the sequence.

```text
Resolve(pattern, context)
  pinned global mapping → app-specific mapping → global mapping → miss
```

Desktop and taskbar contexts deliberately bypass application mappings. Before a shortcut is sent, `WindowActivator` verifies that the captured window still belongs to the same process and, when configured, activates it. A failed activation fails safely rather than sending input to an unintended foreground window.

## Configuration and migration

Settings are stored at `%LOCALAPPDATA%\PulseMouse\settings.json`. Writes use a temporary file and replacement to avoid partial files. Schema version 9 adds command identities, context scopes, pinning, context applications, reminder counts, and id-keyed statistics; `SettingsMigrator` upgrades older settings while preserving commands and historical counters.

No settings, diagnostics, or usage statistics are intentionally sent to a network service.

## Testing boundary

The smoke test exercises rhythm recognition, native-input decision state machines, gestures, parsing, context resolution, pinning, conflict validation, templates, migration, and execution-result errors. It cannot prove operating-system behavior such as injection, privilege boundaries, tray rendering, or multi-display layout; those live checks are in [RELEASE_CHECKLIST.md](RELEASE_CHECKLIST.md).
