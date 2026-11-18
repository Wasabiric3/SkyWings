using System.Text;
using SkyWings;
using SkyWings.Email;

namespace SkyWings.Tests;

[TestClass]
[DoNotParallelize]   // draws the shared cached logo bitmap: GDI+ objects are not thread-safe
public class ReceiptPdfTests
{
    private static ReceiptData Sample() => new(
        "SW-DEMO-2026-001", "PAID · DEMO TRANSACTION", new DateTime(2026, 11, 18),
        "MNL", "Manila", "CEB", "Cebu", "Patweak Malakas", "PR-101", "Economy · 01A",
        new DateTime(2026, 12, 1, 9, 15, 0), new DateTime(2026, 12, 1, 10, 45, 0), "One-way", null,
        new List<ReceiptLine> { new("Base fare", 5000m), new("Taxes and fees (VAT 12%)", 600m) },
        "PHP 5,600.00", "Credit Card **** 4521");

    [TestMethod]
    public void Create_ProducesAOnePageA4PdfHoldingAJpeg()
    {
        byte[] pdf = ReceiptPdf.Create(Sample());
        string text = Encoding.Latin1.GetString(pdf);

        Assert.IsTrue(text.StartsWith("%PDF-1.4"));
        Assert.IsTrue(text.TrimEnd().EndsWith("%%EOF"));
        StringAssert.Contains(text, "/MediaBox [0 0 595.28 841.89]");
        StringAssert.Contains(text, "/Count 1");
        StringAssert.Contains(text, "/Filter /DCTDecode");
        StringAssert.Contains(text, "/Width 1588 /Height 2246");
        Assert.IsTrue(pdf.Length > 20_000, "the page picture should be in the file");
    }

    [TestMethod]
    public void Wrap_WritesACorrectCrossReferenceTable()
    {
        byte[] jpeg = { 0xFF, 0xD8, 1, 2, 3, 0xFF, 0xD9 };
        byte[] pdf = ReceiptPdf.Wrap(jpeg, 10, 20);
        string text = Encoding.Latin1.GetString(pdf);

        int startxref = int.Parse(text[(text.LastIndexOf("startxref\n", StringComparison.Ordinal) + 10)..].Split('\n')[0]);
        Assert.AreEqual("xref", text.Substring(startxref, 4));

        // Every "n 0 obj" must start exactly where the table says it does.
        string[] entries = text[(startxref + 4)..].Split('\n').Skip(3).Take(5).ToArray();   // skip "", "0 6", free entry
        for (int i = 0; i < 5; i++)
        {
            int offset = int.Parse(entries[i][..10]);
            StringAssert.StartsWith(text[offset..], $"{i + 1} 0 obj");
        }
    }
}
