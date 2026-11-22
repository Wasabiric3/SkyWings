using System.Reflection;
using SkyWings.UI;

namespace SkyWings.Tests;

/// <summary>The waitlist dialogs in the wizard's look: a page header, rounded fields, the long hint fully visible, every button still there.</summary>
[TestClass]
[DoNotParallelize]
public sealed class WaitlistDialogsLookTests
{
    private static IEnumerable<Control> All(Control root)
    {
        foreach (Control c in root.Controls)
        {
            yield return c;
            foreach (Control inner in All(c)) yield return inner;
        }
    }

    private static Form Open(string name, FormHarness harness)
    {
        (_, Func<Form> create) = harness.Forms().Concat(harness.BookingForms()).First(f => f.Name == name);
        Form form = create();
        form.Show();
        Application.DoEvents();
        return form;
    }

    [TestMethod]
    public void JoinWaitlist_SeatsIsARoundedChoiceOfOneToNine_AndTheWholeHintFits() => FormHarness.OnStaThread(() =>
    {
        using var harness = new FormHarness();
        using Form form = Open("join-waitlist", harness);
        Assert.IsFalse(All(form).OfType<NumericUpDown>().Any(), "the native spinner is gone");
        ComboBox seats = All(form).OfType<ComboBox>().Single();
        Assert.AreEqual(9, seats.Items.Count);
        Assert.AreEqual(0, seats.SelectedIndex);
        Assert.IsTrue(All(form).OfType<RoundedInput>().Any(r => ReferenceEquals(r.Input, seats)));
        PageHeader header = All(form).OfType<PageHeader>().Single();
        Assert.Contains("bookings close", header.Hint);
        Assert.IsEmpty(FormHarness.Clipped(form));
    });

    [TestMethod]
    public void WaitlistLookup_HasAPageHeader_AndAllFourButtons() => FormHarness.OnStaThread(() =>
    {
        using var harness = new FormHarness();
        using Form form = Open("waitlist-status", harness);
        Assert.AreEqual("Check your waitlist spot", All(form).OfType<PageHeader>().Single().Title);
        string[] buttons = All(form).OfType<GlassButton>().Select(b => b.Text).ToArray();
        CollectionAssert.IsSubsetOf(new[] { "Look up", "Close", "Book now  →", "Leave waitlist" }, buttons);
        Assert.IsEmpty(FormHarness.Clipped(form));
    });

    [TestMethod]
    public void WaitlistEntry_HasAPageHeaderWithTheFlight_AndTheQueueStaysReadable() => FormHarness.OnStaThread(() =>
    {
        using var harness = new FormHarness();
        using Form form = Open("waitlist-entry", harness);
        Assert.StartsWith("PR-101", All(form).OfType<PageHeader>().Single().Title);
        TextBox queue = All(form).OfType<TextBox>().Single(t => t.Multiline);
        Assert.Contains("▶", queue.Text);
        Assert.IsEmpty(FormHarness.Clipped(form));
    });
}
