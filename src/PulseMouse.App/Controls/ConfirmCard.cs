using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using PulseMouse.App.Services;

namespace PulseMouse.App.Controls;

/// <summary>Confirmation dialog body shared by the in-window overlay and the standalone confirm window.</summary>
public sealed class ConfirmCard : Border
{
    public event Action<bool>? Closed;
    private readonly System.Windows.Controls.Button _ok;

    public ConfirmCard(ConfirmRequest request)
    {
        Width = 420;
        CornerRadius = new CornerRadius(14);
        Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x12, 0x17, 0x1C));
        BorderBrush = Ui.Brush("Line2Brush");
        BorderThickness = new Thickness(1);
        Effect = new DropShadowEffect { BlurRadius = 70, ShadowDepth = 24, Direction = 270, Opacity = 0.65, Color = Colors.Black };

        var tile = new Border
        {
            Width = 36, Height = 36, CornerRadius = new CornerRadius(10), VerticalAlignment = VerticalAlignment.Top,
            Background = Ui.Brush(request.Danger ? "CoralDimBrush" : "AmberDimBrush"),
            Child = new Icon
            {
                Kind = request.Danger ? IconKind.Trash : IconKind.Shield, Size = 18, HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center, Foreground = Ui.Brush(request.Danger ? "CoralBrush" : "AmberBrush")
            }
        };
        var text = new StackPanel { Margin = new Thickness(14, 0, 0, 0) };
        text.Children.Add(new TextBlock
        {
            Text = request.Title, FontFamily = (System.Windows.Media.FontFamily)FindResource("DisplayFont"), FontSize = 15, FontWeight = FontWeights.SemiBold,
            Foreground = Ui.Brush("TextBrush"), Margin = new Thickness(0, 0, 0, 6), TextWrapping = TextWrapping.Wrap
        });
        text.Children.Add(new TextBlock { Text = request.Body, FontSize = 12.5, Foreground = Ui.Brush("Text2Brush"), TextWrapping = TextWrapping.Wrap, LineHeight = 20 });
        if (request.Items is { Count: > 0 })
        {
            var list = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
            for (int i = 0; i < request.Items.Count; i++)
            {
                list.Children.Add(new TextBlock
                {
                    Text = $"{i + 1}. {request.Items[i]}", FontFamily = (System.Windows.Media.FontFamily)FindResource("MonoFont"), FontSize = 11.5,
                    Foreground = Ui.Brush("Text2Brush"), LineHeight = 21, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(4, 0, 0, 0)
                });
            }
            text.Children.Add(list);
        }
        var body = new DockPanel { Margin = new Thickness(20, 20, 20, 16) };
        DockPanel.SetDock(tile, Dock.Left);
        body.Children.Add(tile);
        body.Children.Add(text);

        var cancel = new System.Windows.Controls.Button { Content = "取消", Style = (Style)FindResource("Btn"), Margin = new Thickness(0, 0, 8, 0) };
        _ok = new System.Windows.Controls.Button { Content = request.OkText, Style = (Style)FindResource(request.Danger ? "BtnDanger" : "BtnPrimary"), IsDefault = true };
        cancel.Click += (_, _) => Closed?.Invoke(false);
        _ok.Click += (_, _) => Closed?.Invoke(true);
        var footer = new Border
        {
            Background = Ui.Brush("Shade22Brush"), BorderBrush = Ui.Brush("LineBrush"), BorderThickness = new Thickness(0, 1, 0, 0),
            Padding = new Thickness(16, 12, 16, 12), CornerRadius = new CornerRadius(0, 0, 13, 13),
            Child = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Children = { cancel, _ok } }
        };

        var root = new DockPanel();
        DockPanel.SetDock(footer, Dock.Bottom);
        root.Children.Add(footer);
        root.Children.Add(body);
        Child = root;
        Loaded += (_, _) => _ok.Focus();
        PreviewKeyDown += (_, e) => { if (e.Key == Key.Escape) { e.Handled = true; Closed?.Invoke(false); } };
    }
}

/// <summary>Topmost confirm window used when the main window is hidden in the tray.</summary>
public sealed class ConfirmWindow : Window
{
    public bool Result { get; private set; }

    public ConfirmWindow(ConfirmRequest request)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Topmost = true;
        ShowInTaskbar = true;
        Title = "PulseMouse";
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = (System.Windows.Media.FontFamily)FindResource("BodyFont");
        var card = new ConfirmCard(request) { Margin = new Thickness(40, 20, 40, 60) };
        card.Closed += ok => { Result = ok; Close(); };
        Content = card;
    }
}
