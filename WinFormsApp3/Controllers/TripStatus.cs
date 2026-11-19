namespace SkyWings.Controllers;

/// <summary>Where a booking's trip is right now.</summary>
public enum TripPhase { Upcoming, CheckInOpen, CheckedIn, Boarding, InFlight, Completed, NoShow, Canceled }

/// <summary>
/// A booking's trip status. <see cref="ReturnLeg"/> is true while the return flight is the one that decides it;
/// <see cref="LandedAt"/> is set once the trip is completed; <see cref="Destination"/> is where the deciding leg goes.
/// </summary>
public sealed record TripStatusInfo(TripPhase Phase, string Label, bool ReturnLeg, DateTime? LandedAt, string Destination);

/// <summary>
/// Works out a booking's trip status from the clock: the flight timeline (<see cref="FlightStatusEngine"/>, so delays and durations
/// match the departures board) plus who has checked in. Nothing is stored; the answer is always current. No WinForms dependency.
/// </summary>
public static class TripStatus
{
    /// <summary>The choices of the staff Bookings filter.</summary>
    public static readonly string[] Filters = { "All", "Upcoming", "In progress", "Completed", "No-show", "Canceled" };

    public static TripStatusInfo Of(BookingRecord booking, IReadOnlyCollection<CheckInRecord> checkIns, DateTime now)
    {
        if (booking.Status == AppData.StatusCanceled)
            return new TripStatusInfo(TripPhase.Canceled, "Canceled", false, null, booking.Destination);

        TripStatusInfo outbound = Leg(booking.FlightNo, booking.FlightDateValue, booking.Destination, false, checkIns, now);
        if (outbound.Phase != TripPhase.Completed || booking.ReturnFlightNo is null || booking.ReturnFlightDateValue is not { } back)
            return outbound;   // a no-show on the way out is final; otherwise the outbound decides until it lands
        return Leg(booking.ReturnFlightNo, back, booking.Origin, true, checkIns, now);
    }

    public static bool Matches(TripPhase phase, string filter) => filter switch
    {
        "Upcoming" => phase == TripPhase.Upcoming,
        "In progress" => phase is TripPhase.CheckInOpen or TripPhase.CheckedIn or TripPhase.Boarding or TripPhase.InFlight,
        "Completed" => phase == TripPhase.Completed,
        "No-show" => phase == TripPhase.NoShow,
        "Canceled" => phase == TripPhase.Canceled,
        _ => true
    };

    private static TripStatusInfo Leg(string flightNo, DateTime date, string destination, bool returnLeg,
        IReadOnlyCollection<CheckInRecord> checkIns, DateTime now)
    {
        FlightStatusInfo flight = FlightStatusEngine.Evaluate(flightNo, date, now);
        bool anyCheckedIn = checkIns.Any(c => c.FlightNo == flightNo && c.FlightDate.Date == date.Date);
        // Online check-in runs from 24 h before the *scheduled* departure until the scheduled departure (BookingStore.IsCheckInOpen),
        // even when the flight is delayed; the timeline itself follows the estimated (delayed) times.
        DateTime scheduled = AppData.DepartureOf(flightNo, date);
        TripPhase phase = flight.Kind switch
        {
            FlightStatusKind.Scheduled or FlightStatusKind.CheckInOpen or FlightStatusKind.Delayed =>
                anyCheckedIn ? TripPhase.CheckedIn
                : now < scheduled.AddHours(-AppData.CheckInWindowHours) ? TripPhase.Upcoming
                : now < scheduled ? TripPhase.CheckInOpen
                : TripPhase.Boarding,   // check-in has closed; the delayed flight is still on the ground
            FlightStatusKind.Boarding or FlightStatusKind.GateClosing => TripPhase.Boarding,
            FlightStatusKind.Departed or FlightStatusKind.InFlight => anyCheckedIn ? TripPhase.InFlight : TripPhase.NoShow,
            _ => anyCheckedIn ? TripPhase.Completed : TripPhase.NoShow
        };
        bool final = phase is TripPhase.Completed or TripPhase.NoShow;
        string label = Label(phase);
        if (returnLeg && !final) label = "Return · " + label;
        return new TripStatusInfo(phase, label, returnLeg && phase != TripPhase.Completed,
            phase == TripPhase.Completed ? flight.EstimatedArrival : null, destination);
    }

    private static string Label(TripPhase phase) => phase switch
    {
        TripPhase.Upcoming => "Upcoming",
        TripPhase.CheckInOpen => "Check-in open",
        TripPhase.CheckedIn => "Checked in",
        TripPhase.Boarding => "Boarding",
        TripPhase.InFlight => "In flight",
        TripPhase.Completed => "Completed",
        TripPhase.NoShow => "No-show",
        _ => "Canceled"
    };
}
