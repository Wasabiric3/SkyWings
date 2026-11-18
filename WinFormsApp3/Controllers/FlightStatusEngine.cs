namespace SkyWings.Controllers;

public enum FlightStatusKind { Scheduled, CheckInOpen, Delayed, Boarding, GateClosing, Departed, InFlight, Landed }

/// <summary>Where a flight is at a given moment, as shown on the departures board.</summary>
/// <param name="Gate">Empty until check-in opens (a flight far in the future has no gate yet).</param>
/// <param name="Countdown">A short phrase such as "boards in 1h 20m" or "lands in 45m".</param>
public sealed record FlightStatusInfo(
    FlightStatusKind Kind, string Label, DateTime Scheduled, DateTime Estimated, DateTime EstimatedArrival,
    int DelayMinutes, string Gate, string Countdown);

/// <summary>
/// Works out a flight's live status from the clock alone. There is no real tracking feed, so this is a simulation:
/// the schedule, an on-time or slightly late estimate and a gate are derived from the flight number and date, which
/// makes the answer the same every time for the same flight, date and moment.
/// </summary>
public static class FlightStatusEngine
{
    public const int BoardingMinutes = 40;      // boarding opens this long before departure
    public const int GateClosingMinutes = 10;   // the gate closes this long before departure
    public const int DepartedMinutes = 10;      // "Departed" is shown this long after push-back, then "In flight"
    public const int MaxDelayMinutes = 75;

    public static FlightStatusInfo Evaluate(string flightNo, DateTime flightDate, DateTime now)
    {
        DateTime scheduled = AppData.DepartureOf(flightNo, flightDate);
        var (delay, gate) = DelayAndGate(flightNo, flightDate);
        DateTime estimated = scheduled.AddMinutes(delay);
        int duration = AppData.FlightEstDurationMin.GetValueOrDefault(flightNo, 120);
        DateTime arrival = estimated.AddMinutes(duration);

        double toDeparture = (estimated - now).TotalMinutes;   // positive before departure
        FlightStatusKind kind;
        string countdown;
        if (now >= arrival)
        {
            kind = FlightStatusKind.Landed;
            countdown = $"landed {Span(now - arrival)} ago";
        }
        else if (toDeparture <= -DepartedMinutes)
        {
            kind = FlightStatusKind.InFlight;
            countdown = $"lands in {Span(arrival - now)}";
        }
        else if (toDeparture <= 0)
        {
            kind = FlightStatusKind.Departed;
            countdown = $"departed {Span(now - estimated)} ago";
        }
        else if (toDeparture <= GateClosingMinutes)
        {
            kind = FlightStatusKind.GateClosing;
            countdown = $"departs in {Span(estimated - now)}";
        }
        else if (toDeparture <= BoardingMinutes)
        {
            kind = FlightStatusKind.Boarding;
            countdown = $"departs in {Span(estimated - now)}";
        }
        else if (toDeparture <= AppData.CheckInWindowHours * 60)
        {
            // A delay is announced once check-in is open; before that the flight simply shows as scheduled.
            kind = delay > 0 ? FlightStatusKind.Delayed : FlightStatusKind.CheckInOpen;
            countdown = $"boards in {Span(estimated - now - TimeSpan.FromMinutes(BoardingMinutes))}";
        }
        else
        {
            kind = FlightStatusKind.Scheduled;
            countdown = $"departs in {Span(estimated - now)}";
        }

        string label = kind switch
        {
            FlightStatusKind.Scheduled => "Scheduled",
            FlightStatusKind.CheckInOpen => "Check-in open",
            FlightStatusKind.Delayed => $"Delayed +{delay}m",
            FlightStatusKind.Boarding => "Boarding",
            FlightStatusKind.GateClosing => "Gate closing",
            FlightStatusKind.Departed => "Departed",
            FlightStatusKind.InFlight => "In flight",
            _ => "Landed"
        };
        string shownGate = kind == FlightStatusKind.Scheduled ? "" : gate;
        return new FlightStatusInfo(kind, label, scheduled, estimated, arrival, delay, shownGate, countdown);
    }

    /// <summary>About 70% of flights are on time; the rest are 10 to 75 minutes late. Gates run from A1 to D24.</summary>
    private static (int Delay, string Gate) DelayAndGate(string flightNo, DateTime flightDate)
    {
        var rnd = new JavaRandom(StableSeed($"{flightNo}|{flightDate:yyyyMMdd}"));
        int delay = rnd.NextInt(100) < 70 ? 0 : 10 + 5 * rnd.NextInt((MaxDelayMinutes - 10) / 5 + 1);
        string gate = $"{(char)('A' + rnd.NextInt(4))}{1 + rnd.NextInt(24)}";
        return (delay, gate);
    }

    /// <summary>FNV-1a hash. string.GetHashCode changes from one run to the next, which would reshuffle delays on every launch.</summary>
    private static long StableSeed(string text)
    {
        unchecked
        {
            ulong hash = 14695981039346656037UL;
            foreach (char c in text)
            {
                hash ^= c;
                hash *= 1099511628211UL;
            }
            return (long)hash;
        }
    }

    /// <summary>Whole minutes, rounded up so a countdown never shows "0m" while there is still time left.</summary>
    private static string Span(TimeSpan span)
    {
        int minutes = Math.Max(1, (int)Math.Ceiling(span.TotalMinutes));
        if (minutes >= 24 * 60) return $"{minutes / (24 * 60)}d {minutes % (24 * 60) / 60}h";
        if (minutes >= 60) return $"{minutes / 60}h {minutes % 60:00}m";
        return $"{minutes}m";
    }
}
