using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;

namespace SkyWings.UI;

/// <summary>Where the main blocks of the printed receipt landed (in page units); used to check that everything fits on one page.</summary>
internal sealed record ReceiptLayout(RectangleF StatusBand, RectangleF InfoBox, float FooterTop, float FooterBottom,
    IReadOnlyList<float> GridRowHeights, bool AnyCellTruncated);

/// <summary>
/// Draws the booking receipt following the brand kit's "Printable_Receipt_A4" design: green top bar, logo and title,
/// payment-status panel with the reference, route, details grid, fare summary, notice box and footer.
/// All coordinates are in 1/96 inch on an A4 page (794 x 1123), so the same code serves a bitmap and a printer.
/// </summary>
internal static class ReceiptPrinter
{
    public const int PageWidth = 794, PageHeight = 1123;

    /// <summary>Soft green-grey of the payment-status panel (taken from the kit's receipt).</summary>
    public static readonly Color PanelTint = Color.FromArgb(0xE8, 0xEE, 0xEA);

    private const float Left = 66, Right = 728, TextLeft = 73, TextRight = 710;
    private const float FooterLineY = 1046, ContentLimit = FooterLineY - 4;

    /// <summary>Row spacing of the three layouts: normal, then tighter ones for long bookings (return flights, many fare lines).</summary>
    private readonly record struct Metrics(float GridPitch, float FarePitch, float FarePx);
    private static readonly Metrics[] Densities = { new(66, 31, 14), new(54, 24, 14), new(46, 20, 14), new(40, 17, 12.5f) };

    private sealed class Fonts : IDisposable
    {
        private readonly List<Font> _all = new();
        public Font Make(float px, bool bold = false)
        {
            var font = new Font("Segoe UI", px, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
            _all.Add(font);
            return font;
        }
        public void Dispose() { foreach (Font f in _all) f.Dispose(); }
    }


    /// <summary>Shows the print preview (A4) for a receipt; the person prints or saves it from there. Used by every Print receipt button.</summary>
    public static void ShowPreview(IWin32Window owner, ReceiptData data)
    {
        using var document = new System.Drawing.Printing.PrintDocument { DocumentName = $"SkyWings receipt {data.Reference}" };
        document.DefaultPageSettings.PaperSize = new System.Drawing.Printing.PaperSize("A4", 827, 1169);   // hundredths of an inch
        document.PrintPage += (_, e) => RenderPage(e, data);
        using var preview = new PrintPreviewDialog { Document = document, Width = 900, Height = 1000 };
        preview.ShowDialog(owner);
    }

    /// <summary>
    /// Prints the receipt on the current page of a <see cref="System.Drawing.Printing.PrintDocument"/>. The receipt is laid out
    /// on a 794 x 1123 page in 1/96 inch (A4): this switches the printer graphics to those units, starts at the paper's corner
    /// (not the printable area's) and shrinks to fit when the paper is smaller than A4.
    /// </summary>
    public static void RenderPage(System.Drawing.Printing.PrintPageEventArgs e, ReceiptData data)
    {
        BeginA4Page(e);
        Draw(e.Graphics!, data);
        e.HasMorePages = false;
    }

    /// <summary>
    /// Switches a printer page to the 794 x 1123 (1/96 inch, A4) design space used by every printout: starts at the paper's corner
    /// (not the printable area's) and shrinks to fit when the paper is smaller than A4.
    /// </summary>
    internal static void BeginA4Page(System.Drawing.Printing.PrintPageEventArgs e)
    {
        Graphics g = e.Graphics!;
        g.PageUnit = GraphicsUnit.Inch;
        g.PageScale = 1f / 96f;
        g.TranslateTransform(-e.PageSettings.HardMarginX * 0.96f, -e.PageSettings.HardMarginY * 0.96f);   // margins are 1/100 inch
        float fit = Math.Min(1f, Math.Min(e.PageBounds.Width * 0.96f / PageWidth, e.PageBounds.Height * 0.96f / PageHeight));
        g.ScaleTransform(fit, fit);
    }

    /// <summary>Draws the receipt. <paramref name="g"/> must use 1/96-inch units with the page's top-left as the origin.</summary>
    public static ReceiptLayout Draw(Graphics g, ReceiptData d)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
        using var fonts = new Fonts();

        // Pick the roomiest density whose content still ends above the footer.
        Metrics chosen = Densities[^1];
        foreach (Metrics m in Densities)
            if (Run(g, d, fonts, m, paint: false).InfoBox.Bottom <= ContentLimit) { chosen = m; break; }
        return Run(g, d, fonts, chosen, paint: true);
    }

    private static ReceiptLayout Run(Graphics g, ReceiptData d, Fonts f, Metrics m, bool paint)
    {
        var culture = CultureInfo.InvariantCulture;
        // Per call, not static: GDI+ objects are not thread-safe and receipts may be rendered from several threads (tests).
        using var Typographic = new StringFormat(StringFormat.GenericTypographic)
        {
            FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces
        };
        using var green = new SolidBrush(Theme.Accent);
        using var dark = new SolidBrush(Theme.TextPrimary);
        using var gray = new SolidBrush(Theme.TextSecondary);
        using var rule = new Pen(Theme.BorderLight, 1f);
        Font label = f.Make(9, true), value = f.Make(15.5f, true), body = f.Make(m.FarePx), bodyBold = f.Make(m.FarePx, true);

        float Spaced(string text, Font font, Brush brush, float x, float y, float spacing, bool alignRight = false)
        {
            float width = 0;
            foreach (char c in text) width += g.MeasureString(c.ToString(), font, PointF.Empty, Typographic).Width + spacing;
            width -= text.Length > 0 ? spacing : 0;
            float cx = alignRight ? x - width : x;
            if (paint)
                foreach (char c in text)
                {
                    g.DrawString(c.ToString(), font, brush, cx, y, Typographic);
                    cx += g.MeasureString(c.ToString(), font, PointF.Empty, Typographic).Width + spacing;
                }
            return width;
        }

        void Text(string text, Font font, Brush brush, RectangleF box, bool alignRight = false, bool wrap = false)
        {
            if (!paint || text.Length == 0) return;
            using var format = new StringFormat(StringFormat.GenericTypographic)
            {
                Alignment = alignRight ? StringAlignment.Far : StringAlignment.Near,
                Trimming = StringTrimming.EllipsisWord,
                FormatFlags = (wrap ? 0 : StringFormatFlags.NoWrap) | StringFormatFlags.LineLimit
            };
            g.DrawString(text, font, brush, box, format);
        }

        float TextWidth(string text, Font font) => g.MeasureString(text, font, PointF.Empty, Typographic).Width;
        void Rule(float y, float x1 = Left, float x2 = Right) { if (paint) g.DrawLine(rule, x1, y, x2, y); }
        void Panel(RectangleF r, Color fill, float radius)
        {
            if (!paint) return;
            using var path = Gfx.RoundedRect(r, radius);
            using var brush = new SolidBrush(fill);
            g.FillPath(brush, path);
        }

        // ---- top bar, logo and title ----
        if (paint)
        {
            g.FillRectangle(green, 0, 0, PageWidth, 8);
            BrandArt.DrawScaled(g, Assets.Logo(LogoVariant.Green, 192), new Rectangle(50, 40, 72, 72));
        }
        Text("SkyWings", f.Make(27, true), dark, new RectangleF(124, 48, 300, 38));
        Spaced("AIRLINE", f.Make(9, true), green, 126, 90, 5);
        Spaced("BOOKING RECEIPT", f.Make(13, true), green, Right - 2, 58, 2.4f, alignRight: true);
        Text($"Issued {d.IssuedOn.ToString("d MMM yyyy", culture)}", f.Make(11), gray, new RectangleF(Right - 260, 82, 260, 18), alignRight: true);
        Rule(129);

        // ---- payment status panel ----
        var band = new RectangleF(Left, 153, Right - Left, 78);
        Panel(band, PanelTint, 18);
        Spaced("PAYMENT STATUS", label, gray, 86, 168, 2);
        Font statusFont = f.Make(20, true);
        float statusWidth = TextWidth(d.PaymentStatus, statusFont);
        Text(d.PaymentStatus, statusFont, green, new RectangleF(86, 186, statusWidth + 12, 28));
        Spaced("BOOKING REFERENCE", label, gray, Right - 20, 168, 2, alignRight: true);
        float room = band.Width - 40 - statusWidth - 24;   // what the reference may use before it would touch the status
        Font refFont = f.Make(21, true);
        for (float px = 21; px > 11 && TextWidth(d.Reference, refFont) > room; px -= 1) refFont = f.Make(px, true);
        Text(d.Reference, refFont, dark, new RectangleF(Right - 20 - Math.Max(room, 60), 186, Math.Max(room, 60), 28), alignRight: true);

        // ---- route ----
        Spaced("FLIGHT INFORMATION", f.Make(10, true), green, 69, 262, 2.4f);
        Font code = f.Make(54, true), city = f.Make(15);
        Text(d.OriginCode, code, dark, new RectangleF(68, 284, 190, 66));
        Text(d.OriginCity, city, gray, new RectangleF(70, 350, 220, 22));
        Text(d.DestinationCode, code, dark, new RectangleF(480, 284, 200, 66));
        Text(d.DestinationCity, city, gray, new RectangleF(482, 350, 240, 22));
        if (paint)
        {
            g.DrawLine(rule, 262, 323, 455, 323);
            using var arrow = new Pen(Theme.Accent, 3.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            g.DrawLine(arrow, 330, 312, 372, 312);
            g.DrawLines(arrow, new PointF[] { new(362, 303), new(372, 312), new(362, 321) });
        }
        Rule(392);

        // ---- details grid (3 columns) ----
        float[] cx = { TextLeft, 310, 552 }, cw = { 222, 224, Right - 552 };
        string Stamp(DateTime t) => $"{t.ToString("d MMM yyyy", culture)} · {t.ToString("HH:mm", culture)}";

        // Values wrap onto up to four lines; a row grows to fit its longest value so it never runs into the next row.
        const int MaxLines = 4;
        float LineHeight = (float)Math.Ceiling(value.GetHeight(g));   // the font's real line height, so a box never clips its last line
        bool truncated = false;
        int LinesNeeded(string text, int col)
        {
            if (text.Length == 0) return 1;
            using var wrapping = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = 0, Trimming = StringTrimming.Word };   // wraps like the drawing does
            float height = g.MeasureString(text, value, new SizeF(cw[col], 10000), wrapping).Height;
            int lines = Math.Max(1, (int)Math.Ceiling(height / value.GetHeight(g) - 0.05));
            if (lines > MaxLines) truncated = true;
            return Math.Min(lines, MaxLines);
        }

        var gridRows = new List<(string Title, string Text)[]>
        {
            new[] { (d.Passengers.Contains(',') ? "PASSENGERS" : "PASSENGER", d.Passengers), ("FLIGHT", d.FlightNo), ("CLASS / SEAT", d.ClassAndSeat) },
            new[] { ("DEPARTURE", Stamp(d.Departure)), ("ARRIVAL", Stamp(d.Arrival)), ("TRIP TYPE", d.TripType) }
        };
        if (d.Return is { } back)
            gridRows.Add(new[]
            {
                ("RETURN FLIGHT", $"{back.FlightNo} · {back.Route}"), ("RETURN DEPARTURE", Stamp(back.Departure)),
                (back.Seats.Contains(',') ? "RETURN SEATS" : "RETURN SEAT", back.Seats.Length > 0 ? back.Seats : "—")
            });

        float rowY = 410;
        var rowHeights = new List<float>();
        foreach (var row in gridRows)
        {
            int lines = Enumerable.Range(0, row.Length).Max(c => LinesNeeded(row[c].Text, c));
            for (int c = 0; c < row.Length; c++)
            {
                Spaced(row[c].Title, label, gray, cx[c], rowY, 1.8f);
                Text(row[c].Text, value, dark, new RectangleF(cx[c], rowY + 19, cw[c], lines * LineHeight + 2), wrap: true);
            }
            float height = Math.Max(m.GridPitch, 19 + lines * LineHeight + 10);
            rowHeights.Add(height);
            rowY += height;
        }
        float gridEnd = rowY - 8;
        Rule(gridEnd);

        // ---- fare & payment summary ----
        float y = gridEnd + 22;
        Spaced("FARE & PAYMENT SUMMARY", f.Make(10, true), green, 73, y, 2.4f);
        y += 30;
        foreach (ReceiptLine line in d.FareLines)
        {
            Text(line.Label, body, gray, new RectangleF(TextLeft, y, 440, 20));
            Text(line.Text, bodyBold, line.Discount ? green : dark, new RectangleF(TextRight - 220, y, 220, 20), alignRight: true);
            y += m.FarePitch;
        }
        y += 4;
        Rule(y, 70, 725);
        y += 18;
        Spaced("TOTAL PAID", f.Make(15, true), green, TextLeft, y + 8, 2.4f);
        Text(d.TotalPaid, f.Make(30, true), dark, new RectangleF(TextRight - 330, y - 2, 330, 40), alignRight: true);
        y += 48;
        Text("Payment method", body, gray, new RectangleF(TextLeft, y, 300, 20));
        Text(d.PaymentMethod, bodyBold, dark, new RectangleF(TextRight - 340, y, 340, 20), alignRight: true);
        y += 40;

        // ---- notice box ----
        var info = new RectangleF(68, y, 658, 88);
        Panel(info, Theme.BgMain, 16);
        Spaced("IMPORTANT RECEIPT INFORMATION", f.Make(11, true), green, 88, info.Top + 14, 1.2f);
        Text("This confirms your booking payment. It is not a boarding pass.", f.Make(13), dark, new RectangleF(88, info.Top + 36, 620, 20));
        Text("Boarding passes are issued separately after successful check-in.", f.Make(13), gray, new RectangleF(88, info.Top + 58, 620, 20));

        // ---- footer, pinned to the bottom of the page ----
        Rule(FooterLineY, 68, 724);
        Spaced("SKYWINGS AIRLINE · DIGITAL BOOKING RECEIPT", f.Make(10, true), green, 70, FooterLineY + 12, 1.2f);
        Spaced("PAGE 1 OF 1", f.Make(10, true), gray, 722, FooterLineY + 12, 1.2f, alignRight: true);
        Text("Keep this receipt for your records. Present your booking reference when you check in.", f.Make(10.5f), gray,
            new RectangleF(70, FooterLineY + 36, 650, 16));

        return new ReceiptLayout(band, info, FooterLineY, FooterLineY + 52, rowHeights, truncated);
    }
}
