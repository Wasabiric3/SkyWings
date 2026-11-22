using SkyWings.Controllers;
using SkyWings.UI;

namespace SkyWings.Tests;

/// <summary>The panel beside the home board: the selected flight's status, timeline, cabins and its one booking button.</summary>
[TestClass]
[DoNotParallelize]
public sealed class FlightDetailsPanelTests
{
    private static (FlightDetails Details, BookingAction Action, HomeController Controller) Sample(FormHarness harness, string flightNo = "PR-101", int cabin = 0)
    {
        var controller = new HomeController(harness.Store, harness.Session);
        Flight flight = harness.Store.LoadActiveFlights(true).Concat(harness.Store.LoadActiveFlights(false)).First(f => f.FlightNo == flightNo);
        DateTime day = DateTime.Today.AddDays(5);
        FlightDetails details = controller.Describe(flight, day, day.AddDays(-5));
        return (details, controller.ActionFor(flight, day, details.Cabins[cabin], day.AddDays(-5)), controller);
    }

    private static FlightDetailsPanel Open(Form host, int width = 360, int height = 560)
    {
        var panel = new FlightDetailsPanel { Location = new Point(10, 10), Size = new Size(width, height) };
        host.Controls.Add(panel);
        host.Show();
        Application.DoEvents();
        return panel;
    }

    private static Form Host() => new() { Width = 420, Height = 640, StartPosition = FormStartPosition.Manual, Location = new Point(20, 20), BackColor = Theme.PageBg };

    [TestMethod]
    public void Empty_ShowsNoFlightSelected_AndADisabledButton() => FormHarness.OnStaThread(() =>
    {
        using Form host = Host();
        FlightDetailsPanel panel = Open(host);
        panel.ShowFlight(null, 0, new BookingAction(BookAction.None, "Book →", "Choose a flight."));
        Assert.IsFalse(panel.ActionButton.Enabled);
        Assert.AreEqual("Book →", panel.ActionButton.Text);
        Assert.AreEqual("No flight selected", panel.AccessibleDescription);
    });

    [TestMethod]
    public void ShowFlight_FillsTheHeaderTimelineAndCabins() => FormHarness.OnStaThread(() =>
    {
        using var harness = new FormHarness();
        var (details, action, _) = Sample(harness);
        using Form host = Host();
        FlightDetailsPanel panel = Open(host);
        panel.ShowFlight(details, details.DefaultCabin, action);
        Assert.AreEqual(action.Label, panel.ActionButton.Text);
        Assert.IsTrue(panel.ActionButton.Enabled);
        Assert.AreEqual(details.DefaultCabin, panel.SelectedCabin);
        StringAssert.Contains(panel.AccessibleDescription, "PR-101");
        using Bitmap bmp = FormHarness.Snapshot(host);
        Assert.IsTrue(FormHarness.IsDrawn(bmp));
        Directory.CreateDirectory(FormHarness.Folder);
        bmp.Save(Path.Combine(FormHarness.Folder, "flight-details-panel.png"));
    });

    [TestMethod]
    public void ClickingACabinRow_RaisesCabinChosen_WithItsIndex() => FormHarness.OnStaThread(() =>
    {
        using var harness = new FormHarness();
        var (details, action, _) = Sample(harness);
        using Form host = Host();
        FlightDetailsPanel panel = Open(host);
        panel.ShowFlight(details, 0, action);
        var chosen = new List<int>();
        panel.CabinChosen += (_, index) => chosen.Add(index);
        Rectangle row = panel.CabinRowBounds(1);
        Assert.IsGreaterThan(0, row.Height);
        panel.ClickAt(new Point(row.X + row.Width / 2, row.Y + row.Height / 2));
        CollectionAssert.AreEqual(new[] { 1 }, chosen);
        panel.ClickAt(new Point(row.X + 4, row.Y + 4));
        CollectionAssert.AreEqual(new[] { 1, 1 }, chosen, "clicking the chosen row again is allowed and reports it");
    });

    [TestMethod]
    public void TheArrowKeys_MoveTheChosenCabin() => FormHarness.OnStaThread(() =>
    {
        using var harness = new FormHarness();
        var (details, action, _) = Sample(harness);
        using Form host = Host();
        FlightDetailsPanel panel = Open(host);
        panel.ShowFlight(details, 0, action);
        var chosen = new List<int>();
        panel.CabinChosen += (_, index) => chosen.Add(index);
        panel.PressKey(Keys.Down);
        panel.PressKey(Keys.Up);
        panel.PressKey(Keys.Up);   // already at the top: nothing happens
        CollectionAssert.AreEqual(new[] { 1, 0 }, chosen);
    });

    [TestMethod]
    public void TheActionButton_IsDisabled_WhenTheActionIsNone_AndTheReasonIsShown() => FormHarness.OnStaThread(() =>
    {
        using var harness = new FormHarness();
        var (details, _, _) = Sample(harness);
        using Form host = Host();
        FlightDetailsPanel panel = Open(host);
        panel.ShowFlight(details, 0, new BookingAction(BookAction.None, "Book PR-101 · Economy →", "This flight has already left."));
        Assert.IsFalse(panel.ActionButton.Enabled);
        Assert.AreEqual("This flight has already left.", panel.Reason);
    });

    [TestMethod]
    public void ActionClicked_IsRaised_ByTheButton() => FormHarness.OnStaThread(() =>
    {
        using var harness = new FormHarness();
        var (details, action, _) = Sample(harness);
        using Form host = Host();
        FlightDetailsPanel panel = Open(host);
        panel.ShowFlight(details, 0, action);
        int clicks = 0;
        panel.ActionClicked += (_, _) => clicks++;
        panel.ActionButton.PerformClick();
        Assert.AreEqual(1, clicks);
    });

    [TestMethod]
    public void ShowFlight_WithTheSameContent_DoesNotRedraw() => FormHarness.OnStaThread(() =>
    {
        using var harness = new FormHarness();
        var (details, action, _) = Sample(harness);
        using Form host = Host();
        FlightDetailsPanel panel = Open(host);
        panel.ShowFlight(details, 0, action);
        Application.DoEvents();
        int paints = 0;
        panel.Paint += (_, _) => paints++;
        panel.ShowFlight(details with { }, 0, action with { });
        Application.DoEvents();
        Assert.AreEqual(0, paints, "identical content must not repaint (the board refreshes every second)");
        panel.ShowFlight(details, 1, action);
        Application.DoEvents();
        Assert.IsGreaterThan(0, paints, "a different chosen cabin repaints");
    });

    [TestMethod]
    public void ACabinThatIsSoldOut_ShowsSoldOut_AndAnOpenOneShowsSeatsLeft() => FormHarness.OnStaThread(() =>
    {
        using var harness = new FormHarness();
        var (details, action, _) = Sample(harness);
        var soldOut = details with { Cabins = new[] { details.Cabins[0] with { SeatsLeft = 0, LoadPercent = 100 }, details.Cabins[1] } };
        using Form host = Host();
        FlightDetailsPanel panel = Open(host);
        panel.ShowFlight(soldOut, 1, action);
        Assert.AreEqual("Sold out", panel.CabinSeatsText(0));
        Assert.AreEqual($"{details.Cabins[1].SeatsLeft:N0} left", panel.CabinSeatsText(1));
    });

    [TestMethod]
    public void AtItsNarrowestWidth_NothingIsClipped_ForALongRoute() => FormHarness.OnStaThread(() =>
    {
        using var harness = new FormHarness();
        var (details, action, _) = Sample(harness, AppData.EuropeFlights.First().FlightNo);
        using Form host = new() { Width = 360, Height = 640, StartPosition = FormStartPosition.Manual, Location = new Point(20, 20), BackColor = Theme.PageBg };
        FlightDetailsPanel panel = Open(host, 300, 600);
        panel.ShowFlight(details, 0, action);
        Assert.IsEmpty(FormHarness.Clipped(host));
        Assert.IsLessThanOrEqualTo(panel.Height, panel.ActionButton.Bottom, "the button is inside the panel");
        Assert.IsGreaterThanOrEqualTo(panel.CabinRowBounds(details.Cabins.Count - 1).Bottom, panel.ActionButton.Top - 1, "the cabins end above the button");
    });
}
