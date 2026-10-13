using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace PulseMouse.App.Controls;

public enum SlotState { Empty, Next, Pressing, Filled, Miss, Conflict }

/// <summary>One position of a rhythm (hero slots and composer slots).</summary>
public sealed class PulseSlot : FrameworkElement
{
    public static readonly DependencyProperty StateProperty = DependencyProperty.Register(nameof(State), typeof(SlotState), typeof(PulseSlot),
        new FrameworkPropertyMetadata(SlotState.Empty, FrameworkPropertyMetadataOptions.AffectsRender, (d, _) => ((PulseSlot)d).UpdateAnimation()));
    public static readonly DependencyProperty PulseProperty = DependencyProperty.Register(nameof(Pulse), typeof(string), typeof(PulseSlot),
        new FrameworkPropertyMetadata("", FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty GlyphSizeProperty = DependencyProperty.Register(nameof(GlyphSize), typeof(GlyphSize), typeof(PulseSlot),
        new FrameworkPropertyMetadata(GlyphSize.Large, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty RadiusProperty = DependencyProperty.Register(nameof(Radius), typeof(double), typeof(PulseSlot),
        new FrameworkPropertyMetadata(12.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ShadedProperty = DependencyProperty.Register(nameof(Shaded), typeof(bool), typeof(PulseSlot),
        new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty GlowProperty = DependencyProperty.Register(nameof(Glow), typeof(double), typeof(PulseSlot),
        new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public SlotState State { get => (SlotState)GetValue(StateProperty); set => SetValue(StateProperty, value); }
    public string Pulse { get => (string)GetValue(PulseProperty); set => SetValue(PulseProperty, value); }
    public GlyphSize GlyphSize { get => (GlyphSize)GetValue(GlyphSizeProperty); set => SetValue(GlyphSizeProperty, value); }
    public double Radius { get => (double)GetValue(RadiusProperty); set => SetValue(RadiusProperty, value); }
    /// <summary>Hero slots have a dark fill; composer slots are transparent when empty.</summary>
    public bool Shaded { get => (bool)GetValue(ShadedProperty); set => SetValue(ShadedProperty, value); }
    public double Glow { get => (double)GetValue(GlowProperty); set => SetValue(GlowProperty, value); }

    private static SolidColorBrush B(byte a, uint rgb)
    {
        var brush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(a, (byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb));
        brush.Freeze();
        return brush;
    }
    private static readonly SolidColorBrush Mint = B(0xFF, 0x51E6CA), MintDim = B(0x1F, 0x51E6CA), Coral = B(0xFF, 0xE27E68), CoralDim = B(0x21, 0xE27E68),
        Dashed = B(0x1F, 0xFFFFFF), Shade = B(0x29, 0x000000), Mint30 = B(0x4D, 0x51E6CA), Mint35 = B(0x59, 0x51E6CA), Mint40 = B(0x66, 0x51E6CA), Coral40 = B(0x66, 0xE27E68);
    private static readonly DashStyle Dash = new([3.5, 3], 0);

    private void UpdateAnimation()
    {
        if (State == SlotState.Pressing)
            BeginAnimation(GlowProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(400)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase() });
        else
        {
            BeginAnimation(GlowProperty, null);
            Glow = 0;
        }
    }

    protected override void OnRender(DrawingContext dc)
    {
        var rect = new Rect(0.5, 0.5, Math.Max(0, ActualWidth - 1), Math.Max(0, ActualHeight - 1));
        double r = Radius;
        System.Windows.Media.Brush? fill = Shaded ? Shade : null;
        System.Windows.Media.Brush stroke = Dashed;
        bool dashed = true;
        System.Windows.Media.Brush glyph = Mint;
        switch (State)
        {
            case SlotState.Next: stroke = Mint40; break;
            case SlotState.Pressing: stroke = Mint; dashed = false; break;
            case SlotState.Filled: stroke = Shaded ? Mint35 : Mint30; fill = MintDim; dashed = false; break;
            case SlotState.Miss: stroke = Coral40; fill = CoralDim; glyph = Coral; break;
            case SlotState.Conflict: stroke = Coral40; fill = CoralDim; glyph = Coral; dashed = false; break;
        }
        if (State == SlotState.Filled && Shaded)
        {
            for (int i = 1; i <= 4; i++)
            {
                var halo = new SolidColorBrush(System.Windows.Media.Color.FromArgb((byte)(10 - i * 2), 0x51, 0xE6, 0xCA));
                dc.DrawRoundedRectangle(null, new System.Windows.Media.Pen(halo, 3), new Rect(rect.X - i * 3, rect.Y - i * 3, rect.Width + i * 6, rect.Height + i * 6), r + i * 3, r + i * 3);
            }
        }
        if (Glow > 0)
        {
            var ring = new SolidColorBrush(System.Windows.Media.Color.FromArgb((byte)(36 * Glow), 0x51, 0xE6, 0xCA));
            dc.DrawRoundedRectangle(null, new System.Windows.Media.Pen(ring, 4), new Rect(rect.X - 2.5, rect.Y - 2.5, rect.Width + 5, rect.Height + 5), r + 2.5, r + 2.5);
        }
        var pen = new System.Windows.Media.Pen(stroke, 1) { DashStyle = dashed ? Dash : DashStyles.Solid };
        dc.DrawRoundedRectangle(fill, pen, rect, r, r);
        if (!string.IsNullOrEmpty(Pulse))
        {
            double w = PatternGlyph.WidthOf(Pulse, GlyphSize);
            PatternGlyph.Draw(dc, Pulse, GlyphSize, glyph, (ActualWidth - w) / 2, ActualHeight / 2);
        }
    }
}
