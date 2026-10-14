# PulseMouse v0.4.2 - CS0029 编译错误修复

修复位置：`src/PulseMouse.App/MainWindow.xaml.cs` 的 `WindowMaximize` 方法。

错误：将 `WindowState.Maximized` 枚举值作为三元条件（bool）。

修复：使用 `this.WindowState == System.Windows.WindowState.Maximized` 比较当前窗口状态，并在 Normal/Maximized 间切换。

## Windows 验证

```powershell
dotnet build PulseMouse.slnx -c Release
dotnet run --project tests/PulseMouse.Core.SmokeTests -c Release
dotnet run --project src/PulseMouse.App -c Release
```

本修复未在 Windows/.NET SDK 环境编译验证。
