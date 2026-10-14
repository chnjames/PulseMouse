# PulseMouse v0.4.1 编译修复

修复 `MainWindow.xaml.cs` 中的 3 处 CS0104：`CommandBinding` 在 `PulseMouse.Core` 与 `System.Windows.Input` 中重名。使用 `using CommandBinding = PulseMouse.Core.CommandBinding;` 显式指定业务模型。未更改鼠标输入、命令执行或现有配置。

## Windows 验证

```powershell
dotnet build PulseMouse.slnx -c Release
dotnet run --project tests/PulseMouse.Core.SmokeTests -c Release
dotnet run --project src/PulseMouse.App -c Release
```

此环境未安装 Windows/.NET SDK，故未实机编译。
