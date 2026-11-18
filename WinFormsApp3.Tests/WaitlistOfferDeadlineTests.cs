using SkyWings;

namespace SkyWings.Tests;

/// <summary>How long a waitlist offer lasts: up to 24 hours, but never past the point where bookings close for the flight.</summary>
[TestClass]
public class WaitlistOfferDeadlineTests
{
    private static readonly DateTime Now = new(2026, 11, 18, 10, 0, 0);

    [TestMethod]
    public void DistantFlight_GetsTheFullOfferWindow()
        => Assert.AreEqual(Now.AddHours(AppData.WaitlistOfferHours), AppData.WaitlistOfferDeadline(Now, Now.AddDays(5)));

    [TestMethod]
    public void FlightSoonerThanTheWindow_IsCappedAtTheBookingCutoff()
    {
        DateTime departure = Now.AddHours(10);
        Assert.AreEqual(departure.AddHours(-AppData.BookingCutoffHours), AppData.WaitlistOfferDeadline(Now, departure));
    }

    [TestMethod]
    public void FlightExactlyWindowPlusCutoffAway_GetsTheFullWindow()
    {
        DateTime departure = Now.AddHours(AppData.WaitlistOfferHours + AppData.BookingCutoffHours);
        Assert.AreEqual(Now.AddHours(AppData.WaitlistOfferHours), AppData.WaitlistOfferDeadline(Now, departure));
    }

    [TestMethod]
    public void AtTheBookingCutoff_NoOfferCanBeMade()
        => Assert.IsNull(AppData.WaitlistOfferDeadline(Now, Now.AddHours(AppData.BookingCutoffHours)));

    [TestMethod]
    public void AfterTheBookingCutoff_OrAfterDeparture_NoOfferCanBeMade()
    {
        Assert.IsNull(AppData.WaitlistOfferDeadline(Now, Now.AddHours(1)));
        Assert.IsNull(AppData.WaitlistOfferDeadline(Now, Now.AddHours(-3)));
    }

    [TestMethod]
    public void OneMinuteBeforeTheCutoff_StillGetsAShortOffer()
    {
        DateTime departure = Now.AddHours(AppData.BookingCutoffHours).AddMinutes(1);
        Assert.AreEqual(Now.AddMinutes(1), AppData.WaitlistOfferDeadline(Now, departure));
    }
}
