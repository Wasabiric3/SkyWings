using Microsoft.Data.Sqlite;
using SkyWings;

namespace SkyWings.Tests;

/// <summary>Limits and guards that protect customers' bookings from abuse.</summary>
[TestClass]
public sealed class CustomerHardeningTests
{
    private string _dbPath = "";
    private BookingStore _store = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"skywings-harden-{Guid.NewGuid():N}.db");
        _store = new BookingStore(new SqliteConnectionStringBuilder { DataSource = _dbPath, ForeignKeys = true, Pooling = false }.ConnectionString);
        _store.SeedFlightsIfEmpty(AppData.DomesticFlights, AppData.InternationalFlights, 180, 220);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private static PassengerRecord Pax(string name, string address = "1 Test St", string contact = "09171234567", string id = "PSP-1")
        => new(name, address, contact, 30, "1995-01-01", id, false, false, null, Array.Empty<string>(), 100m);

    private static BookingRecord Booking(string reference, params PassengerRecord[] passengers)
        => new(reference, DateTime.Now, "PR-101", "Manila (MNL)", "Cebu (CEB)", DateTime.Today.AddDays(30), true, "Economy",
            "Credit Card", "**** 1111", 100m * passengers.Length, "Scheduled", passengers, AppData.StatusConfirmed, "PAID", "TXN", DateTime.Now, 0m);

    private void Save(BookingRecord booking)
        => _store.SaveConfirmedBooking(booking, false, booking.TotalAmount, "Credit Card", "**** 1111", "TXN", DateTime.Now);

    [TestMethod]
    public void Save_RefusesMoreThanTheTravellerLimit()
    {
        var tooMany = Enumerable.Range(1, BookingRules.MaxPassengersPerBooking + 1).Select(i => Pax("Traveller " + i)).ToArray();
        var allowed = Enumerable.Range(1, BookingRules.MaxPassengersPerBooking).Select(i => Pax("Traveller " + i)).ToArray();

        Assert.ThrowsExactly<ArgumentException>(() => Save(Booking("SKY-TOOMANY001", tooMany)));
        Save(Booking("SKY-JUSTENOUGH1", allowed));
        Assert.IsNull(_store.FindByReference("SKY-TOOMANY001"));
        Assert.IsNotNull(_store.FindByReference("SKY-JUSTENOUGH1"));
    }

    [TestMethod]
    public void Save_RefusesOversizedFieldsAndControlCharacters()
    {
        Assert.ThrowsExactly<ArgumentException>(() => Save(Booking("SKY-LONGNAME01", Pax(new string('a', BookingRules.MaxNameLength + 1)))));
        Assert.ThrowsExactly<ArgumentException>(() => Save(Booking("SKY-LONGADDR01", Pax("Ok Name", address: new string('b', BookingRules.MaxAddressLength + 1)))));
        Assert.ThrowsExactly<ArgumentException>(() => Save(Booking("SKY-CTRLCHAR01", Pax("Line\r\nBreak"))));
        Assert.ThrowsExactly<ArgumentException>(() => Save(Booking("SKY-NOPAX00001")));
    }

    [TestMethod]
    public void Save_NeverOverwritesAnExistingBookingWithTheSameReference()
    {
        Save(Booking("SKY-SAMEREF0001", Pax("First Owner")));

        Assert.ThrowsExactly<IOException>(() => Save(Booking("SKY-SAMEREF0001", Pax("Intruder Person"))));

        BookingRecord kept = _store.FindByReference("SKY-SAMEREF0001")!;
        Assert.AreEqual("First Owner", kept.Passengers.Single().Name);
    }

    [TestMethod]
    public void SeatHolds_AreCappedPerSession()
    {
        DateTime date = DateTime.Today.AddDays(30);
        for (int i = 1; i <= BookingRules.MaxActiveSeatHoldsPerOwner; i++)
            Assert.IsTrue(_store.AddSeatHold("PR-101", date, $"ECO{i}A", "greedy-session"));

        Assert.ThrowsExactly<InvalidOperationException>(() => _store.AddSeatHold("PR-101", date, "ECO30A", "greedy-session"));
        Assert.IsTrue(_store.AddSeatHold("PR-101", date, "ECO30A", "another-session"), "Other sessions are not affected.");

        _store.RemoveHoldsForOwnerAndFlight("greedy-session", "PR-101", date);   // finishing or cancelling frees the allowance
        Assert.IsTrue(_store.AddSeatHold("PR-101", date, "ECO31A", "greedy-session"));
    }
}

/// <summary>The customer lookup lock-out. It keeps process-wide state, so these tests must not run in parallel.</summary>
[TestClass]
[DoNotParallelize]
public sealed class LookupGuardTests
{
    private DateTime _now;

    [TestInitialize]
    public void Setup()
    {
        LookupGuard.Reset();
        _now = new DateTime(2027, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        LookupGuard.Clock = () => _now;
    }

    [TestCleanup]
    public void Cleanup() => LookupGuard.Reset();

    private void FailFiveTimes() { for (int i = 0; i < 5; i++) LookupGuard.RecordFailure(); }

    [TestMethod]
    public void FiveWrongTries_LockLookups_AndEachLockIsLongerThanTheLast()
    {
        Assert.IsNull(LookupGuard.LockedMessage());
        FailFiveTimes();
        Assert.IsNotNull(LookupGuard.LockedMessage());

        _now += TimeSpan.FromSeconds(31);            // first lock lasts 30 seconds
        Assert.IsNull(LookupGuard.LockedMessage());

        FailFiveTimes();                              // second lock lasts 60 seconds
        _now += TimeSpan.FromSeconds(45);
        Assert.IsNotNull(LookupGuard.LockedMessage(), "Still locked after 45s: the second lock is longer.");
        _now += TimeSpan.FromSeconds(20);
        Assert.IsNull(LookupGuard.LockedMessage());
    }

    [TestMethod]
    public void LockLength_IsCappedAtFiveMinutes()
    {
        for (int round = 0; round < 12; round++) FailFiveTimes();
        _now += TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1);
        Assert.IsNull(LookupGuard.LockedMessage());
    }

    [TestMethod]
    public void SuccessResetsTheCounters_AndFailuresUseOneGenericMessage()
    {
        for (int i = 0; i < 4; i++) LookupGuard.RecordFailure();
        LookupGuard.RecordSuccess();
        for (int i = 0; i < 4; i++) LookupGuard.RecordFailure();
        Assert.IsNull(LookupGuard.LockedMessage(), "Four failures after a success must not lock.");

        // A fresh counter: the message never says whether the reference or the name was wrong.
        LookupGuard.Reset();
        LookupGuard.Clock = () => _now;
        Assert.AreEqual("We couldn't find a booking with that reference.", LookupGuard.RecordFailure());
    }
}
