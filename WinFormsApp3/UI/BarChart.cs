using System.ComponentModel;

namespace SkyWings.UI;

/// <summary>A small horizontal bar chart for the admin reports page: one labelled bar per item, longest first.</summary>
public sealed class BarChart : Control
{
    private readonly List<(string Label, double Value, string Text)> _items = new();

    public BarChart()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.BgCard;
        AccessibleRole = AccessibleRole.Chart;
    }

    private string _title = "";

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Title
    {
        get => _title;
        set { _title = value; AccessibleName = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color BarColor { get; set; } = Theme.Primary;

    /// <summary>Replaces the bars. <paramref name="text"/> is the number shown at the end of each bar.</summary>
    public void SetItems(IEnumerable<(string Label, double Value, string Text)> items)
    {
        _items.Clear();
        _items.AddRange(items);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        Gfx.HighQuality(g);
        float pad = Gfx.S(this, 8f);
        using var title = new SolidBrush(Theme.TextSecondary);
        using var text = new SolidBrush(Theme.TextPrimary);
        using var bar = new SolidBrush(BarColor);
        using var track = new SolidBrush(Theme.BgMuted);
        using var near = Gfx.Format(StringAlignment.Near);
        using var far = Gfx.Format(StringAlignment.Far);
        using var titleFont = new Font(Theme.Small, FontStyle.Bold);

        float titleHeight = Gfx.S(this, 30f);
        g.DrawString(Title.ToUpperInvariant(), titleFont, title, new RectangleF(pad, 0, Width - 2 * pad, titleHeight), near);

        if (_items.Count == 0)
        {
            g.DrawString("No data yet.", Theme.Small, title, new RectangleF(pad, titleHeight, Width - 2 * pad, Gfx.S(this, 24f)), near);
            return;
        }

        float labelWidth = Gfx.S(this, 120f), valueWidth = Gfx.S(this, 64f);
        float top = titleHeight + Gfx.S(this, 4f);
        float rowHeight = Math.Min(Gfx.S(this, 34f), (Height - top - pad) / _items.Count);
        if (rowHeight < Gfx.S(this, 12f)) return;
        float barHeight = rowHeight * 0.55f;
        float trackLeft = pad + labelWidth;
        float trackWidth = Math.Max(10f, Width - trackLeft - valueWidth - pad);
        double max = _items.Max(i => i.Value);
        if (max <= 0) max = 1;

        for (int i = 0; i < _items.Count; i++)
        {
            var (label, value, valueText) = _items[i];
            float y = top + i * rowHeight;
            float barTop = y + (rowHeight - barHeight) / 2;
            var labelRect = new RectangleF(pad, y, labelWidth - pad, rowHeight);
            using var middle = Gfx.Format(StringAlignment.Near);
            middle.LineAlignment = StringAlignment.Center;
            middle.Trimming = StringTrimming.EllipsisCharacter;
            middle.FormatFlags |= StringFormatFlags.NoWrap;
            g.DrawString(label, Theme.Small, text, labelRect, middle);

            g.FillRectangle(track, trackLeft, barTop, trackWidth, barHeight);
            float w = (float)(trackWidth * (value / max));
            if (w >= 1f) g.FillRectangle(bar, trackLeft, barTop, w, barHeight);

            using var valueFormat = Gfx.Format(StringAlignment.Far);
            valueFormat.LineAlignment = StringAlignment.Center;
            g.DrawString(valueText, Theme.Small, text, new RectangleF(trackLeft + trackWidth, y, valueWidth, rowHeight), valueFormat);
        }
    }
}
