using System.Globalization;

namespace SkyWings.UI;

/// <summary>How the staff screens word a moment in time, so "when" reads the same everywhere and recent things stand out.</summary>
internal static class RecencyFormat
{
    private static string Clock(DateTime t) => t.ToString("h:mm tt", CultureInfo.InvariantCulture);

    /// <summary>
    /// "just now" / "12 min ago" for the last hour, then "Today, 9:07 AM", "Yesterday, 4:45 PM", "Nov 20, 9:07 AM" (with the year when
    /// it is not this year). A time slightly in the future (clocks differ) reads "just now".
    /// </summary>
    public static string Relative(DateTime when, DateTime now)
    {
        TimeSpan age = now - when;
        if (age < TimeSpan.FromMinutes(1)) return "just now";
        if (age < TimeSpan.FromHours(1)) return $"{(int)age.TotalMinutes} min ago";
        return Stamp(when, now);
    }

    /// <summary>The time itself, never "n min ago": "Today, 9:07 AM", "Yesterday, 4:45 PM", "Nov 20, 9:07 AM", "Dec 31, 2025, 11:59 PM".</summary>
    public static string Stamp(DateTime when, DateTime now)
    {
        if (when.Date == now.Date) return "Today, " + Clock(when);
        if (when.Date == now.Date.AddDays(-1)) return "Yesterday, " + Clock(when);
        string day = when.Year == now.Year
            ? when.ToString("MMM d", CultureInfo.InvariantCulture)
            : when.ToString("MMM d, yyyy", CultureInfo.InvariantCulture);
        return $"{day}, {Clock(when)}";
    }

    /// <summary>True when <paramref name="when"/> is after <paramref name="baseline"/> (the time of the previous look at the same list); never without one.</summary>
    public static bool IsNew(DateTime when, DateTime? baseline) => baseline is { } b && when > b;
}
