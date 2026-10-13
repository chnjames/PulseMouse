# PulseMouse UI 2.0 测试报告

测试日期：2026-10-13  
环境：Windows，.NET SDK 10.0.401

## 自动验证

### Release 编译

- `PulseMouse.Core`：通过
- `PulseMouse.Windows`：通过
- `PulseMouse.Actions`：通过
- `PulseMouse.Infrastructure`：通过
- `PulseMouse.App`（WPF）：通过
- 结果：0 warnings，0 errors
- 第二轮 UI Polish 输出：`src/PulseMouse.App/bin/Release-ui-polish/PulseMouse.exe`，0 warnings，0 errors

### 核心烟雾测试

全部通过：

- 两个短脉冲映射
- 短/长脉冲识别
- 模糊时长拒绝
- 未绑定节奏安全空操作
- 序列间隔切分
- 中键短按不激活
- 中键拖动不激活
- 静止长按中键激活
- 输入窗口超时
- 左键按住时不超时
- 左键释放延长窗口
- 取消清理激活状态
- 上/右方向识别
- 小幅移动和对角线拒绝
- 手势取消

### 应用启动检查

- 通过 `dotnet PulseMouse.dll` 启动并持续运行超过 10 秒，未发生启动期异常。
- 当前自动化环境未进行视觉截图和人工控件遍历。

## 必须在真实桌面手工核实

- X1/X2 和中键 Hook 在具体鼠标驱动、管理员权限应用及游戏中的行为。
- 中键事件透传是否触发浏览器自动滚动等副作用。
- 多显示器、不同 DPI/缩放比例和任务栏位置下 Pulse Capsule 的精确位置。
- 真实目标程序、快捷方式、文件夹、HTTPS 和多步工作流的端到端启动结果。
- 托盘隐藏/恢复、系统休眠恢复、开机长时间常驻。
- Segoe UI Variable/Cascadia Mono 缺失系统上的字体回退效果。

以上项目未经当前自动化环境完整验证，不声明为已通过。

