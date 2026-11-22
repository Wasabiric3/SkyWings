using System.Reflection;
using SkyWings.UI;

namespace SkyWings.Tests;

/// <summary>Help in the wizard's look: a rounded search field and a topic list that selects in the soft green, with the search still working.</summary>
[TestClass]
[DoNotParallelize]
public sealed class HelpLookTests
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, Any)!.GetValue(owner)!;

    private static IEnumerable<Control> All(Control root)
    {
        foreach (Control c in root.Controls)
        {
            yield return c;
            foreach (Control inner in All(c)) yield return inner;
        }
    }

    [TestMethod]
    public void Search_IsARoundedField_AndTheTopicListDrawsItsOwnSelection() => FormHarness.OnStaThread(() =>
    {
        using var harness = new FormHarness();
        using var form = new HelpForm(harness.Session);
        form.Show();
        Application.DoEvents();
        Assert.IsTrue(All(form).OfType<RoundedInput>().Any(r => ReferenceEquals(r.Input, Field<TextBox>(form, "_search"))), "the search box sits in a rounded field");
        var topics = Field<ListBox>(form, "_topics");
        Assert.AreEqual(DrawMode.OwnerDrawFixed, topics.DrawMode);
        Assert.AreEqual(BorderStyle.None, topics.BorderStyle);
    });

    [TestMethod]
    public void Searching_NarrowsTheTopics_AndShowsTheFirstMatch() => FormHarness.OnStaThread(() =>
    {
        using var harness = new FormHarness();
        using var form = new HelpForm(harness.Session);
        form.Show();
        var topics = Field<ListBox>(form, "_topics");
        int all = topics.Items.Count;
        Field<TextBox>(form, "_search").Text = "seat";
        Application.DoEvents();
        Assert.IsGreaterThan(0, topics.Items.Count);
        Assert.IsLessThan(all, topics.Items.Count);
        Assert.IsNotNull(topics.SelectedItem);
    });
}
