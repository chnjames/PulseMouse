using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace PulseMouse.Windows;

/// <summary>Decides whether PulseMouse must leave a press alone, judged by the window under the cursor.</summary>
internal sealed class TargetInspector
{
    private readonly record struct ProcessInfo(string Name, bool Elevated, long CachedAtMs);

    private readonly Dictionary<uint, ProcessInfo> _cache = new();
    private readonly uint _ownPid = (uint)Environment.ProcessId;
    private readonly bool _selfElevated = IsElevated(GetCurrentProcess()) ?? false;
    private HashSet<string> _excluded = new(StringComparer.OrdinalIgnoreCase);

    public bool PauseInFullscreen { get; set; } = true;

    public void SetExcluded(IEnumerable<string> names) =>
        _excluded = new HashSet<string>(names.Select(Normalize).Where(n => n.Length > 0), StringComparer.OrdinalIgnoreCase);

    public static string Normalize(string name)
    {
        name = Path.GetFileName(name.Trim().Trim('"'));
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }

    /// <summary>Returns why the press at (x, y) must pass through untouched, or null to handle it.</summary>
    /// <param name="replays">The press is swallowed and re-sent later, which Windows blocks for elevated windows.</param>
    public string? Bypass(int x, int y, bool replays)
    {
        nint previous = SetThreadDpiAwarenessContext(-4);
        try
        {
            nint target = GetAncestor(WindowFromPoint(new Point { X = x, Y = y }), 2);
            uint pid = Pid(target);
            if (pid != 0 && pid != _ownPid)
            {
                var info = Lookup(pid);
                if (_excluded.Contains(info.Name)) return $"{info.Name} 在排除列表中";
                if (replays && info.Elevated && !_selfElevated) return $"{Label(info)} 以管理员权限运行，Windows 不允许补发右键";
            }
            if (PauseInFullscreen)
            {
                nint foreground = GetForegroundWindow();
                uint fgPid = Pid(foreground);
                if (fgPid != 0 && fgPid != _ownPid && IsFullscreen(foreground)) return $"{Label(Lookup(fgPid))} 正在全屏运行";
            }
            return null;
        }
        finally
        {
            if (previous != 0) SetThreadDpiAwarenessContext(previous);
        }
    }

    private static string Label(ProcessInfo info) => info.Name.Length > 0 ? info.Name : "该窗口";

    private static uint Pid(nint window)
    {
        if (window == 0) return 0;
        GetWindowThreadProcessId(window, out uint pid);
        return pid;
    }

    private ProcessInfo Lookup(uint pid)
    {
        long now = Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency;
        // Pids get reused, so cached answers expire.
        if (_cache.TryGetValue(pid, out var cached) && now - cached.CachedAtMs < 30_000) return cached;
        if (_cache.Count > 256) _cache.Clear();

        string name = "";
        bool elevated = true; // a process we cannot inspect is treated as higher-privileged
        nint process = OpenProcess(0x1000, false, pid); // PROCESS_QUERY_LIMITED_INFORMATION
        if (process != 0)
        {
            try
            {
                var buffer = new StringBuilder(1024);
                int size = buffer.Capacity;
                if (QueryFullProcessImageName(process, 0, buffer, ref size)) name = Normalize(buffer.ToString());
                elevated = IsElevated(process) ?? true;
            }
            finally { CloseHandle(process); }
        }
        var info = new ProcessInfo(name, elevated, now);
        _cache[pid] = info;
        return info;
    }

    private static bool? IsElevated(nint process)
    {
        if (!OpenProcessToken(process, 0x0008, out nint token)) return null; // TOKEN_QUERY
        try
        {
            return GetTokenInformation(token, 20, out int elevation, sizeof(int), out _) ? elevation != 0 : null; // TokenElevation
        }
        finally { CloseHandle(token); }
    }

    private static bool IsFullscreen(nint window)
    {
        var className = new StringBuilder(64);
        GetClassName(window, className, className.Capacity);
        if (className.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd") return false;
        if (!GetWindowRect(window, out var rect)) return false;
        var monitor = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(MonitorFromWindow(window, 2), ref monitor)) return false;
        var m = monitor.Monitor;
        if (rect.Left == m.Left && rect.Top == m.Top && rect.Right == m.Right && rect.Bottom == m.Bottom) return true;
        // Maximized windows overhang the monitor by their frame too; only caption-less ones are fullscreen.
        bool covers = rect.Left <= m.Left && rect.Top <= m.Top && rect.Right >= m.Right && rect.Bottom >= m.Bottom;
        const long caption = 0x00C00000;
        return covers && (GetWindowLongPtr(window, -16) & caption) != caption;
    }

    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }

    [DllImport("user32.dll")] private static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint window, uint flags);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint pid);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out Rect rect);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder name, int max);
    [DllImport("user32.dll")] private static extern nint GetWindowLongPtr(nint window, int index);
    [DllImport("user32.dll")] private static extern nint MonitorFromWindow(nint window, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(nint monitor, ref MonitorInfo info);
    [DllImport("user32.dll")] private static extern nint SetThreadDpiAwarenessContext(nint context);
    [DllImport("kernel32.dll")] private static extern nint GetCurrentProcess();
    [DllImport("kernel32.dll")] private static extern nint OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageName(nint process, int flags, StringBuilder name, ref int size);
    [DllImport("advapi32.dll")] private static extern bool OpenProcessToken(nint process, uint access, out nint token);
    [DllImport("advapi32.dll")] private static extern bool GetTokenInformation(nint token, int infoClass, out int info, int length, out int returned);
}
