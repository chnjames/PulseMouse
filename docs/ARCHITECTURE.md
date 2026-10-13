# Architecture

XBUTTON2 -> Win32 low-level hook (WPF UI thread with message loop) -> `Dispatcher.BeginInvoke` -> `RhythmEngine` -> `ActionRunner` (background task).

Important: the hook callback only reads event metadata and dispatches. It never writes disk, waits for AI, launches processes, or paints UI. Mouse input is suppressed only for XBUTTON2 while enabled, by returning a nonzero value. The recognizer is deterministic and uses monotonic timestamps converted to milliseconds. Ambiguous durations cause resets. All completed sequences are matched exactly and unknown commands are no-ops.

Settings: `%LOCALAPPDATA%\PulseMouse\settings.json`.

**Limits:** current MVP handles only XBUTTON2; requires an accessible mouse side button; no remote execution; no promise of anti-cheat / elevated-process compatibility. Avoid turning arbitrary AI instructions into shell commands.
