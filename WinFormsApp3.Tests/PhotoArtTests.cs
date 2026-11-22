using SkyWings.UI;

namespace SkyWings.Tests;

/// <summary>The home banner and the four region cards show real photographs (cropped to fit), with the old flat art as a fallback.</summary>
[TestClass]
[DoNotParallelize]
public sealed class PhotoArtTests
{
    private static IEnumerable<Control> All(Control root)
    {
        foreach (Control c in root.Controls)
        {
            yield return c;
            foreach (Control inner in All(c)) yield return inner;
        }
    }

    [TestMethod]
    [DataRow("hero", 1.7f, 1.9f)]
    [DataRow("domestic", 1.9f, 2.1f)]
    [DataRow("southeast_asia", 1.7f, 1.85f)]
    [DataRow("europe", 1.8f, 1.95f)]
    [DataRow("north_america", 1.4f, 1.6f)]
    public void ThePhotos_AreEmbedded_WithTheirOwnProportions(string key, float minRatio, float maxRatio)
    {
        Bitmap? photo = Assets.Photo(key);
        Assert.IsNotNull(photo, key);
        float ratio = photo.Width / (float)photo.Height;
        Assert.IsTrue(ratio >= minRatio && ratio <= maxRatio, $"{key} is {photo.Width}x{photo.Height}");
        Assert.AreSame(photo, Assets.Photo(key), "one shared bitmap per photo");
    }

    [TestMethod]
    public void AnUnknownPhoto_IsNull_SoAScreenStillOpens() => Assert.IsNull(Assets.Photo("atlantis"));

    [TestMethod]
    [DataRow("Domestic", "domestic")]
    [DataRow("Southeast Asia", "southeast_asia")]
    [DataRow("Europe", "europe")]
    [DataRow("North America", "north_america")]
    public void EachRegion_UsesThePhotoWithItsName(string title, string key) => Assert.AreEqual(key, DestinationCard.PhotoKeyFor(title));

    [TestMethod]
    public void AnUnknownRegion_HasNoPhoto() => Assert.IsNull(DestinationCard.PhotoKeyFor("Antarctica"));

    [TestMethod]
    public void CoverSource_HasTheTargetsShape_StaysInsideTheImage_AndFollowsTheFocalPoint()
    {
        var image = new Size(2048, 1143);
        var target = new Size(524, 229);
        RectangleF mid = BrandArt.CoverSource(image, target, new PointF(0.5f, 0.45f));
        Assert.AreEqual(target.Width / (float)target.Height, mid.Width / mid.Height, 0.01f);
        Assert.AreEqual(2048f, mid.Width, 0.5f, "a wide target keeps the full width");
        Assert.IsTrue(mid.Top >= 0 && mid.Bottom <= 1143.01f);
        Assert.AreEqual(0.45f * 1143, mid.Top + mid.Height / 2, 1f, "centred on the focal point");

        Assert.AreEqual(0f, BrandArt.CoverSource(image, target, new PointF(0.5f, 0f)).Top, 0.01f, "the top edge clamps");
        Assert.AreEqual(1143f, BrandArt.CoverSource(image, target, new PointF(0.5f, 1f)).Bottom, 0.5f, "the bottom edge clamps");

        RectangleF tall = BrandArt.CoverSource(new Size(1000, 500), new Size(100, 200), new PointF(0.25f, 0.5f));
        Assert.AreEqual(500f, tall.Height, 0.5f, "a tall target keeps the full height");
        Assert.AreEqual(0.25f * 1000, tall.Left + tall.Width / 2, 1f);
    }

    private static int Colours(Bitmap bmp, Rectangle area)
    {
        var set = new HashSet<int>();
        for (int y = area.Top; y < area.Bottom; y += 3)
            for (int x = area.Left; x < area.Right; x += 3) set.Add(bmp.GetPixel(x, y).ToArgb() & 0xF8F8F8);   // ignore tiny noise
        return set.Count;
    }

    [TestMethod]
    public void ARegionCard_ShowsItsPhoto_AndAnUnknownOneKeepsTheFlatColour() => FormHarness.OnStaThread(() =>
    {
        foreach ((string title, bool photo) in new[] { ("Europe", true), ("Domestic", true), ("North America", true), ("Southeast Asia", true), ("Antarctica", false) })
        {
            using var host = new Panel { Width = 300, Height = 246, BackColor = Theme.PageBg };
            var card = new DestinationCard { Width = 268, Height = 212, Title = title, Subtitle = "s", Meta = "m", Accent = Color.FromArgb(120, 140, 176), Location = new Point(10, 10) };
            host.Controls.Add(card);
            host.CreateControl();
            using var bmp = new Bitmap(host.Width, host.Height);
            host.DrawToBitmap(bmp, new Rectangle(Point.Empty, bmp.Size));
            var art = new Rectangle(30, 30, 230, 50);   // inside the picture band, away from the pin icon
            int colours = Colours(bmp, art);
            if (photo) Assert.IsGreaterThan(40, colours, $"{title} shows a photograph");
            else Assert.IsLessThan(8, Colours(bmp, new Rectangle(30, 30, 80, 20)), "no photo: the flat accent band");
        }
    });

    [TestMethod]
    public void TheHomeBanner_ShowsThePlanePhoto_WithItsDarkGreenTail() => FormHarness.OnStaThread(() =>
    {
        using var harness = new FormHarness();
        harness.Store.SetSetting(BookingStore.SettingDemoActivity, "0");
        using var form = new HomeForm(harness.Store, harness.Session) { StartPosition = FormStartPosition.Manual, Location = new Point(20, 20) };
        form.Show();
        Application.DoEvents();
        Control hero = All(form).First(c => c.GetType().Name == "HeroArt");
        using var bmp = new Bitmap(hero.Width, hero.Height);
        hero.DrawToBitmap(bmp, new Rectangle(Point.Empty, bmp.Size));
        int darkGreen = 0, blueSky = 0;
        for (int y = 0; y < bmp.Height; y += 2)
            for (int x = 0; x < bmp.Width; x += 2)
            {
                Color c = bmp.GetPixel(x, y);
                if (c.R < 70 && c.G > c.R + 15 && c.G < 130 && c.B < 100) darkGreen++;
                if (c.B > 150 && c.B > c.R + 40) blueSky++;
            }
        Assert.IsGreaterThan(150, darkGreen, "the SkyWings livery (tail and engine) is visible");
        Assert.IsGreaterThan(500, blueSky, "the blue sky of the photograph is visible");
    });

    [TestMethod]
    public void SaveRegionCardsImage_ForReview() => FormHarness.OnStaThread(() =>
    {
        using var host = new Panel { Width = 4 * 278 + 20, Height = 236, BackColor = Theme.PageBg };
        string[][] regions =
        {
            new[] { "Domestic", "Island time, closer to home.", "5 places · 15 flights · from PHP 2,950" },
            new[] { "Southeast Asia", "New flavors. New perspectives.", "5 places · 15 flights · from PHP 6,250" },
            new[] { "Europe", "A little art. A lot of discovery.", "5 places · 5 flights · from PHP 41,000" },
            new[] { "North America", "Big cities. Bigger possibilities.", "5 places · 5 flights · from PHP 49,000" }
        };
        for (int i = 0; i < regions.Length; i++)
            host.Controls.Add(new DestinationCard { Width = 268, Height = 212, Location = new Point(10 + i * 278, 12), Title = regions[i][0], Subtitle = regions[i][1], Meta = regions[i][2] });
        host.CreateControl();
        using var bmp = new Bitmap(host.Width, host.Height);
        host.DrawToBitmap(bmp, new Rectangle(Point.Empty, bmp.Size));
        Directory.CreateDirectory(FormHarness.Folder);
        bmp.Save(Path.Combine(FormHarness.Folder, "region-cards.png"));
        Assert.IsGreaterThan(0, bmp.Width);
    });
}
