using SkyWings.UI;

namespace SkyWings.Tests;

[TestClass]
[DoNotParallelize]
public sealed class SummaryListTests
{
    private static Bitmap Render(SummaryList list)
    {
        list.CreateControl();
        var bmp = new Bitmap(list.Width, list.Height);
        list.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
        return bmp;
    }

    [TestMethod]
    public void SetRows_KeepsTheRows_AndPaintsInk()
    {
        using var list = new SummaryList { Width = 360, Height = 300 };
        list.SetRows(new[] { ("Flight", "PR-304 · Manila (MNL) → Amsterdam (AMS)"), ("Cabin", "Business") });
        CollectionAssert.AreEqual(new[] { "Flight", "Cabin" }, list.Rows.Select(r => r.Label).ToArray());
        using Bitmap bmp = Render(list);
        bool ink = false;
        for (int y = 0; y < 60 && !ink; y++)
            for (int x = 0; x < bmp.Width && !ink; x++) ink = bmp.GetPixel(x, y).ToArgb() != list.BackColor.ToArgb();
        Assert.IsTrue(ink);
    }

    [TestMethod]
    public void ALongValue_DoesNotPaintPastTheRightEdge()
    {
        using var list = new SummaryList { Width = 260, Height = 80 };
        list.SetRows(new[] { ("Flight", new string('W', 120)) });
        using Bitmap bmp = Render(list);
        for (int y = 0; y < bmp.Height; y++)
            Assert.AreEqual(list.BackColor.ToArgb(), bmp.GetPixel(bmp.Width - 1, y).ToArgb(), $"ink at the right edge, row {y}");
    }

    [TestMethod]
    public void RowsThatDoNotFit_AreNotPaintedHalfWay_AndNoRowsPaintsNothing()
    {
        using var list = new SummaryList { Width = 260, Height = 70 };
        list.SetRows(Enumerable.Range(0, 20).Select(i => ($"Row {i}", "value")));
        using Bitmap bmp = Render(list);   // must not throw
        list.SetRows(Array.Empty<(string, string)>());
        using Bitmap empty = Render(list);
        for (int y = 0; y < empty.Height; y++)
            for (int x = 0; x < empty.Width; x++)
                Assert.AreEqual(list.BackColor.ToArgb(), empty.GetPixel(x, y).ToArgb());
    }
}
