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
    private double _accountedSeconds;
    private long _lastStatsSecond = -1;
    private bool _completedDuringUp;
    private long? _rawDown;

    public AppSettings Settings { get; private set; }
    public FeedbackWindow Feedback { get; } = new();
    public IShell? Shell { get; set; }
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
        string? loadError = null;
        try { Settings = _store.Load(); }
        catch (Exception ex) { loadError = ex.Message; Settings = new AppSettings(); }
        if (File.Exists(_store.FilePath)) SavedAt = File.GetLastWriteTime(_store.FilePath).ToString("HH:mm:ss");
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
        CancelInput(quiet: true);
        Settings = imported;
        NormalizeStatistics();
        ApplySettings();
        _hook.AutoSide = Settings.DetectedSideButton;
        SetStartup(Settings.StartupEnabled);
        Save();
        AddLog("已导入配置并立即生效", "", LogTone.Hit);
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
            if (fresh) ShowCapsule(FeedbackMode.Armed, "", _engine.GapSuspended ? "松开右键即执行" : UsesMiddle ? "指令模式已激活" : "正在输入…");
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
            var exact = Settings.Commands.FirstOrDefault(c => c.Pattern == Sequence);
            ShowCapsule(FeedbackMode.Typing, Sequence, exact?.Name ?? "正在输入…");
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
        var (shown, hidden) = RhythmHints.For(Settings.Commands, prefix, 6);
        try { Feedback.ShowHint(prefix, shown.Select(c => (c.Pattern, c.Name)).ToList(), hidden); }
        catch (Exception ex) { AddLog("节奏提示显示失败: " + ex.Message, "", LogTone.Err); }
    }

    private char? Classify(double duration)
    {
        var s = Settings;
        if (duration < 35 || duration > s.LongMaxMs) return 'x';
        return duration <= s.ShortMaxMs ? '.' : duration >= s.LongMinMs ? '-' : 'x';
    }

    private void OnCompleted(Recognition result)
    {
        _completedDuringUp = true;
        _hook.FinishSequence();
        LastReleaseMs = null;
        HitPattern = result.Pattern;
        HitCommand = result.Command;
        if (result.Command is { } command)
        {
            SetPhase(LivePhase.Matched);
            _ = Execute(command, result.Pattern);
        }
        else
        {
            SetPhase(LivePhase.Miss);
            Settings.MissesToday++;
            AddActivity(new ActivityEntry(DateTime.Now, ActivityType.Miss, "未找到匹配的指令", result.Pattern));
            ShowCapsule(FeedbackMode.Failed, result.Pattern, "未找到匹配指令");
            AddLog("没有匹配的指令: " + result.Pattern, "", LogTone.Err);
            SaveSoon();
            StatsChanged?.Invoke();
        }
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

    public Task ExecuteGesture(GestureBinding gesture) => Execute(
        new CommandBinding { Name = gesture.Name, ActionKind = gesture.ActionKind, Target = gesture.Target }, "", gesture.Direction);

    public async Task Execute(CommandBinding command, string pattern, string direction = "")
    {
        bool isWorkflow = command.ActionKind == ActionKind.Workflow;
        IconKind? icon = direction.Length > 0 ? Directions.Icon(direction) : null;
        if (isWorkflow && Settings.ConfirmWorkflows)
        {
            var steps = ParseSteps(command.Target);
            var items = steps.Select(s => $"{Loc.Kind(s.Kind)}  {s.Target}{(s.DelayMs > 0 ? $"  (等待 {s.DelayMs} ms)" : "")}").ToList();
            bool ok = Shell is not null && await Shell.ConfirmAsync(new ConfirmRequest(
                $"执行工作流「{command.Name}」？",
                $"将按顺序执行 {steps.Count} 个本地动作。可在 设置 › 安全与数据 中关闭此确认。",
                items, "执行"));
            if (!ok)
            {
                Feedback.HideNow();
                AddLog("用户取消了工作流执行", "", LogTone.None);
                return;
            }
        }
        ShowCapsule(FeedbackMode.Typing, pattern, command.Name + " · 正在执行", icon);
        try
        {
            if (command.ActionKind == ActionKind.Shortcut && _hook.InjectionBlocked)
                throw new InvalidOperationException("模拟输入被系统拦截，快捷键无法发送");
            await Task.Run(() => ActionRunner.Run(command, isWorkflow));
            Settings.ExecutionsToday++;
            Settings.CommandsToday ??= new();
            Settings.CommandsToday[command.Name] = Settings.CommandsToday.GetValueOrDefault(command.Name) + 1;
            AddActivity(new ActivityEntry(DateTime.Now, direction.Length > 0 ? ActivityType.Gesture : ActivityType.Ok, command.Name,
                command.Pattern, command.ActionKind, TargetText(command), direction));
            AddLog("已请求执行：" + command.Name, "", LogTone.Hit);
            if (Settings.SuccessSound) System.Media.SystemSounds.Asterisk.Play();
            ShowCapsule(FeedbackMode.Matched, pattern, command.Name + " · 已发起", icon);
        }
        catch (Exception ex)
        {
            string message = ex.GetBaseException().Message;
            AddActivity(new ActivityEntry(DateTime.Now, ActivityType.Failed, "执行失败：" + command.Name, command.Pattern, command.ActionKind, message, direction));
            AddLog($"执行失败：{command.Name} · {message}", "", LogTone.Err);
            ShowCapsule(FeedbackMode.Failed, pattern, "执行失败 · " + message, icon);
            Shell?.Toast("执行失败：" + message, IconKind.Alert);
        }
        SaveSoon();
        StatsChanged?.Invoke();
    }

    public void ShowCapsule(FeedbackMode mode, string pattern, string text, IconKind? icon = null)
    {
        if (!Settings.FloatingFeedback) return;
        try { Feedback.ShowState(mode, pattern, text, icon); }
        catch (Exception ex) { AddLog("状态胶囊显示失败: " + ex.Message, "", LogTone.Err); }
    }

    public void PreviewCapsule()
    {
        if (!Settings.FloatingFeedback) { Shell?.Toast("请先启用状态胶囊", IconKind.Info); return; }
        Feedback.ShowState(FeedbackMode.Matched, ".-", "打开计算器 · 已发起", null);
    }

    public void PreviewHint()
    {
        if (!Settings.FloatingFeedback) { Shell?.Toast("请先启用状态胶囊", IconKind.Info); return; }
        Feedback.ShowState(FeedbackMode.Armed, "", "用左键敲节奏；不敲直接松开即普通右键", null);
        var (shown, hidden) = RhythmHints.For(Settings.Commands, "", 6);
        Feedback.ShowHint("", shown.Select(c => (c.Pattern, c.Name)).ToList(), hidden);
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
        => Settings.Commands.FirstOrDefault(c => c.Pattern == pattern && !ReferenceEquals(c, except))?.Name;

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
            if (Settings.StatisticsDate.Length > 0 && (Settings.ExecutionsToday > 0 || Settings.MissesToday > 0))
            {
                Settings.History.Add(new DailyStat
                {
                    Date = Settings.StatisticsDate, Executions = Settings.ExecutionsToday, Misses = Settings.MissesToday,
                    Commands = Settings.CommandsToday ?? new()
                });
                if (Settings.History.Count > 90) Settings.History.RemoveRange(0, Settings.History.Count - 90);
            }
            Settings.StatisticsDate = today;
            Settings.ExecutionsToday = 0;
            Settings.MissesToday = 0;
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
