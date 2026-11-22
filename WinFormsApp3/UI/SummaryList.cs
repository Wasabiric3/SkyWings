namespace SkyWings.UI;

/// <summary>A list of label / value rows: the label small and muted at the left, the value bold beside it (the payment step's booking summary).</summary>
public sealed class SummaryList : Control, ISelfStyled
{
    private static readonly Font ValueFont = new(Theme.Body, FontStyle.Bold);
    private List<(string Label, string Value)> _rows = new();

    public SummaryList()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);
        TabStop = false;
        BackColor = Theme.BgCard;
        AccessibleName = "Booking summary";
    }

    public IReadOnlyList<(string Label, string Value)> Rows => _rows;

    /// <summary>The height of one row.</summary>
    internal int RowHeight => Gfx.S(this, 38);

    /// <summary>
    /// When true the list is always exactly as tall as its rows, so none is ever left out; put it in a scrolling panel and what does
    /// not fit the card is reached by scrolling.
    /// </summary>
    [System.ComponentModel.DefaultValue(false)]
    public bool GrowToFit { get; set; }

    public void SetRows(IEnumerable<(string Label, string Value)> rows)
    {
        _rows = rows.ToList();
        AccessibleDescription = string.Join(". ", _rows.Select(r => $"{r.Label}: {r.Value}"));
        if (GrowToFit) Height = _rows.Count * RowHeight;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        using (var back = new SolidBrush(BackColor)) g.FillRectangle(back, ClientRectangle);
        const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
        int rowHeight = RowHeight;
        int labelWidth = (int)(Width * 0.36f);
        int y = 0;
        foreach (var (label, value) in _rows)
        {
            if (y + rowHeight > Height) break;   // a row is drawn whole or not at all
            TextRenderer.DrawText(g, label, Theme.Small, new Rectangle(0, y, labelWidth - Gfx.S(this, 8), rowHeight), Theme.TextSecondary, flags);
            TextRenderer.DrawText(g, value, ValueFont, new Rectangle(labelWidth, y, Math.Max(0, Width - labelWidth - 1), rowHeight), Theme.TextPrimary, flags);
            y += rowHeight;
        }
    }
}
