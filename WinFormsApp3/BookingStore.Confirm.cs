using Microsoft.Data.Sqlite;

namespace SkyWings;

public sealed partial class BookingStore
{
    /// <summary>
    /// Saves a confirmed, paid booking together with its legs, options, promo redemption and payment record.
    /// Everything is written in one transaction, so a failure leaves no partial booking behind.
    /// This only ever INSERTS a new booking: if the reference already exists it fails instead of overwriting
    /// somebody else's booking.
    /// </summary>
    public void SaveConfirmedBooking(BookingRecord booking, bool promoRedeemed, decimal paymentAmount,
        string paymentMethod, string maskedAccount, string transactionRef, DateTime paidAt)
    {
        BookingRules.Validate(booking);
        lock (_lock)
        {
            try
            {
                using var con = OpenConnection();
                using var tx = con.BeginTransaction();

                // 1. Booking header (insert, or update when the reference already exists).
                Exec(con, tx,
                    "INSERT INTO bookings (reference,booked_at,flight_no,origin,destination,flight_date," +
                    "domestic,class_name,payment_method,masked_account,total_amount,realtime_est,status," +
                    "payment_status,payment_txn_ref,payment_confirmed_at,refund_amount) " +
                    "VALUES (@reference,@booked_at,@flight_no,@origin,@destination,@flight_date," +
                    "@domestic,@class_name,@payment_method,@masked_account,@total_amount,@realtime_est,@status," +
                    "@payment_status,@payment_txn_ref,@payment_confirmed_at,@refund_amount)",
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

                // 2. Passengers (replace the set on every save).
                Exec(con, tx, "DELETE FROM passengers WHERE booking_ref = @ref", ("@ref", booking.Reference));
                foreach (var p in booking.Passengers)
                {
                    Exec(con, tx,
                        "INSERT INTO passengers (booking_ref,name,address,contact,age,birthday," +
                        "id_number,senior,pwd,seat_code,return_seat_code,addons,total_amount) " +
                        "VALUES (@ref,@name,@address,@contact,@age,@birthday,@id,@senior,@pwd,@seat,@returnSeat,@addons,@total)",
                        ("@ref", booking.Reference), ("@name", p.Name), ("@address", p.Address),
                        ("@contact", p.Contact), ("@age", p.Age), ("@birthday", p.Birthday), ("@id", p.IdNumber),
                        ("@senior", p.Senior ? 1 : 0), ("@pwd", p.Pwd ? 1 : 0), ("@seat", p.SeatCode),
                        ("@returnSeat", p.ReturnSeatCode), ("@addons", string.Join(",", p.Addons)),
                        ("@total", p.TotalAmount));
                }

                // 3. Return leg (booking_legs).
                Exec(con, tx, "DELETE FROM booking_legs WHERE booking_ref = @ref", ("@ref", booking.Reference));
                if (!string.IsNullOrWhiteSpace(booking.ReturnFlightNo))
                {
                    Exec(con, tx,
                        "INSERT INTO booking_legs (booking_ref,leg_order,flight_no,origin,destination,flight_date,domestic,realtime_est) " +
                        "VALUES (@ref,2,@flight,@origin,@destination,@date,@domestic,@est)",
                        ("@ref", booking.Reference), ("@flight", booking.ReturnFlightNo),
                        ("@origin", booking.ReturnOrigin ?? ""), ("@destination", booking.ReturnDestination ?? ""),
                        ("@date", booking.ReturnFlightDateValue?.Date ?? booking.FlightDateValue),
                        ("@domestic", booking.ReturnDomestic ? 1 : 0), ("@est", "Scheduled"));
                }

                // 4. Options (baggage, meal, promo, profile).
                Exec(con, tx,
                    "INSERT INTO booking_options (booking_ref,baggage_option,meal_option,promo_code,profile_ref) " +
                    "VALUES (@ref,@baggage,@meal,@promo,@profile) ON CONFLICT(booking_ref) DO UPDATE SET " +
                    "baggage_option=excluded.baggage_option,meal_option=excluded.meal_option," +
                    "promo_code=excluded.promo_code,profile_ref=excluded.profile_ref",
                    ("@ref", booking.Reference), ("@baggage", booking.BaggageOption), ("@meal", booking.MealOption),
                    ("@promo", booking.PromoCode), ("@profile", booking.ProfileReference));

                // 5. Promo redemption (only when the discount was actually applied).
                if (promoRedeemed && !string.IsNullOrWhiteSpace(booking.PromoCode))
                {
                    Exec(con, tx,
                        "INSERT OR IGNORE INTO promo_redemptions (code,booking_ref,profile_ref,redeemed_at) VALUES (@code,@ref,@profile,@at)",
                        ("@code", booking.PromoCode.Trim().ToUpperInvariant()), ("@ref", booking.Reference),
                        ("@profile", booking.ProfileReference), ("@at", DateTime.UtcNow));
                }

                // 6. Payment attempt.
                Exec(con, tx,
                    "INSERT INTO booking_payments (booking_ref,amount,method,account_masked,status,transaction_ref,detail_message,created_at) " +
                    "VALUES (@ref,@amount,@method,@acct,'SUCCESS',@txn,@detail,@created)",
                    ("@ref", booking.Reference), ("@amount", paymentAmount), ("@method", paymentMethod),
                    ("@acct", maskedAccount), ("@txn", transactionRef), ("@detail", "Demo payment approved"),
                    ("@created", paidAt));

                tx.Commit();
            }
            catch (SqliteException e) when (e.SqliteErrorCode == 19 && e.Message.Contains("bookings.reference", StringComparison.Ordinal))
            {
                throw new IOException("That booking reference is already in use. Please try again.", e);
            }
            catch (SqliteException e)
            {
                throw new IOException("Database error saving confirmed booking: " + e.Message, e);
            }
        }
    }

    /// <summary>Runs a non-query statement inside an existing transaction.</summary>
    private static int Exec(SqliteConnection con, SqliteTransaction tx, string sql, params (string Name, object? Value)[] args)
    {
        using var cmd = Cmd(con, sql, tx, args);
        return cmd.ExecuteNonQuery();
    }
}
