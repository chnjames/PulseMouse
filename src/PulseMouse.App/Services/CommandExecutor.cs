using PulseMouse.Actions;
using PulseMouse.Core;
using PulseMouse.Windows;

namespace PulseMouse.App.Services;

/// <summary>Last successful non-workflow / non-repeat action (in-memory only, spec 5.7).</summary>
public sealed class LastAction
{
    public required CommandBinding Command { get; init; }
    public required ContextSnapshot Context { get; init; }
    public string Direction { get; init; } = "";
}

/// <summary>Activates the target window when needed and runs the action. No UI (spec 7.1).</summary>
public sealed class CommandExecutor
{
    public LastAction? Last { get; private set; }

    public void Remember(LastAction action) => Last = action;

    public void ClearLast() => Last = null;

    public ExecutionResult Run(ExecutionRequest request, bool injectionBlocked, Func<CommandBinding, bool, Task<bool>>? confirmWorkflow = null)
        => RunAsync(request, injectionBlocked, confirmWorkflow).GetAwaiter().GetResult();

    public async Task<ExecutionResult> RunAsync(
        ExecutionRequest request,
        bool injectionBlocked,
        Func<CommandBinding, bool, Task<bool>>? confirmWorkflow = null)
    {
        if (request.Command is null)
            return ExecutionResult.Miss(request.Pattern, request.Context);

        var command = request.Command;
        bool reminding = command.RemindLeft > 0;
        ContextSnapshot activateContext = request.Context;
        bool forceActivate = false;
        CommandBinding? repeatSource = null;

        if (command.ActionKind == ActionKind.Repeat)
        {
            if (Last is null)
                return ExecutionResult.Fail(ExecutionFailReason.NothingToRepeat, "还没有可重复的动作", request, reminding);
            repeatSource = Clone(Last.Command);
            activateContext = Last.Context;
            forceActivate = repeatSource.ActionKind == ActionKind.Shortcut;
            command = repeatSource;
            request = request with
            {
                Command = command,
                Context = activateContext,
                NeedActivate = forceActivate || request.NeedActivate,
                Direction = Last.Direction.Length > 0 ? Last.Direction : request.Direction,
            };
            reminding = false;
        }

        if (command.ActionKind == ActionKind.Shortcut)
        {
            if (injectionBlocked)
                return ExecutionResult.Fail(ExecutionFailReason.ActionFailed, "模拟输入被系统拦截，快捷键无法发送", request, reminding);

            bool needActivate = forceActivate || request.NeedActivate;
            if (needActivate)
            {
                if (activateContext.IsDesktop)
                    return ExecutionResult.Fail(ExecutionFailReason.WindowGone, "光标在桌面，无法激活目标窗口", request, reminding);

                var status = WindowActivator.Ensure(activateContext, restoreMinimized: forceActivate);
                if (status != ActivateStatus.Ok)
                    return FailActivate(status, request, reminding, activateContext.ProcessName);

                if (!WindowActivator.ForegroundIs(activateContext))
                    return ExecutionResult.Fail(ExecutionFailReason.ActivateFailed,
                        $"无法切换到 {Label(activateContext.ProcessName)} 窗口", request, reminding);
            }
            else if (!activateContext.IsDesktop && !ContextTarget.SelfElevated()
                     && ContextTarget.IsElevated(activateContext.ProcessId))
            {
                return ExecutionResult.Fail(ExecutionFailReason.ElevatedTarget,
                    $"目标窗口以管理员身份运行，无法发送快捷键", request, reminding);
            }
        }

        if (command.ActionKind == ActionKind.Workflow && confirmWorkflow is not null)
        {
            bool ok = await confirmWorkflow(command, true);
            if (!ok)
                return ExecutionResult.Fail(ExecutionFailReason.Cancelled, "用户取消了工作流执行", request, reminding);
        }

        try
        {
            bool workflowAuthorized = command.ActionKind != ActionKind.Workflow || confirmWorkflow is null;
            // When confirmWorkflow already ran, authorize the runner.
            if (command.ActionKind == ActionKind.Workflow && confirmWorkflow is not null)
                workflowAuthorized = true;
            await Task.Run(() => ActionRunner.Run(command, workflowAuthorized));
            return ExecutionResult.Success(request, reminding);
        }
        catch (Exception ex)
        {
            return ExecutionResult.Fail(ExecutionFailReason.ActionFailed, ex.GetBaseException().Message, request, reminding);
        }
    }

    /// <summary>Builds a request for a resolved rhythm / gesture / test.</summary>
    public static ExecutionRequest Build(
        ExecutionSource source,
        string pattern,
        ContextSnapshot context,
        CommandBinding? command,
        bool contextEnabled,
        bool activateShortcuts,
        string direction = "")
    {
        var meaning = ContextResolver.Classify(command, context.ContextKey);
        bool needActivate = command is { ActionKind: ActionKind.Shortcut }
            && activateShortcuts
            && !context.IsDesktop
            && source != ExecutionSource.Gesture;
        // Gestures never activate (no context). Repeat forces activate inside Run.
        if (source == ExecutionSource.Repeat && command is { ActionKind: ActionKind.Shortcut })
            needActivate = true;
        return new ExecutionRequest(source, pattern, context, command, meaning, needActivate, direction);
    }

    private static ExecutionResult FailActivate(ActivateStatus status, ExecutionRequest request, bool reminding, string process)
    {
        string label = Label(process);
        return status switch
        {
            ActivateStatus.Gone or ActivateStatus.ProcessMismatch =>
                ExecutionResult.Fail(
                    status == ActivateStatus.ProcessMismatch ? ExecutionFailReason.ProcessMismatch : ExecutionFailReason.WindowGone,
                    status == ActivateStatus.ProcessMismatch ? "原窗口已关闭" : "目标窗口已关闭",
                    request, reminding),
            ActivateStatus.Elevated =>
                ExecutionResult.Fail(ExecutionFailReason.ElevatedTarget, $"目标窗口以管理员身份运行，无法发送快捷键", request, reminding),
            _ => ExecutionResult.Fail(ExecutionFailReason.ActivateFailed, $"无法切换到 {label} 窗口", request, reminding),
        };
    }

    private static string Label(string process) =>
        process is { Length: > 0 } and not ContextNames.Desktop ? process : "目标";

    private static CommandBinding Clone(CommandBinding c) => new()
    {
        Id = c.Id, Name = c.Name, Pattern = c.Pattern, ActionKind = c.ActionKind, Target = c.Target,
        Scope = c.Scope, Pinned = c.Pinned, PinnedManually = c.PinnedManually,
        RemindLeft = c.RemindLeft, LastUsed = c.LastUsed, LastContext = c.LastContext,
    };
}
