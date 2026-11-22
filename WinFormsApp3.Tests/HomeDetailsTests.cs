using System.Reflection;
using SkyWings.Controllers;
using SkyWings.UI;

namespace SkyWings.Tests;

/// <summary>The home screen with its details panel: selecting a flight, choosing a cabin and the one booking button.</summary>
[TestClass]
[DoNotParallelize]
public sealed class HomeDetailsTests
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private static IEnumerable<Control> All(Control root)
    {
        foreach (Control c in root.Controls)
        {
            yield return c;
            foreach (Control inner in All(c)) yield return inner;
        }
    }

    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, Any)!.GetValue(owner)!;

    /// <summary>Opens Home on tomorrow, when every flight is still ahead (so every flight can be booked whatever the time of day).</summary>
    private static HomeForm OpenOnTomorrow(FormHarness harness)
    {
        harness.Store.SetSetting(BookingStore.SettingDemoActivity, "0");
        harness.Store.SetFlightAircraft("PR-101", "A320");   // Economy and Business
        var form = new HomeForm(harness.Store, harness.Session) { StartPosition = FormStartPosition.Manual, Location = new Point(20, 20), ShowInTaskbar = false };
        form.Show();
        Application.DoEvents();
        Field<GlassButton>(form, "_dayNext").PerformClick();
        Application.DoEvents();
        return form;
    }

    private static DataGridView Board(HomeForm form) => Field<DataGridView>(form, "_flights");
    private static FlightDetailsPanel Panel(HomeForm form) => Field<FlightDetailsPanel>(form, "_detailsPanel");

    private static void Select(HomeForm form, string flightNo)
    {
        DataGridView grid = Board(form);
        int index = Enumerable.Range(0, grid.Rows.Count).First(i => ((FlightRow)grid.Rows[i].DataBoundItem!).FlightNo == flightNo);
        grid.CurrentCell = grid[0, index];
        Application.DoEvents();
    }

    [TestMethod]
    public void SelectingARow_FillsThePanel_WithThatFlight() => FormHarness.OnStaThread(() =>
    {
        using var harness = new FormHarness();
        using HomeForm form = OpenOnTomorrow(harness);
        Select(form, "PR-304");
        StringAssert.Contains(Panel(form).AccessibleDescription, "PR-304");
        Select(form, "PR-101");
        StringAssert.Contains(Panel(form).AccessibleDescription, "PR-101");
    });

    [TestMethod]
    public void TheFirstCabinWithSeats_IsChosenAtFirst_AndTheButtonNamesTheFlightAndCabin() => FormHarness.OnStaThread(() =>
    {
        using var harness = new FormHarness();
        using HomeForm form = OpenOnTomorrow(harness);
        Select(form, "PR-101");
        Assert.AreEqual(0, Panel(form).SelectedCabin, "Economy has seats");
        Assert.AreEqual("Book PR-101 · Economy →", Panel(form).ActionButton.Text);
        Assert.IsTrue(Panel(form).ActionButton.Enabled);
    });

    [TestMethod]
    public void ChoosingACabin_ChangesTheButtonLabel() => FormHarness.OnStaThread(() =>
    {
        using var harness = new FormHarness();
        using HomeForm form = OpenOnTomorrow(harness);
        Select(form, "PR-101");
        Rectangle row = Panel(form).CabinRowBounds(1);
        Panel(form).ClickAt(new Point(row.X + 10, row.Y + 10));
        Application.DoEvents();
        Assert.AreEqual(1, Panel(form).SelectedCabin);
        StringAssert.Contains(Panel(form).ActionButton.Text, "Business");
    });

    [TestMethod]
    public void ASoldOutCabin_TurnsTheButtonIntoJoinWaitlist() => FormHarness.OnStaThread(() =>
    {
        using var harness = new FormHarness();
        harness.Store.SetFlightAircraft("PR-101", "A320");
        harness.SellOut("PR-101", DateTime.Today.AddDays(1), "Economy");
        using HomeForm form = OpenOnTomorrow(harness);
        Select(form, "PR-101");
        Assert.AreEqual(1, Panel(form).SelectedCabin, "Economy is sold out, so Business is chosen at first");
        Rectangle economy = Panel(form).CabinRowBounds(0);
        Panel(form).ClickAt(new Point(economy.X + 10, economy.Y + 10));
        Application.DoEvents();
        Assert.AreEqual("Join waitlist →", Panel(form).ActionButton.Text);
        Assert.IsTrue(Panel(form).ActionButton.Enabled);
    });

    [TestMethod]
    public void TheButton_StartsTheBooking_WithTheFlightDateAndCabin() => FormHarness.OnStaThread(() =>
    {
        using var harness = new FormHarness();
        using HomeForm form = OpenOnTomorrow(harness);
        var calls = new List<(BookAction Kind, string Flight, DateTime Day, string Cabin)>();
        typeof(HomeForm).GetField("StartHook", Any)!.SetValue(form, new Action<BookAction, string, DateTime, string>((k, f, d, c) => calls.Add((k, f, d, c))));
        Select(form, "PR-101");
        Rectangle business = Panel(form).CabinRowBounds(1);
        Panel(form).ClickAt(new Point(business.X + 10, business.Y + 10));
        Panel(form).ActionButton.PerformClick();
        Assert.HasCount(1, calls);
        Assert.AreEqual((BookAction.Book, "PR-101", DateTime.Today.AddDays(1), "Business"), calls[0]);
    });

    [TestMethod]
    public void TheButton_JoinsTheWaitlist_ForASoldOutCabin() => FormHarness.OnStaThread(() =>
    {
        using var harness = new FormHarness();
        harness.Store.SetFlightAircraft("PR-101", "A320");
        harness.SellOut("PR-101", DateTime.Today.AddDays(1), "Economy");
        using HomeForm form = OpenOnTomorrow(harness);
        var calls = new List<(BookAction Kind, string Flight, DateTime Day, string Cabin)>();
        typeof(HomeForm).GetField("StartHook", Any)!.SetValue(form, new Action<BookAction, string, DateTime, string>((k, f, d, c) => calls.Add((k, f, d, c))));
        Select(form, "PR-101");
        Rectangle economy = Panel(form).CabinRowBounds(0);
        Panel(form).ClickAt(new Point(economy.X + 10, economy.Y + 10));
        Panel(form).ActionButton.PerformClick();
        Assert.AreEqual((BookAction.JoinWaitlist, "PR-101", DateTime.Today.AddDays(1), "Economy"), calls.Single());
    });

    [TestMethod]
    public void DoubleClickingARow_DoesTheSameAsTheButton() => FormHarness.OnStaThread(() =>
    {
        using var harness = new FormHarness();
        using HomeForm form = OpenOnTomorrow(harness);
        var calls = new List<string>();
        typeof(HomeForm).GetField("StartHook", Any)!.SetValue(form, new Action<BookAction, string, DateTime, string>((k, f, d, c) => calls.Add($"{k}|{f}|{c}")));
        Select(form, "PR-304");
        typeof(DataGridView).GetMethod("OnCellDoubleClick", Any)!.Invoke(Board(form), new object[] { new DataGridViewCellEventArgs(0, 0) });
        Assert.HasCount(1, calls);
        StringAssert.StartsWith(calls[0], "Book|PR-304|");
    });

    [TestMethod]
    public void AFlightThatHasLeft_DisablesTheButton_AndSaysSo() => FormHarness.OnStaThread(() =>
    {
        using var harness = new FormHarness();
        harness.Store.SetSetting(BookingStore.SettingDemoActivity, "0");
        using var form = new HomeForm(harness.Store, harness.Session) { StartPosition = FormStartPosition.Manual, Location = new Point(20, 20), ShowInTaskbar = false };
        form.Show();
        Application.DoEvents();   // today: the earliest flights of the day have left
        DataGridView grid = Board(form);
        FlightRow? landed = grid.Rows.Cast<DataGridViewRow>().Select(r => (FlightRow)r.DataBoundItem!).FirstOrDefault(r => r.Status.Kind is FlightStatusKind.Landed or FlightStatusKind.InFlight or FlightStatusKind.Departed);
        if (landed is null) { Assert.Inconclusive("no flight has left yet at this time of day"); return; }
        Select(form, landed.FlightNo);
        Assert.IsFalse(Panel(form).ActionButton.Enabled);
        Assert.AreEqual("This flight has already left.", Panel(form).Reason);
        bool called = false;
        typeof(HomeForm).GetField("StartHook", Any)!.SetValue(form, new Action<BookAction, string, DateTime, string>((_, _, _, _) => called = true));
        typeof(DataGridView).GetMethod("OnCellDoubleClick", Any)!.Invoke(grid, new object[] { new DataGridViewCellEventArgs(0, 0) });
        Assert.IsFalse(called, "double-clicking a flight that has left does nothing");
    });

    [TestMethod]
    public void TheBoardRefresh_KeepsTheChosenCabin_AndTheSelection() => FormHarness.OnStaThread(() =>
    {
        using var harness = new FormHarness();
        using HomeForm form = OpenOnTomorrow(harness);
        Select(form, "PR-101");
        Rectangle business = Panel(form).CabinRowBounds(1);
        Panel(form).ClickAt(new Point(business.X + 10, business.Y + 10));
        Application.DoEvents();
        typeof(HomeForm).GetMethod("RefreshFlights", Any)!.Invoke(form, null);
        Application.DoEvents();
        Assert.AreEqual("PR-101", ((FlightRow)Board(form).CurrentRow!.DataBoundItem!).FlightNo);
        Assert.AreEqual(1, Panel(form).SelectedCabin, "the chosen cabin survives a refresh");
    });

    [TestMethod]
    public void WithNoFlights_ThePanelSaysNoFlightSelected_AndTheButtonIsOff() => FormHarness.OnStaThread(() =>
    {
        using var harness = new FormHarness();
        using HomeForm form = OpenOnTomorrow(harness);
        Select(form, "PR-101");
        Field<TextBox>(form, "_boardSearch").Text = "zzzz";
        Application.DoEvents();
        Assert.AreEqual("No flight selected", Panel(form).AccessibleDescription);
        Assert.IsFalse(Panel(form).ActionButton.Enabled);
    });

    [TestMethod]
    public void ClickingARegionCard_SetsTheChip_AndShowsThatRegionsFlights() => FormHarness.OnStaThread(() =>
    {
        using var harness = new FormHarness();
        using HomeForm form = OpenOnTomorrow(harness);
        DestinationCard card = All(form).OfType<DestinationCard>().First(c => c.Title == "Europe");
        typeof(Control).GetMethod("OnClick", Any)!.Invoke(card, new object[] { EventArgs.Empty });
        Application.DoEvents();
        Assert.AreEqual("Europe", Field<string>(form, "_region"));
        Assert.AreEqual(GlassButtonStyle.Primary, Field<Dictionary<string, GlassButton>>(form, "_regionChips")["Europe"].Style);
        Assert.IsTrue(Board(form).Rows.Cast<DataGridViewRow>().All(r => FlightRegions.Of(((FlightRow)r.DataBoundItem!).Flight) == "Europe"));
        Assert.IsNotNull(Board(form).CurrentRow, "the first flight of the region is selected");
    });

    [TestMethod]
    public void TheBoardArea_IsTallEnoughForAFourCabinPanel() => FormHarness.OnStaThread(() =>
    {
        using var harness = new FormHarness();
        using HomeForm form = OpenOnTomorrow(harness);
        Assert.IsGreaterThanOrEqualTo(Panel(form).RequiredHeight(4), Panel(form).Height, "the panel is never shorter than four cabins, a reason and the button need");
        Select(form, AppData.EuropeFlights.First().FlightNo);
        Rectangle last = Panel(form).CabinRowBounds(3);
        Assert.IsLessThanOrEqualTo(Panel(form).ReasonBounds.Top, last.Bottom);
        Assert.IsLessThanOrEqualTo(Panel(form).Height, Panel(form).ActionButton.Bottom);
    });
}
