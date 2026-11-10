using System.Diagnostics;
using System.Runtime.InteropServices;
using PulseMouse.Core;

namespace PulseMouse.Windows;

public enum ActivateStatus
{
    Ok,
    Gone,
    ProcessMismatch,
    Failed,
    Elevated,
}

/// <summary>Verifies and activates the snapshot window before sending a shortcut (spec 5.6 / 5.7).</summary>
public static class WindowActivator
{
    private static readonly bool SelfElevated = ContextTarget.SelfElevated();

    public static ActivateStatus Ensure(ContextSnapshot snapshot, bool restoreMinimized = false)
    {
        if (snapshot.IsDesktop || snapshot.Window == 0)
            return ActivateStatus.Gone;
        if (!IsWindow(snapshot.Window))
            return ActivateStatus.Gone;
        GetWindowThreadProcessId(snapshot.Window, out uint pid);
        if (pid == 0 || pid != snapshot.ProcessId)
            return ActivateStatus.ProcessMismatch;
        if (!SelfElevated && ContextTarget.IsElevated(pid))
            return ActivateStatus.Elevated;

        if (restoreMinimized && IsIconic(snapshot.Window))
            ShowWindow(snapshot.Window, 9); // SW_RESTORE

        if (GetForegroundWindow() == snapshot.Window)
            return ActivateStatus.Ok;

        if (!TryForeground(snapshot.Window))
            return ActivateStatus.Failed;

        long deadline = Stopwatch.GetTimestamp() + Stopwatch.Frequency * 150 / 1000;
        while (Stopwatch.GetTimestamp() < deadline)
        {
            if (GetForegroundWindow() == snapshot.Window) return ActivateStatus.Ok;
            Thread.Sleep(10);
        }
        return GetForegroundWindow() == snapshot.Window ? ActivateStatus.Ok : ActivateStatus.Failed;
    }

    /// <summary>Final check that the foreground is still the intended window before SendInput.</summary>
    public static bool ForegroundIs(ContextSnapshot snapshot)
    {
        if (snapshot.Window == 0 || !IsWindow(snapshot.Window)) return false;
        if (GetForegroundWindow() != snapshot.Window) return false;
        GetWindowThreadProcessId(snapshot.Window, out uint pid);
        return pid == snapshot.ProcessId;
    }

    private static bool TryForeground(nint window)
    {
        uint targetThread = GetWindowThreadProcessId(window, out _);
        uint currentThread = GetCurrentThreadId();
        bool attached = false;
        try
        {
            if (targetThread != 0 && targetThread != currentThread)
                attached = AttachThreadInput(currentThread, targetThread, true);
            BringWindowToTop(window);
            ShowWindow(window, IsIconic(window) ? 9 : 5); // SW_RESTORE / SW_SHOW
            return SetForegroundWindow(window);
        }
        finally
        {
            if (attached) AttachThreadInput(currentThread, targetThread, false);
        }
    }

    [DllImport("user32.dll")] private static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] private static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] private static extern bool ShowWindow(nint window, int command);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint window);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(nint window);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint attach, uint attachTo, bool attachFlag);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint pid);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
}
