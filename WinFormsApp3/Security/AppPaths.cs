using System.Security.AccessControl;
using System.Security.Principal;

namespace SkyWings;

/// <summary>
/// Where SkyWings keeps its data. Defaults to <c>%LocalAppData%\SkyWings</c>, a per-user folder that is locked to the
/// current Windows user at startup. Set the <c>SKYWINGS_DATA_DIR</c> environment variable to use another folder
/// (used by tests and tools so they never touch real customer data).
/// </summary>
public static class AppPaths
{
    public const string DataDirectoryVariable = "SKYWINGS_DATA_DIR";
    public const string DatabaseFileName = "skywings.db";

    private static readonly object Sync = new();
    private static bool _prepared;

    public static string DataDirectory { get; } = ResolveDataDirectory();
    public static string LogDirectory => Path.Combine(DataDirectory, "logs");
    public static string SafetyBackupDirectory => Path.Combine(DataDirectory, "safety-backups");
    public static string DatabasePath => Path.Combine(DataDirectory, DatabaseFileName);

    private static string ResolveDataDirectory()
    {
        string? overridePath = Environment.GetEnvironmentVariable(DataDirectoryVariable);
        if (!string.IsNullOrWhiteSpace(overridePath)) return Path.GetFullPath(overridePath);
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SkyWings");
    }

    /// <summary>
    /// Creates the data folder, restricts it to the current user, and copies a database that an older version kept
    /// next to the program (once, never overwriting). Safe to call many times.
    /// </summary>
    public static void Prepare()
    {
        lock (Sync)
        {
            if (_prepared) return;
            _prepared = true;
            Directory.CreateDirectory(DataDirectory);
            RestrictToCurrentUser(DataDirectory);
            CopyLegacyDatabase();
        }
    }

    private static void RestrictToCurrentUser(string directory)
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            SecurityIdentifier? user = WindowsIdentity.GetCurrent().User;
            if (user is null) return;
            var security = new DirectorySecurity();
            // No inheritance from the parent: only this user (and the files below the folder) get access.
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl,
                InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit, PropagationFlags.None,
                AccessControlType.Allow));
            new DirectoryInfo(directory).SetAccessControl(security);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or InvalidOperationException
                                       or PlatformNotSupportedException or System.Security.SecurityException)
        {
            // Not fatal (for example on a share without ACL support), but worth knowing about.
            AppLogger.Info("Security", "Could not restrict the data folder to the current user: " + ex.Message);
        }
    }

    private static void CopyLegacyDatabase()
    {
        string legacy = Path.Combine(AppContext.BaseDirectory, DatabaseFileName);
        if (!File.Exists(legacy) || File.Exists(DatabasePath)) return;
        try
        {
            File.Copy(legacy, DatabasePath, overwrite: false);
            AppLogger.Info("Security", "Copied the existing database into the protected data folder. The old file was left in place.");
        }
        catch (IOException ex)
        {
            AppLogger.Error("Security", ex);
        }
    }
}
