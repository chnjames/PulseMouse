using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using PulseMouse.App.Controls;
using PulseMouse.App.Services;
using PulseMouse.Core;

namespace PulseMouse.App.Views;

public partial class SettingsView : System.Windows.Controls.UserControl, IPageView
{
    private sealed record Timing(string Name, string Description, int Min, int Max, int Step, Func<AppSettings, int> Get, Action<AppSettings, int> Set, int Default);

    private static readonly Timing[] Timings =
    [
        new("短按上限", "按住时长不超过此值记为 ·", 100, 450, 10, s => s.ShortMaxMs, (s, v) => s.ShortMaxMs = v, 300),
        new("长按下限", "按住时长不低于此值记为 —；两者之间视为无效", 300, 1000, 10, s => s.LongMinMs, (s, v) => s.LongMinMs = v, 500),
        new("长按上限", "超过此时长视为误触并重置输入", 1000, 3000, 50, s => s.LongMaxMs, (s, v) => s.LongMaxMs = v, 1600),
        new("停顿提交", "松开后停顿多久提交整段节奏", 400, 1500, 10, s => s.SequenceGapMs, (s, v) => s.SequenceGapMs = v, 750),
        new("中键激活时长", "普通鼠标静止长按中键多久进入指令模式", 300, 2000, 10, s => s.MiddleHoldMs, (s, v) => s.MiddleHoldMs = v, 550),
    ];

    private readonly MainWindow _shell;
    private readonly PulseController _c;
    private readonly Slider[] _sliders = new Slider[Timings.Length];
    private readonly TextBlock[] _values = new TextBlock[Timings.Length];
    private bool _syncing;
    private bool _timingDirty;

    public SettingsView(MainWindow shell)
    {
        InitializeComponent();
        _shell = shell;
        _c = shell.Controller;
        Ruler.NeedleValue = double.NaN;
        BuildTimingRows();
        Relocalize();
        Reload();
    }

    public void OnShown() => Reload();
    public void OnHidden() { }

    public void Relocalize()
    {
        Header.Eyebrow = Loc.Eyebrow(Page.Settings);
        Header.Heading = Loc.Nav(Page.Settings);
        Header.Description = Loc.Description(Page.Settings);
    }

    public void Reload()
    {
        _syncing = true;
        var s = _c.Settings;
        StartupSwitch.IsChecked = s.StartupEnabled;
        SoundSwitch.IsChecked = s.SuccessSound;
        LanguageSeg.SelectedIndex = s.UiLanguage == "en-US" ? 1 : 0;
        CapsuleSwitch.IsChecked = s.FloatingFeedback;
        BreathingSwitch.IsChecked = s.CapsuleBreathing;
        ConfirmSwitch.IsChecked = s.ConfirmWorkflows;
        FullscreenSwitch.IsChecked = s.PauseInFullscreen;
        ExcludedBox.Text = string.Join(", ", s.ExcludedApps);
        int side = _c.DetectedSideButton;
        SideText.Text = side == 0
            ? "尚未检测到侧键：自动模式使用右键 + 左键和弦；按一下侧键即改用侧键"
            : $"已检测到 X{side}：自动模式直接用侧键敲节奏；换成没有侧键的鼠标后请重新检测";
        RedetectButton.IsEnabled = side != 0;
        (s.CapsulePosition switch { "Left" => PosLeft, "Right" => PosRight, _ => PosCenter }).IsChecked = true;
        AiUrlBox.Text = s.AiProviderUrl;
        for (int i = 0; i < Timings.Length; i++) _sliders[i].Value = Timings[i].Get(s);
        _syncing = false;
        RefreshTiming();
    }

    // ---------- timing ----------

    private void BuildTimingRows()
    {
        for (int i = 0; i < Timings.Length; i++)
        {
            var t = Timings[i];
            int index = i;
            var slider = new Slider
            {
                Style = (Style)FindResource("Range"),
                Minimum = t.Min,
                Maximum = t.Max,
                TickFrequency = t.Step,
                SmallChange = t.Step,
                LargeChange = t.Step * 5,
            };
            slider.ValueChanged += (_, _) => TimingChanged(index);
            slider.PreviewMouseLeftButtonUp += (_, _) => CommitTiming();
            slider.LostKeyboardFocus += (_, _) => CommitTiming();

            var value = new TextBlock { Style = (Style)FindResource("Val"), MinWidth = 64, TextAlignment = TextAlignment.Right, Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            _sliders[i] = slider;
            _values[i] = value;

            var control = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(20, 0, 0, 0) };
            control.Children.Add(slider);
            control.Children.Add(value);
            DockPanel.SetDock(control, Dock.Right);

            var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            info.Children.Add(new TextBlock { Style = (Style)FindResource("RowName"), Text = t.Name });
            info.Children.Add(new TextBlock { Style = (Style)FindResource("RowDesc"), Text = t.Description });

            var dock = new DockPanel();
            dock.Children.Add(control);
            dock.Children.Add(info);
            TimingRows.Children.Add(new Border { Style = (Style)FindResource(i == Timings.Length - 1 ? "SRowLast" : "SRow"), Child = dock });
        }
    }

    private void TimingChanged(int index)
    {
        if (_syncing) return;
        var s = _c.Settings;
        int value = (int)Math.Round(_sliders[index].Value);
        Timings[index].Set(s, value);

        _syncing = true;
        if (s.LongMinMs < s.ShortMaxMs + 50)
        {
            if (index == 1) s.ShortMaxMs = Math.Max(Timings[0].Min, s.LongMinMs - 50);
            else s.LongMinMs = Math.Min(Timings[1].Max, s.ShortMaxMs + 50);
        }
        if (s.LongMaxMs < s.LongMinMs + 100) s.LongMaxMs = Math.Min(Timings[2].Max, s.LongMinMs + 100);
        for (int i = 0; i < Timings.Length; i++) _sliders[i].Value = Timings[i].Get(s);
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
        _shell.Toast("节奏识别参数已保存");
    }

    private void RefreshTiming()
    {
        var s = _c.Settings;
        for (int i = 0; i < Timings.Length; i++) _values[i].Text = $"{Timings[i].Get(s)} ms";
        Ruler.ShortMax = s.ShortMaxMs;
        Ruler.LongMin = s.LongMinMs;
        Ruler.LongMax = s.LongMaxMs;
    }

    private void ResetTimingClick(object sender, RoutedEventArgs e)
    {
        foreach (var t in Timings) t.Set(_c.Settings, t.Default);
        Reload();
        _c.ApplySettings();
        _c.Save();
        _shell.Toast("已恢复默认节奏参数");
    }

    // ---------- toggles ----------

    private void StartupClick(object sender, RoutedEventArgs e)
    {
        _c.SetStartup(StartupSwitch.IsChecked == true);
        _c.Save();
        _shell.Toast("设置已自动保存");
    }

    private void ToggleClick(object sender, RoutedEventArgs e)
    {
        var s = _c.Settings;
        s.SuccessSound = SoundSwitch.IsChecked == true;
        s.FloatingFeedback = CapsuleSwitch.IsChecked == true;
        s.CapsuleBreathing = BreathingSwitch.IsChecked == true;
        s.ConfirmWorkflows = ConfirmSwitch.IsChecked == true;
        s.PauseInFullscreen = FullscreenSwitch.IsChecked == true;
        if (!s.FloatingFeedback) _c.Feedback.HideNow();
        _c.ApplySettings();
        _c.Save();
        _shell.Toast("设置已自动保存");
    }

    private void ExcludedLostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        var names = ExcludedBox.Text.Split([',', '，', ';', '；', '\n'], StringSplitOptions.RemoveEmptyEntries);
        var before = string.Join(",", _c.Settings.ExcludedApps);
        _c.SetExcludedApps(names);
        ExcludedBox.Text = string.Join(", ", _c.Settings.ExcludedApps);
        if (before != string.Join(",", _c.Settings.ExcludedApps)) _shell.Toast("排除列表已保存");
    }

    private void RedetectClick(object sender, RoutedEventArgs e)
    {
        _c.ResetSideDetection();
        Reload();
        _shell.Toast("已清除侧键检测结果");
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
        _shell.Toast("设置已自动保存");
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

    // ---------- data ----------

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
        catch (Exception ex) { _shell.Toast("无法打开配置文件夹：" + ex.Message, IconKind.Alert); }
    }

    private void ExportClick(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.SaveFileDialog
        {
            Title = "导出 PulseMouse 配置",
            Filter = "PulseMouse 配置 (*.json)|*.json",
            FileName = $"PulseMouse-backup-{DateTime.Now:yyyyMMdd}.json",
        };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        try
        {
            _c.Export(dialog.FileName);
            _c.AddLog("配置已导出：" + Path.GetFileName(dialog.FileName), "", LogTone.Hit);
            _shell.Toast("已导出 " + Path.GetFileName(dialog.FileName));
        }
        catch (Exception ex) { _shell.Toast("导出失败：" + ex.Message, IconKind.Alert); }
    }

    private async void ImportClick(object sender, RoutedEventArgs e)
    {
        bool ok = await _shell.ConfirmAsync(new ConfirmRequest(
            "导入配置？",
            "将用备份文件替换当前的指令、手势与偏好，导入后立即生效，无需重启。",
            OkText: "选择文件并导入"));
        if (!ok) return;

        using var dialog = new System.Windows.Forms.OpenFileDialog
        {
            Title = "导入 PulseMouse 配置",
            Filter = "PulseMouse 配置 (*.json)|*.json|所有文件 (*.*)|*.*",
        };
        if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        try
        {
            _c.Import(dialog.FileName);
            _shell.Relocalize();
            _shell.Toast("配置已导入并立即生效");
        }
        catch (Exception ex) { _shell.Toast("导入失败：" + ex.Message, IconKind.Alert); }
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
            _shell.Toast("请输入以 https:// 开头的地址", IconKind.Alert);
            return;
        }
        try { Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception ex) { _shell.Toast("无法打开：" + ex.Message, IconKind.Alert); }
    }
}
