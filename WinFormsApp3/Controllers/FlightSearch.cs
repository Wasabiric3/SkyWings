namespace SkyWings.Controllers;

/// <summary>One line of a flight search: a flight with its times, the fare for the chosen cabin and the seats left that day.</summary>
public sealed record FlightRow(string FlightNo, string Route, string Departs, string Arrives, string Duration,
    string Price, string SeatsLeft, Flight Flight, int Seats, FlightStatusInfo Status)
{
    public bool SoldOut => Seats == 0;

    /// <summary>Under this many seats left, the board shows the count in amber.</summary>
    public const int FewSeatsThreshold = 20;

    /// <summary>Some seats are left, but not many.</summary>
    public bool FewSeatsLeft => Seats is > 0 and < FewSeatsThreshold;

    /// <summary>How full the searched cabin is on that date, e.g. "78% full". Shown as a column on the departures board.</summary>
    public string LoadText { get; init; } = "";

    /// <summary>The same load as a number, 0–100, for the board's load bar.</summary>
    public int LoadPercent { get; init; }

    // Text for the departures board columns.
    public string DestinationText => Flight.Destination;
    public string ScheduledText => Status.Scheduled.ToString("h:mm tt");
    public string EstimatedText => Status.Estimated.ToString("h:mm tt");

    /// <summary>The scheduled departure, with the estimate after an arrow when the flight is late ("6:00 AM → 6:25 AM").</summary>
    public string DepartsText => Status.DelayMinutes > 0 ? $"{ScheduledText} → {EstimatedText}" : ScheduledText;
    public string GateText => Status.Gate.Length > 0 ? Status.Gate : "—";
    public string StatusText => Status.Label;
    public string CountdownText => Status.Countdown;
}

/// <summary>
/// Finds the flights leaving Manila for a destination (or every destination), date and cabin.
/// Shared by the home screen's live list and the results window so both always agree.
/// </summary>
public static class FlightSearch
{
    /// <param name="destination">A destination such as "Cebu (CEB)", or null/empty for every flight from Manila.</param>
    /// <exception cref="IOException">The database could not be read.</exception>
    /// <param name="now">The moment the live status is worked out for; defaults to the current time.</param>
    public static List<FlightRow> Search(BookingStore store, string? destination, DateTime date, string className,
        DateTime? now = null)
    {
        DateTime clock = now ?? DateTime.Now;
        bool anywhere = string.IsNullOrEmpty(destination);
        decimal multiplier = AppData.ClassMultipliers[SeatLayout.ClassIndex(className)];
        // A departures board: soonest estimated departure first, for one destination and for all of them.
        var flights = store.LoadActiveFlights(true).Concat(store.LoadActiveFlights(false))
            .Where(f => f.Origin.StartsWith("Manila", StringComparison.Ordinal) && (anywhere || f.Destination == destination))
            .Select(f => (Flight: f, Status: FlightStatusEngine.Evaluate(f.FlightNo, date, clock)))
            .OrderBy(x => x.Status.Estimated).ThenBy(x => x.Flight.FlightNo);

        var rows = new List<FlightRow>();
        foreach (var (f, status) in flights)
        {
            // A flight whose aircraft has no such cabin (First Class on an A320) is not "sold out": it simply does not offer it.
            if (store.LoadAircraft(f.FlightNo).Cabin(className) is not { } cabin) continue;
            DateTime departure = AppData.DepartureOf(f.FlightNo, date);
            int minutes = AppData.FlightEstDurationMin.GetValueOrDefault(f.FlightNo, 120);
            DateTime arrival = departure.AddMinutes(minutes);
            int seats = store.CabinSeatsLeft(f.FlightNo, date, className);
            int cabinSeats = cabin.Seats;
            int percent = (int)Math.Round(100.0 * Math.Max(0, cabinSeats - seats) / cabinSeats);
            rows.Add(new FlightRow(f.FlightNo, $"{f.Origin} → {f.Destination}", departure.ToString("h:mm tt"),
                arrival.ToString("h:mm tt") + (arrival.Date > departure.Date ? " +1" : ""), $"{minutes / 60}h {minutes % 60:00}m",
                $"PHP {AppData.Round2(f.BasePrice * multiplier):N0}", seats == 0 ? "Sold out" : seats.ToString("N0"), f, seats, status)
            { LoadText = $"{percent}% full", LoadPercent = percent });
        }
        return rows;
    }

    /// <summary>
    /// The home board: flights leaving Manila on <paramref name="day"/> in <paramref name="region"/> ("" for every region) whose origin, destination or
    /// flight number contains <paramref name="text"/>. Each row shows the Economy fare ("from") and the seats left across all of the aircraft's cabins.
    /// </summary>
    /// <exception cref="IOException">The database could not be read.</exception>
    public static List<FlightRow> Board(BookingStore store, string region, string text, DateTime day, DateTime? now = null)
    {
        DateTime clock = now ?? DateTime.Now;
        string needle = (text ?? "").Trim();
        var flights = store.LoadActiveFlights(true).Concat(store.LoadActiveFlights(false))
            .Where(f => f.Origin.StartsWith("Manila", StringComparison.Ordinal)
                && (string.IsNullOrEmpty(region) || FlightRegions.Of(f) == region)
                && (needle.Length == 0
                    || f.Origin.Contains(needle, StringComparison.CurrentCultureIgnoreCase)
                    || f.Destination.Contains(needle, StringComparison.CurrentCultureIgnoreCase)
                    || f.FlightNo.Contains(needle, StringComparison.CurrentCultureIgnoreCase)))
            .Select(f => (Flight: f, Status: FlightStatusEngine.Evaluate(f.FlightNo, day, clock)))
            .OrderBy(x => x.Status.Estimated).ThenBy(x => x.Flight.FlightNo);

        var rows = new List<FlightRow>();
        foreach (var (f, status) in flights)
        {
            Aircraft aircraft = store.LoadAircraft(f.FlightNo);
            if (aircraft.Cabins.Count == 0) continue;
            CabinLayout fareCabin = aircraft.Cabin("Economy") ?? aircraft.Cabins[^1];   // the cheapest cabin the aircraft has
            decimal fare = AppData.Round2(f.BasePrice * AppData.ClassMultipliers[SeatLayout.ClassIndex(fareCabin.ClassName)]);
            int seats = aircraft.Cabins.Sum(c => store.CabinSeatsLeft(f.FlightNo, day, c.ClassName));
            int capacity = aircraft.Cabins.Sum(c => c.Seats);
            int percent = capacity == 0 ? 0 : (int)Math.Round(100.0 * Math.Max(0, capacity - seats) / capacity);
            DateTime departure = AppData.DepartureOf(f.FlightNo, day);
            int minutes = AppData.FlightEstDurationMin.GetValueOrDefault(f.FlightNo, 120);
            DateTime arrival = departure.AddMinutes(minutes);
            rows.Add(new FlightRow(f.FlightNo, $"{f.Origin} â {f.Destination}", departure.ToString("h:mm tt"),
                arrival.ToString("h:mm tt") + (arrival.Date > departure.Date ? " +1" : ""), $"{minutes / 60}h {minutes % 60:00}m",
                $"PHP {fare:N0}", seats == 0 ? "Sold out" : seats.ToString("N0"), f, seats, status)
            { LoadText = $"{percent}% full", LoadPercent = percent });
        }
        return rows;
    }
}
