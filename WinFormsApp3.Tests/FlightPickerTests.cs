using SkyWings.UI;

namespace SkyWings.Tests;

/// <summary>The flight picker: a compact, searchable, grouped list in a popup that never grows past a few rows.</summary>
[TestClass]
[DoNotParallelize]
public sealed class FlightPickerTests
{
    private static List<Flight> Sample() => AppData.DomesticFlights.Concat(AppData.InternationalFlights).Concat(AppData.EuropeFlights).Concat(AppData.NorthAmericaFlights)
        .Concat(new[] { new Flight("PR-501", "Cebu (CEB)", "Manila (MNL)", 3500m), new Flight("PR-502", "Davao (DVO)", "Manila (MNL)", 4200m), new Flight("PR-602", "Bangkok (BKK)", "Manila (MNL)", 7800m) })
        .ToList();

    private static IEnumerable<string> Headings(IReadOnlyList<object> items) => items.OfType<string>();

    [TestMethod]
    public void Groups_AreDomesticFirst_ThenSoutheastAsia_Europe_NorthAmerica()
    {
        IReadOnlyList<object> items = FlightPicker.BuildItems(Sample(), "");
        CollectionAssert.AreEqual(new[] { "DOMESTIC", "SOUTHEAST ASIA", "EUROPE", "NORTH AMERICA" }, Headings(items).ToArray());
        Assert.AreEqual(Sample().Count, items.OfType<Flight>().Count(), "every flight is listed once");
    }

    [TestMethod]
    public void WithinAGroup_FlightsLeavingManilaComeFirst_ThenTheReturns()
    {
        var domestic = FlightPicker.BuildItems(Sample(), "").TakeWhile(i => i is not string s || s == "DOMESTIC").OfType<Flight>().ToList();
        int firstReturn = domestic.FindIndex(f => !f.Origin.StartsWith("Manila", StringComparison.Ordinal));
        Assert.IsGreaterThan(0, firstReturn);
        Assert.IsTrue(domestic.Skip(firstReturn).All(f => !f.Origin.StartsWith("Manila", StringComparison.Ordinal)), "no outbound flight after the first return");
    }

    [TestMethod]
    [DataRow("bangkok")]
    [DataRow("BKK")]
    [DataRow("  Bangkok  ")]
    public void Search_MatchesACity_InEitherDirection(string term)
    {
        IReadOnlyList<object> items = FlightPicker.BuildItems(Sample(), term);
        List<Flight> flights = items.OfType<Flight>().ToList();
        Assert.IsGreaterThan(0, flights.Count);
        Assert.IsTrue(flights.All(f => (f.Origin + f.Destination).Contains("Bangkok", StringComparison.OrdinalIgnoreCase)));
        CollectionAssert.AreEqual(new[] { "SOUTHEAST ASIA" }, Headings(items).ToArray(), "only the groups that still have a match keep their heading");
    }

    [TestMethod]
    public void Search_MatchesAFlightNumber() => Assert.AreEqual("PR-304", FlightPicker.BuildItems(Sample(), "pr-304").OfType<Flight>().Single().FlightNo);

    [TestMethod]
    public void Search_WithNoMatch_GivesAnEmptyList() => Assert.IsEmpty(FlightPicker.BuildItems(Sample(), "zzzz"));

    [TestMethod]
    public void NextSelectable_SkipsHeadings_AndStopsAtTheEnds()
    {
        IReadOnlyList<object> items = FlightPicker.BuildItems(Sample(), "");
        Assert.AreEqual(1, FlightPicker.NextSelectable(items, -1, +1), "from nothing, down lands on the first flight, not on the heading");
        Assert.IsNull(FlightPicker.NextSelectable(items, 1, -1), "up from the first flight stays put");
        int last = items.Count - 1;
        Assert.IsNull(FlightPicker.NextSelectable(items, last, +1));
        int beforeHeading = Enumerable.Range(1, items.Count - 1).First(i => items[i] is string) - 1;
        Assert.IsTrue(items[FlightPicker.NextSelectable(items, beforeHeading, +1)!.Value] is Flight, "down over a heading lands on a flight");
    }

    [TestMethod]
    public void Picking_SetsTheSelection_RaisesTheEventOnce_AndClosesThePopup() => FormHarness.OnStaThread(() =>
    {
        using var form = new Form { Width = 700, Height = 400, StartPosition = FormStartPosition.Manual, Location = new Point(20, 20) };
        var picker = new FlightPicker { Width = 520, Location = new Point(20, 20) };
        picker.SetFlights(Sample());
        form.Controls.Add(picker);
        form.Show();
        Application.DoEvents();
        int raised = 0;
        picker.SelectedIndexChanged += (_, _) => raised++;
        picker.OpenDropDown();
        Assert.IsTrue(picker.IsOpen);
        Flight chosen = Sample()[7];
        picker.Pick(chosen);
        Assert.AreSame(chosen, picker.Selected);
        Assert.AreEqual(7, picker.SelectedIndex);
        Assert.AreEqual(1, raised);
        Assert.IsFalse(picker.IsOpen);
        picker.Pick(chosen);
        Assert.AreEqual(1, raised, "picking the same flight again is not a change");
    });

    [TestMethod]
    public void ThePopup_ShowsAboutSixRows_NotTheWholeList_AndMatchesTheFieldWidth() => FormHarness.OnStaThread(() =>
    {
        using var form = new Form { Width = 700, Height = 500, StartPosition = FormStartPosition.Manual, Location = new Point(20, 20) };
        var picker = new FlightPicker { Width = 520, Location = new Point(20, 20) };
        picker.SetFlights(Sample());
        form.Controls.Add(picker);
        form.Show();
        Application.DoEvents();
        picker.OpenDropDown();
        Assert.IsNotNull(picker.Popup);
        int rowHeight = Gfx.S(picker, 46);
        Assert.IsLessThanOrEqualTo(Gfx.S(picker, 44) + 7 * rowHeight, picker.Popup.Height, "no taller than a search box and about six rows");
        Assert.IsGreaterThanOrEqualTo(picker.Width, picker.Popup.Width, "as wide as the field");
        Assert.IsLessThanOrEqualTo(picker.Width + Gfx.S(picker, 40), picker.Popup.Width);
        picker.Popup.Close();
    });

    [TestMethod]
    public void TheCollapsedField_ShowsTheRouteAndTheFlightNumber_OrAPrompt() => FormHarness.OnStaThread(() =>
    {
        var picker = new FlightPicker();
        picker.SetFlights(Sample());
        Assert.AreEqual("Choose a flight", picker.DisplayText);
        picker.SelectedIndex = 0;
        StringAssert.Contains(picker.DisplayText, Sample()[0].FlightNo);
        StringAssert.Contains(picker.DisplayText, Sample()[0].Destination);
        picker.SelectedIndex = -1;
        Assert.AreEqual("Choose a flight", picker.DisplayText);
    });

    [TestMethod]
    public void Wizard_Step1_UsesThePicker_InARoundedBox_AndTheReturnListFollowsTheOutboundFlight() => WizardHarness.OnStaThread(() =>
    {
        using var wizard = new WizardHarness();
        var flight = wizard.Field<FlightPicker>("_flight");
        var back = wizard.Field<FlightPicker>("_returnFlight");
        Assert.IsGreaterThan(5, flight.Flights.Count);
        Assert.IsNotNull(flight.Selected, "the first flight is chosen at the start");
        Assert.IsInstanceOfType<RoundedInput>(flight.Parent, "in the wizard's rounded box");
        Assert.IsFalse(back.Enabled, "the return picker waits for 'Add a return flight'");

        flight.Selected = flight.Flights.First(f => f.FlightNo == "PR-101");
        Assert.IsTrue(back.Flights.All(f => f.Origin == flight.Selected!.Destination && f.Destination == flight.Selected.Origin), "only flights back along the same route");
        Assert.IsGreaterThan(0, back.Flights.Count);
        Assert.IsNotNull(back.Selected);
    });

    [TestMethod]
    public void Wizard_Prefill_FromTheHomeScreen_ChoosesThatFlight() => WizardHarness.OnStaThread(() =>
    {
        using var wizard = new WizardHarness();
        wizard.Form.Prefill("PR-105", null, null, null);
        Assert.AreEqual("PR-105", wizard.Field<FlightPicker>("_flight").Selected!.FlightNo);
    });

    [TestMethod]
    public void SavePopupImage_ForReview() => FormHarness.OnStaThread(() =>
    {
        using var form = new Form { Width = 760, Height = 520, StartPosition = FormStartPosition.Manual, Location = new Point(20, 20), BackColor = Theme.PageBg };
        var picker = new FlightPicker { Width = 520, Location = new Point(40, 40) };
        picker.SetFlights(Sample());
        var box = new RoundedInput(picker) { Width = 520, Location = new Point(40, 40) };
        form.Controls.Add(box);
        form.Show();
        Application.DoEvents();
        picker.SelectedIndex = 3;
        picker.OpenDropDown();
        Application.DoEvents();
        var host = (ToolStripControlHost)picker.Popup!.Items[0];
        using var bmp = new Bitmap(host.Control!.Width, host.Control.Height);
        host.Control.DrawToBitmap(bmp, new Rectangle(Point.Empty, bmp.Size));
        Directory.CreateDirectory(FormHarness.Folder);
        bmp.Save(Path.Combine(FormHarness.Folder, "flight-picker-popup.png"));
        using Bitmap field = FormHarness.Snapshot(form);
        field.Save(Path.Combine(FormHarness.Folder, "flight-picker-field.png"));
        Assert.IsGreaterThan(0, bmp.Height);
        picker.Popup.Close();
    });
}
