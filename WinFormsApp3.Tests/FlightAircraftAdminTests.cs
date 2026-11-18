using Microsoft.Data.Sqlite;

namespace SkyWings.Tests;

/// <summary>Staff can change a flight's aircraft, unless real booked seats would no longer exist.</summary>
[TestClass]
public sealed class FlightAircraftAdminTests
{
    private const string GoodPassword = "a long enough password 1!";
    private string _dbPath = "";
    private BookingStore _store = null!;
    private AdminService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"skywings-adm-{Guid.NewGuid():N}.db");
        _store = new BookingStore(new SqliteConnectionStringBuilder { DataSource = _dbPath, ForeignKeys = true }.ConnectionString);
        _store.SeedFlightsIfEmpty(AppData.DomesticFlights, AppData.InternationalFlights, 180, 220, assignAircraft: true);
        _service = ServiceFor(StaffRole.Admin, "boss");
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private AdminService ServiceFor(StaffRole role, string username)
    {
        StaffAccount staff = _store.CreateStaff(username, username, GoodPassword, role, false);
        return new AdminService(_store, new StaffSession(staff.Id, staff.Username, staff.DisplayName, staff.Role, false));
    }

    private void Book(string reference, string flightNo, string seat, string cabinClass)
    {
        var passenger = new PassengerRecord("Maria Santos", "1 St", "0917", 35, "1991-01-01", "PSP-1", false, false,
            new SeatAssignment("Maria Santos", seat), Array.Empty<string>(), 4000m);
        var booking = new BookingRecord(reference, DateTime.Now, flightNo, "Manila (MNL)", "London (LHR)", DateTime.Today.AddDays(30),
            false, cabinClass, "Credit Card", "**** 4521", 4000m, "Scheduled", new[] { passenger },
            AppData.StatusConfirmed, "PAID", "TXN-" + reference, DateTime.Now, 0m);
        _store.SaveConfirmedBooking(booking, false, 4000m, "Credit Card", "**** 4521", booking.PaymentTransactionRef, DateTime.Now);
    }

    [TestMethod]
    public void WithNoBookings_TheAircraftCanBeChanged_AndTheChangeIsAudited()
    {
        AircraftChangeResult result = _service.ChangeAircraft("PR-301", "A330");

        Assert.IsTrue(result.Ok, result.Message);
        Assert.AreEqual("A330", _store.LoadAircraft("PR-301").Id);
        Assert.AreEqual(Aircraft.A330.TotalSeats, _store.LoadAllFlights().Single(f => f.FlightNo == "PR-301").Capacity);
        Assert.IsTrue(_store.LoadAudit().Any(e => e.Action == "FLIGHT_AIRCRAFT_CHANGED" && e.Target == "PR-301"));
    }

    [TestMethod]
    public void ABookedSeatThatWouldNotExist_BlocksTheChange_AndNamesIt()
    {
        Book("SKY-AC0000000001", "PR-301", "FST1A", "First Class");   // the 777 has a First cabin, an A330 does not

        AircraftChangeResult result = _service.ChangeAircraft("PR-301", "A330");

        Assert.IsFalse(result.Ok);
        StringAssert.Contains(result.Message, "FST1A");
        Assert.AreEqual("B777", _store.LoadAircraft("PR-301").Id, "nothing changes when the check fails");
    }

    [TestMethod]
    public void BookedSeatsThatExistOnTheNewAircraft_DoNotBlock()
    {
        Book("SKY-AC0000000002", "PR-301", "ECO1A", "Economy");

        Assert.IsTrue(_service.ChangeAircraft("PR-301", "A330").Ok);
    }

    [TestMethod]
    public void ClearingTheAircraft_IsAllowed_AndRestoresTheLegacyLayout()
    {
        Assert.IsTrue(_service.ChangeAircraft("PR-101", null).Ok);
        Assert.AreEqual(Aircraft.LegacyId, _store.LoadAircraft("PR-101").Id);
    }

    [TestMethod]
    public void UnknownAircraftOrFlight_IsRejectedWithoutChangingAnything()
    {
        Assert.IsFalse(_service.ChangeAircraft("PR-101", "NOPE").Ok);
        Assert.IsFalse(_service.ChangeAircraft("ZZ-999", "A320").Ok);
        Assert.AreEqual("A320", _store.LoadAircraft("PR-101").Id);
    }

    [TestMethod]
    public void AgentsCannotChangeAircraft()
    {
        AdminService agent = ServiceFor(StaffRole.Agent, "agent9");
        Assert.ThrowsExactly<UnauthorizedAccessException>(() => agent.ChangeAircraft("PR-101", "B777"));
        Assert.AreEqual("A320", _store.LoadAircraft("PR-101").Id);
    }
}
