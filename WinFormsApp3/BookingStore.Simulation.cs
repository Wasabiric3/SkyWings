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

    /// <summary>Whether the demo activity (simulated bookings, holds and cancellations) runs. Off unless a staff member switches it on.</summary>
    public bool DemoActivityEnabled => GetSetting(SettingDemoActivity, "0") == "1";

    /// <summary>Whether staff reports include simulated bookings. Off by default so real numbers stay clean.</summary>
    public bool IncludeSimulatedInReports => GetSetting(SettingDemoInReports, "0") == "1";

    /// <summary>" WHERE is_simulated = 0" for a query without a WHERE, or "" when reports include demo bookings.</summary>
    private string ReportWhere(string alias = "") => IncludeSimulatedInReports ? "" : $" WHERE {alias}is_simulated = 0";

    /// <summary>" AND is_simulated = 0" for a query that already has a WHERE, or "" when reports include demo bookings.</summary>
    private string ReportAnd(string alias = "") => IncludeSimulatedInReports ? "" : $" AND {alias}is_simulated = 0";

    // ------------------------------------------------------------------ simulated bookings

    /// <summary>
    /// Stores a generated booking. Writes only the booking and its passengers (and return leg): no payment record, promo, loyalty,
    /// contact, email or audit entry, so a demo booking has no side effects outside the seat map and the board.
    /// </summary>
    /// <exception cref="ArgumentException">The reference does not start with <c>SIM-</c>: real bookings can never be flagged by accident.</exception>
    public void SaveSimulatedBooking(BookingRecord booking)
    {
        if (!booking.Reference.StartsWith(SimulatedPrefix, StringComparison.Ordinal))
            throw new ArgumentException("A simulated booking's reference must start with " + SimulatedPrefix, nameof(booking));
        lock (_lock)
        {
            try
            {
                using var con = OpenConnection();
                using var tx = con.BeginTransaction();
                Exec(con, tx,
                    "INSERT OR REPLACE INTO bookings (reference,booked_at,flight_no,origin,destination,flight_date," +
                    "domestic,class_name,payment_method,masked_account,total_amount,realtime_est,status," +
                    "payment_status,payment_txn_ref,payment_confirmed_at,refund_amount,is_simulated) " +
                    "VALUES (@reference,@booked_at,@flight_no,@origin,@destination,@flight_date," +
                    "@domestic,@class_name,@payment_method,@masked_account,@total_amount,@realtime_est,@status," +
                    "@payment_status,@payment_txn_ref,@payment_confirmed_at,@refund_amount,1)",
                    ("@reference", booking.Reference), ("@booked_at", booking.BookedAtValue),
                    ("@flight_no", booking.FlightNo), ("@origin", booking.Origin), ("@destination", booking.Destination),
                    ("@flight_date", booking.FlightDateValue.Date), ("@domestic", booking.Domestic ? 1 : 0),
                    ("@class_name", booking.ClassName), ("@payment_method", booking.PaymentMethod),
                    ("@masked_account", booking.MaskedAccount), ("@total_amount", booking.TotalAmount),
                    ("@realtime_est", booking.RealtimeEst), ("@status", booking.Status),
                    ("@payment_status", booking.PaymentStatus),
                    ("@payment_txn_ref", booking.PaymentTransactionRef.Length == 0 ? null : booking.PaymentTransactionRef),
                    ("@payment_confirmed_at", booking.PaymentConfirmedAtValue),
                    ("@refund_amount", booking.RefundAmount));

                Exec(con, tx, "DELETE FROM passengers WHERE booking_ref = @ref", ("@ref", booking.Reference));
                foreach (var p in booking.Passengers)
                    Exec(con, tx,
                        "INSERT INTO passengers (booking_ref,name,address,contact,age,birthday," +
                        "id_number,senior,pwd,seat_code,return_seat_code,addons,total_amount) " +
                        "VALUES (@ref,@name,@address,@contact,@age,@birthday,@id,@senior,@pwd,@seat,@returnSeat,@addons,@total)",
                        ("@ref", booking.Reference), ("@name", p.Name), ("@address", p.Address),
                        ("@contact", p.Contact), ("@age", p.Age), ("@birthday", p.Birthday), ("@id", p.IdNumber),
                        ("@senior", p.Senior ? 1 : 0), ("@pwd", p.Pwd ? 1 : 0), ("@seat", p.SeatCode),
                        ("@returnSeat", p.ReturnSeatCode), ("@addons", string.Join(",", p.Addons)), ("@total", p.TotalAmount));

                Exec(con, tx, "DELETE FROM booking_legs WHERE booking_ref = @ref", ("@ref", booking.Reference));
                if (!string.IsNullOrWhiteSpace(booking.ReturnFlightNo))
                    Exec(con, tx,
                        "INSERT INTO booking_legs (booking_ref,leg_order,flight_no,origin,destination,flight_date,domestic,realtime_est) " +
                        "VALUES (@ref,2,@flight,@origin,@destination,@date,@domestic,@est)",
                        ("@ref", booking.Reference), ("@flight", booking.ReturnFlightNo),
                        ("@origin", booking.ReturnOrigin ?? ""), ("@destination", booking.ReturnDestination ?? ""),
                        ("@date", booking.ReturnFlightDateValue?.Date ?? booking.FlightDateValue),
                        ("@domestic", booking.ReturnDomestic ? 1 : 0), ("@est", "Scheduled"));
                tx.Commit();
            }
            catch (SqliteException e)
            {
                throw new IOException("Error saving a simulated booking: " + e.Message, e);
            }
        }
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
                string holdScope = flightNo is null ? "" : " AND flight_no = @no";
                Exec(con, tx, $"UPDATE seat_holds SET status = 'RELEASED' WHERE owner LIKE 'SIM-%' AND status = 'ACTIVE'{holdScope}", args);
                tx.Commit();
                return removed;
            }
            catch (SqliteException e)
            {
                throw new IOException("Error removing simulated bookings: " + e.Message, e);
            }
        }
    }
}
