using System.ComponentModel;

namespace SkyWings.UI;

/// <summary>Small glass tile showing one headline number (used on the admin reports page).</summary>
public sealed class StatTile : GlassCard
{
    private static readonly Font ValueFont = new(Theme.Title, FontStyle.Bold);

    public StatTile()
    {
        ContentPadding = 6;
        ShadowSize = 8;
        Height = Gfx.S(this, 96);
        AccessibleRole = AccessibleRole.StaticText;
    }

    private string _title = "";
    private string _value = "—";

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Title
    {
        get => _title;
        set { _title = value; AccessibleName = $"{_title}: {_value}"; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Value
    {
        get => _value;
        set { _value = value; AccessibleName = $"{_title}: {_value}"; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color Accent { get; set; } = Theme.Accent;

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        Gfx.HighQuality(g);
        Rectangle area = DisplayRectangle;
        if (area.Width < 30) return;
        using var near = Gfx.Format(StringAlignment.Near);
        using var muted = new SolidBrush(Theme.TextSecondary);
        using var accent = new SolidBrush(Accent);
        var valueFont = ValueFont;
        float pad = Gfx.S(this, 10f);
        g.DrawString(Title.ToUpperInvariant(), Theme.Small, muted,
            new RectangleF(area.Left + pad, area.Top + Gfx.S(this, 4f), area.Width - 2 * pad, Gfx.S(this, 22f)), near);
        g.DrawString(Value, valueFont, accent,
            new RectangleF(area.Left + pad, area.Top + Gfx.S(this, 28f), area.Width - 2 * pad, Gfx.S(this, 40f)), near);
    }
}
