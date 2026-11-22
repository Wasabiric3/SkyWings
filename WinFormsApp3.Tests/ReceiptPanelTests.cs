using SkyWings.UI;

namespace SkyWings.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ReceiptPanelTests
{
    private static readonly (string, string)[] Rows =
    {
        ("Passengers", "Brisias G. Labuson"), ("Class", "Business"), ("Outbound", "PR-304 · Manila (MNL) → Amsterdam (AMS)"), ("Seats", "BUS4B")
    };

    private static Bitmap Render(ReceiptPanel panel)
    {
        panel.CreateControl();
        var bmp = new Bitmap(panel.Width, panel.Height);
        panel.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
        return bmp;
    }

    private static ReceiptPanel Make(string? totalPaid = null, Color? band = null)
    {
        var panel = new ReceiptPanel { Width = 760, Height = 360 };
        if (band is { } c) panel.BandColor = c;
        panel.SetReceipt("SKY-4B702937F2C1", Rows, totalPaid);
        return panel;
    }

    // In the green header band, clear of the title (left) and the logo (right).
    private static Point InBand(ReceiptPanel panel) => new(panel.Width / 2, 8);

    [TestMethod]
    public void ByDefault_TheHeaderBandIsBrand_LikeTheRestOfTheApp()
    {
        using ReceiptPanel panel = Make();
        using Bitmap bmp = Render(panel);
        Assert.AreEqual(Theme.Brand.ToArgb(), panel.BandColor.ToArgb());
        Assert.AreEqual(Theme.Brand.ToArgb(), bmp.GetPixel(InBand(panel).X, InBand(panel).Y).ToArgb());
    }

    [TestMethod]
    public void WithABrandBand_TheHeaderIsDeepGreen()
    {
        using ReceiptPanel panel = Make(band: Theme.Brand);
        using Bitmap bmp = Render(panel);
        Assert.AreEqual(Theme.Brand.ToArgb(), bmp.GetPixel(InBand(panel).X, InBand(panel).Y).ToArgb());
    }

    [TestMethod]
    public void WithATotalPaid_ALightGreenBandSitsAtTheBottomOfTheDetails()
    {
        using ReceiptPanel panel = Make(totalPaid: "PHP 117,600.00");
        using Bitmap bmp = Render(panel);
        Rectangle band = panel.TotalBand();
        Assert.AreEqual("PHP 117,600.00", panel.TotalPaid);
        Assert.AreEqual(Theme.BrandSoft.ToArgb(), bmp.GetPixel(band.X + 6, band.Y + band.Height / 2).ToArgb());
        Assert.IsGreaterThan(Gfx.S(panel, 100), band.Y, "below the first rows of details");
        Assert.IsLessThan(panel.Height, band.Bottom);
    }

    [TestMethod]
    public void WithoutATotalPaid_NoBandIsPainted()
    {
        using ReceiptPanel panel = Make();
        using Bitmap bmp = Render(panel);
        Assert.IsNull(panel.TotalPaid);
        Rectangle band = panel.TotalBand();
        Assert.AreEqual(Color.White.ToArgb(), bmp.GetPixel(band.X + 6, band.Y + band.Height / 2).ToArgb());
    }

    [TestMethod]
    public void TheDetailRows_StopAboveTheTotalBand()
    {
        using var panel = new ReceiptPanel { Width = 760, Height = 200 };
        panel.SetReceipt("SKY-1", Enumerable.Range(0, 12).Select(i => ($"Label {i}", "Value")), "PHP 1.00");
        using Bitmap bmp = Render(panel);   // must not throw, and no row may be painted over the band
        Rectangle band = panel.TotalBand();
        Assert.AreEqual(Theme.BrandSoft.ToArgb(), bmp.GetPixel(band.X + 6, band.Y + band.Height / 2).ToArgb());
    }
}
