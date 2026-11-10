using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PulseMouse.App.Controls;
using PulseMouse.App.Services;
using PulseMouse.Core;

namespace PulseMouse.App.Views;

public partial class SettingsView : System.Windows.Controls.UserControl, IPageView
{
    private sealed record Timing(Func<string> Name, Func<string> Description, int Min, int Max, int Step, Func<AppSettings, int> Get, Action<AppSettings, int> Set, int Default);

    private readonly Timing[] _timings =
    [
        new(() => Loc.T("短按上限", "Short max"), () => Loc.T("按住时长不超过此值记为 ·", "Hold ≤ this → ·"), 100, 450, 10, s => s.ShortMaxMs, (s, v) => s.ShortMaxMs = v, 300),
        new(() => Loc.T("长按下限", "Long min"), () => Loc.T("按住时长不低于此值记为 —；两者之间视为无效", "Hold ≥ this → —; between is invalid"), 300, 1000, 10, s => s.LongMinMs, (s, v) => s.LongMinMs = v, 500),
        new(() => Loc.T("长按上限", "Long max"), () => Loc.T("超过此时长视为误触并重置输入", "Longer than this resets input"), 1000, 3000, 50, s => s.LongMaxMs, (s, v) => s.LongMaxMs = v, 1600),
        new(() => Loc.T("停顿提交", "Gap commit"), () => Loc.T("松开后停顿多久提交整段节奏", "Idle gap before the rhythm commits"), 400, 1500, 10, s => s.SequenceGapMs, (s, v) => s.SequenceGapMs = v, 750),
        new(() => Loc.T("中键激活时长", "Middle arm hold"), () => Loc.T("普通鼠标静止长按中键多久进入指令模式", "Still middle hold to arm classic mode"), 300, 2000, 10, s => s.MiddleHoldMs, (s, v) => s.MiddleHoldMs = v, 550),
    ];

    private readonly MainWindow _shell;
    private readonly PulseController _c;
    private readonly Slider[] _sliders;
    private readonly TextBlock[] _values;
    private readonly TextBlock[] _timingNames;
    private readonly TextBlock[] _timingDescs;
    private bool _syncing;
    private bool _timingDirty;
    private bool? _dual;

    public SettingsView(MainWindow shell)
    {
        InitializeComponent();
        _shell = shell;
        _c = shell.Controller;
        _sliders = new Slider[_timings.Length];
        _values = new TextBlock[_timings.Length];
        _timingNames = new TextBlock[_timings.Length];
        _timingDescs = new TextBlock[_timings.Length];
        Ruler.NeedleValue = double.NaN;
        BuildTimingRows();
        ApplyLayout(dual: false);
        Relocalize();
        Reload();
    }

    public void OnShown() { Reload(); ApplyLayout(ContentWidth() >= 900); }
    public void OnHidden() { }

    private void OnViewSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Breakpoint is content-pane width (spec 5.13), not ShellColumn.MaxWidth.
        if (e.WidthChanged || _dual is null) ApplyLayout(ContentWidth() >= 900);
    }

    /// <summary>ScrollViewer viewport when hosted in MainWindow; otherwise this view's width.</summary>
    private double ContentWidth()
    {
        for (DependencyObject? p = this; p is not null; p = VisualTreeHelper.GetParent(p))
            if (p is ScrollViewer { ViewportWidth: > 0 } sv)
                return sv.ViewportWidth;
        return ActualWidth > 0 ? ActualWidth : 0;
    }

    private void ApplyLayout(bool dual)
    {
        if (_dual == dual && LeftCol.Children.Count > 0) return;
        _dual = dual;
        Detach(SecGeneral, SecContext, SecMouse, SecCapsule, SecSafety, SecAi, SecTiming);
        LeftCol.Children.Clear();
        RightCol.Children.Clear();
        TimingHost.Children.Clear();

        // Header + cards share one centered column so maximized windows don't leave the title stretched edge-to-edge.
        ShellColumn.MaxWidth = dual ? 1100 : 640;
        ColGap.Width = dual ? new GridLength(18) : new GridLength(0);
        ColRight.Width = dual ? new GridLength(1, GridUnitType.Star) : new GridLength(0);

        if (dual)
        {
            Place(LeftCol, SecGeneral, SecContext, SecMouse);
            Place(RightCol, SecCapsule, SecSafety, SecAi);
            Place(TimingHost, SecTiming);
            TimingRows.MaxWidth = 640;
            TimingRows.HorizontalAlignment = HorizontalAlignment.Left;
        }
        else
        {
            Place(LeftCol, SecGeneral, SecContext, SecMouse, SecCapsule, SecSafety, SecAi);
            Place(TimingHost, SecTiming);
            TimingRows.MaxWidth = double.PositiveInfinity;
            TimingRows.HorizontalAlignment = HorizontalAlignment.Stretch;
        }
    }

    private static void Detach(params FrameworkElement[] sections)
    {
        foreach (var s in sections)
            if (s.Parent is Panel p) p.Children.Remove(s);
    }

    private static void Place(Panel host, params FrameworkElement[] sections)
    {
        foreach (var s in sections) host.Children.Add(s);
    }

    public void Relocalize()
    {
        Header.Eyebrow = Loc.Eyebrow(Page.Settings);
        Header.Heading = Loc.Nav(Page.Settings);
        Header.Description = Loc.Description(Page.Settings);
        OpenConfigButton.Content = Loc.T("打开配置文件夹", "Open config folder");

        TitleGeneral.Text = Loc.T("通用", "General");
        RowStartup.Text = Loc.T("开机自启动", "Start with Windows");
        DescStartup.Text = Loc.T("Windows 登录后自动运行 PulseMouse（写入当前用户启动项，无需管理员权限）", "Launch PulseMouse at sign-in (per-user Run key, no admin)");
        RowSound.Text = Loc.T("执行成功提示音", "Success sound");
        DescSound.Text = Loc.T("指令成功发起后播放系统轻提示音", "Play a system chime after a successful run");
        RowLanguage.Text = Loc.T("语言 / Language", "Language");
        DescLanguage.Text = Loc.T("切换全部界面文案；你起的指令名称保持不变", "Switch all UI chrome; your command names stay as-is");

        TitleContext.Text = Loc.T("情境 · Context Pulse", "Context · Context Pulse");
        RowContext.Text = Loc.T("按程序切换含义", "Per-app meanings");
        DescContext.Text = Loc.T("光标所在程序给某个节奏定义了自己的含义时使用它；关闭后只用全局含义", "Use an app’s own meaning when present; off = globals only");
        RowRemind.Text = Loc.T("新含义提醒", "New-meaning reminder");
        DescRemind.Text = Loc.T("替换了全局含义后，前 5 次使用时胶囊提示", "Amber capsule tip for the first 5 uses of a replacement");
        RowCapsuleContext.Text = Loc.T("胶囊显示当前程序", "Capsule shows current app");
        DescCapsuleContext.Text = Loc.T("胶囊显示光标下的程序及含义来源", "Show the app under the cursor and meaning source");
        RowActivate.Text = Loc.T("发送快捷键前激活光标下的窗口", "Activate target before shortcut");
        DescActivate.Text = Loc.T("保证快捷键作用在指着的窗口；关闭后发到当前前台", "Focus the pointed window first; off = current foreground");

        TitleMouse.Text = Loc.T("鼠标拦截", "Mouse interception");
        RowFullscreen.Text = Loc.T("全屏时暂停", "Pause in fullscreen");
        DescFullscreen.Text = Loc.T("前台是全屏游戏、演示或视频时，不拦截任何鼠标按键", "Don’t intercept while a fullscreen app is in front");
        RowExcluded.Text = Loc.T("排除的程序", "Excluded apps");
        DescExcluded.Text = Loc.T("光标位于这些程序的窗口上时完全不拦截（填写进程名，用逗号分隔）。管理员权限窗口会自动放行右键。", "Never intercept over these process names (comma-separated). Elevated windows always pass right-clicks.");
        Ui.SetPlaceholder(ExcludedBox, Loc.T("例如：cs2, blender, acad", "e.g. cs2, blender, acad"));
        RowSide.Text = Loc.T("侧键检测", "Side-button detection");
        RedetectButton.Content = Loc.T("重新检测", "Re-detect");

        TitleCapsule.Text = Loc.T("Pulse Capsule · 屏幕状态胶囊", "Pulse Capsule");
        RowCapsule.Text = Loc.T("启用状态胶囊", "Enable capsule");
        DescCapsule.Text = Loc.T("在鼠标所在屏幕的任务栏上方显示识别状态，不抢焦点、可点击穿透", "Status pill above the taskbar — no focus steal, click-through");
        RowPosition.Text = Loc.T("胶囊位置", "Capsule position");
        DescPosition.Text = Loc.T("选择状态胶囊的停靠位置", "Where the capsule docks");
        PosLeft.Content = Loc.T("左下", "Left");
        PosCenter.Content = Loc.T("底部居中", "Center");
        PosRight.Content = Loc.T("右下", "Right");
        RowBreathing.Text = Loc.T("呼吸动画", "Breathing animation");
        DescBreathing.Text = Loc.T("激活与输入期间使用缓慢呼吸反馈", "Slow pulse while armed / typing");
        RowHint.Text = Loc.T("犹豫时显示节奏清单", "Hesitation rhythm list");
        DescHint.Text = Loc.T("按住右键或敲到一半停顿 0.6 秒，在胶囊上方列出还能匹配的指令", "After 0.6s pause, list reachable rhythms above the capsule");
        PreviewHintButton.Content = Loc.T("预览", "Preview");
        RowPreview.Text = Loc.T("预览", "Preview");
        DescPreview.Text = Loc.T("在屏幕上显示一次「已匹配」状态", "Show a one-shot matched state on screen");
        PreviewCapsuleButton.Content = Loc.T("预览胶囊", "Preview capsule");

        TitleSafety.Text = Loc.T("安全与数据", "Safety & data");
        RowConfirm.Text = Loc.T("工作流执行前确认", "Confirm workflows");
        DescConfirm.Text = Loc.T("运行多步骤动作前显示确认对话框", "Ask before running multi-step actions");
        RowBackup.Text = Loc.T("配置备份", "Config backup");
        DescBackup.Text = Loc.T("导出或恢复指令、手势与偏好（JSON，含版本号与情境）", "Export / restore commands, gestures and prefs (JSON, versioned, with context)");
        ExportButton.Content = Loc.T("导出", "Export");
        ImportButton.Content = Loc.T("导入", "Import");

        TitleAi.Text = Loc.T("AI 入口 · 可选", "AI entry · optional");
        RowAi.Text = Loc.T("AI 网页地址", "AI page URL");
        DescAi.Text = Loc.T("只打开指定的 HTTPS 页面，不读取或上传屏幕内容", "Opens an HTTPS page only — never reads or uploads the screen");
        OpenAiButton.Content = Loc.T("打开", "Open");

        TitleTiming.Text = Loc.T("节奏识别", "Rhythm recognition");
        ResetTimingButton.Content = Loc.T("恢复默认", "Reset defaults");
        for (int i = 0; i < _timings.Length; i++)
        {
            _timingNames[i].Text = _timings[i].Name();
            _timingDescs[i].Text = _timings[i].Description();
        }
        RefreshSideText();
    }

    public void Reload()
    {
        _syncing = true;
        var s = _c.Settings;
        StartupSwitch.IsChecked = s.StartupEnabled;
        SoundSwitch.IsChecked = s.SuccessSound;
        LanguageSeg.SelectedIndex = s.UiLanguage == "en-US" ? 1 : 0;
        ContextSwitch.IsChecked = s.ContextEnabled;
        RemindSwitch.IsChecked = s.NewMeaningReminder;
        CapsuleContextSwitch.IsChecked = s.CapsuleShowsContext;
        ActivateSwitch.IsChecked = s.ActivateTargetForShortcut;
        CapsuleSwitch.IsChecked = s.FloatingFeedback;
        BreathingSwitch.IsChecked = s.CapsuleBreathing;
        HintSwitch.IsChecked = s.HesitationHints;
        ConfirmSwitch.IsChecked = s.ConfirmWorkflows;
        FullscreenSwitch.IsChecked = s.PauseInFullscreen;
        ExcludedBox.Text = string.Join(", ", s.ExcludedApps);
        RefreshSideText();
        RedetectButton.IsEnabled = _c.DetectedSideButton != 0;
        (s.CapsulePosition switch { "Left" => PosLeft, "Right" => PosRight, _ => PosCenter }).IsChecked = true;
        AiUrlBox.Text = s.AiProviderUrl;
        for (int i = 0; i < _timings.Length; i++) _sliders[i].Value = _timings[i].Get(s);
        _syncing = false;
        RefreshTiming();
    }

    private void RefreshSideText()
    {
        int side = _c.DetectedSideButton;
        SideText.Text = side == 0
            ? Loc.T("尚未检测到侧键：自动模式使用右键 + 左键和弦；按一下侧键即改用侧键",
                "No side button yet: Auto uses right+left chord; press a side button once to switch")
            : Loc.T($"已检测到 X{side}：自动模式直接用侧键敲节奏；换成没有侧键的鼠标后请重新检测",
                $"Detected X{side}: Auto uses the side button; re-detect after switching mice");
    }

    private void BuildTimingRows()
    {
        for (int i = 0; i < _timings.Length; i++)
        {
            var t = _timings[i];
            int index = i;
            var slider = new Slider
            {
                Style = (Style)FindResource("Range"),
                Minimum = t.Min, Maximum = t.Max, TickFrequency = t.Step,
                SmallChange = t.Step, LargeChange = t.Step * 5,
            };
            slider.ValueChanged += (_, _) => TimingChanged(index);
            slider.PreviewMouseLeftButtonUp += (_, _) => CommitTiming();
            slider.LostKeyboardFocus += (_, _) => CommitTiming();

            var value = new TextBlock { Style = (Style)FindResource("Val"), MinWidth = 64, TextAlignment = TextAlignment.Right, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            _sliders[i] = slider;
            _values[i] = value;

            slider.Width = 220;
            slider.HorizontalAlignment = HorizontalAlignment.Right;
            var control = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(20, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            control.Children.Add(slider);
            control.Children.Add(value);
            DockPanel.SetDock(control, Dock.Right);

            var name = new TextBlock { Style = (Style)FindResource("RowName"), Text = t.Name() };
            var desc = new TextBlock { Style = (Style)FindResource("RowDesc"), Text = t.Description() };
            _timingNames[i] = name;
            _timingDescs[i] = desc;
            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(name);
            info.Children.Add(desc);

            var dock = new DockPanel();
            dock.Children.Add(control);
            dock.Children.Add(info);
            TimingRows.Children.Add(new Border { Style = (Style)FindResource(i == _timings.Length - 1 ? "SRowLast" : "SRow"), Child = dock });
        }
    }

    private void TimingChanged(int index)
    {
        if (_syncing) return;
        var s = _c.Settings;
        int value = (int)Math.Round(_sliders[index].Value);
        _timings[index].Set(s, value);

        _syncing = true;
        if (s.LongMinMs < s.ShortMaxMs + 50)
        {
            if (index == 1) s.ShortMaxMs = Math.Max(_timings[0].Min, s.LongMinMs - 50);
            else s.LongMinMs = Math.Min(_timings[1].Max, s.ShortMaxMs + 50);
        }
        if (s.LongMaxMs < s.LongMinMs + 100) s.LongMaxMs = Math.Min(_timings[2].Max, s.LongMinMs + 100);
        for (int i = 0; i < _timings.Length; i++) _sliders[i].Value = _timings[i].Get(s);
        _syncing = false;

        RefreshTiming();
        _c.ApplySettings();
        _c.SaveSoon();
        _timingDirty = true;
    }

    private void CommitTiming()
    {
        if (!_timingDirty) return;
        _timingDirty = false;
        _c.Save();
        _shell.Toast(Loc.T("节奏识别参数已保存", "Rhythm settings saved"));
    }

    private void RefreshTiming()
    {
        var s = _c.Settings;
        for (int i = 0; i < _timings.Length; i++) _values[i].Text = $"{_timings[i].Get(s)} ms";
        Ruler.ShortMax = s.ShortMaxMs;
        Ruler.LongMin = s.LongMinMs;
        Ruler.LongMax = s.LongMaxMs;
    }

    private void ResetTimingClick(object sender, RoutedEventArgs e)
    {
        foreach (var t in _timings) t.Set(_c.Settings, t.Default);
        Reload();
        _c.ApplySettings();
        _c.Save();
        _shell.Toast(Loc.T("已恢复默认节奏参数", "Rhythm defaults restored"));
    }

    private void StartupClick(object sender, RoutedEventArgs e)
    {
        _c.SetStartup(StartupSwitch.IsChecked == true);
        _c.Save();
        _shell.Toast(Loc.T("设置已自动保存", "Settings saved"));
    }

    private void ToggleClick(object sender, RoutedEventArgs e)
    {
        var s = _c.Settings;
        s.SuccessSound = SoundSwitch.IsChecked == true;
        s.ContextEnabled = ContextSwitch.IsChecked == true;
        s.NewMeaningReminder = RemindSwitch.IsChecked == true;
        s.CapsuleShowsContext = CapsuleContextSwitch.IsChecked == true;
        s.ActivateTargetForShortcut = ActivateSwitch.IsChecked == true;
        s.FloatingFeedback = CapsuleSwitch.IsChecked == true;
        s.CapsuleBreathing = BreathingSwitch.IsChecked == true;
        s.HesitationHints = HintSwitch.IsChecked == true;
        s.ConfirmWorkflows = ConfirmSwitch.IsChecked == true;
        s.PauseInFullscreen = FullscreenSwitch.IsChecked == true;
        if (!s.FloatingFeedback) _c.Feedback.HideNow();
        _c.ApplySettings();
        _c.Save();
        _shell.Toast(Loc.T("设置已自动保存", "Settings saved"));
        _shell.RefreshChromePublic();
    }

    private void ExcludedLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        var names = ExcludedBox.Text.Split([',', '，', ';', '；', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var before = string.Join(",", _c.Settings.ExcludedApps);
        _c.SetExcludedApps(names);
        ExcludedBox.Text = string.Join(", ", _c.Settings.ExcludedApps);
        if (before != string.Join(",", _c.Settings.ExcludedApps))
            _shell.Toast(Loc.T("排除列表已保存", "Exclusion list saved"));
    }

    private void RedetectClick(object sender, RoutedEventArgs e)
    {
        _c.ResetSideDetection();
        Reload();
        _shell.Toast(Loc.T("已清除侧键检测结果", "Side-button detection cleared"));
    }

    private void LanguageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || LanguageSeg.SelectedIndex < 0) return;
        string language = LanguageSeg.SelectedIndex == 1 ? "en-US" : "zh-CN";
        if (_c.Settings.UiLanguage == language) return;
        _c.Settings.UiLanguage = language;
        _c.ApplySettings();
        _c.Save();
        _shell.Relocalize();
        _shell.Toast(Loc.T("设置已自动保存", "Settings saved"));
    }

    private void PositionChecked(object sender, RoutedEventArgs e)
    {
        if (_syncing || sender is not RadioButton { Tag: string position }) return;
        _c.Settings.CapsulePosition = position;
        _c.ApplySettings();
        _c.Save();
        if (_c.Settings.FloatingFeedback) _c.PreviewCapsule();
    }

    private void PreviewClick(object sender, RoutedEventArgs e) => _c.PreviewCapsule();
    private void PreviewHintClick(object sender, RoutedEventArgs e) => _c.PreviewHint();

    private void OpenConfigClick(object sender, RoutedEventArgs e)
    {
        try
        {
            _c.Save();
            string? folder = Path.GetDirectoryName(_c.ConfigPath);
            if (folder is null) return;
            Directory.CreateDirectory(folder);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
        catch (Exception ex) { _shell.Toast(Loc.T("无法打开配置文件夹：", "Cannot open folder: ") + ex.Message, IconKind.Alert); }
    }

    private void ExportClick(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.SaveFileDialog
        {
            Title = Loc.T("导出 PulseMouse 配置", "Export PulseMouse settings"),
            Filter = Loc.T("PulseMouse 配置 (*.json)|*.json", "PulseMouse settings (*.json)|*.json"),
            FileName = $"PulseMouse-backup-{DateTime.Now:yyyyMMdd}.json",
        };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        try
        {
            _c.Export(dialog.FileName);
            _c.AddLog(Loc.T("配置已导出：", "Settings exported: ") + Path.GetFileName(dialog.FileName), "", LogTone.Hit);
            _shell.Toast(Loc.T("已导出 ", "Exported ") + Path.GetFileName(dialog.FileName));
        }
        catch (Exception ex) { _shell.Toast(Loc.T("导出失败：", "Export failed: ") + ex.Message, IconKind.Alert); }
    }

    private async void ImportClick(object sender, RoutedEventArgs e)
    {
        bool ok = await _shell.ConfirmAsync(new ConfirmRequest(
            Loc.T("导入配置？", "Import settings?"),
            Loc.T("将用备份文件替换当前的指令、手势与偏好，导入后立即生效，无需重启。",
                "Replaces commands, gestures and prefs from a backup. Takes effect immediately."),
            OkText: Loc.T("选择文件并导入", "Choose file & import")));
        if (!ok) return;

        using var dialog = new System.Windows.Forms.OpenFileDialog
        {
            Title = Loc.T("导入 PulseMouse 配置", "Import PulseMouse settings"),
            Filter = Loc.T("PulseMouse 配置 (*.json)|*.json|所有文件 (*.*)|*.*", "PulseMouse settings (*.json)|*.json|All files (*.*)|*.*"),
        };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        try
        {
            _c.Import(dialog.FileName);
            _shell.Relocalize();
            _shell.Toast(Loc.T("配置已导入并立即生效", "Settings imported and applied"));
        }
        catch (Exception ex) { _shell.Toast(Loc.T("导入失败：", "Import failed: ") + ex.Message, IconKind.Alert); }
    }

    private void AiUrlChanged(object sender, TextChangedEventArgs e)
    {
        if (_syncing) return;
        _c.Settings.AiProviderUrl = AiUrlBox.Text.Trim();
        _c.SaveSoon();
    }

    private void OpenAiClick(object sender, RoutedEventArgs e)
    {
        string url = AiUrlBox.Text.Trim();
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            _shell.Toast(Loc.T("请输入以 https:// 开头的地址", "Enter an https:// URL"), IconKind.Alert);
            return;
        }
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) { _shell.Toast(Loc.T("无法打开：", "Cannot open: ") + ex.Message, IconKind.Alert); }
    }
}
