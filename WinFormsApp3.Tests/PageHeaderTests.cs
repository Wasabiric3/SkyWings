using SkyWings.UI;

namespace SkyWings.Tests;

[TestClass]
[DoNotParallelize]
public sealed class PageHeaderTests
{
    private static PageHeader Make(string title, string hint, int width = 600)
    {
        var host = new Panel { Width = width, Height = 400, BackColor = Theme.PageBg };
        var header = new PageHeader { Title = title, Hint = hint };
        host.Controls.Add(header);
        host.CreateControl();
        host.PerformLayout();
        return header;
    }

    [TestMethod]
    public void EmptyHint_IsShorterThanAHint() => FormHarness.OnStaThread(() =>
    {
        PageHeader plain = Make("Online check-in", "");
        PageHeader hinted = Make("Online check-in", "Enter your booking reference.");
        Assert.IsLessThan(hinted.Height, plain.Height);
    });

    [TestMethod]
    public void Height_GrowsWhenTheHintWraps() => FormHarness.OnStaThread(() =>
    {
        const string hint = "Check-in is open from the moment you book until departure. Enter your booking reference to find the flight.";
        PageHeader wide = Make("Online check-in", hint, 900);
        PageHeader narrow = Make("Online check-in", hint, 240);
        Assert.IsGreaterThan(wide.Height, narrow.Height);
    });

    [TestMethod]
    public void Paints_OnTheParentSurface() => FormHarness.OnStaThread(() =>
    {
        PageHeader header = Make("Title", "Hint");
        using var bmp = new Bitmap(header.Width, header.Height);
        header.DrawToBitmap(bmp, new Rectangle(0, 0, header.Width, header.Height));
        Assert.AreEqual(Theme.PageBg.ToArgb(), bmp.GetPixel(bmp.Width - 2, bmp.Height - 2).ToArgb());
    });

    [TestMethod]
    public void AccessibleName_IsTheTitle() => FormHarness.OnStaThread(() =>
    {
        PageHeader header = Make("Manage booking", "");
        Assert.AreEqual("Manage booking", header.AccessibleName);
        header.Title = "Flight results";
        Assert.AreEqual("Flight results", header.AccessibleName);
    });

    [TestMethod]
    public void IsDockedToTheTopByDefault() => FormHarness.OnStaThread(() => Assert.AreEqual(DockStyle.Top, new PageHeader().Dock));
}
