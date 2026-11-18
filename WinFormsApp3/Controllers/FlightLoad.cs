namespace SkyWings.Controllers;

/// <summary>One cabin of a flight on a date: its seats and how many are left.</summary>
public sealed record CabinLoad(string ClassName, int Total, int Left)
{
    public int Booked => Math.Max(0, Total - Left);
    public double Fraction => Total <= 0 ? 0 : Math.Clamp(Booked / (double)Total, 0, 1);
    public int Percent => (int)Math.Round(Fraction * 100);
}

/// <summary>How full flights are: occupancy per cabin and a text bar for the flight details. No WinForms dependency.</summary>
public static class FlightLoad
{
    /// <summary>A bar of <paramref name="cells"/> blocks, filled in proportion to <paramref name="fraction"/> (clamped to 0..1).</summary>
    public static string Bar(double fraction, int cells = 10)
    {
        int filled = (int)Math.Round(Math.Clamp(fraction, 0, 1) * cells);
        return new string('█', filled) + new string('░', cells - filled);
    }

    /// <summary>The cabins of a flight's aircraft with the seats left on a date, in the aircraft's cabin order.</summary>
    public static IReadOnlyList<CabinLoad> Cabins(BookingStore store, string flightNo, DateTime date)
        => store.LoadAircraft(flightNo).Cabins
            .Select(c => new CabinLoad(c.ClassName, c.Seats, store.CabinSeatsLeft(flightNo, date, c.ClassName)))
            .ToList();


    /// <summary>
    /// The text of the flight-details panel: route, times, the aircraft, then one row per cabin the aircraft has with its fare and how
    /// full it is on <paramref name="day"/>.
    /// </summary>
    public static IReadOnlyList<string> Details(BookingStore store, Flight flight, string routeType, decimal baseFare,
        DateTime departure, DateTime arrival, DateTime day)
    {
        var lines = new List<string>
        {
            $"{flight.FlightNo}   {flight.Origin} → {flight.Destination}   ({routeType})",
            $"Departs {departure:h:mm tt}  ·  arrives {arrival:h:mm tt}{(arrival.Date > departure.Date ? " (+1 day)" : "")}",
            "Aircraft: " + store.LoadAircraft(flight.FlightNo).Summary,
            "",
            $"{"Cabin",-16} {"Fare (PHP)",11}   Load on {day:d MMM}"
        };
        foreach (CabinLoad cabin in Cabins(store, flight.FlightNo, day))
        {
            int index = Math.Max(0, Array.IndexOf(AppData.ClassNames, cabin.ClassName));
            decimal fare = AppData.Round2(baseFare * AppData.ClassMultipliers[index]);
            lines.Add($"{cabin.ClassName,-16} {fare,11:N2}   {Occupancy(cabin)}");
        }
        return lines;
    }
    /// <summary>"████████░░  78%  37 left" (or "sold out"): the bar, the percentage and the seats left.</summary>
    public static string Occupancy(CabinLoad cabin)
        => $"{Bar(cabin.Fraction)} {cabin.Percent,3}%  {(cabin.Left == 0 ? "sold out" : cabin.Left + " left")}";

    /// <summary>"Economy          ████████░░  78%  37 left" (or "sold out").</summary>
    public static string Line(CabinLoad cabin)
        => $"{cabin.ClassName,-16} {Occupancy(cabin)}";
}
