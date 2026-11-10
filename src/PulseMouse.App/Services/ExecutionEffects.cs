using PulseMouse.App.Controls;
using PulseMouse.Core;

namespace PulseMouse.App.Services;

/// <summary>Applies an <see cref="ExecutionResult"/> to capsule, stats, remind, last-action, activity and log (spec 7.1).</summary>
public sealed class ExecutionEffects
{
    private readonly PulseController _controller;

    public ExecutionEffects(PulseController controller) => _controller = controller;

    /// <param name="fired">The binding that the user triggered (may be Repeat); null when miss.</param>
    public void Apply(ExecutionResult result, ExecutionSource source, CommandBinding? fired = null)
    {
        if (!result.Ok)
        {
            ApplyFailure(result, source);
            return;
        }

        var settings = _controller.Settings;
        var command = result.Command!;
        IconKind? icon = result.Direction.Length > 0 ? Directions.Icon(result.Direction) : null;
        string pattern = result.Pattern;
        string contextLabel = ContextLabel(result.Context);
        bool wasRepeat = fired is { ActionKind: ActionKind.Repeat };
        string displayName = wasRepeat ? $"重复：{command.Name}" : command.Name;

        settings.ExecutionsToday++;
        settings.CommandsToday ??= new();
        Bump(settings.CommandsToday, Key(command));
        if (wasRepeat && fired is { } repeat && Key(repeat) != Key(command))
            Bump(settings.CommandsToday, Key(repeat));

        command.LastUsed = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        command.LastContext = result.Context.ContextKey;
        if (result.Reminding && settings.NewMeaningReminder && command.RemindLeft > 0)
            command.RemindLeft--;

        // Remember successful non-workflow actions; never store Repeat/Workflow themselves.
        if (!wasRepeat && command.ActionKind is not (ActionKind.Workflow or ActionKind.Repeat))
        {
            _controller.Executor.Remember(new LastAction
            {
                Command = Clone(command),
                Context = result.Context,
                Direction = result.Direction,
            });
        }

        TrackRecentApp(result.Context);

        bool own = result.Meaning == MeaningSource.Program;
        string meaningNote = own
            ? Loc.T($"{contextLabel} 中的含义", $"Meaning in {contextLabel}")
            : contextLabel.Length > 0 ? Loc.T($"全局 · 在 {contextLabel}", $"Global · in {contextLabel}") : Loc.T("全局", "Global");
        _controller.AddActivityPublic(new ActivityEntry(
            DateTime.Now,
            result.Direction.Length > 0 ? ActivityType.Gesture : ActivityType.Ok,
            displayName,
            pattern.Length > 0 ? pattern : command.Pattern,
            command.ActionKind,
            PulseController.TargetText(command),
            result.Direction,
            result.Context.ContextKey,
            own));

        _controller.AddLog($"已请求执行：{displayName}", meaningNote, LogTone.Hit);
        if (settings.SuccessSound) System.Media.SystemSounds.Asterisk.Play();

        string capsuleText = displayName + " · 已发起";
        string contextLine = _controller.CapsuleLineFor(result);
        if (result.Reminding && settings.NewMeaningReminder)
        {
            var global = settings.Commands.Global(command.Pattern);
            string globalName = global is not null && global.Id != command.Id ? global.Name : "";
            capsuleText = globalName.Length > 0
                ? $"{contextLabel}中 {PatternGlyphText(command.Pattern)} 是新含义 · 全局是「{globalName}」"
                : $"{contextLabel}中 {PatternGlyphText(command.Pattern)} 是新含义";
            _controller.ShowCapsule(FeedbackMode.Matched, pattern, capsuleText, icon, reminding: true, contextLine);
        }
        else
            _controller.ShowCapsule(FeedbackMode.Matched, pattern, capsuleText, icon, contextLine: contextLine);

        _controller.SaveSoon();
        _controller.RaiseStats();
    }

    private void ApplyFailure(ExecutionResult result, ExecutionSource source)
    {
        var settings = _controller.Settings;
        IconKind? icon = result.Direction.Length > 0 ? Directions.Icon(result.Direction) : null;
        string pattern = result.Pattern;
        string contextLabel = ContextLabel(result.Context);

        if (result.FailReason == ExecutionFailReason.Miss)
        {
            settings.MissesToday++;
            string miss = contextLabel.Length > 0
                ? $"{contextLabel} 中没有 {PatternGlyphText(pattern)}"
                : "未找到匹配的指令";
            _controller.AddActivityPublic(new ActivityEntry(DateTime.Now, ActivityType.Miss, miss, pattern, Context: result.Context.ContextKey));
            _controller.ShowCapsule(FeedbackMode.Failed, pattern, miss, icon, contextLine: _controller.CapsuleLineFor(result));
            _controller.AddLog(miss, "", LogTone.Err);
        }
        else if (result.FailReason == ExecutionFailReason.Cancelled)
        {
            _controller.Feedback.HideNow();
            _controller.AddLog(result.FailMessage, "", LogTone.None);
        }
        else
        {
            if (result.FailReason is ExecutionFailReason.ActivateFailed or ExecutionFailReason.WindowGone
                or ExecutionFailReason.ProcessMismatch or ExecutionFailReason.ElevatedTarget)
                settings.ActivationFailedToday++;

            string title = result.Command?.Name ?? "执行";
            string message = result.FailMessage.Length > 0 ? result.FailMessage : result.FailReason.ToString();
            _controller.AddActivityPublic(new ActivityEntry(
                DateTime.Now, ActivityType.Failed, "执行失败：" + title, pattern,
                result.Command?.ActionKind, message, result.Direction));
            _controller.AddLog($"执行失败：{title} · {message}", contextLabel, LogTone.Err);
            _controller.ShowCapsule(FeedbackMode.Failed, pattern, message, icon);
            _controller.Shell?.Toast(message, IconKind.Alert);
        }
        _controller.SaveSoon();
        _controller.RaiseStats();
    }

    public void TrackRecentApp(ContextSnapshot context)
    {
        if (!context.IsTrackable) return;
        _controller.RecentProcess = context.ProcessName;
    }

    private static string Key(CommandBinding c) => c.Id.Length > 0 ? c.Id : c.Name;

    private static void Bump(Dictionary<string, int> map, string key) =>
        map[key] = map.GetValueOrDefault(key) + 1;

    private static string ContextLabel(ContextSnapshot context) =>
        context.IsDesktop || context.ProcessName.Length == 0 || context.ProcessName == ContextNames.Desktop
            ? ""
            : context.ProcessName;

    private static string PatternGlyphText(string pattern) =>
        string.Concat(pattern.Select(c => c == '.' ? '·' : c == '-' ? '—' : c));

    private static CommandBinding Clone(CommandBinding c) => new()
    {
        Id = c.Id, Name = c.Name, Pattern = c.Pattern, ActionKind = c.ActionKind, Target = c.Target,
        Scope = c.Scope, Pinned = c.Pinned, PinnedManually = c.PinnedManually,
        RemindLeft = c.RemindLeft, LastUsed = c.LastUsed, LastContext = c.LastContext,
    };
}
