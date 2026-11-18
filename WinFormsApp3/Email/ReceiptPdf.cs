using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Text;
using SkyWings.UI;

namespace SkyWings.Email;

/// <summary>
/// Turns a receipt into a one-page A4 PDF that looks exactly like the "Print receipt" page. The page is drawn by
/// <see cref="ReceiptPrinter.Draw"/> (the same code the print preview uses), saved as a JPEG and wrapped in a small PDF, so
/// the customer gets what staff see on paper. No PDF library is needed; the text in it is part of the picture, not selectable.
/// </summary>
public static class ReceiptPdf
{
    /// <summary>Pixels per design unit. 2 gives about 192 dpi on A4, sharp when printed and still a small file.</summary>
    private const int Scale = 2;

    public static byte[] Create(ReceiptData data)
    {
        int width = ReceiptPrinter.PageWidth * Scale, height = ReceiptPrinter.PageHeight * Scale;
        using var page = new Bitmap(width, height);
        using (Graphics g = Graphics.FromImage(page))
        {
            g.Clear(Color.White);
            g.ScaleTransform(Scale, Scale);   // Draw works in 794 x 1123 units (1/96 inch)
            ReceiptPrinter.Draw(g, data);
        }
        return Wrap(Jpeg(page), width, height);
    }

    internal static byte[] Jpeg(Bitmap page)
    {
        ImageCodecInfo codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 92L);
        using var stream = new MemoryStream();
        page.Save(stream, codec, parameters);
        return stream.ToArray();
    }

    /// <summary>One A4 page (595.28 x 841.89 points) that shows the JPEG over the whole page.</summary>
    internal static byte[] Wrap(byte[] jpeg, int width, int height) => Wrap(new[] { (jpeg, width, height) });

    /// <summary>A PDF with one A4 page per JPEG, each shown over the whole page.</summary>
    internal static byte[] Wrap(IReadOnlyList<(byte[] Jpeg, int Width, int Height)> pages)
    {
        var output = new MemoryStream();
        var offsets = new List<long>();

        void Text(string s) { byte[] b = Encoding.ASCII.GetBytes(s); output.Write(b, 0, b.Length); }
        void Object(string body) { offsets.Add(output.Position); Text($"{offsets.Count} 0 obj\n{body}\nendobj\n"); }

        // Objects: 1 catalog, 2 page list, then for page n (from 0): 3+3n the page, 4+3n its content, 5+3n its picture.
        Text("%PDF-1.4\n");
        Object("<< /Type /Catalog /Pages 2 0 R >>");
        Object($"<< /Type /Pages /Kids [{string.Join(" ", pages.Select((_, n) => $"{3 + 3 * n} 0 R"))}] /Count {pages.Count} >>");
        for (int n = 0; n < pages.Count; n++)
        {
            Object($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595.28 841.89] /Contents {4 + 3 * n} 0 R /Resources << /XObject << /Im0 {5 + 3 * n} 0 R >> >> >>");
            string content = "q 595.28 0 0 841.89 0 0 cm /Im0 Do Q";
            Object($"<< /Length {content.Length} >>\nstream\n{content}\nendstream");

            (byte[] jpeg, int width, int height) = pages[n];
            offsets.Add(output.Position);
            Text(string.Create(CultureInfo.InvariantCulture,
                $"{offsets.Count} 0 obj\n<< /Type /XObject /Subtype /Image /Width {width} /Height {height} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode /Length {jpeg.Length} >>\nstream\n"));
            output.Write(jpeg, 0, jpeg.Length);
            Text("\nendstream\nendobj\n");
        }

        long xref = output.Position;
        Text($"xref\n0 {offsets.Count + 1}\n0000000000 65535 f \n");
        foreach (long offset in offsets) Text(offset.ToString("D10", CultureInfo.InvariantCulture) + " 00000 n \n");
        Text($"trailer\n<< /Size {offsets.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return output.ToArray();
    }
}
