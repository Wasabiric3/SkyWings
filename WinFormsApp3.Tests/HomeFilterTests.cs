using System.Reflection;
using SkyWings.Controllers;
using SkyWings.UI;

namespace SkyWings.Tests;

/// <summary>The home screen's filter row (region chips, day selector, search box) that replaced the search card.</summary>
[TestClass]
[DoNotParallelize]
public sealed class HomeFilterTests
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

    private static HomeForm Open(FormHarness harness)
    {
        harness.Store.SetSetting(BookingStore.SettingDemoActivity, "0");
        var form = new HomeForm(harness.Store, harness.Session) { StartPosition = FormStartPosition.Manual, Location = new Point(20, 20), ShowInTaskbar = false };
        form.Show();
        Application.DoEvents();
        return form;
    }

    private static List<FlightRow> Rows(HomeForm form)
        => Field<DataGridView>(form, "_flights").Rows.Cast<DataGridViewRow>().Select(r => (FlightRow)r.DataBoundItem!).ToList();

    private static Dictionary<string, GlassButton> Chips(HomeForm form) => Field<Dictionary<string, GlassButton>>(form, "_regionChips");

    [TestMethod]
    public void TheSearchCard_IsGone() => FormHarness.OnStaThread(() =>
    {
        using var harness = new FormHarness();
        using HomeForm form = Open(harness);
        foreach (string name in new[] { "_to", "_cabin", "_bookSelected" })
            Assert.IsNull(typeof(HomeForm).GetField(name, Any), name + " was part of the search card");
        Assert.IsFalse(All(form).OfType<FieldRow>().Any(r => r.Controls.OfType<Label>().Any(l => l.Text.StartsWith("TO", StringComparison.Ordinal))), "no 'TO (FROM MANILA)' field");
        Assert.IsFalse(All(form).OfType<GlassButton>().Any(b => b.Text.StartsWith("Book selected", StringComparison.Ordinal)));
    });

    [TestMethod]
    public void Chips_AreAllAndTheFourRegions_AndAllIsChosenFirst() => FormHarness.OnStaThread(() =>
    {
        using var harness = new FormHarness();
        using HomeForm form = Open(harness);
        Dictionary<string, GlassButton> chips = Chips(form);
        CollectionAssert.AreEqual(new[] { "", "Domestic", "Southeast Asia", "Europe", "North America" }, chips.Keys.ToArray());
        Assert.AreEqual("All", chips[""].Text);
        Assert.AreEqual(GlassButtonStyle.Primary, chips[""].Style, "All is chosen at the start");
        Assert.AreEqual(GlassButtonStyle.Outline, chips["Europe"].Style);
    });

    [TestMethod]
    public void ChoosingARegion_FiltersTheBoard_AndMarksItsChip() => FormHarness.OnStaThread(() =>
    {
        using var harness = new FormHarness();
        using HomeForm form = Open(harness);
        int all = Rows(form).Count;
        Chips(form)["Europe"].PerformClick();
        Application.DoEvents();
        List<FlightRow> europe = Rows(form);
        Assert.IsNotEmpty(europe);
        Assert.IsLessThan(all, europe.Count);
        Assert.IsTrue(europe.All(r => FlightRegions.Of(r.Flight) == "Europe"));
        Assert.AreEqual(GlassButtonStyle.Primary, Chips(form)["Europe"].Style);
        Assert.AreEqual(GlassButtonStyle.Outline, Chips(form)[""].Style);
        Chips(form)[""].PerformClick();
        Application.DoEvents();
        Assert.HasCount(all, Rows(form));
    });

    [TestMethod]
    public void TheDayArrows_MoveTheDay_NeverBeforeToday_AndTodayComesBack() => FormHarness.OnStaThread(() =>
    {
        using var harness = new FormHarness();
        using HomeForm form = Open(harness);
        var day = Field<DateField>(form, "_day");
        GlassButton prev = Field<GlassButton>(form, "_dayPrev"), next = Field<GlassButton>(form, "_dayNext"), today = Field<GlassButton>(form, "_dayToday");
        Assert.AreEqual(DateTime.Today, day.Value.Date);
        Assert.IsFalse(prev.Enabled, "nothing before today");
        next.PerformClick();
        Assert.AreEqual(DateTime.Today.AddDays(1), day.Value.Date);
        Assert.IsTrue(prev.Enabled);
        next.PerformClick();
        prev.PerformClick();
        Assert.AreEqual(DateTime.Today.AddDays(1), day.Value.Date);
        today.PerformClick();
        Assert.AreEqual(DateTime.Today, day.Value.Date);
        Assert.IsFalse(prev.Enabled);
        // the board follows the day: tomorrow's flights have a tomorrow status
        next.PerformClick();
        Application.DoEvents();
        Assert.IsTrue(Rows(form).All(r => r.Status.Scheduled.Date == DateTime.Today.AddDays(1)));
    });

    [TestMethod]
    public void TheSearchBox_NarrowsTheBoard_AndCombinesWithTheRegion() => FormHarness.OnStaThread(() =>
    {
        using var harness = new FormHarness();
        using HomeForm form = Open(harness);
        var search = Field<TextBox>(form, "_boardSearch");
        search.Text = "cebu";
        Application.DoEvents();
        List<FlightRow> rows = Rows(form);
        Assert.IsNotEmpty(rows);
        Assert.IsTrue(rows.All(r => r.Flight.Destination.Contains("Cebu", StringComparison.OrdinalIgnoreCase)));
        Chips(form)["Europe"].PerformClick();
        Application.DoEvents();
        Assert.IsEmpty(Rows(form), "Cebu is not in Europe");
    });

    [TestMethod]
    public void ANoMatchSearch_LeavesAnEmptyBoard_WithoutError() => FormHarness.OnStaThread(() =>
    {
        using var harness = new FormHarness();
        using HomeForm form = Open(harness);
        foreach (string text in new[] { "zzzz", "%", "' OR 1=1 --", "   " })
        {
            Field<TextBox>(form, "_boardSearch").Text = text;
            Application.DoEvents();
            if (text.Trim().Length > 0 && text != "zzzz" || text == "zzzz") Assert.IsEmpty(Rows(form), $"'{text}' matches no flight");
        }
        Field<TextBox>(form, "_boardSearch").Text = "";
        Application.DoEvents();
        Assert.IsNotEmpty(Rows(form), "clearing the search brings the flights back");
    });

    [TestMethod]
    public void ARefresh_KeepsTheSelectedFlight() => FormHarness.OnStaThread(() =>
    {
        using var harness = new FormHarness();
        using HomeForm form = Open(harness);
        var grid = Field<DataGridView>(form, "_flights");
        grid.CurrentCell = grid[0, Math.Min(2, grid.Rows.Count - 1)];
        string chosen = ((FlightRow)grid.CurrentRow!.DataBoundItem!).FlightNo;
        typeof(HomeForm).GetMethod("RefreshFlights", Any)!.Invoke(form, null);
        Application.DoEvents();
        Assert.AreEqual(chosen, ((FlightRow)grid.CurrentRow!.DataBoundItem!).FlightNo, "the same flight stays selected after the board reloads");
    });
}
