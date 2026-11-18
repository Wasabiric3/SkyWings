using SkyWings;
using SkyWings.UI;

namespace SkyWings.Tests;

[TestClass]
public class PanelPaintTests
{
    private static Bitmap Render(Control control, int w, int h)
    {
        control.Size = new Size(w, h);
        var bmp = new Bitmap(w, h);
        control.DrawToBitmap(bmp, new Rectangle(0, 0, w, h));
        return bmp;
    }

    private static double Luminance(Color c) => (0.2126 * c.R + 0.7152 * c.G + 0.0722 * c.B) / 255.0;

    [TestMethod]
    public void LoginBrandPanel_IsLightSageHero_NotDarkGradient()
    {
        using var panel = new LoginBrandPanel();
        using Bitmap bmp = Render(panel, 360, 520);
        Assert.IsTrue(Luminance(bmp.GetPixel(24, 24)) > 0.75, "top-left should be the light sage hero background");
        Assert.AreEqual(255, bmp.GetPixel(0, 0).A);
    }

    [TestMethod]
    public void LoginBrandPanel_Paints_AtSmallSizes_WithoutException()
    {
        using var panel = new LoginBrandPanel();
        using Bitmap a = Render(panel, 40, 40);
        using Bitmap b = Render(panel, 1, 1);
    }

    [TestMethod]
    public void ReceiptPanel_HeaderBand_IsFlatPrimary()
    {
        using var panel = new ReceiptPanel();
        panel.SetReceipt("SW-ABC123", new[] { ("Passenger", "Ana Cruz"), ("Flight", "SW101") });
        using Bitmap bmp = Render(panel, 760, 300);
        Assert.AreEqual(Theme.Primary.ToArgb(), bmp.GetPixel(380, 8).ToArgb());
        Assert.AreEqual(Theme.Primary.ToArgb(), bmp.GetPixel(700, 8).ToArgb(), "band must not be a gradient");
        Assert.AreEqual(255, bmp.GetPixel(0, 0).A, "corner must be painted, not transparent");
    }

    [TestMethod]
    public void DestinationCard_ArtBand_IsFlatAccent()
    {
        using var card = new DestinationCard { Accent = Color.Red, Title = "Asia", Subtitle = "12 flights", Meta = "from PHP 3,000" };
        using Bitmap bmp = Render(card, 268, 166);
        int s = Gfx.S(card, 8) * 2;   // margin + inset of the artwork
        Assert.AreEqual(Color.Red.ToArgb(), bmp.GetPixel(s + 6, s + 6).ToArgb());
    }

    [TestMethod]
    public void StatTile_And_PriceSummary_PaintOnCardColour()
    {
        using var tile = new StatTile { Title = "Bookings", Value = "42" };
        using Bitmap t = Render(tile, 220, 96);
        Assert.AreEqual(Theme.BgCard.ToArgb(), t.GetPixel(210 - Gfx.S(tile, 8), 48).ToArgb());

        using var summary = new PriceSummaryPanel();
        using Bitmap s = Render(summary, 300, 300);
        Assert.AreEqual(Theme.BgCard.ToArgb(), s.GetPixel(150, 250).ToArgb());
    }
}
