namespace SkyWings.UI;

/// <summary>
/// A status shown as a rounded pill (like the pills in the tables): the text on a tinted fill, centred in whatever room the control is
/// given and drawn on the parent's colour, so it never shows as a tinted block.
/// </summary>
public sealed class StatusPill : Control, ISelfStyled
{
    private static readonly Font PillFont = new(Theme.Small, FontStyle.Bold);

    private Color _fill = Theme.BrandSoft, _textColor = Theme.Brand;

    public StatusPill()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);
        TabStop = false;
    }

    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string Text
    {
        get => base.Text;
        set { base.Text = value ?? ""; AccessibleName = base.Text; Invalidate(); }
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color FillColor
    {
        get => _fill;
        set { _fill = value; Invalidate(); }
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public Color TextColor
    {
        get => _textColor;
        set { _textColor = value; Invalidate(); }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        using (var surface = new SolidBrush(Gfx.Surface(this))) g.FillRectangle(surface, ClientRectangle);
        if (Text.Length == 0) return;
        Size text = TextRenderer.MeasureText(g, Text, PillFont, Size.Empty, TextFormatFlags.NoPadding);
        int height = Gfx.S(this, 30), width = Math.Min(Width - 2, text.Width + Gfx.S(this, 40));
        var pill = new Rectangle((Width - width) / 2, (Height - height) / 2, width, height);
        Gfx.HighQuality(g);
        using (var path = Gfx.RoundedRect(pill, height / 2f))
        using (var fill = new SolidBrush(_fill))
            g.FillPath(fill, path);
        TextRenderer.DrawText(g, Text, PillFont, pill, _textColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
    }
}
