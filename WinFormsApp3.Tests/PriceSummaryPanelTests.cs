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

    // What the wizard sends for a round trip with a discount, the busiest summary there is.
    private static PriceLine[] BusyRoundTrip() => new[]
    {
        new PriceLine("PR-304", "Manila (MNL) → Amsterdam (AMS)", PriceLineKind.Hero),
        new PriceLine("Departure", "Nov 21, 2026", PriceLineKind.Stat),
        new PriceLine("PR-305", "Amsterdam (AMS) → Manila (MNL)", PriceLineKind.Hero),
        new PriceLine("Return", "Nov 27, 2026", PriceLineKind.Stat),
        new PriceLine("Travellers", "3 travellers", PriceLineKind.Stat),
        new PriceLine("Cabin", "Business", PriceLineKind.Stat),
        new PriceLine("Base fare", "PHP 630,000.00"),
        new PriceLine("Senior / PWD", "-PHP 63,000.00", PriceLineKind.Discount),
        new PriceLine("Promo SKY10", "-PHP 56,700.00", PriceLineKind.Discount),
        new PriceLine("VAT 12%", "PHP 61,236.00"),
        new PriceLine("Total", "PHP 571,536.00", PriceLineKind.Total),
    };

    /// <summary>The total box's rows: every row inside the card that has a BrandSoft pixel in the content column.</summary>
    private static (int Top, int Bottom)? TotalBoxRows(PriceSummaryPanel panel, Bitmap bmp)
    {
        int x = panel.DisplayRectangle.Left + 4;
        int? top = null, bottom = null;
        for (int y = 0; y < bmp.Height; y++)
            if (bmp.GetPixel(x, y).ToArgb() == Theme.BrandSoft.ToArgb()) { top ??= y; bottom = y; }
        return top is null ? null : (top.Value, bottom!.Value);
    }

    [TestMethod]
    public void TheTotal_IsAlwaysDrawn_EvenWhenTheTripLinesDoNotFit_AtTheMinimumWindowSize()
    {
        // The minimum wizard window leaves the summary card about 365 px (96 dpi) tall.
        foreach (int cardHeight in new[] { 365, 420, 500 })
        {
            using var panel = new PriceSummaryPanel { Width = 300 };
            panel.Height = Gfx.S(panel, cardHeight);
            panel.SetLines(BusyRoundTrip());
            using Bitmap bmp = Render(panel);
            var box = TotalBoxRows(panel, bmp);
            Assert.IsNotNull(box, $"card height {cardHeight}: the total box is drawn");
            Assert.IsLessThanOrEqualTo(panel.DisplayRectangle.Bottom, box!.Value.Bottom, $"card height {cardHeight}: the box is inside the card");

            // The amount itself (brand-green text) is inside the box.
            bool amountInk = false;
            for (int y = box.Value.Top; y <= box.Value.Bottom && !amountInk; y++)
                for (int x = panel.DisplayRectangle.Left + 10; x < panel.DisplayRectangle.Right - 10 && !amountInk; x++)
                    amountInk = bmp.GetPixel(x, y).ToArgb() == Theme.Brand.ToArgb();
            Assert.IsTrue(amountInk, $"card height {cardHeight}: the total's text is drawn");
        }
    }

    [TestMethod]
    public void TheFareLines_StayVisible_WhenTheTripLinesAreCompacted()
    {
        using var panel = new PriceSummaryPanel { Width = 300 };
        panel.Height = Gfx.S(panel, 365);
        panel.SetLines(BusyRoundTrip());
        using Bitmap bmp = Render(panel);
        var box = TotalBoxRows(panel, bmp)!.Value;
        // Just above the total box, the last fare line (VAT) has ink: the fare breakdown was kept, the trip detail was trimmed.
        bool ink = false;
        for (int y = box.Top - Gfx.S(panel, 30); y < box.Top - 2 && !ink; y++)
            for (int x = panel.DisplayRectangle.Left; x < panel.DisplayRectangle.Right && !ink; x++)
                ink = bmp.GetPixel(x, y).ToArgb() != Theme.BgCard.ToArgb();
        Assert.IsTrue(ink, "the VAT line is drawn above the total");
    }

    [TestMethod]
    public void WithRoom_TheFullLayoutIsKept_StackedStatsAndAllLines()
    {
        using var panel = new PriceSummaryPanel { Width = 300, Height = 900 };
        panel.SetLines(new[]
        {
            new PriceLine("PR-304", "MNL → AMS", PriceLineKind.Hero),
            new PriceLine("Departure", "Nov 21, 2026", PriceLineKind.Stat),
            new PriceLine("Total", "PHP 1.00", PriceLineKind.Total),
        });
        using Bitmap bmp = Render(panel);
        var box = TotalBoxRows(panel, bmp)!.Value;
        // Title block 40 + hero 68 + stat 52 = 160, then the box: it sits right after them, not pushed to the card's bottom.
        Assert.IsLessThan(panel.DisplayRectangle.Top + Gfx.S(panel, 190), box.Top);
    }
}
