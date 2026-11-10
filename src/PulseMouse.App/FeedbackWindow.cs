using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using PulseMouse.App.Controls;
using Forms = System.Windows.Forms;

namespace PulseMouse.App;

public enum FeedbackMode { Armed, Typing, Matched, Failed }

/// <summary>Pulse Capsule: non-activating, click-through pill on the cursor's monitor, above its taskbar.</summary>
public sealed class FeedbackWindow : Window
{
    private const double PillWidth = 380, PillHeight = 56, Shadow = 36, BottomGap = 22, SideGap = 28;
    /// <summary>Room reserved above the pill for the rhythm hint card.</summary>
    private const double HintArea = 250;

    private readonly Border _hint;
    private readonly StackPanel _hintRows = new();
    private readonly StackPanel _hintTyped = new() { Orientation = System.Windows.Controls.Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
    private readonly PatternGlyph _hintTypedGlyph = new() { GlyphSize = GlyphSize.Small, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 0, 0) };
    private readonly TextBlock _hintFooter;

    private readonly Border _pill;
    private readonly Border _orb;
    private readonly Icon _orbIcon = new() { Size = 18, HorizontalAlignment = System.Windows.HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    private readonly PatternGlyph _glyph = new() { GlyphSize = GlyphSize.Medium, VerticalAlignment = VerticalAlignment.Center, MinWidth = 24 };
    private readonly FrameworkElement _divider;
    private readonly TrackedText _label;
    private readonly TextBlock _text;
    private readonly TranslateTransform _slide = new();
    private readonly DispatcherTimer _hideTimer;
    private readonly TrackedText _hintTitle;
    private string _position = "Center";
    private bool _breathing = true;
    private int _version;
    private bool _fading;

    private static SolidColorBrush B(byte a, byte r, byte g, byte b) { var x = new SolidColorBrush(System.Windows.Media.Color.FromArgb(a, r, g, b)); x.Freeze(); return x; }
    private static readonly SolidColorBrush Mint = B(255, 0x51, 0xE6, 0xCA), MintDim = B(0x1F, 0x51, 0xE6, 0xCA), MintEdge = B(0x70, 0x51, 0xE6, 0xCA),
        Coral = B(255, 0xE2, 0x7E, 0x68), CoralDim = B(0x21, 0xE2, 0x7E, 0x68), CoralEdge = B(0xCC, 0xE2, 0x7E, 0x68),
        Amber = B(255, 0xF0, 0xB4, 0x5A), AmberDim = B(0x28, 0xF0, 0xB4, 0x5A), AmberEdge = B(0xCC, 0xF0, 0xB4, 0x5A);

    public FeedbackWindow()
    {
        Width = PillWidth + Shadow * 2;
        Height = HintArea + PillHeight + Shadow * 2;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;
        IsHitTestVisible = false;

        _orb = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(18), Child = _orbIcon, VerticalAlignment = VerticalAlignment.Center };
        _divider = new Border { Width = 1, Height = 22, Background = B(0x1F, 255, 255, 255), Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
        _label = new TrackedText
        {
            Text = "PULSE CAPSULE", FontSize = 9.5, Tracking = 1.14, FontWeight = FontWeights.SemiBold,
            FontFamily = new System.Windows.Media.FontFamily("Cascadia Mono, Consolas"), Foreground = B(255, 0x6E, 0x7D, 0x82),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left
        };
        _text = new TextBlock
        {
            FontSize = 13, Foreground = B(255, 0xF2, 0xF7, 0xF8), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 1, 0, 0),
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI")
        };
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(_label);
        texts.Children.Add(_text);

        var row = new DockPanel { LastChildFill = true };
        _glyph.Margin = new Thickness(12, 0, 0, 0);
        DockPanel.SetDock(_orb, Dock.Left);
        DockPanel.SetDock(_glyph, Dock.Left);
        DockPanel.SetDock(_divider, Dock.Left);
        row.Children.Add(_orb);
        row.Children.Add(_glyph);
        row.Children.Add(_divider);
        row.Children.Add(texts);

        _pill = new Border
        {
            Width = PillWidth, Height = PillHeight, CornerRadius = new CornerRadius(28),
            Background = B(0xF7, 14, 18, 22), BorderBrush = MintEdge, BorderThickness = new Thickness(1),
            Padding = new Thickness(10, 0, 22, 0), Child = row, Opacity = 0, RenderTransform = _slide,
            Effect = new DropShadowEffect { BlurRadius = 34, ShadowDepth = 12, Direction = 270, Opacity = 0.5, Color = Colors.Black }
        };

        var hintHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        _hintTyped.Children.Add(new TextBlock { Text = "已输入", FontSize = 11.5, Foreground = B(255, 0x6E, 0x7D, 0x82), VerticalAlignment = VerticalAlignment.Center });
        _hintTyped.Children.Add(_hintTypedGlyph);
        _hintTypedGlyph.Foreground = Mint;
        DockPanel.SetDock(_hintTyped, Dock.Right);
        hintHeader.Children.Add(_hintTyped);
        _hintTitle = new TrackedText
        {
            Text = "可用节奏", FontSize = 9.5, Tracking = 1.14, FontWeight = FontWeights.SemiBold,
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI"), Foreground = B(255, 0x6E, 0x7D, 0x82),
            VerticalAlignment = VerticalAlignment.Center
        };
        hintHeader.Children.Add(_hintTitle);
        _hintFooter = new TextBlock { FontSize = 11.5, Foreground = B(255, 0x6E, 0x7D, 0x82), Margin = new Thickness(0, 6, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        _hint = new Border
        {
            Width = PillWidth, CornerRadius = new CornerRadius(18), Background = B(0xF7, 14, 18, 22), BorderBrush = B(0x1F, 255, 255, 255),
            BorderThickness = new Thickness(1), Padding = new Thickness(18, 12, 18, 12), Margin = new Thickness(0, 0, 0, 10),
            Visibility = Visibility.Collapsed, Opacity = 0,
            Child = new StackPanel { Children = { hintHeader, _hintRows, _hintFooter } },
            Effect = new DropShadowEffect { BlurRadius = 34, ShadowDepth = 12, Direction = 270, Opacity = 0.5, Color = Colors.Black }
        };
        Content = new Grid
        {
            Margin = new Thickness(Shadow),
            Children = { new StackPanel { VerticalAlignment = VerticalAlignment.Bottom, Children = { _hint, _pill } } }
        };

        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1150) };
        _hideTimer.Tick += (_, _) => { _hideTimer.Stop(); FadeOut(); };
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var style = GetWindowLongPtr(hwnd, -20);
            // WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE
            SetWindowLongPtr(hwnd, -20, style | 0x20 | 0x80 | 0x08000000);
        };
    }

    public void Configure(string position, bool breathing)
    {
        _position = position is "Left" or "Right" ? position : "Center";
        _breathing = breathing;
    }

    public void ShowState(FeedbackMode mode, string pattern, string text, IconKind? icon, bool reminding = false, string? contextLine = null)
    {
        _hideTimer.Stop();
        bool failed = mode == FeedbackMode.Failed;
        var edge = failed ? CoralEdge : reminding ? AmberEdge : MintEdge;
        var dim = failed ? CoralDim : reminding ? AmberDim : MintDim;
        var fg = failed ? Coral : reminding ? Amber : Mint;
        _pill.BorderBrush = edge;
        _orb.Background = dim;
        _orbIcon.Foreground = _glyph.Foreground = fg;
        _label.Foreground = reminding ? Amber : B(255, 0x6E, 0x7D, 0x82);
        _label.Text = string.IsNullOrWhiteSpace(contextLine) ? "PULSE CAPSULE" : contextLine;
        _orbIcon.Kind = icon ?? mode switch
        {
            FeedbackMode.Matched => IconKind.Check,
            FeedbackMode.Failed => IconKind.X,
            FeedbackMode.Armed => IconKind.Dot,
            _ => IconKind.Mouse
        };
        _glyph.Pattern = pattern ?? "";
        _glyph.Visibility = string.IsNullOrEmpty(pattern) ? Visibility.Collapsed : Visibility.Visible;
        _text.Text = text;

        _version++;
        PlaceOnCursorScreen();
        bool wasVisible = IsVisible && !_fading;
        _fading = false;
        if (!IsVisible) Show();
        _pill.BeginAnimation(OpacityProperty, null);
        _slide.BeginAnimation(TranslateTransform.YProperty, null);
        _pill.Opacity = 1;
        _slide.Y = 0;
        if (!wasVisible)
        {
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            _pill.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });
            _slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(200)) { EasingFunction = ease });
        }
        if (_breathing && mode is FeedbackMode.Armed or FeedbackMode.Typing)
        {
            _pill.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.74, TimeSpan.FromMilliseconds(900))
            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
        }
        if (mode is FeedbackMode.Matched or FeedbackMode.Failed)
        {
            HideHint();
            // New-meaning reminder stays a bit longer (spec 5.8: ~2.2 s vs ~1.4 s).
            _hideTimer.Interval = TimeSpan.FromMilliseconds(failed ? 1600 : reminding ? 2200 : 1400);
            _hideTimer.Start();
        }
    }

    public void HideNow()
    {
        _hideTimer.Stop();
        HideHint();
        if (IsVisible) FadeOut();
    }

    public bool HintVisible => _hint.Visibility == Visibility.Visible && IsVisible && !_fading;

    /// <summary>Lists the rhythms still reachable from <paramref name="typed"/> above the pill (which must already be showing).</summary>
    public void ShowHint(string typed, IReadOnlyList<(string Pattern, string Name, string Tag)> items, int hidden, string? title = null)
    {
        _hintTitle.Text = string.IsNullOrWhiteSpace(title) ? "可用节奏" : title;
        _hintTyped.Visibility = typed.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _hintTypedGlyph.Pattern = typed;
        _hintRows.Children.Clear();
        foreach (var (pattern, name, tag) in items)
        {
            var row = new Grid { Height = 26 };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(78) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var glyph = new PatternGlyph { GlyphSize = GlyphSize.Small, Pattern = pattern, Foreground = Mint, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = System.Windows.HorizontalAlignment.Left };
            var label = new TextBlock
            {
                Text = name, FontSize = 13, Foreground = B(255, 0xF2, 0xF7, 0xF8), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                FontFamily = new System.Windows.Media.FontFamily("Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI")
            };
            var tagBlock = new TextBlock
            {
                Text = tag, FontSize = 11, Foreground = tag.Contains('替') ? Amber : B(255, 0x6E, 0x7D, 0x82),
                VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0)
            };
            Grid.SetColumn(label, 1);
            Grid.SetColumn(tagBlock, 2);
            row.Children.Add(glyph);
            row.Children.Add(label);
            if (tag.Length > 0) row.Children.Add(tagBlock);
            _hintRows.Children.Add(row);
        }
        _hintFooter.Text = items.Count == 0 ? "没有以此开头的指令" : hidden > 0 ? $"还有 {hidden} 个，继续敲以缩小范围" : "";
        _hintFooter.Visibility = _hintFooter.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (!IsVisible || _hint.Visibility == Visibility.Visible) return;
        _hint.Visibility = Visibility.Visible;
        _hint.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
    }

    public void HideHint()
    {
        _hint.BeginAnimation(OpacityProperty, null);
        _hint.Opacity = 0;
        _hint.Visibility = Visibility.Collapsed;
    }

    private void FadeOut()
    {
        int version = _version;
        _fading = true;
        var fade = new DoubleAnimation(_pill.Opacity, 0, TimeSpan.FromMilliseconds(200));
        fade.Completed += (_, _) =>
        {
            if (version != _version) return;
            _fading = false;
            _pill.BeginAnimation(OpacityProperty, null);
            _pill.Opacity = 0;
            Hide();
        };
        _pill.BeginAnimation(OpacityProperty, fade);
        _slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, 10, TimeSpan.FromMilliseconds(200)));
    }

    private void PlaceOnCursorScreen()
    {
        var cursor = Forms.Cursor.Position;
        var area = Forms.Screen.FromPoint(cursor).WorkingArea;
        // Screen coordinates are physical pixels; WPF positions are DIPs.
        var source = PresentationSource.FromVisual(this) ?? PresentationSource.FromVisual(System.Windows.Application.Current.MainWindow);
        double sx = source?.CompositionTarget?.TransformFromDevice.M11 ?? 1;
        double sy = source?.CompositionTarget?.TransformFromDevice.M22 ?? 1;
        double left = area.Left * sx, right = area.Right * sx, bottom = area.Bottom * sy;
        double pillLeft = _position switch
        {
            "Left" => left + SideGap,
            "Right" => right - SideGap - PillWidth,
            _ => left + (right - left - PillWidth) / 2
        };
        Left = pillLeft - Shadow;
        Top = bottom - BottomGap - PillHeight - Shadow - HintArea;
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
}
