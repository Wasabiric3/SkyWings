using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using System.Drawing.Text;
using System.Globalization;
using SkyWings.Email;

namespace SkyWings.UI;

/// <summary>
/// Draws printed boarding passes following the brand kit's "Boarding_Pass_Example" ticket: tinted backdrop, white card with a
/// header band, passenger and big route, flight details, a dashed tear line and a QR panel. A ticket is laid out in a
/// 1040 x 430 design space; the printout places up to three tickets on each A4 page.
/// </summary>
internal static class BoardingPassPrinter
{
    public const int TicketWidth = 1040, TicketHeight = 430;
    private const int PassesPerPage = 3;
    private const float TicketScale = 0.69f, PageLeft = 38, PageTop = 70, PageGap = 26;

    private static readonly Color Backdrop = Color.FromArgb(0xED, 0xF2, 0xEE), Band = Color.FromArgb(0xF7, 0xFA, 0xF7);
    private static readonly Color QrInk = Color.FromArgb(0x23, 0x47, 0x39);

    // ------------------------------------------------------------------ printing

    /// <summary>A print document for the passes: three tickets per A4 page, as many pages as needed.</summary>
    public static PrintDocument CreateDocument(IReadOnlyList<BoardingPassData> passes, IQrRenderer? qr = null)
    {
        var document = new PrintDocument { DocumentName = $"SkyWings boarding passes {(passes.Count > 0 ? passes[0].Reference : "")}".Trim() };
        document.DefaultPageSettings.PaperSize = new PaperSize("A4", 827, 1169);   // hundredths of an inch
        int page = 0;
        document.PrintPage += (_, e) => RenderPage(e, passes, page++, qr);
        return document;
    }

    public static void ShowPreview(IWin32Window owner, IReadOnlyList<BoardingPassData> passes)
    {
        using PrintDocument document = CreateDocument(passes);
        using var preview = new PrintPreviewDialog { Document = document, Width = 900, Height = 1000 };
        preview.ShowDialog(owner);
    }

    private static void RenderPage(PrintPageEventArgs e, IReadOnlyList<BoardingPassData> passes, int pageIndex, IQrRenderer? qr)
    {
        Graphics g = e.Graphics!;
        ReceiptPrinter.BeginA4Page(e);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var bar = new SolidBrush(Theme.Accent)) g.FillRectangle(bar, 0, 0, ReceiptPrinter.PageWidth, 8);

        int first = pageIndex * PassesPerPage;
        int pages = Math.Max(1, (passes.Count + PassesPerPage - 1) / PassesPerPage);
        for (int i = 0; i < PassesPerPage && first + i < passes.Count; i++)
        {
            GraphicsState state = g.Save();
            g.TranslateTransform(PageLeft, PageTop + i * (TicketHeight * TicketScale + PageGap));
            g.ScaleTransform(TicketScale, TicketScale);
            DrawTicket(g, passes[first + i], qr);
            g.Restore(state);
        }

        using var font = new Font("Segoe UI", 10.5f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var gray = new SolidBrush(Theme.TextSecondary);
        using var centre = new StringFormat(StringFormat.GenericTypographic) { Alignment = StringAlignment.Center };
        string reference = passes.Count > 0 ? passes[0].Reference : "";
        g.DrawString($"SkyWings boarding passes · {reference} · Page {pageIndex + 1} of {pages}", font, gray,
            new RectangleF(0, 1080, ReceiptPrinter.PageWidth, 16), centre);
        e.HasMorePages = first + PassesPerPage < passes.Count;
    }

    // ------------------------------------------------------------------ one ticket (1040 x 430 design space)

    public static void DrawTicket(Graphics g, BoardingPassData d, IQrRenderer? qr = null)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        var culture = CultureInfo.InvariantCulture;
        using var dark = new SolidBrush(Color.FromArgb(0x1C, 0x28, 0x24));
        using var gray = new SolidBrush(Theme.TextSecondary);
        using var green = new SolidBrush(Theme.Primary);

        void Round(RectangleF r, float radius, Color fill)
        {
            using var path = Gfx.RoundedRect(r, radius);
            using var brush = new SolidBrush(fill);
            g.FillPath(brush, path);
        }
        Round(new RectangleF(0, 0, TicketWidth, TicketHeight), 28, Backdrop);
        Round(new RectangleF(26, 26, 988, 378), 24, Color.White);
        Round(new RectangleF(26, 26, 988, 94), 24, Band);

        // ---- header ----
        BrandArt.DrawScaled(g, Assets.Logo(LogoVariant.Green, 192), new Rectangle(48, 36, 60, 60));
        Text(g, "SkyWings", Px(27, true), dark, 118, 77);
        Spaced(g, "BOARDING PASS", Px(10, true), green, 118, 98, 1.6f);
        Text(g, $"CHECKED IN · {d.CheckedInAt.ToString("d MMM, h:mm tt", culture).ToUpperInvariant()}", Px(11, true), gray, 975, 81, right: true, maxWidth: 420);

        // ---- tear line ----
        using (var tear = new Pen(Theme.Border, 2f) { DashPattern = new[] { 3.5f, 3.5f } })
            g.DrawLine(tear, 768, 126, 768, 374);

        // ---- passenger and route ----
        Text(g, "PASSENGER", Px(11, true), gray, 63, 159);
        Text(g, d.PassengerName, Fit(g, d.PassengerName, 22, 690, bold: true), dark, 63, 186, maxWidth: 690);
        Text(g, d.OriginCode, Px(53, true), dark, 63, 245);
        Text(g, d.DestinationCode, Px(53, true), dark, 302, 245);
        using (var arrow = new Pen(Theme.Primary, 2.8f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
        {
            g.DrawLine(arrow, 222, 222, 268, 222);
            g.DrawLines(arrow, new PointF[] { new(258, 213), new(268, 222), new(258, 231) });
        }
        Text(g, d.OriginCity, Px(13), gray, 63, 266, maxWidth: 220);
        Text(g, d.DestinationCity, Px(13), gray, 304, 266, maxWidth: 440);

        // ---- flight details ----
        (string Label, string Value, float X, float Width)[] fields =
        {
            ("FLIGHT", d.FlightNo, 64, 115),
            ("DATE", d.Departure.ToString("d MMM yyyy", culture).ToUpperInvariant(), 190, 145),
            ("DEPARTS", d.Departure.ToString("h:mm tt", culture), 345, 115),
            ("CLASS", d.ClassName.ToUpperInvariant(), 470, 185),
            ("SEAT", d.Seat, 668, 90)
        };
        foreach (var (label, value, x, width) in fields)
        {
            Spaced(g, label, Px(10, true), gray, x, 311, 1.2f);
            Text(g, value, Fit(g, value, 16, width, bold: true), dark, x, 338, maxWidth: width);
        }
        Text(g, "Present this pass with a valid ID at the gate.", Px(12), gray, 63, 376, maxWidth: 680);

        // ---- QR panel ----
        Round(new RectangleF(795, 151, 128, 128), 14, Color.White);
        DrawQr(g, d.Reference, qr, new Rectangle(801, 157, 116, 116));
        Spaced(g, "BOOKING REFERENCE", Px(9, true), gray, 859, 300, 1.2f, centre: true);
        Text(g, d.Reference, Fit(g, d.Reference, 12, 150, bold: true), dark, 859, 318, centre: true, maxWidth: 150);
    }

    /// <summary>
    /// The real QR code when the renderer works. Until the QR package is added the renderer throws, and a decorative bar
    /// pattern (not scannable, derived from the reference) is drawn so the panel is never blank.
    /// </summary>
    private static void DrawQr(Graphics g, string reference, IQrRenderer? qr, Rectangle area)
    {
        try
        {
            byte[] png = (qr ?? new QrCodeRenderer()).RenderPng(QrPayload.Build(reference), 8);
            using var stream = new MemoryStream(png);
            using var image = new Bitmap(stream);
            GraphicsState state = g.Save();
            g.InterpolationMode = InterpolationMode.NearestNeighbor;   // keep QR modules crisp
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.DrawImage(image, area);
            g.Restore(state);
            return;
        }
        catch (Exception ex) when (ex is NotSupportedException or ArgumentException or IOException) { }

        using var ink = new SolidBrush(QrInk);
        int seed = 17;
        float x = area.X;
        foreach (char ch in reference + reference + reference)
        {
            seed = seed * 31 + ch;
            float barWidth = 2 + Math.Abs(seed % 3) * 2;
            if (x + barWidth > area.Right) break;
            if (Math.Abs(seed % 2) == 0) g.FillRectangle(ink, x, area.Y, barWidth, area.Height);
            x += barWidth + 2;
        }
    }

    // ------------------------------------------------------------------ text helpers (baseline-positioned, like the SVG)

    private static readonly List<Font> OwnedFonts = new();   // tiny set of fonts, created once per size and kept for the process

    private static Font Px(float size, bool bold = false)
    {
        lock (OwnedFonts)
        {
            Font? found = OwnedFonts.FirstOrDefault(f => Math.Abs(f.Size - size) < 0.01f && f.Bold == bold);
            if (found is not null) return found;
            var font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
            OwnedFonts.Add(font);
            return font;
        }
    }

    /// <summary>The largest font up to <paramref name="px"/> (down to 11 px) in which the text fits the width.</summary>
    private static Font Fit(Graphics g, string text, float px, float maxWidth, bool bold)
    {
        using var format = new StringFormat(StringFormat.GenericTypographic) { FormatFlags = StringFormatFlags.NoWrap };
        for (float size = px; size > 11; size -= 1)
        {
            Font font = Px(size, bold);
            if (g.MeasureString(text, font, PointF.Empty, format).Width <= maxWidth) return font;
        }
        return Px(11, bold);
    }

    private static float Ascent(Font font)
        => font.FontFamily.GetCellAscent(font.Style) * font.Size / font.FontFamily.GetEmHeight(font.Style);

    private static void Text(Graphics g, string text, Font font, Brush brush, float x, float baseline,
        bool right = false, bool centre = false, float maxWidth = 400)
    {
        if (text.Length == 0) return;
        using var format = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = right ? StringAlignment.Far : centre ? StringAlignment.Center : StringAlignment.Near,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };
        float left = right ? x - maxWidth : centre ? x - maxWidth / 2 : x;
        g.DrawString(text, font, brush, new RectangleF(left, baseline - Ascent(font), maxWidth, font.Height + 2), format);
    }

    private static void Spaced(Graphics g, string text, Font font, Brush brush, float x, float baseline, float spacing, bool centre = false)
    {
        using var format = new StringFormat(StringFormat.GenericTypographic)
        {
            FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.MeasureTrailingSpaces
        };
        float Width(char c) => g.MeasureString(c.ToString(), font, PointF.Empty, format).Width;
        float total = text.Sum(c => Width(c) + spacing) - (text.Length > 0 ? spacing : 0);
        float cx = centre ? x - total / 2 : x;
        float top = baseline - Ascent(font);
        foreach (char c in text)
        {
            g.DrawString(c.ToString(), font, brush, cx, top, format);
            cx += Width(c) + spacing;
        }
    }
}
