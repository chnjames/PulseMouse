using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;

namespace PulseMouse.App.Controls;

public enum IconKind
{
    None, Dashboard, Commands, Gestures, Workflows, Settings, Search, Plus, Play, Trash,
    App, Folder, Url, Workflow, Copy, Check, X, Up, Down, Left, Right, ChevronUp, ChevronDown,
    Mouse, Refresh, Back, Shield, Info, Download, Upload, External, Dot, Alert, Keyboard
}

/// <summary>Stroke icons drawn on a 24-unit grid, matching the 1.7 stroke weight of the design spec.</summary>
public sealed class Icon : FrameworkElement
{
    public static readonly DependencyProperty KindProperty = DependencyProperty.Register(nameof(Kind), typeof(IconKind), typeof(Icon),
        new FrameworkPropertyMetadata(IconKind.None, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(nameof(Size), typeof(double), typeof(Icon),
        new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(typeof(Icon),
        new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    public IconKind Kind { get => (IconKind)GetValue(KindProperty); set => SetValue(KindProperty, value); }
    public double Size { get => (double)GetValue(SizeProperty); set => SetValue(SizeProperty, value); }
    public System.Windows.Media.Brush Foreground { get => (System.Windows.Media.Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    protected override System.Windows.Size MeasureOverride(System.Windows.Size availableSize)
        => Kind == IconKind.None ? new System.Windows.Size(0, 0) : new System.Windows.Size(Size, Size);

    protected override void OnRender(DrawingContext dc)
    {
        if (Kind == IconKind.None || !Shapes.TryGetValue(Kind, out var parts)) return;
        var pen = new System.Windows.Media.Pen(Foreground, 1.7) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
        dc.PushTransform(new ScaleTransform(Size / 24, Size / 24));
        foreach (var (geometry, filled) in parts)
            dc.DrawGeometry(filled ? Foreground : null, filled ? null : pen, geometry);
        dc.Pop();
    }

    public static Geometry GeometryOf(IconKind kind) => Shapes.TryGetValue(kind, out var parts)
        ? new GeometryGroup { Children = new GeometryCollection(parts.Select(p => p.Geometry)) }
        : Geometry.Empty;

    private static readonly Dictionary<IconKind, (Geometry Geometry, bool Filled)[]> Shapes = new()
    {
        [IconKind.Dashboard] = [R(3.5, 3.5, 7, 7, 2), R(13.5, 3.5, 7, 7, 2), R(3.5, 13.5, 7, 7, 2), R(13.5, 13.5, 7, 7, 2)],
        [IconKind.Commands] = [C(5, 8, 1.4), P("M10 8h9"), P("M5 16h7"), C(17.5, 16, 1.4)],
        [IconKind.Gestures] = [P("M12 4v16M4 12h16"), P("M9.5 6.5L12 4l2.5 2.5M9.5 17.5L12 20l2.5-2.5M6.5 9.5L4 12l2.5 2.5M17.5 9.5L20 12l-2.5 2.5")],
        [IconKind.Workflows] = [R(3, 4, 7, 5, 1.5), R(14, 15, 7, 5, 1.5), P("M6.5 9v3.5a2 2 0 0 0 2 2H14")],
        [IconKind.Workflow] = [R(3, 4, 7, 5, 1.5), R(14, 15, 7, 5, 1.5), P("M6.5 9v3.5a2 2 0 0 0 2 2H14")],
        [IconKind.Settings] = [C(12, 12, 3), P("M19.4 15a1.7 1.7 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.8-.3 1.7 1.7 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1A1.7 1.7 0 0 0 9 19.4a1.7 1.7 0 0 0-1.8.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0 .3-1.8 1.7 1.7 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1A1.7 1.7 0 0 0 4.6 9a1.7 1.7 0 0 0-.3-1.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.8.3H9a1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.8-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.8V9a1.7 1.7 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1z")],
        [IconKind.Search] = [C(11, 11, 6.5), P("M20 20l-4.2-4.2")],
        [IconKind.Plus] = [P("M12 5v14M5 12h14")],
        [IconKind.Play] = [P("M7.5 5.5l11 6.5-11 6.5z")],
        [IconKind.Trash] = [P("M4 7h16M10 11v6M14 11v6M6 7l1 13h10l1-13M9 7V4h6v3")],
        [IconKind.App] = [R(3.5, 4.5, 17, 15, 2.5), P("M3.5 9h17")],
        [IconKind.Folder] = [P("M3.5 7.5a2 2 0 0 1 2-2h4l2 2.5h7a2 2 0 0 1 2 2v7.5a2 2 0 0 1-2 2h-13a2 2 0 0 1-2-2z")],
        [IconKind.Url] = [C(12, 12, 8.5), P("M3.5 12h17M12 3.5c2.5 2.6 3.5 5.4 3.5 8.5s-1 5.9-3.5 8.5c-2.5-2.6-3.5-5.4-3.5-8.5s1-5.9 3.5-8.5z")],
        [IconKind.Copy] = [R(8, 8, 12, 12, 2), P("M16 8V6a2 2 0 0 0-2-2H6a2 2 0 0 0-2 2v8a2 2 0 0 0 2 2h2")],
        [IconKind.Check] = [P("M5 12.5l4.5 4.5L19 7.5")],
        [IconKind.X] = [P("M6 6l12 12M18 6L6 18")],
        [IconKind.Up] = [P("M12 19V5M6 11l6-6 6 6")],
        [IconKind.Down] = [P("M12 5v14M6 13l6 6 6-6")],
        [IconKind.Left] = [P("M19 12H5M11 6l-6 6 6 6")],
        [IconKind.Right] = [P("M5 12h14M13 6l6 6-6 6")],
        [IconKind.ChevronUp] = [P("M7 14l5-5 5 5")],
        [IconKind.ChevronDown] = [P("M7 10l5 5 5-5")],
        [IconKind.Mouse] = [R(6.5, 3, 11, 18, 5.5), P("M12 7v4")],
        [IconKind.Refresh] = [P("M20 11a8 8 0 1 0-2.3 5.7"), P("M20 5v6h-6")],
        [IconKind.Back] = [P("M9 5h11v14H9l-6-7z"), P("M12 9.5l5 5M17 9.5l-5 5")],
        [IconKind.Shield] = [P("M12 3l7.5 3v5.5c0 4.5-3.2 8.2-7.5 9.5-4.3-1.3-7.5-5-7.5-9.5V6z")],
        [IconKind.Info] = [C(12, 12, 8.5), P("M12 11v5M12 8h.01")],
        [IconKind.Download] = [P("M12 4v11M7 10l5 5 5-5M5 20h14")],
        [IconKind.Upload] = [P("M12 16V5M7 10l5-5 5 5M5 20h14")],
        [IconKind.External] = [P("M14 4h6v6M20 4l-9 9M18 14v5a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1V7a1 1 0 0 1 1-1h5")],
        [IconKind.Dot] = [(Freeze(new EllipseGeometry(new System.Windows.Point(12, 12), 4, 4)), true)],
        [IconKind.Alert] = [P("M12 4l9 16H3z"), P("M12 10v4M12 17h.01")],
        [IconKind.Keyboard] = [R(3, 6, 18, 12, 2.5), P("M7 10h.01M10.3 10h.01M13.7 10h.01M17 10h.01M8 14.5h8")],
    };

    private static (Geometry, bool) P(string data) => (Freeze(Geometry.Parse(data)), false);
    private static (Geometry, bool) R(double x, double y, double w, double h, double r) => (Freeze(new RectangleGeometry(new Rect(x, y, w, h), r, r)), false);
    private static (Geometry, bool) C(double cx, double cy, double r) => (Freeze(new EllipseGeometry(new System.Windows.Point(cx, cy), r, r)), false);
    private static Geometry Freeze(Geometry g) { g.Freeze(); return g; }
}
