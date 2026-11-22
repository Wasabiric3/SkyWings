using SkyWings.UI;

namespace SkyWings.Tests;

/// <summary>The four cabin cards on the wizard's class step show a photograph of their cabin above the name and price.</summary>
[TestClass]
[DoNotParallelize]
public sealed class CabinPhotoTests
{
    [TestMethod]
    [DataRow("economy")]
    [DataRow("premium_economy")]
    [DataRow("business")]
    [DataRow("first_class")]
    public void TheCabinPhotos_AreEmbedded(string key)
    {
        Bitmap? photo = Assets.Photo(key);
        Assert.IsNotNull(photo, key);
        Assert.IsGreaterThan(photo.Height, photo.Width, key + " is landscape");
    }

    [TestMethod]
    [DataRow("Economy", "economy")]
    [DataRow("Premium Economy", "premium_economy")]
    [DataRow("Business", "business")]
    [DataRow("First Class", "first_class")]
    public void EachCabin_UsesThePhotoWithItsName(string className, string key) => Assert.AreEqual(key, ChoiceCard.PhotoKeyFor(className));

    [TestMethod]
    public void EveryCabinTheAppOffers_HasAPhoto()
    {
        foreach (string className in AppData.ClassNames)
            Assert.IsNotNull(ChoiceCard.PhotoKeyFor(className), className + " has no photo");
    }

    [TestMethod]
    public void AnUnknownCabin_HasNoPhoto() => Assert.IsNull(ChoiceCard.PhotoKeyFor("Space"));

    private static Bitmap Render(ChoiceCard card)
    {
        var host = new Panel { Width = card.Width + 20, Height = card.Height + 20, BackColor = Theme.PageBg };
        card.Location = new Point(10, 10);
        host.Controls.Add(card);
        host.CreateControl();
        var bmp = new Bitmap(host.Width, host.Height);
        host.DrawToBitmap(bmp, new Rectangle(Point.Empty, bmp.Size));
        return bmp;
    }

    private static int Colours(Bitmap bmp, Rectangle area)
    {
        var set = new HashSet<int>();
        for (int y = area.Top; y < area.Bottom; y += 2)
            for (int x = area.Left; x < area.Right; x += 2) set.Add(bmp.GetPixel(x, y).ToArgb() & 0xF8F8F8);
        return set.Count;
    }

    [TestMethod]
    public void ACardWithAPhoto_ShowsItAtTheTop_AndPushesTheTextBelowIt() => FormHarness.OnStaThread(() =>
    {
        using var plain = new ChoiceCard { Width = 200, Height = 120, Title = "Business", Subtitle = "PHP 21,000", Detail = "Lie-flat seat" };
        using var withPhoto = new ChoiceCard { Width = 200, Height = 204, Title = "Business", Subtitle = "PHP 21,000", Detail = "Lie-flat seat", PhotoKey = "business" };
        Assert.AreEqual(0, plain.PhotoOffset, "no photo, no offset");
        Assert.IsGreaterThan(40, withPhoto.PhotoOffset);

        using Bitmap bmp = Render(withPhoto);
        Assert.IsGreaterThan(60, Colours(bmp, new Rectangle(24, 24, 160, withPhoto.PhotoOffset - 20)), "the photograph is drawn in the top band");
        // below the photo is the plain card with its text: few colours, and the card's bottom is plain white
        Assert.IsLessThan(25, Colours(bmp, new Rectangle(24, 10 + withPhoto.Height - 12, 150, 6)), "the bottom edge is plain card");
    });

    [TestMethod]
    public void TheTickOfASelectedCard_SitsOnThePhoto_WhenThereIsOne() => FormHarness.OnStaThread(() =>
    {
        using var plain = new ChoiceCard { Width = 200, Height = 120, Selected = true };
        using var withPhoto = new ChoiceCard { Width = 200, Height = 204, Selected = true, PhotoKey = "economy" };
        Assert.IsLessThan(withPhoto.PhotoOffset, withPhoto.TickBounds.Bottom, "the tick is inside the photo band");
        Assert.AreEqual(2 + Gfx.S(plain, 14), plain.TickBounds.Top, "a card without a photo keeps its tick where it was");
    });

    [TestMethod]
    public void ACabinThatIsNotOffered_ShowsItsPhotoWashedOut() => FormHarness.OnStaThread(() =>
    {
        using var on = new ChoiceCard { Width = 200, Height = 204, Title = "First Class", PhotoKey = "first_class" };
        using var off = new ChoiceCard { Width = 200, Height = 204, Title = "First Class", PhotoKey = "first_class", Enabled = false };
        using Bitmap a = Render(on), b = Render(off);
        long Sum(Bitmap bmp)
        {
            long total = 0;
            for (int y = 24; y < 24 + on.PhotoOffset - 20; y += 4) for (int x = 24; x < 184; x += 4) { Color c = bmp.GetPixel(x, y); total += c.R + c.G + c.B; }
            return total;
        }
        Assert.IsGreaterThan(Sum(a), Sum(b), "the unavailable cabin is lighter (washed out)");
    });

    [TestMethod]
    public void TheWizardsClassStep_GivesEachCabinCardItsOwnPhoto() => WizardHarness.OnStaThread(() =>
    {
        using var wizard = new WizardHarness();
        var cards = wizard.Field<ChoiceCard[]>("_classCards");
        CollectionAssert.AreEqual(new[] { "economy", "premium_economy", "business", "first_class" }, cards.Select(c => c.PhotoKey).ToArray());
        wizard.GoTo(2);
        Assert.IsTrue(cards.All(c => c.Visible && c.Height > 150), "the cards are tall enough for their photos");
    });

    [TestMethod]
    public void OnANarrowCard_TheCabinNameIsNotCutShort_WhenThereIsAPhoto() => FormHarness.OnStaThread(() =>
    {
        // At the minimum wizard width a card is about 120px wide. "PREMIUM ECONOMY" cannot fit, but "BUSINESS" and "ECONOMY" can.
        using var photo = new ChoiceCard { Width = 120, Height = 204, Title = "Economy", Subtitle = "PHP 84,000", PhotoKey = "economy", Selected = true };
        using var plain = new ChoiceCard { Width = 120, Height = 120, Title = "Economy", Subtitle = "PHP 84,000", Selected = true };
        using Bitmap withPhoto = Render(photo), without = Render(plain);
        int Ink(Bitmap bmp, int top)   // dark title pixels in the title row, right half (where "...") would end the word
        {
            int n = 0;
            for (int y = top; y < top + 18; y++) for (int x = 70; x < 112; x++) { Color c = bmp.GetPixel(x, y); if (c.R < 90 && c.G < 120) n++; }
            return n;
        }
        Assert.IsGreaterThan(5, Ink(withPhoto, 10 + 14 + photo.PhotoOffset), "the whole word is drawn, up to the card's right side");
    });
}
