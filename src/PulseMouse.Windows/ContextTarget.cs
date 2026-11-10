using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using PulseMouse.Core;

namespace PulseMouse.Windows;

/// <summary>Reads the window under a point into a <see cref="ContextSnapshot"/> (spec 5.1).</summary>
public static class ContextTarget
{
    private static readonly string[] DesktopClasses =
        ["Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd"];

    private static readonly uint OwnPid = (uint)Environment.ProcessId;

    public static ContextSnapshot AtCursor()
    {
        nint previous = SetThreadDpiAwarenessContext(-4);
        try
        {
            GetCursorPos(out var point);
            return At(point.X, point.Y);
        }
        finally
        {
            if (previous != 0) SetThreadDpiAwarenessContext(previous);
        }
    }

    public static ContextSnapshot At(int x, int y)
    {
        nint previous = SetThreadDpiAwarenessContext(-4);
        try
        {
            nint window = GetAncestor(WindowFromPoint(new Point { X = x, Y = y }), 2); // GA_ROOT
            if (window == 0) return ContextSnapshot.Desktop;
            var className = new StringBuilder(64);
            GetClassName(window, className, className.Capacity);
            if (DesktopClasses.Contains(className.ToString()))
                return new ContextSnapshot(window, Pid(window), ContextNames.Desktop, true);

            uint pid = Pid(window);
            if (pid == 0 || pid == OwnPid) return ContextSnapshot.Desktop;
            string name = ProcessName(pid);
            if (name.Length == 0) return ContextSnapshot.Desktop;
            return new ContextSnapshot(window, pid, name, false);
        }
        catch
        {
            return ContextSnapshot.Desktop;
        }
        finally
        {
            if (previous != 0) SetThreadDpiAwarenessContext(previous);
        }
    }

    public static bool IsElevated(uint processId)
    {
        nint process = OpenProcess(0x1000, false, processId);
        if (process == 0) return true;
        try
        {
            if (!OpenProcessToken(process, 0x0008, out nint token)) return true;
            try
            {
                return GetTokenInformation(token, 20, out int elevation, sizeof(int), out _) && elevation != 0;
            }
            finally { CloseHandle(token); }
        }
        finally { CloseHandle(process); }
    }

    public static bool SelfElevated()
    {
        nint process = GetCurrentProcess();
        if (!OpenProcessToken(process, 0x0008, out nint token)) return false;
        try
        {
            return GetTokenInformation(token, 20, out int elevation, sizeof(int), out _) && elevation != 0;
        }
        finally { CloseHandle(token); }
    }

    public static string? ExePath(uint processId)
    {
        nint process = OpenProcess(0x1000, false, processId);
        if (process == 0) return null;
        try
        {
            var buffer = new StringBuilder(1024);
            int size = buffer.Capacity;
            return QueryFullProcessImageName(process, 0, buffer, ref size) ? buffer.ToString() : null;
        }
        finally { CloseHandle(process); }
    }

    public static string DisplayName(string? exePath, string processName)
    {
        if (exePath is { Length: > 0 })
        {
            try
            {
                string? description = FileVersionInfo.GetVersionInfo(exePath).FileDescription;
                if (!string.IsNullOrWhiteSpace(description)) return description.Trim();
            }
            catch { /* fall through */ }
        }
        return processName;
    }

    private static uint Pid(nint window)
    {
        if (window == 0) return 0;
        GetWindowThreadProcessId(window, out uint pid);
        return pid;
    }

    private static string ProcessName(uint pid)
    {
        nint process = OpenProcess(0x1000, false, pid);
        if (process == 0) return "";
        try
        {
            var buffer = new StringBuilder(1024);
            int size = buffer.Capacity;
            if (!QueryFullProcessImageName(process, 0, buffer, ref size)) return "";
            return TargetInspector.Normalize(buffer.ToString());
        }
        finally { CloseHandle(process); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }

    [DllImport("user32.dll")] private static extern nint WindowFromPoint(Point point);
    [DllImport("user32.dll")] private static extern nint GetAncestor(nint window, uint flags);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder name, int max);
    [DllImport("user32.dll")] private static extern nint SetThreadDpiAwarenessContext(nint context);
    [DllImport("kernel32.dll")] private static extern nint GetCurrentProcess();
    [DllImport("kernel32.dll")] private static extern nint OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(nint handle);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageName(nint process, int flags, StringBuilder name, ref int size);
    [DllImport("advapi32.dll")] private static extern bool OpenProcessToken(nint process, uint access, out nint token);
    [DllImport("advapi32.dll")] private static extern bool GetTokenInformation(nint token, int infoClass, out int info, int length, out int returned);
}
