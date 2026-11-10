using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PulseMouse.App.Services;

// Usage: IconGen <ico-path> [preview-png-path] [logo-png-path]
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        var ico = Path.GetFullPath(args.Length > 0 ? args[0] : "PulseMouse.ico");
        Directory.CreateDirectory(Path.GetDirectoryName(ico)!);
        File.WriteAllBytes(ico, AppIcon.EncodeIco(IconTone.Active, dark: true, AppIcon.IcoSizes));
        Console.WriteLine("ico: " + ico);
        if (args.Length > 1)
        {
            Preview(args[1]);
            TrayPreview(Path.ChangeExtension(args[1], null) + "-tray.png");
        }
        if (args.Length > 2)
        {
            Logo(args[2]);
        }
    }

    /// <summary>Draws the real tray HICONs (the path NotifyIcon uses) on light and dark taskbars, zoomed 8x.</summary>
    private static void TrayPreview(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var tones = new[] { IconTone.Active, IconTone.Paused, IconTone.Blocked };
        using var strip = new System.Drawing.Bitmap(3 * 40 + 8, 2 * 32);
        using (var g = System.Drawing.Graphics.FromImage(strip))
        {
            g.Clear(System.Drawing.Color.FromArgb(0xD8, 0xE8, 0xDC));
            g.FillRectangle(new System.Drawing.SolidBrush(System.Drawing.Color.FromArgb(0x20, 0x20, 0x20)), 0, 32, strip.Width, 32);
            for (int row = 0; row < 2; row++)
                for (int i = 0; i < tones.Length; i++)
                {
                    using var icon = AppIcon.CreateTrayIcon(tones[i], 24);
                    g.DrawIcon(icon, 8 + i * 40, row * 32 + 4);
                }
        }
        using var zoom = new System.Drawing.Bitmap(strip.Width * 8, strip.Height * 8);
        using (var g = System.Drawing.Graphics.FromImage(zoom))
        {
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
            g.DrawImage(strip, 0, 0, zoom.Width, zoom.Height);
        }
        zoom.Save(path);
        Console.WriteLine("tray: " + path);
    }

    private static void Pixelated(DrawingContext dc, BitmapSource source, Rect rect)
    {
        var group = new DrawingGroup();
        RenderOptions.SetBitmapScalingMode(group, BitmapScalingMode.NearestNeighbor);
        group.Children.Add(new ImageDrawing(source, rect));
        dc.DrawDrawing(group);
    }

    private static void Preview(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        const int width = 1240, height = 900;
        var tones = new[] { IconTone.Active, IconTone.Paused, IconTone.Blocked };
        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            for (int half = 0; half < 2; half++)
            {
                double top = half * height / 2.0;
                var bg = half == 0 ? Color.FromRgb(0x20, 0x20, 0x20) : Color.FromRgb(0xF3, 0xF3, 0xF3);
                dc.DrawRectangle(new SolidColorBrush(bg), null, new Rect(0, top, width, height / 2.0));

                // App icon (dark tile), large to small; the in-app title mark (light tile) at the end.
                double x = 30;
                foreach (int s in new[] { 256, 64, 48, 40, 32, 24, 20, 16 })
                {
                    int shown = s == 256 ? 160 : s;
                    dc.DrawImage(AppIcon.Render(s, IconTone.Active, dark: true), new Rect(x, top + 30 + 160 - shown, shown, shown));
                    x += shown + 22;
                }
                x += 40;
                dc.DrawImage(AppIcon.Render(20), new Rect(x, top + 170, 20, 20));

                // Tray at 100% / 125% / 150% scaling, real size.
                double ty = top + 240;
                dc.DrawRectangle(new SolidColorBrush(half == 0 ? Color.FromRgb(0x1A, 0x1A, 0x1A) : Color.FromRgb(0xE6, 0xE6, 0xE6)), null, new Rect(20, ty - 12, 560, 48));
                x = 36;
                foreach (int s in new[] { 16, 20, 24 })
                {
                    foreach (var tone in tones)
                    {
                        dc.DrawImage(AppIcon.Render(s, tone), new Rect(x, ty + (24 - s) / 2.0, s, s));
                        x += s + 18;
                    }
                    x += 30;
                }

                // Magnified pixel grid of the tray sizes.
                x = 620;
                foreach (var (s, dark) in new[] { (16, false), (24, false), (16, true), (24, true) })
                {
                    Pixelated(dc, AppIcon.Render(s, IconTone.Active, dark), new Rect(x, top + 220, s * 6, s * 6));
                    x += s * 6 + 20;
                }
            }
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(visual);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var fs = File.Create(path);
        encoder.Save(fs);
        Console.WriteLine("preview: " + path);
    }

    /// <summary>Exports the canonical 1:1 application mark for stores and product listings.</summary>
    private static void Logo(string path)
    {
        const int size = 1024;
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var bitmap = AppIcon.Render(size, IconTone.Active, dark: true);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var fs = File.Create(path);
        encoder.Save(fs);
        Console.WriteLine("logo: " + path);
    }
}
