using System.Security.Cryptography;
using SkyWings;
using Microsoft.Data.Sqlite;

namespace SkyWings.Tests;

[TestClass]

[DoNotParallelize]
public sealed class CsvAndPasswordHelperTests
{
    [TestMethod]
    [DataRow("=HYPERLINK(\"http://evil\")", "\"'=HYPERLINK(\"\"http://evil\"\")\"")]
    [DataRow("+1+1", "\"'+1+1\"")]
    [DataRow("-2", "\"'-2\"")]
    [DataRow("@SUM(A1)", "\"'@SUM(A1)\"")]
    [DataRow("Maria Santos", "\"Maria Santos\"")]
    [DataRow("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [DataRow("", "\"\"")]
    public void CsvSafe_NeutralisesFormulasAndQuotesValues(string input, string expected)
    {
        Assert.AreEqual(expected, CsvSafe.Cell(input));
    }

    [TestMethod]
    public void PasswordGenerator_ProducesPasswordsThatPassThePolicy()
    {
        for (int i = 0; i < 20; i++) Assert.IsNull(PasswordPolicy.Check(PasswordGenerator.Generate("maria"), "maria"));
        Assert.AreNotEqual(PasswordGenerator.Generate(), PasswordGenerator.Generate());
    }
}

/// <summary>Encrypted backups and the safety rules of restore, against throwaway SQLite files.</summary>
[TestClass]
[DoNotParallelize]
public sealed class BackupSecurityTests
{
    private const string BackupPassword = "backup passphrase one";
    private const string StaffPassword = "correct horse battery";
    private string _folder = "";
    private string _liveConnection = "";
    private string _livePath = "";
    private string _safetyFolder = "";
    private BookingStore _live = null!;

    [TestInitialize]
    public void Setup()
    {
        _folder = Path.Combine(Path.GetTempPath(), "skywings-backup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        _livePath = Path.Combine(_folder, "live.db");
        _safetyFolder = Path.Combine(_folder, "safety");
        _liveConnection = new SqliteConnectionStringBuilder { DataSource = _livePath, ForeignKeys = true }.ConnectionString;
        _live = new BookingStore(_liveConnection);
        _live.SeedFlightsIfEmpty(AppData.DomesticFlights, AppData.InternationalFlights, 180, 220);
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private static BookingRecord Booking(string reference, string seat)
    {
        var passenger = new PassengerRecord("Maria Santos", "1 Test St", "09171234567", 35, "1991-01-01", "PSP-0000001",
            false, false, new SeatAssignment("Maria Santos", seat), Array.Empty<string>(), 3_500m);
        return new BookingRecord(reference, DateTime.Now, "PR-101", "Manila (MNL)", "Cebu (CEB)", DateTime.Today.AddDays(30), true,
            "Economy", "Credit Card", "**** 4521", 3_500m, "Scheduled", new[] { passenger }, AppData.StatusConfirmed, "PAID",
            "TXN-1", DateTime.Now, 0m);
    }

    private void Save(BookingStore store, string reference, string seat)
        => store.SaveConfirmedBooking(Booking(reference, seat), false, 3_500m, "Credit Card", "**** 4521", "TXN-1", DateTime.Now);

    private string MakeBackup(BookingStore store, string name = "b.skybak")
    {
        string path = Path.Combine(_folder, name);
        DatabaseBackup.Backup(store.ConnectionString, path, BackupPassword);
        return path;
    }

    private void RestoreLive(string backup, string password = BackupPassword)
        => DatabaseBackup.Restore(_liveConnection, backup, password, _safetyFolder);

    // ------------------------------------------------------------------ encryption

    [TestMethod]
    public void BackupFile_IsEncrypted_AndCustomerDataIsNotReadableInIt()
    {
        Save(_live, "SKY-SECRET01", "ECO1A");
        string backup = MakeBackup(_live);

        byte[] bytes = File.ReadAllBytes(backup);
        string asText = System.Text.Encoding.Latin1.GetString(bytes);

        Assert.DoesNotContain("SQLite format", asText);
        Assert.DoesNotContain("Maria Santos", asText);
        Assert.DoesNotContain("SKY-SECRET01", asText);
    }

    [TestMethod]
    public void Decrypt_FailsForWrongPasswordTamperingAndForeignFiles()
    {
        string backup = MakeBackup(_live);
        string plain = Path.Combine(_folder, "out.db");

        Assert.ThrowsExactly<CryptographicException>(() => BackupCrypto.DecryptFile(backup, plain, "not the password at all"));

        byte[] bytes = File.ReadAllBytes(backup);
        bytes[^10] ^= 0xFF;   // flip a bit in the encrypted data
        string tampered = Path.Combine(_folder, "tampered.skybak");
        File.WriteAllBytes(tampered, bytes);
        Assert.ThrowsExactly<CryptographicException>(() => BackupCrypto.DecryptFile(tampered, plain, BackupPassword));

        string foreign = Path.Combine(_folder, "foreign.skybak");
        File.WriteAllBytes(foreign, new byte[200]);
        Assert.ThrowsExactly<InvalidDataException>(() => BackupCrypto.DecryptFile(foreign, plain, BackupPassword));
        Assert.IsFalse(File.Exists(plain), "Nothing may be written when decryption fails.");
    }

    [TestMethod]
    public void Backup_RefusesWeakPasswordsAndLeavesNoPlainTempFile()
    {
        Assert.ThrowsExactly<ArgumentException>(() => DatabaseBackup.Backup(_liveConnection, Path.Combine(_folder, "weak.skybak"), "short"));

        MakeBackup(_live);
        Assert.IsEmpty(Directory.GetFiles(_folder, ".backup-*"), "The plain temporary copy must be deleted.");
    }

    // ------------------------------------------------------------------ restore

    [TestMethod]
    public void Restore_ReplacesBookingData_AndKeepsAPasswordProtectedSafetyCopy()
    {
        Save(_live, "SKY-KEEP0001", "ECO1A");
        string backup = MakeBackup(_live);
        Save(_live, "SKY-LATER001", "ECO2A");   // made after the backup

        RestoreLive(backup);

        var after = new BookingStore(_liveConnection);
        Assert.IsNotNull(after.FindByReference("SKY-KEEP0001"));
        Assert.IsNull(after.FindByReference("SKY-LATER001"), "Data newer than the backup is replaced.");
        string safety = Directory.GetFiles(_safetyFolder, "*" + BackupCrypto.Extension).Single();
        // The safety copy opens with the same password and still contains the data that was just replaced.
        string plain = Path.Combine(_folder, "safety.db");
        BackupCrypto.DecryptFile(safety, plain, BackupPassword);
        DatabaseBackup.Validate(plain);
    }

    [TestMethod]
    public void Restore_NeverTakesStaffAccountsOrAuditLogFromTheBackup()
    {
        // The "attacker" builds their own database with an administrator they control, and makes a valid backup of it.
        string evilPath = Path.Combine(_folder, "evil.db");
        string evilConnection = new SqliteConnectionStringBuilder { DataSource = evilPath }.ConnectionString;
        var evil = new BookingStore(evilConnection);
        evil.SeedFlightsIfEmpty(AppData.DomesticFlights, AppData.InternationalFlights, 180, 220);
        evil.CreateStaff("evil.admin", "Evil Admin", "attackers own password", StaffRole.Admin, false);
        evil.WriteAudit("evil.admin", "ADMIN", "FAKE_ENTRY", "x", "planted");
        string evilBackup = MakeBackup(evil, "evil.skybak");

        _live.CreateStaff("real.admin", "Real Admin", StaffPassword, StaffRole.Admin, false);
        _live.WriteAudit("real.admin", "ADMIN", "REAL_ENTRY");

        RestoreLive(evilBackup);

        var after = new BookingStore(_liveConnection);
        Assert.IsNull(after.FindStaff("evil.admin"), "A backup must not be able to add staff.");
        Assert.IsNotNull(after.FindStaff("real.admin"));
        Assert.IsTrue(after.Authenticate("real.admin", StaffPassword).Success);
        var audit = after.LoadAudit();
        Assert.IsTrue(audit.Any(a => a.Action == "REAL_ENTRY"));
        Assert.IsFalse(audit.Any(a => a.Action == "FAKE_ENTRY"));
    }

    [TestMethod]
    public void Restore_WithWrongPassword_LeavesLiveDataUntouched()
    {
        Save(_live, "SKY-KEEP0002", "ECO1A");
        string backup = MakeBackup(_live);
        Save(_live, "SKY-LATER002", "ECO2A");

        Assert.ThrowsExactly<CryptographicException>(() => RestoreLive(backup, "the wrong backup password"));

        var after = new BookingStore(_liveConnection);
        Assert.IsNotNull(after.FindByReference("SKY-LATER002"), "Live data must still be there.");
        Assert.IsFalse(Directory.Exists(_safetyFolder) && Directory.GetFiles(_safetyFolder).Length > 0, "No safety copy is made for a failed restore.");
    }

    // ------------------------------------------------------------------ validation

    private string PlainCopy(Action<SqliteConnection>? tamper = null)
    {
        string path = Path.Combine(_folder, "candidate-" + Guid.NewGuid().ToString("N") + ".db");
        using (var con = new SqliteConnection(_liveConnection))
        {
            con.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "VACUUM INTO $d";
            cmd.Parameters.AddWithValue("$d", path);
            cmd.ExecuteNonQuery();
        }
        if (tamper is not null)
        {
            using var con = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ConnectionString);
            con.Open();
            tamper(con);
        }
        return path;
    }

    private static void Run(SqliteConnection con, string sql)
    {
        using var cmd = con.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    [TestMethod]
    public void Validate_AcceptsAHealthyDatabase() => DatabaseBackup.Validate(PlainCopy());

    [TestMethod]
    public void Validate_RejectsTriggersViewsMissingColumnsAndCorruption()
    {
        string withTrigger = PlainCopy(c => Run(c, "CREATE TRIGGER t AFTER INSERT ON bookings BEGIN DELETE FROM passengers; END"));
        string withView = PlainCopy(c => Run(c, "CREATE VIEW v AS SELECT * FROM bookings"));
        string missingColumn = PlainCopy(c => Run(c, "ALTER TABLE flights DROP COLUMN capacity"));
        string notADatabase = Path.Combine(_folder, "notadb.db");
        File.WriteAllText(notADatabase, "this is just some text, not a database at all............");
        string corrupt = PlainCopy();
        byte[] bytes = File.ReadAllBytes(corrupt);
        for (int i = bytes.Length / 2; i < bytes.Length / 2 + 4096 && i < bytes.Length; i++) bytes[i] = 0xAB;
        File.WriteAllBytes(corrupt, bytes);

        foreach (string file in new[] { withTrigger, withView, missingColumn, notADatabase, corrupt })
            Assert.ThrowsExactly<InvalidDataException>(() => DatabaseBackup.Validate(file), Path.GetFileName(file));
    }

    // ------------------------------------------------------------------ who may restore

    [TestMethod]
    public void AdminService_Restore_NeedsTheStaffMembersOwnPassword_AndTheRightRole()
    {
        StaffAccount admin = _live.CreateStaff("boss2", "Boss Two", StaffPassword, StaffRole.Admin, false);
        StaffAccount agent = _live.CreateStaff("agent9", "Agent Nine", StaffPassword, StaffRole.Agent, false);
        string backup = MakeBackup(_live);
        var adminService = new AdminService(_live, new StaffSession(admin.Id, admin.Username, admin.DisplayName, admin.Role, false));
        var agentService = new AdminService(_live, new StaffSession(agent.Id, agent.Username, agent.DisplayName, agent.Role, false));

        Assert.ThrowsExactly<UnauthorizedAccessException>(() => agentService.Restore(backup, BackupPassword, StaffPassword));
        Assert.ThrowsExactly<UnauthorizedAccessException>(() => agentService.Backup(Path.Combine(_folder, "x.skybak"), BackupPassword));
        Assert.ThrowsExactly<UnauthorizedAccessException>(() => adminService.Restore(backup, BackupPassword, "wrong staff password"));
        Assert.IsTrue(_live.LoadAudit().Any(a => a.Action == "RESTORE_REFUSED"));

        adminService.Restore(backup, BackupPassword, StaffPassword);
        Assert.IsTrue(new BookingStore(_liveConnection).LoadAudit().Any(a => a.Action == "RESTORE_COMPLETED" && a.Actor == "boss2"));
    }
}
