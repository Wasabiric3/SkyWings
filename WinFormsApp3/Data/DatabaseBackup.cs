using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace SkyWings;

/// <summary>
/// Encrypted backups and a careful restore.
/// Backup: copy the live database with VACUUM INTO, encrypt the copy, delete the plain copy.
/// Restore: decrypt to a temporary file, validate it strictly, keep the LIVE staff and audit tables (a backup file must
/// never be able to add administrators or roll back passwords), save an encrypted safety copy, then swap the file.
/// If any step fails the live database is left untouched.
/// </summary>
public static class DatabaseBackup
{
    // Tables and columns a SkyWings database must have before it may replace the live one.
    private static readonly Dictionary<string, string[]> RequiredColumns = new()
    {
        ["bookings"] = new[] { "reference", "booked_at", "flight_no", "flight_date", "class_name", "total_amount", "status", "payment_status", "refund_amount" },
        ["passengers"] = new[] { "booking_ref", "name", "seat_code" },
        ["flights"] = new[] { "flight_no", "origin", "destination", "base_price", "capacity", "active" }
    };

    public static string DatabasePathOf(string connectionString)
        => Path.GetFullPath(new SqliteConnectionStringBuilder(connectionString).DataSource, AppContext.BaseDirectory);

    public static void Backup(string liveConnectionString, string destination, string password)
    {
        if (PasswordPolicy.Check(password) is { } problem) throw new ArgumentException("Backup password: " + problem);
        string fullDestination = Path.GetFullPath(destination);
        if (!fullDestination.EndsWith(BackupCrypto.Extension, StringComparison.OrdinalIgnoreCase))
            fullDestination += BackupCrypto.Extension;
        Directory.CreateDirectory(Path.GetDirectoryName(fullDestination)!);

        string plain = Path.Combine(Path.GetDirectoryName(DatabasePathOf(liveConnectionString))!, ".backup-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            CopyDatabase(liveConnectionString, plain);
            BackupCrypto.EncryptFile(plain, fullDestination, password);
        }
        finally
        {
            DeleteQuietly(plain);
        }
    }

    public static void Restore(string liveConnectionString, string source, string backupPassword, string safetyBackupDirectory)
    {
        string live = DatabasePathOf(liveConnectionString);
        string directory = Path.GetDirectoryName(live)!;
        string candidate = Path.Combine(directory, ".restore-" + Guid.NewGuid().ToString("N") + ".tmp");
        string safetyPlain = Path.Combine(directory, ".safety-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            BackupCrypto.DecryptFile(Path.GetFullPath(source), candidate, backupPassword);
            Validate(candidate);
            KeepLiveSecurityTables(candidate, liveConnectionString);
            Validate(candidate);

            // Everything above can fail without touching live data. From here on, a safety copy comes first.
            Directory.CreateDirectory(safetyBackupDirectory);
            CopyDatabase(liveConnectionString, safetyPlain);
            string safetyFile = Path.Combine(safetyBackupDirectory, $"before-restore-{DateTime.Now:yyyyMMdd-HHmmss}{BackupCrypto.Extension}");
            BackupCrypto.EncryptFile(safetyPlain, safetyFile, backupPassword);

            // Release the pooled handles on the old file before replacing it. Only the live database's pool: ClearAllPools would also dispose
            // idle connections of every other database in the process (and of any other thread using one), which then fail mid-use.
            using (var liveHandle = new SqliteConnection(liveConnectionString)) SqliteConnection.ClearPool(liveHandle);
            File.Move(candidate, live, overwrite: true);
        }
        finally
        {
            DeleteQuietly(candidate);
            DeleteQuietly(safetyPlain);
        }
    }

    /// <summary>Throws <see cref="InvalidDataException"/> when the file is not a healthy, plain SkyWings database.</summary>
    public static void Validate(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists) throw new FileNotFoundException("The database was not found.", path);
        if (info.Length > BackupCrypto.MaxPlainBytes) throw new InvalidDataException("The database is too large.");

        try
        {
            var builder = new SqliteConnectionStringBuilder { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false };
            using var con = new SqliteConnection(builder.ConnectionString);
            con.Open();

            // 1. Not corrupt.
            using (var check = con.CreateCommand())
            {
                check.CommandText = "PRAGMA integrity_check";
                var results = new List<string>();
                using var rs = check.ExecuteReader();
                while (rs.Read()) results.Add(rs.GetString(0));
                if (results.Count != 1 || results[0] != "ok")
                    throw new InvalidDataException("The database failed its integrity check, so it was not restored.");
            }

            // 2. No triggers or views: a file from outside could use them to run SQL later, when the app writes to it.
            using (var extras = con.CreateCommand())
            {
                extras.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type IN ('trigger','view')";
                if (Convert.ToInt32(extras.ExecuteScalar()) > 0)
                    throw new InvalidDataException("The database contains triggers or views, which SkyWings never creates. It was not restored.");
            }

            // 3. The tables and columns the app needs are there.
            foreach (var (table, columns) in RequiredColumns)
            {
                using var info2 = con.CreateCommand();
                info2.CommandText = $"PRAGMA table_info({table})";   // table names come from the fixed list above
                var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using var rs = info2.ExecuteReader();
                while (rs.Read()) present.Add(rs.GetString(1));
                if (columns.Any(c => !present.Contains(c)))
                    throw new InvalidDataException("The selected file is not a complete SkyWings database.");
            }
        }
        catch (SqliteException)
        {
            throw new InvalidDataException("The selected file is not a valid SkyWings database.");
        }
    }

    /// <summary>
    /// Replaces the staff and audit tables in <paramref name="candidate"/> with the live ones. The backup supplies booking
    /// data only; who may sign in, and the record of what they did, always stays what it is today.
    /// </summary>
    private static void KeepLiveSecurityTables(string candidate, string liveConnectionString)
    {
        // Drop whatever the file claims for these tables, then let the normal schema code recreate them.
        var candidateConnection = new SqliteConnectionStringBuilder { DataSource = candidate, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ConnectionString;
        using (var con = new SqliteConnection(candidateConnection))
        {
            con.Open();
            using var drop = con.CreateCommand();
            drop.CommandText = "DROP TABLE IF EXISTS staff_users; DROP TABLE IF EXISTS audit_log;";
            drop.ExecuteNonQuery();
        }
        _ = new BookingStore(new SqliteConnectionStringBuilder(candidateConnection) { Mode = SqliteOpenMode.ReadWriteCreate }.ConnectionString);

        using var target = new SqliteConnection(new SqliteConnectionStringBuilder(candidateConnection) { Pooling = false }.ConnectionString);
        target.Open();
        using var attach = target.CreateCommand();
        attach.CommandText = "ATTACH DATABASE $live AS live";
        attach.Parameters.AddWithValue("$live", DatabasePathOf(liveConnectionString));
        attach.ExecuteNonQuery();
        using var copy = target.CreateCommand();
        copy.CommandText =
            "INSERT INTO main.staff_users SELECT * FROM live.staff_users;" +
            "INSERT INTO main.audit_log SELECT * FROM live.audit_log;";
        copy.ExecuteNonQuery();
        using var detach = target.CreateCommand();
        detach.CommandText = "DETACH DATABASE live";
        detach.ExecuteNonQuery();
        SqliteConnection.ClearPool(target);
    }

    private static void CopyDatabase(string liveConnectionString, string destination)
    {
        using var con = new SqliteConnection(liveConnectionString);
        con.Open();
        using var command = con.CreateCommand();
        command.CommandText = "VACUUM INTO $destination";
        command.Parameters.AddWithValue("$destination", destination);
        command.ExecuteNonQuery();
    }

    private static void DeleteQuietly(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (IOException) { /* a leftover temp file is protected by the data folder permissions */ }
        catch (UnauthorizedAccessException) { }
    }
}
