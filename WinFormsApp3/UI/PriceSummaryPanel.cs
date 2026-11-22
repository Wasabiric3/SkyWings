
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
    private const TextFormatFlags Flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter;

    private List<PriceLine> _lines = new();

    public PriceSummaryPanel()
    {
        ContentPadding = 16;
        AccessibleName = "Trip summary";
    }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public string Title { get; set; } = "Your trip";

    /// <summary>The lines currently shown (read by tests).</summary>
    internal IReadOnlyList<PriceLine> Lines => _lines;

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
            if (size <= 8f || TextRenderer.MeasureText(text, font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width <= maxWidth) return font;
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
        int y = area.Top;

        TextRenderer.DrawText(g, Title.ToUpperInvariant(), SmallBold, new Rectangle(area.Left, y, area.Width, Gfx.S(this, 24)), Theme.Brand, Flags);
        y += Gfx.S(this, 30);
        g.DrawLine(divider, area.Left, y, area.Right, y);
        y += Gfx.S(this, 10);

        // The total is the one line that must never be lost, so its room is set aside first. The trip lines then use what is left:
        // stacked (label above value) when they fit; otherwise one row each; and when even that is too much, the Stat lines
        // (travellers, cabin, dates) are left out, because the step itself already shows them, and the fare lines stay.
        PriceLine? total = _lines.LastOrDefault(l => l.Kind == PriceLineKind.Total);
        int totalSpace = total is null ? 0 : Gfx.S(this, 70) + 2 * Gfx.S(this, 4);
        int available = area.Bottom - totalSpace - y;
        List<PriceLine> lines = _lines.Where(l => l.Kind != PriceLineKind.Total).ToList();
        bool compact = lines.Sum(l => LineHeight(l, compact: false)) > available;
        if (compact && lines.Sum(l => LineHeight(l, compact: true)) > available)
            lines = lines.Where(l => l.Kind != PriceLineKind.Stat).ToList();

        int limit = y + available;
        foreach (PriceLine line in lines)
        {
            int height = LineHeight(line, compact);
            if (y + height > limit) break;
            DrawLine(g, line, area, y, compact, divider);
            y += height;
        }

        if (total is not null)
        {
            y = Math.Min(y, area.Bottom - totalSpace);   // directly under the lines, or at the bottom of the card when they fill it
            DrawTotal(g, total, area, ref y);
        }
    }

    /// <summary>How tall a summary line is. Compact mode puts a Stat on one row and drops the Hero's divider.</summary>
    private int LineHeight(PriceLine line, bool compact) => line.Kind switch
    {
        PriceLineKind.Hero => compact ? Gfx.S(this, 50) : Gfx.S(this, 68),
        PriceLineKind.Stat => compact ? Gfx.S(this, 23) : Gfx.S(this, 52),
        PriceLineKind.Heading => Gfx.S(this, 6) + Gfx.S(this, 23),
        _ => Gfx.S(this, 23)
    };

    private void DrawLine(Graphics g, PriceLine line, Rectangle area, int y, bool compact, Pen divider)
    {
        int rowH = Gfx.S(this, 23);
        switch (line.Kind)
        {
            case PriceLineKind.Hero when compact:
                TextRenderer.DrawText(g, line.Label, BoldFont, new Rectangle(area.Left, y, area.Width, Gfx.S(this, 26)), Theme.Brand, Flags);
                TextRenderer.DrawText(g, line.Value, Theme.Small, new Rectangle(area.Left, y + Gfx.S(this, 26), area.Width, Gfx.S(this, 20)), Theme.TextSecondary, Flags);
                break;
            case PriceLineKind.Hero:
                TextRenderer.DrawText(g, line.Label, TitleFont, new Rectangle(area.Left, y, area.Width, Gfx.S(this, 30)), Theme.Brand, Flags);
                TextRenderer.DrawText(g, line.Value, Theme.Small, new Rectangle(area.Left, y + Gfx.S(this, 30), area.Width, Gfx.S(this, 22)), Theme.TextSecondary, Flags);
                g.DrawLine(divider, area.Left, y + Gfx.S(this, 58), area.Right, y + Gfx.S(this, 58));
                break;
            case PriceLineKind.Stat when compact:
                DrawItem(g, line, area, y, rowH, valueFont: BoldFont, valueColor: Theme.TextPrimary);
                break;
            case PriceLineKind.Stat:
                TextRenderer.DrawText(g, line.Label, Theme.Small, new Rectangle(area.Left, y, area.Width, Gfx.S(this, 20)), Theme.TextSecondary, Flags);
                TextRenderer.DrawText(g, line.Value, BoldFont, new Rectangle(area.Left, y + Gfx.S(this, 20), area.Width, Gfx.S(this, 26)), Theme.TextPrimary, Flags);
                break;
            case PriceLineKind.Heading:
                TextRenderer.DrawText(g, line.Label.ToUpperInvariant(), Theme.Small, new Rectangle(area.Left, y + Gfx.S(this, 6), area.Width, rowH), Theme.TextSecondary, Flags);
                break;
            case PriceLineKind.Note:
                TextRenderer.DrawText(g, line.Label, Theme.Small, new Rectangle(area.Left, y, area.Width, rowH), Theme.TextSecondary, Flags);
                break;
            default:
                DrawItem(g, line, area, y, rowH, valueFont: SmallBold, valueColor: line.Kind == PriceLineKind.Discount ? Theme.Accent : Theme.TextPrimary);
                break;
        }
    }

    /// <summary>One row: the label muted at the left, the value right-aligned.</summary>
    private static void DrawItem(Graphics g, PriceLine line, Rectangle area, int y, int rowH, Font valueFont, Color valueColor)
    {
        int labelWidth = (int)(area.Width * 0.58f);
        TextRenderer.DrawText(g, line.Label, Theme.Small, new Rectangle(area.Left, y, labelWidth, rowH), Theme.TextSecondary, Flags);
        TextRenderer.DrawText(g, line.Value, valueFont, new Rectangle(area.Left + labelWidth, y, area.Width - labelWidth, rowH), valueColor, Flags | TextFormatFlags.Right);
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
