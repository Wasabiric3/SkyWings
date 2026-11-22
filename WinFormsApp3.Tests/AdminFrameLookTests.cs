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
}
