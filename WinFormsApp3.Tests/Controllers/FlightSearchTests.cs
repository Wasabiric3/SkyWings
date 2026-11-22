using Microsoft.Data.Sqlite;
using SkyWings.Controllers;

namespace SkyWings.Tests.Controllers;

[TestClass]
public sealed class FlightSearchTests
{
    private string _dbPath = "";
    private BookingStore _store = null!;
    private readonly DateTime _date = DateTime.Today.AddDays(30);

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"skywings-fs-{Guid.NewGuid():N}.db");
        var csb = new SqliteConnectionStringBuilder { DataSource = _dbPath, ForeignKeys = true, Pooling = false };
        _store = new BookingStore(csb.ConnectionString);
        _store.SeedFlightsIfEmpty(AppData.DomesticFlights, AppData.InternationalFlights,
            AppData.DefaultDomesticCapacity, AppData.DefaultInternationalCapacity);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    [TestMethod]
    public void Search_Anywhere_ListsEveryManilaFlightSoonestDepartureFirst()
    {
        List<FlightRow> rows = FlightSearch.Search(_store, "", _date, AppData.ClassNames[0], _date.AddHours(5));

        Assert.IsNotEmpty(rows);
        Assert.IsTrue(rows.All(r => r.Flight.Origin.StartsWith("Manila")));
        CollectionAssert.AreEqual(rows.Select(r => r.Status.Estimated).OrderBy(t => t).ToList(),
            rows.Select(r => r.Status.Estimated).ToList());
    }

    [TestMethod]
    public void Search_RowsCarryTheLiveStatusForTheGivenMoment()
    {
        DateTime now = new FlightRowClock(_date).At(7, 30);

        List<FlightRow> rows = FlightSearch.Search(_store, "", _date, AppData.ClassNames[0], now);

        Assert.IsTrue(rows.All(r => r.Status.Scheduled == AppData.DepartureOf(r.FlightNo, _date)));
        Assert.IsTrue(rows.All(r => r.StatusText.Length > 0 && r.CountdownText.Length > 0));
        Assert.IsTrue(rows.Any(r => r.Status.Kind != FlightStatusKind.Scheduled));
    }

    [TestMethod]
    public void Restamp_MovesStatusesForwardWithoutTouchingTheDatabase()
    {
        List<FlightRow> early = FlightSearch.Search(_store, "", _date, AppData.ClassNames[0], _date.AddDays(-3));
        List<FlightRow> late = HomeController.Restamp(early, _date, _date.AddDays(3));

        Assert.IsTrue(early.All(r => r.Status.Kind == FlightStatusKind.Scheduled));
        Assert.IsTrue(late.All(r => r.Status.Kind == FlightStatusKind.Landed));
        Assert.HasCount(early.Count, late);
    }

    private readonly record struct FlightRowClock(DateTime Day)
    {
        public DateTime At(int hour, int minute) => Day.Date.AddHours(hour).AddMinutes(minute);
    }

    [TestMethod]
    public void Search_OneDestination_OnlyThatDestinationEarliestFirst()
    {
        List<FlightRow> rows = FlightSearch.Search(_store, "Cebu (CEB)", _date, AppData.ClassNames[0]);

        Assert.IsNotEmpty(rows);
        Assert.IsTrue(rows.All(r => r.Flight.Destination == "Cebu (CEB)"));
        CollectionAssert.AreEqual(
            rows.Select(r => AppData.DepartureOf(r.FlightNo, _date)).OrderBy(d => d).ToList(),
            rows.Select(r => AppData.DepartureOf(r.FlightNo, _date)).ToList());
    }

    [TestMethod]
    public void Search_HigherCabin_CostsMoreForTheSameFlight()
    {
        FlightRow economy = FlightSearch.Search(_store, "Cebu (CEB)", _date, AppData.ClassNames[0]).First();
        FlightRow top = FlightSearch.Search(_store, "Cebu (CEB)", _date, AppData.ClassNames[^1])
            .First(r => r.FlightNo == economy.FlightNo);

        decimal Parse(string price) => decimal.Parse(price.Replace("PHP", "").Replace(",", "").Trim());
        Assert.IsGreaterThan(Parse(economy.Price), Parse(top.Price));
    }

    [TestMethod]
    public void Search_RowsShowTimesDurationAndSeats()
    {
        FlightRow row = FlightSearch.Search(_store, "Cebu (CEB)", _date, AppData.ClassNames[0]).First();

        Assert.IsFalse(string.IsNullOrEmpty(row.Departs));
        Assert.IsFalse(string.IsNullOrEmpty(row.Arrives));
        StringAssert.Contains(row.Duration, "h");
        Assert.IsGreaterThan(0, row.Seats);
        Assert.IsFalse(row.SoldOut);
    }

    [TestMethod]
    public void Search_UnknownDestination_IsEmpty()
    {
        Assert.IsEmpty(FlightSearch.Search(_store, "Atlantis", _date, AppData.ClassNames[0]));
    }
}
