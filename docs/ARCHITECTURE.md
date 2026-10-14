# 架构

## 项目划分

| 项目 | 职责 | 依赖 |
|---|---|---|
| `PulseMouse.Core` | 纯逻辑、可单测：节奏识别 `RhythmEngine`、右键和弦状态机 `ChordActivation`、中键激活 `MiddleActivation`、方向手势 `GestureRecognizer`、快捷键解析 `KeyChord`、犹豫提示 `RhythmHints`、配置模型 `Models` | 无 |
| `PulseMouse.Windows` | 全局鼠标钩子 `MouseHook`、目标窗口检查 `TargetInspector` | Core |
| `PulseMouse.Actions` | 执行动作 `ActionRunner`、发送快捷键 `KeySender` | Core |
| `PulseMouse.Infrastructure` | 配置读写 `SettingsStore` | Core |
| `PulseMouse.App` | WPF 界面、托盘、状态胶囊 `FeedbackWindow`、总控 `PulseController` | 以上全部 |
| `tests/PulseMouse.Core.SmokeTests` | Core 的冒烟测试（控制台程序，失败即非零退出） | Core |
| `tools/IconGen` | 生成 `Assets/PulseMouse.ico` 与预览图，复用 `AppIcon.cs` | — |

## 线程

- **钩子线程**：`MouseHook` 在独立线程（最高优先级）安装 `WH_MOUSE_LL` 并运行消息循环，另有 40 ms 定时器处理超时。界面卡顿不会拖慢系统鼠标；回调超时的钩子会被 Windows 静默移除，因此回调里只做判断和投递，从不写盘、启动进程或绘制界面。钩子状态由一把锁保护。
- **界面线程**：`PulseController` 订阅钩子事件，一律 `Dispatcher.BeginInvoke` 投递到界面线程处理；节奏识别、统计、胶囊和日志都在这里。
- **后台任务**：动作执行（启动进程、发送快捷键、工作流延迟）在后台任务中进行。

## 输入处理

```
WH_MOUSE_LL 回调
  ├─ 自己补发的事件（ExtraInfo = 0x50554C53）：直接放行，并用于确认模拟输入可用
  ├─ 右键和弦：ChordActivation 决定吞掉、补发或放行 → ChordEvent / ButtonEvent
  ├─ 侧键 X1/X2：吞掉选中的侧键 → ButtonEvent
  ├─ 中键：MiddleActivation 判断长按激活；GestureRecognizer 判断方向手势 → GestureEvent
  └─ 中键激活后的左键：吞掉 → ButtonEvent
        ↓ Dispatcher
PulseController → RhythmEngine（按下/抬起时长 → 点/划；停顿或松开右键时提交）
        ↓ 匹配指令
ActionRunner（后台） → 统计、胶囊、日志
```

### 右键和弦

右键按下时先吞掉，由 `ChordActivation` 根据后续事件决定：

| 情况 | 处理 |
|---|---|
| 按下左键 | 进入和弦，左键按下/抬起作为节奏脉冲 |
| 松开右键（已有节奏） | 立即提交，不等待停顿 |
| 松开右键（没按左键） | 在原位置补发一次完整右键单击 |
| 移动超过 16 px | 在原位置补发右键按下，之后放行（右键拖拽） |
| 按住 8 s 未按左键 | 补发右键按下并放行 |
| 和弦中滚动滚轮 | 取消本次输入，吞掉剩余事件直到松开右键 |
| 超过 10 s 未见右键抬起 | 安全兜底，放弃本次输入 |

补发使用 `SendInput`，并带上自己的 `ExtraInfo` 标记，回调据此识别并放行，避免循环。其他程序注入的输入（如远程控制工具，ToDesk 标记为 `0x77`）按用户输入处理；但若在我们补发后 150 ms 内出现带外来标记的右键按下，视为回声并放行，防止与其他钩子工具来回补发。

### 模拟输入被拦截的检测

每次补发后等待自己的事件回到钩子。300 ms 内没有回来，即判定模拟输入被系统丢弃（远程控制工具屏蔽键鼠、受限的进程环境等）：右键和弦暂停、右键恢复原生，快捷键动作拒绝执行。暂停期间每次按右键会发送一次“右移 1 px 再移回”的探测，事件一回来即自动恢复。

### 放行规则（`TargetInspector`）

右键按下、中键按下、侧键按下时检查光标下的窗口：

- 进程在排除列表中（按进程名，不区分大小写，忽略 `.exe`）；
- 开启“全屏时暂停”且前台窗口全屏（桌面与任务栏除外）；
- 仅右键和弦：目标进程以管理员权限运行而 PulseMouse 没有（UIPI 会静默丢弃补发的输入，无法查询的进程按管理员处理）。

命中任一条即整次按压放行。进程信息缓存 30 秒。

### 自动模式

未检测到侧键时使用右键和弦；首次按下侧键即改用该侧键，并写入配置 `DetectedSideButton`，重启后保持。设置页可“重新检测”。

## 节奏识别（`RhythmEngine`）

- 按压时长 ≤ `ShortMaxMs`（300）为点，`LongMinMs`–`LongMaxMs`（500–1600）为划；其余（含 < 35 ms）作废并重置。
- 达到 `MaxPulses`（5）立即提交；侧键与中键模式停顿 `SequenceGapMs`（750）后提交；右键和弦关闭停顿提交，由松开右键提交。
- 完全匹配才执行；未绑定的节奏只计为未匹配，不执行任何动作。

## 动作（`ActionRunner`）

- 程序：展开环境变量后 `ShellExecute`。
- 文件夹：必须存在。
- 网址：仅 http / https。
- 快捷键：`KeyChord` 解析，`KeySender` 按序发送修饰键与主键（`SendInput`，扫描码 + 扩展键标志），发送不完整即报错。
- 工作流：JSON 步骤 1–12 个，延迟 0–5000 ms，不允许嵌套；默认执行前弹窗确认（`ConfirmWorkflows`，可在设置中关闭）。

## 配置与统计

- `%LOCALAPPDATA%\PulseMouse\settings.json`，`SchemaVersion` 当前为 8。写入先写 `.tmp` 再替换，避免写一半损坏；界面修改通过 `SaveSoon` 合并保存。
- 旧配置缺失的字段取模型默认值；导入配置立即应用，无需重启。
- 统计按日滚动：跨日时把前一天的执行、未匹配和各指令次数写入 `History`（最多 90 天）。

## 界面

- `MainWindow`：自绘标题栏、五个页面（总览、指令、手势、工作流、设置）、托盘（按状态显示薄荷绿 / 灰 / 琥珀色图标）。
- `FeedbackWindow`：不抢焦点、可点击穿透的状态胶囊，上方可显示犹豫提示卡片。
- 图标由 `AppIcon` 用代码绘制：应用图标为深色底，托盘为浅色底以便用颜色表示状态。
