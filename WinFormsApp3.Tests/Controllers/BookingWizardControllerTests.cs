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

        string text = _controller.DescribeFlight(flight);

        StringAssert.StartsWith(text, "Departs ");
        StringAssert.Contains(text, $"{AppData.DefaultDomesticCapacity} of {AppData.DefaultDomesticCapacity} seats open");
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
}
