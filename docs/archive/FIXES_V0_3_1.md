# PulseMouse v0.3.1 — Windows 编译错误修正

基于 v0.3 完整工程，仅针对用户实测的八项 C# 编译错误做最小补丁。

- MainWindow.xaml.cs: 消除 Brush、Color、Clipboard 引用冲突；补充 System.IO 以使用 Directory。
- FeedbackWindow.cs: 消除 Brushes、Color 引用冲突；使用 System.Windows.HorizontalAlignment.Center。
- 没有修改鼠标监听或节奏识别业务逻辑、现有 JSON 配置。

## 验证

在 Windows 的工程根目录：

```powershell
dotnet build PulseMouse.slnx -c Release
dotnet run --project tests/PulseMouse.Core.SmokeTests -c Release
dotnet run --project src/PulseMouse.App -c Release
```

当前执行环境没有 Windows/.NET SDK，无法本地实际编译；八项原报错已逐项针对性修改，尚待 Windows 验证。
