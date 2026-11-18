using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace SkyWings.UI;

/// <summary>Marker for controls that style themselves; <c>Theme.Apply</c> leaves them (and their children) alone.</summary>
public interface ISelfStyled { }

internal static class Gfx
{
    /// <summary>Scales a 96-dpi pixel value to the control's current DPI.</summary>
    public static int S(Control c, int px) => (int)Math.Round(px * c.DeviceDpi / 96.0);

    public static float S(Control c, float px) => px * c.DeviceDpi / 96f;

    /// <summary>
    /// The first opaque colour found walking up the parents: what shows around a control's rounded corners.
    /// Self-painting controls fill with this instead of being transparent, so repaints do not cascade up the parent chain.
    /// </summary>
    public static Color Surface(Control control)
    {
        for (Control? c = control.Parent; c is not null; c = c.Parent)
            if (c.BackColor.A == 255) return c.BackColor;
        return Theme.BgMain;
    }

    public static GraphicsPath RoundedRect(RectangleF r, float radius)
    {
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        var path = new GraphicsPath();
        if (d <= 0)
        {
            path.AddRectangle(r);
            return path;
        }
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }

    public static void HighQuality(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
    }

    public static Color Mix(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return Color.FromArgb(
            (int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t),
            (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
    }

    public static StringFormat Format(StringAlignment horizontal, StringAlignment vertical = StringAlignment.Center)
        => new(StringFormat.GenericTypographic)
        {
            Alignment = horizontal,
            LineAlignment = vertical,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };

}
