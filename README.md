# PulseMouse v0.7 — V3 Liquid Glass

本版本依据 `PulseMouse-V3.html` 设计稿重构仪表盘与设置体验，并实现统计、可视化节奏编辑、自启动、提示音、语言、胶囊位置/动画和配置备份。详见 `docs/V0_7_DESIGN_AND_IMPLEMENTATION.md` 与 `docs/TEST_REPORT_V0_7.md`。

基于已在用户 Windows 环境正常运行的 v0.1.2 更新，兼容其 JSON 配置和指令映射。

## 本版新增
- 中键长按的移动防误触：按住期间鼠标位移大于 12px 时，不激活左键指令窗口。
- 激活窗口 7 秒到期时，即使鼠标完全静止，也由 UI 计时器主动清理状态。
- 添加“取消当前输入”按钮，退出输入窗口并清空节奏序列。
- 增加中键激活状态的纯逻辑单元冒烟测试（短按、移动、激活、超时、取消）。

## 使用
```powershell
dotnet restore PulseMouse.slnx
dotnet build PulseMouse.slnx -c Release
dotnet run --project tests/PulseMouse.Core.SmokeTests
dotnet run --project src/PulseMouse.App
```

## 验证步骤
1. 中键短按，不进入输入模式。
2. 按住中键 650ms 同时拖动鼠标超过 12px，不能进入输入模式。
3. 中键保持位置按住 650ms 后松开，输入 `..`，等待 750ms，打开记事本。
4. 中键长按后不操作，7 秒后应自动退出输入模式；左键恢复正常。
5. 长按中键后按“取消当前输入”，左键立即恢复普通点击。
6. 测试 X1/X2 原始监听及手动侧键模式；原有 JSON 指令不被覆盖。

## 已知限制与安全说明
- 中键原始事件仍透传到其他软件，可能触发浏览器自动滚动等操作；这不是完整的中键无副作用方案。
- 自动模式仍可能在首次出现侧键事件时锁定侧键，可手动指定“中键”。
- 当前没有系统级 ESC 取消快捷键；失控时可切换托盘暂停或关闭程序。
- 浮层、AI 和方向手势仍未实现。
- 源码仅做静态检查：本生成环境无 Windows/.NET SDK，需在 Windows 真机编译与验证。

## v0.2 UI update
The v0.2 dashboard uses a WPF Minimal Fluent-inspired visual style and retains the v0.1.3 input engine. See `CHANGELOG.md` for exact scope, limitations, and Windows acceptance steps.


## v0.3 candidate features
- Navigable sections (scroll-to), editable cardinal mouse gestures, non-activating overlay, JSON workflow steps (requiring confirmation), AI HTTPS launcher.
- See `docs/RELEASE_CHECKLIST.md` for known limitations and manual acceptance tests.
- Do not claim production-ready until real Windows build/tests pass.
