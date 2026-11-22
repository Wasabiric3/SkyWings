using SkyWings.UI;

namespace SkyWings.Tests;

[TestClass]
[DoNotParallelize]
public sealed class PriceSummaryPanelTests
{
    private static Bitmap Render(PriceSummaryPanel panel)
    {
        var bmp = new Bitmap(panel.Width, panel.Height);
        panel.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
        return bmp;
    }

    private static int Measure(string text, Font font)
        => TextRenderer.MeasureText(text, font, Size.Empty, TextFormatFlags.NoPadding).Width;

    [TestMethod]
    public void FitFont_ShortText_KeepsStartSize()
    {
        using Font fit = PriceSummaryPanel.FitFont("PHP 5.00", Theme.Subtitle, 400);
        Assert.AreEqual(Theme.Subtitle.Size, fit.Size);
    }

    [TestMethod]
    public void FitFont_LongAmount_FitsTheWidth()
    {
        const string amount = "PHP 1,234,567.89";
        using Font fit = PriceSummaryPanel.FitFont(amount, Theme.Subtitle, 110);
        Assert.IsLessThanOrEqualTo(110, Measure(amount, fit));
        Assert.IsGreaterThanOrEqualTo(8f, fit.Size);
        Assert.IsLessThan(Theme.Subtitle.Size, fit.Size, "a long amount had to shrink");
    }

    [TestMethod]
    public void Total_LongAmount_PaintsInsideTheCard()
    {
        using var panel = new PriceSummaryPanel { Width = 300, Height = 420 };
        panel.SetLines(new[] { new PriceLine("Total", "PHP 1,234,567.89", PriceLineKind.Total) });
        using Bitmap bmp = Render(panel);
        Rectangle area = panel.DisplayRectangle;
        int outerEdge = panel.Width - Gfx.S(panel, panel.ShadowSize) - 3;   // just inside the card's border
        int margin = Gfx.S(panel, panel.ShadowSize) + Gfx.S(panel, panel.Radius);   // rows inside the card, clear of its rounded corners
        for (int y = margin; y < bmp.Height - margin; y++)
            for (int x = area.Right + 1; x < outerEdge; x++)
                Assert.AreEqual(Theme.BgCard.ToArgb(), bmp.GetPixel(x, y).ToArgb(), $"ink right of the content area at {x},{y}");
    }

    [TestMethod]
    public void Total_PaintsALightGreenBox()
    {
        using var panel = new PriceSummaryPanel { Width = 300, Height = 420 };
        panel.SetLines(new[] { new PriceLine("Total", "PHP 117,600.00", PriceLineKind.Total) });
        using Bitmap bmp = Render(panel);
        int x = panel.DisplayRectangle.Left + 4;
        bool found = false;
        for (int y = 0; y < bmp.Height && !found; y++) found = bmp.GetPixel(x, y).ToArgb() == Theme.BrandSoft.ToArgb();
        Assert.IsTrue(found, "the total sits in a BrandSoft box");
    }

    [TestMethod]
    public void HeroAndStatLines_Paint_AndShowTheirText()
    {
        using var panel = new PriceSummaryPanel { Width = 300, Height = 420 };
        panel.SetLines(new[]
        {
            new PriceLine("PR-304", "MNL → AMS", PriceLineKind.Hero),
            new PriceLine("Departure", "Nov 21, 2026", PriceLineKind.Stat),
            new PriceLine("Base fare", "PHP 105,000.00"),
        });
        using Bitmap bmp = Render(panel);
        Rectangle area = panel.DisplayRectangle;
        bool ink = false;
        for (int y = area.Top; y < area.Top + 160 && !ink; y++)
            for (int x = area.Left; x < area.Right && !ink; x++)
                ink = bmp.GetPixel(x, y).ToArgb() != Theme.BgCard.ToArgb();
        Assert.IsTrue(ink);
    }
}
