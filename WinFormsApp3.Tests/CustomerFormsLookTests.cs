using System.Reflection;
using SkyWings.UI;

namespace SkyWings.Tests;

/// <summary>Check-in and Manage booking in the wizard's look: a page header above the card, Enter still finds the booking.</summary>
[TestClass]
[DoNotParallelize]
public sealed class CustomerFormsLookTests
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

    [TestMethod]
    [DataRow("checkin", "Online check-in")]
    [DataRow("manage-booking", "Manage booking")]
    public void Form_HasAPageHeader_AboveItsCard_AndEnterStillFindsTheBooking(string name, string title) => FormHarness.OnStaThread(() =>
    {
        using var harness = new FormHarness();
        (_, Func<Form> create) = harness.Forms().First(f => f.Name == name);
        using Form form = create();
        form.Show();
        Application.DoEvents();
        PageHeader header = All(form).OfType<PageHeader>().Single();
        Assert.AreEqual(title, header.Title);
        Assert.IsGreaterThanOrEqualTo(0, header.Hint.Length);
        GlassCard card = All(form).OfType<GlassCard>().First();
        Assert.IsLessThanOrEqualTo(card.Top, header.Bottom, "the header sits above the card, not inside it");
        Assert.AreSame(Field<GlassButton>(form, "_find"), form.AcceptButton, "Enter finds the booking");
        Assert.IsFalse(All(form).OfType<Label>().Any(l => l.Text == title), "the old centred heading is gone");
    });

    [TestMethod]
    public void CheckIn_AfterAFailedLookup_StillShowsTheMessageOnTheReferenceRow() => FormHarness.OnStaThread(() =>
    {
        using var harness = new FormHarness();
        using var form = new CheckInForm(harness.Store);
        form.Show();
        Field<TextBox>(form, "_reference").Text = "SKY-DOESNOTEXIST";
        Field<GlassButton>(form, "_find").PerformClick();
        Application.DoEvents();
        Assert.IsGreaterThanOrEqualTo(40, Field<FieldRow>(form, "_referenceRow").Height);
        Assert.IsTrue(Field<Panel>(form, "_lookupPage").Visible, "a failed lookup stays on the lookup page");
    });
}
