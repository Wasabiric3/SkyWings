using SkyWings;
using SkyWings.UI;

namespace SkyWings.Tests;

[TestClass]
public class GlassCardTests
{
    [TestMethod]
    public void DisplayRectangle_InsetUnchanged()
    {
        using var card = new GlassCard { Width = 400, Height = 300, ShadowSize = 10, ContentPadding = 24 };

        int inset = Gfx.S(card, 10) + Gfx.S(card, 24);
        Rectangle r = card.DisplayRectangle;

        Assert.AreEqual(inset, r.X);
        Assert.AreEqual(inset, r.Y);
        Assert.AreEqual(400 - 2 * inset, r.Width);
        if (card.DeviceDpi == 96) Assert.AreEqual(34, r.X);   // the literal value layouts were built around
    }

    [TestMethod]
    public void ShadowSize_AndOpacity_AreAcceptedButNotRequired()
    {
        using var card = new GlassCard();
        card.ShadowSize = 8;
        card.Opacity = 120;
        Assert.AreEqual(Theme.BgCard.ToArgb(), card.BackColor.ToArgb());
    }

    [TestMethod]
    public void Card_IsOpaque()
    {
        using var card = new GlassCard();
        Assert.AreEqual(Theme.BgCard.ToArgb(), card.BackColor.ToArgb());
        Assert.AreEqual(255, card.BackColor.A);
    }

    [TestMethod]
    public void Card_PaintsFlatWithoutException()
    {
        using var card = new GlassCard { Width = 200, Height = 120 };
        using var bmp = new Bitmap(200, 120);
        card.DrawToBitmap(bmp, new Rectangle(0, 0, 200, 120));
        // The card body (inside the border) is the opaque card colour.
        Color centre = bmp.GetPixel(100, 60);
        Assert.AreEqual(255, centre.A);
        Assert.AreEqual(Theme.BgCard.ToArgb(), centre.ToArgb());
    }

    [TestMethod]
    public void GlassForm_BackgroundIsSolidBgMain()
    {
        using var form = new GlassForm();
        Assert.AreEqual(Theme.BgMain.ToArgb(), form.BackColor.ToArgb());
    }
}
