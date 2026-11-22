namespace SkyWings.UI;

/// <summary>A light-green rounded strip with one line of bold text (the chosen flight's departure, duration and seats).</summary>
public sealed class InfoStrip : Control, ISelfStyled
{
    private static readonly Font TextFont = new(Theme.Body, FontStyle.Bold);

    public InfoStrip()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);
        TabStop = false;
        Height = Gfx.S(this, 48);
    }

    public override string Text
    {
        get => base.Text;
        set { base.Text = value; Invalidate(); }
    }

    private Color _fill = Theme.BrandSoft, _textColor = Theme.Brand;

    /// <summary>The strip's colour; light green by default, amber for the demo-payment notice.</summary>
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
        var g = e.Graphics;
        using (var surface = new SolidBrush(Gfx.Surface(this))) g.FillRectangle(surface, ClientRectangle);
        Gfx.HighQuality(g);
        // A little space above and below, so the strip does not touch the rows around it.
        var bounds = new RectangleF(0, Gfx.S(this, 4f), Width - 1, Height - Gfx.S(this, 8f) - 1);
        using (var path = Gfx.RoundedRect(bounds, Gfx.S(this, 12f)))
        using (var fill = new SolidBrush(_fill))
            g.FillPath(fill, path);
        int pad = Gfx.S(this, 16);
        TextRenderer.DrawText(g, Text, TextFont, new Rectangle(pad, (int)bounds.Y, Math.Max(0, Width - 2 * pad), (int)bounds.Height), _textColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
    }
}
