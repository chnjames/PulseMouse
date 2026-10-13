# PulseMouse v0.4 Windows acceptance checklist

## Build
```powershell
dotnet restore PulseMouse.slnx
dotnet build PulseMouse.slnx -c Release
dotnet run --project tests/PulseMouse.Core.SmokeTests -c Release
dotnet run --project src/PulseMouse.App -c Release
```

## Appearance
- Custom dark title bar matches content; drag, double-click maximize and edge resize function correctly.
- Buttons do not display default dotted focus; keyboard focus remains observable where appropriate.
- Dropdown items, selected items, grid headers and grid rows show readable contrasting text.
- Check dark theme at 100%, 125%, 150% Windows scaling.
- Long lists and scrolling do not freeze input handling.

## Pulse Capsule
- With main window hidden in tray: hold middle button to activate, verify capsule appears above taskbar.
- Enter `..`, watch `·`, `··`, then execution confirmation; verify Notepad opens.
- Enter an unmapped sequence, verify failure message.
- With side button mode selected, observe rhythm feedback for X1/X2.
- Verify capsule never steals focus and click-through works.
- Verify foreground screen / secondary monitor positioning (multi-display + mixed-DPI must be checked).
- Verify no overlay interference in full-screen games or screen-sharing applications; suppression is **not implemented yet**.
- Check middle button native event passthrough remains a known limitation (browser auto-scroll may fire).

## Command picker
- Select Application in the command editor and Browse; choose exe or lnk and save.
- Select Folder and Browse; choose a directory and save.
- Verify saved paths work after restarting.
- Workflow and URL remain text/JSON input; rich editor and Start Menu catalog are not built.

## Known limitations
- No automated Windows UI tests in this delivery.
- ComboBox uses platform template with explicit dark foreground and custom dropdown item style. Visual verification is required to catch theme-specific popups.
- The main view is still a scrolling dashboard, not a true multi-page navigation system.
- Capsule is a WPF window using WinForms monitor bounds and WPF DIP conversion; mixed-DPI placement needs validation.
- AI still opens a URL; there is no on-device or hosted agent integration yet.
