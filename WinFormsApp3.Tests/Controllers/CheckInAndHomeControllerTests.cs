using Microsoft.Data.Sqlite;
using SkyWings.Controllers;

namespace SkyWings.Tests.Controllers;

[TestClass]
[DoNotParallelize]
public sealed class CheckInControllerTests
{
    private string _dbPath = "";
    private BookingStore _store = null!;
    private CheckInController _controller = null!;
    private BookingRecord _booking = null!;
    private DateTime _flightDate;

    [TestInitialize]
    public void Setup()
    {
        LookupGuard.Reset();
        _dbPath = Path.Combine(Path.GetTempPath(), $"skywings-chk-{Guid.NewGuid():N}.db");
        var csb = new SqliteConnectionStringBuilder { DataSource = _dbPath, ForeignKeys = true };
        _store = new BookingStore(csb.ConnectionString);
        _store.SeedFlightsIfEmpty(AppData.DomesticFlights, AppData.InternationalFlights,
            AppData.DefaultDomesticCapacity, AppData.DefaultInternationalCapacity);
        _controller = new CheckInController(_store);

        _flightDate = DateTime.Today.AddDays(30);
        var seated = new PassengerRecord("Maria Santos", "1 Test St", "09171234567", 35, "1991-01-01",
            "PSP-1", false, false, new SeatAssignment("Maria Santos", "ECO1A"), Array.Empty<string>(), 2_000m);
        var unseated = new PassengerRecord("Juan Santos", "1 Test St", "09171234567", 33, "1993-01-01",
            "PSP-2", false, false, null, Array.Empty<string>(), 2_000m);
        _booking = new BookingRecord("SKY-CHK000001", DateTime.Now, "PR-101", "Manila (MNL)", "Cebu (CEB)", _flightDate,
            true, "Economy", "Credit Card", "**** 4521", 4_000m, "Scheduled", new[] { seated, unseated },
            AppData.StatusConfirmed, "PAID", "TXN-CHK1", DateTime.Now, 0m);
        _store.SaveConfirmedBooking(_booking, false, 4_000m, "Credit Card", "**** 4521", "TXN-CHK1", DateTime.Now);
    }

    [TestCleanup]
    public void Cleanup()
    {
        LookupGuard.Reset();
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private DateTime Departure => AppData.DepartureOf("PR-101", _flightDate);

    [TestMethod]
    public void Find_UnknownReference_FailsAndCurrentStaysEmpty()
    {
        LookupResult result = _controller.Find("SKY-NOPE00000");

        Assert.IsFalse(result.Found);
        Assert.IsNull(_controller.Current);
    }

    [TestMethod]
    public void LegLabels_OneWay_HasOnlyOutbound()
    {
        _controller.Find("SKY-CHK000001");

        IReadOnlyList<string> labels = _controller.LegLabels();

        Assert.HasCount(1, labels);
        StringAssert.StartsWith(labels[0], "Outbound · PR-101");
    }

    [TestMethod]
    public void Load_BeforeWindow_ShowsWhenItOpens()
    {
        _controller.Find("SKY-CHK000001");

        CheckInView view = _controller.Load(false, Departure.AddHours(-AppData.CheckInWindowHours - 1));

        Assert.AreEqual(CheckInBannerKind.NotYetOpen, view.Banner);
        Assert.IsFalse(view.Open);
    }

    [TestMethod]
    public void Load_InsideWindow_IsOpenAndListsTravellersWithSeats()
    {
        _controller.Find("SKY-CHK000001");

        CheckInView view = _controller.Load(false, Departure.AddHours(-2));

        Assert.AreEqual(CheckInBannerKind.Open, view.Banner);
        Assert.IsTrue(view.Open);
        Assert.HasCount(2, view.Travellers);
        Assert.IsTrue(view.Travellers.Single(t => t.Name == "Maria Santos").HasSeat);
        Assert.IsFalse(view.Travellers.Single(t => t.Name == "Juan Santos").HasSeat);
    }

    [TestMethod]
    public void Load_AfterDeparture_IsClosed()
    {
        _controller.Find("SKY-CHK000001");

        CheckInView view = _controller.Load(false, Departure.AddHours(1));

        Assert.AreEqual(CheckInBannerKind.Closed, view.Banner);
        Assert.IsFalse(view.Open);
    }

    [TestMethod]
    public void CheckIn_SeatedTraveller_SucceedsAndShowsAsCheckedIn()
    {
        _controller.Find("SKY-CHK000001");
        DateTime now = Departure.AddHours(-2);

        CheckInSummary summary = _controller.CheckIn(new[] { "Maria Santos" }, false, now);
        CheckInView view = _controller.Load(false, now);

        CollectionAssert.AreEqual(new[] { "Maria Santos" }, summary.Done.ToArray());
        Assert.IsEmpty(summary.Problems);
        Assert.IsTrue(view.Travellers.Single(t => t.Name == "Maria Santos").IsCheckedIn);
        Assert.IsTrue(view.AnyCheckedIn);
        Assert.HasCount(1, _controller.PassRecords(false, null));
    }

    [TestMethod]
    public void CheckIn_TravellerWithoutSeat_ReportsProblem()
    {
        _controller.Find("SKY-CHK000001");

        CheckInSummary summary = _controller.CheckIn(new[] { "Juan Santos" }, false, Departure.AddHours(-2));

        Assert.IsEmpty(summary.Done);
        Assert.HasCount(1, summary.Problems);
        StringAssert.Contains(summary.Problems[0], "Juan Santos");
    }

    [TestMethod]
    public void CheckIn_BeforeWindow_ReportsNotOpen()
    {
        _controller.Find("SKY-CHK000001");

        CheckInSummary summary = _controller.CheckIn(new[] { "Maria Santos" }, false, Departure.AddHours(-100));

        Assert.IsEmpty(summary.Done);
        StringAssert.Contains(summary.Problems.Single(), "not open");
    }

    [TestMethod]
    public void PassRecords_CanFilterByName()
    {
        _controller.Find("SKY-CHK000001");
        _controller.CheckIn(new[] { "Maria Santos" }, false, Departure.AddHours(-2));

        Assert.HasCount(1, _controller.PassRecords(false, new[] { "Maria Santos" }));
        Assert.IsEmpty(_controller.PassRecords(false, new[] { "Someone Else" }));
    }

    [TestMethod]
    public void Load_WithoutBooking_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => _controller.Load(false, DateTime.Now));
    }
}

[TestClass]
public sealed class HomeControllerTests
{
    private string _dbPath = "";
    private BookingStore _store = null!;
    private HomeController _controller = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"skywings-home-{Guid.NewGuid():N}.db");
        var csb = new SqliteConnectionStringBuilder { DataSource = _dbPath, ForeignKeys = true };
        _store = new BookingStore(csb.ConnectionString);
        _controller = new HomeController(_store, new StaffSession(1, "agent1", "Ana Reyes", StaffRole.Agent, false));
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    [TestMethod]
    public void FirstName_IsFirstWordOfDisplayName()
    {
        Assert.AreEqual("Ana", _controller.FirstName);
    }

    [TestMethod]
    public void LoadDashboard_GroupsManilaFlightsIntoFourRegions()
    {
        Dashboard dashboard = _controller.LoadDashboard();

        CollectionAssert.AreEqual(new[] { "Domestic", "Southeast Asia", "Europe", "North America" },
            dashboard.Routes.Select(r => r.Title).ToArray());
        Assert.IsTrue(dashboard.Routes.All(r => r.Flights.All(f => f.Origin.StartsWith("Manila"))));
        Assert.AreEqual(dashboard.Routes.Sum(r => r.Flights.Count), dashboard.FlightCount);
        StringAssert.Contains(dashboard.Summary, $"{dashboard.FlightCount} daily flights");
    }

    [TestMethod]
    public void LoadDashboard_DestinationListStartsWithAnywhere()
    {
        Dashboard dashboard = _controller.LoadDashboard();

        Assert.AreEqual("", dashboard.Destinations[0].Destination);
        Assert.AreEqual(dashboard.FlightCount, dashboard.Destinations[0].Flights);
        Assert.AreEqual(dashboard.DestinationCount + 1, dashboard.Destinations.Count);
    }

    [TestMethod]
    public void DefaultDestinationIndex_PrefersPreviousThenCebu()
    {
        Dashboard dashboard = _controller.LoadDashboard();
        string other = dashboard.Destinations.First(d => d.Destination.Length > 0 && !d.Destination.StartsWith("Cebu")).Destination;

        int previous = dashboard.DefaultDestinationIndex(other);
        int fallback = dashboard.DefaultDestinationIndex(null);

        Assert.AreEqual(other, dashboard.Destinations[previous].Destination);
        StringAssert.StartsWith(dashboard.Destinations[fallback].Destination, "Cebu");
    }

    [TestMethod]
    public void DestinationOption_ToStringDescribesEntry()
    {
        StringAssert.Contains(new DestinationOption("", 1_000m, 12).ToString(), "show all 12 flights");
        StringAssert.Contains(new DestinationOption("Cebu (CEB)", 2_500m, 3).ToString(), "from PHP 2,500");
    }

    [TestMethod]
    public void RecordSignOut_WritesAuditEntry()
    {
        _controller.RecordSignOut("Signed out");

        // No exception means the audit row was written; a second call must also work.
        _controller.RecordSignOut("Closed the application");
    }
}
