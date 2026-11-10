using System.Runtime.InteropServices;
using System.Text;

namespace PulseMouse.Windows;

public sealed record RunningApp(string Process, string DisplayName, string ExePath);

/// <summary>Visible top-level windows' processes for the "add program" picker (spec 5.5).</summary>
public static class WindowCatalog
{
    private static readonly HashSet<string> SystemProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer", "searchhost", "searchui", "shellexperiencehost", "startmenuexperiencehost",
        "applicationframehost", "systemsettings", "textinputhost", "runtimebroker",
        "dwm", "csrss", "smss", "winlogon", "services", "lsass", "svchost",
        "taskhostw", "sihost", "fontdrvhost", "conhost", "dllhost",
    };

    private static readonly uint OwnPid = (uint)Environment.ProcessId;

    public static IReadOnlyList<RunningApp> List()
    {
        var byProcess = new Dictionary<string, RunningApp>(StringComparer.OrdinalIgnoreCase);
        EnumWindows((window, _) =>
        {
            if (!IsWindowVisible(window) || GetWindow(window, 4) != 0) return true; // GW_OWNER
            GetWindowThreadProcessId(window, out uint pid);
            if (pid == 0 || pid == OwnPid) return true;
            string? path = ContextTarget.ExePath(pid);
            string process = path is null ? "" : TargetInspector.Normalize(path);
            if (process.Length == 0 || SystemProcesses.Contains(process)) return true;
            if (byProcess.ContainsKey(process)) return true;
            byProcess[process] = new RunningApp(process, ContextTarget.DisplayName(path, process), path ?? "");
            return true;
        }, 0);
        return byProcess.Values.OrderBy(a => a.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>Pick the process under the cursor after a short delay (UI sleeps then calls this).</summary>
    public static RunningApp? UnderCursor()
    {
        var snap = ContextTarget.AtCursor();
        if (snap.IsDesktop || snap.ProcessName.Length == 0) return null;
        string? path = ContextTarget.ExePath(snap.ProcessId);
        return new RunningApp(snap.ProcessName, ContextTarget.DisplayName(path, snap.ProcessName), path ?? "");
    }

    private delegate bool EnumProc(nint window, nint param);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, nint param);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll")] private static extern nint GetWindow(nint window, uint command);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint window, out uint pid);
}
