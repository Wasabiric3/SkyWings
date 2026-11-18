using Microsoft.Data.Sqlite;
using SkyWings.Controllers;

namespace SkyWings.Tests;

/// <summary>Per-cabin seat counts, searches and class choices follow the flight's aircraft.</summary>
[TestClass]
public sealed class AircraftSeatCountTests
{
    private string _dbPath = "";
    private BookingStore _store = null!;
    private readonly DateTime _date = DateTime.Today.AddDays(30);

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"skywings-acs-{Guid.NewGuid():N}.db");
        _store = new BookingStore(new SqliteConnectionStringBuilder { DataSource = _dbPath, ForeignKeys = true }.ConnectionString);
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private void Seed(bool aircraft)
        => _store.SeedFlightsIfEmpty(AppData.DomesticFlights, AppData.InternationalFlights, 180, 220, aircraft);

    [TestMethod]
    public void CabinSeatsLeft_FollowsTheAircraftsCabins()
    {
        Seed(aircraft: true);

        Assert.AreEqual(168, _store.CabinSeatsLeft("PR-101", _date, "Economy"));
        Assert.AreEqual(12, _store.CabinSeatsLeft("PR-101", _date, "Business"));
        Assert.AreEqual(0, _store.CabinSeatsLeft("PR-101", _date, "First Class"), "an A320 has no First cabin");
        Assert.AreEqual(8, _store.CabinSeatsLeft("PR-301", _date, "First Class"));
        Assert.AreEqual(28, _store.CabinSeatsLeft("PR-201", _date, "Premium Economy"));
    }

    [TestMethod]
    public void CabinSeatsLeft_ForALegacyFlight_IsUnchanged()
    {
        Seed(aircraft: false);
        int expected = SeatLayout.RowCount("Economy", 180) * AppData.ClassSeatLetters[0].Length;
        Assert.AreEqual(expected, _store.CabinSeatsLeft("PR-101", _date, "Economy"));
        Assert.IsGreaterThan(0, _store.CabinSeatsLeft("PR-101", _date, "First Class"), "legacy flights have all four cabins");
    }

    [TestMethod]
    public void Search_LeavesOutFlightsWhoseAircraftHasNoSuchCabin()
    {
        Seed(aircraft: true);

        List<FlightRow> first = FlightSearch.Search(_store, null, _date, "First Class");
        List<FlightRow> business = FlightSearch.Search(_store, null, _date, "Business");

        Assert.IsNotEmpty(first);
        Assert.IsTrue(first.All(r => AppData_IsLongHaul(r.FlightNo)), "First Class exists only on the 777 flights");
        Assert.IsTrue(business.Any(r => r.FlightNo == "PR-101"), "an A320 has Business");
        Assert.IsFalse(first.Any(r => r.SeatsLeft == "Sold out"), "a missing cabin is not 'sold out'");
    }

    private static bool AppData_IsLongHaul(string flightNo) => Aircraft.DefaultFor(flightNo, false).Id == "B777";

    [TestMethod]
    public void AvailableClasses_ListsTheAircraftsCabins_InTheUsualOrder()
    {
        Seed(aircraft: true);
        var controller = new BookingWizardController(_store);
        Flight domestic = _store.LoadActiveFlights(true).First(f => f.FlightNo == "PR-101");
        Flight longHaul = _store.LoadActiveFlights(false).First(f => f.FlightNo == "PR-301");

        CollectionAssert.AreEqual(new[] { "Economy", "Business" }, controller.AvailableClasses(domestic, null).ToArray());
        CollectionAssert.AreEqual(AppData.ClassNames, controller.AvailableClasses(longHaul, null).ToArray());
    }

    [TestMethod]
    public void AvailableClasses_WithAReturnFlight_KeepsOnlyClassesBothLegsHave()
    {
        Seed(aircraft: true);
        var controller = new BookingWizardController(_store);
        Flight outbound = _store.LoadActiveFlights(false).First(f => f.FlightNo == "PR-301");   // 777: four cabins
        Flight back = _store.LoadActiveFlights(true).First(f => f.FlightNo == "PR-101");         // A320: two cabins

        CollectionAssert.AreEqual(new[] { "Economy", "Business" }, controller.AvailableClasses(outbound, back).ToArray());
    }

    [TestMethod]
    public void SeatCabin_ReturnsTheLayoutForTheSessionsClass_OrNull()
    {
        Seed(aircraft: true);
        var controller = new BookingWizardController(_store);
        Flight domestic = _store.LoadActiveFlights(true).First(f => f.FlightNo == "PR-101");

        controller.Session.ClassName = "Business";
        Assert.AreEqual("2-2", controller.SeatCabin(domestic)!.LayoutText);
        controller.Session.ClassName = "First Class";
        Assert.IsNull(controller.SeatCabin(domestic));
    }
}
