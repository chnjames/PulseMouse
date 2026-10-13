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
    private readonly HookProc _callback;
    private nint _handle;
    private readonly MiddleActivation _middle = new();
    private bool _leftIsDown;
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
    public string ActiveInput => Mode == MouseInputMode.Auto
        ? (_autoSide == 0 ? "自动：侧键优先 / 中键备用" : $"自动：X{_autoSide}（已观察到）")
        : Mode.ToString();
    public event Action<MouseButtonEvent>? RawButtonEvent;
    public event Action<bool, long>? ButtonEvent;
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
            CheckTimeout(ticks);
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
                    if (Enabled && Mode == MouseInputMode.Auto && _autoSide == 0 && !_middle.Armed && down)
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
                bool eligible = Enabled && (Mode == MouseInputMode.Middle || (Mode == MouseInputMode.Auto && _autoSide == 0));
                long ms = ToMs(ticks);
                if (down)
                {
                    _middle.Down(ms, Marshal.ReadInt32(lParam, 0), Marshal.ReadInt32(lParam, 4));
                    _gestureReady = eligible && GesturesEnabled;
                    _gesture.Configure(GestureMinDistancePx);
                    if (_gestureReady) _gesture.Start(Marshal.ReadInt32(lParam, 0), Marshal.ReadInt32(lParam, 4));
                }
                else
                {
                    string? direction = _gestureReady ? _gesture.End(Marshal.ReadInt32(lParam, 0), Marshal.ReadInt32(lParam, 4)) : null;
                    _gestureReady = false;
                    bool activated = _middle.Up(ms);
                    if (eligible && direction is not null)
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
                bool consume = Enabled && _middle.Armed && (Mode == MouseInputMode.Middle || (Mode == MouseInputMode.Auto && _autoSide == 0));
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
    }
    public void CancelInput()
    {
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
    [DllImport("user32.dll", SetLastError = true)] private static extern nint SetWindowsHookEx(int idHook, HookProc callback, nint module, uint threadId);
    [DllImport("user32.dll")] private static extern nint CallNextHookEx(nint hook, int nCode, nint wParam, nint lParam);
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
}
