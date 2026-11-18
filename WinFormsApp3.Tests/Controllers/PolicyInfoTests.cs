using SkyWings.Controllers;

namespace SkyWings.Tests.Controllers;

[TestClass]
public sealed class PolicyInfoTests
{
    private static string Flatten(InfoPage page)
        => string.Join("\n", page.Sections.SelectMany(s => s.Rows.Select(r => $"{s.Heading}|{r.Rule}|{r.Value}")));

    [TestMethod]
    public void Pages_AreTheThreeFooterLinksInOrder()
    {
        CollectionAssert.AreEqual(new[] { "Flight policies", "Baggage guide", "Fare rules" },
            PolicyInfo.Pages().Select(p => p.Title).ToArray());
    }

    [TestMethod]
    public void EveryPage_HasIntroAndNonEmptySections()
    {
        foreach (InfoPage page in PolicyInfo.Pages())
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(page.Intro), page.Title);
            Assert.IsNotEmpty(page.Sections, page.Title);
            Assert.IsTrue(page.Sections.All(s => s.Heading.Length > 0 && s.Rows.Count > 0), page.Title);
            Assert.IsTrue(page.Sections.SelectMany(s => s.Rows).All(r => r.Rule.Length > 0 && r.Value.Length > 0), page.Title);
        }
    }

    [TestMethod]
    public void FlightPolicies_RefundTiersMatchTheRealRefundRule()
    {
        string text = Flatten(PolicyInfo.FlightPolicies());

        StringAssert.Contains(text, $"{AppData.RefundRate(DateTime.Today.AddDays(7)):P0} of the total");
        StringAssert.Contains(text, $"{AppData.RefundRate(DateTime.Today.AddDays(1)):P0} of the total");
        StringAssert.Contains(text, $"{AppData.RefundRate(DateTime.Today):P0} of the total");
        StringAssert.Contains(text, $"{AppData.CheckInWindowHours} hours before departure");
        StringAssert.Contains(text, $"{AppData.SeatHoldMinutes} minutes");
    }

    [TestMethod]
    public void BaggageGuide_HasASectionPerCabinAndTheExtras()
    {
        InfoPage page = PolicyInfo.BaggageGuide();

        foreach (string cabin in AppData.ClassNames)
            Assert.IsTrue(page.Sections.Any(s => s.Heading == cabin), cabin);
        Assert.IsTrue(page.Sections.Any(s => s.Heading == "Extras you can add"));
        StringAssert.Contains(Flatten(page), "PHP 3,000");   // pet fee
    }

    [TestMethod]
    public void FareRules_ShowCabinMultipliersTaxAndDiscounts()
    {
        string text = Flatten(PolicyInfo.FareRules());

        Assert.IsTrue(AppData.ClassNames.All(c => text.Contains(c)));
        StringAssert.Contains(text, $"{AppData.TaxRate:P0}");
        StringAssert.Contains(text, $"{AppData.DiscountRate:P0} off");
        StringAssert.Contains(text, $"{AppData.AdvancePurchaseDays}+ days ahead");
        StringAssert.Contains(text, "SKY10");
        StringAssert.Contains(text, "EARLY500");
    }
}
