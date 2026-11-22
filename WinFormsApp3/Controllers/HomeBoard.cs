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

public enum StageState { Done, Current, Upcoming }

/// <summary>One step of a flight's timeline: its name, when it happens (the estimated time, with the date when it is not the flight's day) and whether it is past, now or ahead.</summary>
public sealed record TimelineStage(string Label, string Time, StageState State);

/// <summary>One cabin of the flight's aircraft: the fare per traveller (base fare times the cabin's multiplier), the seats left that day and how full it is.</summary>
public sealed record CabinOption(string ClassName, decimal Fare, int SeatsLeft, int Seats, int LoadPercent)
{
    public bool SoldOut => SeatsLeft == 0;
}

/// <summary>Everything the home screen's details panel shows about the selected flight.</summary>
/// <param name="Aircraft">The aircraft model, such as "Airbus A320".</param>
/// <param name="Duration">For example "about 1h 30m".</param>
/// <param name="DelayText">"Delayed 20 min", or empty when the flight is on time.</param>
/// <param name="DefaultCabin">Index into <paramref name="Cabins"/> to choose first: the first cabin with seats, else Economy.</param>
public sealed record FlightDetails(Flight Flight, FlightStatusInfo Status, string Aircraft, string Duration, string DelayText,
    IReadOnlyList<TimelineStage> Timeline, IReadOnlyList<CabinOption> Cabins, int DefaultCabin);

public sealed partial class HomeController
{
    /// <summary>The details of one flight on a day, worked out for <paramref name="now"/> (the current time by default).</summary>
    /// <exception cref="IOException">The database could not be read.</exception>
    public FlightDetails Describe(Flight flight, DateTime day, DateTime? now = null)
    {
        DateTime clock = now ?? DateTime.Now;
        FlightStatusInfo status = FlightStatusEngine.Evaluate(flight.FlightNo, day, clock);
        Aircraft aircraft = _store.LoadAircraft(flight.FlightNo);

        string At(DateTime time) => time.Date == day.Date ? time.ToString("h:mm tt") : time.ToString("MMM d, h:mm tt");
        int current = status.Kind switch
        {
            FlightStatusKind.Scheduled => 0,
            FlightStatusKind.CheckInOpen or FlightStatusKind.Delayed => 1,
            FlightStatusKind.Boarding or FlightStatusKind.GateClosing => 2,
            FlightStatusKind.Departed or FlightStatusKind.InFlight => 3,
            _ => 4
        };
        var steps = new (string Label, DateTime Time)[]
        {
            ("Scheduled", status.Scheduled),
            ("Check-in open", status.Estimated.AddHours(-AppData.CheckInWindowHours)),
            ("Boarding", status.Estimated.AddMinutes(-FlightStatusEngine.BoardingMinutes)),
            ("Departed", status.Estimated),
            ("Landed", status.EstimatedArrival)
        };
        var timeline = steps.Select((s, i) => new TimelineStage(s.Label, At(s.Time), i < current ? StageState.Done : i == current ? StageState.Current : StageState.Upcoming)).ToList();

        var cabins = aircraft.Cabins
            .OrderBy(c => SeatLayout.ClassIndex(c.ClassName))
            .Select(c =>
            {
                int left = _store.CabinSeatsLeft(flight.FlightNo, day, c.ClassName);
                int percent = c.Seats == 0 ? 0 : (int)Math.Round(100.0 * Math.Max(0, c.Seats - left) / c.Seats);
                decimal fare = AppData.Round2(flight.BasePrice * AppData.ClassMultipliers[SeatLayout.ClassIndex(c.ClassName)]);
                return new CabinOption(c.ClassName, fare, left, c.Seats, percent);
            }).ToList();
        int firstWithSeats = cabins.FindIndex(c => c.SeatsLeft > 0);
        int economy = Math.Max(0, cabins.FindIndex(c => c.ClassName == "Economy"));

        int minutes = AppData.FlightEstDurationMin.GetValueOrDefault(flight.FlightNo, 0);
        string duration = minutes > 0 ? $"about {minutes / 60}h {minutes % 60:00}m" : "";
        string delay = status.DelayMinutes > 0 ? $"Delayed {status.DelayMinutes} min" : "";
        return new FlightDetails(flight, status, aircraft.Model, duration, delay, timeline, cabins, firstWithSeats >= 0 ? firstWithSeats : economy);
    }
}

public enum BookAction { Book, JoinWaitlist, None }

/// <summary>What the details panel's one button does: its kind, its label, and (when it is off) why.</summary>
public sealed record BookingAction(BookAction Kind, string Label, string Reason);

public sealed partial class HomeController
{
    /// <summary>
    /// The one booking button for a flight and a chosen cabin: "Book PR-111 · Economy →", "Join waitlist →" when the cabin is sold out, or off
    /// with the reason when there is no cabin chosen, the flight has left, or bookings have closed.
    /// </summary>
    public BookingAction ActionFor(Flight flight, DateTime day, CabinOption? cabin, DateTime? now = null)
    {
        DateTime clock = now ?? DateTime.Now;
        if (cabin is null) return new BookingAction(BookAction.None, "Book →", "Choose a cabin.");
        string label = $"Book {flight.FlightNo} · {cabin.ClassName} →";
        if (!CanBook(flight.FlightNo, day, clock))
        {
            bool left = clock >= FlightStatusEngine.Evaluate(flight.FlightNo, day, clock).Estimated;
            return new BookingAction(BookAction.None, label,
                left ? "This flight has already left." : $"Bookings close {AppData.BookingCutoffHours} hours before departure.");
        }
        return cabin.SoldOut ? new BookingAction(BookAction.JoinWaitlist, "Join waitlist →", "") : new BookingAction(BookAction.Book, label, "");
    }
}
