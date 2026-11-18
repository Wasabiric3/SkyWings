using SkyWings.UI;

namespace SkyWings.Tests;

[TestClass]
public class AssetsTests
{
    [TestMethod]
    [DataRow(96, "x1")]
    [DataRow(108, "x1")]
    [DataRow(120, "x15")]
    [DataRow(144, "x15")]
    [DataRow(156, "x15")]
    [DataRow(192, "x2")]
    [DataRow(288, "x2")]
    public void Bucket_PicksByDpi(int dpi, string expected)
        => Assert.AreEqual(expected, Assets.Bucket(dpi));

    [TestMethod]
    [DataRow(96)]
    [DataRow(144)]
    [DataRow(192)]
    public void Icon_Flights_Green_Loads(int dpi)
    {
        Bitmap? icon = Assets.Icon("Flights", white: false, dpi);
        Assert.IsNotNull(icon);
        Assert.IsTrue(icon.Width > 0 && icon.Height > 0);
    }

    [TestMethod]
    public void Icon_HigherDpi_IsLargerBitmap()
        => Assert.IsTrue(Assets.Icon("Flights", false, 192)!.Width > Assets.Icon("Flights", false, 96)!.Width);

    [TestMethod]
    public void Icon_White_Loads() => Assert.IsNotNull(Assets.Icon("Home", white: true, 96));

    [TestMethod]
    public void Icon_Missing_ReturnsNull_DoesNotThrow()
        => Assert.IsNull(Assets.Icon("DoesNotExist", false, 96));

    [TestMethod]
    public void Icon_SameCall_ReturnsSameInstance()
        => Assert.AreSame(Assets.Icon("Seats", false, 96), Assets.Icon("Seats", false, 96));

    [TestMethod]
    public void Logo_AllVariants_Load()
    {
        foreach (LogoVariant v in Enum.GetValues<LogoVariant>())
            Assert.IsNotNull(Assets.Logo(v, 96), v.ToString());
    }

    [TestMethod]
    public void Hero_And_Watermark_Load()
    {
        Assert.IsNotNull(Assets.Hero(96));
        Assert.IsNotNull(Assets.Watermark(96));
    }
}
