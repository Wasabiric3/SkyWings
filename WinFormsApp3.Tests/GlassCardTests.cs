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
    public void Card_WithHeading_DisplayRectangleStartsLowerBy30Scaled()
    {
        using var plain = new GlassCard { Width = 400, Height = 300 };
        using var titled = new GlassCard { Width = 400, Height = 300, Heading = "Booking summary" };
        Assert.AreEqual(Gfx.S(titled, 30), titled.DisplayRectangle.Y - plain.DisplayRectangle.Y);
        Assert.AreEqual(plain.DisplayRectangle.Height - Gfx.S(titled, 30), titled.DisplayRectangle.Height);
    }

    [TestMethod]
    public void Card_WithoutHeading_DisplayRectangleUnchanged()
    {
        using var card = new GlassCard { Width = 400, Height = 300, Heading = "" };
        int inset = Gfx.S(card, 10) + Gfx.S(card, 24);
        Assert.AreEqual(new Rectangle(inset, inset, 400 - 2 * inset, 300 - 2 * inset), card.DisplayRectangle);
    }

    [TestMethod]
    public void Card_WithHeading_PaintsHeadingTextInBrandGreen()
    {
        using var card = new GlassCard { Width = 300, Height = 120, Heading = "PAYMENT DETAILS" };
        using var bmp = new Bitmap(300, 120);
        card.DrawToBitmap(bmp, new Rectangle(0, 0, 300, 120));
        bool found = false;
        int top = card.DisplayRectangle.Y - Gfx.S(card, 30);
        for (int y = top; y < top + Gfx.S(card, 30) && !found; y++)
            for (int x = card.DisplayRectangle.X; x < card.DisplayRectangle.X + 160; x++)
                if (bmp.GetPixel(x, y).ToArgb() == Theme.Brand.ToArgb()) { found = true; break; }
        Assert.IsTrue(found, "the heading is drawn with Theme.Brand");
    }

    [TestMethod]
    public void GlassForm_BackgroundIsSolidBgMain()
    {
        using var form = new GlassForm();
        Assert.AreEqual(Theme.BgMain.ToArgb(), form.BackColor.ToArgb());
    }
}
