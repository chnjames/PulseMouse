using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Drawing = System.Drawing;

namespace PulseMouse.App.Services;

public enum IconTone { Active, Paused, Blocked }

/// <summary>
/// The dot-dash mark: one short and one long pulse on a rounded tile.
/// The app icon (Assets/PulseMouse.ico, regenerated with tools/IconGen) uses the dark tile; the tray uses the light tile.
/// </summary>
public static class AppIcon
{
    public static readonly int[] IcoSizes = { 16, 20, 24, 32, 40, 48, 64, 256 };

    private static readonly Color Glyph = Color.FromRgb(0x06, 0x12, 0x0F);

    private static (Color Top, Color Bottom) Palette(IconTone tone) => tone switch
    {
        IconTone.Paused => (Color.FromRgb(0xC4, 0xCD, 0xD0), Color.FromRgb(0x86, 0x93, 0x97)),
        IconTone.Blocked => (Color.FromRgb(0xF7, 0xDB, 0x8F), Color.FromRgb(0xE0, 0xA5, 0x3A)),
        _ => (Color.FromRgb(0x8E, 0xF6, 0xE2), Color.FromRgb(0x2C, 0xC6, 0xA9)),
    };

    private static DrawingVisual Mark(int size, IconTone tone, bool dark)
    {
        var visual = new DrawingVisual();
        using var dc = visual.RenderOpen();
        bool small = size <= 40;
        double inset = small ? 0 : Math.Round(size * 0.055);
        double w = size - 2 * inset;
        var tile = new Rect(inset, inset, w, w);
        double radius = w * 0.27;

        var (top, bottom) = Palette(tone);
        Brush tileFill = dark
            ? new LinearGradientBrush(Color.FromRgb(0x1D, 0x27, 0x2B), Color.FromRgb(0x0A, 0x0F, 0x12), new Point(0.2, 0), new Point(0.8, 1))
            : new LinearGradientBrush(top, bottom, new Point(0.2, 0), new Point(0.8, 1));
        dc.DrawRoundedRectangle(tileFill, null, tile, radius, radius);
        if (dark)
        {
            var glow = new RadialGradientBrush(Color.FromArgb(0x38, top.R, top.G, top.B), Color.FromArgb(0, top.R, top.G, top.B)) { RadiusX = 0.5, RadiusY = 0.32 };
            dc.DrawRoundedRectangle(glow, null, tile, radius, radius);
        }
        if (!small)
        {
            byte alpha = dark ? (byte)0x40 : (byte)0x80;
            var rim = new LinearGradientBrush(Color.FromArgb(alpha, 0xFF, 0xFF, 0xFF), Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF), new Point(0, 0), new Point(0, 0.6));
            double t = Math.Max(1, w / 96);
            var r = new Rect(tile.X + t / 2, tile.Y + t / 2, tile.Width - t, tile.Height - t);
            dc.DrawRoundedRectangle(null, new Pen(rim, t), r, radius - t / 2, radius - t / 2);
        }

        // Small sizes use hand-tuned whole pixels so the mark stays crisp in the tray.
        var (d, gap, dash) = size switch
        {
            <= 16 => (4.0, 2.0, 8.0),
            <= 20 => (4.0, 2.0, 10.0),
            <= 24 => (4.0, 2.0, 12.0),
            <= 32 => (6.0, 3.0, 15.0),
            <= 40 => (6.0, 3.0, 17.0),
            _ => (w * 0.14, w * 0.075, w * 0.38),
        };

        double x = inset + (w - d - gap - dash) / 2, y = inset + (w - d) / 2;
        Brush ink = dark ? new LinearGradientBrush(top, bottom, 90) : new SolidColorBrush(Glyph);
        dc.DrawEllipse(ink, null, new Point(x + d / 2, y + d / 2), d / 2, d / 2);
        dc.DrawRoundedRectangle(ink, null, new Rect(x + d + gap, y, dash, d), d / 2, d / 2);
        return visual;
    }

    public static BitmapSource Render(int size, IconTone tone = IconTone.Active, bool dark = false)
    {
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(Mark(size, tone, dark));
        bitmap.Freeze();
        return bitmap;
    }

    /// <summary>Icons expect straight alpha; feeding premultiplied pixels darkens the anti-aliased rim.</summary>
    private static BitmapSource Straight(BitmapSource source)
    {
        var converted = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);
        converted.Freeze();
        return converted;
    }

    /// <summary>Tray uses the light tile: its colour carries the state, and a dark tile vanishes on a dark taskbar.</summary>
    public static Drawing.Icon CreateTrayIcon(IconTone tone, int size)
    {
        // Bitmap.GetHicon premultiplies the colours, which darkens the anti-aliased rim; load from .ico bytes instead.
        using var ms = new MemoryStream(EncodeIco(tone, dark: false, size));
        return new Drawing.Icon(ms, size, size);
    }

    /// <summary>PNG-compressed .ico with one frame per size (supported since Windows Vista).</summary>
    public static byte[] EncodeIco(IconTone tone, bool dark, params int[] sizes)
    {
        var frames = sizes.Select(s =>
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(Straight(Render(s, tone, dark))));
            using var png = new MemoryStream();
            encoder.Save(png);
            return (Size: s, Data: png.ToArray());
        }).ToList();

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        bw.Write((ushort)0); bw.Write((ushort)1); bw.Write((ushort)frames.Count);
        int offset = 6 + 16 * frames.Count;
        foreach (var (size, data) in frames)
        {
            bw.Write((byte)(size >= 256 ? 0 : size)); bw.Write((byte)(size >= 256 ? 0 : size));
            bw.Write((byte)0); bw.Write((byte)0);
            bw.Write((ushort)1); bw.Write((ushort)32);
            bw.Write(data.Length); bw.Write(offset);
            offset += data.Length;
        }
        foreach (var (_, data) in frames) bw.Write(data);
        bw.Flush();
        return ms.ToArray();
    }
}
