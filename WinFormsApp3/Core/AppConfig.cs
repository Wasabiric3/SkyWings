using System.Globalization;
using Microsoft.Data.Sqlite;

namespace SkyWings;

/// <summary>
/// Reads the same db.properties file the Java app used, so you do not need a new config format.
/// Port of the property-loading code in BookingStore + the admin login values.
/// </summary>
public static class AppConfig
{
    private const string PropsFile = "db.properties";

    public static string DbUrl { get; }
    public static string DbUser { get; }
    public static string DbPassword { get; }
    /// <summary>Optional mail server for receipt emails. Without it, emails are saved to the outbox folder instead.</summary>
    public static SkyWings.Email.SmtpSettings Smtp { get; }
    /// <summary>Where refund request emails are read from. Reuses the mail account above unless imap.* keys say otherwise.</summary>
    public static SkyWings.Email.ImapSettings Imap { get; }
    // The old single shared admin account. Only read once, to move it into the staff accounts table; after that
    // these settings are ignored and should be deleted from db.properties.
    private static string LegacyAdminUsername { get; set; } = "admin";
    private static string LegacyAdminPassword { get; set; } = "";
    private static string LegacyAdminPasswordHash { get; set; } = "";

    static AppConfig()
    {
        var props = LoadProperties();
        DbUrl = Get(props, "db.url", "Data Source=" + AppPaths.DatabasePath);
        DbUser = Get(props, "db.user", "");
        DbPassword = Get(props, "db.password", "");
        int port = int.TryParse(Get(props, "smtp.port", "587"), NumberStyles.None, CultureInfo.InvariantCulture, out int p) && p is > 0 and <= 65535 ? p : 587;
        bool ssl = !string.Equals(Get(props, "smtp.ssl", "true"), "false", StringComparison.OrdinalIgnoreCase);
        Smtp = new SkyWings.Email.SmtpSettings(Get(props, "smtp.host", ""), port, Get(props, "smtp.user", ""),
            ReadSmtpPassword(Get(props, "smtp.password", "")), Get(props, "smtp.from", ""), ssl);
        string imapHost = Get(props, "imap.host", SkyWings.Email.ImapSettings.HostFor(Smtp.Host));
        int imapPort = int.TryParse(Get(props, "imap.port", "993"), NumberStyles.None, CultureInfo.InvariantCulture, out int ip) && ip is > 0 and <= 65535 ? ip : 993;
        bool imapOn = !string.Equals(Get(props, "imap.enabled", "true"), "false", StringComparison.OrdinalIgnoreCase);
        Imap = Smtp.IsConfigured
            ? new SkyWings.Email.ImapSettings(imapHost, imapPort, Get(props, "imap.user", Smtp.User),
                props.ContainsKey("imap.password") ? ReadSmtpPassword(Get(props, "imap.password", "")) : Smtp.Password, imapOn)
            : SkyWings.Email.ImapSettings.None;
        LegacyAdminUsername = Get(props, "admin.username", "admin");
        LegacyAdminPassword = Get(props, "admin.password", "");
        LegacyAdminPasswordHash = Get(props, "admin.password.hash", "");
    }

    /// <summary>The settings file that is in use (next to the exe, else the data folder), or null when there is none.</summary>
    public static string? PropertiesPath
        => new[] { Path.Combine(AppContext.BaseDirectory, PropsFile), Path.Combine(AppPaths.DataDirectory, PropsFile) }
            .FirstOrDefault(File.Exists);

    // A DPAPI-protected password is decrypted here. If it cannot be read (another Windows user or machine), mail falls back
    // to the outbox folder instead of the program failing to start.
    private static string ReadSmtpPassword(string value)
    {
        try { return SecretProtector.Unprotect(value); }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or FormatException)
        {
            AppLogger.Info("Config", "smtp.password could not be decrypted (it was protected by another Windows user or machine). Enter it again as plain text and run --protect-smtp-password.");
            return "";
        }
    }

    private static string Get(Dictionary<string, string> p, string key, string fallback)
        => p.TryGetValue(key, out var v) ? v : fallback;

    private static Dictionary<string, string> LoadProperties()
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        // Next to the exe, then the protected data folder. The current working directory is deliberately NOT searched:
        // anyone who can start the program from a folder they control could otherwise plant their own settings.
        string[] candidates =
        {
            Path.Combine(AppContext.BaseDirectory, PropsFile),
            Path.Combine(AppPaths.DataDirectory, PropsFile)
        };
        foreach (var path in candidates)
        {
            if (!File.Exists(path)) continue;
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#') || line.StartsWith('!')) continue;
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                result[line[..eq].Trim()] = line[(eq + 1)..].Trim();
            }
            break;
        }
        return result;
    }

    /// <summary>Builds a SQLite connection string from db.url or db.path.</summary>
    public static string BuildConnectionString()
    {
        AppPaths.Prepare();   // creates and locks the data folder, copies an old database once

        string value = DbUrl.Trim();
        if (value.StartsWith("jdbc:", StringComparison.OrdinalIgnoreCase))
            value = "Data Source=" + AppPaths.DatabasePath;

        if (!value.Contains('=', StringComparison.Ordinal))
            value = $"Data Source={value}";

        var csb = new SqliteConnectionStringBuilder(value)
        {
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true
        };

        if (csb.DataSource != ":memory:")
        {
            string fullPath = Path.GetFullPath(csb.DataSource, AppContext.BaseDirectory);
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            csb.DataSource = fullPath;
        }

        return csb.ConnectionString;
    }

    /// <summary>
    /// Moves the old single admin from db.properties into the staff accounts, once, and only while no staff exist.
    /// The imported person must choose a new password at first sign-in.
    /// </summary>
    public static bool ImportLegacyAdmin(BookingStore store)
    {
        if (LegacyAdminPassword.Length == 0 && LegacyAdminPasswordHash.Length == 0) return false;
        bool imported = store.ImportLegacyAdmin(LegacyAdminUsername, LegacyAdminPasswordHash, LegacyAdminPassword);
        if (imported)
            AppLogger.Info("Security", "The admin account from db.properties was moved into the staff accounts. " +
                                       "Delete the admin.* lines from that file; they are no longer used.");
        return imported;
    }

    public static string Invariant(this DateTime dt, string format)
        => dt.ToString(format, CultureInfo.InvariantCulture);
}
