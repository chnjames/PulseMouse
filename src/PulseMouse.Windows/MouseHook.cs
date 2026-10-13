using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using PulseMouse.Core;

namespace PulseMouse.Windows;

public readonly record struct MouseButtonEvent(string Button, bool IsDown, long Timestamp, bool Consumed, string Note);

/// <summary>Installs on WPF UI thread; callbacks enqueue work and avoid blocking the hook.</summary>
public sealed class MouseHook : IDisposable
{
    private const int WhMouseLl = 14;
    private const int WmLDown = 0x0201, WmLUp = 0x0202, WmMove = 0x0200;
    private const int WmMDown = 0x0207, WmMUp = 0x0208;
    private const int WmXDown = 0x020B, WmXUp = 0x020C;
    private const int WmRDown = 0x0204, WmRUp = 0x0205, WmWheel = 0x020A, WmHWheel = 0x020E;
    private const uint LlmhfInjected = 0x01;
    private const uint MouseEventRightDown = 0x0008, MouseEventRightUp = 0x0010;
    private static readonly nuint InjectionMarker = 0x50554C53;
    private readonly HookProc _callback;
    private nint _handle;
    private readonly MiddleActivation _middle = new();
    private readonly ChordActivation _chord = new();
    private bool _leftIsDown;
    private bool _physicalLeftDown;
    private long _lastReplayMs = long.MinValue / 2;
    private readonly GestureRecognizer _gesture = new();
    private bool _gestureReady;
    public bool GesturesEnabled { get; set; } = true;
    public int GestureMinDistancePx { get; set; } = 75;
    public event Action<string>? GestureEvent;
    private int _autoSide; // 0: unobserved, 1: X1, 2: X2
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
    /// <summary>Chord lifecycle: <see cref="ChordStep.Begin"/>, <see cref="ChordStep.Commit"/> or <see cref="ChordStep.Cancel"/>.</summary>
    public event Action<ChordStep, long>? ChordEvent;
    public event Action<string>? StateChanged;
    public MouseHook() => _callback = HookCallback;

    public void Install()
    {
        if (_handle != 0) return;
        _handle = SetWindowsHookEx(WhMouseLl, _callback, GetModuleHandle(null), 0);
        if (_handle == 0) throw new Win32Exception(Marshal.GetLastWin32Error(), "安装 WH_MOUSE_LL 失败");
    }

    public void ResetInput()
    {
        _chord.Reset();
        _middle.Disarm();
        _gesture.Cancel(); _gestureReady = false;
        _leftIsDown = false;
        _autoSide = 0;
    }

    private nint HookCallback(int nCode, nint wParam, nint lParam)
    {
        if (nCode < 0) return CallNextHookEx(_handle, nCode, wParam, lParam);
        long ticks = Stopwatch.GetTimestamp();
        try
        {
            var info = Marshal.PtrToStructure<MsLlHookStruct>(lParam);
            if (info.ExtraInfo == InjectionMarker) return CallNextHookEx(_handle, nCode, wParam, lParam);
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
                        StateChanged?.Invoke($"已检测到侧键 X{button}，自动启用侧键模式");
                    }
                    bool selected = Mode switch
                    {
                        MouseInputMode.Auto => _autoSide == button,
                        MouseInputMode.X1 => button == 1,
                        MouseInputMode.X2 => button == 2,
                        _ => false
                    };
                    bool consume = Enabled && selected;
                    RawButtonEvent?.Invoke(new($"X{button}", down, ticks, consume, consume ? "节奏输入" : "透传"));
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
                bool eligible = Enabled && Mode == MouseInputMode.Middle;
                bool gestureEligible = eligible || (Enabled && ChordActive);
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
                if (down) RawButtonEvent?.Invoke(new("中键", true, ticks, false, "原始事件透传"));
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
    /// <summary>Called by a UI timer so arm expiry is enforced even when the mouse is idle.</summary>
    public void CheckTimeout(long ticks)
    {
        if (_middle.Expire(ToMs(ticks), _leftIsDown)) StateChanged?.Invoke("中键指令窗口超时，恢复普通鼠标操作");
        switch (_chord.Tick(ToMs(ticks)))
        {
            case ChordStep.ReplayDown:
                ReplayPressAtOrigin();
                ChordNote(ticks, $"按住超过 {_chord.HoldTimeoutMs} ms 未按左键，补发右键按下并放行");
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
            RawButtonEvent?.Invoke(new("右键", true, ticks, true, injected ? $"和弦：等待左键节奏（模拟输入，标记 0x{info.ExtraInfo:X}）" : "和弦：等待左键节奏"));
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
                return false;
            case ChordStep.ReplayClick:
                Inject(MouseEventRightDown, MouseEventRightUp);
                RawButtonEvent?.Invoke(new("右键", false, ticks, false, "未输入节奏，补发右键单击"));
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

    private void Inject(params uint[] flags)
    {
        _lastReplayMs = ToMs(Stopwatch.GetTimestamp());
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
        _chord.Cancel();
        _gesture.Cancel(); _gestureReady = false;
        _middle.Disarm(); _leftIsDown = false;
        StateChanged?.Invoke("已取消本次输入");
    }
    public void FinishSequence()
    {
        if (_middle.Armed && !_leftIsDown)
        {
            _middle.Disarm();
            StateChanged?.Invoke("本次中键节奏输入已结束");
        }
    }
    public void Dispose()
    {
        if (_handle != 0) { UnhookWindowsHookEx(_handle); _handle = 0; }
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
}
