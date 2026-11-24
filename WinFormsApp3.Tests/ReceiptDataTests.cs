using SkyWings;

namespace SkyWings.Tests;

[TestClass]
public class ReceiptDataTests
{
    private static readonly DateTime Booked = new(2026, 11, 18, 10, 30, 0);
    private static readonly Flight Out = new("PR-101", "Manila (MNL)", "Cebu (CEB)", 3000m);
    private static readonly Flight Back = new("PR-102", "Cebu (CEB)", "Manila (MNL)", 3000m);

    private static BookingSession Session(int travellers = 1, bool roundTrip = false, string className = "Business")
    {
        var s = new BookingSession { SelectedFlight = Out, FlightDate = new DateTime(2026, 12, 20), ClassName = className };
        if (roundTrip) { s.ReturnFlight = Back; s.ReturnDate = new DateTime(2026, 12, 27); }
        for (int i = 0; i < travellers; i++) s.Passengers.Add(new PassengerDraft { Name = $"Traveller {i + 1}" });
        return s;
    }

    private static BookingRecord Record(BookingSession s, string seat = "BUS1B", string returnSeat = "")
    {
        var passengers = s.Passengers.Select((p, i) => p.ToRecord(seat.Length > 0 ? $"{seat}{i}" : "", s.Addons.ToList(), 0m, returnSeat)).ToList();
        decimal total = FarePricing.Calculate(s, Booked).Total;
        return new BookingRecord("SW-TEST-001", Booked, Out.FlightNo, Out.Origin, Out.Destination, s.FlightDate, true,
            s.ClassName, "Debit Card", "···· 3523", total, "09:15", passengers, "Confirmed", "Paid", "TX-1", Booked, 0m)
        {
            ReturnFlightNo = s.ReturnFlight?.FlightNo, ReturnOrigin = s.ReturnFlight?.Origin,
            ReturnDestination = s.ReturnFlight?.Destination, ReturnFlightDateValue = s.ReturnDate,
            BaggageOption = s.BaggageOption, MealOption = s.MealOption
        };
    }

    [TestMethod]
    [DataRow("Manila (MNL)", "MNL", "Manila")]
    [DataRow("Tokyo Narita (NRT)", "NRT", "Tokyo Narita")]
    [DataRow("Cebu", "CEB", "Cebu")]
    public void SplitPlace_SeparatesCodeAndCity(string place, string code, string city)
        => Assert.AreEqual((code, city), ReceiptData.SplitPlace(place));

    [TestMethod]
    public void From_OneWay_MapsRouteTimesSeatAndReference()
    {
        BookingSession s = Session();
        ReceiptData d = ReceiptData.From(Record(s), s);

        Assert.AreEqual("SW-TEST-001", d.Reference);
        Assert.AreEqual("MNL", d.OriginCode);
        Assert.AreEqual("CEB", d.DestinationCode);
        Assert.AreEqual("Manila", d.OriginCity);
        Assert.AreEqual("PR-101", d.FlightNo);
        Assert.AreEqual("One-way", d.TripType);
        Assert.IsNull(d.Return);
        Assert.AreEqual(AppData.DepartureOf("PR-101", s.FlightDate), d.Departure);
        Assert.IsTrue(d.Arrival > d.Departure, "arrival must come after departure");
        Assert.AreEqual("Business · BUS1B0", d.ClassAndSeat);
        Assert.AreEqual("Traveller 1", d.Passengers);
        Assert.AreEqual("PAID", d.PaymentStatus.Split(' ')[0]);
        Assert.AreEqual("Debit Card ···· 3523", d.PaymentMethod);
    }

    [TestMethod]
    public void From_NoSeatChosen_ShowsClassOnly()
    {
        BookingSession s = Session();
        Assert.AreEqual("Business", ReceiptData.From(Record(s, seat: ""), s).ClassAndSeat);
    }

    [TestMethod]
    public void From_RoundTrip_AddsReturnLeg()
    {
        BookingSession s = Session(roundTrip: true);
        ReceiptData d = ReceiptData.From(Record(s, returnSeat: "BUS2A"), s);

        Assert.AreEqual("Round trip", d.TripType);
        Assert.IsNotNull(d.Return);
        Assert.AreEqual("PR-102", d.Return!.FlightNo);
        Assert.AreEqual(AppData.DepartureOf("PR-102", s.ReturnDate!.Value), d.Return.Departure);
        StringAssert.Contains(d.Return.Seats, "BUS2A");
    }

    [TestMethod]
    public void From_ManyTravellers_ListsEveryName()
    {
        BookingSession s = Session(travellers: 3);
        ReceiptData d = ReceiptData.From(Record(s), s);
        Assert.AreEqual("Traveller 1, Traveller 2, Traveller 3", d.Passengers);
    }

    [TestMethod]
    public void FareLines_AddUpToTotalPaid()
    {
        BookingSession s = Session(travellers: 2, className: "Premium Economy");
        s.BaggageOption = AppData.BaggageOptionsForClass(s.ClassName).Last();
        s.MealOption = AppData.MealOptionsForClass(s.ClassName).Last();
        BookingRecord record = Record(s);

        ReceiptData d = ReceiptData.From(record, s);

        decimal sum = d.FareLines.Sum(l => l.Discount ? -l.Amount : l.Amount);
        Assert.AreEqual(record.TotalAmount, sum, 0.02m, "fare lines must add up to the total paid");
        Assert.AreEqual($"PHP {record.TotalAmount:N2}", d.TotalPaid);
        Assert.IsTrue(d.FareLines.Any(l => l.Label == "Base fare"));
        Assert.IsTrue(d.FareLines.Any(l => l.Label.StartsWith("Taxes and fees")));
    }

    [TestMethod]
    public void FareLines_ShowEachAddOnByName_ForEveryTraveller()
    {
        BookingSession s = Session(travellers: 2);
        s.Addons.Add("Insurance");

        ReceiptData d = ReceiptData.From(Record(s), s);

        ReceiptLine insurance = d.FareLines.Single(l => l.Label.StartsWith("Insurance"));
        Assert.AreEqual(AppData.AddonPrices[AppData.GetAddonIndex("Insurance")] * 2, insurance.Amount);
    }


    [TestMethod]
    public void FromStored_RebuildsTheSameFareLinesAsTheLiveWizardSession()
    {
        BookingSession s = Session(travellers: 2, roundTrip: true, className: "Premium Economy");
        s.BaggageOption = AppData.BaggageOptionsForClass(s.ClassName).Last();
        s.MealOption = AppData.MealOptionsForClass(s.ClassName).Last();
        s.Addons.Add("Insurance");
        BookingRecord record = Record(s, returnSeat: "PRE2A");

        ReceiptData live = ReceiptData.From(record, s);
        ReceiptData stored = ReceiptData.FromStored(record, new[] { Out, Back });

        CollectionAssert.AreEqual(live.FareLines.ToList(), stored.FareLines.ToList());
        Assert.AreEqual(live.TotalPaid, stored.TotalPaid);
        Assert.AreEqual(live.Passengers, stored.Passengers);
    }

    [TestMethod]
    public void FromStored_SeniorsAndPromo_StillAddUpToTheTotal()
    {
        BookingSession s = Session(travellers: 2);
        s.Passengers[0].Senior = true;
        s.PromoCode = "EARLY500";
        BookingRecord record = Record(s);

        ReceiptData stored = ReceiptData.FromStored(record, new[] { Out, Back });

        decimal sum = stored.FareLines.Sum(l => l.Discount ? -l.Amount : l.Amount);
        Assert.AreEqual(record.TotalAmount, sum, 0.02m);
    }

    [TestMethod]
    public void FromStored_FlightNoLongerExists_FallsBackToOneHonestTotalLine()
    {
        BookingSession s = Session();
        BookingRecord record = Record(s);

        ReceiptData stored = ReceiptData.FromStored(record, Array.Empty<Flight>());

        ReceiptLine only = stored.FareLines.Single();
        Assert.AreEqual("Fare, add-ons and taxes", only.Label);
        Assert.AreEqual(record.TotalAmount, only.Amount);
        Assert.IsFalse(only.Discount);
    }

    [TestMethod]
    public void FromStored_PricesChangedSinceBooking_FallsBackInsteadOfShowingAWrongBreakdown()
    {
        BookingSession s = Session();
        BookingRecord record = Record(s);
        var dearerNow = new Flight(Out.FlightNo, Out.Origin, Out.Destination, Out.BasePrice + 1000m);

        ReceiptData stored = ReceiptData.FromStored(record, new[] { dearerNow, Back });

        Assert.HasCount(1, stored.FareLines);
        Assert.AreEqual(record.TotalAmount, stored.FareLines[0].Amount);
    }
    [TestMethod]
    public void FormattedMoney_UsesPhpWithTwoDecimals()
        => Assert.AreEqual("PHP 49,000.00", ReceiptLine.Money(49000m));
}
