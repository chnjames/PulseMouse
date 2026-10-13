using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Media.Animation;
using Forms = System.Windows.Forms;

namespace PulseMouse.App;

public enum FeedbackMode { Armed, Typing, Matched, Failed }

/// <summary>Non-activating click-through capsule on the active monitor, above its taskbar.</summary>
public sealed class FeedbackWindow : Window
{
    private readonly TextBlock _pattern;
    private readonly TextBlock _target;
    private readonly Border _surface;
    private readonly DispatcherTimer _hideTimer;
    private string _position = "Center";
    private bool _breathing = true;

    public FeedbackWindow()
    {
        Width = 372;
        Height = 64;
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Topmost = true;
        Focusable = false;

        _pattern = new TextBlock { FontFamily = new System.Windows.Media.FontFamily("Cascadia Mono, Consolas"), FontSize = 18, FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(81, 230, 202)),
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0, 15, 0) };
        _target = new TextBlock { FontFamily = new System.Windows.Media.FontFamily("Segoe UI Variable Text, Segoe UI"), FontSize = 12, Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(242, 247, 248)),
            TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        var content = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(_pattern, Dock.Left);
        content.Children.Add(_pattern);
        content.Children.Add(_target);
        _surface = new Border { CornerRadius = new CornerRadius(32),
            Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(248, 14, 18, 22)),
            BorderBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(92, 81, 230, 202)),
            BorderThickness = new Thickness(1), Padding = new Thickness(20, 0, 18, 0), Child = content,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 26, ShadowDepth = 6, Opacity = 0.42, Color = System.Windows.Media.Colors.Black } };
        Content = _surface;
        _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1250) };
        _hideTimer.Tick += (_, _) => { _hideTimer.Stop(); Hide(); };
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            var style = GetWindowLongPtr(hwnd, -20);
            SetWindowLongPtr(hwnd, -20, style | (nint)0x20 | (nint)0x80 | (nint)0x08000000);
        };
    }

    public void Configure(string position, bool breathing)
    {
        _position = position is "Left" or "Right" ? position : "Center";
        _breathing = breathing;
    }

    public void ShowState(string pattern, string target, FeedbackMode mode)
    {
        _hideTimer.Stop();
        _pattern.Text = pattern;
        _target.Text = target;
        _surface.BorderBrush = new SolidColorBrush(mode == FeedbackMode.Failed
            ? System.Windows.Media.Color.FromRgb(226, 126, 104)
            : System.Windows.Media.Color.FromArgb(112, 81, 230, 202));
        var p = Forms.Cursor.Position;
        var workingArea = Forms.Screen.FromPoint(p).WorkingArea;
        // WinForms works in physical screen coordinates; WPF positions are DIPs.
        // Convert from pixels to DIPs using the presentation source where available.
        var src = PresentationSource.FromVisual(this);
        double scaleX = src?.CompositionTarget?.TransformFromDevice.M11 ?? 1;
        double scaleY = src?.CompositionTarget?.TransformFromDevice.M22 ?? 1;
        Left = _position switch
        {
            "Left" => (workingArea.Left + 28) * scaleX,
            "Right" => (workingArea.Right - 28 - Width / scaleX) * scaleX,
            _ => (workingArea.Left + (workingArea.Width - Width / scaleX) / 2) * scaleX
        };
        Top = (workingArea.Bottom - 22) * scaleY - Height;
        if (!IsVisible) Show();
        _surface.BeginAnimation(OpacityProperty, null);
        if (_breathing && mode is FeedbackMode.Armed or FeedbackMode.Typing)
        {
            _surface.BeginAnimation(OpacityProperty, new DoubleAnimation(0.72, 1.0, TimeSpan.FromMilliseconds(900))
            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
        }
        else _surface.Opacity = 1;
        if (mode == FeedbackMode.Matched || mode == FeedbackMode.Failed)
        {
            _hideTimer.Interval = TimeSpan.FromMilliseconds(mode == FeedbackMode.Failed ? 1600 : 1150);
            _hideTimer.Start();
        }
    }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern nint GetWindowLongPtr(nint hwnd, int index);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
}
