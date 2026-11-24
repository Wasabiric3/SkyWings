namespace SkyWings;

/// <summary>Everything one printed boarding pass shows. Drawn by <c>BoardingPassPrinter</c>.</summary>
public sealed record BoardingPassData(
    string Reference, string PassengerName,
    string OriginCode, string OriginCity, string DestinationCode, string DestinationCity,
    string FlightNo, DateTime Departure, string ClassName, string Seat, DateTime CheckedInAt)
{
    /// <summary>The pass for one checked-in traveller on the outbound or the return leg of a booking.</summary>
    public static BoardingPassData From(BookingRecord booking, bool returnLeg, CheckInRecord record)
    {
        var (originCode, originCity) = ReceiptData.SplitPlace(returnLeg ? booking.ReturnOrigin ?? "" : booking.Origin);
        var (destinationCode, destinationCity) = ReceiptData.SplitPlace(returnLeg ? booking.ReturnDestination ?? "" : booking.Destination);
        return new BoardingPassData(
            booking.Reference, record.PassengerName, originCode, originCity, destinationCode, destinationCity,
            record.FlightNo, AppData.DepartureOf(record.FlightNo, record.FlightDate), booking.ClassName, record.SeatCode,
            record.CheckedInAt);
    }
}
