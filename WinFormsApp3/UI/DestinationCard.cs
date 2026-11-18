using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace SkyWings.UI;

/// <summary>Clickable glass card for a region: gradient artwork on top, title and route summary below.</summary>
public sealed class DestinationCard : GlassCard
{
    private bool _hover;

    public DestinationCard()
    {
        ContentPadding = 0;
        ShadowSize = 8;
        SetStyle(ControlStyles.Selectable | ControlStyles.StandardClick, true);
        TabStop = true;
        Cursor = Cursors.Hand;
        Font = Theme.Small;
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Title { get; set; } = "";

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Subtitle { get; set; } = "";

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Meta { get; set; } = "";

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color Accent { get; set; } = Theme.Primary;

    protected override void OnMouseEnter(EventArgs e) { _hover = true; BackColor = Gfx.Mix(Theme.BgCard, Theme.SoftFill, 0.5f); Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; BackColor = Theme.BgCard; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnMouseDown(MouseEventArgs e) { Focus(); base.OnMouseDown(e); }
    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Enter or Keys.Space) { OnClick(EventArgs.Empty); e.Handled = true; }
        base.OnKeyUp(e);
    }
    protected override bool IsInputKey(Keys keyData) => keyData == Keys.Space || base.IsInputKey(keyData);

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        Gfx.HighQuality(g);
        Rectangle area = DisplayRectangle;
        if (area.Width < 40 || area.Height < 40) return;

        float inset = Gfx.S(this, 8f);
        float artH = area.Height * 0.46f;
        var art = new RectangleF(area.Left + inset, area.Top + inset, area.Width - 2 * inset, artH - inset);
        using (var path = Gfx.RoundedRect(art, Gfx.S(this, 12f)))
        {
            var oldClip = g.Clip;
            g.SetClip(path, CombineMode.Intersect);
            using (var sky = new LinearGradientBrush(art, Gfx.Mix(Color.White, Accent, 0.28f), Accent, 40f))
                g.FillRectangle(sky, art);
            using (var hills = new SolidBrush(Color.FromArgb(120, Theme.Accent)))
            {
                g.FillEllipse(hills, art.X - art.Width * 0.1f, art.Y + art.Height * 0.45f, art.Width * 0.7f, art.Height * 0.9f);
                g.FillEllipse(hills, art.X + art.Width * 0.35f, art.Y + art.Height * 0.55f, art.Width * 0.8f, art.Height * 0.9f);
            }
            using (var sun = new SolidBrush(Color.FromArgb(150, 255, 255, 255)))
                g.FillEllipse(sun, art.Right - art.Width * 0.28f, art.Y + art.Height * 0.12f, art.Height * 0.3f, art.Height * 0.3f);
            g.Clip = oldClip;
        }

        float textX = area.Left + inset + Gfx.S(this, 4f);
        float textW = area.Width - 2 * inset - Gfx.S(this, 8f);
        float y = art.Bottom + Gfx.S(this, 8f);
        using var titleFont = new Font(Theme.Body, FontStyle.Bold);
        using var titleBrush = new SolidBrush(Theme.TextPrimary);
        using var mutedBrush = new SolidBrush(Theme.TextSecondary);
        using var accentBrush = new SolidBrush(Theme.Accent);
        using var near = Gfx.Format(StringAlignment.Near);
        float line = Gfx.S(this, 22f);
        g.DrawString(Title, titleFont, titleBrush, new RectangleF(textX, y, textW, line), near);
        g.DrawString(Subtitle, Font, mutedBrush, new RectangleF(textX, y + line, textW, line), near);
        g.DrawString(Meta, Font, accentBrush, new RectangleF(textX, y + line * 2, textW, line), near);

        if (_hover || (Focused && ShowFocusCues))
        {
            using var ring = new Pen(Color.FromArgb(Focused ? 200 : 110, Theme.Primary), Focused ? 2f : 1.2f);
            using var ringPath = Gfx.RoundedRect(RectangleF.Inflate(new RectangleF(area.Left, area.Top, area.Width, area.Height), -1, -1), Gfx.S(this, 16f));
            g.DrawPath(ring, ringPath);
        }
    }
}
