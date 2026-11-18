using SkyWings.UI;

namespace SkyWings.Tests;

[TestClass]
public class BrandArtTests
{
    [TestMethod]
    [DataRow(720, 340, 300, 460)]   // tall login panel
    [DataRow(720, 340, 420, 168)]   // home hero
    [DataRow(720, 340, 720, 340)]   // same shape
    [DataRow(720, 340, 100, 100)]
    public void CoverSource_MatchesDestinationAspect_AndStaysInsideImage(int iw, int ih, int dw, int dh)
    {
        Rectangle src = BrandArt.CoverSource(new Size(iw, ih), new Size(dw, dh));

        double want = dw / (double)dh, got = src.Width / (double)src.Height;
        Assert.AreEqual(want, got, 0.02, $"source {src} would be stretched into {dw}x{dh}");
        Assert.IsTrue(src.Left >= 0 && src.Top >= 0 && src.Right <= iw && src.Bottom <= ih, $"{src} leaves the image");
        Assert.IsTrue(src.Width > 0 && src.Height > 0);
    }

    [TestMethod]
    public void CoverSource_TrimsTheKitsBakedInRoundedCorners()
    {
        Rectangle src = BrandArt.CoverSource(new Size(720, 340), new Size(720, 340));
        Assert.IsTrue(src.Left > 0 && src.Top > 0, "the baked-in corner radius must be cropped away");
    }

    [TestMethod]
    [DataRow(96)]
    [DataRow(144)]
    [DataRow(192)]
    public void MapPinIcon_NativePixelSizeMatchesDrawnSize_SoItIsNeverResampled(int dpi)
    {
        Bitmap pin = Assets.Icon("Map_Pin", white: true, dpi)!;
        Assert.AreEqual((int)Math.Round(24 * dpi / 96.0), pin.Width);
    }
}
