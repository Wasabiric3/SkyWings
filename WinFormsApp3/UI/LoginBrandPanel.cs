using System.ComponentModel;

namespace SkyWings.UI;

/// <summary>
/// Branded side panel for the sign-in window: the brand kit's sage hero background, a faint wing watermark,
/// the SkyWings logo mark and the name. The backdrop is rendered once per size and reused, so repaints are a single blit.
/// </summary>
public sealed class LoginBrandPanel : Control, ISelfStyled
{
    private static readonly Font EyebrowFont = new("Segoe UI", 8.5f, FontStyle.Bold);
    private static readonly Font HeadingFont = new("Segoe UI", 28f, FontStyle.Bold);
    private static readonly Font TaglineFont = new("Segoe UI", 10.5f, FontStyle.Regular);

    private readonly BackdropCache _backdrop = new();

    public LoginBrandPanel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        TabStop = false;
        AccessibleName = "SkyWings";
        AccessibleRole = AccessibleRole.Graphic;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Heading { get; set; } = "SkyWings";

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Tagline { get; set; } = "A little closer to everywhere.";

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Eyebrow { get; set; } = "STAFF PORTAL";

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        Rectangle r = ClientRectangle;
        if (r.Width < 8 || r.Height < 8) return;
        Gfx.HighQuality(g);

        g.DrawImageUnscaled(_backdrop.Get(r.Size, DeviceDpi, PaintBackdrop), 0, 0);

        // Wordmark block, anchored to the bottom-left.
        float w = r.Width, h = r.Height;
        float pad = Gfx.S(this, 32f);
        using var near = Gfx.Format(StringAlignment.Near, StringAlignment.Near);
        using var eyebrow = new SolidBrush(Theme.Primary);
        using var heading = new SolidBrush(Theme.AccentDark);
        using var tagline = new SolidBrush(Theme.TextSecondary);

        float textWidth = w - 2 * pad;
        float taglineY = h - pad - Gfx.S(this, 24f);
        float headingY = taglineY - Gfx.S(this, 52f);
        float eyebrowY = headingY - Gfx.S(this, 24f);
        g.DrawString(Eyebrow, EyebrowFont, eyebrow, new RectangleF(pad, eyebrowY, textWidth, Gfx.S(this, 20f)), near);
        g.DrawString(Heading, HeadingFont, heading, new RectangleF(pad, headingY, textWidth, Gfx.S(this, 50f)), near);
        g.DrawString(Tagline, TaglineFont, tagline, new RectangleF(pad, taglineY, textWidth, Gfx.S(this, 24f)), near);
    }

    private void PaintBackdrop(Graphics g)
    {
        Rectangle r = ClientRectangle;
        BrandArt.FillHero(g, r, DeviceDpi);

        // Faint wing watermark bleeding off the lower right.
        Bitmap? mark = Assets.Watermark(DeviceDpi);
        if (mark is not null)
        {
            int size = (int)Math.Min(r.Width * 1.1f, Gfx.S(this, 320));
            BrandArt.DrawScaled(g, mark, new Rectangle(r.Right - size + Gfx.S(this, 40), r.Bottom - size + Gfx.S(this, 20), size, size));
        }

        // Logo mark, top-left.
        int logo = Gfx.S(this, 72);
        BrandArt.DrawScaled(g, Assets.Logo(LogoVariant.Green, DeviceDpi), new Rectangle(Gfx.S(this, 28), Gfx.S(this, 28), logo, logo));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _backdrop.Dispose();
        base.Dispose(disposing);
    }
}
