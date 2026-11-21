using Microsoft.Data.Sqlite;

namespace SkyWings.Tests;

/// <summary>The admin database browser: generic table access that must stay safe, masked and admin-only.</summary>
[TestClass]
[DoNotParallelize]
public sealed class DataBrowserTests
{
    private string _dbPath = "";
    private BookingStore _store = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"skywings-browser-{Guid.NewGuid():N}.db");
        _store = new BookingStore(new SqliteConnectionStringBuilder { DataSource = _dbPath, ForeignKeys = true }.ConnectionString);
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private static Dictionary<string, string?> Row(params (string, string?)[] pairs) => pairs.ToDictionary(p => p.Item1, p => p.Item2);

    private static Dictionary<string, string?> Promo(string code, string value) => Row(
        ("code", code), ("discount_type", "PERCENT"), ("discount_value", value),
        ("starts_on", "2026-01-01"), ("ends_on", "2026-12-31"));

    [TestMethod]
    public void ListTables_IncludesAppTablesAndHidesSqliteInternals()
    {
        var tables = _store.ListTables();
        CollectionAssert.Contains(tables, "bookings");
        CollectionAssert.Contains(tables, "promo_codes");
        Assert.IsFalse(tables.Any(t => t.StartsWith("sqlite_", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void InsertUpdateDelete_RoundTripsOnASimpleTable()
    {
        int baseline = _store.QueryTable("promo_codes").Rows.Count;   // the store seeds a few promo codes

        _store.InsertRow("promo_codes", Promo("TEST10", "10"));
        var rows = _store.QueryTable("promo_codes", "TEST10");
        Assert.AreEqual(1, rows.Rows.Count);
        Assert.AreEqual("TEST10", rows.Rows[0]["code"]);
        Assert.AreEqual(baseline + 1, _store.QueryTable("promo_codes").Rows.Count);

        _store.UpdateRow("promo_codes", Row(("code", "TEST10")), Row(("discount_value", "25")));
        Assert.AreEqual("25", _store.QueryTable("promo_codes", "TEST10").Rows[0]["discount_value"]);

        _store.DeleteRow("promo_codes", Row(("code", "TEST10")));
        Assert.AreEqual(baseline, _store.QueryTable("promo_codes").Rows.Count);
    }

    [TestMethod]
    public void Search_FindsRowsByAnyColumn()
    {
        _store.InsertRow("promo_codes", Promo("ALPHA", "5"));
        _store.InsertRow("promo_codes", Promo("BRAVO", "5"));
        Assert.AreEqual(1, _store.QueryTable("promo_codes", "alph").Rows.Count);
        Assert.AreEqual(_store.QueryTable("promo_codes").Rows.Count, _store.QueryTable("promo_codes", "").Rows.Count, "a blank search shows everything");
    }

    [TestMethod]
    public void UnknownOrInjectedNames_AreRejected()
    {
        Assert.ThrowsExactly<ArgumentException>(() => _store.QueryTable("nope"));
        Assert.ThrowsExactly<ArgumentException>(() => _store.QueryTable("promo_codes; DROP TABLE bookings"));
        Assert.ThrowsExactly<ArgumentException>(() => _store.DeleteRow("x\" ; DROP TABLE bookings; --", Row(("a", "1"))));
        CollectionAssert.Contains(_store.ListTables(), "bookings");
    }

    [TestMethod]
    public void BadNumbers_AndMissingRequiredFields_GiveClearErrors()
    {
        var bad = Promo("BAD", "ten");
        var ex = Assert.ThrowsExactly<ArgumentException>(() => _store.InsertRow("promo_codes", bad));
        StringAssert.Contains(ex.Message, "discount_value");

        var missing = Promo("MISSING", "");
        Assert.ThrowsExactly<ArgumentException>(() => _store.InsertRow("promo_codes", missing));
    }

    [TestMethod]
    public void DatabaseConstraints_SurfaceAsReadableErrors()
    {
        _store.InsertRow("promo_codes", Promo("DUP", "1"));
        var ex = Assert.ThrowsExactly<InvalidOperationException>(() => _store.InsertRow("promo_codes", Promo("DUP", "2")));
        StringAssert.Contains(ex.Message, "rejected");

        // booking_legs.booking_ref has a foreign key to bookings.
        var orphan = Row(("booking_ref", "NOPE"), ("leg_order", "1"), ("flight_no", "PR-101"), ("origin", "A"), ("destination", "B"),
            ("flight_date", "2026-01-01"), ("domestic", "1"), ("realtime_est", "x"));
        Assert.ThrowsExactly<InvalidOperationException>(() => _store.InsertRow("booking_legs", orphan));
    }

    [TestMethod]
    public void CompositeKeyTables_NeedEveryKeyColumn()
    {
        var info = _store.DescribeTable("booking_legs");
        Assert.AreEqual(2, info.Columns.Count(c => c.IsKey));
        Assert.ThrowsExactly<ArgumentException>(() => _store.DeleteRow("booking_legs", Row(("booking_ref", "X"))));
    }

    [TestMethod]
    public void StaffAudit_AndMigrationTables_AreViewOnly_AndSecretsAreMasked()
    {
        foreach (var t in new[] { "staff_users", "audit_log", "schema_migrations" })
            Assert.IsTrue(_store.DescribeTable(t).ReadOnly, t);
        Assert.ThrowsExactly<InvalidOperationException>(() => _store.DeleteRow("audit_log", Row(("id", "1"))));

        using (var con = new SqliteConnection(_store.ConnectionString))
        {
            con.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "INSERT INTO staff_users (username, display_name, password_hash, role, created_at) VALUES ('t', 'T', 'SECRETHASH', 'AGENT', '2026-01-01')";
            cmd.ExecuteNonQuery();
        }
        var rows = _store.QueryTable("staff_users");
        Assert.AreEqual(BookingStore.MaskedValue, rows.Rows[0]["password_hash"]);
        Assert.AreEqual(0, _store.QueryTable("staff_users", "SECRETHASH").Rows.Count, "a secret must not be searchable");
    }

    [TestMethod]
    public void AgentIsRefused_AdminIsAllowed_AndChangesAreAudited()
    {
        var agent = new AdminService(_store, new StaffSession(1, "agent", "Agent", StaffRole.Agent, false));
        Assert.ThrowsExactly<UnauthorizedAccessException>(() => agent.ListTables());
        Assert.ThrowsExactly<UnauthorizedAccessException>(() => agent.InsertRow("promo_codes", Promo("A", "1")));

        var plainAdmin = new AdminService(_store, new StaffSession(3, "plain", "Plain", StaffRole.Admin, false));
        Assert.ThrowsExactly<UnauthorizedAccessException>(() => plainAdmin.ListTables(), "only a Super Admin may browse the database");

        var admin = new AdminService(_store, new StaffSession(2, "admin", "Admin", StaffRole.SuperAdmin, false));
        admin.InsertRow("promo_codes", Promo("ADM", "1"));
        admin.DeleteRow("promo_codes", Row(("code", "ADM")));

        var actions = _store.LoadAudit().Select(a => a.Action).ToList();
        CollectionAssert.Contains(actions, "DB_INSERT");
        CollectionAssert.Contains(actions, "DB_DELETE");
        Assert.IsTrue(_store.LoadAudit().Any(a => a.Action == "DB_INSERT" && a.Detail.Contains("code='ADM'")), "the inserted values are logged");
    }

    [TestMethod]
    public void Update_AuditShowsOnlyTheChangedColumns_BeforeAndAfter()
    {
        var admin = new AdminService(_store, new StaffSession(2, "admin", "Admin", StaffRole.SuperAdmin, false));
        admin.InsertRow("promo_codes", Promo("CHG", "10"));
        admin.UpdateRow("promo_codes", Row(("code", "CHG")), Row(("discount_value", "25"), ("discount_type", "PERCENT")));

        string detail = _store.LoadAudit().First(a => a.Action == "DB_UPDATE").Detail;
        StringAssert.Contains(detail, "code=CHG");
        StringAssert.Contains(detail, "discount_value: '10' → '25'");
        Assert.DoesNotContain("discount_type", detail, "an unchanged column is not listed");
    }

    [TestMethod]
    public void Delete_AuditKeepsWhatTheRowHeld()
    {
        var admin = new AdminService(_store, new StaffSession(2, "admin", "Admin", StaffRole.SuperAdmin, false));
        admin.InsertRow("promo_codes", Promo("GONE", "15"));
        admin.DeleteRow("promo_codes", Row(("code", "GONE")));

        string detail = _store.LoadAudit().First(a => a.Action == "DB_DELETE").Detail;
        StringAssert.Contains(detail, "was ");
        StringAssert.Contains(detail, "discount_value='15'");
    }

    [TestMethod]
    public void AuditValue_HidesCustomerDetailsAndSecrets_AndShortensLongText()
    {
        DataColumnInfo Col(string name, bool masked = false) => new(name, "VARCHAR(40)", true, false, false, masked);
        Assert.AreEqual("(hidden)", BookingStore.AuditValue(Col("id_number"), "P1234567A"));
        Assert.AreEqual("(hidden)", BookingStore.AuditValue(Col("birthday"), "1991-01-01"));
        Assert.AreEqual("(hidden)", BookingStore.AuditValue(Col("contact_email"), "a@b.c"));
        Assert.AreEqual("(hidden)", BookingStore.AuditValue(Col("password_hash", masked: true), "x"));
        Assert.AreEqual("NULL", BookingStore.AuditValue(Col("note"), null));
        Assert.AreEqual("'a b'", BookingStore.AuditValue(Col("note"), "a\nb"));
        Assert.AreEqual("'" + new string('x', 30) + "…'", BookingStore.AuditValue(Col("note"), new string('x', 50)));
    }
}