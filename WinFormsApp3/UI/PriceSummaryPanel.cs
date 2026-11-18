
namespace SkyWings.UI;

public enum PriceLineKind { Heading, Item, Discount, Total, Note }

public sealed record PriceLine(string Label, string Value, PriceLineKind Kind = PriceLineKind.Item);

/// <summary>Right-hand live summary: trip details and the fare breakdown, redrawn as the booking changes.</summary>
public sealed class PriceSummaryPanel : GlassCard
{
    private List<PriceLine> _lines = new();

    public PriceSummaryPanel()
    {
        ContentPadding = 16;
        AccessibleName = "Trip summary";
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string Title { get; set; } = "Your trip";

    public void SetLines(IEnumerable<PriceLine> lines)
    {
        _lines = lines.ToList();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        Gfx.HighQuality(g);
        Rectangle area = DisplayRectangle;
        if (area.Width < 40) return;

        using var left = Gfx.Format(StringAlignment.Near);
        using var right = Gfx.Format(StringAlignment.Far);
        using var titleFont = new Font(Theme.Subtitle, FontStyle.Bold);
        using var boldFont = new Font(Theme.Body, FontStyle.Bold);
        using var totalFont = new Font(Theme.Subtitle, FontStyle.Bold);
        using var textBrush = new SolidBrush(Theme.TextPrimary);
        using var mutedBrush = new SolidBrush(Theme.TextSecondary);
        using var accentBrush = new SolidBrush(Theme.Accent);
        using var divider = new Pen(Theme.BorderLight, 1f);

        float y = area.Top;
        float rowH = Gfx.S(this, 23f);
        g.DrawString(Title, titleFont, textBrush, new RectangleF(area.Left, y, area.Width, Gfx.S(this, 30f)), left);
        y += Gfx.S(this, 34f);

        foreach (PriceLine line in _lines)
        {
            if (y > area.Bottom - rowH) break;
            switch (line.Kind)
            {
                case PriceLineKind.Heading:
                    y += Gfx.S(this, 6f);
                    g.DrawString(line.Label.ToUpperInvariant(), Theme.Small, mutedBrush,
                        new RectangleF(area.Left, y, area.Width, rowH), left);
                    y += rowH;
                    break;
                case PriceLineKind.Total:
                    y += Gfx.S(this, 4f);
                    g.DrawLine(divider, area.Left, y, area.Right, y);
                    y += Gfx.S(this, 8f);
                    float labelW = TextRenderer.MeasureText(line.Label, boldFont).Width + Gfx.S(this, 8f);
                    float totalH = Gfx.S(this, 32f);
                    g.DrawString(line.Label, boldFont, textBrush, new RectangleF(area.Left, y, labelW, totalH), left);
                    g.DrawString(line.Value, totalFont, accentBrush,
                        new RectangleF(area.Left + labelW, y, area.Width - labelW, totalH), right);
                    y += totalH + Gfx.S(this, 4f);
                    break;
                case PriceLineKind.Note:
                    g.DrawString(line.Label, Theme.Small, mutedBrush, new RectangleF(area.Left, y, area.Width, rowH), left);
                    y += rowH;
                    break;
                default:
                    bool discount = line.Kind == PriceLineKind.Discount;
                    float lw = area.Width * 0.58f;
                    g.DrawString(line.Label, Theme.Small, mutedBrush, new RectangleF(area.Left, y, lw, rowH), left);
                    using (var valueBrush = new SolidBrush(discount ? Theme.Accent : Theme.TextPrimary))
                        g.DrawString(line.Value, Theme.Small, valueBrush,
                            new RectangleF(area.Left + lw, y, area.Width - lw, rowH), right);
                    y += rowH;
                    break;
            }
        }
    }
}
