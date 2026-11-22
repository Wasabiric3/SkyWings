namespace SkyWings.UI;

/// <summary>
/// The large green page title with an optional hint line under it, drawn on the page colour above a screen's content
/// (the same header the booking wizard has). It docks to the top and sets its own height from the title and the wrapped hint.
/// </summary>
public sealed class PageHeader : Control, ISelfStyled
{
    private string _title = "", _hint = "";

    public PageHeader()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);
        TabStop = false;
        Dock = DockStyle.Top;
        Height = HeightFor(Width);
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string Title
    {
        get => _title;
        set { _title = value ?? ""; AccessibleName = _title; Relayout(); }
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string Hint
    {
        get => _hint;
        set { _hint = value ?? ""; Relayout(); }
    }

    private const TextFormatFlags Flags = TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

    private int HeightFor(int width)
    {
        width = Math.Max(width, 1);
        int titleHeight = Math.Max(Gfx.S(this, 46), TextRenderer.MeasureText(_title.Length == 0 ? " " : _title, Theme.Title, new Size(width, 0), Flags).Height + Gfx.S(this, 8));
        if (_hint.Length == 0) return titleHeight + Gfx.S(this, 6);
        int hintHeight = TextRenderer.MeasureText(_hint, Theme.Small, new Size(width, 0), Flags).Height;
        return titleHeight + hintHeight + Gfx.S(this, 12);
    }

    private void Relayout()
    {
        int height = HeightFor(Width);
        if (Height != height) Height = height;
        Invalidate();
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        Relayout();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        using (var surface = new SolidBrush(Gfx.Surface(this))) g.FillRectangle(surface, ClientRectangle);
        int titleHeight = Math.Max(Gfx.S(this, 46), TextRenderer.MeasureText(_title.Length == 0 ? " " : _title, Theme.Title, new Size(Width, 0), Flags).Height + Gfx.S(this, 8));
        TextRenderer.DrawText(g, _title, Theme.Title, new Rectangle(0, 0, Width, titleHeight), Theme.Brand,
            Flags & ~TextFormatFlags.Top | TextFormatFlags.VerticalCenter);
        if (_hint.Length > 0)
            TextRenderer.DrawText(g, _hint, Theme.Small, new Rectangle(0, titleHeight, Width, Height - titleHeight), Theme.TextSecondary, Flags);
    }
}
