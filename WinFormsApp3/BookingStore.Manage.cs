using Microsoft.Data.Sqlite;

namespace SkyWings;

public sealed partial class BookingStore
{
    /// <summary>
    /// Customer lookup: a booking is returned only when the reference exists and the last name matches one of its
    /// passengers. Callers show one generic "not found" message for both failures, so references cannot be guessed.
    /// </summary>
    public BookingRecord? FindForCustomer(string reference, string lastName)
    {
        string key = lastName.Trim();
        if (reference.Trim().Length == 0 || key.Length == 0) return null;
        BookingRecord? booking = FindByReference(reference.Trim().ToUpperInvariant());
        if (booking is null) return null;
        bool match = booking.Passengers.Any(p => string.Equals(LastWord(p.Name), key, StringComparison.OrdinalIgnoreCase));
        return match ? booking : null;
    }

    private static string LastWord(string fullName)
    {
        string trimmed = fullName.Trim();
        int space = trimmed.LastIndexOf(' ');
        return space < 0 ? trimmed : trimmed[(space + 1)..];
    }

    /// <summary>
    /// Cancels a confirmed booking and records the refund. Returns false when the booking is missing or is no longer
    /// confirmed (for example it was already canceled from another screen), so a refund is never recorded twice.
    /// </summary>
    public bool CancelBooking(string reference, decimal refundAmount)
    {
        var freed = new List<(string FlightNo, DateTime Date)>();
        bool done;
        lock (_lock)
        {
            try
            {
                using var con = OpenConnection();
                done = Exec(con,
                    "UPDATE bookings SET status = 'CANCELED', refund_amount = @refund, " +
                    "payment_status = CASE WHEN @refund > 0 THEN 'REFUNDED' ELSE payment_status END " +
                    "WHERE reference = @ref AND status = 'CONFIRMED'",
                    ("@refund", refundAmount), ("@ref", reference)) > 0;
                if (done)
                {
                    using var cmd = Cmd(con,
                        "SELECT flight_no, flight_date FROM bookings WHERE reference = @ref " +
                        "UNION SELECT flight_no, flight_date FROM booking_legs WHERE booking_ref = @ref", null, ("@ref", reference));
                    using var rs = cmd.ExecuteReader();
                    while (rs.Read()) freed.Add((Str(rs, "flight_no"), Dt(rs, "flight_date")));
                }
            }
            catch (SqliteException e)
            {
                throw new IOException("Database error canceling booking: " + e.Message, e);
            }
        }

        // Seats on every leg are free again: offer them to the waitlist (outside the lock; sends the offer emails).
        foreach (var (flightNo, date) in freed)
        {
            try { PromoteWaitlist(flightNo, date, DateTime.Now); }
            catch (Exception ex) { LogError(ex); }
        }
        return done;
    }

    /// <summary>
    /// Moves one passenger to another seat on the outbound or return leg. Returns false when the seat is taken, held by
    /// someone else, outside the booked cabin, or the booking is no longer confirmed.
    /// </summary>
    public bool ChangeSeat(string reference, string passengerName, bool returnLeg, string newSeat)
    {
        string wanted = SeatLayout.Normalize(newSeat);
        if (wanted.Length == 0) return false;

        lock (_lock)
        {
            try
            {
                using var con = OpenConnection();
                string flightNo, className, status;
                DateTime flightDate;
                using (var cmd = Cmd(con,
                    "SELECT status, class_name, flight_no, flight_date FROM bookings WHERE reference = @ref", null, ("@ref", reference)))
                using (var rs = cmd.ExecuteReader())
                {
                    if (!rs.Read()) return false;
                    status = Str(rs, "status");
                    className = Str(rs, "class_name");
                    flightNo = Str(rs, "flight_no");
                    flightDate = Dt(rs, "flight_date");
                }
                if (status != AppData.StatusConfirmed) return false;
                if (!wanted.StartsWith(AppData.SeatPrefixForClass(className), StringComparison.Ordinal)) return false;

                if (returnLeg)
                {
                    using var leg = Cmd(con,
                        "SELECT flight_no, flight_date FROM booking_legs WHERE booking_ref = @ref AND leg_order = 2", null, ("@ref", reference));
                    using var lrs = leg.ExecuteReader();
                    if (!lrs.Read()) return false;
                    flightNo = Str(lrs, "flight_no");
                    flightDate = Dt(lrs, "flight_date");
                }

                if (AppData.DepartureOf(flightNo, flightDate) <= DateTime.Now) return false;

                // The seat this passenger already has is not "taken" from their own point of view.
                string column = returnLeg ? "return_seat_code" : "seat_code";
                string current = "";
                using (var cmd = Cmd(con, $"SELECT {column} FROM passengers WHERE booking_ref = @ref AND name = @name", null,
                           ("@ref", reference), ("@name", passengerName)))
                {
                    object? value = cmd.ExecuteScalar();
                    if (value is null) return false;
                    current = SeatLayout.Normalize(value as string);
                }
                if (current == wanted) return true;

                // Once checked in, the seat is on a boarding pass and can no longer move.
                using (var checkedIn = Cmd(con,
                    "SELECT COUNT(*) FROM check_ins WHERE booking_ref = @ref AND passenger_name = @name " +
                    "AND flight_no = @no AND flight_date = @date", null,
                    ("@ref", reference), ("@name", passengerName), ("@no", flightNo), ("@date", flightDate.Date)))
                    if (Convert.ToInt32(checkedIn.ExecuteScalar()) > 0) return false;

                if (LoadTakenSeatsCore(con, flightNo, flightDate).Contains(wanted)) return false;

                using (var hold = Cmd(con,
                    "SELECT COUNT(*) FROM seat_holds WHERE flight_no = @no AND flight_date = @date AND seat_code = @seat " +
                    "AND expires_at >= @now AND status = 'ACTIVE'", null,
                    ("@no", flightNo), ("@date", flightDate.Date), ("@seat", newSeat), ("@now", DateTime.Now)))
                    if (Convert.ToInt32(hold.ExecuteScalar()) > 0) return false;

                return Exec(con, $"UPDATE passengers SET {column} = @seat WHERE booking_ref = @ref AND name = @name",
                    ("@seat", newSeat), ("@ref", reference), ("@name", passengerName)) > 0;
            }
            catch (SqliteException e)
            {
                throw new IOException("Database error changing seat: " + e.Message, e);
            }
        }
    }
}
