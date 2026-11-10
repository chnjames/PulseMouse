namespace PulseMouse.Core;

/// <summary>Window under the cursor when the trigger was pressed (spec 5.1 / 7.1).</summary>
public readonly record struct ContextSnapshot(nint Window, uint ProcessId, string ProcessName, bool IsDesktop)
{
    public static readonly ContextSnapshot Desktop = new(0, 0, ContextNames.Desktop, true);

    /// <summary>Context key used by <see cref="ContextResolver"/>: desktop sentinel or process name.</summary>
    public string ContextKey => IsDesktop || ProcessName.Length == 0 ? ContextNames.Desktop : ProcessName;

    /// <summary>True when this context should update "最近输入的程序".</summary>
    public bool IsTrackable => !IsDesktop && ProcessName.Length > 0;
}

public enum ExecutionSource { Rhythm, Gesture, Repeat, Test }

public enum MeaningSource { None, Program, Global, Pinned }

public enum ExecutionFailReason
{
    None = 0,
    Miss,
    WindowGone,
    ProcessMismatch,
    ActivateFailed,
    ElevatedTarget,
    ActionFailed,
    NothingToRepeat,
    Cancelled,
}

/// <summary>Immutable request handed to <c>CommandExecutor</c>.</summary>
public sealed record ExecutionRequest(
    ExecutionSource Source,
    string Pattern,
    ContextSnapshot Context,
    CommandBinding? Command,
    MeaningSource Meaning,
    bool NeedActivate,
    string Direction = "");

/// <summary>Immutable result produced by execution; UI/stats/log all read this.</summary>
public sealed record ExecutionResult(
    bool Ok,
    ExecutionFailReason FailReason,
    string FailMessage,
    CommandBinding? Command,
    string Pattern,
    ContextSnapshot Context,
    MeaningSource Meaning,
    bool Reminding,
    string Direction = "")
{
    public static ExecutionResult Miss(string pattern, ContextSnapshot context) =>
        new(false, ExecutionFailReason.Miss, "", null, pattern, context, MeaningSource.None, false);

    public static ExecutionResult Fail(ExecutionFailReason reason, string message, ExecutionRequest request, bool reminding = false) =>
        new(false, reason, message, request.Command, request.Pattern, request.Context, request.Meaning, reminding, request.Direction);

    public static ExecutionResult Success(ExecutionRequest request, bool reminding) =>
        new(true, ExecutionFailReason.None, "", request.Command, request.Pattern, request.Context, request.Meaning, reminding, request.Direction);
}
