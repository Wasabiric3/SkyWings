using SkyWings;
using SkyWings.UI;

namespace SkyWings.Tests;

[TestClass]
[DoNotParallelize]   // draws the shared cached logo bitmap: GDI+ objects are not thread-safe
public class ReceiptPrinterTests
{
    private static ReceiptData Sample(int travellers = 1, bool roundTrip = false, int fareLines = 5, string name = "Patweak Malakas")
    {
        var lines = new List<ReceiptLine> { new("Base fare", 49000m) };
        for (int i = 1; i < fareLines - 1; i++) lines.Add(new($"Add-on number {i}", 350m * i));
        lines.Add(new("Taxes and fees (VAT 12%)", 2440m));
        return new ReceiptData(
            "SW-DEMO-2026-001", "PAID · DEMO TRANSACTION", new DateTime(2026, 11, 18),
            "MNL", "Manila", "NRT", "Tokyo Narita",
            string.Join(", ", Enumerable.Repeat(name, travellers)), "SW328", "Business · 01B",
            new DateTime(2026, 11, 20, 9, 15, 0), new DateTime(2026, 11, 20, 14, 35, 0),
            roundTrip ? "Round trip" : "One-way",
            roundTrip ? new ReceiptReturn("SW329", "NRT → MNL", new DateTime(2026, 11, 27, 8, 0, 0), "01B") : null,
            lines, "PHP 53,790.00", "Debit Card ···· 3523");
    }

    private static (Bitmap Page, ReceiptLayout Layout) Render(ReceiptData data)
    {
        var bmp = new Bitmap(ReceiptPrinter.PageWidth, ReceiptPrinter.PageHeight);
        using var g = Graphics.FromImage(bmp);
        g.Clear(Color.White);
        return (bmp, ReceiptPrinter.Draw(g, data));
    }

    [TestMethod]
    public void TopBand_IsBrandGreen_AcrossTheWholeWidth()
    {
        (Bitmap page, _) = Render(Sample());
        using (page)
        {
            Assert.AreEqual(Theme.Accent.ToArgb(), page.GetPixel(2, 3).ToArgb());
            Assert.AreEqual(Theme.Accent.ToArgb(), page.GetPixel(ReceiptPrinter.PageWidth - 3, 3).ToArgb());
            Assert.AreEqual(Color.White.ToArgb(), page.GetPixel(400, 40).ToArgb(), "the area below the band stays white paper");
        }
    }

    [TestMethod]
    public void PaymentStatusBand_IsATintedPanel()
    {
        (Bitmap page, ReceiptLayout layout) = Render(Sample());
        using (page)
            Assert.AreEqual(ReceiptPrinter.PanelTint.ToArgb(),
                page.GetPixel((int)layout.StatusBand.Left + 6, (int)(layout.StatusBand.Top + layout.StatusBand.Height / 2)).ToArgb());
    }

    [TestMethod]
    public void ImportantInfoBox_UsesThePageTint()
    {
        (Bitmap page, ReceiptLayout layout) = Render(Sample());
        using (page)
            Assert.AreEqual(Theme.BgMain.ToArgb(),
                page.GetPixel((int)layout.InfoBox.Left + 6, (int)(layout.InfoBox.Top + layout.InfoBox.Height / 2)).ToArgb());
    }

    [TestMethod]
    public void OneTraveller_OneWay_FitsAboveTheFooter()
    {
        (Bitmap page, ReceiptLayout layout) = Render(Sample());
        using (page) AssertFits(layout);
    }

    [TestMethod]
    public void WorstCase_ManyTravellers_LongNames_RoundTrip_ManyFareLines_StillFitsOnOnePage()
    {
        ReceiptData data = Sample(travellers: 6, roundTrip: true, fareLines: 10, name: "Maria Concepcion de la Santisima Trinidad");
        (Bitmap page, ReceiptLayout layout) = Render(data);
        using (page) AssertFits(layout);
    }


    [TestMethod]
    public void LongPassengerList_MakesItsRowTaller_InsteadOfOverlappingTheNextRow()
    {
        (Bitmap page, ReceiptLayout layout) = Render(Sample(travellers: 3, name: "Maria Concepcion de la Santisima Trinidad"));
        using (page)
        {
            Assert.IsGreaterThan(layout.GridRowHeights[1],
layout.GridRowHeights[0], $"row heights {string.Join(", ", layout.GridRowHeights)}: the wrapped passenger row must grow");
            AssertFits(layout);
        }
    }

    [TestMethod]
    public void AGroupOfFour_IsListedInFull_NothingCutOff()
    {
        (Bitmap page, ReceiptLayout layout) = Render(Sample(travellers: 4, name: "Maria Santos Cruz"));
        using (page) Assert.IsFalse(layout.AnyCellTruncated, "four ordinary names must all be shown");
    }

    [TestMethod]
    public void AnAbsurdlyLongList_IsCutOffWithAnEllipsis_ButStillFitsThePage()
    {
        (Bitmap page, ReceiptLayout layout) = Render(Sample(travellers: 30, name: "Maria Concepcion de la Santisima Trinidad", roundTrip: true, fareLines: 10));
        using (page)
        {
            Assert.IsTrue(layout.AnyCellTruncated);
            AssertFits(layout);
        }
    }
    [TestMethod]
    public void EmptyAndOddText_DoesNotThrow()
    {
        ReceiptData blank = Sample() with { Passengers = "", ClassAndSeat = "", Reference = "", PaymentMethod = "", FareLines = new List<ReceiptLine>() };
        (Bitmap page, ReceiptLayout layout) = Render(blank);
        using (page) AssertFits(layout);
    }

    [TestMethod]
    public void Draw_IsRepeatable_SameInputSamePixels()
    {
        (Bitmap a, _) = Render(Sample());
        (Bitmap b, _) = Render(Sample());
        using (a) using (b)
            for (int x = 0; x < a.Width; x += 37)
                for (int y = 0; y < a.Height; y += 41)
                    Assert.AreEqual(a.GetPixel(x, y).ToArgb(), b.GetPixel(x, y).ToArgb());
    }

    private static void AssertFits(ReceiptLayout layout)
    {
        Assert.IsLessThanOrEqualTo(layout.FooterTop - 4, layout.InfoBox.Bottom, $"content ends at {layout.InfoBox.Bottom}, footer starts at {layout.FooterTop}");
        Assert.IsLessThanOrEqualTo(ReceiptPrinter.PageHeight - 20, layout.FooterBottom, $"footer ends at {layout.FooterBottom} on a {ReceiptPrinter.PageHeight}px page");
    }
}

[TestClass]
[DoNotParallelize]
public class ReceiptPrintPageTests
{
    private static ReceiptData Data() => new(
        "SW-DEMO-2026-001", "PAID · DEMO TRANSACTION", new DateTime(2026, 11, 18), "MNL", "Manila", "NRT", "Tokyo Narita",
        "Patweak Malakas", "SW328", "Business · 01B", new DateTime(2026, 11, 20, 9, 15, 0), new DateTime(2026, 11, 20, 14, 35, 0),
        "One-way", null, new List<ReceiptLine> { new("Base fare", 49000m), new("Taxes and fees (VAT 12%)", 2440m) },
        "PHP 51,440.00", "Debit Card ···· 3523");

    private static System.Drawing.Printing.PreviewPageInfo[] Print(System.Drawing.Printing.PaperSize paper)
    {
        var controller = new System.Drawing.Printing.PreviewPrintController();
        using var document = new System.Drawing.Printing.PrintDocument { PrintController = controller };
        document.DefaultPageSettings.PaperSize = paper;
        ReceiptData data = Data();
        document.PrintPage += (_, e) => { ReceiptPrinter.RenderPage(e, data); };
        document.Print();
        return controller.GetPreviewPageInfo();
    }

    private static void RequirePrinterOrSkip()
    {
        if (System.Drawing.Printing.PrinterSettings.InstalledPrinters.Count == 0)
            Assert.Inconclusive("No printer is installed, so page settings cannot be created.");
    }

    [TestMethod]
    public void RenderPage_OnA4_ProducesExactlyOnePage()
    {
        RequirePrinterOrSkip();
        var pages = Print(new System.Drawing.Printing.PaperSize("A4", 827, 1169));
        Assert.HasCount(1, pages);
        Assert.IsNotNull(pages[0].Image);
    }

    [TestMethod]
    public void RenderPage_OnSmallerPaper_StillRendersOnePageWithoutThrowing()
    {
        RequirePrinterOrSkip();
        var pages = Print(new System.Drawing.Printing.PaperSize("Letter", 850, 1100));
        Assert.HasCount(1, pages);
    }
}
