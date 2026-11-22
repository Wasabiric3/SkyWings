using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace SkyWings.UI;

/// <summary>Clickable glass card for a region: gradient artwork on top, title and route summary below.</summary>
public sealed class DestinationCard : GlassCard
{
    private static readonly Font TitleFont = new(Theme.Body, FontStyle.Bold);

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

    /// <summary>The photograph that goes with a region card, named after the region ("Southeast Asia" is "southeast_asia"); null for any other title.</summary>
    internal static string? PhotoKeyFor(string title) => title switch
    {
        "Domestic" => "domestic",
        "Southeast Asia" => "southeast_asia",
        "Europe" => "europe",
        "North America" => "north_america",
        _ => null
    };

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
        // About half the card; a taller card gives the extra height to the picture (the text below keeps a fixed 84px).
        float artH = Math.Max(area.Height * 0.46f, area.Height - Gfx.S(this, 84f));
        var art = new RectangleF(area.Left + inset, area.Top + inset, area.Width - 2 * inset, artH - inset);
        // The region's photograph, cropped to the band; without one (an unknown region, a missing file) a flat accent band with the map pin.
        string? photoKey = PhotoKeyFor(Title);
        Bitmap? photo = photoKey is null ? null : Assets.Photo(photoKey);
        using (var path = Gfx.RoundedRect(art, Gfx.S(this, 12f)))
        {
            if (photo is not null)
            {
                GraphicsState saved = g.Save();
                g.SetClip(path);
                BrandArt.DrawCover(g, photo, art, Assets.PhotoFocus(photoKey!));
                g.Restore(saved);
            }
            else
            {
                using var band = new SolidBrush(Accent);
                g.FillPath(band, path);
            }
        }
        Bitmap? pin = photo is null ? Assets.Icon("Map_Pin", white: true, DeviceDpi) : null;
        if (pin is not null)
        {
            int iconSize = Gfx.S(this, 24);   // the icon's native logical size: drawn 1:1, never resampled
            BrandArt.DrawScaled(g, pin, new Rectangle((int)(art.X + (art.Width - iconSize) / 2), (int)(art.Y + (art.Height - iconSize) / 2), iconSize, iconSize));
        }

        float textX = area.Left + inset + Gfx.S(this, 4f);
        float textW = area.Width - 2 * inset - Gfx.S(this, 8f);
        float y = art.Bottom + Gfx.S(this, 8f);
        var titleFont = TitleFont;
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
