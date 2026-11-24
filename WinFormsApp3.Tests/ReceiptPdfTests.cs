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

        Assert.StartsWith("%PDF-1.4", text);
        Assert.EndsWith("%%EOF", text.TrimEnd());
        StringAssert.Contains(text, "/MediaBox [0 0 595.28 841.89]");
        StringAssert.Contains(text, "/Count 1");
        StringAssert.Contains(text, "/Filter /DCTDecode");
        StringAssert.Contains(text, "/Width 1588 /Height 2246");
        Assert.IsGreaterThan(20_000, pdf.Length, "the page picture should be in the file");
    }

    [TestMethod]
    public void Wrap_WithSeveralPages_ListsEachPageAndKeepsTheTableCorrect()
    {
        byte[] jpeg = { 0xFF, 0xD8, 1, 2, 3, 0xFF, 0xD9 };
        byte[] pdf = ReceiptPdf.Wrap(new[] { (jpeg, 10, 20), (jpeg, 10, 20), (jpeg, 10, 20) });
        string text = Encoding.Latin1.GetString(pdf);

        StringAssert.Contains(text, "/Kids [3 0 R 6 0 R 9 0 R] /Count 3");
        int startxref = int.Parse(text[(text.LastIndexOf("startxref\n", StringComparison.Ordinal) + 10)..].Split('\n')[0]);
        string[] entries = text[(startxref + 4)..].Split('\n').Skip(3).Take(11).ToArray();   // objects 1..11
        for (int i = 0; i < 11; i++)
            StringAssert.StartsWith(text[int.Parse(entries[i][..10])..], $"{i + 1} 0 obj");
    }

    [TestMethod]
    public void BoardingPassPdf_HasOnePagePerThreeTickets()
    {
        var pass = new BoardingPassData("SKY-AB12CD34EF56", "Maria Santos", "MNL", "Manila", "CEB", "Cebu", "PR-101",
            new DateTime(2026, 12, 1, 9, 15, 0), "Economy", "ECO-01A", new DateTime(2026, 11, 30, 18, 0, 0));

        string one = Encoding.Latin1.GetString(BoardingPassPdf.Create(new[] { pass }));
        string four = Encoding.Latin1.GetString(BoardingPassPdf.Create(new[] { pass, pass, pass, pass }));

        StringAssert.Contains(one, "/Count 1");
        StringAssert.Contains(four, "/Count 2");
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
