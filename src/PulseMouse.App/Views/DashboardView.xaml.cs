using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using PulseMouse.App.Controls;
using PulseMouse.App.Services;
using PulseMouse.Core;

namespace PulseMouse.App.Views;

public partial class DashboardView : System.Windows.Controls.UserControl, IPageView
{
    private readonly MainWindow _shell;
    private readonly PulseController _c;
    private readonly PulseSlot[] _slots = new PulseSlot[5];
    private readonly DispatcherTimer _frame;
    private bool _visible, _logDirty = true, _activityDirty = true, _routeSync;

    public DashboardView(MainWindow shell)
    {
        InitializeComponent();
        _shell = shell;
        _c = shell.Controller;
        for (int i = 0; i < _slots.Length; i++)
        {
            _slots[i] = new PulseSlot { Width = 74, Height = 60, Radius = 12, GlyphSize = GlyphSize.Large, Margin = new Thickness(5, 0, 5, 0) };
            Slots.Children.Add(_slots[i]);
        }
        _frame = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        _frame.Tick += (_, _) => UpdateMotion();
        IsVisibleChanged += (_, _) => { if (IsVisible && _visible) _frame.Start(); else _frame.Stop(); };

        _c.LiveChanged += () => { if (_visible) RefreshLive(); };
        _c.StatsChanged += () => { if (_visible) RefreshStats(); };
        _c.BindingsChanged += () => { if (_visible) { RefreshStats(); RefreshLive(); } };
        _c.StateChanged += () => { if (_visible) { RefreshRoute(); RefreshLive(); } };
        _c.Log.CollectionChanged += (_, _) => { _logDirty = true; if (_visible) RefreshLog(); };
        _c.Activity.CollectionChanged += (_, _) => { _activityDirty = true; if (_visible) RefreshActivity(); };
        Relocalize();
    }

    public void OnShown()
    {
        _visible = true;
        RefreshStats();
        RefreshRoute();
        RefreshLive();
        RefreshLog();
        RefreshActivity();
        _frame.Start();
    }

    public void OnHidden()
    {
        _visible = false;
        _frame.Stop();
    }

    public void Relocalize()
    {
        Header.Eyebrow = Loc.Eyebrow(Page.Dashboard);
        Header.Heading = Loc.Nav(Page.Dashboard);
        Header.Description = Loc.Description(Page.Dashboard);
        TodayLabel.Text = Loc.T("今日执行", "Runs today");
        BoundLabel.Text = Loc.T("在用的节奏", "Rhythms in use");
        WeekLabel.Text = Loc.T("有专属含义的程序", "Apps with own meanings");
        if (_visible) RefreshStats();
    }

    public void Reload() { if (_visible) OnShown(); }

    // ---------- stats & route ----------

    private void RefreshStats()
    {
        var s = _c.Settings;
        TodayValue.Text = s.ExecutionsToday.ToString();
        var week = (s.History ?? new()).TakeLast(7).ToList();
        int runs = week.Sum(d => d.Executions), misses = week.Sum(d => d.Misses);
        TodaySub.Text = week.Count == 0
            ? Loc.T($"未匹配 {s.MissesToday} 次", $"{s.MissesToday} misses")
            : Loc.T($"未匹配 {s.MissesToday} 次 · 前 {week.Count} 天日均 {runs / (double)week.Count:0.#} 次，未匹配率 {(runs + misses == 0 ? 0 : misses * 100.0 / (runs + misses)):0}%",
                $"{s.MissesToday} misses · {week.Count}-day avg {runs / (double)week.Count:0.#}, miss rate {(runs + misses == 0 ? 0 : misses * 100.0 / (runs + misses)):0}%");
        int patterns = s.Commands.Select(c => c.Pattern).Distinct().Count();
        int pinned = s.Commands.Count(c => c.Scope.Length == 0 && c.Pinned);
        int flexible = s.Commands.Count(c => c.Scope.Length == 0 && !c.Pinned);
        BoundValue.Text = patterns.ToString();
        BoundSub.Text = Loc.T($"{pinned} 个固定 · {flexible} 个可按程序改", $"{pinned} pinned · {flexible} overridable");
        int apps = s.ContextApps.Count;
        int replaces = s.Commands.Count(c => c.Scope.Length > 0 && s.Commands.Global(c.Pattern) is not null);
        WeekValue.Text = apps.ToString();
        WeekSub.Text = replaces > 0
            ? Loc.T($"{replaces} 处替换了全局含义", $"{replaces} override(s) of globals")
            : s.ActivationFailedToday > 0
                ? Loc.T($"今日激活失败 {s.ActivationFailedToday} 次", $"{s.ActivationFailedToday} activation failure(s) today")
                : Loc.T("程序列表独立于指令保存", "App list is stored separately from commands");
    }

    private void RefreshRoute()
    {
        _routeSync = true;
        RouteList.SelectedIndex = (int)_c.Settings.InputMode;
        _routeSync = false;
        HookState.Text = _c.HookInstalled ? "已安装" : "未安装";
        HookState.Foreground = Ui.Brush(_c.HookInstalled ? "MintBrush" : "CoralBrush");
        RouteActive.Text = _c.ActiveInputText;
    }

    private void RouteChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_routeSync || RouteList.SelectedIndex < 0) return;
        _c.SetInputMode((MouseInputMode)RouteList.SelectedIndex);
    }

    private void ReinstallClick(object sender, RoutedEventArgs e) => _c.ReinstallHook();

    // ---------- live pulse ----------

    private void RefreshLive()
    {
        var s = _c.Settings;
        string seq = _c.Sequence;
        bool pressing = _c.PressStartMs is not null;
        var phase = _c.Phase;
        string hit = _c.HitPattern;
        for (int i = 0; i < _slots.Length; i++)
        {
            var slot = _slots[i];
            string pulse = i < seq.Length ? seq[i].ToString() : "";
            var state = pulse.Length > 0 ? SlotState.Filled
                : pressing && i == seq.Length ? SlotState.Pressing
                : seq.Length > 0 && i == seq.Length ? SlotState.Next
                : SlotState.Empty;
            if (phase is LivePhase.Miss or LivePhase.Matched && i < hit.Length)
            {
                pulse = hit[i].ToString();
                state = phase == LivePhase.Miss ? SlotState.Miss : SlotState.Filled;
            }
            slot.Pulse = pulse;
            slot.State = state;
        }

        string chip, chipTone = "", state1, sub;
        bool check = false;
        if (!_c.Enabled) { chip = "已暂停"; state1 = "监听已暂停"; sub = "正常鼠标操作已恢复，在左下角重新开启监听"; }
        else switch (phase)
        {
            case LivePhase.Press: chip = "按住中"; chipTone = "live"; state1 = $"第 {seq.Length + 1} 位 · 正在计时"; sub = "松开后按时长判定为 · 或 —"; break;
            case LivePhase.Typing: chip = "输入中"; chipTone = "live"; state1 = $"已输入 {seq.Length} 位";
                sub = _c.IsChordMode ? "松开右键立即提交；滚动滚轮取消" : $"停顿 {s.SequenceGapMs} ms 后自动提交；在本窗口按 Esc 可取消"; break;
            case LivePhase.Armed: chip = "已激活"; chipTone = "live"; state1 = "指令模式已激活"; sub = "7 秒内用左键输入节奏，不输入会自动退出"; break;
            case LivePhase.Matched when _c.HitCommand is { } cmd:
                chip = "已匹配"; chipTone = "live"; check = true; state1 = "已执行 · " + cmd.Name;
                sub = $"{Loc.Kind(cmd.ActionKind)} · {PulseController.TargetText(cmd)}"; break;
            case LivePhase.Miss: chip = "未匹配"; chipTone = "bad"; state1 = "未找到匹配的指令"; sub = "可以在指令库中为这个节奏绑定动作"; break;
            case LivePhase.Reset: chip = "已重置"; chipTone = "bad"; state1 = "本次输入无效"; sub = _c.PhaseMessage; break;
            default:
                chip = "待命"; state1 = "等待输入";
                sub = _c.IsChordMode ? "按住右键，用左键敲节奏；松开右键立即执行"
                    : s.InputMode == MouseInputMode.Middle ? $"静止长按中键 {s.MiddleHoldMs} ms 激活，然后用左键输入节奏"
                    : "按住侧键输入节奏；松开后停顿即提交";
                break;
        }
        var (fg, border, bg) = chipTone switch
        {
            "live" => ("MintBrush", "MintLine30Brush", "MintDimBrush"),
            "bad" => ("CoralBrush", "CoralLine30Brush", "CoralDimBrush"),
            _ => ("Text2Brush", "Line2Brush", "SurfaceBrush")
        };
        StateChip.Background = Ui.Brush(bg);
        StateChip.BorderBrush = Ui.Brush(border);
        StateChipText.Foreground = StateChipDot.Fill = Ui.Brush(fg);
        StateChipText.Text = chip;
        StateIcon.Visibility = check ? Visibility.Visible : Visibility.Collapsed;
        StateText.Text = state1;
        SubText.Text = sub;
        HintText.Text = s.InputMode == MouseInputMode.Middle ? "当前：中键长按 + 左键" : "当前：" + _c.ActiveInputText;

        Ruler.ShortMax = s.ShortMaxMs;
        Ruler.LongMin = s.LongMinMs;
        Ruler.LongMax = s.LongMaxMs;
        GapText.Text = $"{s.SequenceGapMs} ms";
        RefreshCandidates(seq);
        UpdateMotion();
    }

    private void RefreshCandidates(string seq)
    {
        Candidates.Children.Clear();
        var commands = _c.Settings.Commands;
        Candidates.Children.Add(new TextBlock
        {
            Text = seq.Length > 0 ? "匹配候选" : "已绑定节奏", FontSize = 11, Foreground = Ui.Brush("Text3Brush"),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 6)
        });
        if (seq.Length > 0)
        {
            var matches = commands.Where(c => c.Pattern.StartsWith(seq, StringComparison.Ordinal)).OrderBy(c => c.Pattern.Length).ToList();
            if (matches.Count == 0) Candidates.Children.Add(CandidateChip(null, "没有以此开头的指令", false, false));
            foreach (var c in matches) Candidates.Children.Add(CandidateChip(c.Pattern, c.Name, true, c.Pattern == seq));
        }
        else foreach (var c in commands) Candidates.Children.Add(CandidateChip(c.Pattern, c.Name, false, false));
    }

    private static Border CandidateChip(string? pattern, string name, bool hit, bool exact)
    {
        var row = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (pattern is not null)
            row.Children.Add(new PatternGlyph { Pattern = pattern, Foreground = Ui.Brush(hit ? "MintBrush" : "Text3Brush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        row.Children.Add(new TextBlock { Text = name, FontSize = 11.5, Foreground = Ui.Brush(hit ? "TextBrush" : "Text2Brush"), VerticalAlignment = VerticalAlignment.Center });
        return new Border
        {
            Height = 26, Padding = new Thickness(10, 0, 10, 0), CornerRadius = new CornerRadius(8), BorderThickness = new Thickness(1),
            BorderBrush = Ui.Brush(hit ? "MintLine35Brush" : "LineBrush"), Background = Ui.Brush(exact ? "MintDimBrush" : "Shade20Brush"),
            Margin = new Thickness(0, 0, 6, 6), Child = row
        };
    }

    /// <summary>Per-frame: needle position while pressing and the pause-to-submit progress.</summary>
    private void UpdateMotion()
    {
        var s = _c.Settings;
        long now = PulseController.NowMs;
        if (_c.PressStartMs is long start)
        {
            double d = now - start;
            Ruler.NeedleValue = d;
            Ruler.NeedleText = $"{d:0} ms";
            Ruler.NeedleBrush = Ui.Brush(d <= s.ShortMaxMs ? "MintBrush" : d < s.LongMinMs || d > s.LongMaxMs ? "CoralBrush" : "BlueBrush");
        }
        else if (_c.LastDurationMs is double last)
        {
            Ruler.NeedleValue = last;
            Ruler.NeedleText = $"{last:0} ms → " + (_c.LastPulse switch { '.' => "·", '-' => "—", _ => "无效" });
            Ruler.NeedleBrush = System.Windows.Media.Brushes.White;
        }
        else Ruler.NeedleValue = double.NaN;

        double fraction = _c.PressStartMs is null && _c.LastReleaseMs is long release ? Math.Min(1, (now - release) / (double)s.SequenceGapMs) : 0;
        GapFill.Width = GapTrack.ActualWidth * fraction;
    }

    private void SimDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        SimButton.CaptureMouse();
        _c.SimulateDown();
    }

    private void SimUp(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        SimButton.ReleaseMouseCapture();
        _c.SimulateUp();
    }

    private void CancelClick(object sender, RoutedEventArgs e) => _c.CancelInput();

    // ---------- recent runs & log ----------

    private void RefreshActivity()
    {
        if (!_activityDirty) return;
        _activityDirty = false;
        ActivityList.Children.Clear();
        var items = _c.Activity.Take(5).ToList();
        if (items.Count == 0)
        {
            ActivityList.Children.Add(new TextBlock
            {
                Text = "还没有执行记录。按住侧键输入节奏，或在左侧点「按住模拟」试一试。", FontSize = 12, Foreground = Ui.Brush("Text3Brush"),
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 6)
            });
            return;
        }
        for (int i = 0; i < items.Count; i++) ActivityList.Children.Add(ActivityRow(items[i], i == items.Count - 1));
    }

    private static UIElement ActivityRow(ActivityEntry a, bool last)
    {
        var (icon, fg, bg) = a.Type switch
        {
            ActivityType.Ok => (a.Kind is { } k ? KindChip.IconOf(k) : IconKind.Check, "MintBrush", "MintDimBrush"),
            ActivityType.Gesture => (Directions.Icon(a.Direction), "BlueBrush", "BlueDimBrush"),
            ActivityType.Miss or ActivityType.Failed => (IconKind.X, "CoralBrush", "CoralDimBrush"),
            _ => (IconKind.Info, "Text3Brush", "Surface2Brush")
        };
        var tile = new Border
        {
            Width = 30, Height = 30, CornerRadius = new CornerRadius(9), Background = Ui.Brush(bg),
            Child = new Icon { Kind = icon, Size = 15, Foreground = Ui.Brush(fg), HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }
        };
        var sub = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 0) };
        void Text(string text) => sub.Children.Add(new TextBlock { Text = text, FontSize = 11.5, Foreground = Ui.Brush("Text3Brush"), Margin = new Thickness(0, 0, 7, 0), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        void Glyph(string pattern) => sub.Children.Add(new PatternGlyph { Pattern = pattern, Foreground = Ui.Brush("Text2Brush"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 7, 0) });
        string ContextNote()
        {
            if (a.Context.Length == 0 || a.Context == ContextNames.Desktop) return a.Own ? "" : Loc.T("全局", "Global");
            return a.Own
                ? Loc.T($"{a.Context} 中的含义", $"Meaning in {a.Context}")
                : Loc.T($"全局 · 在 {a.Context}", $"Global · in {a.Context}");
        }
        switch (a.Type)
        {
            case ActivityType.Ok:
                Glyph(a.Pattern); Text("·"); Text(a.Kind is { } kind ? Loc.Kind(kind) : ""); Text("·"); Text(ContextNote()); break;
            case ActivityType.Gesture:
                Text($"手势 · 向{Directions.Zh(a.Direction)}"); Text("·"); Text(a.Target); break;
            case ActivityType.Miss:
                Glyph(a.Pattern);
                Text(a.Context.Length > 0 && a.Context != ContextNames.Desktop
                    ? Loc.T($"· {a.Context} 中没有这个节奏", $"· no match in {a.Context}")
                    : "· 没有绑定此节奏");
                break;
            case ActivityType.Failed:
                if (a.Pattern.Length > 0) Glyph(a.Pattern);
                Text(a.Target); break;
            default: Text("系统"); break;
        }
        var texts = new StackPanel { Margin = new Thickness(11, 0, 11, 0), VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock { Text = a.Title, FontSize = 13, FontWeight = FontWeights.Medium, TextTrimming = TextTrimming.CharacterEllipsis });
        texts.Children.Add(sub);
        var time = new TextBlock
        {
            Text = a.Time.ToString("HH:mm:ss"), FontFamily = (System.Windows.Media.FontFamily)System.Windows.Application.Current.Resources["MonoFont"],
            FontSize = 11, Foreground = Ui.Brush("Text3Brush"), VerticalAlignment = VerticalAlignment.Center
        };
        var row = new DockPanel();
        DockPanel.SetDock(tile, Dock.Left);
        DockPanel.SetDock(time, Dock.Right);
        row.Children.Add(tile);
        row.Children.Add(time);
        row.Children.Add(texts);
        return new Border { Padding = new Thickness(0, 9, 0, 9), BorderBrush = Ui.Brush("LineBrush"), BorderThickness = new Thickness(0, 0, 0, last ? 0 : 1), Child = row, ClipToBounds = true };
    }

    private void RefreshLog()
    {
        if (!_logDirty) return;
        _logDirty = false;
        LogList.Children.Clear();
        foreach (var entry in _c.Log)
        {
            var line = new TextBlock { TextTrimming = TextTrimming.CharacterEllipsis, LineHeight = 21, Foreground = Ui.Brush("Text2Brush") };
            line.Inlines.Add(new Run(entry.Time.ToString("HH:mm:ss.fff")) { Foreground = Ui.Brush("Text4Brush") });
            line.Inlines.Add(new Run(" " + entry.Text));
            if (entry.Note.Length > 0)
            {
                string tone = entry.Tone switch { LogTone.Hit => "MintBrush", LogTone.Err => "CoralBrush", LogTone.Pass => "Text3Brush", _ => "Text2Brush" };
                line.Inlines.Add(new Run(" " + entry.Note) { Foreground = Ui.Brush(tone) });
            }
            else if (entry.Tone == LogTone.Err) line.Inlines.LastInline.Foreground = Ui.Brush("CoralBrush");
            LogList.Children.Add(line);
        }
    }

    private void CopyLogClick(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Windows.Clipboard.SetText(string.Join(Environment.NewLine, _c.Log.Select(l => l.ToString())));
            _shell.Toast("诊断日志已复制");
        }
        catch (Exception ex) { _shell.Toast("复制失败：" + ex.Message, IconKind.Alert); }
    }

    private void NewCommandClick(object sender, RoutedEventArgs e)
    {
        _shell.Commands.BeginNew();
        _shell.Navigate(Page.Commands);
    }

    private void OpenCommandsClick(object sender, RoutedEventArgs e) => _shell.Navigate(Page.Commands);
}
