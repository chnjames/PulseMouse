using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using PulseMouse.App.Services;
using PulseMouse.Core;

namespace PulseMouse.App.Controls;

/// <summary>Attached properties consumed by the templates in Themes/Controls.xaml.</summary>
public static class Ui
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.RegisterAttached("Icon", typeof(IconKind), typeof(Ui), new FrameworkPropertyMetadata(IconKind.None));
    public static IconKind GetIcon(DependencyObject d) => (IconKind)d.GetValue(IconProperty);
    public static void SetIcon(DependencyObject d, IconKind value) => d.SetValue(IconProperty, value);

    public static readonly DependencyProperty IconSizeProperty = DependencyProperty.RegisterAttached("IconSize", typeof(double), typeof(Ui), new FrameworkPropertyMetadata(15.0));
    public static double GetIconSize(DependencyObject d) => (double)d.GetValue(IconSizeProperty);
    public static void SetIconSize(DependencyObject d, double value) => d.SetValue(IconSizeProperty, value);

    public static readonly DependencyProperty CornerRadiusProperty = DependencyProperty.RegisterAttached("CornerRadius", typeof(CornerRadius), typeof(Ui), new FrameworkPropertyMetadata(new CornerRadius(10)));
    public static CornerRadius GetCornerRadius(DependencyObject d) => (CornerRadius)d.GetValue(CornerRadiusProperty);
    public static void SetCornerRadius(DependencyObject d, CornerRadius value) => d.SetValue(CornerRadiusProperty, value);

    public static readonly DependencyProperty PlaceholderProperty = DependencyProperty.RegisterAttached("Placeholder", typeof(string), typeof(Ui), new FrameworkPropertyMetadata(""));
    public static string GetPlaceholder(DependencyObject d) => (string)d.GetValue(PlaceholderProperty);
    public static void SetPlaceholder(DependencyObject d, string value) => d.SetValue(PlaceholderProperty, value);

    public static readonly DependencyProperty MetaProperty = DependencyProperty.RegisterAttached("Meta", typeof(string), typeof(Ui), new FrameworkPropertyMetadata(""));
    public static string GetMeta(DependencyObject d) => (string)d.GetValue(MetaProperty);
    public static void SetMeta(DependencyObject d, string value) => d.SetValue(MetaProperty, value);

    public static readonly DependencyProperty IsDashedProperty = DependencyProperty.RegisterAttached("IsDashed", typeof(bool), typeof(Ui), new FrameworkPropertyMetadata(false));
    public static bool GetIsDashed(DependencyObject d) => (bool)d.GetValue(IsDashedProperty);
    public static void SetIsDashed(DependencyObject d, bool value) => d.SetValue(IsDashedProperty, value);

    public static System.Windows.Media.Brush Brush(string key) => (System.Windows.Media.Brush)System.Windows.Application.Current.Resources[key];
}

public enum ChipKind { App, Folder, Url, Workflow, Muted, Shortcut, Repeat }

/// <summary>Small colored tag: action kinds, counts and statuses.</summary>
public sealed class KindChip : Border
{
    private readonly Icon _icon = new() { Size = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 5, 0) };
    private readonly TextBlock _text = new() { FontSize = 11, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };

    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(nameof(Kind), typeof(ChipKind), typeof(KindChip),
        new FrameworkPropertyMetadata(ChipKind.Muted, (d, _) => ((KindChip)d).Refresh()));
    public static readonly DependencyProperty TextProperty = DependencyProperty.Register(nameof(Text), typeof(string), typeof(KindChip),
        new FrameworkPropertyMetadata(null, (d, _) => ((KindChip)d).Refresh()));

    public ChipKind Kind { get => (ChipKind)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public string? Text { get => (string?)GetValue(TextProperty); set => SetValue(TextProperty, value); }

    public KindChip()
    {
        Height = 22;
        Padding = new Thickness(8, 0, 8, 0);
        CornerRadius = new CornerRadius(7);
        VerticalAlignment = VerticalAlignment.Center;
        HorizontalAlignment = System.Windows.HorizontalAlignment.Left;
        var row = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        row.Children.Add(_icon);
        row.Children.Add(_text);
        Child = row;
        Refresh();
    }

    public static ChipKind From(ActionKind kind) => kind switch
    {
        ActionKind.Folder => ChipKind.Folder,
        ActionKind.Url => ChipKind.Url,
        ActionKind.Workflow => ChipKind.Workflow,
        ActionKind.Shortcut => ChipKind.Shortcut,
        ActionKind.Repeat => ChipKind.Repeat,
        _ => ChipKind.App
    };

    public static IconKind IconOf(ActionKind kind) => kind switch
    {
        ActionKind.Folder => IconKind.Folder,
        ActionKind.Url => IconKind.Url,
        ActionKind.Workflow => IconKind.Workflow,
        ActionKind.Shortcut => IconKind.Keyboard,
        ActionKind.Repeat => IconKind.Refresh,
        _ => IconKind.App
    };

    private void Refresh()
    {
        var (fg, bg, icon, name) = Kind switch
        {
            ChipKind.App => ("BlueBrush", "BlueDimBrush", IconKind.App, Loc.Kind(ActionKind.Application)),
            ChipKind.Folder => ("AmberBrush", "AmberDimBrush", IconKind.Folder, Loc.Kind(ActionKind.Folder)),
            ChipKind.Url => ("MintBrush", "MintDimBrush", IconKind.Url, Loc.Kind(ActionKind.Url)),
            ChipKind.Workflow => ("VioletBrush", "VioletDimBrush", IconKind.Workflow, Loc.Kind(ActionKind.Workflow)),
            ChipKind.Shortcut => ("RoseBrush", "RoseDimBrush", IconKind.Keyboard, Loc.Kind(ActionKind.Shortcut)),
            ChipKind.Repeat => ("AmberBrush", "AmberDimBrush", IconKind.Refresh, Loc.Kind(ActionKind.Repeat)),
            _ => ("Text3Brush", "Surface2Brush", IconKind.None, "")
        };
        Background = Ui.Brush(bg);
        _text.Foreground = _icon.Foreground = Ui.Brush(fg);
        _icon.Kind = icon;
        _icon.Visibility = icon == IconKind.None ? Visibility.Collapsed : Visibility.Visible;
        _text.Text = Text ?? name;
    }
}

/// <summary>Eyebrow, H1, description and right-aligned actions shared by every page.</summary>
public sealed class PageHeader : ContentControl
{
    public static readonly DependencyProperty EyebrowProperty = DependencyProperty.Register(nameof(Eyebrow), typeof(string), typeof(PageHeader), new PropertyMetadata(""));
    public static readonly DependencyProperty HeadingProperty = DependencyProperty.Register(nameof(Heading), typeof(string), typeof(PageHeader), new PropertyMetadata(""));
    public static readonly DependencyProperty DescriptionProperty = DependencyProperty.Register(nameof(Description), typeof(string), typeof(PageHeader), new PropertyMetadata(""));
    public string Eyebrow { get => (string)GetValue(EyebrowProperty); set => SetValue(EyebrowProperty, value); }
    public string Heading { get => (string)GetValue(HeadingProperty); set => SetValue(HeadingProperty, value); }
    public string Description { get => (string)GetValue(DescriptionProperty); set => SetValue(DescriptionProperty, value); }
}
