
namespace SkyWings.UI;

/// <summary>
/// How a summary line is drawn. <see cref="Hero"/>: the label large and bold with the value small underneath (a flight and its route).
/// <see cref="Stat"/>: a small muted label with the value in bold underneath (departure, travellers, cabin).
/// </summary>
public enum PriceLineKind { Heading, Item, Discount, Total, Note, Hero, Stat }

public sealed record PriceLine(string Label, string Value, PriceLineKind Kind = PriceLineKind.Item);

/// <summary>Right-hand live summary: trip details and the fare breakdown, redrawn as the booking changes.</summary>
public sealed class PriceSummaryPanel : GlassCard
{
    private static readonly Font TitleFont = new(Theme.Subtitle, FontStyle.Bold);
    private static readonly Font BoldFont = new(Theme.Body, FontStyle.Bold);
    private static readonly Font SmallBold = new(Theme.Small, FontStyle.Bold);
    private const TextFormatFlags Flags = TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter;

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

    /// <summary>
    /// A font in the family and style of <paramref name="start"/>, no larger than it and no smaller than 8 pt, in which
    /// <paramref name="text"/> is at most <paramref name="maxWidth"/> pixels wide. The caller disposes it.
    /// </summary>
    internal static Font FitFont(string text, Font start, int maxWidth)
    {
        float size = start.Size;
        while (true)
        {
            var font = new Font(start.FontFamily, size, start.Style);
            if (size <= 8f || TextRenderer.MeasureText(text, font, Size.Empty, TextFormatFlags.NoPadding).Width <= maxWidth) return font;
            font.Dispose();
            size -= 0.5f;
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        Gfx.HighQuality(g);
        Rectangle area = DisplayRectangle;
        if (area.Width < 40) return;

        using var divider = new Pen(Theme.BorderLight, 1f);
        float rowH = Gfx.S(this, 23f);
        int y = area.Top;

        TextRenderer.DrawText(g, Title.ToUpperInvariant(), SmallBold, new Rectangle(area.Left, y, area.Width, Gfx.S(this, 24)), Theme.Brand, Flags);
        y += Gfx.S(this, 30);
        g.DrawLine(divider, area.Left, y, area.Right, y);
        y += Gfx.S(this, 10);

        foreach (PriceLine line in _lines)
        {
            if (y > area.Bottom - rowH) break;
            switch (line.Kind)
            {
                case PriceLineKind.Hero:
                    TextRenderer.DrawText(g, line.Label, TitleFont, new Rectangle(area.Left, y, area.Width, Gfx.S(this, 30)), Theme.Brand, Flags);
                    y += Gfx.S(this, 30);
                    TextRenderer.DrawText(g, line.Value, Theme.Small, new Rectangle(area.Left, y, area.Width, Gfx.S(this, 22)), Theme.TextSecondary, Flags);
                    y += Gfx.S(this, 28);
                    g.DrawLine(divider, area.Left, y, area.Right, y);
                    y += Gfx.S(this, 10);
                    break;
                case PriceLineKind.Stat:
                    TextRenderer.DrawText(g, line.Label, Theme.Small, new Rectangle(area.Left, y, area.Width, Gfx.S(this, 20)), Theme.TextSecondary, Flags);
                    y += Gfx.S(this, 20);
                    TextRenderer.DrawText(g, line.Value, BoldFont, new Rectangle(area.Left, y, area.Width, Gfx.S(this, 26)), Theme.TextPrimary, Flags);
                    y += Gfx.S(this, 32);
                    break;
                case PriceLineKind.Heading:
                    y += Gfx.S(this, 6);
                    TextRenderer.DrawText(g, line.Label.ToUpperInvariant(), Theme.Small, new Rectangle(area.Left, y, area.Width, (int)rowH), Theme.TextSecondary, Flags);
                    y += (int)rowH;
                    break;
                case PriceLineKind.Total:
                    DrawTotal(g, line, area, ref y);
                    break;
                case PriceLineKind.Note:
                    TextRenderer.DrawText(g, line.Label, Theme.Small, new Rectangle(area.Left, y, area.Width, (int)rowH), Theme.TextSecondary, Flags);
                    y += (int)rowH;
                    break;
                default:
                    bool discount = line.Kind == PriceLineKind.Discount;
                    int labelWidth = (int)(area.Width * 0.58f);
                    TextRenderer.DrawText(g, line.Label, Theme.Small, new Rectangle(area.Left, y, labelWidth, (int)rowH), Theme.TextSecondary, Flags);
                    TextRenderer.DrawText(g, line.Value, SmallBold, new Rectangle(area.Left + labelWidth, y, area.Width - labelWidth, (int)rowH),
                        discount ? Theme.Accent : Theme.TextPrimary, Flags | TextFormatFlags.Right);
                    y += (int)rowH;
                    break;
            }
        }
    }

    /// <summary>The light-green total box: a small caption and the amount, shrunk to fit when the amount is long.</summary>
    private void DrawTotal(Graphics g, PriceLine line, Rectangle area, ref int y)
    {
        y += Gfx.S(this, 4);
        int pad = Gfx.S(this, 14);
        var box = new RectangleF(area.Left, y, area.Width, Gfx.S(this, 70f));
        using (var path = Gfx.RoundedRect(box, Gfx.S(this, 14f)))
        using (var fill = new SolidBrush(Theme.BrandSoft))
            g.FillPath(fill, path);

        string caption = line.Label == "Total" ? "TOTAL AMOUNT" : line.Label.ToUpperInvariant();
        TextRenderer.DrawText(g, caption, SmallBold, new Rectangle((int)box.X + pad, (int)box.Y + Gfx.S(this, 8), (int)box.Width - 2 * pad, Gfx.S(this, 20)),
            Theme.Brand, Flags);
        using Font amount = FitFont(line.Value, TitleFont, (int)box.Width - 2 * pad);
        TextRenderer.DrawText(g, line.Value, amount, new Rectangle((int)box.X + pad, (int)box.Y + Gfx.S(this, 28), (int)box.Width - 2 * pad, Gfx.S(this, 34)),
            Theme.Brand, Flags);
        y += (int)box.Height + Gfx.S(this, 4);
    }
}
