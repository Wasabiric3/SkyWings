
namespace SkyWings;

/// <summary>The label/value rows shown on a booking card, shared by the staff details and the customer views.</summary>
internal static class BookingSummary
{
    public static List<(string Label, string Value)> Rows(BookingRecord booking)
    {
        var rows = new List<(string, string)>
        {
            ("Status", $"{booking.Status} · {booking.PaymentStatus}"),
            ("Class", booking.ClassName),
            ("Travellers", string.Join(", ", booking.Passengers.Select(p => p.Name))),
            ("Outbound", $"{booking.FlightNo} · {booking.Origin} → {booking.Destination}"),
            ("Date", booking.FlightDate),
            ("Seats", string.Join(", ", booking.Passengers.Select(p => p.SeatCode).Where(c => c.Length > 0)))
        };
        if (booking.ReturnFlightNo is not null)
        {
            rows.Add(("Return", $"{booking.ReturnFlightNo} · {booking.ReturnOrigin} → {booking.ReturnDestination}"));
            rows.Add(("Return date", $"{booking.ReturnFlightDateValue:MMM dd, yyyy}"));
            rows.Add(("Return seats", string.Join(", ", booking.Passengers.Select(p => p.ReturnSeatCode).Where(c => c.Length > 0))));
        }
        rows.Add(("Baggage", booking.BaggageOption));
        rows.Add(("Meal", booking.MealOption));
        rows.Add(("Booked", booking.BookedAt));
        rows.Add(("Total", $"PHP {booking.TotalAmount:N2}"));
        rows.Add(("Payment", $"{booking.PaymentMethod} {booking.MaskedAccount}"));
        rows.Add(("Refund", booking.RefundAmount > 0 ? $"PHP {booking.RefundAmount:N2}" : "—"));
        return rows;
    }

    /// <summary>Customers can change or cancel only confirmed bookings whose outbound flight has not left yet.</summary>
    public static bool CanModify(BookingRecord booking)
        => booking.Status == AppData.StatusConfirmed &&
           AppData.DepartureOf(booking.FlightNo, booking.FlightDateValue) > DateTime.Now;
}
