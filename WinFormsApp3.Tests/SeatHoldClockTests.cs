using Microsoft.Data.Sqlite;

namespace SkyWings.Tests;

/// <summary>Seat holds last SeatHoldMinutes on the app's (local) clock, in any time zone.</summary>
[TestClass]
[DoNotParallelize]
public sealed class SeatHoldClockTests
{
    private string _dbPath = "";
    private BookingStore _store = null!;
    private readonly DateTime _flightDate = DateTime.Today.AddDays(20);
    private readonly DateTime _t0 = new(2026, 11, 18, 10, 0, 0);

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"skywings-hold-{Guid.NewGuid():N}.db");
        _store = new BookingStore(new SqliteConnectionStringBuilder { DataSource = _dbPath, ForeignKeys = true }.ConnectionString);
        _store.SeedFlightsIfEmpty(AppData.DomesticFlights, AppData.InternationalFlights, 180, 220);
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    [TestMethod]
    public void AHold_IsActiveForTheHoldPeriod_ThenGone()
    {
        Assert.IsTrue(_store.AddSeatHold("PR-101", _flightDate, "ECO1A", "owner-1", _t0));

        Assert.IsTrue(_store.GetActiveHoldsForFlightDate("PR-101", _flightDate, _t0.AddMinutes(AppData.SeatHoldMinutes - 1)).ContainsKey("ECO1A"));
        Assert.IsFalse(_store.GetActiveHoldsForFlightDate("PR-101", _flightDate, _t0.AddMinutes(AppData.SeatHoldMinutes).AddSeconds(1)).ContainsKey("ECO1A"),
            "a hold must not outlive its period");
    }

    [TestMethod]
    public void AnExpiredHold_FreesTheSeatForSomeoneElse()
    {
        Assert.IsTrue(_store.AddSeatHold("PR-101", _flightDate, "ECO1B", "owner-1", _t0));
        Assert.IsFalse(_store.AddSeatHold("PR-101", _flightDate, "ECO1B", "owner-2", _t0.AddMinutes(5)), "still held at 5 minutes");

        _store.PurgeExpiredSeatHolds(_t0.AddMinutes(AppData.SeatHoldMinutes + 1));

        Assert.IsTrue(_store.AddSeatHold("PR-101", _flightDate, "ECO1B", "owner-2", _t0.AddMinutes(AppData.SeatHoldMinutes + 1)));
    }

    [TestMethod]
    public void TheHoldLimitPerOwner_OnlyCountsHoldsThatAreStillActive()
    {
        for (int i = 0; i < BookingRules.MaxActiveSeatHoldsPerOwner; i++)
            Assert.IsTrue(_store.AddSeatHold("PR-101", _flightDate, $"ECO2{(char)('A' + i)}", "owner-3", _t0));

        // An hour later those holds are history, so the same owner can hold again.
        Assert.IsTrue(_store.AddSeatHold("PR-101", _flightDate, "ECO3A", "owner-3", _t0.AddHours(1)));
    }

    [TestMethod]
    public void WithTheRealClock_AFreshHoldIsActive_AndExpiresLocalTenMinutesLater()
    {
        Assert.IsTrue(_store.AddSeatHold("PR-101", _flightDate, "ECO4A", "owner-4"));

        Dictionary<string, SeatHoldInfo> holds = _store.GetActiveHoldsForFlightDate("PR-101", _flightDate);

        Assert.IsTrue(holds.ContainsKey("ECO4A"));
        TimeSpan left = holds["ECO4A"].ExpiresAt - DateTime.Now;
        Assert.IsTrue(left > TimeSpan.FromMinutes(AppData.SeatHoldMinutes - 1) && left <= TimeSpan.FromMinutes(AppData.SeatHoldMinutes),
            $"expiry should be about {AppData.SeatHoldMinutes} minutes from now but is {left.TotalMinutes:F1}");
        _store.PurgeExpiredSeatHolds();
        Assert.IsTrue(_store.GetActiveHoldsForFlightDate("PR-101", _flightDate).ContainsKey("ECO4A"), "a fresh hold survives a purge");
        Assert.IsFalse(_store.GetActiveHoldsForFlightDate("PR-101", _flightDate, DateTime.Now.AddMinutes(AppData.SeatHoldMinutes + 1)).ContainsKey("ECO4A"));
    }
}
