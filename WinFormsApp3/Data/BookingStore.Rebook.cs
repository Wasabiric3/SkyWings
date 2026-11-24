using Microsoft.Data.Sqlite;

namespace SkyWings;

public sealed partial class BookingStore
{
    /// <summary>Status of a <c>booking_payments</c> row that charges extra after the booking was made (a flight change).</summary>
    public const string ChangeChargeStatus = "SUCCESS";

    /// <summary>True when any traveller on the booking has checked in for the booking's outbound flight.</summary>
    public bool HasOutboundCheckIn(string reference)
    {
        lock (_lock)
        {
            try
            {
                using var con = OpenConnection();
                using var cmd = Cmd(con,
                    "SELECT COUNT(*) FROM check_ins c JOIN bookings b ON b.reference = c.booking_ref " +
                    "WHERE c.booking_ref = @ref AND c.flight_no = b.flight_no AND c.flight_date = b.flight_date", null, ("@ref", reference));
                return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
            }
            catch (SqliteException e)
            {
                LogError(e);
                throw new IOException("Unable to read the check-ins: " + e.Message, e);
            }
        }
    }

    /// <summary>
    /// Moves a confirmed booking's outbound leg to another flight and date on the same route and in the same cabin. The travellers
    /// get the first free seats of the cabin on the new flight (the old seats are freed and offered to the waitlist), the change
    /// charge from <see cref="FlightChangeRules.Quote"/> is added to the booking total and recorded as a payment, and everything
    /// is written in one transaction. The checks are repeated here, so a screen that is out of date cannot move a booking wrongly.
    /// </summary>
    public FlightChangeResult ChangeOutboundFlight(string reference, string newFlightNo, DateTime newDate, string changedBy)
    {
        DateTime oldDate;
        string oldFlightNo;
        FlightChangeResult result;
        lock (_lock)
        {
            try
            {
                BookingRecord? booking = FindByReference(reference);
                if (booking is null) return FlightChangeResult.Refused("That booking was not found.");
                oldFlightNo = booking.FlightNo;
                oldDate = booking.FlightDateValue;
                if (FlightChangeRules.BookingRefusal(booking, HasOutboundCheckIn(reference), DateTime.Now) is { } why)
                    return FlightChangeResult.Refused(why);

                List<FlightAdminRow> all = LoadAllFlights();
                FlightAdminRow? oldRow = all.FirstOrDefault(f => f.FlightNo == oldFlightNo);
                FlightAdminRow? newRow = all.FirstOrDefault(f => f.FlightNo == newFlightNo);
                if (oldRow is null || newRow is null || newRow.Status != "Active")
                    return FlightChangeResult.Refused("That flight is not on the schedule.");
                var oldFlight = new Flight(oldRow.FlightNo, oldRow.Origin, oldRow.Destination, oldRow.BasePrice);
                var newFlight = new Flight(newRow.FlightNo, newRow.Origin, newRow.Destination, newRow.BasePrice);
                newDate = newDate.Date;
                if (FlightChangeRules.TargetRefusal(booking, newFlight, newDate, DateTime.Now) is { } problem)
                    return FlightChangeResult.Refused(problem);

                // Reads first (their own connections), then one short write transaction.
                Aircraft aircraft = LoadAircraft(newFlightNo);
                CabinLayout? cabin = aircraft.Cabin(booking.ClassName);
                if (cabin is null) return FlightChangeResult.Refused($"The new flight has no {booking.ClassName} cabin.");
                int pax = booking.Passengers.Count;
                if (pax == 0) return FlightChangeResult.Refused("The booking has no travellers.");
                if (CabinSeatsLeft(newFlightNo, newDate, booking.ClassName) < pax)
                    return FlightChangeResult.Refused($"There are not enough {booking.ClassName} seats left on {newFlightNo} for {pax} traveller(s).");
                var unavailable = new HashSet<string>(LoadTakenSeats(newFlightNo, newDate), StringComparer.Ordinal);
                foreach (string held in GetActiveHoldsForFlightDate(newFlightNo, newDate).Keys) unavailable.Add(SeatLayout.Normalize(held));
                var seats = new List<string>();
                for (int row = 1; row <= cabin.Rows && seats.Count < pax; row++)
                    foreach (char letter in cabin.Letters)
                    {
                        string code = cabin.Code(row, letter);
                        if (unavailable.Contains(SeatLayout.Normalize(code))) continue;
                        seats.Add(code);
                        if (seats.Count == pax) break;
                    }
                if (seats.Count < pax) return FlightChangeResult.Refused("There are not enough free seats on that flight.");

                ChangeQuote quote = FlightChangeRules.Quote(booking, oldFlight, newFlight, newDate);
                decimal total = quote.Total;

                using var con = OpenConnection();
                using var tx = con.BeginTransaction();
                var passengerIds = new List<long>();
                using (var cmd = Cmd(con, "SELECT id FROM passengers WHERE booking_ref = @ref ORDER BY id", tx, ("@ref", reference)))
                using (var rs = cmd.ExecuteReader())
                    while (rs.Read()) passengerIds.Add(rs.GetInt64(0));
                if (passengerIds.Count != pax) return FlightChangeResult.Refused("The traveller list changed. Reload the booking and try again.");

                int updated = Exec(con, tx,
                    "UPDATE bookings SET flight_no = @no, flight_date = @date, domestic = @dom, total_amount = total_amount + @due " +
                    "WHERE reference = @ref AND status = 'CONFIRMED' AND flight_no = @oldNo AND flight_date = @oldDate",
                    ("@no", newFlightNo), ("@date", newDate), ("@dom", newFlight.Domestic ? 1 : 0), ("@due", total),
                    ("@ref", reference), ("@oldNo", oldFlightNo), ("@oldDate", oldDate.Date));
                if (updated == 0) return FlightChangeResult.Refused("The booking was changed elsewhere. Reload it and try again.");

                decimal share = AppData.Round2(total / pax);
                for (int i = 0; i < pax; i++)
                {
                    decimal part = i == 0 ? total - share * (pax - 1) : share;   // the first traveller carries the rounding
                    Exec(con, tx, "UPDATE passengers SET seat_code = @seat, total_amount = total_amount + @part WHERE id = @id",
                        ("@seat", seats[i]), ("@part", part), ("@id", passengerIds[i]));
                }
                Exec(con, tx,
                    "INSERT INTO booking_payments (booking_ref,amount,method,account_masked,status,transaction_ref,detail_message,created_at) " +
                    "SELECT reference, @due, payment_method, masked_account, '" + ChangeChargeStatus + "', @txn, @detail, @now FROM bookings WHERE reference = @ref",
                    ("@due", total), ("@txn", "CHG-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant()),
                    ("@detail", $"Flight change {oldFlightNo} {oldDate:yyyy-MM-dd} to {newFlightNo} {newDate:yyyy-MM-dd} by {changedBy}. {quote.Summary}"),
                    ("@now", DateTime.Now), ("@ref", reference));
                tx.Commit();
                result = new FlightChangeResult(FlightChangeOutcome.Changed,
                    $"Moved to {newFlightNo} on {newDate:MMM d, yyyy}. Charged PHP {total:N2}.", seats);
            }
            catch (SqliteException e)
            {
                LogError(e);
                throw new IOException("Database error changing the flight: " + e.Message, e);
            }
        }

        // The old seats are free again: offer them to the waitlist (outside the lock; sends the offer emails).
        try { PromoteWaitlist(oldFlightNo, oldDate, DateTime.Now); }
        catch (Exception ex) { LogError(ex); }
        return result;
    }
}
