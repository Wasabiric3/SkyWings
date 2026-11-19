using SkyWings;

namespace SkyWings.Tests;

[TestClass]
public sealed class BookingRulesTests
{
    [TestMethod]
    public void IsBookable_UntilTwoHoursBeforeDeparture_SameDayIncluded()
    {
        DateTime today = new(2027, 4, 5);
        DateTime departure = AppData.DepartureOf("PR-103", today);   // 1:00 PM
        DateTime cutoff = departure.AddHours(-AppData.BookingCutoffHours);

        Assert.AreEqual(cutoff, AppData.BookingCutoff("PR-103", today));
        Assert.IsTrue(AppData.IsBookable("PR-103", today, today.AddHours(8)), "same day, five hours ahead");
        Assert.IsTrue(AppData.IsBookable("PR-103", today, cutoff.AddMinutes(-1)));
        Assert.IsFalse(AppData.IsBookable("PR-103", today, cutoff), "closed at the cutoff");
        Assert.IsFalse(AppData.IsBookable("PR-103", today, departure.AddHours(1)));
        Assert.IsFalse(AppData.IsBookable("PR-103", today.AddDays(-1), today.AddHours(8)), "a past date");
        Assert.IsTrue(AppData.IsBookable("PR-103", today.AddDays(1), today.AddHours(23)));
    }

    [TestMethod]
    public void ComputeRefundAmount_UsesFlightDatePolicy()
    {
        decimal refund = AppData.ComputeRefundAmount(10_000m, DateTime.Today.AddDays(8));

        Assert.AreEqual(8_000, refund);
    }

    [TestMethod]
    public void BookingSession_TotalAppliesDiscountAndTax()
    {
        var session = new BookingSession { SelectedFlight = AppData.DomesticFlights[0] };
        session.Passengers.Add(new PassengerDraft { Name = "Senior", Senior = true });
        session.Passengers.Add(new PassengerDraft { Name = "Adult" });

        decimal expectedSubtotal = 2 * 3_500m;
        decimal expectedDiscount = 3_500m * AppData.DiscountRate;
        decimal expectedTax = (expectedSubtotal - expectedDiscount) * AppData.TaxRate;

        Assert.AreEqual(Math.Round(expectedSubtotal - expectedDiscount + expectedTax, 2), session.Total);
    }

    [TestMethod]
    public void BookingSession_AllEligiblePassengersAreTaxExempt()
    {
        var session = new BookingSession { SelectedFlight = AppData.DomesticFlights[0] };
        session.Passengers.Add(new PassengerDraft { Name = "Senior", Senior = true });
        session.Passengers.Add(new PassengerDraft { Name = "PWD", Pwd = true });

        Assert.AreEqual(0, session.TaxTotal);
    }

    [TestMethod]
    public void FarePricing_ChargesEconomyBaggageAndMealOptions()
    {
        var session = new BookingSession
        {
            SelectedFlight = AppData.DomesticFlights[0],
            FlightDate = new DateTime(2027, 2, 1),
            BaggageOption = "Add checked baggage (+PHP 1,500)",
            MealOption = "Standard meal (+PHP 500)"
        };
        session.Passengers.Add(new PassengerDraft { Name = "Adult" });

        FareQuote quote = FarePricing.Calculate(session, new DateTime(2027, 1, 1));

        Assert.AreEqual(2_000, quote.Addons);
        Assert.AreEqual(5_500, quote.TaxableAmount);
        Assert.AreEqual(6_160, quote.Total);
    }

    [TestMethod]
    public void FarePricing_AppliesPeakSurchargeAndAdvanceDiscountPerRoundTrip()
    {
        var session = new BookingSession
        {
            SelectedFlight = AppData.DomesticFlights[0],
            FlightDate = new DateTime(2027, 5, 1),
            ReturnFlight = new Flight("PR-501", "Cebu (CEB)", "Manila (MNL)", 3_500) { Domestic = true },
            ReturnDate = new DateTime(2027, 7, 1)
        };
        session.Passengers.Add(new PassengerDraft { Name = "Adult" });

        FareQuote quote = FarePricing.Calculate(session, new DateTime(2027, 1, 1));

        Assert.AreEqual(525, quote.SeasonalSurcharge);
        Assert.AreEqual(700, quote.AdvancePurchaseDiscount);
        Assert.AreEqual(7_644, quote.Total);
    }

    [TestMethod]
    public void FarePricing_Early500ReplacesAdvanceDiscount()
    {
        var session = new BookingSession
        {
            SelectedFlight = AppData.DomesticFlights[0],
            FlightDate = new DateTime(2027, 5, 1),
            ClassName = "Premium Economy",
            PromoCode = "EARLY500"
        };
        session.Passengers.Add(new PassengerDraft { Name = "Adult" });

        FareQuote quote = FarePricing.Calculate(session, new DateTime(2027, 1, 1));

        Assert.AreEqual(0, quote.AdvancePurchaseDiscount);
        Assert.AreEqual(500, quote.PromoDiscount);
        Assert.AreEqual(5_320, quote.Total);
    }

    [TestMethod]
    public void FarePricing_Sky10CannotStackWithAdvancePurchase()
    {
        var session = new BookingSession
        {
            SelectedFlight = AppData.DomesticFlights[0],
            FlightDate = new DateTime(2027, 5, 1),
            PromoCode = "SKY10"
        };
        session.Passengers.Add(new PassengerDraft { Name = "Adult" });

        FareQuote quote = FarePricing.Calculate(session, new DateTime(2027, 1, 1));

        Assert.AreEqual(0, quote.PromoDiscount);
        Assert.IsTrue(quote.PromoMessage.Contains("cannot be combined", StringComparison.OrdinalIgnoreCase));
    }
}
