using System.Reflection;
using SkyWings.UI;

namespace SkyWings.Tests;

/// <summary>The admin console frame in the wizard's look: the section buttons get their own row and fit, the flights toolbar does not overflow.</summary>
[TestClass]
[DoNotParallelize]
public sealed class AdminFrameLookTests
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

    private static AdminForm OpenAtMinimum(FormHarness harness)
    {
        var form = new AdminForm(harness.Admin) { StartPosition = FormStartPosition.Manual, Location = new Point(20, 20) };
        form.Show();
        form.Size = form.MinimumSize;
        Application.DoEvents();
        return form;
    }

    [TestMethod]
    public void EverySectionButton_IsVisible_InsideTheWindow_AtTheMinimumSize() => FormHarness.OnStaThread(() =>
    {
        using var harness = new FormHarness();
        using AdminForm form = OpenAtMinimum(harness);
        string[] sections = { "Flights", "Bookings", "Refunds", "Waitlist", "Reports", "Staff", "Audit", "Database" };
        foreach (string name in sections)
        {
            GlassButton button = All(form).OfType<GlassButton>().Single(b => b.Text.StartsWith(name, StringComparison.Ordinal));
            Rectangle r = form.RectangleToClient(button.Parent!.RectangleToScreen(button.Bounds));
            Assert.IsLessThanOrEqualTo(form.ClientSize.Width, r.Right, $"{name} runs past the right edge");
            Assert.IsGreaterThanOrEqualTo(0, r.Left, name);
        }
    });

    [TestMethod]
    public void SectionButtons_SitBelowTheTitleRow_NotBesideThePasswordButton() => FormHarness.OnStaThread(() =>
    {
        using var harness = new FormHarness();
        using AdminForm form = OpenAtMinimum(harness);
        GlassButton flights = All(form).OfType<GlassButton>().Single(b => b.Text == "Flights");
        GlassButton password = All(form).OfType<GlassButton>().Single(b => b.Text == "Password");
        Rectangle f = form.RectangleToClient(flights.Parent!.RectangleToScreen(flights.Bounds));
        Rectangle p = form.RectangleToClient(password.Parent!.RectangleToScreen(password.Bounds));
        Assert.IsGreaterThanOrEqualTo(p.Bottom, f.Top, "the section row starts under the title and account row");
    });

    [TestMethod]
    public void TheFlightsInventoryLine_IsBelowTheGrid_NotInTheToolbar() => FormHarness.OnStaThread(() =>
    {
        using var harness = new FormHarness();
        using AdminForm form = OpenAtMinimum(harness);
        var label = (Label)typeof(AdminForm).GetField("_inventoryLabel", Any)!.GetValue(form)!;
        var flights = (DataGridView)typeof(AdminForm).GetField("_flights", Any)!.GetValue(form)!;
        Assert.IsNotInstanceOfType<FlowLayoutPanel>(label.Parent, "not squeezed into the toolbar");
        Rectangle l = form.RectangleToClient(label.Parent!.RectangleToScreen(label.Bounds));
        Rectangle g = form.RectangleToClient(flights.Parent!.RectangleToScreen(flights.Bounds));
        Assert.IsGreaterThanOrEqualTo(g.Bottom, l.Top);
        Assert.IsLessThanOrEqualTo(form.ClientSize.Width, l.Right);
    });

    private static void OpenTab(AdminForm form, string name)
    {
        All(form).OfType<GlassButton>().First(b => b.Text == name && b.Parent is FlowLayoutPanel).PerformClick();
        Application.DoEvents();
    }

    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, Any)!.GetValue(owner)!;

    [TestMethod]
    public void TheFilterBoxes_OnBookingsRefundsAndAudit_AreRounded() => FormHarness.OnStaThread(() =>
    {
        using var harness = new FormHarness();
        using AdminForm form = OpenAtMinimum(harness);
        foreach ((string tab, string[] fields) in new[]
        {
            ("Bookings", new[] { "_bookingView", "_bookingDate", "_bookingStatus", "_bookingSearch" }),
            ("Refunds", new[] { "_refundView", "_refundPeriod", "_refundSort", "_refundHandled", "_refundSearch" }),
            ("Audit", new[] { "_auditSearch" })
        })
        {
            OpenTab(form, tab);
            foreach (string name in fields)
            {
                var input = (Control)typeof(AdminForm).GetField(name, Any)!.GetValue(form)!;
                Control? box = input.Parent is RoundedInput ? input.Parent : input.Parent?.Parent;   // a combo box sits in a clipping panel inside the box
                Assert.IsInstanceOfType<RoundedInput>(box, $"{tab}: {name} sits in a rounded box");
                Assert.IsGreaterThanOrEqualTo(120, box!.Width, $"{tab}: the {name} box keeps a usable width");
            }
        }
    });

    [TestMethod]
    public void TheBookingsLegend_NeverCoversTheFilters_AtTheMinimumSize() => FormHarness.OnStaThread(() =>
    {
        using var harness = new FormHarness();
        using AdminForm form = OpenAtMinimum(harness);
        OpenTab(form, "Bookings");
        var status = Field<ComboBox>(form, "_bookingStatus");
        Rectangle filters = form.RectangleToClient(status.Parent!.RectangleToScreen(status.Parent.ClientRectangle));
        foreach (TripLegend legend in All(form).OfType<TripLegend>().Where(l => l.Visible))
        {
            Rectangle l = form.RectangleToClient(legend.Parent!.RectangleToScreen(legend.Bounds));
            Assert.IsGreaterThanOrEqualTo(filters.Bottom, l.Top, "the legend starts below the filter row");
        }
    });

    [TestMethod]
    public void SwitchingTheRefundsView_HidesTheWholeBox_NotJustItsInput() => FormHarness.OnStaThread(() =>
    {
        using var harness = new FormHarness();
        using AdminForm form = OpenAtMinimum(harness);
        OpenTab(form, "Refunds");
        var view = Field<ComboBox>(form, "_refundView");
        var sort = Field<ComboBox>(form, "_refundSort");
        Assert.IsTrue(sort.Visible);
        view.SelectedIndex = 1;   // Refund requests
        Application.DoEvents();
        Control box = sort.Parent!.Parent is RoundedInput host ? host : sort.Parent!;
        Assert.IsFalse(box.Visible, "no empty rounded box is left behind");
        view.SelectedIndex = 0;
        Application.DoEvents();
        Assert.IsTrue(sort.Visible);
    });

    [TestMethod]
    public void TheDatabasePage_TablePickerAndSearch_AreRounded_AndFitAtTheMinimumSize() => FormHarness.OnStaThread(() =>
    {
        using var harness = new FormHarness();
        using AdminForm form = OpenAtMinimum(harness);
        OpenTab(form, "Database");
        object page = All(form).First(c => c.GetType().Name == "DatabaseBrowserPage");
        foreach (string name in new[] { "_tables", "_searchBox" })
        {
            var input = (Control)page.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
            Control? box = input.Parent is RoundedInput ? input.Parent : input.Parent?.Parent;
            Assert.IsInstanceOfType<RoundedInput>(box, name + " sits in a rounded box");
            Rectangle r = form.RectangleToClient(box!.Parent!.RectangleToScreen(box.Bounds));
            Assert.IsLessThanOrEqualTo(form.ClientSize.Width, r.Right, name + " is inside the window");
            var count = (Label)page.GetType().GetField("_count", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(page)!;
            Rectangle c = form.RectangleToClient(count.Parent!.RectangleToScreen(count.Bounds));
            Assert.IsLessThanOrEqualTo(c.Left, r.Right, name + " does not run under the row count");
            Assert.IsGreaterThanOrEqualTo(180, box.Width);
        }
    });
}
