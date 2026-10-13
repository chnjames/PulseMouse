using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Drawing = System.Drawing;

namespace PulseMouse.App.Services;

/// <summary>The mint orb logo, rendered for the window icon and the tray.</summary>
public static class AppIcon
{
    private static DrawingVisual Orb(int size)
    {
        var visual = new DrawingVisual();
        using var dc = visual.RenderOpen();
        var fill = new RadialGradientBrush
        {
            GradientOrigin = new System.Windows.Point(0.35, 0.3), Center = new System.Windows.Point(0.35, 0.3), RadiusX = 0.8, RadiusY = 0.8,
            GradientStops =
            {
                new GradientStop(System.Windows.Media.Color.FromRgb(0x9E, 0xFF, 0xEE), 0),
                new GradientStop(System.Windows.Media.Color.FromRgb(0x51, 0xE6, 0xCA), 0.55),
                new GradientStop(System.Windows.Media.Color.FromRgb(0x1F, 0x9E, 0x87), 1)
            }
        };
        double r = size / 2.0;
        dc.DrawEllipse(fill, null, new System.Windows.Point(r, r), r - 0.5, r - 0.5);
        dc.DrawEllipse(new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x07, 0x10, 0x0E)), null, new System.Windows.Point(r, r), r * 0.36, r * 0.36);
        return visual;
    }

    private static BitmapSource Render(int size)
    {
        var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(Orb(size));
        bitmap.Freeze();
        return bitmap;
    }

    public static ImageSource CreateImageSource() => Render(64);

    public static Drawing.Icon CreateTrayIcon()
    {
        var source = Render(32);
        var pixels = new byte[32 * 32 * 4];
        source.CopyPixels(pixels, 32 * 4, 0);
        using var bitmap = new Drawing.Bitmap(32, 32, Drawing.Imaging.PixelFormat.Format32bppPArgb);
        var data = bitmap.LockBits(new Drawing.Rectangle(0, 0, 32, 32), Drawing.Imaging.ImageLockMode.WriteOnly, bitmap.PixelFormat);
        Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
        bitmap.UnlockBits(data);
        var handle = bitmap.GetHicon();
        var icon = (Drawing.Icon)Drawing.Icon.FromHandle(handle).Clone();
        DestroyIcon(handle);
        return icon;
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(nint handle);
}
