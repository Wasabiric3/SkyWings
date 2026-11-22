using Microsoft.Data.Sqlite;

namespace SkyWings.Tests;

/// <summary>The "Recently booked" queries: bookings by when they were made, whatever their flight date.</summary>
[TestClass]
[DoNotParallelize]   // its cleanup clears the SQLite connection pools, which other tests running at the same time would feel
public sealed class RecentBookingsStoreTests
{
    private string _path = "";
    private BookingStore _store = null!;

    [TestInitialize]
    public void Setup()
    {
        _path = Path.Combine(Path.GetTempPath(), $"skywings-recent-{Guid.NewGuid():N}.db");
        _store = new BookingStore(new SqliteConnectionStringBuilder { DataSource = _path, ForeignKeys = true, Pooling = false }.ConnectionString);
        _store.SeedFlightsIfEmpty(AppData.DomesticFlights, AppData.InternationalFlights, AppData.DefaultDomesticCapacity, AppData.DefaultInternationalCapacity);
    }

    [TestCleanup]
    public void Cleanup()
    {
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

    private (DateTime Now, string[] Refs) BookFour()
    {
        DateTime now = new(2026, 11, 22, 12, 0, 0);
        Book("SKY-RECENT000001", now.AddDays(-40), new DateTime(2030, 3, 1), "ECO1A");   // too old for a week
        Book("SKY-RECENT000002", now.AddDays(-2), new DateTime(2030, 3, 2), "ECO1B");
        Book("SKY-RECENT000003", now.AddHours(-3), new DateTime(2030, 3, 3), "ECO1C");
        Book("SKY-RECENT000004", now.AddMinutes(-10), new DateTime(2030, 3, 4), "ECO1D");
        return (now, new[] { "SKY-RECENT000001", "SKY-RECENT000002", "SKY-RECENT000003", "SKY-RECENT000004" });
    }

    [TestMethod]
    public void LoadRecentBookings_ReturnsWhatWasBookedSince_NewestFirst_AcrossFlightDates()
    {
        var (now, _) = BookFour();
        List<BookingRecord> recent = _store.LoadRecentBookings(now.AddDays(-7), 1000);
        CollectionAssert.AreEqual(new[] { "SKY-RECENT000004", "SKY-RECENT000003", "SKY-RECENT000002" }, recent.Select(b => b.Reference).ToArray());
        Assert.AreEqual(3, recent.Select(b => b.FlightDateValue.Date).Distinct().Count(), "three different flight dates");
        Assert.IsTrue(recent.All(b => b.Passengers.Count == 1), "the passengers are loaded with each booking");
    }

    [TestMethod]
    public void LoadRecentBookings_WithALimit_KeepsTheNewest_AndTheCountSaysHowManyThereAre()
    {
        var (now, _) = BookFour();
        List<BookingRecord> two = _store.LoadRecentBookings(now.AddDays(-7), 2);
        CollectionAssert.AreEqual(new[] { "SKY-RECENT000004", "SKY-RECENT000003" }, two.Select(b => b.Reference).ToArray());
        Assert.AreEqual(3, _store.CountBookingsBookedSince(now.AddDays(-7)));
        Assert.AreEqual(4, _store.CountBookingsBookedSince(now.AddDays(-60)));
        Assert.AreEqual(1, _store.CountBookingsBookedSince(now.AddHours(-1)));
    }

    [TestMethod]
    public void LoadRecentBookings_WithNothingInTheWindow_IsEmpty()
    {
        var (now, _) = BookFour();
        Assert.IsEmpty(_store.LoadRecentBookings(now.AddMinutes(-1), 100));
        Assert.AreEqual(0, _store.CountBookingsBookedSince(now.AddMinutes(-1)));
    }

    [TestMethod]
    public void LoadRecentBookings_ALimitBelowOne_ReturnsNothingRatherThanEverything()
    {
        var (now, _) = BookFour();
        Assert.IsEmpty(_store.LoadRecentBookings(now.AddDays(-7), 0));
    }

    [TestMethod]
    public void LoadCheckInsForRefs_ReturnsOnlyTheCheckInsOfThoseBookings()
    {
        DateTime flightDate = new(2030, 3, 5);
        Book("SKY-RECENT000011", new DateTime(2030, 1, 1), flightDate, "ECO2A");
        Book("SKY-RECENT000012", new DateTime(2030, 1, 2), flightDate, "ECO2B");
        DateTime now = AppData.DepartureOf("PR-101", flightDate).AddHours(-1);   // inside the check-in window
        Assert.IsTrue(_store.CheckInPassenger("SKY-RECENT000011", "Ana Example", "PR-101", flightDate, "ECO2A", now));
        Assert.IsTrue(_store.CheckInPassenger("SKY-RECENT000012", "Ana Example", "PR-101", flightDate, "ECO2B", now));

        Dictionary<string, List<CheckInRecord>> only = _store.LoadCheckInsForRefs(new[] { "SKY-RECENT000011" });
        CollectionAssert.AreEqual(new[] { "SKY-RECENT000011" }, only.Keys.ToArray());
        Assert.AreEqual(2, _store.LoadCheckInsForRefs(new[] { "SKY-RECENT000011", "SKY-RECENT000012" }).Count);
        Assert.IsEmpty(_store.LoadCheckInsForRefs(Array.Empty<string>()));
    }

    [TestMethod]
    public void LoadCheckInsForRefs_CopesWithMoreReferencesThanOneQueryCanTake()
    {
        var refs = Enumerable.Range(0, 2500).Select(i => $"SKY-NOSUCH{i:D6}").ToList();
        Assert.IsEmpty(_store.LoadCheckInsForRefs(refs), "no such bookings, no check-ins, and no 'too many SQL variables' error");
    }
}
