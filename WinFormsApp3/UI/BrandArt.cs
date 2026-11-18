using System.Drawing.Drawing2D;

namespace SkyWings.UI;

/// <summary>Draws the brand kit's sage hero background. Falls back to a flat soft-green fill when the image is missing.</summary>
internal static class BrandArt
{
    /// <summary>
    /// Fills <paramref name="dest"/> with the hero background. The kit image has rounded corners baked in,
    /// so a thin border of the source is cropped away and the picture bleeds to the edges of the area.
    /// </summary>
    public static void FillHero(Graphics g, Rectangle dest, int dpi)
    {
        Bitmap? hero = Assets.Hero(dpi);
        if (hero is null)
        {
            using var flat = new SolidBrush(Theme.SoftFill);
            g.FillRectangle(flat, dest);
            return;
        }
        int crop = (int)(hero.Width * 0.03);
        var source = new Rectangle(crop, crop, hero.Width - 2 * crop, hero.Height - 2 * crop);
        var state = g.Save();
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(hero, dest, source.X, source.Y, source.Width, source.Height, GraphicsUnit.Pixel);
        g.Restore(state);
    }

    /// <summary>Draws a brand bitmap scaled to a logical (96-dpi) size, high quality. No-op when the image is missing.</summary>
    public static void DrawScaled(Graphics g, Bitmap? image, Rectangle dest)
    {
        if (image is null || dest.Width < 1 || dest.Height < 1) return;
        var state = g.Save();
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(image, dest);
        g.Restore(state);
    }
}

/// <summary>Keeps one pre-rendered backdrop bitmap per control, rebuilt only when its size or DPI changes.</summary>
internal sealed class BackdropCache : IDisposable
{
    private Bitmap? _bitmap;
    private Size _size;
    private int _dpi;

    public Bitmap Get(Size size, int dpi, Action<Graphics> paint)
    {
        if (_bitmap is null || _size != size || _dpi != dpi)
        {
            _bitmap?.Dispose();
            _bitmap = new Bitmap(Math.Max(1, size.Width), Math.Max(1, size.Height), System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
            using var g = Graphics.FromImage(_bitmap);
            Gfx.HighQuality(g);
            paint(g);
            _size = size;
            _dpi = dpi;
        }
        return _bitmap;
    }

    public void Dispose()
    {
        _bitmap?.Dispose();
        _bitmap = null;
    }
}
