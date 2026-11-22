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
    public void SearchFields_AreRounded_AndTheDateIsADateField() => FormHarness.OnStaThread(() =>
    {
        using var harness = new FormHarness();
        using HomeForm form = Open(harness);
        Assert.IsGreaterThanOrEqualTo(3, All(form).OfType<RoundedInput>().Count(), "destination, date and cabin sit in rounded boxes");
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
    public void Changing_TheDestination_StillRefreshesTheBoard() => FormHarness.OnStaThread(() =>
    {
        using var harness = new FormHarness();
        using HomeForm form = Open(harness);
        var to = (ComboBox)typeof(HomeForm).GetField("_to", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        var board = (DataGridView)typeof(HomeForm).GetField("_flights", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
        to.SelectedIndex = Math.Min(2, to.Items.Count - 1);
        Application.DoEvents();
        Assert.IsGreaterThan(0, board.Rows.Count, "a destination with flights fills the board");
    });
}
