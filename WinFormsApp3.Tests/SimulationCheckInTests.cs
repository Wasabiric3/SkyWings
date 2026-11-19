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
    [DoNotParallelize]   // a wall-clock bound means nothing while every other test competes for the CPU
    public void ProgressCheckIns_TheMinuteByMinuteStep_IsQuickAfterCatchingUp()
    {
        // It runs every minute while holding the store's lock, so it must read only the flights it needs (~60 ms here;
        // a query plan that reads every confirmed booking first took 800-2,000 ms).
        _sim.EnsureLoad(Now, horizonDays: 7);
        _sim.ProgressCheckIns(Now.AddDays(6));   // the one-off catch-up after a long gap

        var clock = Stopwatch.StartNew();
        _sim.ProgressCheckIns(Now.AddDays(6).AddMinutes(1));
        clock.Stop();

        Assert.IsLessThan(500L, clock.ElapsedMilliseconds, $"{clock.ElapsedMilliseconds} ms");
    }

    [TestMethod]
    public void AfterAReset_TodaysDepartedFlightsAreCaughtUpAgain()
    {
        DateTime noon = Now.Date.AddHours(12);
        _sim.EnsureLoad(noon, horizonDays: 1);
        _sim.ProgressCheckIns(noon);

        _sim.Reset(noon, horizonDays: 1);   // regenerates today's demo bookings, including flights that already left
        _sim.ProgressCheckIns(noon.AddMinutes(1));

        DateTime today = Now.Date;
        var departed = _store.LoadActiveFlights(true).Concat(_store.LoadActiveFlights(false))
            .Where(f => AppData.DepartureOf(f.FlightNo, today) < noon).Select(f => f.FlightNo).ToHashSet();
        int waiting = _store.LoadSimulatedCheckInCandidates(today, today).Where(c => departed.Contains(c.FlightNo))
            .Select(c => c.Reference).Distinct().Count();
        int checkedIn = AllCheckIns().Where(c => c.FlightDate.Date == today && departed.Contains(c.FlightNo))
            .Select(c => c.BookingReference).Distinct().Count();
        Assert.IsGreaterThan(0.85, checkedIn / (double)(checkedIn + waiting), "not a wall of no-shows after a reset");
    }

    private double ShareCheckedIn(string flightNo, DateTime date)
    {
        int waiting = _store.LoadSimulatedCheckInCandidates(date, date).Where(c => c.FlightNo == flightNo).Select(c => c.Reference).Distinct().Count();
        int checkedIn = AllCheckIns().Where(c => c.FlightNo == flightNo && c.FlightDate.Date == date).Select(c => c.BookingReference).Distinct().Count();
        return checkedIn + waiting == 0 ? double.NaN : checkedIn / (double)(checkedIn + waiting);
    }

    [TestMethod]
    public void ADeactivatedFlight_StillHasItsDemoTravellersCheckIn()
    {
        // Deactivating a flight leaves its existing bookings alone, so the flight still flies with them.
        _sim.EnsureLoad(Now, horizonDays: 2);
        _sim.ProgressCheckIns(Now);
        _store.SetFlightActive("PR-101", false);
        DateTime tomorrow = Now.Date.AddDays(1);

        _sim.ProgressCheckIns(AppData.DepartureOf("PR-101", tomorrow).AddMinutes(1));

        Assert.IsGreaterThan(0.85, ShareCheckedIn("PR-101", tomorrow));
    }

    [TestMethod]
    public void AFlightAddedLaterInTheDay_GetsItsDepartedLoadCaughtUp()
    {
        DateTime noon = Now.Date.AddHours(12);
        _store.SetFlightActive("PR-101", false);   // no demo load yet for PR-101 (departs in the morning)
        _sim.EnsureLoad(noon, horizonDays: 1);
        _sim.ProgressCheckIns(noon);

        _store.SetFlightActive("PR-101", true);    // reactivated at noon: today's load is generated for a flight that already left
        _sim.EnsureLoad(noon, horizonDays: 1);
        _sim.ProgressCheckIns(noon.AddMinutes(1));

        Assert.IsGreaterThan(0.85, ShareCheckedIn("PR-101", Now.Date), "not a wall of no-shows");
    }

    [TestMethod]
    public void ACheckInStepThatRacedAReset_DoesNotMoveTheWatermarkPastIt()
    {
        // The step read the watermark, then a reset cleared it, then the step saved: the reset's "look back" must survive.
        _sim.EnsureLoad(Now, horizonDays: 1);
        _sim.ProgressCheckIns(Now);
        string read = _store.GetSetting(BookingStore.SettingDemoCheckInsThrough, "");
        Assert.AreNotEqual("", read);

        _store.DeleteSimulatedBookings();
        _store.SaveSimulatedCheckIns(Array.Empty<CheckInRecord>(), expectedThrough: read, newThrough: Now.AddMinutes(1));

        Assert.AreEqual("", _store.GetSetting(BookingStore.SettingDemoCheckInsThrough, ""));
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
