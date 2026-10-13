using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace PulseMouse.App.Controls;

/// <summary>Listening indicator: glowing mint dot that beats while on, grey when paused.</summary>
public sealed class StatusDot : FrameworkElement
{
    private readonly Ellipse _dot = new();

    public static readonly DependencyProperty IsOnProperty = DependencyProperty.Register(nameof(IsOn), typeof(bool), typeof(StatusDot),
        new FrameworkPropertyMetadata(true, (d, _) => ((StatusDot)d).Refresh()));
    public static readonly DependencyProperty DiameterProperty = DependencyProperty.Register(nameof(Diameter), typeof(double), typeof(StatusDot),
        new FrameworkPropertyMetadata(8.0, FrameworkPropertyMetadataOptions.AffectsMeasure, (d, _) => ((StatusDot)d).Refresh()));

    public bool IsOn { get => (bool)GetValue(IsOnProperty); set => SetValue(IsOnProperty, value); }
    public double Diameter { get => (double)GetValue(DiameterProperty); set => SetValue(DiameterProperty, value); }

    public StatusDot()
    {
        AddVisualChild(_dot);
        VerticalAlignment = VerticalAlignment.Center;
        Refresh();
    }

    protected override int VisualChildrenCount => 1;
    protected override Visual GetVisualChild(int index) => _dot;

    protected override System.Windows.Size MeasureOverride(System.Windows.Size availableSize)
    {
        _dot.Measure(new System.Windows.Size(Diameter, Diameter));
        return new System.Windows.Size(Diameter, Diameter);
    }

    protected override System.Windows.Size ArrangeOverride(System.Windows.Size finalSize)
    {
        _dot.Arrange(new Rect(0, 0, Diameter, Diameter));
        return finalSize;
    }

    private void Refresh()
    {
        _dot.Width = _dot.Height = Diameter;
        _dot.BeginAnimation(OpacityProperty, null);
        if (IsOn)
        {
            _dot.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x51, 0xE6, 0xCA));
            _dot.Effect = new DropShadowEffect { Color = System.Windows.Media.Color.FromRgb(0x51, 0xE6, 0xCA), BlurRadius = 10, ShadowDepth = 0, Opacity = 0.65 };
            _dot.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0.55, TimeSpan.FromMilliseconds(1200))
            { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut } });
        }
        else
        {
            _dot.Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x6B, 0x74, 0x78));
            _dot.Effect = null;
            _dot.Opacity = 1;
        }
    }
}
