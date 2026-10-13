# PulseMouse v0.5 UI 重构说明

## 设计来源（借鉴原则，非复制）
- Raycast：强调单一任务和简洁层次；
- PowerToys Command Palette：清晰分组、用户可发现的操作；
- Flow Launcher：键鼠效率、克制的视觉密度；
- Quicker：可配置指令与动作管理。

## 已实施
- 自定义标题栏与统一色彩；
- 侧边栏真分区切换，避免全部页面同时滚动；
- 指令表格增高、清晰标题和显式测试入口；
- 统一导航按钮、表格及暗色组件规范；
- 保留原有 Win32 Hook、节奏引擎、动作执行、配置存储和胶囊行为。

## 待 Windows 真机验证
- dotnet build、SmokeTests、启动与 XAML 运行时资源解析；
- 弹出 ComboBox 的所有状态是否文字清晰；
- 中键透传、拖动/缩放/多显示器；
- 弹窗层级、托盘恢复与触发反馈；
- 高 DPI 与键盘 Focus 可达性。

## 设计风险
系统组合框的弹出部分仍有系统级主题差异；若 Windows 上未能呈现深色，需要额外重写 ComboBox ControlTemplate，而不是只依赖 ComboBoxItem 样式。

## 验收命令
```powershell
dotnet restore PulseMouse.slnx
dotnet build PulseMouse.slnx -c Release
dotnet run --project tests/PulseMouse.Core.SmokeTests -c Release
dotnet run --project src/PulseMouse.App -c Release
```
