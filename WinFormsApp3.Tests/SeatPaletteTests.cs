using SkyWings;
using SkyWings.UI;

namespace SkyWings.Tests;

[TestClass]
public class SeatPaletteTests
{
    private static void AssertRgb(Color actual, int rgb, string what)
        => Assert.AreEqual(rgb, actual.ToArgb() & 0xFFFFFF, $"{what} was #{actual.ToArgb() & 0xFFFFFF:X6}");

    // The seat colours follow the Figma: light green free seats, deep green selected, amber held, grey taken.
    [TestMethod]
    public void Free_UsesBrandSoft() => AssertRgb(SeatPalette.For(SeatState.Free, false).Fill, 0xD9E8DA, "Free fill");

    [TestMethod]
    public void Selected_UsesBrand()
    {
        var p = SeatPalette.For(SeatState.Selected, false);
        AssertRgb(p.Fill, 0x1B4D3E, "Selected fill");
        Assert.AreEqual(Theme.Brand.ToArgb(), p.Border.ToArgb());
        Assert.AreEqual(Color.White.ToArgb(), p.Text.ToArgb());
    }

    [TestMethod]
    public void Taken_And_Held_UseTheFigmaColours()
    {
        AssertRgb(SeatPalette.For(SeatState.Taken, false).Fill, 0xC9C9C9, "Taken fill");
        AssertRgb(SeatPalette.For(SeatState.Held, false).Fill, 0xE8C183, "Held fill");
    }

    [TestMethod]
    public void Hover_ChangesOnlyFreeSeats()
    {
        Assert.AreNotEqual(SeatPalette.For(SeatState.Free, false).Fill.ToArgb(), SeatPalette.For(SeatState.Free, true).Fill.ToArgb());
        Assert.AreEqual(SeatPalette.For(SeatState.Taken, false).Fill.ToArgb(), SeatPalette.For(SeatState.Taken, true).Fill.ToArgb());
    }

    [TestMethod]
    public void AllStates_LabelContrastAtLeast3()
    {
        foreach (SeatState state in Enum.GetValues<SeatState>())
            foreach (bool hover in new[] { false, true })
            {
                var p = SeatPalette.For(state, hover);
                double ratio = Contrast.Ratio(p.Text, p.Fill);
                Assert.IsTrue(ratio >= 3.0, $"{state} hover={hover}: label contrast {ratio:F2}");
            }
    }

    [TestMethod]
    public void SeatLabel_Fits()
    {
        using var font = new Font(Theme.Small, FontStyle.Bold);
        int width = TextRenderer.MeasureText("12A", font, Size.Empty, TextFormatFlags.NoPadding).Width;
        Assert.IsTrue(width <= 38 - 4, $"'12A' is {width}px wide in a 38px seat at 96 dpi");
    }

    [TestMethod]
    public void StatusPills_UseKitColours()
    {
        var paid = GridStyle.StatusStyle("PAID");
        Assert.IsNotNull(paid);
        AssertRgb(paid.Value.Back, 0xDAECE1, "Paid back");
        AssertRgb(paid.Value.Fore, 0x315B49, "Paid text");
        AssertRgb(GridStyle.StatusStyle("Cancelled")!.Value.Back, 0xF5DDDA, "Cancelled back");
        AssertRgb(GridStyle.StatusStyle("Delayed")!.Value.Fore, 0xA85A37, "Delayed text");
        AssertRgb(GridStyle.StatusStyle("pending")!.Value.Back, 0xFAEBD0, "Pending back");
        Assert.IsNull(GridStyle.StatusStyle("On time"));
    }

    [TestMethod]
    public void StatusPills_TextContrastAtLeast3()
    {
        foreach (string s in new[] { "PAID", "CONFIRMED", "CANCELED", "REFUNDED", "DELAYED", "PENDING", "ACTIVE", "CLOSED" })
        {
            var style = GridStyle.StatusStyle(s)!.Value;
            Assert.IsTrue(Contrast.Ratio(style.Fore, style.Back) >= 3.0, $"{s} pill contrast too low");
        }
    }
}

[TestClass]
public class SeatLabelTests
{
    [TestMethod]
    public void FreeSeatLabel_IsRowAndLetter_NotTheClassPrefixedCode()
    {
        Assert.AreEqual("12A", SeatMap.SeatLabel(11, 'A'));
        Assert.AreEqual("1F", SeatMap.SeatLabel(0, 'F'));
    }

    [TestMethod]
    public void FreeSeatLabel_FitsInsideASeat_ForTheLargestRowNumbers()
    {
        using var font = new Font(Theme.Small, FontStyle.Bold);
        int width = TextRenderer.MeasureText(SeatMap.SeatLabel(98, 'K'), font, Size.Empty, TextFormatFlags.NoPadding).Width;
        Assert.IsTrue(width <= 38 - 4, $"'99K' is {width}px in a 38px seat");
    }

    [TestMethod]
    public void SelectedSeatLabel_WithATick_FitsInsideASeat_ForTheLargestRowNumbers()
    {
        using var font = new Font(Theme.Small, FontStyle.Bold);
        int width = TextRenderer.MeasureText(SeatMap.SeatLabel(98, 'K') + "✓", font, Size.Empty, TextFormatFlags.NoPadding).Width;
        Assert.IsTrue(width <= 38 - 2, $"'99K✓' is {width}px in a 38px seat");
    }

    private static SeatMap BusinessMap(int maxSelection)
    {
        var map = new SeatMap { MaxSelection = maxSelection };
        map.Configure(Aircraft.A320.Cabin("Business")!);
        return map;
    }

    [TestMethod]
    public void ASelectedSeat_OfASingleTraveller_ShowsItsCodeAndATick()
    {
        using SeatMap map = BusinessMap(1);
        map.SetSelected(new[] { SeatLayout.Code("Business", 1, 'A') });
        Assert.AreEqual("1A✓", map.SeatText(0, 0));
        Assert.AreEqual("1B", map.SeatText(0, 1), "a free seat shows row and letter");
    }

    [TestMethod]
    public void SelectedSeats_OfSeveralTravellers_ShowTheirOrderNumber()
    {
        using SeatMap map = BusinessMap(2);
        map.SetSelected(new[] { SeatLayout.Code("Business", 1, 'A'), SeatLayout.Code("Business", 2, 'B') });
        Assert.AreEqual("1", map.SeatText(0, 0));
        Assert.AreEqual("2", map.SeatText(1, 1));
    }

    [TestMethod]
    public void TakenAndHeldSeats_KeepTheirMarks()
    {
        using SeatMap map = BusinessMap(1);
        map.SetUnavailable(new[] { SeatLayout.Code("Business", 1, 'A') }, new[] { SeatLayout.Code("Business", 1, 'B') });
        Assert.AreEqual("✕", map.SeatText(0, 0));
        Assert.AreEqual("•••", map.SeatText(0, 1));
    }

    [TestMethod]
    public void LegendGrid_IsTwoByTwo_AndEachSwatchHasItsSeatColour()
    {
        using var legend = new SeatLegend { Grid = true, Width = 280 };
        legend.Height = Gfx.S(legend, 86);
        legend.CreateControl();
        using var bmp = new Bitmap(legend.Width, legend.Height);
        legend.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
        Color[] expected = { Theme.BrandSoft, Theme.Brand, Theme.SeatHeld, Theme.SeatTaken };   // Available, Selected, Held, Taken
        for (int i = 0; i < 4; i++)
        {
            Rectangle swatch = legend.SwatchBounds(i);
            Assert.AreEqual(expected[i].ToArgb(), bmp.GetPixel(swatch.X + swatch.Width / 2, swatch.Y + swatch.Height / 2).ToArgb(), $"swatch {i}");
        }
        Assert.IsLessThan(legend.SwatchBounds(1).X, legend.SwatchBounds(0).X);
        Assert.AreEqual(legend.SwatchBounds(0).X, legend.SwatchBounds(2).X, "second row starts under the first");
        Assert.IsLessThan(legend.SwatchBounds(2).Y, legend.SwatchBounds(0).Y);
    }
}
