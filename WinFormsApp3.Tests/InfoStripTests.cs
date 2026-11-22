using SkyWings.UI;

namespace SkyWings.Tests;

[TestClass]
[DoNotParallelize]
public sealed class InfoStripTests
{
    [TestMethod]
    public void FillsWithBrandSoft_AndTheCornerShowsTheSurface()
    {
        using var strip = new InfoStrip { Width = 400, Text = "Departs 9:00 PM · about 13h 50m" };
        strip.CreateControl();
        using var bmp = new Bitmap(strip.Width, strip.Height);
        strip.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
        Assert.AreEqual(Theme.BgMain.ToArgb(), bmp.GetPixel(0, 0).ToArgb(), "the corner is outside the strip");
        Assert.AreEqual(Theme.BrandSoft.ToArgb(), bmp.GetPixel(strip.Width - 20, strip.Height / 2).ToArgb(), "the strip is light green");
    }

    [TestMethod]
    public void CustomColours_ArePainted_AndTheDefaultsAreBrandSoftAndBrand()
    {
        using var plain = new InfoStrip();
        Assert.AreEqual(Theme.BrandSoft.ToArgb(), plain.FillColor.ToArgb());
        Assert.AreEqual(Theme.Brand.ToArgb(), plain.TextColor.ToArgb());

        using var amber = new InfoStrip { Width = 400, FillColor = Theme.AmberSoft, TextColor = Theme.AmberText, Text = "DEMO ONLY — No real payment will be processed." };
        amber.CreateControl();
        using var bmp = new Bitmap(amber.Width, amber.Height);
        amber.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
        Assert.AreEqual(Theme.AmberSoft.ToArgb(), bmp.GetPixel(amber.Width - 20, amber.Height / 2).ToArgb());
        Assert.IsGreaterThanOrEqualTo(4.5, Contrast.Ratio(Theme.AmberText, Theme.AmberSoft), "the notice text is readable");
    }

    [TestMethod]
    public void IsOneLineTall_AndAShortTextLeavesTheRightSideClear()
    {
        using var strip = new InfoStrip { Width = 400, Text = "Hi" };
        Assert.IsGreaterThanOrEqualTo(Gfx.S(strip, 40), strip.Height);
    }
}
