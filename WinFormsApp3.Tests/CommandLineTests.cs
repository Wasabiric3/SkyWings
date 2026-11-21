using Microsoft.Data.Sqlite;

namespace SkyWings.Tests;

/// <summary>The hidden "--reset-simulation" switch: regenerates only simulated bookings.</summary>
[TestClass]
public sealed class CommandLineTests
{
    private static readonly DateTime Now = DateTime.Today.AddHours(6);
    private string _dbPath = "";
    private BookingStore _store = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"skywings-cmdline-{Guid.NewGuid():N}.db");
        _store = new BookingStore(new SqliteConnectionStringBuilder { DataSource = _dbPath, ForeignKeys = true }.ConnectionString);
        _store.SeedDefaultFlights();
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    [TestMethod]
    public void ResetSimulation_RegeneratesOnlySimulatedBookings()
    {
        DateTime date = Now.Date.AddDays(1);
        var p = new PassengerRecord("Ana Example", "1 St", "09171234567", 35, "1991-01-01", "P1234567A", false, false,
            new SeatAssignment("Ana Example", "ECO9G"), Array.Empty<string>(), 4000m);
        var real = new BookingRecord("SKY-REALRESET0001", DateTime.Now, "PR-101", "Manila (MNL)", "Cebu (CEB)", date, true, "Economy",
            "Credit Card", "**** 4521", 4000m, "Scheduled", new[] { p }, AppData.StatusConfirmed, "PAID", "TXN-R1", DateTime.Now, 0m);
        _store.SaveConfirmedBooking(real, false, 4000m, "Credit Card", "**** 4521", "TXN-R1", DateTime.Now);
        Assert.IsTrue(_store.CheckInPassenger("SKY-REALRESET0001", "Ana Example", "PR-101", date, "ECO9G", AppData.DepartureOf("PR-101", date).AddHours(-2)));
        new SimulationService(_store, TestSim.FullLoad).EnsureLoad(Now, horizonDays: 2);
        string anySimulated = _store.LoadSimulatedCheckInCandidates(date, date)[0].Reference;

        string? message = CommandLine.Run(new[] { "--RESET-simulation" }, _store, Now);

        StringAssert.StartsWith(message, "Simulated bookings regenerated for ");
        Assert.IsNotNull(_store.FindByReference("SKY-REALRESET0001"), "the real booking survives");
        Assert.HasCount(1, _store.LoadCheckIns("SKY-REALRESET0001"), "and so does its check-in");
        Assert.IsGreaterThan(0, _store.CountSimulatedBookings(), "simulated traffic is generated again");
        Assert.IsTrue(_store.IsSimulatedBooking(anySimulated), "same flights, same (deterministic) bookings");
        Assert.IsTrue(_store.LoadAudit().Any(e => e.Action == "MAINTENANCE_RESET"), "a neutral name in the staff Audit tab");
        Assert.IsFalse(_store.LoadAudit().Any(e => e.Action.Contains("SIMULATION")));
    }

    [TestMethod]
    public void ResetSimulation_OnAnOldDatabase_LeavesItOnVersion3_WithEverythingCounted()
    {
        _store.SetSetting(BookingStore.SettingDemoActivity, "0");
        _store.SetSetting(BookingStore.SettingDemoInReports, "0");
        _store.SetSetting(BookingStore.SettingDemoLoadVersion, "2");

        CommandLine.Run(new[] { CommandLine.ResetSimulation }, _store, Now);

        Assert.AreEqual(SimulationService.LoadVersion, _store.GetSetting(BookingStore.SettingDemoLoadVersion, ""));
        Assert.IsTrue(_store.IncludeSimulatedInReports);
        Assert.IsTrue(_store.DemoActivityEnabled);
    }

    [TestMethod]
    public void ResetSimulation_OnAFreshDatabase_SeedsTheFlightsFirst()
    {
        string db = Path.Combine(Path.GetTempPath(), $"skywings-cmdline-fresh-{Guid.NewGuid():N}.db");
        try
        {
            var fresh = new BookingStore(new SqliteConnectionStringBuilder { DataSource = db, ForeignKeys = true }.ConnectionString);

            string? message = CommandLine.Run(new[] { CommandLine.ResetSimulation }, fresh, Now);

            StringAssert.DoesNotMatch(message, new System.Text.RegularExpressions.Regex(" 0 flight dates"));
            Assert.IsGreaterThan(0, fresh.CountSimulatedBookings());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(db)) File.Delete(db);
        }
    }

    [TestMethod]
    public void BookingsHaveAnIndexOnFlightDate()
    {
        // The staff Bookings tab reads one flight date at a time; without this every load scans the whole table.
        using var con = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _dbPath }.ConnectionString);
        con.Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM pragma_index_list('bookings') l JOIN pragma_index_info(l.name) i " +
                          "WHERE i.seqno = 0 AND i.name = 'flight_date'";
        Assert.IsGreaterThan(0L, (long)cmd.ExecuteScalar()!);
    }

    [TestMethod]
    public void UnknownArguments_DoNothing()
    {
        Assert.IsNull(CommandLine.Run(Array.Empty<string>(), _store, Now));
        Assert.IsNull(CommandLine.Run(new[] { "--something-else" }, _store, Now));
        Assert.AreEqual(0, _store.CountSimulatedBookings());
    }
}
