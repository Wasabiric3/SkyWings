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
        new SimulationService(_store).EnsureLoad(Now, horizonDays: 2);
        string anySimulated = _store.LoadSimulatedCheckInCandidates(date, date)[0].Reference;

        string? message = CommandLine.Run(new[] { "--RESET-simulation" }, _store, Now);

        StringAssert.StartsWith(message, "Simulated bookings regenerated for ");
        Assert.IsNotNull(_store.FindByReference("SKY-REALRESET0001"), "the real booking survives");
        Assert.HasCount(1, _store.LoadCheckIns("SKY-REALRESET0001"), "and so does its check-in");
        Assert.IsGreaterThan(0, _store.CountSimulatedBookings(), "simulated traffic is generated again");
        Assert.IsTrue(_store.IsSimulatedBooking(anySimulated), "same flights, same (deterministic) bookings");
        Assert.IsTrue(_store.LoadAudit().Any(e => e.Action == "SIMULATION_RESET"));
    }

    [TestMethod]
    public void UnknownArguments_DoNothing()
    {
        Assert.IsNull(CommandLine.Run(Array.Empty<string>(), _store, Now));
        Assert.IsNull(CommandLine.Run(new[] { "--something-else" }, _store, Now));
        Assert.AreEqual(0, _store.CountSimulatedBookings());
    }
}
