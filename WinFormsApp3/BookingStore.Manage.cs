using Microsoft.Data.Sqlite;

namespace SkyWings;

public sealed partial class BookingStore
{
    /// <summary>
    /// Customer lookup: a booking is found by its reference alone (the reference is a random 12-character code). Callers count
    /// misses towards the lookup lock (<see cref="LookupGuard"/>) so references cannot be guessed by trying many.
    /// </summary>
    public BookingRecord? FindForCustomer(string reference)
        => reference.Trim().Length == 0 ? null : FindByReference(reference.Trim().ToUpperInvariant());

    /// <summary>
    /// Cancels a confirmed booking and records the refund. Returns false when the booking is missing or is no longer
    /// confirmed (for example it was already canceled from another screen), so a refund is never recorded twice.
    /// </summary>
    public bool CancelBooking(string reference, decimal refundAmount, string issuedBy = "customer")
    {
        var freed = new List<(string FlightNo, DateTime Date)>();
        bool done;
        int closedRequests = 0;
        lock (_lock)
        {
            try
            {
                using var con = OpenConnection();
                using (var tx = con.BeginTransaction())
                {
                    done = Exec(con, tx,
                        "UPDATE bookings SET status = 'CANCELED', refund_amount = @refund, " +
                        "payment_status = CASE WHEN @refund > 0 THEN 'REFUNDED' ELSE payment_status END " +
                        "WHERE reference = @ref AND status = 'CONFIRMED'",
                        ("@refund", refundAmount), ("@ref", reference)) > 0;
                    // The money going back is its own record (same table as the payment), so it can be shown and audited.
                    if (done && refundAmount > 0)
                        Exec(con, tx,
                            "INSERT INTO booking_payments (booking_ref,amount,method,account_masked,status,transaction_ref,detail_message,created_at) " +
                            "SELECT reference, @refund, payment_method, masked_account, '" + RefundStatus + "', @txn, @detail, @now FROM bookings WHERE reference = @ref",
                            ("@refund", refundAmount), ("@txn", NewRefundReference()), ("@detail", "Cancellation refund by " + issuedBy),
                            ("@now", DateTime.Now), ("@ref", reference));
                    // A refund request waiting for this booking is answered by the cancellation itself.
                    if (done)
                        closedRequests = Exec(con, tx,
                            "UPDATE refund_requests SET status = 'DONE', closed_at = @now, closed_by = @by WHERE booking_ref = @ref AND status = 'OPEN'",
                            ("@now", DateTime.Now), ("@by", Clean(issuedBy, 80)), ("@ref", reference));
                    tx.Commit();
                }
                if (closedRequests > 0)
                    WriteAuditCore(con, issuedBy, "SYSTEM", "REFUND_REQUEST_CLOSED", reference, "Closed because the booking was cancelled");
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
        if (done)
        {
            // Outside the lock: the handler emails the customer and must never affect the cancellation.
            try { BookingCanceled?.Invoke(reference); }
            catch (Exception ex) { LogError(ex); }
        }
        return done;
    }

    /// <summary>Status of a <c>booking_payments</c> row that records money paid back to the customer.</summary>
    public const string RefundStatus = "REFUND";

    private static string NewRefundReference() => "RFD-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();

    /// <summary>The newest refund issued for the booking, or null when there is none (not canceled, or nothing to return).</summary>
    public RefundRecord? LoadRefund(string reference) => LoadRefunds(reference).FirstOrDefault();

    /// <summary>Every refund recorded for the booking, newest first. A booking is canceled once, so normally zero or one.</summary>
    public List<RefundRecord> LoadRefunds(string reference)
    {
        lock (_lock)
        {
            var list = new List<RefundRecord>();
            try
            {
                using var con = OpenConnection();
                using var cmd = Cmd(con,
                    "SELECT amount, method, account_masked, transaction_ref, detail_message, created_at FROM booking_payments " +
                    "WHERE booking_ref = @ref AND status = @status ORDER BY id DESC", null, ("@ref", reference), ("@status", RefundStatus));
                using var rs = cmd.ExecuteReader();
                while (rs.Read())
                    list.Add(new RefundRecord(Money(rs, "amount"), Str(rs, "method"), Str(rs, "account_masked"),
                        StrOrNull(rs, "transaction_ref") ?? "", Dt(rs, "created_at"), StrOrNull(rs, "detail_message") ?? ""));
            }
            catch (SqliteException e)
            {
                LogError(e);
                throw new IOException("Unable to read the refund: " + e.Message, e);
            }
            return list;
        }
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
