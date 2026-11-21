using Microsoft.Data.Sqlite;

namespace SkyWings;

/// <summary>A customer asked (usually by email) for a booking to be cancelled and refunded. Staff log it, then cancel the booking.</summary>
public sealed record RefundRequestRow(long Id, string Reference, string FlightNo, string Route, DateTime FlightDate, decimal Total,
    decimal EstimatedRefund, string Status, string LoggedBy, DateTime LoggedAt, DateTime? ClosedAt, string ClosedBy, string Note,
    string CustomerMessage = "", string Source = "STAFF", bool SenderMatches = true);

public sealed record RefundRequestResult(bool Ok, string Message);

/// <summary>The outcome of approving a refund request: the booking is cancelled and <see cref="Refund"/> was paid back under <see cref="RefundReference"/>.</summary>
public sealed record RefundApprovalResult(bool Ok, string Message, decimal Refund = 0m, string RefundReference = "");

public sealed partial class BookingStore
{
    public const string RequestOpen = "OPEN";
    public const string RequestDone = "DONE";
    public const string RequestDismissed = "DISMISSED";

    /// <summary>Raised, outside the lock, after a booking was cancelled (staff, customer desk or simulator). Used to email the customer.</summary>
    public event Action<string>? BookingCanceled;

    /// <summary>Raised, outside the lock, when staff decline a request and chose to tell the customer: the booking reference and the reason.</summary>
    public event Action<string, string>? RefundRequestDeclined;

    /// <summary>Raised, outside the lock, when a refund request arrived by email and was logged: the booking reference (used to acknowledge it).</summary>
    public event Action<string>? RefundRequestReceived;

    private static void EnsureRefundRequestSchema(SqliteConnection con)
    {
        Exec(con, "CREATE TABLE IF NOT EXISTS refund_requests (" +
                  "id INTEGER PRIMARY KEY AUTOINCREMENT, booking_ref VARCHAR(20) NOT NULL, status VARCHAR(12) NOT NULL DEFAULT 'OPEN', " +
                  "estimated_refund DECIMAL(10,2) NOT NULL DEFAULT 0, logged_by VARCHAR(80) NOT NULL DEFAULT '', logged_at DATETIME NOT NULL, " +
                  "closed_at DATETIME NULL, closed_by VARCHAR(80) NOT NULL DEFAULT '', note VARCHAR(200) NOT NULL DEFAULT '', " +
                  "customer_message VARCHAR(1000) NOT NULL DEFAULT '', source VARCHAR(10) NOT NULL DEFAULT 'STAFF', sender_matches INTEGER NOT NULL DEFAULT 1, " +
                  "FOREIGN KEY (booking_ref) REFERENCES bookings(reference) ON DELETE CASCADE)");
        // A table made before the customer's message was kept gets the column.
        EnsureColumn(con, "ALTER TABLE refund_requests ADD COLUMN customer_message VARCHAR(1000) NOT NULL DEFAULT ''");
        EnsureColumn(con, "ALTER TABLE refund_requests ADD COLUMN source VARCHAR(10) NOT NULL DEFAULT 'STAFF'");
        EnsureColumn(con, "ALTER TABLE refund_requests ADD COLUMN sender_matches INTEGER NOT NULL DEFAULT 1");
        // One open request per booking, however many times the customer writes.
        Exec(con, "CREATE UNIQUE INDEX IF NOT EXISTS ux_refund_requests_open ON refund_requests(booking_ref) WHERE status = 'OPEN'");
        Exec(con, "CREATE INDEX IF NOT EXISTS idx_refund_requests_status ON refund_requests(status, logged_at)");
    }

    /// <summary>
    /// Records that the customer asked for a refund. Refused (with a reason) for a booking that does not exist, is a demo booking,
    /// is not confirmed, has already left, or already has an open request. Nothing is cancelled or refunded here.
    /// </summary>
    public RefundRequestResult LogRefundRequest(string reference, string loggedBy, DateTime? now = null, string customerMessage = "",
        string source = "STAFF", bool senderMatches = true)
    {
        RefundRequestResult result = LogRefundRequestCore(reference, loggedBy, now, customerMessage, source, senderMatches);
        if (result.Ok && source == "EMAIL")
        {
            try { RefundRequestReceived?.Invoke(reference.Trim().ToUpperInvariant()); }
            catch (Exception ex) { LogError(ex); }
        }
        return result;
    }

    private RefundRequestResult LogRefundRequestCore(string reference, string loggedBy, DateTime? now, string customerMessage, string source, bool senderMatches)
    {
        DateTime at = now ?? DateTime.Now;
        lock (_lock)
        {
            try
            {
                BookingRecord? booking = FindByReference(reference.Trim().ToUpperInvariant());
                if (booking is null) return new RefundRequestResult(false, "No booking has that reference.");
                if (IsSimulatedBooking(booking.Reference)) return new RefundRequestResult(false, "Refund requests cannot be logged for this booking.");
                if (booking.Status != AppData.StatusConfirmed) return new RefundRequestResult(false, "Only a confirmed booking can have a refund request.");
                if (AppData.DepartureOf(booking.FlightNo, booking.FlightDateValue) <= at)
                    return new RefundRequestResult(false, "This flight has already left, so the booking can no longer be cancelled or refunded.");

                decimal estimate = AppData.ComputeRefundAmount(booking.TotalAmount, booking.FlightDateValue);
                using var con = OpenConnection();
                int added = Exec(con,
                    "INSERT INTO refund_requests (booking_ref, status, estimated_refund, logged_by, logged_at, customer_message, source, sender_matches) " +
                    "SELECT @ref, 'OPEN', @estimate, @by, @at, @message, @source, @matches WHERE NOT EXISTS (SELECT 1 FROM refund_requests WHERE booking_ref = @ref AND status = 'OPEN')",
                    ("@ref", booking.Reference), ("@estimate", estimate), ("@by", Clean(loggedBy, 80)), ("@at", at),
                    ("@message", Clean(customerMessage, 1000)), ("@source", source == "EMAIL" ? "EMAIL" : "STAFF"), ("@matches", senderMatches ? 1 : 0));
                return added == 0
                    ? new RefundRequestResult(false, "This booking already has an open refund request.")
                    : new RefundRequestResult(true, $"Refund request logged for {booking.Reference}. Refund if cancelled today: PHP {estimate:N2}.");
            }
            catch (SqliteException e)
            {
                LogError(e);
                throw new IOException("Unable to log the refund request: " + e.Message, e);
            }
        }
    }

    /// <summary>Requests with their booking details, open ones first and then the newest. Closed (done or dismissed) ones only when asked.</summary>
    /// <param name="since">Only closed requests logged from this moment on are listed; an open request is always listed, however old.</param>
    public List<RefundRequestRow> LoadRefundRequests(bool includeClosed = false, int limit = 500, DateTime? since = null)
    {
        lock (_lock)
        {
            var rows = new List<RefundRequestRow>();
            try
            {
                using var con = OpenConnection();
                using var cmd = Cmd(con,
                    "SELECT r.id, r.booking_ref, r.status, r.estimated_refund, r.logged_by, r.logged_at, r.closed_at, r.closed_by, r.note, r.customer_message, r.source, r.sender_matches, " +
                    "b.flight_no, b.origin, b.destination, b.flight_date, b.total_amount " +
                    "FROM refund_requests r JOIN bookings b ON b.reference = r.booking_ref " +
                    (!includeClosed ? "WHERE r.status = 'OPEN' " : since is not null ? "WHERE (r.status = 'OPEN' OR r.logged_at >= @since) " : "") +
                    "ORDER BY CASE r.status WHEN 'OPEN' THEN 0 ELSE 1 END, r.logged_at DESC, r.id DESC LIMIT @limit", null,
                    since is { } from && includeClosed ? new (string, object?)[] { ("@limit", Math.Clamp(limit, 1, 5000)), ("@since", from) }
                                                       : new (string, object?)[] { ("@limit", Math.Clamp(limit, 1, 5000)) });
                using var rs = cmd.ExecuteReader();
                while (rs.Read())
                    rows.Add(new RefundRequestRow(rs.GetInt64(rs.GetOrdinal("id")), Str(rs, "booking_ref"), Str(rs, "flight_no"),
                        Str(rs, "origin") + " → " + Str(rs, "destination"), Dt(rs, "flight_date"), Money(rs, "total_amount"),
                        Money(rs, "estimated_refund"), Str(rs, "status"), Str(rs, "logged_by"), Dt(rs, "logged_at"),
                        rs.IsDBNull(rs.GetOrdinal("closed_at")) ? null : Dt(rs, "closed_at"), Str(rs, "closed_by"), Str(rs, "note"),
                        Str(rs, "customer_message"), Str(rs, "source"), Int(rs, "sender_matches") != 0));
            }
            catch (SqliteException e)
            {
                LogError(e);
                throw new IOException("Unable to load the refund requests: " + e.Message, e);
            }
            return rows;
        }
    }

    public int CountOpenRefundRequests()
    {
        lock (_lock)
        {
            try
            {
                using var con = OpenConnection();
                using var cmd = Cmd(con, "SELECT COUNT(*) FROM refund_requests WHERE status = 'OPEN'");
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
            catch (SqliteException e)
            {
                LogError(e);
                throw new IOException("Unable to count the refund requests: " + e.Message, e);
            }
        }
    }

    /// <summary>
    /// Closes an open request without cancelling anything (the booking stays active). False when it is not open any more. With
    /// <paramref name="notifyCustomer"/> the customer is told the reason by email (see <see cref="RefundRequestDeclined"/>).
    /// </summary>
    public bool DismissRefundRequest(long id, string dismissedBy, string note, bool notifyCustomer = false)
    {
        string? reference = null;
        bool done;
        lock (_lock)
        {
            try
            {
                using var con = OpenConnection();
                using (var find = Cmd(con, "SELECT booking_ref FROM refund_requests WHERE id = @id AND status = 'OPEN'", null, ("@id", id)))
                    reference = find.ExecuteScalar() as string;
                done = reference is not null && Exec(con,
                    "UPDATE refund_requests SET status = 'DISMISSED', closed_at = @at, closed_by = @by, note = @note WHERE id = @id AND status = 'OPEN'",
                    ("@at", DateTime.Now), ("@by", Clean(dismissedBy, 80)), ("@note", Clean(note, 200)), ("@id", id)) > 0;
            }
            catch (SqliteException e)
            {
                LogError(e);
                throw new IOException("Unable to dismiss the refund request: " + e.Message, e);
            }
        }
        if (done && notifyCustomer)
        {
            try { RefundRequestDeclined?.Invoke(reference!, Clean(note, 200)); }
            catch (Exception ex) { LogError(ex); }
        }
        return done;
    }

    /// <summary>True when the booking had a refund request that was answered by cancelling it (so its cancellation email says "approved").</summary>
    public bool HasCompletedRefundRequest(string reference)
    {
        lock (_lock)
        {
            try
            {
                using var con = OpenConnection();
                using var cmd = Cmd(con, "SELECT 1 FROM refund_requests WHERE booking_ref = @ref AND status = 'DONE' LIMIT 1", null, ("@ref", reference));
                return cmd.ExecuteScalar() is not null;
            }
            catch (SqliteException e)
            {
                LogError(e);
                throw new IOException("Unable to read the refund request: " + e.Message, e);
            }
        }
    }

    /// <summary>
    /// Approves an open request: cancels the booking, pays the refund the policy gives today back to the original payment method, and
    /// closes the request, all in the normal cancellation (so the seats are freed, the waitlist is offered them, and the customer is
    /// emailed). Refused when the request is not open, or the booking is no longer confirmed or has left.
    /// </summary>
    public RefundApprovalResult ApproveRefundRequest(long id, string approvedBy, DateTime? now = null)
    {
        string reference;
        decimal refund;
        lock (_lock)
        {
            try
            {
                using var con = OpenConnection();
                using var cmd = Cmd(con, "SELECT booking_ref FROM refund_requests WHERE id = @id AND status = 'OPEN'", null, ("@id", id));
                if (cmd.ExecuteScalar() as string is not { } found) return new RefundApprovalResult(false, "That request is already closed.");
                reference = found;
            }
            catch (SqliteException e)
            {
                LogError(e);
                throw new IOException("Unable to read the refund request: " + e.Message, e);
            }
            BookingRecord? booking = FindByReference(reference);
            if (booking is null) return new RefundApprovalResult(false, "The booking was not found.");
            if (booking.Status != AppData.StatusConfirmed) return new RefundApprovalResult(false, "The booking is no longer confirmed.");
            if (AppData.DepartureOf(booking.FlightNo, booking.FlightDateValue) <= (now ?? DateTime.Now))
                return new RefundApprovalResult(false, "This flight has already left, so the booking can no longer be refunded.");
            refund = AppData.ComputeRefundAmount(booking.TotalAmount, booking.FlightDateValue);
        }

        if (!CancelBooking(reference, refund, approvedBy))
            return new RefundApprovalResult(false, "The booking was already cancelled or changed elsewhere.");
        RefundRecord? record = LoadRefund(reference);
        return new RefundApprovalResult(true, $"Refund approved: {reference} is cancelled and PHP {refund:N2} was refunded.", refund, record?.Reference ?? "");
    }
}