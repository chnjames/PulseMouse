# PulseMouse Changelog

## v0.2.0 — Minimal Fluent UI
- Reworked WPF dashboard with side navigation visual, adaptive input card, command management, and diagnostic log.
- Added 5-segment live rhythm pulse display (short/long bars).
- Preserved the v0.1.3 input hook, input state machine, action runner, settings JSON structure, and command handling.
- Retained existing side button and middle button input modes.

### Limitations and verification
- The sidebar is a visual hierarchy in this iteration, **not** a clickable multi-page navigation system.
- Pulse bars show recognized input sequence, not a high-frequency oscilloscope waveform.
- No new mouse gesture engine, AI integration, light mode, or installer is included.
- Windows build and integration tests must be performed on a Windows machine with .NET 10 SDK.

## Windows verification
```powershell
dotnet restore PulseMouse.slnx
dotnet build PulseMouse.slnx -c Release
dotnet run --project tests/PulseMouse.Core.SmokeTests
dotnet run --project src/PulseMouse.App
```
Verify X1/X2, middle-hold + left-click, mapping CRUD, test action, cancellation, system tray, and the pulse visualization. Close prior PulseMouse instance before launching.

## v0.3 (candidate)
- Real one-stroke mouse gestures using middle hold+movement; user-configurable mappings.
- Click-through feedback overlay, navigation, AI URL launcher and workflow JSON execution with confirmation.
- Added gesture recognition smoke tests.
- Windows build and real-world interaction validation PENDING.

## v0.4.0 (UI & Capsule candidate, 2026-10-13)
- Unified the WPF dark design system for combo items, DataGrid cells, headers and selection, and keyboard focus appearance.
- Replaced mismatched OS title bar with custom WindowChrome title bar including native window resizing and dragging.
- Added bottom-center, click-through Pulse Capsule for activation, pulse entry, matched commands, and unmatched sequences.
- Added Browse action for selecting .exe/.lnk applications and folders when editing commands.
- Enabled content scrolling/virtualization flags on command and gesture grids; preserved existing input and workflow code.
- Validation: XML parsing only in non-Windows environment; Windows build and runtime acceptance are still mandatory.

## v0.5 · UI redesign candidate
- 将一页长滚动改为真正分区的侧边导航。
- 调整颜色资源与内容层级，保留所有命令事件绑定。
- 未进行 Windows 原生编译，需真机验收。
