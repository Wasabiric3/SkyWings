namespace SkyWings.Tests;

/// <summary>Simulator settings shared by tests.</summary>
internal static class TestSim
{
    /// <summary>No daily booking budget: every flight gets its load, as tests about one flight's load expect.</summary>
    public static readonly SimulationOptions FullLoad = new(DailyBookingBudget: int.MaxValue);
}
