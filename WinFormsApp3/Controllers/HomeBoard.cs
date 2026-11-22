namespace SkyWings.Controllers;

/// <summary>The regions of the network shown as filter chips and cards on the home screen.</summary>
public static class FlightRegions
{
    public static readonly IReadOnlyList<string> Names = new[] { "Domestic", "Southeast Asia", "Europe", "North America" };

    /// <summary>The region of a flight from its number: PR-1xx and the returns PR-5xx are Domestic, 2xx/6xx Southeast Asia, 3xx/7xx Europe, 4xx/8xx North America; anything else is "Other".</summary>
    public static string Of(Flight flight)
    {
        string digits = new(flight.FlightNo.SkipWhile(c => !char.IsDigit(c)).TakeWhile(char.IsDigit).ToArray());
        if (!flight.FlightNo.StartsWith("PR-", StringComparison.OrdinalIgnoreCase) || !int.TryParse(digits, out int number)) return "Other";
        return (number / 100) switch
        {
            1 or 5 => "Domestic",
            2 or 6 => "Southeast Asia",
            3 or 7 => "Europe",
            4 or 8 => "North America",
            _ => "Other"
        };
    }
}
