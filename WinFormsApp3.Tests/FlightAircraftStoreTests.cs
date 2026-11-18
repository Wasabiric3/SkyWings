using Microsoft.Data.Sqlite;

namespace SkyWings.Tests;

/// <summary>Flights can carry an aircraft; flights without one keep the original ratio-based layout.</summary>
[TestClass]
public sealed class FlightAircraftStoreTests
{
    private string _dbPath = "";
    private string _connection = "";

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"skywings-ac-{Guid.NewGuid():N}.db");
        _connection = new SqliteConnectionStringBuilder { DataSource = _dbPath, ForeignKeys = true }.ConnectionString;
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private BookingStore Seeded(bool assignAircraft)
    {
        var store = new BookingStore(_connection);
        store.SeedFlightsIfEmpty(AppData.DomesticFlights, AppData.InternationalFlights, 180, 220, assignAircraft);
        return store;
    }

    private static int CapacityOf(BookingStore store, string flightNo) => store.LoadAllFlights().Single(f => f.FlightNo == flightNo).Capacity;

    [TestMethod]
    public void SeededWithAircraft_EachRouteGroupGetsItsDefault_AndCapacityMatchesTheSeats()
    {
        BookingStore store = Seeded(assignAircraft: true);

        Assert.AreEqual("A320", store.LoadAircraft("PR-101").Id);
        Assert.AreEqual("A320", store.LoadAircraft("PR-511").Id, "a domestic return leg uses the same type");
        Assert.AreEqual("A330", store.LoadAircraft("PR-201").Id);
        Assert.AreEqual("B777", store.LoadAircraft("PR-301").Id);
        Assert.AreEqual("B777", store.LoadAircraft("PR-401").Id);
        Assert.AreEqual(Aircraft.A320.TotalSeats, CapacityOf(store, "PR-101"));
        Assert.AreEqual(Aircraft.A330.TotalSeats, CapacityOf(store, "PR-201"));
        Assert.AreEqual(Aircraft.B777.TotalSeats, CapacityOf(store, "PR-301"));
        Assert.AreEqual("A320", store.LoadAllFlights().Single(f => f.FlightNo == "PR-101").AircraftId);
    }

    [TestMethod]
    public void SeededWithoutAircraft_KeepsTheLegacyRatioLayout()
    {
        BookingStore store = Seeded(assignAircraft: false);

        Aircraft a = store.LoadAircraft("PR-101");

        Assert.AreEqual(Aircraft.LegacyId, a.Id);
        Assert.AreEqual(SeatLayout.RowCount("Economy", 180), a.Cabin("Economy")!.Rows);
        Assert.AreEqual(180, CapacityOf(store, "PR-101"));
        Assert.IsNull(store.LoadAllFlights().Single(f => f.FlightNo == "PR-101").AircraftId);
    }

    [TestMethod]
    public void SeedingAgainWithTheFlag_DoesNotChangeFlightsThatAlreadyExist()
    {
        Seeded(assignAircraft: false);
        BookingStore again = Seeded(assignAircraft: true);

        Assert.AreEqual(Aircraft.LegacyId, again.LoadAircraft("PR-101").Id, "existing flights are left alone (INSERT OR IGNORE)");
        Assert.AreEqual(180, CapacityOf(again, "PR-101"));
    }

    [TestMethod]
    public void SetFlightAircraft_ChangesTheLayoutAndCapacity_AndNullGoesBackToLegacy()
    {
        BookingStore store = Seeded(assignAircraft: false);

        store.SetFlightAircraft("PR-101", "B777");
        Assert.AreEqual("B777", store.LoadAircraft("PR-101").Id);
        Assert.AreEqual(Aircraft.B777.TotalSeats, CapacityOf(store, "PR-101"));

        store.SetFlightAircraft("PR-101", null);
        Assert.AreEqual(Aircraft.LegacyId, store.LoadAircraft("PR-101").Id);
        Assert.AreEqual(Aircraft.B777.TotalSeats, CapacityOf(store, "PR-101"), "capacity is kept when the aircraft is cleared");
    }

    [TestMethod]
    public void UnknownAircraftId_IsTreatedAsLegacy_NotACrash()
    {
        BookingStore store = Seeded(assignAircraft: false);
        store.SetFlightAircraft("PR-101", "NOPE");
        Assert.AreEqual(Aircraft.LegacyId, store.LoadAircraft("PR-101").Id);
    }

    [TestMethod]
    public void UnknownFlight_FallsBackToALegacyLayout()
        => Assert.AreEqual(Aircraft.LegacyId, Seeded(false).LoadAircraft("ZZ-999").Id);

    [TestMethod]
    public void AnOldDatabaseWithoutTheColumn_IsUpgradedAndStillWorks()
    {
        using (var con = new SqliteConnection(_connection))
        {
            con.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText =
                "CREATE TABLE flights (flight_no VARCHAR(10) PRIMARY KEY, origin VARCHAR(60) NOT NULL, destination VARCHAR(60) NOT NULL," +
                " base_price DECIMAL(10,2) NOT NULL, domestic INTEGER NOT NULL, capacity INT NOT NULL, active INTEGER NOT NULL DEFAULT 1," +
                " departure_time TIME NOT NULL DEFAULT '08:00:00');" +
                "INSERT INTO flights VALUES ('PR-101','Manila (MNL)','Cebu (CEB)',3000,1,150,1,'08:00:00');";
            cmd.ExecuteNonQuery();
        }

        var store = new BookingStore(_connection);   // opening upgrades the schema

        Aircraft a = store.LoadAircraft("PR-101");
        Assert.AreEqual(Aircraft.LegacyId, a.Id);
        Assert.AreEqual(SeatLayout.RowCount("Economy", 150), a.Cabin("Economy")!.Rows);
        store.SetFlightAircraft("PR-101", "A320");
        Assert.AreEqual("A320", store.LoadAircraft("PR-101").Id);
    }
}

[TestClass]
public sealed class SeedDefaultFlightsTests
{
    private string _dbPath = "";
    private string _connection = "";

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"skywings-sdf-{Guid.NewGuid():N}.db");
        _connection = new SqliteConnectionStringBuilder { DataSource = _dbPath, ForeignKeys = true }.ConnectionString;
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    [TestMethod]
    public void ABrandNewDatabase_GetsRealisticAircraft()
    {
        var store = new BookingStore(_connection);
        Assert.IsFalse(store.HasFlights());

        store.SeedDefaultFlights();

        Assert.IsTrue(store.HasFlights());
        Assert.AreEqual("A320", store.LoadAircraft("PR-101").Id);
        Assert.AreEqual("A330", store.LoadAircraft("PR-201").Id);
        Assert.AreEqual("B777", store.LoadAircraft("PR-301").Id);
    }

    [TestMethod]
    public void AnExistingDatabase_KeepsItsLegacyLayouts_EvenWhenSeededAgain()
    {
        var store = new BookingStore(_connection);
        store.SeedFlightsIfEmpty(AppData.DomesticFlights, AppData.InternationalFlights, 180, 220);   // an older install

        store.SeedDefaultFlights();

        Assert.AreEqual(Aircraft.LegacyId, store.LoadAircraft("PR-101").Id);
        Assert.AreEqual(Aircraft.LegacyId, store.LoadAircraft("PR-301").Id);
    }

    [TestMethod]
    public void SeedingTwice_IsHarmless()
    {
        var store = new BookingStore(_connection);
        store.SeedDefaultFlights();
        store.SeedDefaultFlights();
        Assert.AreEqual("A320", store.LoadAircraft("PR-101").Id);
    }
}
