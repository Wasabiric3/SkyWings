using Microsoft.Data.Sqlite;

namespace SkyWings.Tests;

/// <summary>Staff (admin only) switch the demo activity on and off, include it in reports, and reset it.</summary>
[TestClass]
public sealed class DemoAdminTests
{
    private const string GoodPassword = "a long enough password 1!";
    private string _dbPath = "";
    private BookingStore _store = null!;
    private AdminService _admin = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"skywings-demo-{Guid.NewGuid():N}.db");
        _store = new BookingStore(new SqliteConnectionStringBuilder { DataSource = _dbPath, ForeignKeys = true }.ConnectionString);
        _store.SeedDefaultFlights();
        _admin = ServiceFor(StaffRole.Admin, "boss");
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private AdminService ServiceFor(StaffRole role, string username)
    {
        StaffAccount staff = _store.CreateStaff(username, username, GoodPassword, role, false);
        return new AdminService(_store, new StaffSession(staff.Id, staff.Username, staff.DisplayName, staff.Role, false));
    }

    [TestMethod]
    public void TheDemoIsOnByDefault_ButKeptOutOfReports()
    {
        DemoStatus status = _admin.LoadDemoStatus();
        Assert.IsTrue(status.Enabled);
        Assert.IsFalse(status.IncludeInReports);
        Assert.AreEqual(0, status.DemoBookings);
    }

    [TestMethod]
    public void SwitchingTheDemoOnAndOff_IsSavedAndAudited()
    {
        _admin.SetDemoActivity(true);
        Assert.IsTrue(_admin.LoadDemoStatus().Enabled);
        Assert.IsTrue(_store.LoadAudit().Any(e => e.Action == "DEMO_ACTIVITY_ON"));

        _admin.SetDemoActivity(false);
        Assert.IsFalse(_admin.LoadDemoStatus().Enabled);
        Assert.IsTrue(_store.LoadAudit().Any(e => e.Action == "DEMO_ACTIVITY_OFF"));
    }

    [TestMethod]
    public void IncludeInReports_IsSavedAndAudited()
    {
        _admin.SetIncludeDemoInReports(true);
        Assert.IsTrue(_admin.LoadDemoStatus().IncludeInReports);
        Assert.IsTrue(_store.IncludeSimulatedInReports);
        Assert.IsTrue(_store.LoadAudit().Any(e => e.Action == "DEMO_REPORTS_ON"));
    }

    [TestMethod]
    public void RunDemoNow_DoesNothingWhileOff_AndFillsTheFlightsWhenOn()
    {
        _admin.SetDemoActivity(false);
        Assert.IsNull(_admin.RunDemoNow());
        Assert.AreEqual(0, _admin.LoadDemoStatus().DemoBookings);

        _admin.SetDemoActivity(true);
        SimulationTick? tick = _admin.RunDemoNow();

        Assert.IsNotNull(tick);
        Assert.IsGreaterThan(0, _admin.LoadDemoStatus().DemoBookings);
    }

    [TestMethod]
    public void ResetDemoData_RemovesAndRegeneratesOnlyDemoBookings()
    {
        var p = new PassengerRecord("Real Person", "1 St", "0917", 35, "1991-01-01", "PSP-1", false, false,
            new SeatAssignment("Real Person", "ECO1A"), Array.Empty<string>(), 4000m);
        var real = new BookingRecord("SKY-REAL0000DEMO", DateTime.Now, "PR-101", "Manila (MNL)", "Cebu (CEB)", DateTime.Today.AddDays(2), true,
            "Economy", "Credit Card", "**** 4521", 4000m, "Scheduled", new[] { p }, AppData.StatusConfirmed, "PAID", "TXN-1", DateTime.Now, 0m);
        _store.SaveConfirmedBooking(real, false, 4000m, "Credit Card", "**** 4521", "TXN-1", DateTime.Now);
        _admin.SetDemoActivity(true);
        _admin.RunDemoNow();
        int before = _admin.LoadDemoStatus().DemoBookings;

        int regenerated = _admin.ResetDemoData();

        Assert.IsGreaterThan(0, regenerated);
        Assert.IsNotNull(_store.FindByReference("SKY-REAL0000DEMO"));
        Assert.IsGreaterThan(0, _admin.LoadDemoStatus().DemoBookings);
        Assert.IsTrue(_store.LoadAudit().Any(e => e.Action == "DEMO_RESET"));
        Assert.IsTrue(before > 0);
    }


    [TestMethod]
    public void ResetDemoData_WhileTheDemoIsOff_OnlyClears()
    {
        _admin.SetDemoActivity(true);
        _admin.RunDemoNow();
        Assert.IsGreaterThan(0, _admin.LoadDemoStatus().DemoBookings);
        _admin.SetDemoActivity(false);

        int regenerated = _admin.ResetDemoData();

        Assert.AreEqual(0, regenerated);
        Assert.AreEqual(0, _admin.LoadDemoStatus().DemoBookings, "with the demo off, reset leaves the system empty of demo data");
    }
    [TestMethod]
    public void AgentsCannotTouchTheDemoControls()
    {
        AdminService agent = ServiceFor(StaffRole.Agent, "agent7");
        Assert.ThrowsExactly<UnauthorizedAccessException>(() => agent.LoadDemoStatus());
        Assert.ThrowsExactly<UnauthorizedAccessException>(() => agent.SetDemoActivity(false));
        Assert.ThrowsExactly<UnauthorizedAccessException>(() => agent.SetIncludeDemoInReports(true));
        Assert.ThrowsExactly<UnauthorizedAccessException>(() => agent.ResetDemoData());
        Assert.ThrowsExactly<UnauthorizedAccessException>(() => agent.RunDemoNow());
        Assert.IsTrue(_store.DemoActivityEnabled, "the agent's attempt to switch it off changed nothing");
        Assert.IsFalse(_store.IncludeSimulatedInReports);
    }
}
