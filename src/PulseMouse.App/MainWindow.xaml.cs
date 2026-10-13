using System.Collections.ObjectModel;
using System.Text.Json;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Media;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using CommandBinding = PulseMouse.Core.CommandBinding;
using PulseMouse.Actions;
using PulseMouse.Core;
using PulseMouse.Infrastructure;
using PulseMouse.Windows;
using Forms = System.Windows.Forms;
namespace PulseMouse.App;
public partial class MainWindow : Window
{
    private readonly SettingsStore _store = new();
    private readonly AppSettings _settings;
    private readonly ObservableCollection<CommandBinding> _commands;
    private readonly RhythmEngine _engine;
    private readonly MouseHook _hook = new();
    private readonly DispatcherTimer _timer;
    private readonly Forms.NotifyIcon _tray;
    private CommandBinding? _editing;
    private readonly ObservableCollection<GestureBinding> _gestures;
    private FeedbackWindow? _feedback;
    private bool _exiting;
    private readonly ObservableCollection<string> _events = new();
    private long? _lastDown;
    private Border[]? _pulseBars;
    private readonly Stopwatch _sessionUptime = Stopwatch.StartNew();
    private double _accountedSessionSeconds;
    private long _lastStatsUiSecond = -1;
    private void TitleDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2) { WindowMaximize(sender, new RoutedEventArgs()); return; }
        if (e.ChangedButton == MouseButton.Left) DragMove();
    }
    private void WindowMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void WindowMaximize(object sender, RoutedEventArgs e) => WindowState = this.WindowState == System.Windows.WindowState.Maximized ? System.Windows.WindowState.Normal : System.Windows.WindowState.Maximized;
    private void WindowClose(object sender, RoutedEventArgs e) => Hide();
    private void BrowseTargetClick(object sender, RoutedEventArgs e)
    {
        if (KindBox.SelectedIndex == (int)ActionKind.Folder)
        {
            using var dialog = new Forms.FolderBrowserDialog { Description = "选择要打开的文件夹", ShowNewFolderButton = false };
            if (dialog.ShowDialog() == Forms.DialogResult.OK) TargetBox.Text = dialog.SelectedPath;
            return;
        }
        if (KindBox.SelectedIndex != (int)ActionKind.Application) { StatusText.Text = "当前类型无需应用文件选择器"; return; }
        using var picker = new Forms.OpenFileDialog { Title = "选择应用程序或快捷方式", Filter = "应用或快捷方式 (*.exe;*.lnk)|*.exe;*.lnk|所有文件 (*.*)|*.*", CheckFileExists = true };
        if (picker.ShowDialog() == Forms.DialogResult.OK)
        {
            TargetBox.Text = picker.FileName;
            if (string.IsNullOrWhiteSpace(NameBox.Text)) NameBox.Text = Path.GetFileNameWithoutExtension(picker.FileName);
        }
    }
    private void PreviewCapsuleClick(object sender, RoutedEventArgs e) { _feedback ??= new FeedbackWindow(); _feedback.ShowState("· —", "VS Code · 已匹配", FeedbackMode.Matched); }
    private void DisplaySequence(string sequence)
    {
        if (_settings is { FloatingFeedback: true } && !string.IsNullOrEmpty(sequence))
        {
            _feedback ??= new FeedbackWindow();
            string name = _commands?.FirstOrDefault(c => c.Pattern == sequence)?.Name ?? "正在输入…";
            _feedback.ShowState(sequence.Replace('.', '·').Replace('-', '—'), name, FeedbackMode.Typing);
        }
        SequenceText.Text = string.IsNullOrEmpty(sequence) ? "等待输入 · / —" : sequence.Replace('.', '·').Replace('-', '—');
        if (_pulseBars is null) return;
        for (var i = 0; i < _pulseBars.Length; i++)
        {
            bool active = i < sequence.Length;
            _pulseBars[i].Height = active ? sequence[i] == '-' ? 34 : 15 : 12;
            _pulseBars[i].Background = active ? (System.Windows.Media.Brush)FindResource("AccentBrush") : new SolidColorBrush(System.Windows.Media.Color.FromRgb(61, 83, 100));
        }
    }
    public MainWindow()
    {
        InitializeComponent();
        _pulseBars = new[] { PulseBar1, PulseBar2, PulseBar3, PulseBar4, PulseBar5 };
        try { _settings = _store.Load(); }
        catch (Exception ex) { System.Windows.MessageBox.Show(ex.Message + "\n将加载默认配置。", "配置读取失败"); _settings = new AppSettings(); }
        _commands = new ObservableCollection<CommandBinding>(_settings.Commands);
        NormalizeStatistics();
        _gestures = new ObservableCollection<GestureBinding>(_settings.Gestures ?? new());
        GestureGrid.ItemsSource = _gestures;
        GesturesBox.IsChecked = _settings.GesturesEnabled;
        GestureDistanceBox.Text = _settings.GestureMinDistancePx.ToString();
        FloatingBox.IsChecked = _settings.FloatingFeedback;
        ConfirmWorkflowsBox.IsChecked = _settings.ConfirmWorkflows;
        StartupBox.IsChecked = _settings.StartupEnabled;
        SuccessSoundBox.IsChecked = _settings.SuccessSound;
        ChineseLanguageBox.IsChecked = _settings.UiLanguage != "en-US";
        EnglishLanguageBox.IsChecked = _settings.UiLanguage == "en-US";
        CapsulePositionBox.SelectedIndex = _settings.CapsulePosition switch { "Left" => 0, "Right" => 2, _ => 1 };
        CapsuleBreathingBox.IsChecked = _settings.CapsuleBreathing;
        _feedback = new FeedbackWindow();
        _feedback.Configure(_settings.CapsulePosition, _settings.CapsuleBreathing);
        ApplyLanguage();
        AiUrlBox.Text = _settings.AiProviderUrl;
        _hook.GesturesEnabled = _settings.GesturesEnabled;
        _hook.GestureMinDistancePx = _settings.GestureMinDistancePx;
        _hook.GestureEvent += dir => Dispatcher.BeginInvoke(new Action(() => OnGesture(dir)));
        CommandGrid.ItemsSource = _commands;
        EnabledBox.IsChecked = _settings.Enabled;
        MouseButtonBox.SelectedIndex = (int)_settings.InputMode;
        _hook.Mode = _settings.InputMode;
        _hook.MiddleHoldMs = _settings.MiddleHoldMs;
        DiagnosticList.ItemsSource = _events;
        _engine = new RhythmEngine(() => _settings);
        _engine.SequenceChanged += DisplaySequence;
        _engine.Completed += result => { _hook.FinishSequence(); OnCompleted(result); };
        _hook.StateChanged += message => Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => { StatusText.Text = message; Log(message); if (message.Contains("取消") || message.Contains("超时") || message.Contains("已结束")) _feedback?.Hide(); if (_settings.FloatingFeedback && message.Contains("激活")) { _feedback ??= new FeedbackWindow(); _feedback.ShowState("●", "指令模式已激活", FeedbackMode.Armed); } }));
        _hook.ButtonEvent += (down, ticks) =>
        {
            // Hook callback: only enqueue, never execute work or modify WPF UI here.
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                long ms = ticks * 1000 / Stopwatch.Frequency;
                if (down) _engine.Down(ms); else _engine.Up(ms);
            }));
        };
        _hook.RawButtonEvent += ev =>
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                var name = ev.Button;
                var ms = ev.Timestamp * 1000 / Stopwatch.Frequency;
                if (ev.IsDown) _lastDown = ev.Timestamp;
                var duration = !ev.IsDown && _lastDown is long start
                    ? $"，时长 {(ev.Timestamp - start) * 1000.0 / Stopwatch.Frequency:F0} ms" : "";
                if (!ev.IsDown) _lastDown = null;
                var line = $"{DateTime.Now:HH:mm:ss.fff} {name} {(ev.IsDown ? "按下" : "释放")}{duration} {(ev.Consumed ? "[识别]" : "[透传]")} {ev.Note}";
                LastEventText.Text = "最后事件：" + line;
                _events.Insert(0, line);
                while (_events.Count > 60) _events.RemoveAt(_events.Count - 1);
            }));
        };
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(40), DispatcherPriority.Background,
            (_, _) => { var ticks = Stopwatch.GetTimestamp(); _hook.CheckTimeout(ticks); _engine.Tick(ticks * 1000 / Stopwatch.Frequency); if (_sessionUptime.ElapsedMilliseconds / 1000 != _lastStatsUiSecond) { _lastStatsUiSecond = _sessionUptime.ElapsedMilliseconds / 1000; UpdateStatisticsUi(); } }, Dispatcher);
        _timer.Start();
        _tray = new Forms.NotifyIcon
        {
            Text = "PulseMouse",
            Icon = System.Drawing.SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = new Forms.ContextMenuStrip()
        };
        _tray.ContextMenuStrip.Items.Add("打开 PulseMouse", null, (_, _) => RestoreWindow());
        _tray.ContextMenuStrip.Items.Add("暂停 / 恢复", null, (_, _) => Dispatcher.Invoke(() => EnabledBox.IsChecked = !_hook.Enabled));
        _tray.ContextMenuStrip.Items.Add("退出", null, (_, _) => Dispatcher.Invoke(Exit));
        _tray.DoubleClick += (_, _) => Dispatcher.Invoke(RestoreWindow);
        Loaded += (_, _) => { SelectPage("overview"); InstallHook(); UpdateStatisticsUi(); };
        Closing += (_, e) => { if (!_exiting) { e.Cancel = true; Hide(); } };
    }
    private void InstallHook()
    {
        try
        {
            _hook.Install();
            _hook.Enabled = EnabledBox.IsChecked == true;
            HookStatusText.Text = $"Hook: 已安装 | {_hook.ActiveInput} | {(_hook.Enabled ? "监听中" : "暂停")}";
            _events.Insert(0, $"{DateTime.Now:HH:mm:ss} Hook 安装成功；可按 X1/X2 检测侧键，或长按中键后用左键输入");
        }
        catch (Exception ex)
        {
            _hook.Enabled = false;
            EnabledBox.IsChecked = false;
            HookStatusText.Text = "Hook: 安装失败 - " + ex.Message;
            _events.Insert(0, HookStatusText.Text);
        }
    }
    private void ReinstallHookClick(object sender, RoutedEventArgs e)
    {
        _hook.Dispose(); _hook.ResetInput(); _engine.Reset(); InstallHook();
    }
    private void MouseButtonChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (_settings is null || _hook is null || MouseButtonBox is null) return;
        _settings.InputMode = (MouseInputMode)Math.Clamp(MouseButtonBox.SelectedIndex, 0, 3);
        _hook.Mode = _settings.InputMode;
        _hook.ResetInput();
        _engine?.Reset();
        if (HookStatusText is not null) HookStatusText.Text = $"Hook: {(_hook.IsInstalled ? "已安装" : "未安装")} | {_hook.ActiveInput}";
        if (_commands is not null && _gestures is not null) SaveConfiguration();
    }
    private void CancelInputClick(object sender, RoutedEventArgs e)
    {
        _hook.CancelInput(); _engine.Reset(); _feedback?.Hide();
        StatusText.Text = "已取消输入，恢复普通鼠标操作";
    }
    private void TestActionClick(object sender, RoutedEventArgs e)
    {
        if (CommandGrid.SelectedItem is not CommandBinding command)
        { StatusText.Text = "请先在指令表中选中要测试的动作"; return; }
        OnCompleted(new Recognition(command.Pattern, command));
    }
    private void OnCompleted(Recognition result)
    {
        if (result.Command is not { } command) { StatusText.Text = $"没有匹配的指令: {result.Pattern}"; if (_settings.FloatingFeedback) { _feedback ??= new FeedbackWindow(); _feedback.ShowState(result.Pattern, "未找到匹配指令", FeedbackMode.Failed); } return; }
        StatusText.Text = $"已识别 {result.Pattern} → {command.Name}";
        ExecuteCommand(command); 
    }
    private void ExecuteCommand(CommandBinding command)
    {
        if (command.ActionKind == ActionKind.Workflow && _settings.ConfirmWorkflows &&
            System.Windows.MessageBox.Show($"确认执行工作流：{command.Name}？", "PulseMouse 安全确认", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        StatusText.Text = $"正在执行：{command.Name}";
        if (_settings.FloatingFeedback)
        {
            _feedback ??= new FeedbackWindow();
            _feedback.ShowState("…", command.Name + " · 正在执行", FeedbackMode.Typing);
        }
        _ = Task.Run(() => ActionRunner.Run(command, command.ActionKind == ActionKind.Workflow))
            .ContinueWith(task => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (task.IsFaulted)
                {
                    var message = task.Exception?.GetBaseException().Message ?? "未知错误";
                    StatusText.Text = "执行失败：" + message;
                    Log(StatusText.Text);
                    if (_settings.FloatingFeedback) _feedback?.ShowState("!", "执行失败 · " + message, FeedbackMode.Failed);
                }
                else
                {
                    StatusText.Text = "已请求执行：" + command.Name;
                    _settings.ExecutionsToday++;
                    UpdateStatisticsUi();
                    if (_settings.SuccessSound) System.Media.SystemSounds.Asterisk.Play();
                    if (_settings.FloatingFeedback) _feedback?.ShowState("✓", command.Name + " · 已发起", FeedbackMode.Matched);
                }
            })), TaskScheduler.Default);
    }
    private void EnabledChanged(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button)
        {
            EnabledBox.IsChecked = EnabledBox.IsChecked != true;
            return;
        }
        if (_hook is null || _settings is null) return;
        _hook.Enabled = EnabledBox.IsChecked == true;
        if (!_hook.Enabled) _feedback?.Hide();
        _settings.Enabled = _hook.Enabled;
        _engine?.Reset();
        StatusText.Text = _hook.Enabled ? "监听已启用" : "监听已暂停，正常鼠标操作已恢复";
        _hook.ResetInput();
        if (HookStatusText is not null) HookStatusText.Text = $"Hook: {(_hook.IsInstalled ? "已安装" : "未安装")} | {_hook.ActiveInput} | {(_hook.Enabled ? "监听中" : "暂停")}";
        SaveConfiguration();
    }
    private void ToggleListeningClick(object sender, RoutedEventArgs e)
        => EnabledBox.IsChecked = EnabledBox.IsChecked != true;
    private void Log(string message) { _events.Insert(0, $"{DateTime.Now:HH:mm:ss.fff} {message}"); while (_events.Count > 60) _events.RemoveAt(_events.Count - 1); }
    private void SaveConfiguration()
    {
        try {
        _settings.Gestures = _gestures.ToList();
        _settings.GesturesEnabled = GesturesBox.IsChecked == true;
        _settings.GestureMinDistancePx = int.TryParse(GestureDistanceBox.Text, out int distance) ? Math.Clamp(distance, 25, 500) : 75;
        _settings.FloatingFeedback = FloatingBox.IsChecked == true;
        _settings.ConfirmWorkflows = ConfirmWorkflowsBox.IsChecked == true;
        _settings.StartupEnabled = StartupBox.IsChecked == true;
        _settings.SuccessSound = SuccessSoundBox.IsChecked == true;
        _settings.UiLanguage = EnglishLanguageBox.IsChecked == true ? "en-US" : "zh-CN";
        _settings.CapsulePosition = CapsulePositionBox.SelectedIndex switch { 0 => "Left", 2 => "Right", _ => "Center" };
        _settings.CapsuleBreathing = CapsuleBreathingBox.IsChecked == true;
        var elapsed = _sessionUptime.Elapsed.TotalSeconds;
        _settings.ActiveSecondsThisWeek += Math.Max(0, elapsed - _accountedSessionSeconds);
        _accountedSessionSeconds = elapsed;
        ApplyStartupSetting(_settings.StartupEnabled);
        _feedback?.Configure(_settings.CapsulePosition, _settings.CapsuleBreathing);
        _settings.AiProviderUrl = AiUrlBox.Text.Trim();
        _hook.GesturesEnabled = _settings.GesturesEnabled;
        _hook.GestureMinDistancePx = _settings.GestureMinDistancePx;
        _settings.Commands = _commands.ToList(); _store.Save(_settings); FooterText.Text = "已保存 " + DateTime.Now.ToString("HH:mm:ss"); }
        catch (Exception ex) { System.Windows.MessageBox.Show(ex.Message, "保存失败"); }
    }
    private void SaveClick(object sender, RoutedEventArgs e)
    {
        var name = NameBox.Text.Trim(); var pattern = PatternBox.Text.Trim().Replace('·','.').Replace('—','-'); var target = TargetBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(target) || pattern.Length is < 1 or > 5 || pattern.Any(c => c != '.' && c != '-'))
        { System.Windows.MessageBox.Show("请填写名称、目标及 1–5 位 ./- 节奏。"); return; }
        if (_commands.Any(c => c != _editing && c.Pattern == pattern)) { System.Windows.MessageBox.Show("该节奏已经被占用。"); return; }
        var kind = (ActionKind)KindBox.SelectedIndex;
        if (kind == ActionKind.Url && (!Uri.TryCreate(target, UriKind.Absolute, out var u) || (u.Scheme != "http" && u.Scheme != "https")))
        { System.Windows.MessageBox.Show("网址只支持 http:// 或 https:// 。"); return; }
        if (kind == ActionKind.Workflow)
        {
            try { var steps = JsonSerializer.Deserialize<List<WorkflowStep>>(target); if (steps is null || steps.Count is < 1 or > 12 || steps.Any(x=>x.Kind==ActionKind.Workflow || x.DelayMs is < 0 or > 5000 || string.IsNullOrWhiteSpace(x.Target))) throw new Exception(); }
            catch { System.Windows.MessageBox.Show("工作流需填写 JSON 数组，包含 Kind(0应用/1文件夹/2网址)、Target、DelayMs，最多12步。", "工作流格式错误"); return; }
        }
        if (_editing is null) _commands.Add(new CommandBinding { Name = name, Pattern = pattern, Target = target, ActionKind = kind });
        else { _editing.Name = name; _editing.Pattern = pattern; _editing.Target = target; _editing.ActionKind = kind; CommandGrid.Items.Refresh(); }
        _editing = null; NameBox.Clear(); PatternBox.Clear(); TargetBox.Clear(); SaveConfiguration();
    }
    private void AddDotClick(object sender, RoutedEventArgs e) => AppendPattern('.');
    private void AddDashClick(object sender, RoutedEventArgs e) => AppendPattern('-');
    private void ClearPatternClick(object sender, RoutedEventArgs e) => PatternBox.Clear();
    private void AppendPattern(char pulse)
    {
        var value = PatternBox.Text.Trim().Replace('·', '.').Replace('—', '-');
        if (value.Length >= 5) { StatusText.Text = "节奏最多 5 位"; return; }
        PatternBox.Text = value + pulse;
        PatternBox.CaretIndex = PatternBox.Text.Length;
    }
    private void EditClick(object sender, RoutedEventArgs e)
    {
        if (CommandGrid.SelectedItem is not CommandBinding b) return;
        _editing = b; NameBox.Text = b.Name; PatternBox.Text = b.Pattern; TargetBox.Text = b.Target; KindBox.SelectedIndex = (int)b.ActionKind;
    }
    private void DeleteClick(object sender, RoutedEventArgs e)
    {
        if (CommandGrid.SelectedItem is CommandBinding b) { _commands.Remove(b); if (_editing == b) _editing = null; SaveConfiguration(); }
    }
    private void OnGesture(string direction)
    {
        Log($"识别中键方向手势 {direction}");
        var g = _gestures.FirstOrDefault(x => x.Direction.Equals(direction, StringComparison.OrdinalIgnoreCase));
        if (g is null) { StatusText.Text = "未配置手势：" + direction; return; }
        StatusText.Text = $"手势 {direction} → {g.Name}";
        ExecuteCommand(new CommandBinding { Name = g.Name, ActionKind = g.ActionKind, Target = g.Target });
    }
    private void ShowFeedback(string label)
    {
        if (!_settings.FloatingFeedback) return;
        try { _feedback ??= new FeedbackWindow(); _feedback.ShowState("✓", label + " · 已执行", FeedbackMode.Matched); }
        catch (Exception ex) { Log("浮层显示失败: " + ex.Message); }
    }
    private void AddGestureClick(object sender, RoutedEventArgs e)
    {
        var direction = ((ComboBoxItem)GestureDirectionBox.SelectedItem).Content.ToString()!;
        var name = GestureNameBox.Text.Trim(); var target = GestureTargetBox.Text.Trim();
        if (name.Length == 0 || target.Length == 0) { StatusText.Text = "手势名称和目标不能为空"; return; }
        var type = target.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ? ActionKind.Url : Directory.Exists(target) ? ActionKind.Folder : ActionKind.Application;
        var current = _gestures.FirstOrDefault(x => x.Direction == direction);
        if (current != null) _gestures.Remove(current);
        _gestures.Add(new GestureBinding { Direction = direction, Name=name, Target=target, ActionKind=type }); SaveConfiguration();
    }
    private void DeleteGestureClick(object sender, RoutedEventArgs e)
    { if (GestureGrid.SelectedItem is GestureBinding g) { _gestures.Remove(g); SaveConfiguration(); } }
    private void OpenAiClick(object sender, RoutedEventArgs e)
    {
        SaveConfiguration();
        if (!Uri.TryCreate(_settings.AiProviderUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        { StatusText.Text = "AI 网址必须为 HTTPS"; return; }
        _ = Task.Run(() => ActionRunner.Run(new CommandBinding { ActionKind=ActionKind.Url, Target=uri.ToString() }));
    }
    private void CopyWorkflowClick(object sender, RoutedEventArgs e)
    {
        var example = new[] { new WorkflowStep { Kind=ActionKind.Application, Target="notepad.exe", DelayMs=0 }, new WorkflowStep { Kind=ActionKind.Url, Target="https://example.com", DelayMs=500 } };
        System.Windows.Clipboard.SetText(JsonSerializer.Serialize(example, new JsonSerializerOptions { WriteIndented = true }));
        StatusText.Text = "已复制工作流 JSON；在指令映射中选择工作流类型并粘贴至目标";
    }
    private void ExportSettingsClick(object sender, RoutedEventArgs e)
    {
        SaveConfiguration();
        using var dialog = new Forms.SaveFileDialog { Title = "导出 PulseMouse 配置", Filter = "JSON 配置 (*.json)|*.json", FileName = $"PulseMouse-backup-{DateTime.Now:yyyyMMdd}.json" };
        if (dialog.ShowDialog() != Forms.DialogResult.OK) return;
        File.Copy(_store.FilePath, dialog.FileName, true);
        StatusText.Text = "配置已导出";
    }
    private void ImportSettingsClick(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.OpenFileDialog { Title = "导入 PulseMouse 配置", Filter = "JSON 配置 (*.json)|*.json", CheckFileExists = true };
        if (dialog.ShowDialog() != Forms.DialogResult.OK) return;
        try
        {
            var imported = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(dialog.FileName));
            if (imported is null) throw new InvalidDataException("配置内容为空");
            _store.Save(imported);
            System.Windows.MessageBox.Show("配置已导入。PulseMouse 将退出，请重新打开以应用全部设置。", "导入完成", MessageBoxButton.OK, MessageBoxImage.Information);
            Exit();
        }
        catch (Exception ex) { System.Windows.MessageBox.Show("无法导入配置：" + ex.Message, "导入失败", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
    private void LanguageChanged(object sender, RoutedEventArgs e)
    {
        if (_settings is null || ChineseLanguageBox is null || EnglishLanguageBox is null) return;
        if (sender == ChineseLanguageBox && ChineseLanguageBox.IsChecked == true) EnglishLanguageBox.IsChecked = false;
        if (sender == EnglishLanguageBox && EnglishLanguageBox.IsChecked == true) ChineseLanguageBox.IsChecked = false;
        if (ChineseLanguageBox.IsChecked != true && EnglishLanguageBox.IsChecked != true) ChineseLanguageBox.IsChecked = true;
        ApplyLanguage();
    }
    private void ApplyLanguage()
    {
        bool english = EnglishLanguageBox?.IsChecked == true;
        NavOverview.Content = english ? "●    Dashboard" : "●    仪表盘";
        NavCommands.Content = english ? "⌘    Commands" : "⌘    指令库";
        NavGestures.Content = english ? "↗    Gestures" : "↗    方向手势";
        NavWorkflows.Content = english ? "◇    Workflows" : "◇    工作流";
        NavSettings.Content = english ? "⚙    Settings" : "⚙    设置";
        if (IsLoaded) SelectPage(DashboardPage.Visibility == Visibility.Visible ? "overview" : CommandsPage.Visibility == Visibility.Visible ? "commands" : GesturesPage.Visibility == Visibility.Visible ? "gestures" : WorkflowsPage.Visibility == Visibility.Visible ? "workflows" : "settings");
    }
    private void NormalizeStatistics()
    {
        var today = DateTime.Today.ToString("yyyy-MM-dd");
        if (_settings.StatisticsDate != today) { _settings.StatisticsDate = today; _settings.ExecutionsToday = 0; }
        var weekStart = DateTime.Today.AddDays(-(((int)DateTime.Today.DayOfWeek + 6) % 7)).ToString("yyyy-MM-dd");
        if (_settings.StatisticsWeekStart != weekStart) { _settings.StatisticsWeekStart = weekStart; _settings.ActiveSecondsThisWeek = 0; }
    }
    private void UpdateStatisticsUi()
    {
        if (TodayExecutionsText is null) return;
        TodayExecutionsText.Text = _settings.ExecutionsToday.ToString();
        BoundCommandsText.Text = _commands.Count.ToString();
        var seconds = _settings.ActiveSecondsThisWeek + Math.Max(0, _sessionUptime.Elapsed.TotalSeconds - _accountedSessionSeconds);
        WeeklyActiveText.Text = $"{seconds / 3600:0.0}h";
    }
    private static void ApplyStartupSetting(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        const string name = "PulseMouse";
        if (enabled) key.SetValue(name, $"\"{Environment.ProcessPath}\""); else key.DeleteValue(name, false);
    }
    // Navigation only changes visibility: the listening engine and bindings stay alive between pages.
    private void SelectPage(string key)
    {
        DashboardPage.Visibility = key == "overview" ? Visibility.Visible : Visibility.Collapsed;
        CommandsPage.Visibility = key == "commands" ? Visibility.Visible : Visibility.Collapsed;
        GesturesPage.Visibility = key == "gestures" ? Visibility.Visible : Visibility.Collapsed;
        WorkflowsPage.Visibility = key == "workflows" ? Visibility.Visible : Visibility.Collapsed;
        SettingsPage.Visibility = key == "settings" ? Visibility.Visible : Visibility.Collapsed;
        bool english = EnglishLanguageBox?.IsChecked == true;
        (PageTitle.Text, PageSubtitle.Text) = (key, english) switch
        {
            ("commands", true) => ("Commands", "Manage rhythm cards for apps, folders, websites, and workflows."),
            ("gestures", true) => ("Gestures", "Configure directional actions while holding the middle button."),
            ("workflows", true) => ("Workflows", "Chain local actions with confirmation before execution."),
            ("settings", true) => ("Settings", "Adjust behavior, Pulse Capsule, language, and backups."),
            ("overview", true) => ("Dashboard", "Monitor rhythm input, listening status, and recent events."),
            ("commands", false) => ("指令库", "用节奏卡片管理程序、文件夹、网页与工作流。"),
            ("gestures", false) => ("方向手势", "配置按住中键移动时触发的方向动作。"),
            ("workflows", false) => ("工作流", "串联多个本地动作，并保留执行前确认。"),
            ("settings", false) => ("设置", "调整行为、Pulse Capsule、语言与备份。"),
            _ => ("仪表盘", "实时观察节奏输入、监听状态与最近事件。")
        };
        var pages = new[] { (NavOverview, "overview"), (NavCommands, "commands"),
            (NavGestures, "gestures"), (NavWorkflows, "workflows"), (NavSettings, "settings") };
        foreach (var (button, page) in pages)
        {
            button.Background = page == key
                ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(34, 74, 75))
                : System.Windows.Media.Brushes.Transparent;
            button.Foreground = page == key
                ? (System.Windows.Media.Brush)FindResource("AccentBrush")
                : new SolidColorBrush(System.Windows.Media.Color.FromRgb(197, 211, 225));
        }
        MainScroller.ScrollToTop();
    }
    private void GoOverviewClick(object sender, RoutedEventArgs e) => SelectPage("overview");
    private void GoCommandsClick(object sender, RoutedEventArgs e) => SelectPage("commands");
    private void GoGesturesClick(object sender, RoutedEventArgs e) => SelectPage("gestures");
    private void GoWorkflowsClick(object sender, RoutedEventArgs e) => SelectPage("workflows");
    private void GoSettingsClick(object sender, RoutedEventArgs e) => SelectPage("settings");
    private void SaveSettingsClick(object sender, RoutedEventArgs e) => SaveConfiguration();
    private void HideClick(object sender, RoutedEventArgs e) => Hide();
    private void RestoreWindow() { Show(); WindowState = WindowState.Normal; Activate(); }
    private void Exit() { _feedback?.Close(); _exiting = true; _hook.Enabled = false; _hook.Dispose(); _timer.Stop(); _tray.Visible = false; _tray.Dispose(); System.Windows.Application.Current.Shutdown(); }
    protected override void OnClosed(EventArgs e) { _hook.Dispose(); _tray.Dispose(); base.OnClosed(e); }
}
