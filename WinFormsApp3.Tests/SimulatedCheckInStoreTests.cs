using Microsoft.Data.Sqlite;

namespace SkyWings.Tests;

/// <summary>Store support for demo check-ins: who is still to check in, saving them, loading all check-ins, and reset.</summary>
[TestClass]
[DoNotParallelize]
public sealed class SimulatedCheckInStoreTests
{
    private static readonly DateTime Now = DateTime.Today.AddHours(6);
    private string _dbPath = "";
    private BookingStore _store = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"skywings-simcheckin-{Guid.NewGuid():N}.db");
        _store = new BookingStore(new SqliteConnectionStringBuilder { DataSource = _dbPath, ForeignKeys = true, Pooling = false }.ConnectionString);
        _store.SeedDefaultFlights();
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private static BookingRecord RealBooking(string reference, string flightNo, DateTime date, string seat)
    {
        var p = new PassengerRecord("Real Person", "1 St", "0917", 35, "1991-01-01", "PSP-1", false, false,
            new SeatAssignment("Real Person", seat), Array.Empty<string>(), 4000m);
        return new BookingRecord(reference, DateTime.Now, flightNo, "Manila (MNL)", "Cebu (CEB)", date, true, "Economy",
            "Credit Card", "**** 4521", 4000m, "Scheduled", new[] { p }, AppData.StatusConfirmed, "PAID", "TXN-" + reference, DateTime.Now, 0m);
    }

    private void SaveReal(BookingRecord b)
        => _store.SaveConfirmedBooking(b, false, b.TotalAmount, "Credit Card", "**** 4521", b.PaymentTransactionRef, DateTime.Now);

    private static CheckInRecord From(SimCheckInCandidate c, DateTime at)
        => new(c.Reference, c.PassengerName, c.FlightNo, c.FlightDate, c.SeatCode, at);

    [TestMethod]
    public void Candidates_ListOnlyDemoTravellersNotYetCheckedIn()
    {
        DateTime date = Now.Date.AddDays(1);
        SaveReal(RealBooking("SKY-REALCHECKIN01", "PR-101", date, "ECO9A"));
        new SimulationService(_store, TestSim.FullLoad).EnsureLoad(Now, horizonDays: 2);

        List<SimCheckInCandidate> before = _store.LoadSimulatedCheckInCandidates(Now.Date, date);
        Assert.IsNotEmpty(before);
        Assert.IsTrue(before.All(c => _store.IsSimulatedBooking(c.Reference)), "real bookings are never candidates");
        Assert.IsTrue(before.All(c => c.SeatCode.Length > 0));

        SimCheckInCandidate first = before[0];
        Assert.AreEqual(1, _store.SaveSimulatedCheckIns(new[] { From(first, AppData.DepartureOf(first.FlightNo, first.FlightDate).AddHours(-2)) }));

        List<SimCheckInCandidate> after = _store.LoadSimulatedCheckInCandidates(Now.Date, date);
        Assert.HasCount(before.Count - 1, after);
        Assert.IsFalse(after.Any(c => c.Reference == first.Reference && c.PassengerName == first.PassengerName));
    }

    [TestMethod]
    public void Candidates_RespectTheDateRange()
    {
        new SimulationService(_store, TestSim.FullLoad).EnsureLoad(Now, horizonDays: 3);
        DateTime day = Now.Date.AddDays(1);
        Assert.IsTrue(_store.LoadSimulatedCheckInCandidates(day, day).All(c => c.FlightDate.Date == day));
    }

    [TestMethod]
    public void SaveSimulatedCheckIns_IgnoresRealReferences()
    {
        DateTime date = Now.Date.AddDays(1);
        SaveReal(RealBooking("SKY-REALCHECKIN02", "PR-101", date, "ECO9B"));

        int saved = _store.SaveSimulatedCheckIns(new[]
        {
            new CheckInRecord("SKY-REALCHECKIN02", "Real Person", "PR-101", date, "ECO9B", AppData.DepartureOf("PR-101", date).AddHours(-2))
        });

        Assert.AreEqual(0, saved);
        Assert.IsEmpty(_store.LoadCheckIns("SKY-REALCHECKIN02"));
    }

    [TestMethod]
    public void SaveSimulatedCheckIns_IgnoresRealBookings_EvenWithASkyReference()
    {
        DateTime date = Now.Date.AddDays(1);
        SaveReal(RealBooking("SKY-REALCHECKIN05", "PR-101", date, "ECO9F"));

        int saved = _store.SaveSimulatedCheckIns(new[]
        {
            new CheckInRecord("SKY-REALCHECKIN05", "Real Person", "PR-101", date, "ECO9F", AppData.DepartureOf("PR-101", date).AddHours(-2))
        });

        Assert.AreEqual(0, saved);
        Assert.IsEmpty(_store.LoadCheckIns("SKY-REALCHECKIN05"));
    }

    [TestMethod]
    public void LoadCheckInsByBooking_GroupsByReference()
    {
        new SimulationService(_store, TestSim.FullLoad).EnsureLoad(Now, horizonDays: 2);
        List<SimCheckInCandidate> candidates = _store.LoadSimulatedCheckInCandidates(Now.Date.AddDays(1), Now.Date.AddDays(1));
        SimCheckInCandidate[] pick = candidates.GroupBy(c => c.Reference).First(g => g.Count() >= 2).Take(2)
            .Append(candidates.First(c => c.Reference != candidates[0].Reference)).ToArray();
        _store.SaveSimulatedCheckIns(pick.Select(c => From(c, AppData.DepartureOf(c.FlightNo, c.FlightDate).AddHours(-1))).ToList());

        Dictionary<string, List<CheckInRecord>> all = _store.LoadCheckInsByBooking();

        foreach (IGrouping<string, SimCheckInCandidate> group in pick.GroupBy(c => c.Reference))
        {
            Assert.IsTrue(all.ContainsKey(group.Key));
            Assert.HasCount(group.Count(), all[group.Key]);
        }
        Assert.AreEqual(pick.Length, all.Values.Sum(l => l.Count));
    }

    [TestMethod]
    public void LoadCheckInsByBooking_CanLeaveDemoCheckInsOut()
    {
        DateTime date = Now.Date.AddDays(1);
        SaveReal(RealBooking("SKY-REALCHECKIN04", "PR-101", date, "ECO9E"));
        Assert.IsTrue(_store.CheckInPassenger("SKY-REALCHECKIN04", "Real Person", "PR-101", date, "ECO9E", AppData.DepartureOf("PR-101", date).AddHours(-3)));
        new SimulationService(_store, TestSim.FullLoad).EnsureLoad(Now, horizonDays: 2);
        SimCheckInCandidate demo = _store.LoadSimulatedCheckInCandidates(date, date)[0];
        _store.SaveSimulatedCheckIns(new[] { From(demo, AppData.DepartureOf(demo.FlightNo, demo.FlightDate).AddHours(-2)) });

        Dictionary<string, List<CheckInRecord>> realOnly = _store.LoadCheckInsByBooking(includeSimulated: false);

        Assert.IsTrue(realOnly.ContainsKey("SKY-REALCHECKIN04"));
        Assert.IsFalse(realOnly.Keys.Any(_store.IsSimulatedBooking));
        Assert.IsTrue(_store.LoadCheckInsByBooking().ContainsKey(demo.Reference), "by default everything is read");
    }

    [TestMethod]
    public void DeleteSimulatedBookings_RemovesDemoCheckIns_KeepsRealOnes()
    {
        DateTime date = Now.Date.AddDays(1);
        SaveReal(RealBooking("SKY-REALCHECKIN03", "PR-101", date, "ECO9C"));
        DateTime departure = AppData.DepartureOf("PR-101", date);
        Assert.IsTrue(_store.CheckInPassenger("SKY-REALCHECKIN03", "Real Person", "PR-101", date, "ECO9C", departure.AddHours(-3)));
        new SimulationService(_store, TestSim.FullLoad).EnsureLoad(Now, horizonDays: 2);
        SimCheckInCandidate demo = _store.LoadSimulatedCheckInCandidates(date, date)[0];
        _store.SaveSimulatedCheckIns(new[] { From(demo, departure.AddHours(-2)) });

        _store.DeleteSimulatedBookings();

        Dictionary<string, List<CheckInRecord>> all = _store.LoadCheckInsByBooking();
        Assert.IsTrue(all.ContainsKey("SKY-REALCHECKIN03"), "the real check-in survives");
        CollectionAssert.AreEqual(new[] { "SKY-REALCHECKIN03" }, all.Keys.ToArray(), "demo check-ins are removed with their bookings");
    }
}
