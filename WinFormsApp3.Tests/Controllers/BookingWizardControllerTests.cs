using Microsoft.Data.Sqlite;
using SkyWings.Controllers;

namespace SkyWings.Tests.Controllers;

/// <summary>Controller tests run without any form, against a temporary SQLite file.</summary>
[TestClass]
public sealed class BookingWizardControllerTests
{
    private string _dbPath = "";
    private BookingStore _store = null!;
    private BookingWizardController _controller = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"skywings-ctl-{Guid.NewGuid():N}.db");
        var csb = new SqliteConnectionStringBuilder { DataSource = _dbPath, ForeignKeys = true };
        _store = new BookingStore(csb.ConnectionString);
        _controller = new BookingWizardController(_store);
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private static PassengerInput Input(string name = "Maria Santos", string birthday = "1990-04-23", string email = "",
        bool senior = false)
        => new(name, "1 Test St", "0917 123 4567", "PSP-0000001", birthday, email, senior, false);

    private void PrepareOneWayBooking(int travellers = 1)
    {
        List<Flight> flights = _controller.LoadFlights();
        _controller.Session.SelectedFlight = flights.First(f => f.FlightNo == "PR-101");
        _controller.Session.FlightDate = DateTime.Today.AddDays(30);
        for (int i = 0; i < travellers; i++)
            Assert.IsTrue(_controller.AddPassenger(Input($"Traveller {i}"), DateTime.Today).IsValid);
    }

    [TestMethod]
    public void LoadFlights_SeedsAndGroupsOutboundBeforeReturn()
    {
        List<Flight> flights = _controller.LoadFlights();

        Assert.IsNotEmpty(flights);
        string firstPlace = flights[0].Origin.StartsWith("Manila") ? flights[0].Destination : flights[0].Origin;
        Assert.IsTrue(flights[0].Origin.StartsWith("Manila"), $"first flight for {firstPlace} should leave Manila");
    }

    [TestMethod]
    public void DescribeFlight_ShowsOpenSeats()
    {
        Flight flight = _controller.LoadFlights().First(f => f.FlightNo == "PR-101");

        int economy = _store.LoadAircraft("PR-101").Cabin("Economy")!.Seats;

        string text = _controller.DescribeFlight(flight, DateTime.Today.AddDays(5), "Economy", DateTime.Now);

        StringAssert.StartsWith(text, "Departs ");
        StringAssert.Contains(text, $"{economy} of {economy} Economy seats open");
    }

    [TestMethod]
    public void AddPassenger_ValidInput_AddsToSessionAndKeepsEmail()
    {
        PassengerValidation result = _controller.AddPassenger(Input(email: "maria@example.com"), new DateTime(2026, 6, 1));

        Assert.IsTrue(result.IsValid);
        Assert.HasCount(1, _controller.Session.Passengers);
        Assert.AreEqual(36, _controller.Session.Passengers[0].Age);
        Assert.AreEqual("maria@example.com", _controller.Session.ContactEmail);
    }

    [TestMethod]
    public void AddPassenger_ReportsEachBadField()
    {
        var bad = new PassengerInput("", "", "abc", "", "not a date", "nope", false, false);

        PassengerValidation result = _controller.AddPassenger(bad, DateTime.Today);

        Assert.IsFalse(result.IsValid);
        Assert.IsNotNull(result.Name);
        Assert.IsNotNull(result.IdNumber);
        Assert.IsNotNull(result.Address);
        Assert.IsNotNull(result.Contact);
        Assert.IsNotNull(result.Email);
        Assert.IsNotNull(result.Birthday);
        Assert.IsEmpty(_controller.Session.Passengers);
    }

    [TestMethod]
    public void AddPassenger_RejectsDuplicateNameIgnoringCase()
    {
        _controller.AddPassenger(Input("Maria Santos"), DateTime.Today);

        PassengerValidation result = _controller.AddPassenger(Input("maria santos"), DateTime.Today);

        Assert.IsNotNull(result.Name);
        Assert.HasCount(1, _controller.Session.Passengers);
    }

    [TestMethod]
    public void AddPassenger_SeniorDiscountNeedsMinimumAge()
    {
        PassengerValidation result = _controller.AddPassenger(Input(birthday: "1990-04-23", senior: true), DateTime.Today);

        Assert.IsNotNull(result.Birthday);
        StringAssert.Contains(result.Birthday, AppData.SeniorMinimumAge.ToString());
    }

    [TestMethod]
    public void AddPassenger_StopsAtMaximumTravellers()
    {
        for (int i = 0; i < BookingRules.MaxPassengersPerBooking; i++)
            Assert.IsTrue(_controller.AddPassenger(Input($"Traveller {i}"), DateTime.Today).IsValid);

        PassengerValidation result = _controller.AddPassenger(Input("One Too Many"), DateTime.Today);

        Assert.IsNotNull(result.General);
        Assert.HasCount(BookingRules.MaxPassengersPerBooking, _controller.Session.Passengers);
    }

    [TestMethod]
    public void AddPassenger_RejectsControlCharacters()
    {
        PassengerValidation result = _controller.AddPassenger(Input("Maria\nSantos"), DateTime.Today);

        Assert.IsNotNull(result.General);
        Assert.IsEmpty(_controller.Session.Passengers);
    }

    [TestMethod]
    public void RemovePassenger_IgnoresBadIndex()
    {
        _controller.AddPassenger(Input(), DateTime.Today);

        Assert.IsFalse(_controller.RemovePassenger(-1));
        Assert.IsFalse(_controller.RemovePassenger(5));
        Assert.IsTrue(_controller.RemovePassenger(0));
        Assert.IsEmpty(_controller.Session.Passengers);
    }

    [TestMethod]
    public void HoldSeats_WrongCount_IsRejectedWithoutHolding()
    {
        PrepareOneWayBooking(2);

        SeatHoldResult result = _controller.HoldSeats(new[] { "ECO1A" }, Array.Empty<string>());

        Assert.AreEqual(SeatHoldOutcome.WrongSeatCount, result.Outcome);
        Assert.IsNull(_controller.HoldOwner);
    }

    [TestMethod]
    public void HoldSeats_AssignsSeatsToTravellersAndBlocksOthers()
    {
        PrepareOneWayBooking(2);

        SeatHoldResult result = _controller.HoldSeats(new[] { "ECO1A", "ECO1B" }, Array.Empty<string>());

        Assert.IsTrue(result.Succeeded);
        Assert.AreEqual("ECO1A", _controller.Session.SeatAssignments["Traveller 0"]);
        Assert.AreEqual("ECO1B", _controller.Session.SeatAssignments["Traveller 1"]);
        Assert.IsFalse(_store.AddSeatHold("PR-101", _controller.Session.FlightDate, "ECO1A", "someone-else"));
        Assert.IsTrue(_controller.IsReadyForPayment());
    }

    [TestMethod]
    public void HoldSeats_SeatTakenByOther_ReturnsSeatTakenAndReleasesAll()
    {
        PrepareOneWayBooking(2);
        Assert.IsTrue(_store.AddSeatHold("PR-101", _controller.Session.FlightDate, "ECO1B", "someone-else"));

        SeatHoldResult result = _controller.HoldSeats(new[] { "ECO1A", "ECO1B" }, Array.Empty<string>());

        Assert.AreEqual(SeatHoldOutcome.SeatTaken, result.Outcome);
        StringAssert.Contains(result.Message, "ECO1B");
        Assert.IsEmpty(_controller.Session.SeatAssignments);
        // ECO1A was released again, so another owner can take it.
        Assert.IsTrue(_store.AddSeatHold("PR-101", _controller.Session.FlightDate, "ECO1A", "someone-else"));
    }

    [TestMethod]
    public void HoldSeats_CanBeRepeatedWithoutCollidingWithOwnHolds()
    {
        PrepareOneWayBooking();
        Assert.IsTrue(_controller.HoldSeats(new[] { "ECO1A" }, Array.Empty<string>()).Succeeded);

        SeatHoldResult again = _controller.HoldSeats(new[] { "ECO1A" }, Array.Empty<string>());

        Assert.IsTrue(again.Succeeded);
    }

    [TestMethod]
    public void ReleaseSeatHolds_FreesSeatsAndForgetsOwner()
    {
        PrepareOneWayBooking();
        _controller.HoldSeats(new[] { "ECO1A" }, Array.Empty<string>());

        _controller.ReleaseSeatHolds();

        Assert.IsNull(_controller.HoldOwner);
        Assert.IsTrue(_store.AddSeatHold("PR-101", _controller.Session.FlightDate, "ECO1A", "someone-else"));
    }

    [TestMethod]
    public void SeatsHeldByOthers_ExcludesOwnHolds()
    {
        PrepareOneWayBooking();
        _controller.HoldSeats(new[] { "ECO1A" }, Array.Empty<string>());
        _store.AddSeatHold("PR-101", _controller.Session.FlightDate, "ECO1B", "someone-else");

        string[] others = _controller.SeatsHeldByOthers(_controller.Session.SelectedFlight!, _controller.Session.FlightDate).ToArray();

        CollectionAssert.AreEqual(new[] { "ECO1B" }, others);
    }

    [TestMethod]
    public void IsReadyForPayment_FalseUntilSeatsAssigned()
    {
        PrepareOneWayBooking();

        Assert.IsFalse(_controller.IsReadyForPayment());
    }

    [TestMethod]
    public void SubmitBooking_SavesMasksAccountAndReleasesHolds()
    {
        PrepareOneWayBooking();
        _controller.HoldSeats(new[] { "ECO1A" }, Array.Empty<string>());

        SubmitResult result = _controller.SubmitBooking("Credit Card", "4111111111114521");

        Assert.IsTrue(result.Succeeded, result.Error?.Message);
        BookingRecord booking = result.Booking!;
        StringAssert.StartsWith(booking.Reference, "SKY-");
        Assert.AreEqual("**** 4521", booking.MaskedAccount);
        Assert.AreEqual(_controller.Session.Total, booking.TotalAmount);
        Assert.AreEqual(booking.Reference, _controller.Session.BookingReference);
        Assert.IsTrue(_controller.BookingSubmitted);
        Assert.IsNull(_controller.HoldOwner);
        Assert.Contains("ECO1A", _store.LoadTakenSeats("PR-101", _controller.Session.FlightDate));
    }

    [TestMethod]
    public void SubmitBooking_Incomplete_Throws()
    {
        PrepareOneWayBooking();

        Assert.Throws<InvalidOperationException>(() => _controller.SubmitBooking("Credit Card", "4111111111114521"));
    }

    [TestMethod]
    public void SubmitBooking_ShortAccount_Throws()
    {
        PrepareOneWayBooking();
        _controller.HoldSeats(new[] { "ECO1A" }, Array.Empty<string>());

        Assert.Throws<ArgumentException>(() => _controller.SubmitBooking("Credit Card", "123"));
    }

    [TestMethod]
    public void BuildReviewText_ListsFlightTravellersAndSeats()
    {
        PrepareOneWayBooking();
        _controller.HoldSeats(new[] { "ECO1A" }, Array.Empty<string>());

        string text = _controller.BuildReviewText();

        StringAssert.Contains(text, "PR-101");
        StringAssert.Contains(text, "Traveller 0");
        StringAssert.Contains(text, "ECO1A");
    }

    // ------------------------------------------------------------------ same-day booking and seats open

    private Flight Pr103() => _controller.LoadFlights().First(f => f.FlightNo == "PR-103");   // departs 1:00 PM

    [TestMethod]
    public void FlightDateProblem_SameDayIsFine_UntilTwoHoursBeforeDeparture()
    {
        DateTime today = new(2027, 4, 5);
        Flight flight = Pr103();

        Assert.IsNull(_controller.FlightDateProblem(flight, today, today.AddHours(8)), "booking at 8 AM for the 1 PM flight");
        Assert.IsNull(_controller.FlightDateProblem(flight, today.AddDays(1), today.AddHours(20)));

        string? closed = _controller.FlightDateProblem(flight, today, today.AddHours(11).AddMinutes(30));
        Assert.AreEqual("Bookings for PR-103 on Apr 5 closed at 11:00 AM, 2 hours before departure. Choose a later flight or date.", closed);
    }

    [TestMethod]
    public void DescribeFlight_CountsSeatsOpenOnTheChosenDateAndCabin()
    {
        Flight flight = Pr103();
        DateTime day = DateTime.Today.AddDays(9);
        int economySeats = _store.LoadAircraft("PR-103").Cabin("Economy")!.Seats;
        var p = new PassengerRecord("Maria Santos", "1 St", "09171234567", 35, "1991-01-01", "P1234567A", false, false,
            new SeatAssignment("Maria Santos", "ECO3A"), Array.Empty<string>(), 4000m);
        var other = new BookingRecord("SKY-DESCRIBE0001", DateTime.Now, "PR-103", "Manila (MNL)", "Boracay (MPH)", day.AddDays(1), true,
            "Economy", "Credit Card", "**** 4521", 4000m, "Scheduled", new[] { p }, AppData.StatusConfirmed, "PAID", "TXN-X", DateTime.Now, 0m);
        _store.SaveConfirmedBooking(other, false, 4000m, "Credit Card", "**** 4521", "TXN-X", DateTime.Now);   // another date

        string text = _controller.DescribeFlight(flight, day, "Economy", DateTime.Today);

        StringAssert.Contains(text, $"{economySeats} of {economySeats} Economy seats open");
        StringAssert.Contains(_controller.DescribeFlight(flight, day.AddDays(1), "Economy", DateTime.Today),
            $"{economySeats - 1} of {economySeats} Economy seats open");
    }

    [TestMethod]
    public void DescribeFlight_SaysWhenBookingsHaveClosed()
    {
        DateTime today = new(2027, 4, 5);
        StringAssert.Contains(_controller.DescribeFlight(Pr103(), today, "Economy", today.AddHours(12)), "bookings closed");
    }

    // ---- the structured review rows (the wizard's Booking summary card) ----

    /// <summary>The review text as it was written before <c>ReviewRows</c> existed; the confirmation dialog still shows this text.</summary>
    private static string ReferenceReviewText(BookingSession s)
    {
        var lines = new List<string>
        {
            $"{s.SelectedFlight?.Origin} → {s.SelectedFlight?.Destination}   ·   {s.SelectedFlight?.FlightNo}   ·   {s.FlightDate:ddd, MMM d, yyyy}"
        };
        if (s.ReturnFlight is not null)
            lines.Add($"{s.ReturnFlight.Origin} → {s.ReturnFlight.Destination}   ·   {s.ReturnFlight.FlightNo}   ·   {s.ReturnDate:ddd, MMM d, yyyy}");
        lines.Add("");
        foreach (var p in s.Passengers)
        {
            string seat = s.SeatAssignments.GetValueOrDefault(p.Name, "—");
            string back = s.ReturnFlight is null ? "" : $"  ·  return {s.ReturnSeatAssignments.GetValueOrDefault(p.Name, "—")}";
            lines.Add($"{p.Name}   ·   seat {seat}{back}");
        }
        lines.Add("");
        lines.Add($"{s.ClassName}   ·   {s.BaggageOption}   ·   {s.MealOption}");
        return string.Join(Environment.NewLine, lines);
    }

    [TestMethod]
    public void BuildReviewText_IsUnchanged_ForOneWayAndRoundTrip()
    {
        PrepareOneWayBooking(2);
        _controller.HoldSeats(new[] { "ECO1A", "ECO1B" }, Array.Empty<string>());
        Assert.AreEqual(ReferenceReviewText(_controller.Session), _controller.BuildReviewText());

        MakeRoundTrip();
        _controller.HoldSeats(new[] { "ECO1A", "ECO1B" }, new[] { "ECO2A", "ECO2B" });
        Assert.AreEqual(ReferenceReviewText(_controller.Session), _controller.BuildReviewText());
    }

    private void MakeRoundTrip()
    {
        Flight outbound = _controller.Session.SelectedFlight!;
        _controller.Session.ReturnFlight = _controller.LoadFlights().First(f => f.Origin == outbound.Destination && f.Destination == outbound.Origin);
        _controller.Session.ReturnDate = _controller.Session.FlightDate.AddDays(3);
    }

    [TestMethod]
    public void ReviewRows_OneWay_AreFlightDepartureTravellerCabinBaggageMealSeat()
    {
        PrepareOneWayBooking();
        _controller.HoldSeats(new[] { "ECO1A" }, Array.Empty<string>());
        var rows = _controller.ReviewRows();
        BookingSession s = _controller.Session;

        CollectionAssert.AreEqual(new[] { "Flight", "Departure", "Traveller", "Cabin", "Baggage", "Meal", "Selected seat" }, rows.Select(r => r.Label).ToArray());
        static string Code(string place) => System.Text.RegularExpressions.Regex.Match(place, @"\((.+)\)").Groups[1].Value;
        Assert.AreEqual($"PR-101 · {Code(s.SelectedFlight!.Origin)} → {Code(s.SelectedFlight.Destination)}", rows[0].Value);
        Assert.IsLessThan(24, rows[0].Value.Length, "short enough to fit the summary card");
        StringAssert.StartsWith(rows[1].Value, s.FlightDate.ToString("MMM d, yyyy"));
        Assert.AreEqual("Traveller 0", rows[2].Value);
        Assert.AreEqual(s.ClassName, rows[3].Value);
        Assert.AreEqual(s.BaggageOption, rows[4].Value);
        Assert.AreEqual(s.MealOption, rows[5].Value);
        Assert.AreEqual("ECO1A", rows[6].Value);
    }

    [TestMethod]
    public void ReviewRows_SeveralTravellers_AreNumbered_AndTheirSeatsListed()
    {
        PrepareOneWayBooking(2);
        _controller.HoldSeats(new[] { "ECO1A", "ECO1B" }, Array.Empty<string>());
        var rows = _controller.ReviewRows();
        CollectionAssert.AreEqual(new[] { "Flight", "Departure", "Traveller 1", "Traveller 2", "Cabin", "Baggage", "Meal", "Selected seats" }, rows.Select(r => r.Label).ToArray());
        Assert.AreEqual("ECO1A, ECO1B", rows[^1].Value);
    }

    [TestMethod]
    public void ReviewRows_RoundTrip_AddsTheReturnRows()
    {
        PrepareOneWayBooking();
        MakeRoundTrip();
        _controller.HoldSeats(new[] { "ECO1A" }, new[] { "ECO2A" });
        var rows = _controller.ReviewRows();
        CollectionAssert.AreEqual(new[] { "Flight", "Departure", "Return", "Return departure", "Traveller", "Cabin", "Baggage", "Meal", "Selected seat", "Return seat" },
            rows.Select(r => r.Label).ToArray());
        Assert.AreEqual("ECO2A", rows[^1].Value);
    }

    [TestMethod]
    public void ReviewRows_ASeatNotPickedYet_ShowsADash()
    {
        PrepareOneWayBooking();
        Assert.AreEqual("—", _controller.ReviewRows().Last(r => r.Label == "Selected seat").Value);
    }
}
