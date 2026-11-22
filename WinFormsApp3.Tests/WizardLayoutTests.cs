using SkyWings.UI;

namespace SkyWings.Tests;

/// <summary>The wizard frame: header, titles, footer buttons, the summary card and the window size.</summary>
[TestClass]
[DoNotParallelize]
public sealed class WizardLayoutTests
{
    [TestMethod]
    public void EveryStep_FooterAndSummary_FitAtMinimumSize() => WizardHarness.OnStaThread(() =>
    {
        using var wizard = new WizardHarness();
        wizard.Form.Size = wizard.Form.MinimumSize;
        Application.DoEvents();
        Rectangle client = wizard.Form.ClientRectangle;
        var pageHost = wizard.Field<Panel>("_pageHost");
        for (int step = 0; step <= 5; step++)
        {
            wizard.GoTo(step);
            foreach (string name in new[] { "_back", "_next", "_print" })
            {
                GlassButton button = wizard.Field<GlassButton>(name);
                if (!button.Visible) continue;
                Rectangle r = wizard.InForm(button);
                Assert.IsTrue(client.Contains(r), $"step {step}: {button.Text} is inside the window ({r} in {client})");
            }
            Rectangle price = wizard.InForm(wizard.Field<PriceSummaryPanel>("_price"));
            Assert.IsTrue(client.Contains(price), $"step {step}: the summary is inside the window");
            Assert.IsFalse(price.IntersectsWith(wizard.InForm(pageHost)), $"step {step}: the summary does not overlap the page");
        }
    });

    [TestMethod]
    public void Header_ShowsTheLogo_AndSixPills() => WizardHarness.OnStaThread(() =>
    {
        using var wizard = new WizardHarness();
        var bar = wizard.Field<StepBar>("_stepBar");
        Assert.IsTrue(bar.ShowLogo);
        Assert.AreEqual(6, Enumerable.Range(0, 6).Count(i => bar.PillBounds(i).Width > 0));
    });

    [TestMethod]
    public void FooterTexts_FollowTheFigma() => WizardHarness.OnStaThread(() =>
    {
        using var wizard = new WizardHarness();
        var back = wizard.Field<GlassButton>("_back");
        var next = wizard.Field<GlassButton>("_next");
        var print = wizard.Field<GlassButton>("_print");
        string[] nextText = { "Continue to Passengers →", "Continue to Class & extras →", "Continue to Seats →", "Continue to Payment →", "Confirm and pay →", "Done →" };
        string[] backText = { "← Back", "← Back to Flight", "← Back to Passengers", "← Back to Class & extras", "← Back to Seats", "" };
        for (int step = 0; step <= 5; step++)
        {
            wizard.GoTo(step);
            Assert.AreEqual(nextText[step], next.Text, $"step {step} next");
            if (step < 5) { Assert.IsTrue(back.Visible); Assert.AreEqual(backText[step], back.Text, $"step {step} back"); }
            else { Assert.IsFalse(back.Visible); Assert.IsTrue(print.Visible); Assert.AreEqual("Print receipt", print.Text); }
        }
    });

    [TestMethod]
    public void BackOnTheFirstStep_ClosesTheWizard() => WizardHarness.OnStaThread(() =>
    {
        using var wizard = new WizardHarness();
        var back = wizard.Field<GlassButton>("_back");
        Assert.IsTrue(back.Visible);
        back.PerformClick();
        Application.DoEvents();
        Assert.IsTrue(wizard.Form.IsDisposed, "closing a shown, non-modal form disposes it");
    });

    [TestMethod]
    public void TitlesAndHints_AreTheFigmaWording() => WizardHarness.OnStaThread(() =>
    {
        using var wizard = new WizardHarness();
        var title = wizard.Field<Label>("_pageTitle");
        var hint = wizard.Field<Label>("_pageHint");
        wizard.GoTo(0);
        Assert.AreEqual(Theme.Brand.ToArgb(), title.ForeColor.ToArgb(), "the page title is brand green (Theme.Apply must not reset it)");
        Assert.AreEqual("Where are you flying?", title.Text);
        Assert.AreEqual("Choose your destination, travel date, and preferred flight.", hint.Text);
        wizard.GoTo(5);
        Assert.AreEqual("You're all set!", title.Text);
    });

    [TestMethod]
    public void Summary_ShowsRealFares_NotSampleNumbers() => WizardHarness.OnStaThread(() =>
    {
        using var wizard = new WizardHarness();
        wizard.AddTraveller();
        var session = wizard.Controller.Session;
        IReadOnlyList<PriceLine> lines = wizard.Field<PriceSummaryPanel>("_price").Lines;
        Assert.IsTrue(lines.Any(l => l.Kind == PriceLineKind.Hero && l.Label == session.SelectedFlight!.FlightNo), "the flight is the hero line");
        Assert.IsTrue(lines.Any(l => l.Kind == PriceLineKind.Stat && l.Label == "Cabin" && l.Value == session.ClassName));
        Assert.IsTrue(lines.Any(l => l.Kind == PriceLineKind.Stat && l.Label == "Travellers" && l.Value == "1 traveller"));
        PriceLine total = lines.Single(l => l.Kind == PriceLineKind.Total);
        Assert.AreEqual($"PHP {session.Quote.Total:N2}", total.Value);
    });

    [TestMethod]
    public void TheWindow_OpensLarge_ButNeverBelowTheMinimum() => WizardHarness.OnStaThread(() =>
    {
        using var wizard = new WizardHarness();
        Assert.IsGreaterThanOrEqualTo(wizard.Form.MinimumSize.Width, wizard.Form.Width);
        Assert.IsGreaterThanOrEqualTo(wizard.Form.MinimumSize.Height, wizard.Form.Height);
        Rectangle area = Screen.PrimaryScreen!.WorkingArea;
        Assert.AreEqual(Math.Min(Gfx.S(wizard.Form, 1280), area.Width - 16), wizard.Form.Width, "1280 wide (scaled), or the screen if smaller");
        Assert.AreEqual(Math.Min(Gfx.S(wizard.Form, 800), area.Height - 16), wizard.Form.Height);
    });
}
