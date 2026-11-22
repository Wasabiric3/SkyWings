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
        Rectangle source = CoverSource(hero.Size, dest.Size);
        var state = g.Save();
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(hero, dest, source.X, source.Y, source.Width, source.Height, GraphicsUnit.Pixel);
        g.Restore(state);
    }

    /// <summary>
    /// The part of the image to draw so it covers a destination of the given size without distortion: the kit's baked-in
    /// rounded corners are trimmed, then the longer side is cropped (centred) to the destination's aspect ratio.
    /// </summary>
    internal static Rectangle CoverSource(Size image, Size dest)
    {
        int crop = (int)(image.Width * 0.03);
        var inner = new Rectangle(crop, crop, Math.Max(1, image.Width - 2 * crop), Math.Max(1, image.Height - 2 * crop));
        if (dest.Width < 1 || dest.Height < 1) return inner;

        double want = dest.Width / (double)dest.Height, have = inner.Width / (double)inner.Height;
        if (have > want)   // image is wider than the destination: trim the sides
        {
            int w = Math.Max(1, (int)Math.Round(inner.Height * want));
            return new Rectangle(inner.X + (inner.Width - w) / 2, inner.Y, w, inner.Height);
        }
        int h = Math.Max(1, (int)Math.Round(inner.Width / want));   // image is taller: trim top and bottom
        return new Rectangle(inner.X, inner.Y + (inner.Height - h) / 2, inner.Width, h);
    }

    /// <summary>
    /// The part of a photograph to draw so it covers <paramref name="dest"/> without distortion: the longer side is cropped to the
    /// destination's shape, centred on <paramref name="focus"/> (0..1 across and down) and kept inside the image, so the subject of the
    /// photo stays in view. The whole width (or height) of the image is always used.
    /// </summary>
    internal static RectangleF CoverSource(Size image, Size dest, PointF focus)
    {
        if (dest.Width < 1 || dest.Height < 1) return new RectangleF(0, 0, image.Width, image.Height);
        float want = dest.Width / (float)dest.Height, have = image.Width / (float)image.Height;
        float w = image.Width, h = image.Height;
        if (have > want) w = image.Height * want;     // image is wider than the destination: trim the sides
        else h = image.Width / want;                  // image is taller: trim top and bottom
        float x = Math.Clamp(focus.X * image.Width - w / 2, 0, image.Width - w);
        float y = Math.Clamp(focus.Y * image.Height - h / 2, 0, image.Height - h);
        return new RectangleF(x, y, w, h);
    }

    /// <summary>Draws a photograph to fill <paramref name="dest"/>, cropped around <paramref name="focus"/> (see <see cref="CoverSource(Size, Size, PointF)"/>). The caller sets any clip.</summary>
    public static void DrawCover(Graphics g, Bitmap? photo, RectangleF dest, PointF focus)
    {
        if (photo is null || dest.Width < 1 || dest.Height < 1) return;
        RectangleF source = CoverSource(photo.Size, Size.Round(dest.Size), focus);
        var state = g.Save();
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = PixelOffsetMode.Half;
        g.DrawImage(photo, dest, source, GraphicsUnit.Pixel);
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
