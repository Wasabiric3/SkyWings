using SkyWings;
using SkyWings.UI;

namespace SkyWings.Tests;

[TestClass]
public class GlassButtonTests
{
    [TestMethod]
    public void Contrast_BlackOnWhite_Is21()
        => Assert.AreEqual(21.0, Contrast.Ratio(Color.Black, Color.White), 0.01);

    [TestMethod]
    public void PrimaryButton_TextContrastAtLeast4_5()
        => Assert.IsTrue(Contrast.Ratio(Color.White, Theme.Primary) >= 4.5, "white on Primary is too faint");

    [TestMethod]
    public void DisabledButton_TextContrastAtLeast3()
        => Assert.IsTrue(Contrast.Ratio(Theme.DisabledText, Theme.DisabledBack) >= 3.0,
            $"disabled text contrast {Contrast.Ratio(Theme.DisabledText, Theme.DisabledBack):F2}");

    private static Color FillOf(GlassButtonStyle style, bool enabled)
    {
        using var button = new GlassButton { Style = style, Enabled = enabled, Width = 120, Height = 40 };
        using var bmp = new Bitmap(120, 40);
        button.DrawToBitmap(bmp, new Rectangle(0, 0, 120, 40));
        return bmp.GetPixel(12, 20);   // inside the rounded rect, left of the (empty) caption
    }

    [TestMethod]
    public void Primary_FillsWithPrimary()
        => Assert.AreEqual(Theme.Primary.ToArgb(), FillOf(GlassButtonStyle.Primary, true).ToArgb());

    [TestMethod]
    public void Secondary_FillsWithSoftFill()
        => Assert.AreEqual(Theme.SoftFill.ToArgb(), FillOf(GlassButtonStyle.Secondary, true).ToArgb());

    [TestMethod]
    public void Disabled_FillsWithDisabledBack()
        => Assert.AreEqual(Theme.DisabledBack.ToArgb(), FillOf(GlassButtonStyle.Primary, false).ToArgb());

    [TestMethod]
    public void Button_IsOpaqueAroundCorners()
    {
        using var button = new GlassButton { Width = 120, Height = 40 };
        using var bmp = new Bitmap(120, 40);
        button.DrawToBitmap(bmp, new Rectangle(0, 0, 120, 40));
        Assert.AreEqual(255, bmp.GetPixel(0, 0).A, "corner pixel must be painted, not transparent");
    }
}
