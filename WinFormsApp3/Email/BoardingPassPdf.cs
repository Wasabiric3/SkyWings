using System.Drawing;
using SkyWings.UI;

namespace SkyWings.Email;

/// <summary>
/// The boarding passes as a PDF that looks exactly like the print preview: A4 pages with three tickets each. Drawn by
/// <see cref="BoardingPassPrinter.DrawPage"/>, the same code the printer and the on-screen pass use. Like the receipt PDF,
/// each page is a picture (the text is not selectable).
/// </summary>
public static class BoardingPassPdf
{
    /// <summary>Pixels per design unit, as in <see cref="ReceiptPdf"/>: about 192 dpi on A4.</summary>
    private const int Scale = 2;

    public static byte[] Create(IReadOnlyList<BoardingPassData> passes, IQrRenderer? qr = null)
    {
        int width = ReceiptPrinter.PageWidth * Scale, height = ReceiptPrinter.PageHeight * Scale;
        var pages = new List<(byte[] Jpeg, int Width, int Height)>();
        for (int page = 0; page < BoardingPassPrinter.PageCount(passes.Count); page++)
        {
            using var bitmap = new Bitmap(width, height);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.White);
                g.ScaleTransform(Scale, Scale);   // DrawPage works in 794 x 1123 units (1/96 inch)
                BoardingPassPrinter.DrawPage(g, passes, page, qr);
            }
            pages.Add((ReceiptPdf.Jpeg(bitmap), width, height));
        }
        return ReceiptPdf.Wrap(pages);
    }
}
