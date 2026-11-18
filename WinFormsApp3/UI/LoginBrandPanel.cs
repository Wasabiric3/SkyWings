using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace SkyWings.UI;

/// <summary>
/// Branded side panel for the sign-in window: a sage gradient with soft glows, dashed flight routes,
/// a paper plane on the main route and the SkyWings name. Entirely drawn, so it needs no image files.
/// </summary>
public sealed class LoginBrandPanel : Control, ISelfStyled
{
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

    private static PointF Bezier(PointF a, PointF b, PointF c, PointF d, float t)
    {
        float u = 1 - t;
        return new PointF(
            u * u * u * a.X + 3 * u * u * t * b.X + 3 * u * t * t * c.X + t * t * t * d.X,
            u * u * u * a.Y + 3 * u * u * t * b.Y + 3 * u * t * t * c.Y + t * t * t * d.Y);
    }

    private static PointF BezierTangent(PointF a, PointF b, PointF c, PointF d, float t)
    {
        float u = 1 - t;
        return new PointF(
            3 * u * u * (b.X - a.X) + 6 * u * t * (c.X - b.X) + 3 * t * t * (d.X - c.X),
            3 * u * u * (b.Y - a.Y) + 6 * u * t * (c.Y - b.Y) + 3 * t * t * (d.Y - c.Y));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        Gfx.HighQuality(g);
        Rectangle r = ClientRectangle;
        if (r.Width < 8 || r.Height < 8) return;
        float w = r.Width, h = r.Height, reach = Math.Max(w, h);

        using (var bg = new LinearGradientBrush(r, Theme.PrimaryLight, Color.FromArgb(0x2C, 0x49, 0x3D), 62f))
            g.FillRectangle(bg, r);
        Gfx.Glow(g, w * 0.88f, h * 0.10f, reach * 0.62f, Color.FromArgb(74, 255, 255, 255));
        Gfx.Glow(g, w * 0.05f, h * 1.00f, reach * 0.55f, Color.FromArgb(70, 150, 208, 190));

        // A few fixed "stars" so the panel feels like open sky without any randomness between repaints.
        using (var star = new SolidBrush(Color.FromArgb(70, 255, 255, 255)))
        {
            float s = Gfx.S(this, 2f);
            foreach (var (fx, fy) in new[] { (0.18f, 0.12f), (0.46f, 0.07f), (0.74f, 0.20f), (0.90f, 0.44f), (0.08f, 0.40f), (0.62f, 0.50f) })
                g.FillEllipse(star, w * fx, h * fy, s, s);
        }

        // Two routes: a faint one underneath, the main one with the plane on top.
        PointF a2 = new(w * 0.16f, h * 0.60f), c2a = new(w * 0.34f, h * 0.52f), c2b = new(w * 0.58f, h * 0.40f), b2 = new(w * 0.84f, h * 0.50f);
        using (var faint = new Pen(Color.FromArgb(55, 255, 255, 255), Gfx.S(this, 1.5f)) { DashStyle = DashStyle.Dash })
            g.DrawBezier(faint, a2, c2a, c2b, b2);

        PointF a = new(w * 0.14f, h * 0.50f), c1 = new(w * 0.28f, h * 0.18f), c2 = new(w * 0.62f, h * 0.10f), b = new(w * 0.86f, h * 0.34f);
        using (var route = new Pen(Color.FromArgb(150, 255, 255, 255), Gfx.S(this, 1.75f)) { DashStyle = DashStyle.Dash })
            g.DrawBezier(route, a, c1, c2, b);

        float dot = Gfx.S(this, 5f);
        using (var ring = new Pen(Color.FromArgb(200, 255, 255, 255), Gfx.S(this, 1.5f)))
        using (var fill = new SolidBrush(Color.FromArgb(230, 255, 255, 255)))
        {
            g.FillEllipse(fill, a.X - dot, a.Y - dot, dot * 2, dot * 2);
            g.DrawEllipse(ring, b.X - dot, b.Y - dot, dot * 2, dot * 2);
            g.FillEllipse(fill, a2.X - dot * 0.7f, a2.Y - dot * 0.7f, dot * 1.4f, dot * 1.4f);
            g.FillEllipse(fill, b2.X - dot * 0.7f, b2.Y - dot * 0.7f, dot * 1.4f, dot * 1.4f);
        }

        DrawPlane(g, Bezier(a, c1, c2, b, 0.62f), BezierTangent(a, c1, c2, b, 0.62f), Gfx.S(this, 15f));

        // Wordmark block, anchored to the bottom-left.
        float pad = Gfx.S(this, 32f);
        using var near = Gfx.Format(StringAlignment.Near, StringAlignment.Near);
        using var eyebrowFont = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        using var headingFont = new Font("Segoe UI", 28f, FontStyle.Bold);
        using var taglineFont = new Font("Segoe UI", 10.5f, FontStyle.Regular);
        using var white = new SolidBrush(Color.White);
        using var soft = new SolidBrush(Color.FromArgb(215, 255, 255, 255));
        using var dim = new SolidBrush(Color.FromArgb(170, 255, 255, 255));

        float textWidth = w - 2 * pad;
        float taglineY = h - pad - Gfx.S(this, 24f);
        float headingY = taglineY - Gfx.S(this, 52f);
        float eyebrowY = headingY - Gfx.S(this, 24f);
        g.DrawString(Eyebrow, eyebrowFont, dim, new RectangleF(pad, eyebrowY, textWidth, Gfx.S(this, 20f)), near);
        g.DrawString(Heading, headingFont, white, new RectangleF(pad, headingY, textWidth, Gfx.S(this, 50f)), near);
        g.DrawString(Tagline, taglineFont, soft, new RectangleF(pad, taglineY, textWidth, Gfx.S(this, 24f)), near);
    }

    /// <summary>Paper-plane dart, pointing along <paramref name="direction"/>, centred on <paramref name="at"/>.</summary>
    private static void DrawPlane(Graphics g, PointF at, PointF direction, float size)
    {
        float angle = (float)(Math.Atan2(direction.Y, direction.X) * 180 / Math.PI);
        var state = g.Save();
        g.TranslateTransform(at.X, at.Y);
        g.RotateTransform(angle);
        PointF[] body = { new(1f * size, 0), new(-0.85f * size, 0.58f * size), new(-0.45f * size, 0), new(-0.85f * size, -0.58f * size) };
        using (var shadow = new SolidBrush(Color.FromArgb(40, 0, 0, 0)))
        {
            g.TranslateTransform(0, size * 0.18f);
            g.FillPolygon(shadow, body);
            g.TranslateTransform(0, -size * 0.18f);
        }
        using (var fill = new SolidBrush(Color.White))
            g.FillPolygon(fill, body);
        PointF[] fold = { new(1f * size, 0), new(-0.45f * size, 0), new(-0.85f * size, 0.58f * size) };
        using (var shade = new SolidBrush(Color.FromArgb(46, 38, 72, 62)))
            g.FillPolygon(shade, fold);
        g.Restore(state);
    }
}
