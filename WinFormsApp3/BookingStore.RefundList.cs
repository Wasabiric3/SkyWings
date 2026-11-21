using Microsoft.Data.Sqlite;

namespace SkyWings;

/// <summary>One cancelled booking in the staff Refunds tab. <see cref="RefundReference"/> and <see cref="RefundedAt"/> are empty
/// for older cancellations that were saved before refunds got their own record.</summary>
public sealed record RefundListRow(string Reference, string FlightNo, string Route, DateTime FlightDate, decimal Total, decimal Refund,
    string RefundReference, string Method, DateTime? RefundedAt, string HandledBy);

/// <summary>The newest cancelled bookings that match, and the totals over every match (the rows are capped).</summary>
public sealed record RefundList(IReadOnlyList<RefundListRow> Rows, int MatchCount, decimal MatchRefundTotal);

/// <summary>How the Refunds tab orders its list.</summary>
public static class RefundSort
{
    public const string Newest = "newest";
    public const string Oldest = "oldest";
    public const string Largest = "largest";
    public const string Smallest = "smallest";
}

/// <summary>Who handled a refund, as the Refunds tab filters it.</summary>
public static class RefundHandler
{
    public const string Anyone = "";
    public const string Staff = "staff";
    public const string Customer = "customer";
    public const string System = "system";
}

public sealed partial class BookingStore
{
    private const string RefundByMarker = "Cancellation refund by ";

    /// <summary>
    /// Cancelled bookings with the refund paid back. By default only bookings that got money back are listed;
    /// <paramref name="includeNoRefund"/> adds the ones cancelled with nothing returned. <paramref name="search"/> matches the booking
    /// reference, the flight number or the refund reference.
    /// </summary>
    /// <param name="since">Only refunds from this moment on (the refund's own time; for older cancellations that have none, the flight date).</param>
    /// <param name="sort">One of <see cref="RefundSort"/>. Refunds with a recorded time always come before the older ones without.</param>
    /// <param name="handledBy">One of <see cref="RefundHandler"/>. Older cancellations with no refund record match none of the specific choices.</param>
    public RefundList LoadRefundList(string? search = null, bool includeNoRefund = false, int limit = 1000,
        DateTime? since = null, string sort = RefundSort.Newest, string handledBy = RefundHandler.Anyone)
    {
        lock (_lock)
        {
            string where = " WHERE b.status = 'CANCELED'" + (includeNoRefund ? "" : " AND b.refund_amount > 0");
            var args = new List<(string, object?)>();
            if (!string.IsNullOrWhiteSpace(search))
            {
                where += " AND (b.reference LIKE @s ESCAPE '\\' OR b.flight_no LIKE @s ESCAPE '\\' OR p.transaction_ref LIKE @s ESCAPE '\\')";
                args.Add(("@s", "%" + search.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%"));
            }
            if (since is { } from)
            {
                where += " AND COALESCE(p.created_at, b.flight_date) >= @since";
                args.Add(("@since", from));
            }
            where += handledBy switch
            {
                RefundHandler.Customer => " AND p.detail_message = '" + RefundByMarker + "customer'",
                RefundHandler.System => " AND p.detail_message = '" + RefundByMarker + "system'",
                RefundHandler.Staff => " AND p.detail_message LIKE '" + RefundByMarker + "%' AND p.detail_message <> '" + RefundByMarker + "customer' AND p.detail_message <> '" + RefundByMarker + "system'",
                _ => ""
            };
            string when = "COALESCE(p.created_at, b.flight_date)";
            string order = sort switch
            {
                RefundSort.Oldest => $"(p.created_at IS NULL), {when} ASC",
                RefundSort.Largest => "b.refund_amount DESC, (p.created_at IS NULL), " + when + " DESC",
                RefundSort.Smallest => "b.refund_amount ASC, (p.created_at IS NULL), " + when + " DESC",
                _ => $"(p.created_at IS NULL), {when} DESC"
            };
            const string from2 =
                " FROM bookings b LEFT JOIN booking_payments p ON p.booking_ref = b.reference AND p.status = '" + RefundStatus + "'" +
                " AND p.id = (SELECT MAX(id) FROM booking_payments WHERE booking_ref = b.reference AND status = '" + RefundStatus + "')";
            var rows = new List<RefundListRow>();
            try
            {
                using var con = OpenConnection();
                using (var cmd = Cmd(con,
                    "SELECT b.reference, b.flight_no, b.origin, b.destination, b.flight_date, b.total_amount, b.refund_amount," +
                    " b.payment_method, b.masked_account, p.transaction_ref, p.created_at, p.detail_message" + from2 + where +
                    " ORDER BY " + order + ", b.reference LIMIT @limit", null,
                    args.Append(("@limit", (object?)Math.Clamp(limit, 1, 5000))).ToArray()))
                using (var rs = cmd.ExecuteReader())
                    while (rs.Read())
                    {
                        string detail = StrOrNull(rs, "detail_message") ?? "";
                        int by = detail.IndexOf(RefundByMarker, StringComparison.Ordinal);
                        rows.Add(new RefundListRow(Str(rs, "reference"), Str(rs, "flight_no"),
                            Str(rs, "origin") + " → " + Str(rs, "destination"), Dt(rs, "flight_date"),
                            Money(rs, "total_amount"), Money(rs, "refund_amount"), StrOrNull(rs, "transaction_ref") ?? "",
                            (Str(rs, "payment_method") + " " + Str(rs, "masked_account")).Trim(),
                            rs.IsDBNull(rs.GetOrdinal("created_at")) ? null : Dt(rs, "created_at"),
                            by < 0 ? "" : detail[(by + RefundByMarker.Length)..]));
                    }

                int count;
                decimal total;
                using (var cmd = Cmd(con, "SELECT COUNT(*), COALESCE(SUM(b.refund_amount), 0)" + from2 + where, null, args.ToArray()))
                using (var rs = cmd.ExecuteReader())
                {
                    rs.Read();
                    count = rs.GetInt32(0);
                    total = Convert.ToDecimal(rs.GetValue(1), System.Globalization.CultureInfo.InvariantCulture);
                }
                return new RefundList(rows, count, total);
            }
            catch (SqliteException e)
            {
                LogError(e);
                throw new IOException("Unable to load the refunds: " + e.Message, e);
            }
        }
    }
}
