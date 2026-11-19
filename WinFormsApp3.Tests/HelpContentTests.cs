using SkyWings;

namespace SkyWings.Tests;

[TestClass]
public sealed class HelpContentTests
{
    [TestMethod]
    public void EveryTopicHasATitleAndBody()
    {
        var topics = HelpContent.Topics();

        Assert.IsNotEmpty(topics);
        foreach (var topic in topics)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(topic.Title));
            Assert.IsFalse(string.IsNullOrWhiteSpace(topic.Body), topic.Title);
        }
        Assert.AreEqual(topics.Count, topics.Select(t => t.Title).Distinct().Count(), "Topic titles must be unique.");
    }

    [TestMethod]
    public void TripStatusTopic_NamesEveryStatus()
    {
        var topic = HelpContent.Topics().Single(t => t.Title == "Trip status");
        foreach (string label in new[] { "Upcoming", "Check-in open", "Checked in", "Boarding", "In flight", "Completed", "No-show", "Canceled" })
            StringAssert.Contains(topic.Body, label);
    }

    [TestMethod]
    public void NoHelpTopicMentionsDemo()
    {
        // SkyWings is presented as a live operations system: the simulation runs unseen.
        foreach (var topic in HelpContent.Topics())
        {
            Assert.IsFalse(topic.Title.Contains("demo", StringComparison.OrdinalIgnoreCase), topic.Title);
            Assert.IsFalse(topic.Body.Contains("demo", StringComparison.OrdinalIgnoreCase), topic.Title);
        }
        Assert.IsTrue(HelpContent.Topics().Any(t => t.Title == "Aircraft and seat maps"));
    }

    [TestMethod]
    public void RefundRowsMatchTheRefundRules()
    {
        var refunds = HelpContent.PolicyRows().Where(r => r.Area == "Refunds").ToList();

        Assert.HasCount(3, refunds);
        StringAssert.StartsWith(refunds[0].Value, $"{AppData.RefundRate(DateTime.Today.AddDays(7)):P0}");
        StringAssert.StartsWith(refunds[1].Value, $"{AppData.RefundRate(DateTime.Today.AddDays(1)):P0}");
        StringAssert.StartsWith(refunds[2].Value, $"{AppData.RefundRate(DateTime.Today):P0}");
    }

    [TestMethod]
    public void PolicyRowsListTaxesAddOnsAndBaggage()
    {
        var rows = HelpContent.PolicyRows();

        Assert.IsTrue(rows.Any(r => r.Rule == "VAT" && r.Value.StartsWith($"{AppData.TaxRate:P0}")));
        Assert.IsTrue(rows.Any(r => r.Area == "Add-ons" && r.Rule == "Meal"));
        Assert.IsFalse(rows.Any(r => r.Area == "Add-ons" && r.Rule == AppData.AddonTax), "The Tax line is not an add-on a customer picks.");
        Assert.AreEqual(4, rows.Count(r => r.Area == "Baggage"));
        Assert.IsTrue(rows.All(r => r.Area.Length > 0 && r.Rule.Length > 0 && r.Value.Length > 0));
    }
}
