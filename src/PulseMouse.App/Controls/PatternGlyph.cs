using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace PulseMouse.App.Controls;

public enum GlyphSize { Small, Medium, Large }

/// <summary>Renders a rhythm such as ".-" as rounded dots and dashes.</summary>
public sealed class PatternGlyph : FrameworkElement
{
    public static readonly DependencyProperty PatternProperty = DependencyProperty.Register(nameof(Pattern), typeof(string), typeof(PatternGlyph),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty GlyphSizeProperty = DependencyProperty.Register(nameof(GlyphSize), typeof(GlyphSize), typeof(PatternGlyph),
        new FrameworkPropertyMetadata(GlyphSize.Small, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(typeof(PatternGlyph),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    public string Pattern { get => (string)GetValue(PatternProperty); set => SetValue(PatternProperty, value); }
    public GlyphSize GlyphSize { get => (GlyphSize)GetValue(GlyphSizeProperty); set => SetValue(GlyphSizeProperty, value); }
    public System.Windows.Media.Brush Foreground { get => (System.Windows.Media.Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    public static (double Height, double Dot, double Dash, double Gap) Metrics(GlyphSize size) => size switch
    {
        GlyphSize.Large => (12, 12, 34, 8),
        GlyphSize.Medium => (8, 8, 22, 6),
        _ => (6, 6, 16, 4)
    };

    public static double WidthOf(string pattern, GlyphSize size)
    {
        if (string.IsNullOrEmpty(pattern)) return 0;
        var m = Metrics(size);
        return pattern.Sum(c => c == '.' ? m.Dot : m.Dash) + m.Gap * (pattern.Length - 1);
    }

    public static void Draw(DrawingContext dc, string pattern, GlyphSize size, System.Windows.Media.Brush brush, double x, double centerY)
    {
        var m = Metrics(size);
        foreach (var c in pattern)
        {
            double w = c == '.' ? m.Dot : m.Dash;
            dc.DrawRoundedRectangle(brush, null, new Rect(x, centerY - m.Height / 2, w, m.Height), m.Height / 2, m.Height / 2);
            x += w + m.Gap;
        }
    }

    protected override System.Windows.Size MeasureOverride(System.Windows.Size availableSize)
        => string.IsNullOrEmpty(Pattern) ? new System.Windows.Size(0, 0) : new System.Windows.Size(WidthOf(Pattern, GlyphSize), Metrics(GlyphSize).Height);

    protected override void OnRender(DrawingContext dc)
    {
        if (string.IsNullOrEmpty(Pattern)) return;
        Draw(dc, Pattern, GlyphSize, Foreground, 0, ActualHeight / 2);
    }
}
