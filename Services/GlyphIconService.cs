using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.UI.WindowsAndMessaging;

namespace TrayWiz.Services;

/// <summary>
/// Renders a Segoe Fluent Icons glyph into an .ico file at exactly the size the tray shows, so
/// each extra tray icon is sharp. The glyph is drawn large, cropped to its ink so it fills the
/// slot, then box-averaged down to the real pixel size with proper alpha. It is white on a dark
/// taskbar and black on a light one, like the app's own icon.
/// </summary>
internal static class GlyphIconService
{
    // Source resolution the glyph is drawn at before it is averaged down.
    private const int Canvas = 256;
    private const int Threshold = 8;

    private static readonly string Folder = Path.Combine(Path.GetTempPath(), "TrayWiz-icons");

    /// <summary>Returns the path of the icon for <paramref name="glyph"/>, or null if it couldn't be drawn.</summary>
    public static string? GetIconPath(string glyph, bool isLight)
    {
        if (string.IsNullOrEmpty(glyph))
            return null;

        int size = Math.Clamp(PInvoke.GetSystemMetricsForDpi(SYSTEM_METRICS_INDEX.SM_CXSMICON, PInvoke.GetDpiForSystem()), 16, 64);
        string path = Path.Combine(Folder, $"{(int)glyph[0]:X4}-{(isLight ? "dark" : "light")}-{size}.ico");
        if (File.Exists(path))
            return path;

        try
        {
            byte[] alpha = RenderAlpha(glyph);
            uint[] pixels = Downsample(alpha, size, isLight ? 0xFF000000 : 0xFFFFFFFF);
            Directory.CreateDirectory(Folder);
            File.WriteAllBytes(path, IcoEncoder.Encode(pixels, size));
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ExternalException or ArgumentException)
        {
            System.Diagnostics.Debug.WriteLine($"[GlyphIcon] Couldn't draw {glyph}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Draws the glyph centered on a <see cref="Canvas"/> square and returns its alpha channel.</summary>
    private static byte[] RenderAlpha(string glyph)
    {
        using Bitmap bitmap = new(Canvas, Canvas, PixelFormat.Format32bppArgb);
        using (Graphics graphics = Graphics.FromImage(bitmap))
        using (Font font = new("Segoe Fluent Icons", Canvas * 0.75f, FontStyle.Regular, GraphicsUnit.Pixel))
        using (StringFormat format = new(StringFormat.GenericTypographic)
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        })
        {
            graphics.TextRenderingHint = TextRenderingHint.AntiAlias;
            graphics.DrawString(glyph, font, Brushes.White, new RectangleF(0, 0, Canvas, Canvas), format);
        }

        BitmapData data = bitmap.LockBits(new Rectangle(0, 0, Canvas, Canvas), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            byte[] bgra = new byte[Canvas * Canvas * 4];
            Marshal.Copy(data.Scan0, bgra, 0, bgra.Length);

            byte[] alpha = new byte[Canvas * Canvas];
            for (int i = 0; i < alpha.Length; i++)
                alpha[i] = bgra[(i * 4) + 3];
            return alpha;
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static uint[] Downsample(byte[] alpha, int size, uint color)
    {
        // Crop to the glyph's ink so every icon fills its slot the same way.
        int minX = Canvas, minY = Canvas, maxX = -1, maxY = -1;
        for (int y = 0; y < Canvas; y++)
        {
            for (int x = 0; x < Canvas; x++)
            {
                if (alpha[(y * Canvas) + x] <= Threshold)
                    continue;
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);
            }
        }

        uint[] pixels = new uint[size * size];
        if (maxX < 0)
            return pixels;

        double margin = size / 16.0;
        double side = Math.Max(maxX - minX + 1, maxY - minY + 1);
        double perPixel = side / (size - (2 * margin)); // source pixels per output pixel
        double originX = ((minX + maxX + 1) / 2.0) - (side / 2);
        double originY = ((minY + maxY + 1) / 2.0) - (side / 2);

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                double sx0 = originX + ((x - margin) * perPixel);
                double sy0 = originY + ((y - margin) * perPixel);
                pixels[(y * size) + x] = IcoEncoder.WithCoverage(color, BoxAverage(alpha, sx0, sy0, sx0 + perPixel, sy0 + perPixel));
            }
        }

        return pixels;
    }

    /// <summary>Average coverage (0-1) of the source rectangle, weighting partly covered source pixels.</summary>
    private static double BoxAverage(byte[] alpha, double x0, double y0, double x1, double y1)
    {
        double sum = 0;
        for (int y = (int)Math.Floor(y0); y < Math.Ceiling(y1); y++)
        {
            double wy = Math.Min(y + 1, y1) - Math.Max(y, y0);
            if (y < 0 || y >= Canvas || wy <= 0)
                continue;

            for (int x = (int)Math.Floor(x0); x < Math.Ceiling(x1); x++)
            {
                double wx = Math.Min(x + 1, x1) - Math.Max(x, x0);
                if (x < 0 || x >= Canvas || wx <= 0)
                    continue;
                sum += alpha[(y * Canvas) + x] * wx * wy;
            }
        }

        return sum / (255 * (x1 - x0) * (y1 - y0));
    }
}
