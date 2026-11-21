using SkyWings.Controllers;

namespace SkyWings.Tests;

[TestClass]
public sealed class PaymentLabelTests
{
    [TestMethod]
    public void PaidNoShow_ReadsNotUsed_EverythingElseIsUnchanged()
    {
        Assert.AreEqual("PAID · not used", BookingSummary.PaymentLabel("PAID", TripPhase.NoShow));
        Assert.AreEqual("PAID", BookingSummary.PaymentLabel("PAID", TripPhase.Completed));
        Assert.AreEqual("PAID", BookingSummary.PaymentLabel("PAID", TripPhase.InFlight));
        Assert.AreEqual("REFUNDED", BookingSummary.PaymentLabel("REFUNDED", TripPhase.Canceled));
        Assert.AreEqual("REFUNDED", BookingSummary.PaymentLabel("REFUNDED", TripPhase.NoShow));
    }

    [TestMethod]
    public void TheLabelHasItsOwnPillColour_DifferentFromPaid()
    {
        var notUsed = SkyWings.UI.GridStyle.StatusStyle("PAID · not used");
        Assert.IsNotNull(notUsed);
        Assert.AreNotEqual(SkyWings.UI.GridStyle.StatusStyle("PAID"), notUsed);
    }
}
