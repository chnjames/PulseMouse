using System.IO;
using System.Windows;
using System.Windows.Controls;
using PulseMouse.App.Controls;
using PulseMouse.App.Services;
using PulseMouse.Core;

namespace PulseMouse.App.Views;

public partial class GesturesView : System.Windows.Controls.UserControl, IPageView
{
    private readonly MainWindow _shell;
    private readonly PulseController _c;
    private string _direction = "Up";
    private bool _syncing;

    public GesturesView(MainWindow shell)
    {
        InitializeComponent();
        _shell = shell;
        _c = shell.Controller;
        Relocalize();
    }

    private GestureBinding? Bound(string direction) => _c.Settings.Gestures.FirstOrDefault(g => g.Direction == direction);

    private RadioButton Tile(string direction) => direction switch
    {
        "Down" => TileDown, "Left" => TileLeft, "Right" => TileRight, _ => TileUp
    };

    public void OnShown()
    {
        _syncing = true;
        EnabledSwitch.IsChecked = _c.Settings.GesturesEnabled;
        DistanceSlider.Value = _c.Settings.GestureMinDistancePx;
        DistanceText.Text = $"{_c.Settings.GestureMinDistancePx} px";
        ClickMaxSlider.Value = _c.Settings.MiddleClickMaxMs;
        ClickMaxText.Text = $"{_c.Settings.MiddleClickMaxMs} ms";
        PassThroughSlider.Value = _c.Settings.MiddlePassThroughMs;
        PassThroughText.Text = _c.Settings.MiddlePassThroughMs == 0 ? "0 (off)" : $"{_c.Settings.MiddlePassThroughMs} ms";
        PreserveSwitch.IsChecked = _c.Settings.PreserveMiddleClick;
        Tile(_direction).IsChecked = true;
        _syncing = false;
        RefreshPad();
        LoadDraft();
    }

    public void OnHidden() { }

    public void Relocalize()
    {
        Header.Eyebrow = Loc.Eyebrow(Page.Gestures);
        Header.Heading = Loc.Nav(Page.Gestures);
        Header.Description = Loc.Description(Page.Gestures);
        DistanceName.Text = Loc.T("最小移动距离", "Min distance");
        DistanceDesc.Text = Loc.T("按下后拖动达到此距离 = 方向手势", "Drag at least this far after pressing = gesture");
        ClickMaxName.Text = Loc.T("单击上限", "Click max");
        ClickMaxDesc.Text = Loc.T("静止且短于此时长松开 → 补发系统中键单击", "Released still and under this → replay system middle-click");
        PassThroughName.Text = Loc.T("静止放行", "Hold-still pass-through");
        PassThroughDesc.Text = Loc.T("静止按住达到此时长 → 交给系统；0 = 关闭", "Held still this long → hand to the system; 0 = off");
        PreserveName.Text = Loc.T("保留中键单击", "Preserve middle click");
        PreserveDesc.Text = Loc.T("关闭后短点也不补发（进阶）", "Off = short clicks are not replayed (advanced)");
        DecisionNote.Text = Loc.T(
            "短点仍是系统中键；按下就拖是手势，不会带动页面滚动；静止按住一会儿再拖，仍是系统的自动滚动或平移。",
            "Short clicks stay a system middle-click; press-and-drag is a gesture with no autoscroll; hold still, then drag for system autoscroll / pan.");
        CadNoteRun.Text = Loc.T("CAD、Blender、Figma 等按下中键就拖动平移的程序：平移会变成手势。建议加入", "Apps that pan with press-and-drag middle (CAD, Blender, Figma): panning becomes a gesture. Add them to ");
        ExcludedLinkRun.Text = Loc.T("排除的程序", "Excluded apps");
        CadNoteTailRun.Text = Loc.T("，或先静止按住再拖。", ", or hold still before dragging.");
    }

    public void Reload() { if (IsLoaded) OnShown(); }

    private void RefreshPad()
    {
        bool enabled = _c.Settings.GesturesEnabled;
        Pad.Opacity = enabled ? 1 : 0.4;
        Pad.IsHitTestVisible = enabled;
        foreach (var direction in Directions.All)
        {
            var tile = Tile(direction);
            var gesture = Bound(direction);
            bool selected = direction == _direction;
            Ui.SetIsDashed(tile, gesture is null);
            var content = new StackPanel { HorizontalAlignment = System.Windows.HorizontalAlignment.Center };
            content.Children.Add(new Icon { Kind = Directions.Icon(direction), Size = 20, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, Foreground = Ui.Brush(selected ? "MintBrush" : "Text2Brush") });
            content.Children.Add(new TextBlock { Text = "向" + Directions.Zh(direction), FontSize = 11, Foreground = Ui.Brush("Text3Brush"), HorizontalAlignment = System.Windows.HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) });
            content.Children.Add(new TextBlock
            {
                Text = gesture?.Name ?? "未绑定", FontSize = 12.5, MaxWidth = 124, TextTrimming = TextTrimming.CharacterEllipsis,
                FontWeight = gesture is null ? FontWeights.Medium : FontWeights.SemiBold,
                Foreground = Ui.Brush(gesture is null ? "Text4Brush" : "TextBrush"), HorizontalAlignment = System.Windows.HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0)
            });
            tile.Content = content;
        }
        BoundChip.Text = $"{Directions.All.Count(d => Bound(d) is not null)} / 4 已绑定";
    }

    private void TileChecked(object sender, RoutedEventArgs e)
    {
        if (_syncing || sender is not RadioButton { Tag: string direction }) return;
        _direction = direction;
        RefreshPad();
        LoadDraft();
    }

    private ActionKind DraftKind => TargetField.KindAt(Math.Clamp(KindSeg.SelectedIndex, 0, 2));

    private void LoadDraft()
    {
        var gesture = Bound(_direction);
        _syncing = true;
        NameBox.Text = gesture?.Name ?? "";
        TargetBox.Text = gesture?.Target ?? "";
        KindSeg.SelectedIndex = gesture is null ? 0 : Math.Min((int)gesture.ActionKind, 2);
        _syncing = false;
        bool bound = gesture is not null;
        InspectorEyebrow.Text = bound ? "EDIT GESTURE" : "NEW GESTURE";
        InspectorIcon.Kind = Directions.Icon(_direction);
        InspectorTitle.Text = $"向{Directions.Zh(_direction)}手势";
        InspectorChip.Kind = bound ? KindChip.From(gesture!.ActionKind) : ChipKind.Muted;
        InspectorChip.Text = bound ? null : "未绑定";
        ClearButton.Visibility = TestButton.Visibility = bound ? Visibility.Visible : Visibility.Collapsed;
        RefreshTarget();
    }

    private void KindChanged(object sender, SelectionChangedEventArgs e) { if (IsLoaded && !_syncing) RefreshTarget(); }

    private void RefreshTarget()
    {
        var (label, placeholder, hint, browse) = TargetField.Config(DraftKind);
        TargetLabel.Text = label;
        Ui.SetPlaceholder(TargetBox, placeholder);
        TargetHint.Text = hint;
        BrowseButton.Visibility = browse ? Visibility.Visible : Visibility.Collapsed;
    }

    private void BrowseClick(object sender, RoutedEventArgs e)
    {
        var path = TargetField.Browse(DraftKind, TargetBox.Text.Trim());
        if (path is null) return;
        TargetBox.Text = path;
        if (string.IsNullOrWhiteSpace(NameBox.Text))
            NameBox.Text = "打开 " + (DraftKind == ActionKind.Folder ? new DirectoryInfo(path).Name : Path.GetFileNameWithoutExtension(path));
    }

    private void SaveClick(object sender, RoutedEventArgs e)
    {
        string name = NameBox.Text.Trim(), target = TargetBox.Text.Trim();
        if (name.Length == 0) { _shell.Toast("请填写名称", IconKind.Alert); return; }
        if (PulseController.ValidateTarget(DraftKind, target) is { } error) { _shell.Toast(error, IconKind.Alert); return; }
        _c.Settings.Gestures.RemoveAll(g => g.Direction == _direction);
        _c.Settings.Gestures.Add(new GestureBinding { Direction = _direction, Name = name, ActionKind = DraftKind, Target = target });
        _c.NotifyBindingsChanged();
        _shell.Toast($"已保存向{Directions.Zh(_direction)}手势");
        RefreshPad();
        LoadDraft();
    }

    private void ClearClick(object sender, RoutedEventArgs e)
    {
        _c.Settings.Gestures.RemoveAll(g => g.Direction == _direction);
        _c.NotifyBindingsChanged();
        _shell.Toast("已清除绑定");
        RefreshPad();
        LoadDraft();
    }

    private void TestClick(object sender, RoutedEventArgs e)
    {
        if (Bound(_direction) is not { } gesture) return;
        _c.AddLog("识别中键方向手势 " + _direction, "[测试]", LogTone.None);
        _ = _c.ExecuteGesture(gesture);
    }

    private void EnabledClick(object sender, RoutedEventArgs e)
    {
        _c.Settings.GesturesEnabled = EnabledSwitch.IsChecked == true;
        _c.ApplySettings();
        _c.Save();
        RefreshPad();
    }

    private void DistanceChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncing || !IsLoaded) return;
        int value = (int)Math.Round(DistanceSlider.Value);
        DistanceText.Text = $"{value} px";
        _c.Settings.GestureMinDistancePx = value;
        _c.ApplySettings();
        _c.SaveSoon();
    }

    private void ClickMaxChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncing || !IsLoaded) return;
        int value = (int)Math.Round(ClickMaxSlider.Value);
        ClickMaxText.Text = $"{value} ms";
        _c.Settings.MiddleClickMaxMs = value;
        _c.Settings.MiddlePassThroughMs = MiddleDecision.NormalizePassThroughMs(_c.Settings.MiddlePassThroughMs, value);
        if (!_syncing)
        {
            _syncing = true;
            PassThroughSlider.Value = _c.Settings.MiddlePassThroughMs;
            PassThroughText.Text = _c.Settings.MiddlePassThroughMs == 0 ? "0 (off)" : $"{_c.Settings.MiddlePassThroughMs} ms";
            _syncing = false;
        }
        _c.ApplySettings();
        _c.SaveSoon();
    }

    private void PassThroughChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_syncing || !IsLoaded) return;
        int value = (int)Math.Round(PassThroughSlider.Value);
        // Snap low non-zero values up so the control can still reach 0 to disable.
        if (value is > 0 and < 400) value = 400;
        value = MiddleDecision.NormalizePassThroughMs(value, _c.Settings.MiddleClickMaxMs);
        PassThroughText.Text = value == 0 ? "0 (off)" : $"{value} ms";
        _c.Settings.MiddlePassThroughMs = value;
        _c.ApplySettings();
        _c.SaveSoon();
    }

    private void PreserveClick(object sender, RoutedEventArgs e)
    {
        _c.Settings.PreserveMiddleClick = PreserveSwitch.IsChecked == true;
        _c.ApplySettings();
        _c.Save();
    }

    private void ExcludedLinkClick(object sender, RoutedEventArgs e) => _shell.Navigate(Page.Settings);
}
