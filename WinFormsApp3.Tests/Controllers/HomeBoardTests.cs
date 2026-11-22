using Microsoft.Data.Sqlite;
using SkyWings.Controllers;

namespace SkyWings.Tests.Controllers;

/// <summary>What the home board shows and what its details panel needs, decided without a window.</summary>
[TestClass]
public sealed class HomeBoardTests
{
    private string _dbPath = "";
    private BookingStore _store = null!;
    private HomeController _controller = null!;
    private readonly DateTime _day = DateTime.Today.AddDays(30);

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"skywings-board-{Guid.NewGuid():N}.db");
        var csb = new SqliteConnectionStringBuilder { DataSource = _dbPath, ForeignKeys = true, Pooling = false };
        _store = new BookingStore(csb.ConnectionString);
        _store.SeedFlightsIfEmpty(AppData.DomesticFlights, AppData.InternationalFlights, AppData.DefaultDomesticCapacity, AppData.DefaultInternationalCapacity);
        _controller = new HomeController(_store, new StaffSession(1, "agent1", "Ana Reyes", StaffRole.Agent, false));
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    // ------------------------------------------------------------------ regions and the board query

    [TestMethod]
    [DataRow("PR-101", "Domestic")]
    [DataRow("PR-501", "Domestic")]
    [DataRow("PR-201", "Southeast Asia")]
    [DataRow("PR-602", "Southeast Asia")]
    [DataRow("PR-304", "Europe")]
    [DataRow("PR-704", "Europe")]
    [DataRow("PR-402", "North America")]
    [DataRow("PR-802", "North America")]
    [DataRow("XX-9", "Other")]
    public void Region_OfAFlight_FollowsItsNumber(string flightNo, string region)
        => Assert.AreEqual(region, FlightRegions.Of(new Flight(flightNo, "Manila (MNL)", "Somewhere", 1000m)));

    [TestMethod]
    public void TheRegionNames_AreTheFourOnTheHomeScreen()
        => CollectionAssert.AreEqual(new[] { "Domestic", "Southeast Asia", "Europe", "North America" }, FlightRegions.Names.ToArray());

    [TestMethod]
    public void Board_FilteredByRegion_ListsOnlyThatRegion()
    {
        List<FlightRow> all = _controller.LoadBoard("", "", _day);
        List<FlightRow> europe = _controller.LoadBoard("Europe", "", _day);
        Assert.IsNotEmpty(europe);
        Assert.IsLessThan(all.Count, europe.Count);
        Assert.IsTrue(europe.All(r => FlightRegions.Of(r.Flight) == "Europe"));
        Assert.IsTrue(all.All(r => r.Flight.Origin.StartsWith("Manila")), "the board lists flights leaving Manila");
    }

    [TestMethod]
    [DataRow(" ceb ")]
    [DataRow("CEBU")]
    public void Board_FilteredByText_MatchesACity_IgnoringCaseAndSpaces(string text)
    {
        List<FlightRow> rows = _controller.LoadBoard("", text, _day);
        Assert.IsNotEmpty(rows);
        Assert.IsTrue(rows.All(r => r.Flight.Destination.Contains("Cebu", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void Board_FilteredByText_MatchesAFlightNumber() => Assert.AreEqual("PR-304", _controller.LoadBoard("", "pr-304", _day).Single().FlightNo);

    [TestMethod]
    [DataRow("zzzz")]
    [DataRow("%")]
    [DataRow("' OR 1=1 --")]
    public void Board_WithNoMatch_IsEmpty_AndDoesNotThrow(string text) => Assert.IsEmpty(_controller.LoadBoard("", text, _day));

    [TestMethod]
    public void Board_TextAndRegion_Combine()
    {
        Assert.IsNotEmpty(_controller.LoadBoard("Domestic", "cebu", _day));
        Assert.IsEmpty(_controller.LoadBoard("Europe", "cebu", _day), "Cebu is not in Europe");
    }

    [TestMethod]
    public void Board_ShowsTheEconomyFare_AndTotalSeatsAcrossTheAircraftsCabins()
    {
        FlightRow row = _controller.LoadBoard("", "PR-101", _day).Single();
        Aircraft aircraft = _store.LoadAircraft("PR-101");
        Assert.AreEqual($"PHP {row.Flight.BasePrice:N0}", row.Price, "the Economy fare, from");
        int total = aircraft.Cabins.Sum(c => _store.CabinSeatsLeft("PR-101", _day, c.ClassName));
        Assert.AreEqual(total, row.Seats);
        Assert.AreEqual(total.ToString("N0"), row.SeatsLeft);
    }

    [TestMethod]
    public void Board_IsOrderedByEstimatedDeparture()
    {
        List<FlightRow> rows = _controller.LoadBoard("", "", _day);
        CollectionAssert.AreEqual(rows.Select(r => r.Status.Estimated).OrderBy(t => t).ToList(), rows.Select(r => r.Status.Estimated).ToList());
    }
}
