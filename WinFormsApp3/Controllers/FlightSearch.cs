namespace SkyWings.Controllers;

/// <summary>One line of a flight search: a flight with its times, the fare for the chosen cabin and the seats left that day.</summary>
public sealed record FlightRow(string FlightNo, string Route, string Departs, string Arrives, string Duration,
    string Price, string SeatsLeft, Flight Flight, int Seats, FlightStatusInfo Status)
{
    public bool SoldOut => Seats == 0;

    // Text for the departures board columns.
    public string ScheduledText => Status.Scheduled.ToString("h:mm tt");
    public string EstimatedText => Status.Estimated.ToString("h:mm tt");
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
            DateTime departure = AppData.DepartureOf(f.FlightNo, date);
            int minutes = AppData.FlightEstDurationMin.GetValueOrDefault(f.FlightNo, 120);
            DateTime arrival = departure.AddMinutes(minutes);
            int seats = store.CabinSeatsLeft(f.FlightNo, date, className);
            rows.Add(new FlightRow(f.FlightNo, $"{f.Origin} → {f.Destination}", departure.ToString("h:mm tt"),
                arrival.ToString("h:mm tt") + (arrival.Date > departure.Date ? " +1" : ""), $"{minutes / 60}h {minutes % 60:00}m",
                $"PHP {AppData.Round2(f.BasePrice * multiplier):N0}", seats == 0 ? "Sold out" : seats.ToString("N0"), f, seats, status));
        }
        return rows;
    }
}
