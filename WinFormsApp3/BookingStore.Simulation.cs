using System.Globalization;
using Microsoft.Data.Sqlite;

namespace SkyWings;

/// <summary>
/// Demo support: simulated bookings (marked <c>is_simulated = 1</c>, references start with <c>SIM-</c>) that fill seat maps and
/// availability so the system looks busy, plus the small settings store that switches the demo activity on and off.
/// Simulated bookings block seats like real ones, but staff reports leave them out unless the setting says otherwise.
/// </summary>
public sealed partial class BookingStore
{
    public const string SimulatedPrefix = "SIM-";
    public const string SettingDemoActivity = "demo.activity";
    public const string SettingDemoInReports = "demo.in_reports";
    /// <summary>Which version of the load algorithm made the current demo bookings; a newer one regenerates them once.</summary>
    public const string SettingDemoLoadVersion = "demo.load_version";

    // ------------------------------------------------------------------ settings

    public string GetSetting(string key, string fallback)
    {
        lock (_lock)
        {
            try
            {
                using var con = OpenConnection();
                using var cmd = Cmd(con, "SELECT setting_value FROM app_settings WHERE setting_key = @k", null, ("@k", key));
                return cmd.ExecuteScalar() as string ?? fallback;
            }
            catch (SqliteException e) { LogError(e); return fallback; }
        }
    }

    public void SetSetting(string key, string value)
    {
        lock (_lock)
        {
            try
            {
                using var con = OpenConnection();
                Exec(con, "INSERT INTO app_settings (setting_key, setting_value) VALUES (@k, @v) " +
                          "ON CONFLICT(setting_key) DO UPDATE SET setting_value = excluded.setting_value", ("@k", key), ("@v", value));
            }
            catch (SqliteException e)
            {
                throw new IOException("Error saving a setting: " + e.Message, e);
            }
        }
    }

    /// <summary>
    /// Whether the demo activity (simulated bookings, holds and cancellations) runs. On by default so the board shows realistic
    /// loads; an admin can switch it off. Demo bookings stay out of reports either way unless <see cref="IncludeSimulatedInReports"/>.
    /// </summary>
    public bool DemoActivityEnabled => GetSetting(SettingDemoActivity, "1") == "1";

    /// <summary>Whether staff reports include simulated bookings. Off by default so real numbers stay clean.</summary>
    public bool IncludeSimulatedInReports => GetSetting(SettingDemoInReports, "0") == "1";

    /// <summary>" WHERE is_simulated = 0" for a query without a WHERE, or "" when reports include demo bookings.</summary>
    private string ReportWhere(string alias = "") => IncludeSimulatedInReports ? "" : $" WHERE {alias}is_simulated = 0";

    /// <summary>" AND is_simulated = 0" for a query that already has a WHERE, or "" when reports include demo bookings.</summary>
    private string ReportAnd(string alias = "") => IncludeSimulatedInReports ? "" : $" AND {alias}is_simulated = 0";

    // ------------------------------------------------------------------ simulated bookings

    /// <summary>Stores one generated booking. See <see cref="SaveSimulatedBookings"/>.</summary>
    public void SaveSimulatedBooking(BookingRecord booking) => SaveSimulatedBookings(new[] { booking });

    /// <summary>
    /// Stores generated bookings in one transaction, reusing prepared commands (a day's load is thousands of rows). Writes only the
    /// booking and its passengers: no payment record, promo, loyalty, contact, email or audit entry, so a demo booking has no
    /// side effects outside the seat map and the board. A reference that already exists is left as it is.
    /// </summary>
    /// <exception cref="ArgumentException">A reference does not start with <c>SIM-</c>: real bookings can never be flagged by accident.</exception>
    public void SaveSimulatedBookings(IReadOnlyList<BookingRecord> bookings)
    {
        foreach (BookingRecord b in bookings)
            if (!b.Reference.StartsWith(SimulatedPrefix, StringComparison.Ordinal))
                throw new ArgumentException("A simulated booking's reference must start with " + SimulatedPrefix, nameof(bookings));
        if (bookings.Count == 0) return;
        lock (_lock)
        {
            try
            {
                using var con = OpenConnection();
                using var tx = con.BeginTransaction();
                using SqliteCommand insertBooking = PreparedCommand(con, tx,
                    "INSERT OR IGNORE INTO bookings (reference,booked_at,flight_no,origin,destination,flight_date," +
                    "domestic,class_name,payment_method,masked_account,total_amount,realtime_est,status," +
                    "payment_status,payment_txn_ref,payment_confirmed_at,refund_amount,is_simulated) " +
                    "VALUES (@reference,@booked_at,@flight_no,@origin,@destination,@flight_date," +
                    "@domestic,@class_name,@payment_method,@masked_account,@total_amount,@realtime_est,@status," +
                    "@payment_status,@payment_txn_ref,@payment_confirmed_at,@refund_amount,1)");
                using SqliteCommand insertPassenger = PreparedCommand(con, tx,
                    "INSERT INTO passengers (booking_ref,name,address,contact,age,birthday," +
                    "id_number,senior,pwd,seat_code,return_seat_code,addons,total_amount) " +
                    "VALUES (@ref,@name,@address,@contact,@age,@birthday,@id,@senior,@pwd,@seat,@returnSeat,@addons,@total)");

                foreach (BookingRecord booking in bookings)
                {
                    Bind(insertBooking, ("@reference", booking.Reference), ("@booked_at", booking.BookedAtValue),
                        ("@flight_no", booking.FlightNo), ("@origin", booking.Origin), ("@destination", booking.Destination),
                        ("@flight_date", booking.FlightDateValue.Date), ("@domestic", booking.Domestic ? 1 : 0),
                        ("@class_name", booking.ClassName), ("@payment_method", booking.PaymentMethod),
                        ("@masked_account", booking.MaskedAccount), ("@total_amount", booking.TotalAmount),
                        ("@realtime_est", booking.RealtimeEst), ("@status", booking.Status),
                        ("@payment_status", booking.PaymentStatus),
                        ("@payment_txn_ref", booking.PaymentTransactionRef.Length == 0 ? null : booking.PaymentTransactionRef),
                        ("@payment_confirmed_at", booking.PaymentConfirmedAtValue), ("@refund_amount", booking.RefundAmount));
                    if (insertBooking.ExecuteNonQuery() == 0) continue;   // already there

                    foreach (PassengerRecord p in booking.Passengers)
                    {
                        Bind(insertPassenger, ("@ref", booking.Reference), ("@name", p.Name), ("@address", p.Address),
                            ("@contact", p.Contact), ("@age", p.Age), ("@birthday", p.Birthday), ("@id", p.IdNumber),
                            ("@senior", p.Senior ? 1 : 0), ("@pwd", p.Pwd ? 1 : 0), ("@seat", p.SeatCode),
                            ("@returnSeat", p.ReturnSeatCode), ("@addons", string.Join(",", p.Addons)), ("@total", p.TotalAmount));
                        insertPassenger.ExecuteNonQuery();
                    }

                    if (!string.IsNullOrWhiteSpace(booking.ReturnFlightNo))
                        Exec(con, tx,
                            "INSERT INTO booking_legs (booking_ref,leg_order,flight_no,origin,destination,flight_date,domestic,realtime_est) " +
                            "VALUES (@ref,2,@flight,@origin,@destination,@date,@domestic,@est)",
                            ("@ref", booking.Reference), ("@flight", booking.ReturnFlightNo),
                            ("@origin", booking.ReturnOrigin ?? ""), ("@destination", booking.ReturnDestination ?? ""),
                            ("@date", booking.ReturnFlightDateValue?.Date ?? booking.FlightDateValue),
                            ("@domestic", booking.ReturnDomestic ? 1 : 0), ("@est", "Scheduled"));
                }
                tx.Commit();
            }
            catch (SqliteException e)
            {
                throw new IOException("Error saving simulated bookings: " + e.Message, e);
            }
        }
    }

    /// <summary>A command whose parameters are created once from the @names in its SQL and re-bound for each row.</summary>
    private static SqliteCommand PreparedCommand(SqliteConnection con, SqliteTransaction tx, string sql)
    {
        var cmd = new SqliteCommand(sql, con, tx);
        foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(sql, "@[A-Za-z_]+"))
            if (!cmd.Parameters.Contains(m.Value)) cmd.Parameters.AddWithValue(m.Value, DBNull.Value);
        return cmd;
    }

    private static void Bind(SqliteCommand cmd, params (string Name, object? Value)[] values)
    {
        foreach (var (name, value) in values) cmd.Parameters[name].Value = value ?? DBNull.Value;
    }

    /// <summary>
    /// Deletes simulated bookings (all, or those of one flight) with their passengers and legs, and releases the demo seat holds.
    /// Real bookings are never touched. Returns how many bookings were removed.
    /// </summary>
    public int DeleteSimulatedBookings(string? flightNo = null)
    {
        lock (_lock)
        {
            try
            {
                using var con = OpenConnection();
                using var tx = con.BeginTransaction();
                string scope = flightNo is null ? "" : " AND flight_no = @no";
                (string, object?)[] args = flightNo is null ? Array.Empty<(string, object?)>() : new (string, object?)[] { ("@no", flightNo) };
                string refs = $"SELECT reference FROM bookings WHERE is_simulated = 1{scope}";
                Exec(con, tx, $"DELETE FROM passengers WHERE booking_ref IN ({refs})", args);
                Exec(con, tx, $"DELETE FROM booking_legs WHERE booking_ref IN ({refs})", args);
                int removed = Exec(con, tx, $"DELETE FROM bookings WHERE is_simulated = 1{scope}", args);
                Exec(con, tx, $"UPDATE seat_holds SET status = 'RELEASED' WHERE owner LIKE 'SIM-%' AND status = 'ACTIVE'{scope}", args);
                tx.Commit();
                return removed;
            }
            catch (SqliteException e)
            {
                throw new IOException("Error removing simulated bookings: " + e.Message, e);
            }
        }
    }

    // ------------------------------------------------------------------ queries used by the simulator


    /// <summary>
    /// Seats assigned on real (not simulated) confirmed bookings for flights between two dates, by "PR-101|20261120", normalized.
    /// One query for the whole range, so generating a week of demo load does not ask once per flight and date.
    /// </summary>
    public Dictionary<string, HashSet<string>> LoadRealTakenSeatsByFlightDate(DateTime from, DateTime to)
    {
        lock (_lock)
        {
            try
            {
                var map = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
                using var con = OpenConnection();
                void Read(string sql)
                {
                    using var cmd = Cmd(con, sql, null, ("@a", from.Date), ("@b", to.Date));
                    using var rs = cmd.ExecuteReader();
                    while (rs.Read())
                    {
                        string key = SimulatedKey(rs.GetString(0), rs.GetDateTime(1));
                        if (!map.TryGetValue(key, out HashSet<string>? seats)) map[key] = seats = new HashSet<string>(StringComparer.Ordinal);
                        seats.Add(SeatLayout.Normalize(rs.GetString(2)));
                    }
                }
                Read("SELECT b.flight_no, b.flight_date, p.seat_code FROM bookings b JOIN passengers p ON p.booking_ref = b.reference " +
                     "WHERE b.is_simulated = 0 AND b.status = 'CONFIRMED' AND b.flight_date BETWEEN @a AND @b AND p.seat_code <> ''");
                Read("SELECT l.flight_no, l.flight_date, p.return_seat_code FROM bookings b JOIN booking_legs l ON l.booking_ref = b.reference " +
                     "JOIN passengers p ON p.booking_ref = b.reference " +
                     "WHERE b.is_simulated = 0 AND b.status = 'CONFIRMED' AND l.flight_date BETWEEN @a AND @b AND p.return_seat_code <> ''");
                return map;
            }
            catch (SqliteException e) { LogError(e); throw new IOException("Unable to read booked seats: " + e.Message, e); }
        }
    }

    /// <summary>How many demo bookings exist (confirmed and cancelled).</summary>
    public int CountSimulatedBookings()
    {
        lock (_lock)
        {
            using var con = OpenConnection();
            using var cmd = Cmd(con, "SELECT COUNT(*) FROM bookings WHERE is_simulated = 1");
            return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
    }
    /// <summary>The flight/date pairs that already have generated bookings, as "PR-101|20261120".</summary>
    public HashSet<string> LoadSimulatedFlightDates()
    {
        lock (_lock)
        {
            try
            {
                var keys = new HashSet<string>(StringComparer.Ordinal);
                using var con = OpenConnection();
                using var cmd = Cmd(con, "SELECT DISTINCT flight_no, flight_date FROM bookings WHERE is_simulated = 1");
                using var rs = cmd.ExecuteReader();
                while (rs.Read()) keys.Add(SimulatedKey(Str(rs, "flight_no"), Dt(rs, "flight_date")));
                return keys;
            }
            catch (SqliteException e) { LogError(e); throw new IOException("Unable to read simulated bookings: " + e.Message, e); }
        }
    }

    public static string SimulatedKey(string flightNo, DateTime date) => $"{flightNo}|{date:yyyyMMdd}";

    public int CountSimulatedConfirmed(DateTime fromDate)
    {
        lock (_lock)
        {
            using var con = OpenConnection();
            using var cmd = Cmd(con, "SELECT COUNT(*) FROM bookings WHERE is_simulated = 1 AND status = 'CONFIRMED' AND flight_date >= @d", null, ("@d", fromDate.Date));
            return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
    }

    /// <summary>The n-th (by reference) confirmed simulated booking on or after a date: reference, flight, date and total.</summary>
    public (string Reference, string FlightNo, DateTime Date, decimal Total)? GetSimulatedConfirmedAt(int index, DateTime fromDate)
    {
        lock (_lock)
        {
            using var con = OpenConnection();
            using var cmd = Cmd(con,
                "SELECT reference, flight_no, flight_date, total_amount FROM bookings WHERE is_simulated = 1 AND status = 'CONFIRMED' " +
                "AND flight_date >= @d ORDER BY reference LIMIT 1 OFFSET @i", null, ("@d", fromDate.Date), ("@i", index));
            using var rs = cmd.ExecuteReader();
            return rs.Read() ? (Str(rs, "reference"), Str(rs, "flight_no"), Dt(rs, "flight_date"), Money(rs, "total_amount")) : null;
        }
    }

    /// <summary>References of confirmed simulated bookings in one cabin of a flight on a date.</summary>
    public List<string> LoadSimulatedConfirmedReferences(string flightNo, DateTime date, string className)
    {
        lock (_lock)
        {
            var list = new List<string>();
            using var con = OpenConnection();
            using var cmd = Cmd(con,
                "SELECT reference FROM bookings WHERE is_simulated = 1 AND status = 'CONFIRMED' AND flight_no = @f AND flight_date = @d " +
                "AND class_name = @c ORDER BY reference", null, ("@f", flightNo), ("@d", date.Date), ("@c", className));
            using var rs = cmd.ExecuteReader();
            while (rs.Read()) list.Add(Str(rs, "reference"));
            return list;
        }
    }

    /// <summary>How many demo seat holds are active at the given time.</summary>
    public int CountActiveSimulatedHolds(DateTime now)
    {
        lock (_lock)
        {
            using var con = OpenConnection();
            using var cmd = Cmd(con, "SELECT COUNT(*) FROM seat_holds WHERE owner LIKE 'SIM-%' AND status = 'ACTIVE' AND expires_at >= @now", null, ("@now", now));
            return Convert.ToInt32(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
        }
    }
}
