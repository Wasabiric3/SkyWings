using SkyWings.UI;

namespace SkyWings.Tests;

[TestClass]
[DoNotParallelize]
public sealed class StaffLoginLookTests
{
    private static IEnumerable<Control> All(Control root)
    {
        foreach (Control c in root.Controls)
        {
            yield return c;
            foreach (Control inner in All(c)) yield return inner;
        }
    }

    [TestMethod]
    public void TheShowPasswordToggle_TogglesTheMask_AndStaysInsideThePasswordRow() => FormHarness.OnStaThread(() =>
    {
        using var harness = new FormHarness();
        (_, Func<Form> create) = harness.Forms().First(f => f.Name == "staff-login");
        using Form form = create();
        form.Show();
        Application.DoEvents();
        GlassButton toggle = All(form).OfType<GlassButton>().Single(b => b.AccessibleName == "Show or hide password");
        TextBox password = All(form).OfType<TextBox>().Single(t => t.UseSystemPasswordChar);
        Assert.IsLessThanOrEqualTo(toggle.Parent!.Width, toggle.Right, "inside the row before the first click");

        toggle.PerformClick();
        Application.DoEvents();
        Assert.IsFalse(password.UseSystemPasswordChar, "Show reveals the password");
        Assert.AreEqual("Hide", toggle.Text);
        Assert.IsLessThanOrEqualTo(toggle.Parent.Width, toggle.Right, "a longer or shorter caption must not push it out of the row");
        Assert.IsGreaterThanOrEqualTo(0, toggle.Left);

        toggle.PerformClick();
        Assert.IsTrue(password.UseSystemPasswordChar);
        Assert.IsLessThanOrEqualTo(toggle.Parent.Width, toggle.Right);
    });
}
