# PulseMouse UI 2.0 — Neo Glass Black

## 视觉与结构

- 第二轮参考 Raycast、PowerToys 与 Windows Fluent 的层级策略，降低卡片和描边密度，收窄侧栏与底部状态区。
- 重做 WPF 滚动条：默认 3px 轻量指示器，悬停/拖动时扩展至 6px，无箭头、无高对比轨道。
- 重做 TextBox、ComboBox、CheckBox 控件模板，消除原生 WPF 控件与深色主题之间的割裂感。
- 新增集中式 `Themes/NeoGlass.xaml` 主题，统一颜色、字体、按钮、输入框、下拉框、列表与表格样式。
- 主窗口改为石墨黑基底、烟玻璃面板、薄荷青实时信号和冰蓝系统状态的 Neo Glass Black 视觉语言。
- 字体角色分离：Segoe UI Variable Display 用于标题，Segoe UI Variable Text 用于正文，Cascadia Mono/Consolas 用于节奏和诊断数据。
- 导航固定为 Dashboard、Commands、Gestures、Workflows、Settings 五个页面。
- Commands 改为节奏卡片列表，并保留创建、载入、编辑、删除、浏览目标和测试执行。
- Dashboard 集成监听状态、实时五段脉冲轨道、输入方式和最近事件。
- 新增独立 Workflows 引导页，保留 JSON 模板复制、12 步限制和执行前确认。
- 重绘窗口底部 Pulse Capsule 状态栏和桌面悬浮 Capsule。

## 功能兼容

- 未改动 `MouseHook`、`RhythmEngine`、`MiddleActivation`、`GestureRecognizer` 和 `ActionRunner` 的核心实现。
- 保留自动/X1/X2/中键输入模式、节奏匹配、方向手势、托盘、设置持久化、程序/目录/HTTPS/工作流执行。
- 页面切换只改变可见性，监听器、计时器和事件绑定保持存活。
- v0.5 配置模型和 JSON 字段保持兼容。

## 主要文件

- `src/PulseMouse.App/Themes/NeoGlass.xaml`
- `src/PulseMouse.App/MainWindow.xaml`
- `src/PulseMouse.App/MainWindow.xaml.cs`
- `src/PulseMouse.App/FeedbackWindow.cs`

