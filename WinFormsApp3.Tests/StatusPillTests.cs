using SkyWings.UI;

namespace SkyWings.Tests;

[TestClass]
[DoNotParallelize]
public sealed class StatusPillTests
{
    private static Bitmap Render(StatusPill pill, Color surface)
    {
        var host = new Panel { Width = 260, Height = 58, BackColor = surface };
        pill.Dock = DockStyle.Right;
        pill.Width = 190;
        host.Controls.Add(pill);
        host.CreateControl();
        host.PerformLayout();
        var bmp = new Bitmap(host.Width, host.Height);
        host.DrawToBitmap(bmp, new Rectangle(Point.Empty, bmp.Size));
        return bmp;
    }

    [TestMethod]
    public void ThePill_IsFilledInTheStatusColour_AndRoundedAtTheEnds() => FormHarness.OnStaThread(() =>
    {
        Color back = Color.FromArgb(0xDA, 0xEC, 0xE1), fore = Color.FromArgb(0x31, 0x5B, 0x49);
        using Bitmap bmp = Render(new StatusPill { Text = "CONFIRMED", FillColor = back, TextColor = fore }, Theme.PageBg);
        int cx = 260 - 190 / 2;
        // just inside the pill's left end, vertically centred: the fill; the very corner of the control: the page colour
        int textWidth = TextRenderer.MeasureText("CONFIRMED", new Font(Theme.Small, FontStyle.Bold)).Width;
        int left = cx - (textWidth + 40) / 2;
        Assert.AreEqual(back.ToArgb(), bmp.GetPixel(left + 14, 29).ToArgb(), "the pill is filled");
        Assert.AreEqual(Theme.PageBg.ToArgb(), bmp.GetPixel(left + 1, 15 - 9).ToArgb(), "its corner is round, so the page shows");
        Assert.AreEqual(Theme.PageBg.ToArgb(), bmp.GetPixel(259, 0).ToArgb(), "outside the pill is the page colour, not a tinted block");
    });

    [TestMethod]
    public void AccessibleName_IsTheText() => FormHarness.OnStaThread(() =>
    {
        var pill = new StatusPill { Text = "UPCOMING" };
        Assert.AreEqual("UPCOMING", pill.AccessibleName);
    });

    [TestMethod]
    public void BookingDetail_ShowsTheTripStatusAsAPill() => FormHarness.OnStaThread(() =>
    {
        using var harness = new FormHarness();
        (_, Func<Form> create) = harness.BookingForms().First(f => f.Name == "booking-detail");
        using Form form = create();
        form.Show();
        Application.DoEvents();
        StatusPill pill = Enumerable.Range(0, 1).SelectMany(_ => AllControls(form)).OfType<StatusPill>().Single();
        Assert.AreEqual("UPCOMING", pill.Text);
        Assert.IsEmpty(FormHarness.Clipped(form));
    });

    private static IEnumerable<Control> AllControls(Control root)
    {
        foreach (Control c in root.Controls)
        {
            yield return c;
            foreach (Control inner in AllControls(c)) yield return inner;
        }
    }
}
