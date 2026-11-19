namespace SkyWings;

/// <summary>
/// Maintenance switches given when starting SkyWings from a terminal. Not shown anywhere in the app:
/// <c>SkyWings.exe --reset-simulation</c> deletes the simulated traffic and generates it again (real bookings are never touched).
/// </summary>
internal static class CommandLine
{
    public const string ResetSimulation = "--reset-simulation";

    /// <summary>Runs the switch in <paramref name="args"/> and returns what to tell the user, or null when there is none (start normally).</summary>
    public static string? Run(string[] args, BookingStore store, DateTime now)
    {
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
