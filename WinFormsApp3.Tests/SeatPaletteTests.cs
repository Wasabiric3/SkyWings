using SkyWings;
using SkyWings.UI;

namespace SkyWings.Tests;

[TestClass]
public class SeatPaletteTests
{
    private static void AssertRgb(Color actual, int rgb, string what)
        => Assert.AreEqual(rgb, actual.ToArgb() & 0xFFFFFF, $"{what} was #{actual.ToArgb() & 0xFFFFFF:X6}");

    [TestMethod]
    public void Free_UsesKitGreen() => AssertRgb(SeatPalette.For(SeatState.Free, false).Fill, 0x9EC8AC, "Free fill");

    [TestMethod]
    public void Selected_UsesPrimary()
    {
        var p = SeatPalette.For(SeatState.Selected, false);
        AssertRgb(p.Fill, 0x527866, "Selected fill");
        Assert.AreEqual(Theme.Accent.ToArgb(), p.Border.ToArgb());
        Assert.AreEqual(Color.White.ToArgb(), p.Text.ToArgb());
    }

    [TestMethod]
    public void Taken_And_Held_UseKitColours()
    {
        AssertRgb(SeatPalette.For(SeatState.Taken, false).Fill, 0xA7ADA9, "Occupied fill");
        AssertRgb(SeatPalette.For(SeatState.Held, false).Fill, 0xCDA875, "On hold fill");
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
}
