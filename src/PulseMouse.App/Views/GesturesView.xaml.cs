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
}
