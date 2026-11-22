using Microsoft.Data.Sqlite;
using SkyWings.Controllers;

namespace SkyWings.Tests;

[TestClass]
[DoNotParallelize]   // its cleanup clears the SQLite connection pools
public sealed class RecentBookingsServiceTests
{
    private string _path = "";
    private BookingStore _store = null!;
    private AdminService _service = null!;

    [TestInitialize]
    public void Setup()
    {
        _path = Path.Combine(Path.GetTempPath(), $"skywings-recentsvc-{Guid.NewGuid():N}.db");
        _store = new BookingStore(new SqliteConnectionStringBuilder { DataSource = _path, ForeignKeys = true }.ConnectionString);
        _store.SeedFlightsIfEmpty(AppData.DomesticFlights, AppData.InternationalFlights, AppData.DefaultDomesticCapacity, AppData.DefaultInternationalCapacity);
        _service = new AdminService(_store, new StaffSession(1, "admin", "Admin", StaffRole.SuperAdmin, false));
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_path)) File.Delete(_path);
    }

    private void Book(string reference, DateTime bookedAt, DateTime flightDate, string seat)
    {
        var person = new PassengerRecord("Ana Example", "1 St", "09171234567", 35, "1991-01-01", "P1234567A", false, false,
            new SeatAssignment("Ana Example", seat), Array.Empty<string>(), 4000m);
        var booking = new BookingRecord(reference, bookedAt, "PR-101", "Manila (MNL)", "Cebu (CEB)", flightDate, true,
            "Economy", "Credit Card", "**** 4521", 4000m, "Scheduled", new[] { person }, AppData.StatusConfirmed, "PAID", "TXN-" + reference, bookedAt, 0m);
        _store.SaveConfirmedBooking(booking, false, 4000m, "Credit Card", "**** 4521", "TXN-" + reference, bookedAt);
    }

    [TestMethod]
    public void LoadRecentBookings_GivesTheNewestBookings_AndHowManyThereWereInAll()
    {
        DateTime now = new(2026, 11, 22, 12, 0, 0);
        Book("SKY-SERVICE00001", now.AddHours(-5), new DateTime(2030, 3, 1), "ECO1A");
        Book("SKY-SERVICE00002", now.AddHours(-3), new DateTime(2030, 3, 2), "ECO1B");
        Book("SKY-SERVICE00003", now.AddHours(-1), new DateTime(2030, 3, 3), "ECO1C");
        var (bookings, total) = _service.LoadRecentBookings(now.AddDays(-1), 2);
        CollectionAssert.AreEqual(new[] { "SKY-SERVICE00003", "SKY-SERVICE00002" }, bookings.Select(b => b.Reference).ToArray());
        Assert.AreEqual(3, total, "the count is of everything in the window, not of what was returned");
    }

    [TestMethod]
    public void LoadTripStatuses_WorksOutEachBookingsStatus_WhateverItsFlightDate()
    {
        DateTime nextDate = new(2030, 3, 6), laterDate = new(2030, 4, 1);
        Book("SKY-SERVICE00011", new DateTime(2030, 1, 1), nextDate, "ECO2A");
        Book("SKY-SERVICE00012", new DateTime(2030, 1, 2), nextDate, "ECO2B");
        Book("SKY-SERVICE00013", new DateTime(2030, 1, 3), laterDate, "ECO2C");
        DateTime now = AppData.DepartureOf("PR-101", nextDate).AddHours(-1);   // inside the check-in window of the first two
        Assert.IsTrue(_store.CheckInPassenger("SKY-SERVICE00011", "Ana Example", "PR-101", nextDate, "ECO2A", now));

        var bookings = _store.LoadRecentBookings(new DateTime(2029, 1, 1), 100);
        Dictionary<string, TripStatusInfo> trips = _service.LoadTripStatuses(bookings, now);

        Assert.AreEqual(3, trips.Count);
        Assert.AreEqual(TripPhase.CheckedIn, trips["SKY-SERVICE00011"].Phase);
        Assert.AreEqual(TripPhase.CheckInOpen, trips["SKY-SERVICE00012"].Phase);
        Assert.AreEqual(TripPhase.Upcoming, trips["SKY-SERVICE00013"].Phase, "a flight weeks away is only upcoming");
    }

    [TestMethod]
    public void BothMethods_NeedThePermissionToViewBookings()
    {
        var nobody = new AdminService(_store, new StaffSession(9, "x", "X", (StaffRole)99, false));
        Assert.ThrowsExactly<UnauthorizedAccessException>(() => nobody.LoadRecentBookings(DateTime.Today, 10));
        Assert.ThrowsExactly<UnauthorizedAccessException>(() => nobody.LoadTripStatuses(new List<BookingRecord>(), DateTime.Now));
    }
}
