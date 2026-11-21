namespace SkyWings;

/// <summary>What changing a booking's outbound flight costs. Only a higher fare is charged; a cheaper flight is not refunded.</summary>
public sealed record ChangeQuote(int Passengers, decimal FareIncrease, decimal Tax, decimal Fee)
{
    public decimal Total => FareIncrease + Tax + Fee;

    public string Summary => FareIncrease > 0
        ? $"Fare difference PHP {FareIncrease:N2} + tax PHP {Tax:N2} + change fee PHP {Fee:N2} = PHP {Total:N2}"
        : $"Change fee PHP {Fee:N2} (no fare increase)";
}

/// <summary>One flight the booking could move to, with what it would cost and whether it is possible.</summary>
public sealed record FlightChangeOption(Flight Flight, DateTime Departure, int SeatsLeft, ChangeQuote Quote, string? Problem)
{
    public bool Possible => Problem is null;
}

public enum FlightChangeOutcome { Changed, Refused, StoreError }

public sealed record FlightChangeResult(FlightChangeOutcome Outcome, string Message, IReadOnlyList<string> NewSeats)
{
    public static FlightChangeResult Refused(string message) => new(FlightChangeOutcome.Refused, message, Array.Empty<string>());
}

/// <summary>Pure rules for changing the outbound flight of a confirmed booking (same route and cabin, any date).</summary>
public static class FlightChangeRules
{
    /// <summary>Charged per traveller on every change, on top of any fare increase. Not taxed.</summary>
    public const decimal FeePerPassenger = 500m;

    /// <summary>
    /// Base fare for the cabin on that date (peak-season surcharge included), per traveller. Add-ons, promo codes and
    /// discounts the booking was made with are not recalculated: only the difference between the two flights is charged.
    /// </summary>
    public static decimal ListFare(Flight flight, string className, DateTime date)
    {
        decimal fare = flight.BasePrice * AppData.ClassMultipliers[SeatLayout.ClassIndex(className)];
        return FarePricing.IsPeakSeason(date) ? fare * (1m + AppData.PeakSeasonRate) : fare;
    }

    public static ChangeQuote Quote(BookingRecord booking, Flight oldFlight, Flight newFlight, DateTime newDate)
    {
        int pax = Math.Max(1, booking.Passengers.Count);
        decimal before = ListFare(oldFlight, booking.ClassName, booking.FlightDateValue);
        decimal after = ListFare(newFlight, booking.ClassName, newDate);
        decimal increase = AppData.Round2(Math.Max(0m, after - before) * pax);
        bool taxExempt = booking.Passengers.Count > 0 && booking.Passengers.All(p => p.Senior || p.Pwd);
        decimal tax = taxExempt ? 0m : AppData.Round2(increase * AppData.TaxRate);
        return new ChangeQuote(pax, increase, tax, FeePerPassenger * pax);
    }

    /// <summary>Why this booking cannot be moved to another flight, or null when it can. <paramref name="outboundCheckedIn"/>: someone has a boarding pass.</summary>
    public static string? BookingRefusal(BookingRecord booking, bool outboundCheckedIn, DateTime now)
    {
        if (booking.Status != AppData.StatusConfirmed) return "Only a confirmed booking can be moved to another flight.";
        if (AppData.DepartureOf(booking.FlightNo, booking.FlightDateValue) <= now) return "This flight has already left.";
        if (outboundCheckedIn) return "A traveller has already checked in, so the flight can no longer be changed.";
        return null;
    }

    /// <summary>Why the booking cannot take this flight on this date, or null when it can (seats are checked separately).</summary>
    public static string? TargetRefusal(BookingRecord booking, Flight target, DateTime date, DateTime now)
    {
        if (target.Origin != booking.Origin || target.Destination != booking.Destination)
            return "Not the same route.";
        if (target.FlightNo == booking.FlightNo && date.Date == booking.FlightDateValue.Date)
            return "This is the flight already booked.";
        if (!AppData.IsBookable(target.FlightNo, date, now))
            return "Booking for this flight has closed.";
        if (booking.ReturnFlightNo is not null && booking.ReturnFlightDateValue is { } back)
        {
            DateTime departure = AppData.DepartureOf(target.FlightNo, date);
            if (departure >= AppData.DepartureOf(booking.ReturnFlightNo, back))
                return "The outbound flight must leave before the return flight.";
        }
        return null;
    }
}
