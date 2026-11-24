using Microsoft.Data.Sqlite;
using SkyWings.Controllers;

namespace SkyWings.Tests;

/// <summary>Changing the outbound flight of a booking: price, seats, refusals, and that a refused change leaves nothing behind.</summary>
[TestClass]
[DoNotParallelize]
public sealed class FlightChangeTests
{
    // Outside the peak season (mid June to August, mid December to mid January), so the fares compare simply.
    private static readonly DateTime Day1 = new(2030, 3, 5);
    private static readonly DateTime Day2 = new(2030, 3, 12);

    private string _dbPath = "";
    private string _connection = "";
    private BookingStore _store = null!;

    [TestInitialize]
    public void Setup()
    {
        LookupGuard.Reset();
        _dbPath = Path.Combine(Path.GetTempPath(), $"skywings-change-{Guid.NewGuid():N}.db");
        _connection = new SqliteConnectionStringBuilder { DataSource = _dbPath, ForeignKeys = true, Pooling = false }.ConnectionString;
        _store = new BookingStore(_connection);
        _store.SeedFlightsIfEmpty(AppData.DomesticFlights, AppData.InternationalFlights,
            AppData.DefaultDomesticCapacity, AppData.DefaultInternationalCapacity);
        // A dearer flight on the same route as PR-101 (Manila to Cebu).
        _store.SaveFlight("PR-190", "Manila (MNL)", "Cebu (CEB)", 5_000m, true, AppData.DefaultDomesticCapacity, true, new TimeOnly(9, 0));
    }

    [TestCleanup]
    public void Cleanup()
    {
        LookupGuard.Reset();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private BookingRecord Save(string reference, DateTime date, int travellers = 1, string cabin = "Economy", string seatPrefix = "ECO",
        bool senior = false, string? returnFlightNo = null, DateTime? returnDate = null)
    {
        var people = Enumerable.Range(0, travellers).Select(i => new PassengerRecord($"Traveller {i + 1}", "1 Test St", "09171234567", 35,
            "1991-01-01", $"PSP-000000{i}", senior, false, new SeatAssignment($"Traveller {i + 1}", $"{seatPrefix}{i + 1}A"),
            Array.Empty<string>(), 4_000m)).ToArray();
        var booking = new BookingRecord(reference, DateTime.Now, "PR-101", "Manila (MNL)", "Cebu (CEB)", date, true, cabin,
            "Credit Card", "**** 4521", 4_000m * travellers, "Scheduled", people, AppData.StatusConfirmed, "PAID", "TXN-" + reference, DateTime.Now, 0m)
        {
            ReturnFlightNo = returnFlightNo,
            ReturnOrigin = returnFlightNo is null ? null : "Cebu (CEB)",
            ReturnDestination = returnFlightNo is null ? null : "Manila (MNL)",
            ReturnFlightDateValue = returnDate,
            ReturnDomestic = returnFlightNo is not null
        };
        _store.SaveConfirmedBooking(booking, false, booking.TotalAmount, "Credit Card", "**** 4521", booking.PaymentTransactionRef, DateTime.Now);
        return booking;
    }

    // ---------------------------------------------------------------- the price

    [TestMethod]
    public void Quote_SameFare_ChargesOnlyTheFeePerTraveller()
    {
        BookingRecord booking = Save("SKY-CHG000001", Day1, travellers: 2);
        var pr101 = new Flight("PR-101", "Manila (MNL)", "Cebu (CEB)", 3_500m);

        ChangeQuote quote = FlightChangeRules.Quote(booking, pr101, pr101, Day2);

        Assert.AreEqual(0m, quote.FareIncrease);
        Assert.AreEqual(0m, quote.Tax);
        Assert.AreEqual(1_000m, quote.Fee, "PHP 500 for each of the two travellers");
        Assert.AreEqual(1_000m, quote.Total);
    }

    [TestMethod]
    public void Quote_DearerFlight_ChargesTheIncreaseWithTax_AndACheaperOneIsNotRefunded()
    {
        BookingRecord booking = Save("SKY-CHG000002", Day1, travellers: 2);
        var pr101 = new Flight("PR-101", "Manila (MNL)", "Cebu (CEB)", 3_500m);
        var pr190 = new Flight("PR-190", "Manila (MNL)", "Cebu (CEB)", 5_000m);

        ChangeQuote dearer = FlightChangeRules.Quote(booking, pr101, pr190, Day2);
        Assert.AreEqual(3_000m, dearer.FareIncrease, "(5,000 - 3,500) x 2 travellers");
        Assert.AreEqual(360m, dearer.Tax, "12% of the increase");
        Assert.AreEqual(1_000m, dearer.Fee);
        Assert.AreEqual(4_360m, dearer.Total);

        ChangeQuote cheaper = FlightChangeRules.Quote(booking, pr190, pr101, Day2);
        Assert.AreEqual(0m, cheaper.FareIncrease, "a cheaper flight is not refunded");
        Assert.AreEqual(1_000m, cheaper.Total);
    }

    [TestMethod]
    public void Quote_SeniorsOnly_PayNoTaxOnTheIncrease()
    {
        BookingRecord booking = Save("SKY-CHG000003", Day1, senior: true);
        ChangeQuote quote = FlightChangeRules.Quote(booking, new Flight("PR-101", "Manila (MNL)", "Cebu (CEB)", 3_500m),
            new Flight("PR-190", "Manila (MNL)", "Cebu (CEB)", 5_000m), Day2);
        Assert.AreEqual(1_500m, quote.FareIncrease);
        Assert.AreEqual(0m, quote.Tax);
    }

    [TestMethod]
    public void ListFare_AddsThePeakSurchargeAndTheCabinMultiplier()
    {
        var flight = new Flight("PR-101", "Manila (MNL)", "Cebu (CEB)", 1_000m);
        Assert.AreEqual(1_000m, FlightChangeRules.ListFare(flight, "Economy", Day1));
        Assert.AreEqual(2_500m, FlightChangeRules.ListFare(flight, "Business", Day1));
        Assert.AreEqual(1_150m, FlightChangeRules.ListFare(flight, "Economy", new DateTime(2030, 7, 10)), "15% peak-season surcharge");
    }

    // ---------------------------------------------------------------- the change itself

    [TestMethod]
    public void Change_MovesTheBooking_ChargesIt_FreesTheOldSeats_AndRecordsThePayment()
    {
        BookingRecord booking = Save("SKY-CHG000010", Day1, travellers: 2);
        Assert.Contains("ECO1A", _store.LoadTakenSeats("PR-101", Day1));

        FlightChangeResult result = _store.ChangeOutboundFlight(booking.Reference, "PR-190", Day2, "agent1");

        Assert.AreEqual(FlightChangeOutcome.Changed, result.Outcome, result.Message);
        BookingRecord after = _store.FindByReference(booking.Reference)!;
        Assert.AreEqual("PR-190", after.FlightNo);
        Assert.AreEqual(Day2, after.FlightDateValue);
        Assert.AreEqual(8_000m + 4_360m, after.TotalAmount);
        Assert.AreEqual(AppData.StatusConfirmed, after.Status);
        Assert.AreEqual(8_000m + 4_360m, after.Passengers.Sum(p => p.TotalAmount), "the travellers' shares still add up to the total");
        CollectionAssert.AreEqual(new[] { "ECO1A", "ECO1B" }, after.Passengers.Select(p => p.SeatCode).ToArray(), "first free seats of the cabin");
        Assert.DoesNotContain("ECO1A", _store.LoadTakenSeats("PR-101", Day1), "the old seats are free again");
        Assert.Contains("ECO1A", _store.LoadTakenSeats("PR-190", Day2));

        using var con = new SqliteConnection(_connection);
        con.Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT amount, detail_message FROM booking_payments WHERE booking_ref = $r AND transaction_ref LIKE 'CHG-%'";
        cmd.Parameters.AddWithValue("$r", booking.Reference);
        using var rs = cmd.ExecuteReader();
        Assert.IsTrue(rs.Read(), "the charge is its own payment row");
        Assert.AreEqual(4_360m, rs.GetDecimal(0));
        StringAssert.Contains(rs.GetString(1), "agent1");
        StringAssert.Contains(rs.GetString(1), "PR-101");
    }

    [TestMethod]
    public void Change_OtherDateOnTheSameFlight_KeepsTheFlight()
    {
        BookingRecord booking = Save("SKY-CHG000011", Day1);
        FlightChangeResult result = _store.ChangeOutboundFlight(booking.Reference, "PR-101", Day2, "agent1");
        Assert.AreEqual(FlightChangeOutcome.Changed, result.Outcome, result.Message);
        BookingRecord after = _store.FindByReference(booking.Reference)!;
        Assert.AreEqual("PR-101", after.FlightNo);
        Assert.AreEqual(Day2, after.FlightDateValue);
        Assert.AreEqual(4_500m, after.TotalAmount, "only the fee");
    }

    [TestMethod]
    public void Change_RefusalsLeaveTheBookingExactlyAsItWas()
    {
        BookingRecord booking = Save("SKY-CHG000012", Day1, travellers: 2);

        // The flight already booked, another route, an unknown flight.
        Assert.AreEqual(FlightChangeOutcome.Refused, _store.ChangeOutboundFlight(booking.Reference, "PR-101", Day1, "a").Outcome);
        Assert.AreEqual(FlightChangeOutcome.Refused, _store.ChangeOutboundFlight(booking.Reference, "PR-102", Day2, "a").Outcome, "Manila to Davao is another route");
        Assert.AreEqual(FlightChangeOutcome.Refused, _store.ChangeOutboundFlight(booking.Reference, "PR-999", Day2, "a").Outcome);
        // A date that has already gone.
        Assert.AreEqual(FlightChangeOutcome.Refused, _store.ChangeOutboundFlight(booking.Reference, "PR-190", DateTime.Today.AddDays(-1), "a").Outcome);
        Assert.AreEqual(FlightChangeOutcome.Refused, _store.ChangeOutboundFlight("SKY-NOSUCHBOOK", "PR-190", Day2, "a").Outcome);

        BookingRecord after = _store.FindByReference(booking.Reference)!;
        Assert.AreEqual("PR-101", after.FlightNo);
        Assert.AreEqual(Day1, after.FlightDateValue);
        Assert.AreEqual(8_000m, after.TotalAmount);
        CollectionAssert.AreEqual(new[] { "ECO1A", "ECO2A" }, after.Passengers.Select(p => p.SeatCode).ToArray());
    }

    [TestMethod]
    public void Change_IsRefusedAfterCheckIn_AndForCanceledBookings()
    {
        BookingRecord checkedIn = Save("SKY-CHG000013", Day1);
        Assert.IsTrue(_store.CheckInPassenger(checkedIn.Reference, "Traveller 1", "PR-101", Day1, "ECO1A", AppData.DepartureOf("PR-101", Day1).AddHours(-2)));
        Assert.IsTrue(_store.HasOutboundCheckIn(checkedIn.Reference));
        FlightChangeResult refused = _store.ChangeOutboundFlight(checkedIn.Reference, "PR-190", Day2, "a");
        Assert.AreEqual(FlightChangeOutcome.Refused, refused.Outcome);
        StringAssert.Contains(refused.Message, "checked in");

        BookingRecord canceled = Save("SKY-CHG000014", Day1);
        Assert.IsTrue(_store.CancelBooking(canceled.Reference, 100m));
        Assert.AreEqual(FlightChangeOutcome.Refused, _store.ChangeOutboundFlight(canceled.Reference, "PR-190", Day2, "a").Outcome);
    }

    [TestMethod]
    public void Change_IsRefusedWhenTheNewFlightHasTooFewSeatsInTheCabin()
    {
        // A tiny aircraft: fill every Economy seat on PR-190 that day, then try to move in.
        BookingRecord mover = Save("SKY-CHG000015", Day1);
        Aircraft aircraft = _store.LoadAircraft("PR-190");
        CabinLayout cabin = aircraft.Cabin("Economy")!;
        int filler = 0;
        var seats = new List<string>();
        for (int row = 1; row <= cabin.Rows; row++)
            foreach (char letter in cabin.Letters) seats.Add(cabin.Code(row, letter));
        foreach (string[] group in seats.Chunk(9))   // a booking holds at most 9 travellers
        {
            var people = group.Select((s, i) => new PassengerRecord($"Filler {filler}-{i}", "x", "09171234567", 30, "1990-01-01", "ID" + filler + i,
                false, false, new SeatAssignment("f", s), Array.Empty<string>(), 100m)).ToArray();
            var full = new BookingRecord($"SKY-FILL{filler++:D8}", DateTime.Now, "PR-190", "Manila (MNL)", "Cebu (CEB)", Day2, true, "Economy",
                "Credit Card", "**** 1", 100m * people.Length, "Scheduled", people, AppData.StatusConfirmed, "PAID", "T", DateTime.Now, 0m);
            _store.SaveConfirmedBooking(full, false, full.TotalAmount, "Credit Card", "**** 1", "T", DateTime.Now);
        }
        Assert.AreEqual(0, _store.CabinSeatsLeft("PR-190", Day2, "Economy"));

        FlightChangeResult result = _store.ChangeOutboundFlight(mover.Reference, "PR-190", Day2, "a");

        Assert.AreEqual(FlightChangeOutcome.Refused, result.Outcome);
        StringAssert.Contains(result.Message, "seats");
        Assert.AreEqual("PR-101", _store.FindByReference(mover.Reference)!.FlightNo);
    }

    [TestMethod]
    public void Change_OnARoundTrip_MustLeaveBeforeTheReturnFlight()
    {
        _store.SaveFlight("PR-191", "Cebu (CEB)", "Manila (MNL)", 3_500m, true, AppData.DefaultDomesticCapacity, true, new TimeOnly(9, 0));
        BookingRecord trip = Save("SKY-CHG000016", Day1, returnFlightNo: "PR-191", returnDate: Day1.AddDays(3));

        FlightChangeResult late = _store.ChangeOutboundFlight(trip.Reference, "PR-190", Day1.AddDays(5), "a");
        Assert.AreEqual(FlightChangeOutcome.Refused, late.Outcome);
        StringAssert.Contains(late.Message, "before the return");

        FlightChangeResult fine = _store.ChangeOutboundFlight(trip.Reference, "PR-190", Day1.AddDays(2), "a");
        Assert.AreEqual(FlightChangeOutcome.Changed, fine.Outcome, fine.Message);
        Assert.AreEqual("PR-191", _store.FindByReference(trip.Reference)!.ReturnFlightNo, "the return leg is untouched");
    }

    // ---------------------------------------------------------------- the screen's controller

    [TestMethod]
    public void Controller_ListsTheSameRouteWithCostAndReasons_AndChangesAndAudits()
    {
        BookingRecord booking = Save("SKY-CHG000020", Day1);
        var controller = new ManageBookingController(_store);
        Assert.IsTrue(controller.Find(booking.Reference).Found);
        Assert.IsNull(controller.ChangeFlightRefusal(DateTime.Now));

        List<FlightChangeOption> options = controller.ChangeOptions(Day2, DateTime.Now);
        CollectionAssert.IsSubsetOf(new[] { "PR-101", "PR-190" }, options.Select(o => o.Flight.FlightNo).ToArray());
        Assert.IsTrue(options.All(o => o.Flight.Origin == "Manila (MNL)" && o.Flight.Destination == "Cebu (CEB)"), "only the booked route");
        Assert.IsTrue(options.All(o => o.Possible));
        FlightChangeOption dearer = options.Single(o => o.Flight.FlightNo == "PR-190");
        Assert.AreEqual(1_500m + 180m + 500m, dearer.Quote.Total);

        Assert.IsFalse(controller.ChangeOptions(Day1, DateTime.Now).Single(o => o.Flight.FlightNo == "PR-101").Possible, "the flight already booked");

        FlightChangeResult result = controller.ChangeFlight(dearer, Day2);
        Assert.AreEqual(FlightChangeOutcome.Changed, result.Outcome, result.Message);
        Assert.IsTrue(_store.LoadAudit().Any(a => a.Action == "BOOKING_REBOOKED" && a.Target == booking.Reference));
        Assert.AreEqual("PR-190", controller.Reload()!.FlightNo);
    }

    [TestMethod]
    public void Controller_ChangeFlightRefusal_ExplainsWhy()
    {
        var controller = new ManageBookingController(_store);
        Assert.IsNotNull(controller.ChangeFlightRefusal(DateTime.Now), "no booking open");
        BookingRecord booking = Save("SKY-CHG000021", Day1);
        controller.Find(booking.Reference);
        Assert.IsNull(controller.ChangeFlightRefusal(DateTime.Now));
        Assert.IsNotNull(controller.ChangeFlightRefusal(Day1.AddDays(1)), "the flight has left");
    }
}
