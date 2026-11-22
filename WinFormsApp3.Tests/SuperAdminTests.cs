using Microsoft.Data.Sqlite;

namespace SkyWings.Tests;

/// <summary>The Super Admin role: permissions, the sign-in type gate, who may manage administrators, and the schema upgrade.</summary>
[TestClass]
[DoNotParallelize]
public sealed class SuperAdminTests
{
    private const string Pass = "correct horse battery";
    private string _dbPath = "";
    private string _connection = "";
    private BookingStore _store = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"skywings-super-{Guid.NewGuid():N}.db");
        _connection = new SqliteConnectionStringBuilder { DataSource = _dbPath, ForeignKeys = true, Pooling = false }.ConnectionString;
        _store = new BookingStore(_connection);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private StaffAccount Add(string name, StaffRole role) => _store.CreateStaff(name, name, Pass, role, false);

    private AdminService Service(StaffAccount a) => new(_store, new StaffSession(a.Id, a.Username, a.DisplayName, a.Role, false));

    // ---------------------------------------------------------------- permissions and roles

    [TestMethod]
    public void Permissions_SuperAdminHasEverything_AdminLacksAdminManagementAndDatabase_AgentIsUnchanged()
    {
        var all = RolePermissions.For(StaffRole.SuperAdmin);
        Assert.AreEqual(Permission.All, all);

        var admin = new StaffSession(1, "a", "A", StaffRole.Admin, false);
        Assert.IsTrue(admin.Has(Permission.ManageStaff | Permission.ManageFlights | Permission.BackupRestore | Permission.ViewAudit));
        Assert.IsFalse(admin.Has(Permission.BrowseData));
        Assert.IsFalse(admin.Has(Permission.ManageAdmins));

        var agent = new StaffSession(2, "g", "G", StaffRole.Agent, false);
        Assert.AreEqual(Permission.ViewBookings | Permission.CancelBookings | Permission.ViewReports, agent.Permissions);
    }

    [TestMethod]
    public void RoleStrings_RoundTrip_AndUnknownFallsBackToAgent()
    {
        foreach (var role in Enum.GetValues<StaffRole>())
            Assert.AreEqual(role, RolePermissions.FromDb(RolePermissions.ToDb(role)));
        Assert.AreEqual(StaffRole.Agent, RolePermissions.FromDb("ROOT"));
        Assert.AreEqual("Super Admin", RolePermissions.Label(StaffRole.SuperAdmin));
    }

    // ---------------------------------------------------------------- schema upgrade

    [TestMethod]
    public void OldStaffTable_IsUpgraded_KeepingRowsAndHashes()
    {
        string oldPath = Path.Combine(Path.GetTempPath(), $"skywings-oldstaff-{Guid.NewGuid():N}.db");
        string conn = new SqliteConnectionStringBuilder { DataSource = oldPath, ForeignKeys = true, Pooling = false }.ConnectionString;
        try
        {
            using (var con = new SqliteConnection(conn))
            {
                con.Open();
                Run(con, "CREATE TABLE staff_users (id INTEGER PRIMARY KEY AUTOINCREMENT, username VARCHAR(40) NOT NULL UNIQUE COLLATE NOCASE, " +
                    "display_name VARCHAR(80) NOT NULL, password_hash VARCHAR(200) NOT NULL, role VARCHAR(10) NOT NULL CHECK (role IN ('ADMIN','AGENT')), " +
                    "active INTEGER NOT NULL DEFAULT 1, must_change_password INTEGER NOT NULL DEFAULT 0, failed_attempts INTEGER NOT NULL DEFAULT 0, " +
                    "locked_until DATETIME NULL, created_at DATETIME NOT NULL, last_login_at DATETIME NULL)");
                Run(con, "INSERT INTO staff_users (id, username, display_name, password_hash, role, created_at) VALUES (7, 'old.admin', 'Old Admin', 'HASH-KEPT', 'ADMIN', '2026-01-01')");
            }

            var store = new BookingStore(conn);   // opening runs the upgrade
            StaffAccount kept = store.FindStaff("old.admin")!;
            Assert.AreEqual(7, kept.Id);
            Assert.AreEqual(StaffRole.Admin, kept.Role);

            store.PromoteToSuperAdmin("old.admin");   // would violate the old CHECK
            Assert.AreEqual(StaffRole.SuperAdmin, store.FindStaff("old.admin")!.Role);

            _ = new BookingStore(conn);   // a second open changes nothing
            using var read = new SqliteConnection(conn);
            read.Open();
            using var cmd = read.CreateCommand();
            cmd.CommandText = "SELECT password_hash FROM staff_users WHERE id = 7";
            Assert.AreEqual("HASH-KEPT", cmd.ExecuteScalar());
        }
        finally
        {
            if (File.Exists(oldPath)) File.Delete(oldPath);
        }
    }

    private static void Run(SqliteConnection con, string sql)
    {
        using var cmd = con.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    // ---------------------------------------------------------------- sign-in recognises the role

    [TestMethod]
    public void SigningIn_ReturnsTheAccountsOwnRole_WithoutAnyTypeChoice()
    {
        Add("boss", StaffRole.SuperAdmin);
        Add("adm", StaffRole.Admin);
        Add("agent1", StaffRole.Agent);

        Assert.AreEqual(StaffRole.SuperAdmin, _store.Authenticate("boss", Pass).Session!.Role);
        Assert.AreEqual(StaffRole.Admin, _store.Authenticate("adm", Pass).Session!.Role);
        Assert.AreEqual(StaffRole.Agent, _store.Authenticate("agent1", Pass).Session!.Role);
        Assert.IsTrue(Service(_store.FindStaff("boss")!).Reauthenticate(Pass));
    }

    // ---------------------------------------------------------------- who may manage whom

    [TestMethod]
    public void Admin_ManagesAgents_ButNotAdministrators()
    {
        var admin = Service(Add("adm", StaffRole.Admin));
        StaffAccount otherAdmin = Add("adm2", StaffRole.Admin);
        StaffAccount boss = Add("boss", StaffRole.SuperAdmin);

        StaffAccount agent = admin.CreateStaff("newagent", "New Agent", Pass, StaffRole.Agent);
        admin.SetActive(agent.Id, false);
        admin.ResetPassword(agent.Id, Pass + "2");

        Assert.ThrowsExactly<UnauthorizedAccessException>(() => admin.CreateStaff("sneaky", "S", Pass, StaffRole.Admin));
        Assert.ThrowsExactly<UnauthorizedAccessException>(() => admin.SetRole(agent.Id, StaffRole.Admin));
        Assert.ThrowsExactly<UnauthorizedAccessException>(() => admin.SetActive(otherAdmin.Id, false));
        Assert.ThrowsExactly<UnauthorizedAccessException>(() => admin.ResetPassword(otherAdmin.Id, Pass + "3"));
        Assert.ThrowsExactly<UnauthorizedAccessException>(() => admin.SetRole(otherAdmin.Id, StaffRole.Agent));
        Assert.ThrowsExactly<UnauthorizedAccessException>(() => admin.SetActive(boss.Id, false));
    }

    [TestMethod]
    public void SuperAdmin_ManagesAdministrators_ButNobodyCanGrantOrRemoveSuperAdminFromTheScreens()
    {
        StaffAccount bossAccount = Add("boss", StaffRole.SuperAdmin);
        var boss = Service(bossAccount);

        StaffAccount made = boss.CreateStaff("newadmin", "New Admin", Pass, StaffRole.Admin);
        Assert.AreEqual(StaffRole.Admin, made.Role);
        boss.SetRole(made.Id, StaffRole.Agent);
        boss.SetRole(made.Id, StaffRole.Admin);

        Assert.ThrowsExactly<UnauthorizedAccessException>(() => boss.CreateStaff("another", "A", Pass, StaffRole.SuperAdmin));
        Assert.ThrowsExactly<UnauthorizedAccessException>(() => boss.SetRole(made.Id, StaffRole.SuperAdmin));
        Assert.ThrowsExactly<UnauthorizedAccessException>(() => boss.SetRole(bossAccount.Id, StaffRole.Agent));
    }

    [TestMethod]
    public void LastAdministratorRule_CountsSuperAdmin()
    {
        StaffAccount boss = Add("boss", StaffRole.SuperAdmin);
        StaffAccount admin = Add("adm", StaffRole.Admin);

        _store.SetStaffActive(admin.Id, false);   // fine: the Super Admin remains
        _store.SetStaffActive(admin.Id, true);
        _store.SetStaffRole(admin.Id, StaffRole.Agent);   // fine for the same reason
        Assert.ThrowsExactly<InvalidOperationException>(() => _store.SetStaffActive(boss.Id, false));
    }

    // ---------------------------------------------------------------- the promote switch

    [TestMethod]
    public void PromoteToSuperAdmin_KeepsThePassword_AndIsAudited()
    {
        Add("patrick", StaffRole.Admin);

        var promoted = _store.PromoteToSuperAdmin("PATRICK");

        Assert.AreEqual(StaffRole.SuperAdmin, promoted.Role);
        Assert.IsTrue(_store.Authenticate("patrick", Pass).Success, "same password as before");
        Assert.IsFalse(_store.FindStaff("patrick")!.MustChangePassword, "no forced password change");
        Assert.IsTrue(_store.LoadAudit().Any(a => a.Action == "SUPERADMIN_GRANTED" && a.Target == "patrick"));
    }

    [TestMethod]
    public void PromoteToSuperAdmin_RejectsUnknownAndInactiveAccounts_ListingTheRealOnes()
    {
        StaffAccount gone = Add("gone", StaffRole.Agent);
        Add("here", StaffRole.Admin);
        _store.SetStaffActive(gone.Id, false);

        var unknown = Assert.ThrowsExactly<InvalidOperationException>(() => _store.PromoteToSuperAdmin("nobody"));
        StringAssert.Contains(unknown.Message, "here");
        Assert.ThrowsExactly<InvalidOperationException>(() => _store.PromoteToSuperAdmin("gone"));
    }

    [TestMethod]
    public void RenameStaff_ChangesOnlyTheUsername_SoTheNewNameSignsInAsSuperAdmin()
    {
        StaffAccount before = Add("admin", StaffRole.SuperAdmin);

        _store.RenameStaff("admin", "Patrick");

        var result = _store.Authenticate("patrick", Pass);   // case-insensitive, same password
        Assert.IsTrue(result.Success);
        Assert.AreEqual(StaffRole.SuperAdmin, result.Session!.Role);
        Assert.AreEqual(before.Id, result.Session.Id);
        Assert.IsFalse(_store.Authenticate("admin", Pass).Success, "the old name no longer works");
        Assert.IsTrue(_store.LoadAudit().Any(a => a.Action == "STAFF_RENAMED" && a.Target == "Patrick"));
    }

    [TestMethod]
    public void RenameStaff_RejectsTakenBadAndUnknownNames()
    {
        Add("admin", StaffRole.Admin);
        Add("taken", StaffRole.Agent);

        Assert.ThrowsExactly<InvalidOperationException>(() => _store.RenameStaff("admin", "TAKEN"));
        Assert.ThrowsExactly<ArgumentException>(() => _store.RenameStaff("admin", "no spaces"));
        Assert.ThrowsExactly<InvalidOperationException>(() => _store.RenameStaff("nobody", "Patrick"));
        Assert.IsNotNull(_store.FindStaff("admin"));
    }

    [TestMethod]
    public void CommandLineSwitch_PromotesTheNamedAccount()
    {
        Add("patrick", StaffRole.Admin);
        string? message = CommandLine.Run(new[] { "--make-superadmin", "patrick" }, _store, DateTime.Now);
        StringAssert.Contains(message, "Super Admin");
        Assert.AreEqual(StaffRole.SuperAdmin, _store.FindStaff("patrick")!.Role);
    }
}
