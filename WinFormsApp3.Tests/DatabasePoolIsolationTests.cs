using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace SkyWings.Tests;

/// <summary>
/// Tests run in parallel and each has its own database, so nothing may touch another database's connection pool.
/// <c>SqliteConnection.ClearAllPools()</c> disposes every idle pooled connection in the process: a test (or the restore code) calling it
/// could dispose a connection another test had just taken from its own pool, which showed up as random
/// "Cannot access a disposed object. Object name: 'SQLitePCL.sqlite3'" failures.
/// </summary>
[TestClass]
public sealed class DatabasePoolIsolationTests
{
    private static string TestProjectFolder()
    {
        string? dir = AppContext.BaseDirectory;
        while (dir is not null && !string.Equals(Path.GetFileName(dir.TrimEnd(Path.DirectorySeparatorChar)), "WinFormsApp3.Tests", StringComparison.OrdinalIgnoreCase))
            dir = Path.GetDirectoryName(dir.TrimEnd(Path.DirectorySeparatorChar));
        return dir ?? throw new InvalidOperationException("The test project folder was not found above " + AppContext.BaseDirectory);
    }

    [TestMethod]
    public void Restore_ReleasesOnlyTheLiveDatabasesPool_NotEveryPoolInTheProcess()
    {
        string folder = Path.Combine(Path.GetTempPath(), "skywings-poolisolation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string Pooled(string file) => new SqliteConnectionStringBuilder { DataSource = Path.Combine(folder, file), ForeignKeys = true }.ConnectionString;
        string Unpooled(string file) => new SqliteConnectionStringBuilder { DataSource = Path.Combine(folder, file), ForeignKeys = true, Pooling = false }.ConnectionString;
        try
        {
            // Another test's database, on a pooled connection that is back in the pool and idle.
            var other = new BookingStore(Unpooled("other.db"));
            other.SeedFlightsIfEmpty(AppData.DomesticFlights, AppData.InternationalFlights, 180, 220);
            string otherPooled = Pooled("other.db");
            nint before;
            using (var warm = new SqliteConnection(otherPooled)) { warm.Open(); before = warm.Handle!.DangerousGetHandle(); }   // returns to the pool on dispose

            // A restore of a different, live database.
            var live = new BookingStore(Unpooled("live.db"));
            live.SeedFlightsIfEmpty(AppData.DomesticFlights, AppData.InternationalFlights, 180, 220);
            string backup = Path.Combine(folder, "backup" + BackupCrypto.Extension);
            const string password = "A-long-backup-phrase-1";
            DatabaseBackup.Backup(Unpooled("live.db"), backup, password);
            DatabaseBackup.Restore(Unpooled("live.db"), backup, password, Path.Combine(folder, "safety"));

            nint after;
            using (var again = new SqliteConnection(otherPooled)) { again.Open(); after = again.Handle!.DangerousGetHandle(); }
            Assert.AreEqual(before, after, "the other database's idle pooled connection was disposed by the restore");
        }
        finally
        {
            SqliteConnection.ClearPool(new SqliteConnection(Pooled("other.db")));
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { }
        }
    }

    [TestMethod]
    public void NoTest_ClearsEveryPoolInTheProcess_AndEveryTestConnectionStringTurnsPoolingOff()
    {
        var problems = new List<string>();
        var builder = new Regex(@"new SqliteConnectionStringBuilder\s*\{[^}]*\}", RegexOptions.Singleline);
        foreach (string file in Directory.EnumerateFiles(TestProjectFolder(), "*.cs", SearchOption.AllDirectories))
        {
            if (file.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar) || file.Contains(Path.DirectorySeparatorChar + "bin" + Path.DirectorySeparatorChar)) continue;
            if (Path.GetFileName(file) == nameof(DatabasePoolIsolationTests) + ".cs") continue;
            string text = File.ReadAllText(file);
            if (text.Contains("ClearAllPools", StringComparison.Ordinal)) problems.Add($"{Path.GetFileName(file)} calls ClearAllPools");
            foreach (Match m in builder.Matches(text))
                if (!m.Value.Contains("Pooling = false", StringComparison.Ordinal))
                    problems.Add($"{Path.GetFileName(file)}: {Regex.Replace(m.Value, @"\s+", " ")} (add Pooling = false)");
        }
        Assert.IsEmpty(problems, "Tests run in parallel: give each test database Pooling = false instead of clearing all pools.\n" + string.Join("\n", problems));
    }
}
