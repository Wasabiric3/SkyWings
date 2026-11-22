using System.Reflection;
using SkyWings.UI;

namespace SkyWings.Tests;

[TestClass]
[DoNotParallelize]
public sealed class ChoiceCardTests
{
    private static ChoiceCard Make(bool selected = false, bool enabled = true)
        => new() { Width = 220, Height = Gfx.S(new Control(), 118), Title = "Business", Subtitle = "PHP 105,000", Detail = "per traveller · 2 bags · meal", Selected = selected, Enabled = enabled };

    private static Bitmap Render(ChoiceCard card)
    {
        card.CreateControl();
        var bmp = new Bitmap(card.Width, card.Height);
        card.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
        return bmp;
    }

    // Inside the card's border, below the text.
    private static Point BottomInside(ChoiceCard card) => new(card.Width / 2, card.Height - Gfx.S(card, 9));

    [TestMethod]
    public void Selected_FillsBrandSoft_AndDrawsABrandTickCircle()
    {
        using var card = Make(selected: true);
        using Bitmap bmp = Render(card);
        Assert.AreEqual(Theme.BrandSoft.ToArgb(), bmp.GetPixel(BottomInside(card).X, BottomInside(card).Y).ToArgb());
        Rectangle tick = card.TickBounds;
        Assert.AreEqual(Theme.Brand.ToArgb(), bmp.GetPixel(tick.Left + 3, tick.Top + tick.Height / 2).ToArgb(), "the tick circle is brand green");
    }

    [TestMethod]
    public void Unselected_FillsWhite_AndShowsNoTickCircle()
    {
        using var card = Make(selected: false);
        using Bitmap bmp = Render(card);
        Assert.AreEqual(Theme.BgCard.ToArgb(), bmp.GetPixel(BottomInside(card).X, BottomInside(card).Y).ToArgb());
        Rectangle tick = card.TickBounds;
        Assert.AreNotEqual(Theme.Brand.ToArgb(), bmp.GetPixel(tick.Left + 3, tick.Top + tick.Height / 2).ToArgb());
    }

    [TestMethod]
    public void Disabled_FillsDisabledBack()
    {
        using var card = Make(enabled: false);
        using Bitmap bmp = Render(card);
        Assert.AreEqual(Theme.DisabledBack.ToArgb(), bmp.GetPixel(BottomInside(card).X, BottomInside(card).Y).ToArgb());
    }

    [TestMethod]
    public void SpaceAndEnter_RaiseClick()
    {
        using var card = Make();
        int clicks = 0;
        card.Click += (_, _) => clicks++;
        var keyUp = typeof(ChoiceCard).GetMethod("OnKeyUp", BindingFlags.Instance | BindingFlags.NonPublic)!;
        keyUp.Invoke(card, new object[] { new KeyEventArgs(Keys.Space) });
        keyUp.Invoke(card, new object[] { new KeyEventArgs(Keys.Enter) });
        Assert.AreEqual(2, clicks);
    }

    [TestMethod]
    public void TheCard_IsTallEnoughForTitlePriceAndDetail()
    {
        using var card = new ChoiceCard();
        Assert.IsGreaterThanOrEqualTo(Gfx.S(card, 110), card.Height);
    }

    private static int Measure(string text, Font font)
        => TextRenderer.MeasureText(text, font, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width;

    [TestMethod]
    public void ALongPrice_ShrinksToFitANarrowCard_InsteadOfBeingCutOff()
    {
        // The wizard's four cards are about 130 px wide at its minimum window width.
        foreach (string price in new[] { "PHP 168,000", "PHP 336,000", "PHP 1,234,567" })
        {
            using var card = new ChoiceCard { Width = 130, Subtitle = price };
            using Font fit = card.FitPriceFont();
            Assert.IsLessThanOrEqualTo(card.PriceTextWidth, Measure(price, fit), $"{price} fits the card's text area");
            Assert.IsGreaterThanOrEqualTo(8f, fit.Size);
        }
    }

    [TestMethod]
    public void AShortPrice_OnAWideCard_KeepsTheFullSize()
    {
        using var card = new ChoiceCard { Width = 260, Subtitle = "PHP 42,000" };
        using Font fit = card.FitPriceFont();
        Assert.AreEqual(Theme.Subtitle.Size, fit.Size);
    }

    [TestMethod]
    public void ANarrowCard_StillPaintsItsPrice_WithoutInkOutsideTheTextArea()
    {
        using var card = new ChoiceCard { Width = 130, Title = "First Class", Subtitle = "PHP 168,000", Detail = "3 bags · dining" };
        card.CreateControl();
        using var bmp = new Bitmap(card.Width, card.Height);
        card.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
        // Between the price line and the card's right padding there is no text ink (it would mean the price ran past its area).
        int pad = Gfx.S(card, 16);
        int y = 2 + Gfx.S(card, 14) + Gfx.S(card, 28) + Gfx.S(card, 16);   // the middle of the price line
        for (int x = card.Width - 3 - pad + 2; x < card.Width - 6; x++)
            Assert.AreEqual(Theme.BgCard.ToArgb(), bmp.GetPixel(x, y).ToArgb(), $"ink in the right padding at {x}");
    }
}
