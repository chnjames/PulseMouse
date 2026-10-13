using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace PulseMouse.App.Controls;

/// <summary>Press-duration scale: invalid / dot / ambiguous / dash / invalid zones with an optional needle.</summary>
public sealed class TimingRuler : FrameworkElement
{
    private const double BarTop = 20, BarHeight = 10, LabelTop = 38;

    public static readonly DependencyProperty ShortMaxProperty = Register(nameof(ShortMax), 300.0);
    public static readonly DependencyProperty LongMinProperty = Register(nameof(LongMin), 500.0);
    public static readonly DependencyProperty LongMaxProperty = Register(nameof(LongMax), 1600.0);
    public static readonly DependencyProperty NeedleValueProperty = Register(nameof(NeedleValue), double.NaN);
    public static readonly DependencyProperty NeedleTextProperty = DependencyProperty.Register(nameof(NeedleText), typeof(string), typeof(TimingRuler),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty NeedleBrushProperty = DependencyProperty.Register(nameof(NeedleBrush), typeof(System.Windows.Media.Brush), typeof(TimingRuler),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

    private static DependencyProperty Register(string name, double value) => DependencyProperty.Register(name, typeof(double), typeof(TimingRuler),
        new FrameworkPropertyMetadata(value, FrameworkPropertyMetadataOptions.AffectsRender));

    public double ShortMax { get => (double)GetValue(ShortMaxProperty); set => SetValue(ShortMaxProperty, value); }
    public double LongMin { get => (double)GetValue(LongMinProperty); set => SetValue(LongMinProperty, value); }
    public double LongMax { get => (double)GetValue(LongMaxProperty); set => SetValue(LongMaxProperty, value); }
    public double NeedleValue { get => (double)GetValue(NeedleValueProperty); set => SetValue(NeedleValueProperty, value); }
    public string NeedleText { get => (string)GetValue(NeedleTextProperty); set => SetValue(NeedleTextProperty, value); }
    public System.Windows.Media.Brush NeedleBrush { get => (System.Windows.Media.Brush)GetValue(NeedleBrushProperty); set => SetValue(NeedleBrushProperty, value); }

    private static readonly System.Windows.Media.Brush Track = Frozen(0x08, 255, 255, 255);
    private static readonly System.Windows.Media.Brush Invalid = Frozen(0x09, 255, 255, 255);
    private static readonly System.Windows.Media.Brush DotZone = Frozen(0x6B, 0x51, 0xE6, 0xCA);
    private static readonly System.Windows.Media.Brush DashZone = Frozen(0x61, 0x75, 0xB7, 0xFF);
    private static readonly System.Windows.Media.Brush Hatch = Frozen(0x4D, 0xE2, 0x7E, 0x68);
    private static readonly System.Windows.Media.Brush LabelBrush = Frozen(0xFF, 0x6E, 0x7D, 0x82);
    private static readonly System.Windows.Media.Brush AmbLabel = Frozen(0xD9, 0xE2, 0x7E, 0x68);
    private static readonly System.Windows.Media.Brush TextBrush = Frozen(0xFF, 0xF2, 0xF7, 0xF8);

    private static System.Windows.Media.Brush Frozen(byte a, byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(a, r, g, b));
        brush.Freeze();
        return brush;
    }

    protected override System.Windows.Size MeasureOverride(System.Windows.Size availableSize)
        => new(double.IsInfinity(availableSize.Width) ? 400 : availableSize.Width, 54);

    public double ScaleMax => Math.Max(1800, LongMax + 200);

    private double X(double v) => Math.Clamp(v / ScaleMax, 0, 1) * ActualWidth;

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth;
        if (w <= 0) return;
        var bar = new Rect(0, BarTop, w, BarHeight);
        dc.PushClip(new RectangleGeometry(bar, 5, 5));
        dc.DrawRectangle(Track, null, bar);
        dc.DrawRectangle(Invalid, null, Zone(0, 35));
        dc.DrawRoundedRectangle(DotZone, null, Zone(35, ShortMax), 2, 2);
        var amb = Zone(ShortMax, LongMin);
        dc.PushClip(new RectangleGeometry(amb));
        var hatchPen = new System.Windows.Media.Pen(Hatch, 2.83);
        for (double x = amb.Left - BarHeight; x < amb.Right + BarHeight; x += 8)
            dc.DrawLine(hatchPen, new System.Windows.Point(x, BarTop + BarHeight), new System.Windows.Point(x + BarHeight, BarTop));
        dc.Pop();
        dc.DrawRoundedRectangle(DashZone, null, Zone(LongMin, LongMax), 2, 2);
        dc.DrawRectangle(Invalid, null, new Rect(X(LongMax), BarTop, Math.Max(0, w - X(LongMax)), BarHeight));
        dc.Pop();

        Label(dc, $"· 短按 ≤{ShortMax:0}", (35 + ShortMax) / 2, LabelBrush);
        Label(dc, "无效", (ShortMax + LongMin) / 2, AmbLabel);
        Label(dc, $"— 长按 {LongMin:0}–{LongMax:0} ms", (LongMin + LongMax) / 2, LabelBrush);

        if (double.IsNaN(NeedleValue)) return;
        double nx = X(NeedleValue);
        var glow = NeedleBrush.Clone();
        glow.Opacity = 0.25;
        dc.DrawRoundedRectangle(glow, null, new Rect(nx - 3, BarTop - 6, 6, BarHeight + 12), 3, 3);
        dc.DrawRoundedRectangle(NeedleBrush, null, new Rect(nx - 1, BarTop - 6, 2, BarHeight + 12), 1, 1);
        if (!string.IsNullOrEmpty(NeedleText))
        {
            var ft = Text(NeedleText, 10.5, TextBrush, true);
            double tx = Math.Clamp(nx - ft.Width / 2, 0, Math.Max(0, w - ft.Width));
            dc.DrawText(ft, new System.Windows.Point(tx, BarTop - 6 - 3 - ft.Height));
        }
    }

    private Rect Zone(double from, double to) => new(X(from), BarTop, Math.Max(0, X(to) - X(from)), BarHeight);

    private void Label(DrawingContext dc, string text, double value, System.Windows.Media.Brush brush)
    {
        var ft = Text(text, 11, brush, false);
        double x = Math.Clamp(X(value) - ft.Width / 2, 0, Math.Max(0, ActualWidth - ft.Width));
        dc.DrawText(ft, new System.Windows.Point(x, LabelTop));
    }

    private FormattedText Text(string text, double size, System.Windows.Media.Brush brush, bool mono) => new(text, CultureInfo.CurrentUICulture,
        System.Windows.FlowDirection.LeftToRight,
        new Typeface(new System.Windows.Media.FontFamily(mono ? "Cascadia Mono, Consolas" : "Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI"),
            FontStyles.Normal, mono ? FontWeights.SemiBold : FontWeights.Normal, FontStretches.Normal),
        size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
}
