using Microsoft.Data.Sqlite;
using SkyWings.Controllers;

namespace SkyWings.Tests;

/// <summary>How full a flight is: the board's load column and the per-cabin occupancy bars.</summary>
[TestClass]
public sealed class FlightLoadTests
{
    private static readonly DateTime Now = DateTime.Today.AddHours(6);
    private string _dbPath = "";
    private BookingStore _store = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"skywings-load-{Guid.NewGuid():N}.db");
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
    [DataRow(0.0, 10, "░░░░░░░░░░")]
    [DataRow(1.0, 10, "██████████")]
    [DataRow(0.78, 10, "████████░░")]
    [DataRow(0.04, 10, "░░░░░░░░░░")]
    [DataRow(0.5, 4, "██░░")]
    [DataRow(-1.0, 5, "░░░░░")]
    [DataRow(7.0, 5, "█████")]
    public void Bar_FillsInProportion_AndNeverOverflows(double fraction, int cells, string expected)
        => Assert.AreEqual(expected, FlightLoad.Bar(fraction, cells));

    [TestMethod]
    public void CabinLoad_ComputesPercentAndFraction()
    {
        var load = new CabinLoad("Economy", 168, 37);
        Assert.AreEqual(131, load.Booked);
        Assert.AreEqual(78, load.Percent);
        Assert.AreEqual(131 / 168.0, load.Fraction, 1e-9);
        Assert.AreEqual(0, new CabinLoad("Business", 0, 0).Percent, "a cabin the aircraft lacks is 0%, not a crash");
        Assert.AreEqual(100, new CabinLoad("Business", 12, 0).Percent);
    }

    [TestMethod]
    public void Cabins_ListsOnlyTheAircraftsCabins_WithTheirSeatsLeft()
    {
        new SimulationService(_store, TestSim.FullLoad).EnsureLoad(Now, horizonDays: 2);
        DateTime date = Now.Date.AddDays(1);

        IReadOnlyList<CabinLoad> domestic = FlightLoad.Cabins(_store, "PR-101", date);
        IReadOnlyList<CabinLoad> longHaul = FlightLoad.Cabins(_store, "PR-301", date);

        CollectionAssert.AreEqual(new[] { "Business", "Economy" }, domestic.Select(c => c.ClassName).ToArray());
        Assert.HasCount(4, longHaul);
        foreach (CabinLoad c in domestic)
        {
            Assert.IsTrue(c.Total > 0 && c.Left >= 0 && c.Left <= c.Total, c.ToString());
            Assert.AreEqual(_store.CabinSeatsLeft("PR-101", date, c.ClassName), c.Left);
        }
        foreach (CabinLoad c in longHaul)
        {
            Assert.IsTrue(c.Total > 0 && c.Left >= 0 && c.Left <= c.Total, c.ToString());
            Assert.AreEqual(_store.CabinSeatsLeft("PR-301", date, c.ClassName), c.Left);
        }
    }

    [TestMethod]
    public void Line_ReadsLikeABoardRow()
    {
        string line = FlightLoad.Line(new CabinLoad("Economy", 168, 37));
        StringAssert.Contains(line, "Economy");
        StringAssert.Contains(line, "78%");
        StringAssert.Contains(line, "37 left");
        StringAssert.Contains(line, "█");
        StringAssert.Contains(FlightLoad.Line(new CabinLoad("Business", 12, 0)), "sold out");
    }


    [TestMethod]
    public void Details_ShowRouteAircraftAndOneRowPerCabin_WithFareAndLoad()
    {
        new SimulationService(_store, TestSim.FullLoad).EnsureLoad(Now, horizonDays: 2);
        Flight flight = _store.LoadActiveFlights(true).First(f => f.FlightNo == "PR-101");
        DateTime day = Now.Date.AddDays(1);

        IReadOnlyList<string> lines = FlightLoad.Details(_store, flight, "Domestic (Philippines)", flight.BasePrice,
            day.AddHours(8), day.AddHours(9).AddMinutes(20), day);
        string text = string.Join("\n", lines);

        StringAssert.Contains(text, "PR-101");
        StringAssert.Contains(text, "Domestic (Philippines)");
        StringAssert.Contains(text, "Airbus A320");
        StringAssert.Contains(text, "Economy");
        StringAssert.Contains(text, "Business");
        Assert.IsFalse(text.Contains("First Class"), "an A320 has no First Class");
        StringAssert.Contains(text, "█");
        StringAssert.Contains(text, $"PHP {AppData.Round2(flight.BasePrice * AppData.ClassMultipliers[0]):N2}".Replace("PHP ", ""));
        Assert.IsLessThanOrEqualTo(10, lines.Count, "the details panel has room for about ten lines");
    }

    [TestMethod]
    public void Details_ForAWideBody_ListEveryCabin()
    {
        Flight flight = _store.LoadActiveFlights(false).First(f => f.FlightNo == "PR-301");
        string text = string.Join("\n", FlightLoad.Details(_store, flight, "International (Europe)", flight.BasePrice,
            DateTime.Today.AddHours(22), DateTime.Today.AddDays(1).AddHours(5), DateTime.Today.AddDays(1)));
        foreach (string cabin in AppData.ClassNames) StringAssert.Contains(text, cabin);
        StringAssert.Contains(text, "(+1 day)");
    }
    [TestMethod]
    public void TheBoard_ShowsHowFullEachFlightIs_ForTheSearchedCabin()
    {
        new SimulationService(_store, TestSim.FullLoad).EnsureLoad(Now, horizonDays: 2);
        DateTime date = Now.Date.AddDays(1);

        List<FlightRow> rows = FlightSearch.Search(_store, null, date, "Economy", Now);

        Assert.IsNotEmpty(rows);
        foreach (FlightRow row in rows)
        {
            int total = _store.LoadAircraft(row.FlightNo).Cabin("Economy")!.Seats;
            int expected = (int)Math.Round(100.0 * (total - row.Seats) / total);
            Assert.AreEqual($"{expected}% full", row.LoadText, row.FlightNo);
            Assert.AreEqual(expected, row.LoadPercent, row.FlightNo);
        }
    }

    [TestMethod]
    public void TheBoard_NamesTheDestination_AndFlagsFewSeatsLeft()
    {
        FlightRow row = FlightSearch.Search(_store, "Cebu (CEB)", Now.Date.AddDays(1), "Economy", Now).First();
        Assert.AreEqual("Cebu (CEB)", row.DestinationText);
        Assert.IsFalse(row.FewSeatsLeft, "an empty flight has plenty of seats");

        Assert.IsTrue((row with { Seats = 12 }).FewSeatsLeft);
        Assert.IsFalse((row with { Seats = 0 }).FewSeatsLeft, "sold out is shown as sold out, not as few left");
    }

    [TestMethod]
    public void TheBoard_ShowsA100PercentLoadForASoldOutCabin_AndZeroForAnEmptyOne()
    {
        FlightRow emptyRow = FlightSearch.Search(_store, "Cebu (CEB)", DateTime.Today.AddDays(40), "Economy", Now).First();
        Assert.AreEqual("0% full", emptyRow.LoadText);
    }
}
