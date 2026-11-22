using Microsoft.Data.Sqlite;

namespace SkyWings.Tests;

/// <summary>The daily booking budget of the demo load, and the simulated waitlist that fills the staff Waitlist tab.</summary>
[TestClass]
[DoNotParallelize]
public sealed class SimulationBudgetAndWaitlistTests
{
    private static readonly DateTime Now = DateTime.Today.AddHours(6);
    private string _dbPath = "";
    private BookingStore _store = null!;
    private SimulationService _sim = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"skywings-simbudget-{Guid.NewGuid():N}.db");
        _store = new BookingStore(new SqliteConnectionStringBuilder { DataSource = _dbPath, ForeignKeys = true, Pooling = false }.ConnectionString);
        _store.SeedDefaultFlights();
        _sim = new SimulationService(_store);   // the real defaults: a 500-booking day
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private int Scalar(string sql, params (string, object)[] args)
    {
        using var con = new SqliteConnection(_store.ConnectionString);
        con.Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (name, value) in args) cmd.Parameters.AddWithValue(name, value);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private int ConfirmedOn(DateTime date)
        => Scalar("SELECT COUNT(*) FROM bookings WHERE is_simulated = 1 AND status = 'CONFIRMED' AND flight_date = @d", ("@d", date.Date));

    // ------------------------------------------------------------------ daily booking budget

    [TestMethod]
    public void EnsureLoad_KeepsEachDayNearTheDailyBudget()
    {
        _sim.EnsureLoad(Now, horizonDays: 5);

        for (int d = 0; d < 5; d++)
        {
            int confirmed = ConfirmedOn(Now.Date.AddDays(d));
            // Stops after the flight that crosses 500, so a day lands a little over it, never at 13,000.
            Assert.IsTrue(confirmed is >= 500 and < 700, $"day {d}: {confirmed} bookings");
        }
    }

    [TestMethod]
    public void EnsureLoad_DoesNotTopUpADayThatReachedItsBudget()
    {
        _sim.EnsureLoad(Now, horizonDays: 2);
        int before = ConfirmedOn(Now.Date);

        int again = _sim.EnsureLoad(Now, horizonDays: 2);

        Assert.AreEqual(0, again);
        Assert.AreEqual(before, ConfirmedOn(Now.Date));
    }

    [TestMethod]
    public void EnsureLoad_EveryDayLoadsAFlightWithASoldOutCabin_ForTheWaitlist()
    {
        _sim.EnsureLoad(Now, horizonDays: 7);

        for (int d = 0; d < 7; d++)
        {
            DateTime date = Now.Date.AddDays(d);
            bool anySoldOut = _store.LoadActiveFlights(true).Concat(_store.LoadActiveFlights(false))
                .Any(f => SimulationService.IsSoldOutDay(f.FlightNo, date)
                          && SimulationService.SoldOutCabinName(_store.LoadAircraft(f.FlightNo)) is { } cabin
                          && _store.CabinSeatsLeft(f.FlightNo, date, cabin) == 0);
            Assert.IsTrue(anySoldOut, $"day {d} has no sold-out cabin");
        }
    }

    [TestMethod]
    public void EnsureLoad_IsStillDeterministic_WithTheBudget()
    {
        _sim.EnsureLoad(Now, horizonDays: 2);
        string dbPath2 = Path.Combine(Path.GetTempPath(), $"skywings-simbudget2-{Guid.NewGuid():N}.db");
        try
        {
            var other = new BookingStore(new SqliteConnectionStringBuilder { DataSource = dbPath2, ForeignKeys = true, Pooling = false }.ConnectionString);
            other.SeedDefaultFlights();
            new SimulationService(other).EnsureLoad(Now, horizonDays: 2);

            CollectionAssert.AreEquivalent(_store.LoadSimulatedFlightDates().ToArray(), other.LoadSimulatedFlightDates().ToArray());
        }
        finally { if (File.Exists(dbPath2)) File.Delete(dbPath2); }
    }

    [TestMethod]
    public void Reset_AllowsTheDaysToBeLoadedAgain()
    {
        _sim.EnsureLoad(Now, horizonDays: 2);

        int regenerated = _sim.Reset(Now, horizonDays: 2);

        Assert.IsGreaterThan(0, regenerated);
        Assert.IsGreaterThanOrEqualTo(500, ConfirmedOn(Now.Date));
    }

    // ------------------------------------------------------------------ simulated waitlist

    [TestMethod]
    public void SeedWaitlist_FillsSoldOutCabins_InJoinOrder_OnlyOnce()
    {
        _sim.EnsureLoad(Now, horizonDays: 7);

        int added = _sim.SeedWaitlist(Now);

        Assert.IsGreaterThan(0, added);
        List<WaitlistEntry> live = _store.LoadAllLiveWaitlist();
        Assert.IsNotEmpty(live);
        Assert.IsTrue(live.All(e => e.IsSimulated && e.Email.EndsWith("@example.com", StringComparison.Ordinal)));
        foreach (var cabin in live.GroupBy(e => (e.FlightNo, e.FlightDate, e.ClassName)))
        {
            bool economy = cabin.Key.ClassName == "Economy";
            Assert.IsTrue(economy ? cabin.Count() is >= 6 and <= 10 : cabin.Count() is >= 3 and <= 6, $"{cabin.Key}: {cabin.Count()} entries");
            var ordered = cabin.OrderBy(e => e.Position).ToList();
            CollectionAssert.AreEqual(ordered.OrderBy(e => e.AddedAt).ToList(), ordered, "position 1 joined first");
            Assert.IsTrue(cabin.All(e => e.SeatsWanted is >= 1 and <= 4));
        }
        Assert.AreEqual(0, _sim.SeedWaitlist(Now), "a cabin is seeded once");
    }

    [TestMethod]
    public void SeedWaitlist_NeverTouchesARealEntry_AndResetRemovesOnlySimulatedOnes()
    {
        _sim.EnsureLoad(Now, horizonDays: 7);
        _sim.SeedWaitlist(Now);
        WaitlistEntry anySimulated = _store.LoadAllLiveWaitlist().First();
        WaitlistEntry real = _store.JoinWaitlist(anySimulated.FlightNo, anySimulated.FlightDate, anySimulated.ClassName,
            "Ana Real", "ana.real@example.org", 1)!;
        Assert.IsFalse(real.IsSimulated);

        _store.DeleteSimulatedBookings();

        Assert.IsNotNull(_store.FindWaitlistEntry(real.BookingReference), "the real entry is kept");
        Assert.IsTrue(_store.LoadAllLiveWaitlist().All(e => !e.IsSimulated), "every simulated entry is gone");
    }

    [TestMethod]
    public void FreeingASeatInASoldOutCabin_OffersItToTheFirstSimulatedTraveller_WithoutAnEmail()
    {
        _sim.EnsureLoad(Now, horizonDays: 7);
        _sim.SeedWaitlist(Now);
        var emails = new List<WaitlistEntry>();
        _store.WaitlistOfferMade += emails.Add;
        // At least two days out: cancellation offers seats against the real clock, and an offer is never made inside the booking cutoff.
        WaitlistEntry first = _store.LoadAllLiveWaitlist().Where(e => e.Position == 1 && e.FlightDate >= Now.Date.AddDays(2))
            .OrderBy(e => e.FlightDate).First();
        string[] refs = _store.LoadSimulatedConfirmedReferences(first.FlightNo, first.FlightDate, first.ClassName).ToArray();
        bool offered = false;
        foreach (string reference in refs)
        {
            _sim.CancelSimulatedBooking(reference);
            if (_store.LoadAllLiveWaitlist().Any(e => e.Status == BookingStore.WaitlistOffered && e.FlightNo == first.FlightNo
                    && e.FlightDate == first.FlightDate && e.ClassName == first.ClassName)) { offered = true; break; }
        }

        Assert.IsTrue(offered, "a cancellation eventually frees enough seats for someone in the queue");
        Assert.IsEmpty(emails, "demo travellers are never emailed");
    }

    [TestMethod]
    public void Tick_CanAddWaitlistTravellers_AndFreeSeatsForThem()
    {
        _sim.EnsureLoad(Now, horizonDays: 7);
        _sim.SeedWaitlist(Now);
        var eager = new SimulationService(_store, new SimulationOptions(WaitlistJoinChance: 1.0, WaitlistFreeChance: 1.0));

        int joined = 0, freed = 0;
        for (int i = 0; i < 6; i++)
        {
            SimulationTick t = eager.Tick(Now, seed: i);
            joined += t.WaitlistJoined;
            freed += t.WaitlistSeatsFreed;
        }

        Assert.IsGreaterThan(0, joined);
        Assert.IsGreaterThan(0, freed);
        Assert.IsTrue(_store.LoadAllLiveWaitlist().Any(e => e.Status == BookingStore.WaitlistOffered), "a freed seat produced an offer");
    }

    [TestMethod]
    public void SeedWaitlist_IncludesAnEconomyQueue_AndJoinTimesAreSpreadOverDays()
    {
        _sim.EnsureLoad(Now, horizonDays: 14);
        _sim.SeedWaitlist(Now);
        List<WaitlistEntry> live = _store.LoadAllLiveWaitlist();

        Assert.IsTrue(live.Any(e => e.ClassName == "Economy"), "a busy flight has an Economy waitlist");
        Assert.IsTrue(live.Any(e => e.ClassName != "Economy"), "and premium cabins still have theirs");
        // Not everyone joined in the same few hours: across the queues some people have waited a day or more...
        Assert.IsTrue(live.Any(e => Now - e.AddedAt > TimeSpan.FromHours(24)), "some entries are more than a day old");
        // ...and each far-off flight's queue is spread out, not bunched into one afternoon.
        var far = live.Where(e => e.FlightDate >= Now.Date.AddDays(4)).GroupBy(e => (e.FlightNo, e.FlightDate, e.ClassName)).ToList();
        Assert.IsNotEmpty(far, "a queue on a flight a few days away");
        Assert.IsTrue(far.Any(g => g.Max(e => e.AddedAt) - g.Min(e => e.AddedAt) > TimeSpan.FromHours(12)), "joins are spread over many hours");
        Assert.IsTrue(live.Select(e => e.PassengerName).Distinct().Count() > live.Count * 0.8, "names rarely repeat");
    }

    [TestMethod]
    public void OfferedDemoTravellers_BookDeclineOrStaySilent_AccordingToTheirFate()
    {
        _sim.EnsureLoad(Now, horizonDays: 7);
        _sim.SeedWaitlist(Now);
        // Offers are made against the real clock, so use a cabin on a flight at least two days away.
        WaitlistEntry first = _store.LoadAllLiveWaitlist().Where(e => e.Position == 1 && e.FlightDate >= Now.Date.AddDays(2))
            .OrderBy(e => e.FlightDate).First();
        foreach (string reference in _store.LoadSimulatedConfirmedReferences(first.FlightNo, first.FlightDate, first.ClassName))
            _sim.CancelSimulatedBooking(reference);   // every seat of the cabin comes free: everyone in line gets an offer
        List<WaitlistEntry> offers = _store.LoadSimulatedOffers().Where(e => e.FlightNo == first.FlightNo && e.FlightDate == first.FlightDate
            && e.ClassName == first.ClassName).ToList();
        Assert.IsNotEmpty(offers);
        Assert.IsTrue(offers.All(e => e.OfferedAt.HasValue), "the time of the offer is recorded");

        int answered = _sim.ProgressWaitlistOffers(DateTime.Now.AddMinutes(-5));
        Assert.AreEqual(0, answered, "nobody answers before a couple of minutes have passed");

        _sim.ProgressWaitlistOffers(DateTime.Now.AddMinutes(60));

        foreach (WaitlistEntry offer in offers)
        {
            WaitlistEntry after = _store.FindWaitlistEntry(offer.BookingReference)!;
            int fate = SimulationService.WaitlistFate(offer.BookingReference);
            if (fate < 60)
            {
                Assert.AreEqual(BookingStore.WaitlistConverted, after.Status, $"{offer.PassengerName} (fate {fate}) books");
                Assert.IsTrue(_store.LoadBookings().Any(b => b.FlightNo == offer.FlightNo && b.FlightDateValue.Date == offer.FlightDate.Date
                    && b.ClassName == offer.ClassName && b.Passengers.Any(p => p.Name == offer.PassengerName)), "and a booking appears");
            }
            else if (fate < 75) Assert.AreEqual(BookingStore.WaitlistCanceled, after.Status, $"{offer.PassengerName} (fate {fate}) declines");
            else Assert.AreEqual(BookingStore.WaitlistOffered, after.Status, $"{offer.PassengerName} (fate {fate}) never answers");
        }
    }

    private AdminService AdminSession()
    {
        StaffAccount staff = _store.CreateStaff("freeseat", "Free Seat", "Free-Seat-Pass1!", StaffRole.Admin, false);
        return new AdminService(_store, new StaffSession(staff.Id, staff.Username, staff.DisplayName, staff.Role, false));
    }

    [TestMethod]
    public void FreeASeat_OffersTheSeatsToSomeoneInLineWhoFits_AndIsAudited()
    {
        _sim.EnsureLoad(Now, horizonDays: 7);
        _sim.SeedWaitlist(Now);
        // At least two days out: offers are made against the real clock and never inside the booking cutoff.
        WaitlistEntry first = _store.LoadAllLiveWaitlist().Where(e => e.Position == 1 && e.FlightDate >= Now.Date.AddDays(2))
            .OrderBy(e => e.FlightDate).First();
        AdminService admin = AdminSession();

        WaitlistSeatRelease? release = admin.ReleaseSeatForWaitlist(first.FlightNo, first.FlightDate, first.ClassName);

        Assert.IsNotNull(release);
        Assert.IsGreaterThan(0, release.SeatsFreed);
        Assert.IsNotNull(release.OfferedTo, "somebody in line was offered the seats");
        Assert.AreEqual(BookingStore.WaitlistOffered, _store.FindWaitlistEntry(release.OfferedTo.BookingReference)!.Status);
        Assert.IsLessThanOrEqualTo(release.SeatsFreed, release.OfferedTo.SeatsWanted, "the party fits the seats freed");
        if (first.SeatsWanted <= release.SeatsFreed)
            Assert.AreEqual(first.BookingReference, release.OfferedTo.BookingReference, "the first in line comes first when they fit");
        Assert.IsTrue(_store.LoadAudit().Any(a => a.Action == "SEAT_RELEASED"), "the staff action is in the audit log");
    }

    [TestMethod]
    public void FreeASeat_NeverCancelsARealCustomersBooking()
    {
        var p = new PassengerRecord("Real Person", "1 St", "0917", 35, "1991-01-01", "PSP-1", false, false,
            new SeatAssignment("Real Person", "ECO1A"), Array.Empty<string>(), 4000m);
        var real = new BookingRecord("SKY-REALFREESEAT", DateTime.Now, "PR-101", "Manila (MNL)", "Cebu (CEB)", Now.Date.AddDays(3), true,
            "Economy", "Credit Card", "**** 4521", 4000m, "Scheduled", new[] { p }, AppData.StatusConfirmed, "PAID", "TXN-REAL", DateTime.Now, 0m);
        _store.SaveConfirmedBooking(real, false, real.TotalAmount, "Credit Card", "**** 4521", real.PaymentTransactionRef, DateTime.Now);

        WaitlistSeatRelease? release = AdminSession().ReleaseSeatForWaitlist("PR-101", Now.Date.AddDays(3), "Economy");

        Assert.IsNull(release, "the only booking in that cabin is a real one");
        Assert.AreEqual(AppData.StatusConfirmed, _store.FindByReference("SKY-REALFREESEAT")!.Status);
    }

    [TestMethod]
    public void TheSameCode_AlwaysHasTheSameFate_AndTheFatesAreMixed()
    {
        Assert.AreEqual(SimulationService.WaitlistFate("WL-ABCD1234"), SimulationService.WaitlistFate("WL-ABCD1234"));
        int[] fates = Enumerable.Range(0, 400).Select(i => SimulationService.WaitlistFate($"WL-{i:X8}")).ToArray();
        Assert.IsTrue(fates.Count(f => f < 60) is > 190 and < 290, "about 60% book");
        Assert.IsTrue(fates.Count(f => f >= 75) is > 60 and < 140, "about 25% never answer");
    }

    [TestMethod]
    public void AnOlderWaitlistTable_GainsTheSimulatedColumn_AndKeepsItsRows()
    {
        // A database from before the column existed: the table has no is_simulated, and one real entry in it.
        string oldPath = Path.Combine(Path.GetTempPath(), $"skywings-oldwl-{Guid.NewGuid():N}.db");
        try
        {
            string connection = new SqliteConnectionStringBuilder { DataSource = oldPath, ForeignKeys = true, Pooling = false }.ConnectionString;
            using (var con = new SqliteConnection(connection))
            {
                con.Open();
                using var cmd = con.CreateCommand();
                cmd.CommandText =
                    "CREATE TABLE waitlist_entries (id INTEGER PRIMARY KEY AUTOINCREMENT, booking_ref VARCHAR(20) NOT NULL, flight_no VARCHAR(10) NOT NULL, " +
                    "flight_date DATE NOT NULL, added_at DATETIME NOT NULL, status VARCHAR(16) NOT NULL DEFAULT 'WAITING', UNIQUE(booking_ref, flight_no, flight_date));" +
                    "INSERT INTO waitlist_entries (booking_ref,flight_no,flight_date,added_at,status) VALUES ('WL-OLD00001','PR-301','2030-01-01','2029-12-01','WAITING');";
                cmd.ExecuteNonQuery();
            }

            var store = new BookingStore(connection);

            WaitlistEntry? old = store.FindWaitlistEntry("WL-OLD00001");
            Assert.IsNotNull(old);
            Assert.IsFalse(old.IsSimulated, "existing rows are real");
        }
        finally { if (File.Exists(oldPath)) File.Delete(oldPath); }
    }
}
