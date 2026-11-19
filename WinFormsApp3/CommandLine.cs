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
        int generated = new SimulationService(store).Reset(now);
        store.WriteAudit("command-line", "system", "SIMULATION_RESET", detail: $"{generated} flight dates regenerated");
        return $"Simulated bookings regenerated for {generated} flight dates.";
    }
}
