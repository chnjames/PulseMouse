using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using Microsoft.Win32;
using PulseMouse.Actions;
using PulseMouse.App.Controls;
using PulseMouse.Core;
using PulseMouse.Infrastructure;
using PulseMouse.Windows;

namespace PulseMouse.App.Services;

/// <summary>Owns settings, the mouse hook, the rhythm engine, execution and statistics. Lives on the UI thread.</summary>
public sealed class PulseController : IDisposable
{
    private const int LogLimit = 60, ActivityLimit = 20, IdleAfterMs = 3200;

    private readonly SettingsStore _store = new();
    private readonly MouseHook _hook = new();
    private readonly RhythmEngine _engine;
    private readonly DispatcherTimer _timer;
    private readonly DispatcherTimer _saveTimer;
    private readonly Dispatcher _dispatcher;
    private readonly Stopwatch _uptime = Stopwatch.StartNew();
    private readonly CommandExecutor _executor = new();
    private readonly ExecutionEffects _effects;
    private double _accountedSeconds;
    private long _lastStatsSecond = -1;
    private bool _completedDuringUp;
    private long? _rawDown;

    public AppSettings Settings { get; private set; }
    public FeedbackWindow Feedback { get; } = new();
    public IShell? Shell { get; set; }
    public CommandExecutor Executor => _executor;
    /// <summary>Most recent non-desktop process under a trigger press this session (spec 5.1 / 5.5).</summary>
    public string RecentProcess { get; set; } = "";
    public ObservableCollection<LogEntry> Log { get; } = new();
    public ObservableCollection<ActivityEntry> Activity { get; } = new();
    public string ConfigPath => _store.FilePath;
    public string SavedAt { get; private set; } = "—";
    public bool HookInstalled => _hook.IsInstalled;
    public bool InjectionBlocked => _hook.InjectionBlocked;

    // Live rhythm state shown on the dashboard
    public LivePhase Phase { get; private set; } = LivePhase.Idle;
    public string Sequence => _engine.Sequence;
    public long? PressStartMs { get; private set; }
    public long? LastReleaseMs { get; private set; }
    public double? LastDurationMs { get; private set; }
    public char? LastPulse { get; private set; }
    public string HitPattern { get; private set; } = "";
    public CommandBinding? HitCommand { get; private set; }
    public string PhaseMessage { get; private set; } = "";
    private long _phaseChangedMs;
    private const int HintDelayMs = 600;
    /// <summary>When the chord trigger started being held back; null once it is released or handed to the app.</summary>
    private long? _triggerHeldMs;
    private bool _holdCapsuleShown;
    private string? _hintPrefix;

    /// <summary>Listening state, input route or hook status changed.</summary>
    public event Action? StateChanged;
    /// <summary>Live rhythm phase changed (press, release, match…).</summary>
    public event Action? LiveChanged;
    /// <summary>Commands or gestures changed.</summary>
    public event Action? BindingsChanged;
    /// <summary>Statistics ticked (once per second or after a run).</summary>
    public event Action? StatsChanged;
    public event Action? Saved;
    /// <summary>The whole settings object was replaced (import).</summary>
    public event Action? SettingsReplaced;

    public static long NowMs => Stopwatch.GetTimestamp() * 1000 / Stopwatch.Frequency;

    public PulseController(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _effects = new ExecutionEffects(this);
        string? loadError = null;
        try { Settings = _store.Load(); }
        catch (Exception ex) { loadError = ex.Message; Settings = new AppSettings(); }
        if (File.Exists(_store.FilePath)) SavedAt = File.GetLastWriteTime(_store.FilePath).ToString("HH:mm:ss");
        EnsureCommandIds();
        NormalizeStatistics();
        ApplySettings();

        _engine = new RhythmEngine(() => Settings);
        _engine.Completed += OnCompleted;
        _hook.GestureEvent += dir => _dispatcher.BeginInvoke(() => OnGesture(dir));
        _hook.StateChanged += message => _dispatcher.BeginInvoke(DispatcherPriority.Background, () => OnHookState(message));
        _hook.ButtonEvent += (down, ticks) =>
            // Hook callback: only enqueue, never execute work or touch WPF here.
            _dispatcher.BeginInvoke(DispatcherPriority.Input, () => OnButton(down, ticks * 1000 / Stopwatch.Frequency));
        _hook.ChordEvent += (step, _) => _dispatcher.BeginInvoke(DispatcherPriority.Input, () => OnChord(step));
        _hook.RawButtonEvent += ev => _dispatcher.BeginInvoke(DispatcherPriority.Background, () => OnRawButton(ev));
        _hook.SideDetected += button => _dispatcher.BeginInvoke(() => { Settings.DetectedSideButton = button; SaveSoon(); });
        _hook.AutoSide = Settings.DetectedSideButton;

        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(40), DispatcherPriority.Background, (_, _) => OnTick(), _dispatcher);
        _saveTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(350), DispatcherPriority.Background, (_, _) => { _saveTimer!.Stop(); Save(); }, _dispatcher);
        _saveTimer.Stop();
        if (loadError is not null) AddLog(loadError + "，已加载默认配置", "", LogTone.Err);
    }

    public void Start()
    {
        InstallHook();
        _timer.Start();
    }

    // ---------- settings ----------

    public void ApplySettings()
    {
        Loc.English = Settings.UiLanguage == "en-US";
        _hook.Enabled = Settings.Enabled && _hook.IsInstalled;
        _hook.Mode = Settings.InputMode;
        _hook.MiddleHoldMs = Settings.MiddleHoldMs;
        _hook.GesturesEnabled = Settings.GesturesEnabled;
        _hook.GestureMinDistancePx = Settings.GestureMinDistancePx;
        Settings.MiddleClickMaxMs = Math.Clamp(Settings.MiddleClickMaxMs, 200, 800);
        Settings.MiddlePassThroughMs = MiddleDecision.NormalizePassThroughMs(Settings.MiddlePassThroughMs, Settings.MiddleClickMaxMs);
        _hook.MiddleClickMaxMs = Settings.MiddleClickMaxMs;
        _hook.MiddlePassThroughMs = Settings.MiddlePassThroughMs;
        _hook.PreserveMiddleClick = Settings.PreserveMiddleClick;
        _hook.PauseInFullscreen = Settings.PauseInFullscreen;
        _hook.SetExcludedApps(Settings.ExcludedApps ??= new());
        Feedback.Configure(Settings.CapsulePosition, Settings.CapsuleBreathing);
    }

    public void SaveSoon()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public bool Save()
    {
        _saveTimer.Stop();
        try
        {
            AccountUptime();
            Settings.SchemaVersion = AppSettings.CurrentSchemaVersion;
            _store.Save(Settings);
            SavedAt = DateTime.Now.ToString("HH:mm:ss");
            Saved?.Invoke();
            return true;
        }
        catch (Exception ex)
        {
            AddLog("保存配置失败：" + ex.Message, "", LogTone.Err);
            Shell?.Toast("保存失败：" + ex.Message, IconKind.Alert);
            return false;
        }
    }

    public void SetStartup(bool enabled)
    {
        Settings.StartupEnabled = enabled;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            const string name = "PulseMouse";
            if (enabled) key.SetValue(name, $"\"{Environment.ProcessPath}\""); else key.DeleteValue(name, false);
        }
        catch (Exception ex) { AddLog("写入启动项失败：" + ex.Message, "", LogTone.Err); }
        SaveSoon();
    }

    public void Export(string path)
    {
        Save();
        File.Copy(_store.FilePath, path, true);
    }

    /// <summary>Replaces all settings from a backup and applies them without restarting.</summary>
    public void Import(string path)
    {
        var imported = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path)) ?? throw new InvalidDataException("配置内容为空");
        imported.Commands ??= new();
        imported.Gestures ??= new();
        imported.ContextApps ??= new();
        imported.ExcludedApps ??= new();
        imported.CommandsToday ??= new();
        imported.History ??= new();
        if (imported.SchemaVersion < AppSettings.CurrentSchemaVersion)
            SettingsMigrator.MigrateFromV8(imported);
        else
            SettingsMigrator.Migrate(imported);
        CancelInput(quiet: true);
        Settings = imported;
        EnsureCommandIds();
        foreach (var command in Settings.Commands)
            PinPolicy.Apply(command); // force Workflow/Repeat global after import
        NormalizeStatistics();
        ApplySettings();
        _hook.AutoSide = Settings.DetectedSideButton;
        SetStartup(Settings.StartupEnabled);
        Save();
        AddLog(Loc.T("已导入配置并立即生效", "Settings imported and applied"), "", LogTone.Hit);
        SettingsReplaced?.Invoke();
        StateChanged?.Invoke();
        BindingsChanged?.Invoke();
        StatsChanged?.Invoke();
    }

    public void NotifyBindingsChanged()
    {
        Save();
        BindingsChanged?.Invoke();
    }

    // ---------- listening ----------

    public bool Enabled => Settings.Enabled;

    public void SetEnabled(bool enabled)
    {
        if (Settings.Enabled == enabled) return;
        Settings.Enabled = enabled;
        _hook.Enabled = enabled && _hook.IsInstalled;
        ResetEngine();
        _hook.ResetInput();
        ResetLive();
        if (!enabled) Feedback.HideNow();
        AddLog(enabled ? "监听已启用" : "监听已暂停，正常鼠标操作已恢复", "", LogTone.None);
        Save();
        StateChanged?.Invoke();
        LiveChanged?.Invoke();
    }

    public void SetInputMode(MouseInputMode mode)
    {
        if (Settings.InputMode == mode) return;
        Settings.InputMode = mode;
        _hook.Mode = mode;
        _hook.ResetInput();
        ResetEngine();
        ResetLive();
        AddLog("输入方式切换为：" + RouteName(mode), "", LogTone.None);
        Save();
        StateChanged?.Invoke();
        LiveChanged?.Invoke();
    }

    public static string RouteName(MouseInputMode mode) => mode switch
    {
        MouseInputMode.X1 => "X1 · 后退侧键",
        MouseInputMode.X2 => "X2 · 前进侧键",
        MouseInputMode.Middle => "中键长按 + 左键",
        MouseInputMode.Chord => "右键 + 左键 · 和弦",
        _ => "自动适配"
    };

    public string ActiveInputText => Settings.InputMode switch
    {
        MouseInputMode.X1 => "X1 后退侧键",
        MouseInputMode.X2 => "X2 前进侧键",
        MouseInputMode.Middle => "中键长按 + 左键",
        MouseInputMode.Chord => "按住右键 + 左键",
        _ => _hook.ActiveInput
    };

    public bool IsChordMode => _hook.ChordActive;

    public int DetectedSideButton => _hook.AutoSide;

    public void ResetSideDetection()
    {
        _hook.AutoSide = 0;
        Settings.DetectedSideButton = 0;
        _hook.ResetInput();
        ResetEngine();
        ResetLive();
        AddLog("已清除侧键检测结果；自动模式改用右键 + 左键和弦，按一下侧键可重新检测", "", LogTone.None);
        Save();
        StateChanged?.Invoke();
        LiveChanged?.Invoke();
    }

    public void SetExcludedApps(IEnumerable<string> names)
    {
        Settings.ExcludedApps = names.Select(MouseHook.NormalizeAppName).Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        ApplySettings();
        SaveSoon();
    }

    /// <summary>Which button currently types rhythms (used for log lines of simulated input).</summary>
    private bool UsesMiddle => Settings.InputMode == MouseInputMode.Middle;
    private bool UsesLeft => UsesMiddle || IsChordMode;

    public void InstallHook()
    {
        try
        {
            _hook.Install();
            _hook.Enabled = Settings.Enabled;
            AddLog("Hook 安装成功；" + ActiveInputText, "", LogTone.None);
        }
        catch (Exception ex)
        {
            _hook.Enabled = false;
            AddLog("Hook 安装失败：" + ex.Message, "", LogTone.Err);
        }
        StateChanged?.Invoke();
    }

    public void ReinstallHook()
    {
        _hook.Dispose();
        _hook.ResetInput();
        ResetEngine();
        ResetLive();
        InstallHook();
        if (_hook.IsInstalled) Shell?.Toast("监听已重新安装");
        LiveChanged?.Invoke();
    }

    public void CancelInput(bool quiet = false)
    {
        bool active = Sequence.Length > 0 || PressStartMs is not null || Phase is LivePhase.Armed or LivePhase.Press or LivePhase.Typing;
        _hook.CancelInput();
        ResetEngine();
        ResetLive();
        Feedback.HideNow();
        LiveChanged?.Invoke();
        if (!quiet && active) Shell?.Toast("已取消输入，恢复普通鼠标操作", IconKind.Info);
        else if (!quiet) Shell?.Toast("当前没有正在进行的输入", IconKind.Info);
    }

    // ---------- simulated input (dashboard "hold to simulate") ----------

    public void SimulateDown()
    {
        if (!Settings.Enabled) { Shell?.Toast("监听已暂停，请在左下角恢复", IconKind.Info); return; }
        AddLog($"{SimButton} 按下", "[模拟] 节奏输入", LogTone.Hit);
        OnButton(true, NowMs);
    }

    public void SimulateUp()
    {
        if (PressStartMs is not long start) return;
        long now = NowMs;
        AddLog($"{SimButton} 释放，时长 {now - start} ms", "[模拟] 节奏输入", LogTone.Hit);
        OnButton(false, now);
    }

    private string SimButton => UsesLeft ? "左键" : Settings.InputMode == MouseInputMode.X1 ? "X1" : "X2";

    // ---------- engine plumbing ----------

    private void OnButton(bool down, long ms)
    {
        if (down)
        {
            if (PressStartMs is not null) return;
            bool fresh = Sequence.Length == 0 && Phase != LivePhase.Typing;
            _engine.Down(ms);
            PressStartMs = ms;
            SetPhase(LivePhase.Press);
            if (fresh)
            {
                ShowCapsule(FeedbackMode.Armed, "", _engine.GapSuspended ? "松开右键即执行" : UsesMiddle ? "指令模式已激活" : "正在输入…",
                    contextLine: CapsuleContextLine(_hook.SessionContext, null, MeaningSource.None, false));
            }
            UpdateHint(ms);
            LiveChanged?.Invoke();
            return;
        }
        if (PressStartMs is not long start) { _engine.Up(ms); return; }
        PressStartMs = null;
        double duration = ms - start;
        LastDurationMs = duration;
        string before = Sequence;
        _completedDuringUp = false;
        _engine.Up(ms);
        if (_completedDuringUp)
        {
            LastPulse = Classify(duration);
        }
        else if (Sequence.Length > before.Length)
        {
            LastPulse = Sequence[^1];
            LastReleaseMs = ms;
            SetPhase(LivePhase.Typing);
            var ctx = _hook.SessionContext;
            var exact = Settings.Commands.Resolve(Sequence, ctx.ContextKey, Settings.ContextEnabled);
            var meaning = ContextResolver.Classify(exact, ctx.ContextKey);
            ShowCapsule(FeedbackMode.Typing, Sequence, exact?.Name ?? "正在输入…",
                contextLine: CapsuleContextLine(ctx, exact, meaning, false));
        }
        else
        {
            LastPulse = 'x';
            LastReleaseMs = null;
            var s = Settings;
            PhaseMessage = duration < 35 || duration > s.LongMaxMs
                ? $"按住 {duration:0} ms 超出有效范围，已重置"
                : $"按住 {duration:0} ms 落在无效区（{s.ShortMaxMs}–{s.LongMinMs} ms），已重置";
            SetPhase(LivePhase.Reset);
            AddLog(PhaseMessage, "", LogTone.Err);
            ShowCapsule(FeedbackMode.Failed, "", PhaseMessage.Split('，')[0]);
        }
        LiveChanged?.Invoke();
    }

    private void ResetEngine()
    {
        _engine.GapSuspended = false;
        _engine.Reset();
        _triggerHeldMs = null;
        _holdCapsuleShown = false;
    }

    private void OnChord(ChordStep step)
    {
        switch (step)
        {
            case ChordStep.Swallow:
                _triggerHeldMs = NowMs;
                break;
            case ChordStep.Begin:
                _engine.GapSuspended = true;
                _holdCapsuleShown = false;
                break;
            case ChordStep.ReplayDown:
            case ChordStep.ReplayClick:
                _triggerHeldMs = null;
                if (_holdCapsuleShown) Feedback.HideNow();
                _holdCapsuleShown = false;
                break;
            case ChordStep.Commit:
                _triggerHeldMs = null;
                _engine.GapSuspended = false;
                _engine.Commit();
                break;
            case ChordStep.Cancel:
                AddLog("和弦输入已取消", "", LogTone.None);
                CancelInput(quiet: true);
                break;
        }
        UpdateHint(NowMs);
    }

    /// <summary>Shows the reachable rhythms once input pauses: trigger held with nothing typed, mid-chord, or middle-armed.</summary>
    private void UpdateHint(long now)
    {
        string? prefix = null;
        if (Settings.HesitationHints && Settings.FloatingFeedback && Settings.Enabled && PressStartMs is null)
        {
            if (_triggerHeldMs is long held)
            {
                long since = Sequence.Length == 0 ? held : LastReleaseMs ?? now;
                if (now - since >= HintDelayMs) prefix = Sequence;
            }
            else if (Phase == LivePhase.Armed && now - _phaseChangedMs >= HintDelayMs) prefix = "";
        }
        if (prefix == _hintPrefix && (prefix is null || Feedback.HintVisible)) return;
        _hintPrefix = prefix;
        if (prefix is null) { Feedback.HideHint(); return; }
        if (_triggerHeldMs is not null && Sequence.Length == 0 && Phase != LivePhase.Armed)
        {
            ShowCapsule(FeedbackMode.Armed, "", "用左键敲节奏；不敲直接松开即普通右键");
            _holdCapsuleShown = true;
        }
        var context = _hook.SessionContext;
        var (shown, hidden) = RhythmHints.ForContext(
            Settings.Commands, context.ContextKey, Settings.ContextEnabled, prefix, 6);
        string title = ContextHintTitle(context);
        var rows = shown.Select(c => (c.Pattern, c.Name, HintTag(c, context.ContextKey))).ToList();
        try { Feedback.ShowHint(prefix, rows, hidden, title); }
        catch (Exception ex) { AddLog("节奏提示显示失败: " + ex.Message, "", LogTone.Err); }
    }

    private string ContextHintTitle(ContextSnapshot context)
    {
        if (!Settings.CapsuleShowsContext || context.IsDesktop) return Loc.T("可用节奏", "Available rhythms");
        return Loc.T($"{context.ProcessName} 中可用", $"Available in {context.ProcessName}");
    }

    private string HintTag(CommandBinding command, string contextKey)
    {
        if (command.Scope.Length > 0)
        {
            var global = Settings.Commands.Global(command.Pattern);
            return global is null
                ? Loc.T("专属", "Own")
                : Loc.T($"替换「{global.Name}」", $"replaces “{global.Name}”");
        }
        return command.Pinned ? Loc.T("🔒 固定", "🔒 pinned") : "";
    }

    private string CapsuleContextLine(ContextSnapshot context, CommandBinding? command, MeaningSource meaning, bool reminding)
    {
        if (!Settings.CapsuleShowsContext) return "";
        if (context.IsDesktop) return Loc.T("全局", "Global");
        string app = context.ProcessName;
        int own = Settings.Commands.Count(c => c.Scope.Equals(app, StringComparison.OrdinalIgnoreCase));
        if (command is null)
            return own > 0 ? Loc.T($"{app} · {own} 个节奏有专属含义", $"{app} · {own} own rhythms") : Loc.T($"{app} · 全部沿用全局", $"{app} · all global");
        if (reminding && meaning == MeaningSource.Program)
        {
            var global = Settings.Commands.Global(command.Pattern);
            string g = global is null ? "" : Loc.T($" · 全局是「{global.Name}」", $" · global is “{global.Name}”");
            return Loc.T($"{app} 中 {PatternText(command.Pattern)} 是新含义{g}", $"{app}: {PatternText(command.Pattern)} is new{g}");
        }
        return meaning switch
        {
            MeaningSource.Program => Loc.T($"{app} 中的含义", $"Meaning in {app}"),
            MeaningSource.Pinned => Loc.T($"🔒 全局 固定 · 在 {app}", $"🔒 Global pinned · in {app}"),
            MeaningSource.Global => Loc.T($"全局 · 在 {app}", $"Global · in {app}"),
            _ => app
        };
    }

    private static string PatternText(string pattern) =>
        string.Concat(pattern.Select(c => c == '.' ? '·' : c == '-' ? '—' : c));

    private char? Classify(double duration)
    {
        var s = Settings;
        if (duration < 35 || duration > s.LongMaxMs) return 'x';
        return duration <= s.ShortMaxMs ? '.' : duration >= s.LongMinMs ? '-' : 'x';
    }

    private void OnCompleted(Recognition result)
    {
        _completedDuringUp = true;
        var context = _hook.SessionContext;
        _effects.TrackRecentApp(context);
        _hook.FinishSequence();
        LastReleaseMs = null;
        HitPattern = result.Pattern;
        var command = Settings.Commands.Resolve(result.Pattern, context.ContextKey, Settings.ContextEnabled);
        HitCommand = command;
        SetPhase(command is null ? LivePhase.Miss : LivePhase.Matched);
        _ = Dispatch(ExecutionSource.Rhythm, result.Pattern, context, command);
        _hook.ReleaseContext();
        LiveChanged?.Invoke();
    }

    private void OnTick()
    {
        long now = NowMs;
        _engine.Tick(now);
        UpdateHint(now);
        if (Phase is LivePhase.Matched or LivePhase.Miss or LivePhase.Reset && now - _phaseChangedMs > IdleAfterMs)
        {
            ResetLive();
            LiveChanged?.Invoke();
        }
        long second = _uptime.ElapsedMilliseconds / 1000;
        if (second != _lastStatsSecond)
        {
            _lastStatsSecond = second;
            if (NormalizeStatistics()) SaveSoon();
            StatsChanged?.Invoke();
        }
    }

    private void OnHookState(string message)
    {
        if (message.StartsWith("模拟输入"))
        {
            bool blocked = _hook.InjectionBlocked;
            AddLog(message, "", blocked ? LogTone.Err : LogTone.Hit);
            Shell?.Toast(blocked ? "模拟输入被拦截，右键和弦已暂停" : "模拟输入已恢复，右键和弦重新启用", blocked ? IconKind.Alert : IconKind.Info);
            StateChanged?.Invoke();
            return;
        }
        AddLog(message, "", LogTone.None);
        if (message.Contains("已检测到侧键"))
        {
            AddActivity(new ActivityEntry(DateTime.Now, ActivityType.System, message));
            StateChanged?.Invoke();
        }
        if ((message.Contains("取消") || message.Contains("超时")) && Phase is not (LivePhase.Matched or LivePhase.Miss))
        {
            Feedback.HideNow();
            if (Phase == LivePhase.Armed) { ResetLive(); LiveChanged?.Invoke(); }
        }
        if (message.Contains("激活") && Settings.Enabled)
        {
            SetPhase(LivePhase.Armed);
            ShowCapsule(FeedbackMode.Armed, "", "指令模式已激活");
            LiveChanged?.Invoke();
        }
    }

    private void OnRawButton(MouseButtonEvent ev)
    {
        string duration = "";
        if (ev.IsDown) _rawDown = ev.Timestamp;
        else if (_rawDown is long start)
        {
            duration = $"，时长 {(ev.Timestamp - start) * 1000.0 / Stopwatch.Frequency:0} ms";
            _rawDown = null;
        }
        AddLog($"{ev.Button} {(ev.IsDown ? "按下" : "释放")}{duration}", $"{(ev.Consumed ? "[识别]" : "[透传]")} {ev.Note}", ev.Consumed ? LogTone.Hit : LogTone.Pass);
    }

    private void OnGesture(string direction)
    {
        AddLog("识别中键方向手势 " + direction, "", LogTone.None);
        var gesture = Settings.Gestures.FirstOrDefault(g => g.Direction.Equals(direction, StringComparison.OrdinalIgnoreCase));
        if (gesture is null)
        {
            ShowCapsule(FeedbackMode.Failed, "", $"向{Directions.Zh(direction)}手势未绑定", Directions.Icon(direction));
            return;
        }
        _ = ExecuteGesture(gesture);
    }

    private async Task Dispatch(ExecutionSource source, string pattern, ContextSnapshot context, CommandBinding? command, string direction = "")
    {
        var request = CommandExecutor.Build(
            source, pattern, context, command, Settings.ContextEnabled, Settings.ActivateTargetForShortcut, direction);
        if (command is null)
        {
            _effects.Apply(ExecutionResult.Miss(pattern, context), source);
            return;
        }

        // Workflow confirm stays on the UI thread before the executor runs.
        Func<CommandBinding, bool, Task<bool>>? confirm = null;
        if (command.ActionKind == ActionKind.Workflow && Settings.ConfirmWorkflows && source != ExecutionSource.Test)
        {
            confirm = async (binding, _) =>
            {
                var steps = ParseSteps(binding.Target);
                var items = steps.Select(s => $"{Loc.Kind(s.Kind)}  {s.Target}{(s.DelayMs > 0 ? $"  (等待 {s.DelayMs} ms)" : "")}").ToList();
                return Shell is not null && await Shell.ConfirmAsync(new ConfirmRequest(
                    $"执行工作流「{binding.Name}」？",
                    $"将按顺序执行 {steps.Count} 个本地动作。可在 设置 › 安全与数据 中关闭此确认。",
                    items, "执行"));
            };
        }

        ShowCapsule(FeedbackMode.Typing, pattern, command.Name + " · 正在执行",
            direction.Length > 0 ? Directions.Icon(direction) : null);
        var fired = command;
        var result = await _executor.RunAsync(request, _hook.InjectionBlocked, confirm);
        // When Repeat expands, result.Command is the underlying action.
        _effects.Apply(result, source, fired);
    }

    private void SetPhase(LivePhase phase)
    {
        Phase = phase;
        _phaseChangedMs = NowMs;
    }

    private void ResetLive()
    {
        Phase = LivePhase.Idle;
        PressStartMs = null;
        LastReleaseMs = null;
        LastDurationMs = null;
        LastPulse = null;
        HitPattern = "";
        HitCommand = null;
        PhaseMessage = "";
    }

    // ---------- execution ----------

    public Task ExecuteGesture(GestureBinding gesture) => Dispatch(
        ExecutionSource.Gesture, "", ContextSnapshot.Desktop,
        new CommandBinding { Name = gesture.Name, ActionKind = gesture.ActionKind, Target = gesture.Target, Pattern = "" },
        gesture.Direction);

    public Task Execute(CommandBinding command, string pattern, string direction = "") =>
        Dispatch(ExecutionSource.Rhythm, pattern, _hook.SessionContext, command, direction);

    /// <summary>Commands-view Test button: same activation rules as a real run (spec 5.6).</summary>
    public Task TestCommand(CommandBinding command)
    {
        var context = ContextTarget.AtCursor();
        _effects.TrackRecentApp(context);
        return Dispatch(ExecutionSource.Test, command.Pattern, context, command);
    }

    public void ShowCapsule(FeedbackMode mode, string pattern, string text, IconKind? icon = null, bool reminding = false, string? contextLine = null)
    {
        if (!Settings.FloatingFeedback) return;
        try { Feedback.ShowState(mode, pattern, text, icon, reminding, contextLine); }
        catch (Exception ex) { AddLog("状态胶囊显示失败: " + ex.Message, "", LogTone.Err); }
    }

    public string CapsuleLineFor(ExecutionResult result) =>
        CapsuleContextLine(result.Context, result.Command, result.Meaning, result.Reminding && Settings.NewMeaningReminder);

    internal void AddActivityPublic(ActivityEntry entry) => AddActivity(entry);
    internal void RaiseStats() => StatsChanged?.Invoke();

    public void PreviewCapsule()
    {
        if (!Settings.FloatingFeedback) { Shell?.Toast("请先启用状态胶囊", IconKind.Info); return; }
        Feedback.ShowState(FeedbackMode.Matched, ".-", "打开计算器 · 已发起", null);
    }

    public void PreviewHint()
    {
        if (!Settings.FloatingFeedback) { Shell?.Toast("请先启用状态胶囊", IconKind.Info); return; }
        Feedback.ShowState(FeedbackMode.Armed, "", "用左键敲节奏；不敲直接松开即普通右键", null);
        var (shown, hidden) = RhythmHints.ForContext(Settings.Commands, ContextNames.Desktop, Settings.ContextEnabled, "", 6);
        Feedback.ShowHint("", shown.Select(c => (c.Pattern, c.Name, HintTag(c, ContextNames.Desktop))).ToList(), hidden);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            if (Phase == LivePhase.Idle && _triggerHeldMs is null) Feedback.HideNow();
        };
        timer.Start();
    }

    // ---------- helpers ----------

    public static List<WorkflowStep> ParseSteps(string json)
    {
        try { return JsonSerializer.Deserialize<List<WorkflowStep>>(json) ?? new(); }
        catch { return new(); }
    }

    public static string TargetText(CommandBinding command) => command.ActionKind == ActionKind.Workflow
        ? $"{ParseSteps(command.Target).Count} 个步骤"
        : command.Target;

    public static string? ValidateTarget(ActionKind kind, string target)
    {
        target = target.Trim();
        if (target.Length == 0) return "目标不能为空";
        if (kind == ActionKind.Url && (!Uri.TryCreate(target, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)))
            return "网址只支持 http:// 或 https://";
        if (kind == ActionKind.Shortcut && !KeyChord.TryParse(target, out _))
            return "无法识别的快捷键，例如 Ctrl+Shift+T、Win+D、VolumeUp";
        return null;
    }

    public string? ConflictName(string pattern, CommandBinding? except)
    {
        var check = Settings.Commands.Check(pattern, except?.Scope ?? "", except?.Id, except?.Pinned ?? false, except?.ActionKind ?? ActionKind.Shortcut);
        return check.Issue is PatternIssue.None or PatternIssue.ReplacesGlobal ? null
            : check.Message.Length > 0 ? check.Message
            : Settings.Commands.FirstOrDefault(c => c.Pattern == pattern && c.Id != except?.Id)?.Name;
    }

    private void EnsureCommandIds()
    {
        foreach (var command in Settings.Commands)
        {
            if (string.IsNullOrEmpty(command.Id)) command.Id = Guid.NewGuid().ToString("N");
            command.Scope ??= "";
            PinPolicy.Apply(command);
        }
        Settings.ContextApps ??= new();
        Settings.CommandsToday ??= new();
        Settings.History ??= new();
    }

    public void AddLog(string text, string note, LogTone tone)
    {
        Log.Insert(0, new LogEntry(DateTime.Now, text, note, tone));
        while (Log.Count > LogLimit) Log.RemoveAt(Log.Count - 1);
    }

    private void AddActivity(ActivityEntry entry)
    {
        Activity.Insert(0, entry);
        while (Activity.Count > ActivityLimit) Activity.RemoveAt(Activity.Count - 1);
    }

    // ---------- statistics ----------

    public double WeeklySeconds => Settings.ActiveSecondsThisWeek + Math.Max(0, _uptime.Elapsed.TotalSeconds - _accountedSeconds);

    private void AccountUptime()
    {
        double elapsed = _uptime.Elapsed.TotalSeconds;
        Settings.ActiveSecondsThisWeek += Math.Max(0, elapsed - _accountedSeconds);
        _accountedSeconds = elapsed;
    }

    /// <summary>Resets daily and weekly counters on rollover (weeks start Monday). Returns true when anything changed.</summary>
    private bool NormalizeStatistics()
    {
        bool changed = false;
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        if (Settings.StatisticsDate != today)
        {
            Settings.History ??= new();
            if (Settings.StatisticsDate.Length > 0 && (Settings.ExecutionsToday > 0 || Settings.MissesToday > 0 || Settings.ActivationFailedToday > 0))
            {
                Settings.History.Add(new DailyStat
                {
                    Date = Settings.StatisticsDate, Executions = Settings.ExecutionsToday, Misses = Settings.MissesToday,
                    ActivationFailed = Settings.ActivationFailedToday,
                    Commands = Settings.CommandsToday ?? new()
                });
                if (Settings.History.Count > 90) Settings.History.RemoveRange(0, Settings.History.Count - 90);
            }
            Settings.StatisticsDate = today;
            Settings.ExecutionsToday = 0;
            Settings.MissesToday = 0;
            Settings.ActivationFailedToday = 0;
            Settings.CommandsToday = new();
            changed = true;
        }
        var weekStart = DateTime.Today.AddDays(-(((int)DateTime.Today.DayOfWeek + 6) % 7)).ToString("yyyy-MM-dd");
        if (Settings.StatisticsWeekStart != weekStart)
        {
            AccountUptime();
            Settings.StatisticsWeekStart = weekStart;
            Settings.ActiveSecondsThisWeek = 0;
            changed = true;
        }
        return changed;
    }

    public void Dispose()
    {
        _timer.Stop();
        Save();
        _hook.Enabled = false;
        _hook.Dispose();
        Feedback.Close();
    }
}
