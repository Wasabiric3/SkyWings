using System.Reflection;
using SkyWings.UI;

namespace SkyWings.Tests;

/// <summary>The home screen in the wizard's look: rounded search fields, a fully visible main action, a date field instead of the native picker.</summary>
[TestClass]
[DoNotParallelize]
public sealed class HomeLookTests
{
    private static IEnumerable<Control> All(Control root)
    {
        foreach (Control c in root.Controls)
        {
            yield return c;
            foreach (Control inner in All(c)) yield return inner;
        }
    }

    private static HomeForm Open(FormHarness harness)
    {
        harness.Store.SetSetting(BookingStore.SettingDemoActivity, "0");
        var form = new HomeForm(harness.Store, harness.Session)
        {
            StartPosition = FormStartPosition.Manual, Location = new Point(20, 20), ShowInTaskbar = false
        };
        form.Show();
        Application.DoEvents();
        return form;
    }

    [TestMethod]
    public void FilterRow_IsRounded_AndTheDayIsADateField() => FormHarness.OnStaThread(() =>
    {
        using var harness = new FormHarness();
        using HomeForm form = Open(harness);
        Assert.IsGreaterThanOrEqualTo(2, All(form).OfType<RoundedInput>().Count(), "the day and the search box sit in rounded boxes");
        Assert.IsTrue(All(form).OfType<DateField>().Any(), "the native date picker is replaced");
        Assert.IsFalse(All(form).OfType<DateTimePicker>().Any());
    });

    [TestMethod]
    public void TheBookAFlightButton_IsFullyInsideItsCard() => FormHarness.OnStaThread(() =>
    {
        using var harness = new FormHarness();
        using HomeForm form = Open(harness);
        var button = (GlassButton)typeof(HomeForm).GetField("_bookButton", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        Control parent = button.Parent!;
        Assert.IsLessThanOrEqualTo(parent.ClientSize.Height, button.Bottom, "the main action is cut off at the bottom of the hero");
    });

    [TestMethod]
    public void TheBoard_IsFilledAtTheStart() => FormHarness.OnStaThread(() =>
    {
        using var harness = new FormHarness();
        using HomeForm form = Open(harness);
        var board = (DataGridView)typeof(HomeForm).GetField("_flights", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        Assert.IsGreaterThan(0, board.Rows.Count, "today's flights fill the board");
    });

    [TestMethod]
    public void TheTopMenu_HasNoRewardsLink_AndKeepsTheOthers() => FormHarness.OnStaThread(() =>
    {
        using var harness = new FormHarness();
        using HomeForm form = Open(harness);
        string[] links = All(form).OfType<GlassButton>().Select(b => b.Text).ToArray();
        CollectionAssert.DoesNotContain(links, "Rewards");
        foreach (string expected in new[] { "Manage booking", "Waitlist", "Check-in", "Help", "Staff" })
            CollectionAssert.Contains(links, expected);
    });
}
