using SkyWings;
using Microsoft.Data.Sqlite;

namespace SkyWings.Tests;

[TestClass]
public sealed class PasswordPolicyTests
{
    [TestMethod]
    [DataRow("short1", null)]
    [DataRow("            ", null)]
    [DataRow("aaaaaaaaaaaaaaaa", null)]
    [DataRow("password1234", null)]
    [DataRow("my-maria-secret-2026", "maria")]
    public void Check_RejectsWeakPasswords(string password, string? username)
    {
        Assert.IsNotNull(PasswordPolicy.Check(password, username));
    }

    [TestMethod]
    public void Check_AcceptsLongVariedPasswords()
    {
        Assert.IsNull(PasswordPolicy.Check("correct horse battery staple", "maria"));
        Assert.IsNull(PasswordPolicy.Check("Tr0ub4dor&3-xyz", null));
    }
}

/// <summary>Staff accounts, sign-in lockout, roles and the audit log, against a throwaway SQLite file.</summary>
[TestClass]
public sealed class StaffSecurityTests
{
    private const string GoodPassword = "correct horse battery";
    private string _dbPath = "";
    private string _connection = "";
    private BookingStore _store = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"skywings-staff-{Guid.NewGuid():N}.db");
        _connection = new SqliteConnectionStringBuilder { DataSource = _dbPath, ForeignKeys = true }.ConnectionString;
        _store = new BookingStore(_connection);
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private StaffAccount AddAdmin(string name = "boss") => _store.CreateStaff(name, "The Boss", GoodPassword, StaffRole.Admin, false);

    [TestMethod]
    public void CreateStaff_RejectsBadUsernameWeakPasswordAndDuplicates()
    {
        Assert.ThrowsExactly<ArgumentException>(() => _store.CreateStaff("a b", "X", GoodPassword, StaffRole.Agent, false));
        Assert.ThrowsExactly<ArgumentException>(() => _store.CreateStaff("agent1", "X", "short", StaffRole.Agent, false));
        _store.CreateStaff("agent1", "Agent One", GoodPassword, StaffRole.Agent, false);
        Assert.ThrowsExactly<InvalidOperationException>(() => _store.CreateStaff("AGENT1", "Again", GoodPassword, StaffRole.Agent, false));
    }

    [TestMethod]
    public void Authenticate_SucceedsWithRightPasswordAndSetsRole()
    {
        AddAdmin();

        AuthResult result = _store.Authenticate("BOSS", GoodPassword);

        Assert.IsTrue(result.Success);
        Assert.AreEqual(StaffRole.Admin, result.Session!.Role);
        Assert.IsTrue(result.Session.Has(Permission.BackupRestore));
    }

    [TestMethod]
    public void Authenticate_GivesTheSameMessageForWrongNameAndWrongPassword()
    {
        AddAdmin();

        string wrongName = _store.Authenticate("nobody", "whatever whatever").Message;
        string wrongPassword = _store.Authenticate("boss", "wrong password here").Message;

        Assert.AreEqual(wrongName, wrongPassword);
        Assert.AreEqual("Invalid username or password.", wrongName);
    }

    [TestMethod]
    public void Lockout_SurvivesANewStoreInstance()
    {
        AddAdmin();
        for (int i = 0; i < BookingStore.MaxFailedSignIns; i++) _store.Authenticate("boss", "wrong password " + i);

        // A new store on the same file stands for "closed the dialog / restarted the app".
        var reopened = new BookingStore(_connection);
        AuthResult afterLock = reopened.Authenticate("boss", GoodPassword);

        Assert.IsFalse(afterLock.Success, "Even the right password must be refused while locked.");
        StringAssert.Contains(afterLock.Message, "Too many failed attempts");
    }

    [TestMethod]
    public void Lockout_AlsoAppliesToNamesThatDoNotExist_WithoutRevealingThem()
    {
        string ghost = "ghost" + Guid.NewGuid().ToString("N")[..6];
        AuthResult last = new(null, "");
        for (int i = 0; i < BookingStore.MaxFailedSignIns; i++) last = _store.Authenticate(ghost, "wrong password " + i);

        StringAssert.Contains(last.Message, "Too many failed attempts");
    }

    [TestMethod]
    public void DeactivatedAccounts_CannotSignIn()
    {
        AddAdmin("root");
        StaffAccount agent = _store.CreateStaff("agent2", "Agent Two", GoodPassword, StaffRole.Agent, false);
        _store.SetStaffActive(agent.Id, false);

        Assert.IsFalse(_store.Authenticate("agent2", GoodPassword).Success);
    }

    [TestMethod]
    public void LastActiveAdministrator_CannotBeDemotedOrDeactivated()
    {
        StaffAccount only = AddAdmin();

        Assert.ThrowsExactly<InvalidOperationException>(() => _store.SetStaffRole(only.Id, StaffRole.Agent));
        Assert.ThrowsExactly<InvalidOperationException>(() => _store.SetStaffActive(only.Id, false));

        StaffAccount second = _store.CreateStaff("second", "Second Admin", GoodPassword, StaffRole.Admin, false);
        _store.SetStaffRole(second.Id, StaffRole.Agent);   // fine: another admin remains
        Assert.AreEqual(StaffRole.Agent, _store.FindStaff("second")!.Role);
    }

    [TestMethod]
    public void ResetPassword_ForcesChangeAndClearsLock()
    {
        StaffAccount agent = _store.CreateStaff("agent3", "Agent Three", GoodPassword, StaffRole.Agent, false);
        for (int i = 0; i < BookingStore.MaxFailedSignIns; i++) _store.Authenticate("agent3", "wrong password " + i);

        _store.ResetPassword(agent.Id, "a brand new temporary pass");
        AuthResult result = _store.Authenticate("agent3", "a brand new temporary pass");

        Assert.IsTrue(result.Success);
        Assert.IsTrue(result.Session!.MustChangePassword);
    }

    [TestMethod]
    public void ImportLegacyAdmin_RunsOnlyOnceAndRequiresPasswordChange()
    {
        Assert.IsTrue(_store.ImportLegacyAdmin("admin", PasswordHasher.Hash("old shared password"), ""));
        Assert.IsFalse(_store.ImportLegacyAdmin("admin2", "", "another"), "Must not import when accounts already exist.");

        AuthResult result = _store.Authenticate("admin", "old shared password");
        Assert.IsTrue(result.Success);
        Assert.IsTrue(result.Session!.MustChangePassword);
    }

    [TestMethod]
    public void Roles_AgentCannotManageFlightsStaffBackupsOrAudit()
    {
        var agent = new StaffSession(1, "agent", "Agent", StaffRole.Agent, false);

        Assert.IsTrue(agent.Has(Permission.ViewBookings | Permission.CancelBookings | Permission.ViewReports));
        foreach (Permission denied in new[] { Permission.ManageFlights, Permission.BackupRestore, Permission.ManageStaff, Permission.ViewAudit })
            Assert.ThrowsExactly<UnauthorizedAccessException>(() => agent.Require(denied));
    }

    [TestMethod]
    public void AdminService_RefusesAgentsBeforeTouchingData()
    {
        StaffAccount agent = _store.CreateStaff("agent4", "Agent Four", GoodPassword, StaffRole.Agent, false);
        var service = new AdminService(_store, new StaffSession(agent.Id, agent.Username, agent.DisplayName, agent.Role, false));

        Assert.ThrowsExactly<UnauthorizedAccessException>(() => service.SetFlightActive("PR-101", false));
        Assert.ThrowsExactly<UnauthorizedAccessException>(() => service.CreateStaff("sneaky", "S", GoodPassword, StaffRole.Admin));
        Assert.ThrowsExactly<UnauthorizedAccessException>(() => service.LoadAudit());
        Assert.IsEmpty(_store.LoadAudit().Where(a => a.Action.StartsWith("FLIGHT") || a.Action == "STAFF_CREATED"));
    }

    [TestMethod]
    public void AdminService_RecordsWhoDidWhat()
    {
        StaffAccount admin = AddAdmin("auditor");
        var service = new AdminService(_store, new StaffSession(admin.Id, admin.Username, admin.DisplayName, admin.Role, false));
        _store.SeedFlightsIfEmpty(AppData.DomesticFlights, AppData.InternationalFlights, 180, 220);

        service.SetFlightActive("PR-101", false);
        service.CreateStaff("newagent", "New Agent", GoodPassword, StaffRole.Agent);

        var log = service.LoadAudit();
        AuditEntry flight = log.Single(a => a.Action == "FLIGHT_DEACTIVATED");
        Assert.AreEqual("auditor", flight.Actor);
        Assert.AreEqual("PR-101", flight.Target);
        Assert.IsTrue(log.Any(a => a.Action == "STAFF_CREATED" && a.Target == "newagent"));
    }

    [TestMethod]
    public void AdminService_ChangeOwnPassword_NeedsTheCurrentPasswordAndClearsTheForcedChange()
    {
        StaffAccount temp = _store.CreateStaff("temp1", "Temp One", GoodPassword, StaffRole.Agent, mustChangePassword: true);
        var session = new StaffSession(temp.Id, temp.Username, temp.DisplayName, temp.Role, true);
        var service = new AdminService(_store, session);

        Assert.ThrowsExactly<UnauthorizedAccessException>(() => service.ChangeOwnPassword("not my password", "another long password"));
        Assert.ThrowsExactly<ArgumentException>(() => service.ChangeOwnPassword(GoodPassword, GoodPassword));
        service.ChangeOwnPassword(GoodPassword, "another long password");

        Assert.IsFalse(session.MustChangePassword);
        Assert.IsTrue(_store.Authenticate("temp1", "another long password").Success);
        Assert.IsFalse(_store.Authenticate("temp1", GoodPassword).Success);
    }

    [TestMethod]
    public void Audit_StripsControlCharactersAndLimitsLength()
    {
        _store.WriteAudit("evil\r\nactor", "", "TEST", new string('x', 500), "line1\nline2");

        AuditEntry entry = _store.LoadAudit().First(a => a.Action == "TEST");
        Assert.AreEqual("evilactor", entry.Actor);
        Assert.AreEqual(120, entry.Target.Length);
        Assert.AreEqual("line1line2", entry.Detail);
    }
}
