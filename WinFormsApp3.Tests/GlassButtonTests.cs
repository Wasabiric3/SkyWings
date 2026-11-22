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

    private static Bitmap Render(GlassButtonStyle style, bool pill)
    {
        using var button = new GlassButton { Style = style, Pill = pill, Width = 120, Height = 40 };
        var bmp = new Bitmap(120, 40);
        button.DrawToBitmap(bmp, new Rectangle(0, 0, 120, 40));
        return bmp;
    }

    [TestMethod]
    public void Brand_WhiteText_ContrastAtLeast4_5()
        => Assert.IsGreaterThanOrEqualTo(4.5, Contrast.Ratio(Color.White, Theme.Brand));

    [TestMethod]
    public void Brand_OnBrandSoft_ContrastAtLeast4_5()
        => Assert.IsGreaterThanOrEqualTo(4.5, Contrast.Ratio(Theme.Brand, Theme.BrandSoft));

    [TestMethod]
    public void PillPrimary_FillsWithBrand()
    {
        using Bitmap bmp = Render(GlassButtonStyle.Primary, pill: true);
        Assert.AreEqual(Theme.Brand.ToArgb(), bmp.GetPixel(14, 20).ToArgb());
    }

    [TestMethod]
    public void Pill_CornerShowsSurface()
    {
        using Bitmap bmp = Render(GlassButtonStyle.Primary, pill: true);
        Assert.AreEqual(Theme.BgMain.ToArgb(), bmp.GetPixel(2, 2).ToArgb(), "a pill's corner is outside the shape");
    }

    [TestMethod]
    public void Outline_FillsWhite()
    {
        using Bitmap bmp = Render(GlassButtonStyle.Outline, pill: true);
        Assert.AreEqual(Color.White.ToArgb(), bmp.GetPixel(14, 20).ToArgb());
    }

    [TestMethod]
    public void NonPillPrimary_StillFillsWithPrimary()
    {
        using Bitmap bmp = Render(GlassButtonStyle.Primary, pill: false);
        Assert.AreEqual(Theme.Primary.ToArgb(), bmp.GetPixel(14, 20).ToArgb());
    }
}
