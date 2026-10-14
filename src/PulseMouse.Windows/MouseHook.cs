using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using PulseMouse.Core;

namespace PulseMouse.Windows;

public readonly record struct MouseButtonEvent(string Button, bool IsDown, long Timestamp, bool Consumed, string Note);

/// <summary>
/// Runs the low-level hook on its own thread so a busy UI never delays system input (Windows silently drops
/// hooks that keep timing out). Events are raised on that thread; subscribers must only enqueue work.
/// </summary>
public sealed class MouseHook : IDisposable
{
    private const int WhMouseLl = 14;
    private const uint WmTimer = 0x0113, WmQuit = 0x0012;
    private const int WmLDown = 0x0201, WmLUp = 0x0202, WmMove = 0x0200;
    private const int WmMDown = 0x0207, WmMUp = 0x0208;
    private const int WmXDown = 0x020B, WmXUp = 0x020C;
    private const int WmRDown = 0x0204, WmRUp = 0x0205, WmWheel = 0x020A, WmHWheel = 0x020E;
    private const uint LlmhfInjected = 0x01;
    private const uint MouseEventRightDown = 0x0008, MouseEventRightUp = 0x0010;
    private static readonly nuint InjectionMarker = 0x50554C53;
    private readonly HookProc _callback;
    private readonly object _gate = new();
    private readonly TargetInspector _inspector = new();
    private Thread? _thread;
    private uint _threadId;
    private volatile nint _handle;
    private bool _sideBypassed, _middleBypassed;
    private readonly MiddleActivation _middle = new();
    private readonly ChordActivation _chord = new();
    private bool _leftIsDown;
    private bool _physicalLeftDown;
    private long _lastReplayMs = long.MinValue / 2;
    /// <summary>When our last injected event was sent and has not come back through the hook yet.</summary>
    private long? _awaitingEchoMs;
    private bool _injectionBlocked;
    /// <summary>Something (a remote-control tool's input lock, a sandbox) discards simulated input; replays and shortcuts cannot work.</summary>
    public bool InjectionBlocked => _injectionBlocked;
    private readonly GestureRecognizer _gesture = new();
    private bool _gestureReady;
    public bool GesturesEnabled { get; set; } = true;
    public int GestureMinDistancePx { get; set; } = 75;
    public event Action<string>? GestureEvent;
    private int _autoSide; // 0: unobserved, 1: X1, 2: X2
    /// <summary>Side button Auto mode settled on (0: none yet); persisted so detection survives restarts.</summary>
    public int AutoSide { get => _autoSide; set { lock (_gate) _autoSide = value is 1 or 2 ? value : 0; } }
    public event Action<int>? SideDetected;
    public bool PauseInFullscreen { get => _inspector.PauseInFullscreen; set => _inspector.PauseInFullscreen = value; }
    public void SetExcludedApps(IEnumerable<string> names) { lock (_gate) _inspector.SetExcluded(names); }
    public static string NormalizeAppName(string name) => TargetInspector.Normalize(name);
    public bool Enabled { get; set; } = true;
    public MouseInputMode Mode { get; set; } = MouseInputMode.Auto;
    public int MiddleHoldMs { get => _middle.MinimumHoldMs; set => _middle.MinimumHoldMs = Math.Clamp(value, 300, 2000); }
    public bool MiddleArmed => _middle.Armed;
    public bool IsInstalled => _handle != 0;
    public string ActiveInput => Mode switch
    {
        MouseInputMode.Auto => _autoSide == 0 ? "自动：右键 + 左键和弦（按一下侧键即改用侧键）" : $"自动：X{_autoSide} 侧键（已检测到）",
        MouseInputMode.Chord => "右键 + 左键 和弦",
        _ => Mode.ToString()
    };
    /// <summary>Right+left chord is the rhythm input: chosen explicitly, or by Auto until a side button is seen.</summary>
    public bool ChordActive => Mode == MouseInputMode.Chord || (Mode == MouseInputMode.Auto && _autoSide == 0);
    public event Action<MouseButtonEvent>? RawButtonEvent;
    public event Action<bool, long>? ButtonEvent;
    /// <summary>
    /// Chord lifecycle: <see cref="ChordStep.Swallow"/> (trigger held back), <see cref="ChordStep.Begin"/>, <see cref="ChordStep.Commit"/>,
    /// <see cref="ChordStep.Cancel"/>, or <see cref="ChordStep.ReplayDown"/>/<see cref="ChordStep.ReplayClick"/> (handed back to the app).
    /// </summary>
    public event Action<ChordStep, long>? ChordEvent;
    public event Action<string>? StateChanged;
    public MouseHook() => _callback = HookCallback;

    public void Install()
    {
        if (_thread is not null) return;
        int error = 0;
        using var ready = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            _threadId = GetCurrentThreadId();
            _handle = SetWindowsHookEx(WhMouseLl, _callback, GetModuleHandle(null), 0);
            if (_handle == 0) error = Marshal.GetLastWin32Error();
            ready.Set();
            if (_handle == 0) return;
            // Enforces hold/arm timeouts even while the mouse is idle.
            nuint timer = SetTimer(0, 0, 40, 0);
            lock (_gate) Probe();
            while (GetMessage(out var msg, 0, 0, 0) > 0)
            {
                if (msg.Message == WmTimer) lock (_gate) CheckTimeout(Stopwatch.GetTimestamp());
            }
            KillTimer(0, timer);
            UnhookWindowsHookEx(_handle);
            _handle = 0;
        })
        { IsBackground = true, Priority = ThreadPriority.Highest, Name = "PulseMouse hook" };
        thread.Start();
        ready.Wait();
        if (_handle == 0)
        {
            thread.Join();
            throw new Win32Exception(error, "安装 WH_MOUSE_LL 失败");
        }
        _thread = thread;
    }

    public void ResetInput()
    {
        lock (_gate)
        {
            _chord.Reset();
            _middle.Disarm();
            _gesture.Cancel(); _gestureReady = false;
            _leftIsDown = false;
        }
    }

    private nint HookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode < 0) return CallNextHookEx(_handle, nCode, wParam, lParam);
        long ticks = Stopwatch.GetTimestamp();
        lock (_gate)
        try
        {
            var info = Marshal.PtrToStructure<MsLlHookStruct>(lParam);
            if (info.ExtraInfo == InjectionMarker)
            {
                _awaitingEchoMs = null;
                if (_injectionBlocked)
                {
                    _injectionBlocked = false;
                    StateChanged?.Invoke("模拟输入已恢复，右键和弦重新启用");
                }
                return CallNextHookEx(_handle, nCode, wParam, lParam);
            }
            bool injected = (info.Flags & LlmhfInjected) != 0;
            // Remote-control tools (ToDesk tags 0x77, RDP…) deliver the user's real clicks as injected input. A tagged
            // right press right after our own replay is another hook re-sending it; swallowing that again would loop.
            bool echo = injected && info.ExtraInfo != 0 && wParam == WmRDown && ToMs(ticks) - _lastReplayMs < 150;
            bool userInput = !echo;
            if (userInput && wParam == WmLDown) _physicalLeftDown = true;
            else if (userInput && wParam == WmLUp) _physicalLeftDown = false;

            CheckTimeout(ticks);
            if (userInput && HandleChord((int)wParam, ticks, info, injected)) return 1;
            if (echo && Enabled && ChordActive)
                RawButtonEvent?.Invoke(new("右键", true, ticks, false, $"其他程序紧接着补发的右键（标记 0x{info.ExtraInfo:X}），放行以免循环"));
            if (wParam == WmMove && _middle.IsPressingMiddle)
            {
                _middle.Move(Marshal.ReadInt32(lParam, 0), Marshal.ReadInt32(lParam, 4));
            }
            if (wParam == WmXDown || wParam == WmXUp)
            {
                uint mouseData = unchecked((uint)Marshal.ReadInt32(lParam, 8));
                int button = (int)((mouseData >> 16) & 0xffff);
                if (button is 1 or 2)
                {
                    bool down = wParam == WmXDown;
                    if (Enabled && Mode == MouseInputMode.Auto && _autoSide == 0 && _chord.IsIdle && down)
                    {
                        _autoSide = button;
                        _middle.Disarm();
                        SideDetected?.Invoke(button);
                        StateChanged?.Invoke($"已检测到侧键 X{button}，自动启用侧键模式");
                    }
                    bool selected = Mode switch
                    {
                        MouseInputMode.Auto => _autoSide == button,
                        MouseInputMode.X1 => button == 1,
                        MouseInputMode.X2 => button == 2,
                        _ => false
                    };
                    string? bypass = null;
                    if (Enabled && selected && down)
                    {
                        bypass = _inspector.Bypass(info.X, info.Y, replays: false);
                        _sideBypassed = bypass is not null;
                    }
                    bool consume = Enabled && selected && !_sideBypassed;
                    RawButtonEvent?.Invoke(new($"X{button}", down, ticks, consume, consume ? "节奏输入" : bypass is null ? "透传" : "透传：" + bypass));
                    if (consume)
                    {
                        ButtonEvent?.Invoke(down, ticks);
                        return 1;
                    }
                }
            }
            else if (wParam == WmMDown || wParam == WmMUp)
            {
                bool down = wParam == WmMDown;
                string? bypass = null;
                if (down)
                {
                    bypass = Enabled && (Mode == MouseInputMode.Middle || ChordActive) ? _inspector.Bypass(info.X, info.Y, replays: false) : null;
                    _middleBypassed = bypass is not null;
                }
                bool eligible = Enabled && Mode == MouseInputMode.Middle && !_middleBypassed;
                bool gestureEligible = eligible || (Enabled && ChordActive && !_middleBypassed);
                long ms = ToMs(ticks);
                if (down)
                {
                    _middle.Down(ms, Marshal.ReadInt32(lParam, 0), Marshal.ReadInt32(lParam, 4));
                    _gestureReady = gestureEligible && GesturesEnabled;
                    _gesture.Configure(GestureMinDistancePx);
                    if (_gestureReady) _gesture.Start(Marshal.ReadInt32(lParam, 0), Marshal.ReadInt32(lParam, 4));
                }
                else
                {
                    string? direction = _gestureReady ? _gesture.End(Marshal.ReadInt32(lParam, 0), Marshal.ReadInt32(lParam, 4)) : null;
                    _gestureReady = false;
                    bool activated = _middle.Up(ms);
                    if (gestureEligible && direction is not null)
                    {
                        _middle.Disarm();
                        GestureEvent?.Invoke(direction);
                    }
                    if (eligible && activated && direction is null)
                        StateChanged?.Invoke($"中键长按激活：7 秒内用左键输入节奏（鼠标移动超过 {_middle.MovementTolerancePx}px 不激活）");
                    else if (!eligible) _middle.Disarm();
                    RawButtonEvent?.Invoke(new("中键", false, ticks, false, activated && eligible ? "激活成功，原始事件透传" : "未激活，原始事件透传"));
                }
                if (down) RawButtonEvent?.Invoke(new("中键", true, ticks, false, bypass is null ? "原始事件透传" : "透传，不识别手势：" + bypass));
            }
            else if (wParam == WmLDown || wParam == WmLUp)
            {
                bool down = wParam == WmLDown;
                bool consume = Enabled && _middle.Armed && Mode == MouseInputMode.Middle;
                if (consume)
                {
                    if (down) _leftIsDown = true;
                    else
                    {
                        _leftIsDown = false;
                        _middle.Extend(ToMs(ticks), 2000); // time for next pulse and sequence completion
                    }
                    RawButtonEvent?.Invoke(new("左键", down, ticks, true, "中键模式节奏输入"));
                    ButtonEvent?.Invoke(down, ticks);
                    return 1;
                }
            }
        }
        catch { /* Don't break the system hook when observers fail. */ }
        return CallNextHookEx(_handle, nCode, wParam, lParam);
    }

    private static long ToMs(long ticks) => ticks * 1000 / Stopwatch.Frequency;
    private void CheckTimeout(long ticks)
    {
        if (_awaitingEchoMs is long sent && ToMs(ticks) - sent > 300)
        {
            _awaitingEchoMs = null;
            if (!_injectionBlocked)
            {
                _injectionBlocked = true;
                StateChanged?.Invoke("模拟输入被系统拦截：PulseMouse 补发的点击没有生效，右键和弦已暂停，右键恢复原生，快捷键动作也无法发送。常见原因：PulseMouse 由受限环境（如开发工具的终端）启动，或远程控制工具屏蔽了键鼠；请退出后直接双击启动");
            }
        }
        if (_middle.Expire(ToMs(ticks), _leftIsDown)) StateChanged?.Invoke("中键指令窗口超时，恢复普通鼠标操作");
        switch (_chord.Tick(ToMs(ticks)))
        {
            case ChordStep.ReplayDown:
                ReplayPressAtOrigin();
                ChordNote(ticks, $"按住超过 {_chord.HoldTimeoutMs} ms 未按左键，补发右键按下并放行");
                ChordEvent?.Invoke(ChordStep.ReplayDown, ticks);
                break;
            case ChordStep.Cancel:
                ChordNote(ticks, "和弦超时，已放弃本次输入");
                ChordEvent?.Invoke(ChordStep.Cancel, ticks);
                break;
        }
    }

    private void ChordNote(long ticks, string note) => RawButtonEvent?.Invoke(new("右键", true, ticks, false, note));

    /// <summary>Right button held + left button taps; returns true when the event must be swallowed.</summary>
    private bool HandleChord(int message, long ticks, in MsLlHookStruct info, bool injected)
    {
        bool chordMode = Enabled && ChordActive;
        if (!chordMode && _chord.IsIdle) return false;
        if (message == WmRDown && _chord.IsIdle && _inspector.Bypass(info.X, info.Y, replays: true) is { } bypass)
        {
            RawButtonEvent?.Invoke(new("右键", true, ticks, false, "透传：" + bypass));
            return false;
        }
        if (message == WmRDown && _chord.IsIdle && _injectionBlocked)
        {
            // Holding the press back would lose it; let it through and check whether injection works again.
            Probe();
            RawButtonEvent?.Invoke(new("右键", true, ticks, false, "透传：模拟输入被拦截，无法补发右键"));
            return false;
        }
        var step = message switch
        {
            WmRDown => chordMode ? _chord.TriggerDown(ToMs(ticks), info.X, info.Y, _physicalLeftDown) : ChordStep.Pass,
            WmRUp => _chord.TriggerUp(),
            WmLDown => _chord.LeftDown(),
            WmLUp => _chord.LeftUp(),
            WmMove => _chord.Move(info.X, info.Y),
            WmWheel or WmHWheel => _chord.Wheel(),
            _ => ChordStep.Pass
        };
        if (message == WmRDown && step == ChordStep.Swallow)
        {
            RawButtonEvent?.Invoke(new("右键", true, ticks, true, injected ? $"和弦：等待左键节奏（模拟输入，标记 0x{info.ExtraInfo:X}）" : "和弦：等待左键节奏"));
            ChordEvent?.Invoke(ChordStep.Swallow, ticks);
        }
        switch (step)
        {
            case ChordStep.Pass:
                return false;
            case ChordStep.ReplayDown:
                long dx = info.X - _chord.OriginX, dy = info.Y - _chord.OriginY;
                if (message == WmMove) ReplayPressAtOrigin(info.X, info.Y); else ReplayPressAtOrigin();
                ChordNote(ticks, message == WmMove
                    ? $"按住时移动 {Math.Sqrt(dx * dx + dy * dy):0} px（阈值 {_chord.MovementTolerancePx}），按拖拽处理，补发右键按下并放行"
                    : "按住时滚动滚轮，补发右键按下并放行");
                ChordEvent?.Invoke(ChordStep.ReplayDown, ticks);
                return false;
            case ChordStep.ReplayClick:
                Inject(MouseEventRightDown, MouseEventRightUp);
                RawButtonEvent?.Invoke(new("右键", false, ticks, false, "未输入节奏，补发右键单击"));
                ChordEvent?.Invoke(ChordStep.ReplayClick, ticks);
                return true;
            case ChordStep.Begin:
                ChordEvent?.Invoke(ChordStep.Begin, ticks);
                RawButtonEvent?.Invoke(new("左键", true, ticks, true, "和弦节奏输入（按住右键）"));
                ButtonEvent?.Invoke(true, ticks);
                return true;
            case ChordStep.PulseDown:
            case ChordStep.PulseUp:
            case ChordStep.PulseUpCommit:
                bool down = step == ChordStep.PulseDown;
                RawButtonEvent?.Invoke(new("左键", down, ticks, true, "和弦节奏输入（按住右键）"));
                ButtonEvent?.Invoke(down, ticks);
                if (step == ChordStep.PulseUpCommit) ChordEvent?.Invoke(ChordStep.Commit, ticks);
                return true;
            case ChordStep.Commit:
                RawButtonEvent?.Invoke(new("右键", false, ticks, true, "和弦：松开右键，提交节奏"));
                ChordEvent?.Invoke(step, ticks);
                return true;
            case ChordStep.Cancel:
                RawButtonEvent?.Invoke(new("滚轮", true, ticks, true, "和弦：滚动滚轮，取消本次输入"));
                ChordEvent?.Invoke(step, ticks);
                return true;
            default:
                return true;
        }
    }

    /// <summary>A 1 px nudge and back, tagged as ours (zero-distance moves may be dropped); only used on demand so it never keeps the PC from idling.</summary>
    private void Probe()
    {
        _lastReplayMs = ToMs(Stopwatch.GetTimestamp());
        _awaitingEchoMs ??= _lastReplayMs;
        var inputs = new[]
        {
            new Input { Type = 0, Mouse = new MouseInput { Dx = 1, Flags = 0x0001, ExtraInfo = InjectionMarker } },
            new Input { Type = 0, Mouse = new MouseInput { Dx = -1, Flags = 0x0001, ExtraInfo = InjectionMarker } },
        };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
    }

    private void Inject(params uint[] flags)
    {
        _lastReplayMs = ToMs(Stopwatch.GetTimestamp());
        _awaitingEchoMs ??= _lastReplayMs;
        var inputs = new Input[flags.Length];
        for (int i = 0; i < flags.Length; i++)
            inputs[i] = new Input { Type = 0, Mouse = new MouseInput { Flags = flags[i], ExtraInfo = InjectionMarker } };
        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
    }

    /// <summary>Replays the held right press where it happened, then returns the cursor to (x, y) or its current position.</summary>
    private void ReplayPressAtOrigin(int? x = null, int? y = null)
    {
        // Hook coordinates are physical pixels; measure the desktop the same way regardless of process DPI awareness.
        _lastReplayMs = ToMs(Stopwatch.GetTimestamp());
        _awaitingEchoMs ??= _lastReplayMs;
        nint previous = SetThreadDpiAwarenessContext(-4);
        try
        {
            GetCursorPos(out var now);
            var inputs = new[]
            {
                AbsoluteMove(_chord.OriginX, _chord.OriginY),
                new Input { Type = 0, Mouse = new MouseInput { Flags = MouseEventRightDown, ExtraInfo = InjectionMarker } },
                AbsoluteMove(x ?? now.X, y ?? now.Y),
            };
            SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<Input>());
        }
        finally
        {
            if (previous != 0) SetThreadDpiAwarenessContext(previous);
        }
    }

    private static Input AbsoluteMove(int x, int y)
    {
        const uint move = 0x0001, virtualDesk = 0x4000, absolute = 0x8000;
        int left = GetSystemMetrics(76), top = GetSystemMetrics(77);
        int width = Math.Max(2, GetSystemMetrics(78)), height = Math.Max(2, GetSystemMetrics(79));
        return new Input
        {
            Type = 0,
            Mouse = new MouseInput
            {
                Dx = (int)Math.Round((x - left) * 65535.0 / (width - 1)),
                Dy = (int)Math.Round((y - top) * 65535.0 / (height - 1)),
                Flags = move | absolute | virtualDesk,
                ExtraInfo = InjectionMarker
            }
        };
    }

    public void CancelInput()
    {
        lock (_gate)
        {
            _chord.Cancel();
            _gesture.Cancel(); _gestureReady = false;
            _middle.Disarm(); _leftIsDown = false;
        }
        StateChanged?.Invoke("已取消本次输入");
    }
    public void FinishSequence()
    {
        bool finished;
        lock (_gate)
        {
            finished = _middle.Armed && !_leftIsDown;
            if (finished) _middle.Disarm();
        }
        if (finished) StateChanged?.Invoke("本次中键节奏输入已结束");
    }
    public void Dispose()
    {
        if (_thread is { } thread)
        {
            PostThreadMessage(_threadId, WmQuit, 0, 0);
            thread.Join(2000);
            _thread = null;
        }
        GC.SuppressFinalize(this);
    }
    private delegate nint HookProc(int nCode, nint wParam, nint lParam);
    [StructLayout(LayoutKind.Sequential)]
    private struct MsLlHookStruct { public int X, Y; public uint MouseData, Flags, Time; public nuint ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput { public int Dx, Dy; public uint MouseData, Flags, Time; public nuint ExtraInfo; }
    [StructLayout(LayoutKind.Sequential)]
    private struct Input { public uint Type; public MouseInput Mouse; }
    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, Input[] inputs, int size);
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern nint SetThreadDpiAwarenessContext(nint context);
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookEx(int idHook, HookProc callback, nint module, uint threadId);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int nCode, nint wParam, nint lParam);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [StructLayout(LayoutKind.Sequential)] private struct Msg { public nint Window; public uint Message; public nint WParam, LParam; public uint Time; public Point Point; }
    [DllImport("user32.dll")] private static extern int GetMessage(out Msg msg, nint window, uint min, uint max);
    [DllImport("user32.dll")] private static extern bool PostThreadMessage(uint threadId, uint message, nint wParam, nint lParam);
    [DllImport("user32.dll")] private static extern nuint SetTimer(nint window, nuint id, uint elapseMs, nint callback);
    [DllImport("user32.dll")] private static extern bool KillTimer(nint window, nuint id);
}
