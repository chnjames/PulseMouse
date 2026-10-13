# Remaining work to ship a stable release

## P0 — before release
1. Windows 10/11 build and real device verification using `build-windows.ps1`.
2. Solve **middle-click passthrough conflict** with browser auto-scroll using an opt-in input suppression strategy; do not globally swallow middle clicks without controlled rollback.
3. Hook callback should not run user actions or blocking operations. Current callbacks enqueue WPF work; review all event handlers.
4. Add explicit state-machine tests for combined gestures, cancellation, device changes and rapid clicks.
5. Validate multi-monitor DPI overlay placement, hot-plug mice, full-screen apps, UAC/admin windows.
6. Workflow allowlisting and cancellation; warn before running user-entered arbitrary executable paths.

## P1
- First-run onboarding and adaptive mouse detection walkthrough.
- Actual multi-page WPF navigation (v0.3 provides scroll-to-section navigation only).
- Import/export with schema version and conflict resolution; installer/signature.
- AI provider integration only after explicit consent and API key secure storage.

## What is and isn't implemented
- ✅ Real source implementations: Windows hook, timed pulses, four cardinal gestures, actions, JSON workflows, basic diagnostics, tray, overlay, AI website opener.
- ⚠️ Not verified here: Windows compile/runtime and integration correctness.
- ❌ Not implemented: native AI agent, screen understanding, advanced workflow editing, installer, gesture trajectories, update infrastructure, complete theme switching.
