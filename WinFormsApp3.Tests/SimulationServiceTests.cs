using Microsoft.Data.Sqlite;

namespace SkyWings.Tests;

/// <summary>The demo simulator: realistic pre-filled loads plus live changes, all tagged so reports and real bookings stay clean.</summary>
[TestClass]
public sealed class SimulationServiceTests
{
    private static readonly DateTime Now = DateTime.Today.AddHours(6);   // early morning: today's flights are still ahead
    private string _dbPath = "";
    private BookingStore _store = null!;
    private SimulationService _sim = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"skywings-simsvc-{Guid.NewGuid():N}.db");
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

    private static int SeatsOf(Aircraft a, string cabin) => a.Cabin(cabin)?.Seats ?? 0;

    private double Occupancy(string flightNo, DateTime date, string cabin)
    {
        int total = SeatsOf(_store.LoadAircraft(flightNo), cabin);
        return total == 0 ? 0 : 1.0 - _store.CabinSeatsLeft(flightNo, date, cabin) / (double)total;
    }

    // ------------------------------------------------------------------ pre-filled loads

    [TestMethod]
    public void BaseLoad_IsFullerCloserToDeparture_AndStaysInBounds()
    {
        for (int d = 0; d < 30; d++)
        {
            Assert.IsTrue(SimulationService.BaseLoad(d, true) is >= 0.60 and <= 0.90, $"day {d}");
            Assert.IsTrue(SimulationService.BaseLoad(d, false) is >= 0.60 and <= 0.90, $"day {d}");
        }
        Assert.IsGreaterThan(SimulationService.BaseLoad(10, true), SimulationService.BaseLoad(1, true));
    }

    [TestMethod]
    public void EnsureLoad_KeepsEveryEconomyCabinBetween60And90PercentFull()
    {
        _sim.EnsureLoad(Now, horizonDays: 14);

        foreach (Flight flight in _store.LoadActiveFlights(true).Concat(_store.LoadActiveFlights(false)))
            for (int d = 0; d < 14; d++)
            {
                double load = Occupancy(flight.FlightNo, Now.Date.AddDays(d), "Economy");
                Assert.IsTrue(load is >= 0.60 and <= 0.90, $"{flight.FlightNo} day {d}: {load:P0}");
            }
    }

    [TestMethod]
    public void EnsureLoad_VariesTheLoadFromFlightToFlight()
    {
        _sim.EnsureLoad(Now, horizonDays: 1);

        var percents = _store.LoadActiveFlights(true).Concat(_store.LoadActiveFlights(false))
            .Select(f => (int)Math.Round(Occupancy(f.FlightNo, Now.Date, "Economy") * 100)).Distinct().Count();
        Assert.IsGreaterThan(5, percents, "every flight showing the same load does not look real");
    }

    [TestMethod]
    public void RunIfEnabled_RegeneratesLoadsMadeByAnOlderVersionOfTheAlgorithm()
    {
        _sim.EnsureLoad(Now, horizonDays: 2);
        _store.SetSetting(BookingStore.SettingDemoLoadVersion, "1");

        SimulationTick? tick = _sim.RunIfEnabled(Now);

        Assert.IsNotNull(tick);
        Assert.IsGreaterThan(0, tick.LoadsGenerated, "old loads are replaced, not kept");
        Assert.AreEqual(SimulationService.LoadVersion, _store.GetSetting(BookingStore.SettingDemoLoadVersion, ""));
        Assert.AreEqual(0, _sim.RunIfEnabled(Now)!.LoadsGenerated, "and only once");
    }

    [TestMethod]
    public void EnsureLoad_FillsFlightsWithBookedSeats_AndOnlyOnTheDatesInTheHorizon()
    {
        int generated = _sim.EnsureLoad(Now, horizonDays: 3);

        Assert.IsGreaterThan(0, generated);
        DateTime inside = Now.Date.AddDays(1), outside = Now.Date.AddDays(3);
        Assert.IsNotEmpty(_store.LoadTakenSeats("PR-101", inside));
        Assert.IsEmpty(_store.LoadTakenSeats("PR-101", outside), "nothing is generated beyond the horizon");
        Assert.IsTrue(Occupancy("PR-101", inside, "Economy") is > 0.1 and <= 1.0);
    }

    [TestMethod]
    public void EnsureLoad_IsIdempotent()
    {
        _sim.EnsureLoad(Now, horizonDays: 2);
        int taken = _store.LoadTakenSeats("PR-101", Now.Date.AddDays(1)).Count;

        int again = _sim.EnsureLoad(Now, horizonDays: 2);

        Assert.AreEqual(0, again);
        Assert.AreEqual(taken, _store.LoadTakenSeats("PR-101", Now.Date.AddDays(1)).Count);
    }

    [TestMethod]
    public void EnsureLoad_IsDeterministic_ForTheSameFlightAndDate()
    {
        _sim.EnsureLoad(Now, horizonDays: 2);
        string dbPath2 = Path.Combine(Path.GetTempPath(), $"skywings-simsvc2-{Guid.NewGuid():N}.db");
        try
        {
            var other = new BookingStore(new SqliteConnectionStringBuilder { DataSource = dbPath2, ForeignKeys = true }.ConnectionString);
            other.SeedDefaultFlights();
            new SimulationService(other).EnsureLoad(Now, horizonDays: 2);

            DateTime date = Now.Date.AddDays(1);
            CollectionAssert.AreEquivalent(_store.LoadTakenSeats("PR-301", date).ToArray(), other.LoadTakenSeats("PR-301", date).ToArray());
        }
        finally { SqliteConnection.ClearAllPools(); if (File.Exists(dbPath2)) File.Delete(dbPath2); }
    }

    [TestMethod]
    public void EnsureLoad_OnlyAssignsSeatsThatExistOnTheAircraft_NeverTwice()
    {
        _sim.EnsureLoad(Now, horizonDays: 2);

        foreach (string flightNo in new[] { "PR-101", "PR-201", "PR-301" })
        {
            Aircraft aircraft = _store.LoadAircraft(flightNo);
            DateTime date = Now.Date.AddDays(1);
            HashSet<string> taken = _store.LoadTakenSeats(flightNo, date);
            Assert.IsNotEmpty(taken, flightNo);
            foreach (string code in taken) Assert.IsTrue(aircraft.HasSeat(code), $"{flightNo} {code} does not exist on the {aircraft.Model}");
            // Every confirmed passenger has their own seat: the number of taken seats equals the number of seated passengers.
            Assert.AreEqual(taken.Count, CountSeatedPassengers(flightNo, date), $"{flightNo}: a seat was assigned twice");
        }
    }

    private int CountSeatedPassengers(string flightNo, DateTime date)
    {
        using var con = new SqliteConnection(_store.ConnectionString);
        con.Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM passengers p JOIN bookings b ON b.reference = p.booking_ref " +
                          "WHERE b.status = 'CONFIRMED' AND b.flight_no = @f AND b.flight_date = @d AND p.seat_code <> ''";
        cmd.Parameters.AddWithValue("@f", flightNo);
        cmd.Parameters.AddWithValue("@d", date.Date);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    [TestMethod]
    public void EnsureLoad_FillsTheCabinsEachAircraftActuallyHas()
    {
        _sim.EnsureLoad(Now, horizonDays: 2);
        DateTime date = Now.Date.AddDays(1);

        Assert.IsGreaterThan(0.0, Occupancy("PR-301", date, "First Class") + Occupancy("PR-301", date, "Business"), "a 777 has premium cabins");
        Assert.IsFalse(_store.LoadTakenSeats("PR-101", date).Any(c => c.StartsWith("FST", StringComparison.Ordinal)), "an A320 has no First cabin");
    }

    [TestMethod]
    public void EnsureLoad_MakesSomeCabinsSoldOut_ButOnlyAFew()
    {
        _sim.EnsureLoad(Now, horizonDays: 7);

        int soldOut = 0, total = 0;
        foreach (Flight flight in _store.LoadActiveFlights(true).Concat(_store.LoadActiveFlights(false)))
            for (int d = 0; d < 7; d++)
            {
                DateTime date = Now.Date.AddDays(d);
                foreach (CabinLayout cabin in _store.LoadAircraft(flight.FlightNo).Cabins)
                {
                    total++;
                    if (_store.CabinSeatsLeft(flight.FlightNo, date, cabin.ClassName) == 0) soldOut++;
                }
            }
        Assert.IsGreaterThan(0, soldOut, "the waitlist needs something sold out to demonstrate");
        Assert.IsLessThan(total / 8, soldOut, $"{soldOut} of {total} cabins sold out is too many");
    }

    [TestMethod]
    public void EnsureLoad_AddsCancelledDemoBookings_ThatDoNotBlockSeats()
    {
        _sim.EnsureLoad(Now, horizonDays: 2);

        (int confirmed, int cancelled) = CountSimulated();

        Assert.IsGreaterThan(0, cancelled);
        Assert.IsTrue(cancelled < confirmed * 0.15, $"{cancelled} cancelled vs {confirmed} confirmed");
    }

    private (int Confirmed, int Cancelled) CountSimulated()
    {
        using var con = new SqliteConnection(_store.ConnectionString);
        con.Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT SUM(status='CONFIRMED'), SUM(status='CANCELED') FROM bookings WHERE is_simulated = 1";
        using var rs = cmd.ExecuteReader();
        rs.Read();
        return (rs.IsDBNull(0) ? 0 : rs.GetInt32(0), rs.IsDBNull(1) ? 0 : rs.GetInt32(1));
    }

    [TestMethod]
    public void EnsureLoad_LeavesRealBookingsAndReportsUntouched()
    {
        BookingRecord real = RealBooking("SKY-REAL0000SIM1", "PR-101", Now.Date.AddDays(1), "ECO1A");
        _store.SaveConfirmedBooking(real, false, real.TotalAmount, "Credit Card", "**** 4521", real.PaymentTransactionRef, DateTime.Now);

        _sim.EnsureLoad(Now, horizonDays: 2);

        Assert.IsNotNull(_store.FindByReference("SKY-REAL0000SIM1"));
        Assert.AreEqual(1, _store.LoadReportStats()!.TotalBookings, "demo bookings stay out of reports");
        Assert.AreEqual(1, _store.LoadBookings().Count);
        int seatedOnReal = CountSeatedPassengers("PR-101", Now.Date.AddDays(1));
        Assert.AreEqual(_store.LoadTakenSeats("PR-101", Now.Date.AddDays(1)).Count, seatedOnReal, "no demo passenger took the real passenger's seat");
    }

    private static BookingRecord RealBooking(string reference, string flightNo, DateTime date, string seat)
    {
        var p = new PassengerRecord("Real Person", "1 St", "0917", 35, "1991-01-01", "PSP-1", false, false,
            new SeatAssignment("Real Person", seat), Array.Empty<string>(), 4000m);
        return new BookingRecord(reference, DateTime.Now, flightNo, "Manila (MNL)", "Cebu (CEB)", date, true, "Economy",
            "Credit Card", "**** 4521", 4000m, "Scheduled", new[] { p }, AppData.StatusConfirmed, "PAID", "TXN-" + reference, DateTime.Now, 0m);
    }

    [TestMethod]
    [DoNotParallelize]   // a wall-clock bound: measured alone, not while every other heavy simulator test competes for the CPU
    public void EnsureLoad_ForAllFlights_FinishesInReasonableTime()
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        _sim.EnsureLoad(Now, horizonDays: 7);
        Assert.IsLessThan(60_000, clock.ElapsedMilliseconds, $"took {clock.ElapsedMilliseconds} ms");
    }


    [TestMethod]
    public void TheDepartureBoardSearch_StaysFast_OnAFullyLoadedDatabase()
    {
        _sim.EnsureLoad(Now, horizonDays: 14);

        var clock = System.Diagnostics.Stopwatch.StartNew();
        var rows = SkyWings.Controllers.FlightSearch.Search(_store, null, Now.Date.AddDays(1), "Economy", Now);
        clock.Stop();

        Assert.IsNotEmpty(rows);
        Assert.IsLessThan(3000, clock.ElapsedMilliseconds, $"the board refresh took {clock.ElapsedMilliseconds} ms");
    }

    [TestMethod]
    public void OpeningASeatMap_StaysFast_OnAFullyLoadedDatabase()
    {
        _sim.EnsureLoad(Now, horizonDays: 14);

        var clock = System.Diagnostics.Stopwatch.StartNew();
        _store.LoadTakenSeats("PR-301", Now.Date.AddDays(5));
        _store.GetActiveHoldsForFlightDate("PR-301", Now.Date.AddDays(5));
        clock.Stop();

        Assert.IsLessThan(500, clock.ElapsedMilliseconds, $"loading a seat map took {clock.ElapsedMilliseconds} ms");
    }
    // ------------------------------------------------------------------ live changes

    [TestMethod]
    public void Tick_AddsDemoHolds_ThatShowAsHeldByOthers_AndAreCapped()
    {
        _sim.EnsureLoad(Now, horizonDays: 3);
        var options = new SimulationOptions(HoldCap: 6);
        var sim = new SimulationService(_store, options);

        for (int i = 0; i < 20; i++) sim.Tick(Now.AddMinutes(i % 2), seed: i);   // stays inside one hold period

        int holds = _store.CountActiveSimulatedHolds(Now.AddMinutes(1));
        Assert.IsGreaterThan(0, holds);
        Assert.IsLessThanOrEqualTo(6 + options.MaxHolds, holds, "the demo never floods the maps with holds");
    }

    [TestMethod]
    public void DemoHolds_Expire_LikeAnyOtherHold()
    {
        _sim.EnsureLoad(Now, horizonDays: 3);
        for (int i = 0; i < 10; i++) _sim.Tick(Now, seed: i);
        Assert.IsGreaterThan(0, _store.CountActiveSimulatedHolds(Now.AddMinutes(1)));

        _store.PurgeExpiredSeatHolds(Now.AddMinutes(AppData.SeatHoldMinutes + 1));

        Assert.AreEqual(0, _store.CountActiveSimulatedHolds(Now.AddMinutes(AppData.SeatHoldMinutes + 1)));
    }

    [TestMethod]
    public void Tick_CanAddNewDemoBookings_AndCancelExistingOnes()
    {
        _sim.EnsureLoad(Now, horizonDays: 3);
        (int before, _) = CountSimulated();
        var eager = new SimulationService(_store, new SimulationOptions(BookingChance: 1.0, CancelChance: 1.0));

        SimulationTick total = new(0, 0, 0, 0);
        for (int i = 0; i < 10; i++)
        {
            SimulationTick t = eager.Tick(Now, seed: i);
            total = new SimulationTick(total.HoldsAdded + t.HoldsAdded, total.BookingsAdded + t.BookingsAdded,
                total.BookingsCancelled + t.BookingsCancelled, 0);
        }

        Assert.IsGreaterThan(0, total.BookingsAdded);
        Assert.IsGreaterThan(0, total.BookingsCancelled);
        (int after, int cancelled) = CountSimulated();
        Assert.AreEqual(before + total.BookingsAdded - total.BookingsCancelled, after);
        Assert.IsGreaterThanOrEqualTo(total.BookingsCancelled, cancelled);
    }

    [TestMethod]
    public void CancellingADemoBooking_OffersTheFreedSeatToARealWaitlistedTraveller()
    {
        _sim.EnsureLoad(Now, horizonDays: 7);
        // Find a cabin that the simulation sold out, and put a real traveller on its waitlist.
        (string flightNo, DateTime date, string cabin)? soldOut = null;
        foreach (Flight f in _store.LoadActiveFlights(true).Concat(_store.LoadActiveFlights(false)))
        {
            for (int d = 2; d < 7 && soldOut is null; d++)
                foreach (CabinLayout c in _store.LoadAircraft(f.FlightNo).Cabins)
                    if (_store.CabinSeatsLeft(f.FlightNo, Now.Date.AddDays(d), c.ClassName) == 0) { soldOut = (f.FlightNo, Now.Date.AddDays(d), c.ClassName); break; }
            if (soldOut is not null) break;
        }
        Assert.IsNotNull(soldOut, "expected at least one sold-out cabin in a week");
        var (flightNo2, date2, cabin2) = soldOut.Value;
        WaitlistEntry entry = _store.JoinWaitlist(flightNo2, date2, cabin2, "Ana Real", "ana@example.com", 1)!;
        string demoRef = _store.LoadSimulatedConfirmedReferences(flightNo2, date2, cabin2).First();

        Assert.IsTrue(_sim.CancelSimulatedBooking(demoRef));

        Assert.AreEqual(BookingStore.WaitlistOffered, _store.FindWaitlistEntry(entry.BookingReference)!.Status);
    }

    // ------------------------------------------------------------------ switch and reset

    [TestMethod]
    public void RunIfEnabled_DoesNothing_WhileTheSwitchIsOff()
    {
        _store.SetSetting(BookingStore.SettingDemoActivity, "0");
        Assert.IsNull(_sim.RunIfEnabled(Now));
        Assert.IsEmpty(_store.LoadTakenSeats("PR-101", Now.Date.AddDays(1)));
    }

    [TestMethod]
    public void RunIfEnabled_GeneratesTheLoadAndTicks_WhenSwitchedOn()
    {
        _store.SetSetting(BookingStore.SettingDemoActivity, "1");

        SimulationTick? tick = _sim.RunIfEnabled(Now);

        Assert.IsNotNull(tick);
        Assert.IsGreaterThan(0, tick.LoadsGenerated);
        Assert.IsNotEmpty(_store.LoadTakenSeats("PR-101", Now.Date.AddDays(1)));
    }

    [TestMethod]
    public void Reset_RemovesOnlyDemoData_AndRegeneratesIt()
    {
        BookingRecord real = RealBooking("SKY-REAL0000SIM2", "PR-101", Now.Date.AddDays(1), "ECO2A");
        _store.SaveConfirmedBooking(real, false, real.TotalAmount, "Credit Card", "**** 4521", real.PaymentTransactionRef, DateTime.Now);
        _sim.EnsureLoad(Now, horizonDays: 2);

        int regenerated = _sim.Reset(Now, horizonDays: 2);

        Assert.IsGreaterThan(0, regenerated);
        Assert.IsNotNull(_store.FindByReference("SKY-REAL0000SIM2"));
        Assert.IsTrue(_store.LoadTakenSeats("PR-101", Now.Date.AddDays(1)).Contains("ECO2A"));
    }
}
