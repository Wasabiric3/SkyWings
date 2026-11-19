using System.Diagnostics;
using Microsoft.Data.Sqlite;

namespace SkyWings.Tests;

/// <summary>Demo travellers check in by themselves during the 24-hour window; about 8% of demo bookings are no-shows.</summary>
[TestClass]
public sealed class SimulationCheckInTests
{
    private static readonly DateTime Now = DateTime.Today.AddHours(6);
    private string _dbPath = "";
    private BookingStore _store = null!;
    private SimulationService _sim = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"skywings-simci-{Guid.NewGuid():N}.db");
        _store = new BookingStore(new SqliteConnectionStringBuilder { DataSource = _dbPath, ForeignKeys = true }.ConnectionString);
        _store.SeedDefaultFlights();
        _sim = new SimulationService(_store);
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private static DateTime Departure(CheckInRecord c) => AppData.DepartureOf(c.FlightNo, c.FlightDate);

    private List<CheckInRecord> AllCheckIns() => _store.LoadCheckInsByBooking().Values.SelectMany(l => l).ToList();

    [TestMethod]
    public void PlannedCheckIn_IsAlwaysInsideTheWindow()
    {
        DateTime departure = new(2027, 5, 10, 9, 30, 0);
        for (int i = 0; i < 1000; i++)
        {
            if (SimulationService.PlannedCheckIn($"SIM-{i:X12}", "Traveller " + i % 3, departure) is not { } at) continue;
            Assert.IsGreaterThan(departure.AddHours(-AppData.CheckInWindowHours), at, $"#{i} before check-in opens");
            Assert.IsLessThan(departure, at, $"#{i} after departure");
        }
    }

    [TestMethod]
    public void PlannedCheckIn_NoShowRateIsAboutEightPercent()
    {
        DateTime departure = new(2027, 5, 10, 9, 30, 0);
        int noShows = Enumerable.Range(0, 2000).Count(i => SimulationService.PlannedCheckIn($"SIM-{i:X12}", "A", departure) is null);
        Assert.IsTrue(noShows is >= 100 and <= 220, $"{noShows} of 2000 no-shows");
    }

    [TestMethod]
    public void PlannedCheckIn_IsStable_AndAPartyChecksInTogether()
    {
        DateTime departure = new(2027, 5, 10, 9, 30, 0);
        string reference = Enumerable.Range(0, 100).Select(i => $"SIM-{i:X12}")
            .First(r => SimulationService.PlannedCheckIn(r, "A", departure) is not null);
        DateTime a = SimulationService.PlannedCheckIn(reference, "A", departure)!.Value;
        Assert.AreEqual(a, SimulationService.PlannedCheckIn(reference, "A", departure));
        DateTime b = SimulationService.PlannedCheckIn(reference, "B", departure)!.Value;
        Assert.IsLessThanOrEqualTo(5.0, Math.Abs((a - b).TotalMinutes), "travellers on one booking check in within minutes of each other");
    }

    [TestMethod]
    public void NobodyChecksInBeforeTheWindowOpens()
    {
        _sim.EnsureLoad(Now, horizonDays: 3);

        int added = _sim.ProgressCheckIns(Now);

        List<CheckInRecord> all = AllCheckIns();
        Assert.AreEqual(all.Count, added);
        foreach (CheckInRecord c in all)
        {
            Assert.IsLessThanOrEqualTo(Now, c.CheckedInAt, "no check-in in the future");
            Assert.IsGreaterThan(Departure(c).AddHours(-AppData.CheckInWindowHours), c.CheckedInAt, "not before check-in opens");
        }
    }

    [TestMethod]
    public void ByDeparture_AboutNinetyTwoPercentOfBookingsCheckedIn()
    {
        _sim.EnsureLoad(Now, horizonDays: 1);
        DateTime today = Now.Date;
        int bookings = _store.LoadSimulatedCheckInCandidates(today, today).Select(c => c.Reference).Distinct().Count();
        DateTime afterLastDeparture = _store.LoadActiveFlights(true).Concat(_store.LoadActiveFlights(false))
            .Max(f => AppData.DepartureOf(f.FlightNo, today)).AddMinutes(1);

        _sim.ProgressCheckIns(afterLastDeparture);

        int stillWaiting = _store.LoadSimulatedCheckInCandidates(today, today).Select(c => c.Reference).Distinct().Count();
        double share = 1.0 - stillWaiting / (double)bookings;
        Assert.IsTrue(share is >= 0.87 and <= 0.97, $"{share:P1} of {bookings} demo bookings checked in");
    }

    [TestMethod]
    public void CatchUp_AfterAGap_BackDatesCheckIns()
    {
        _sim.EnsureLoad(Now, horizonDays: 2);

        int added = _sim.ProgressCheckIns(Now.AddDays(3));

        Assert.IsGreaterThan(0, added);
        foreach (CheckInRecord c in AllCheckIns())
        {
            Assert.IsLessThan(Departure(c), c.CheckedInAt);
            Assert.IsGreaterThan(Departure(c).AddHours(-AppData.CheckInWindowHours), c.CheckedInAt);
        }
    }

    [TestMethod]
    public void RealBookings_AreNeverCheckedIn()
    {
        var p = new PassengerRecord("Real Person", "1 St", "0917", 35, "1991-01-01", "PSP-1", false, false,
            new SeatAssignment("Real Person", "ECO9D"), Array.Empty<string>(), 4000m);
        var real = new BookingRecord("SKY-REALSIMCI001", DateTime.Now, "PR-101", "Manila (MNL)", "Cebu (CEB)", Now.Date, true, "Economy",
            "Credit Card", "**** 4521", 4000m, "Scheduled", new[] { p }, AppData.StatusConfirmed, "PAID", "TXN-1", DateTime.Now, 0m);
        _store.SaveConfirmedBooking(real, false, real.TotalAmount, "Credit Card", "**** 4521", real.PaymentTransactionRef, DateTime.Now);
        _sim.EnsureLoad(Now, horizonDays: 1);

        _sim.ProgressCheckIns(Now.AddDays(1));

        Assert.IsEmpty(_store.LoadCheckIns("SKY-REALSIMCI001"));
    }

    [TestMethod]
    public void RunIfEnabled_ReportsCheckInsAdded()
    {
        SimulationTick? tick = _sim.RunIfEnabled(Now);

        Assert.IsNotNull(tick);
        Assert.IsGreaterThan(0, tick.CheckInsAdded);
        Assert.AreEqual(tick.CheckInsAdded, AllCheckIns().Count);
    }

    [TestMethod]
    public void ProgressCheckIns_TheMinuteByMinuteStep_IsQuickAfterCatchingUp()
    {
        // It runs every minute while holding the store's lock, so it must not rescan a whole week each time.
        _sim.EnsureLoad(Now, horizonDays: 7);
        _sim.ProgressCheckIns(Now.AddDays(6));   // the one-off catch-up after a long gap

        var clock = Stopwatch.StartNew();
        _sim.ProgressCheckIns(Now.AddDays(6).AddMinutes(1));
        clock.Stop();

        Assert.IsLessThan(1000L, clock.ElapsedMilliseconds, $"{clock.ElapsedMilliseconds} ms");
    }

    [TestMethod]
    public void ProgressCheckIns_StillCatchesUpAWeekAfterAGap()
    {
        _sim.EnsureLoad(Now, horizonDays: 2);
        _sim.ProgressCheckIns(Now);   // the last run before the app was closed

        _sim.ProgressCheckIns(Now.AddDays(6));

        DateTime tomorrow = Now.Date.AddDays(1);
        int waiting = _store.LoadSimulatedCheckInCandidates(tomorrow, tomorrow).Select(c => c.Reference).Distinct().Count();
        int checkedIn = AllCheckIns().Where(c => c.FlightDate.Date == tomorrow).Select(c => c.BookingReference).Distinct().Count();
        Assert.IsGreaterThan(0.85, checkedIn / (double)(checkedIn + waiting), "tomorrow's flights were caught up after the gap");
    }
}
