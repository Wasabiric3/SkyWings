namespace SkyWings;

public enum CheckInOutcome { CheckedIn, AlreadyCheckedIn, NotOpen, NoSeat, NotFound }

public sealed partial class BookingStore
{
    /// <summary>
    /// Checks one traveller in for the outbound or return leg. The flight, date and seat come from the booking itself,
    /// so a caller cannot check in someone who is not on it or claim a seat they were not assigned.
    /// </summary>
    public CheckInOutcome CheckIn(string reference, string passengerName, bool returnLeg, DateTime now)
    {
        BookingRecord? booking = FindByReference(reference);
        PassengerRecord? passenger = booking?.Passengers.FirstOrDefault(p => p.Name == passengerName);
        if (booking is null || passenger is null) return CheckInOutcome.NotFound;

        string flightNo = booking.FlightNo;
        DateTime date = booking.FlightDateValue;
        string seat = passenger.SeatCode;
        if (returnLeg)
        {
            if (booking.ReturnFlightNo is null || booking.ReturnFlightDateValue is null) return CheckInOutcome.NotFound;
            flightNo = booking.ReturnFlightNo;
            date = booking.ReturnFlightDateValue.Value;
            seat = passenger.ReturnSeatCode;
        }
        if (seat.Length == 0) return CheckInOutcome.NoSeat;
        if (!IsCheckInOpen(reference, flightNo, date, now)) return CheckInOutcome.NotOpen;
        return CheckInPassenger(reference, passengerName, flightNo, date, seat, now)
            ? CheckInOutcome.CheckedIn
            : CheckInOutcome.AlreadyCheckedIn;
    }

    /// <summary>True when this traveller has already checked in for the given flight and date.</summary>
    public bool IsCheckedIn(string reference, string passengerName, string flightNo, DateTime flightDate)
        => LoadCheckIns(reference).Any(c => c.PassengerName == passengerName && c.FlightNo == flightNo &&
                                            c.FlightDate.Date == flightDate.Date);
}
