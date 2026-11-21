namespace SkyWings;

/// <summary>
/// Maintenance switches given when starting SkyWings from a terminal. Not shown anywhere in the app:
/// <c>SkyWings.exe --reset-simulation</c> deletes the simulated traffic and generates it again (real bookings are never touched).
/// </summary>
internal static class CommandLine
{
    public const string ResetSimulation = "--reset-simulation";

    /// <summary><c>SkyWings.exe --make-superadmin &lt;username&gt;</c> makes an existing account a Super Admin; its password is not touched.</summary>
    public const string MakeSuperAdmin = "--make-superadmin";

    /// <summary><c>SkyWings.exe --rename-staff &lt;old&gt; &lt;new&gt;</c> changes an account's sign-in name; its password and role are not touched.</summary>
    public const string RenameStaff = "--rename-staff";

    /// <summary>Runs the switch in <paramref name="args"/> and returns what to tell the user, or null when there is none (start normally).</summary>
    public static string? Run(string[] args, BookingStore store, DateTime now)
    {
        int rename = Array.FindIndex(args, a => string.Equals(a.Trim(), RenameStaff, StringComparison.OrdinalIgnoreCase));
        if (rename >= 0)
        {
            string oldName = rename + 1 < args.Length ? args[rename + 1] : "";
            string newName = rename + 2 < args.Length ? args[rename + 2] : "";
            try
            {
                var account = store.RenameStaff(oldName, newName);
                return $"The account is now called '{account.Username}'. Its password and role were not changed.";
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException)
            {
                AppLogger.Error("CommandLine", ex);
                return "Could not rename the account: " + ex.Message;
            }
        }
        int promote = Array.FindIndex(args, a => string.Equals(a.Trim(), MakeSuperAdmin, StringComparison.OrdinalIgnoreCase));
        if (promote >= 0)
        {
            string name = promote + 1 < args.Length ? args[promote + 1] : "";
            try
            {
                var account = store.PromoteToSuperAdmin(name);
                return $"'{account.Username}' is now a Super Admin. The password was not changed.";
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException)
            {
                AppLogger.Error("CommandLine", ex);
                return "Could not make a Super Admin: " + ex.Message;
            }
        }
        if (!args.Any(a => string.Equals(a.Trim(), ResetSimulation, StringComparison.OrdinalIgnoreCase))) return null;
        try
        {
            store.SeedDefaultFlights();   // a fresh database has no flights yet (the app normally seeds them on first use)
            var simulation = new SimulationService(store);
            simulation.MigrateIfNeeded();   // an older database: settings brought up to date, so the reset is not undone later
            int generated = simulation.Reset(now);
            // A neutral name: the staff Audit tab shows it.
            store.WriteAudit("command-line", "system", "MAINTENANCE_RESET", detail: $"{generated} flight dates regenerated");
            return $"Simulated bookings regenerated for {generated} flight dates.";
        }
        catch (IOException ex)   // e.g. the database is locked or unreadable
        {
            AppLogger.Error("CommandLine", ex);
            return "The reset could not be completed: " + ex.Message;
        }
    }
}
