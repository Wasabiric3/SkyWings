using Microsoft.Data.Sqlite;

namespace SkyWings;

/// <summary>
/// Customer email data: the address a booking's receipt goes to, and the opt-out list.
/// A person who is not on the list is subscribed; unsubscribing adds (or updates) a row keyed by address.
/// </summary>
public sealed partial class BookingStore
{
    private static void EnsureEmailSchema(SqliteConnection con)
    {
        Exec(con, "CREATE TABLE IF NOT EXISTS booking_contacts (" +
                  "booking_ref VARCHAR(20) PRIMARY KEY, email VARCHAR(254) NOT NULL, created_at DATETIME NOT NULL, " +
                  "FOREIGN KEY (booking_ref) REFERENCES bookings(reference) ON DELETE CASCADE)");
        Exec(con, "CREATE TABLE IF NOT EXISTS email_preferences (" +
                  "email VARCHAR(254) PRIMARY KEY, subscribed INTEGER NOT NULL DEFAULT 1, updated_at DATETIME NOT NULL)");
    }

    private static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    /// <summary>Creates or replaces the receipt address for a booking. The booking must already exist.</summary>
    public void SaveBookingContact(string bookingReference, string email)
    {
        if (!BookingRules.IsValidEmail(email)) throw new ArgumentException("That email address is not valid.");
        lock (_lock)
        {
            try
            {
                using var con = OpenConnection();
                Exec(con,
                    "INSERT INTO booking_contacts (booking_ref,email,created_at) VALUES (@ref,@email,@at) " +
                    "ON CONFLICT(booking_ref) DO UPDATE SET email=excluded.email",
                    ("@ref", bookingReference), ("@email", NormalizeEmail(email)), ("@at", DateTime.UtcNow));
            }
            catch (SqliteException e) { throw new IOException("Database error saving the email address: " + e.Message, e); }
        }
    }

    /// <summary>The receipt address for a booking, or null when none was given.</summary>
    public string? LoadBookingContact(string bookingReference)
    {
        lock (_lock)
        {
            try
            {
                using var con = OpenConnection();
                using var cmd = Cmd(con, "SELECT email FROM booking_contacts WHERE booking_ref=@ref", null, ("@ref", bookingReference));
                return cmd.ExecuteScalar() as string;
            }
            catch (Exception e) { LogError(e); throw; }
        }
    }

    public void DeleteBookingContact(string bookingReference)
    {
        lock (_lock)
        {
            using var con = OpenConnection();
            Exec(con, "DELETE FROM booking_contacts WHERE booking_ref=@ref", ("@ref", bookingReference));
        }
    }

    /// <summary>True unless the address has been unsubscribed.</summary>
    public bool IsEmailSubscribed(string email)
    {
        lock (_lock)
        {
            try
            {
                using var con = OpenConnection();
                using var cmd = Cmd(con, "SELECT subscribed FROM email_preferences WHERE email=@email", null,
                    ("@email", NormalizeEmail(email)));
                object? value = cmd.ExecuteScalar();
                return value is null || Convert.ToInt32(value) != 0;
            }
            catch (Exception e) { LogError(e); throw; }
        }
    }

    public void SetEmailSubscribed(string email, bool subscribed)
    {
        lock (_lock)
        {
            try
            {
                using var con = OpenConnection();
                Exec(con,
                    "INSERT INTO email_preferences (email,subscribed,updated_at) VALUES (@email,@sub,@at) " +
                    "ON CONFLICT(email) DO UPDATE SET subscribed=excluded.subscribed, updated_at=excluded.updated_at",
                    ("@email", NormalizeEmail(email)), ("@sub", subscribed ? 1 : 0), ("@at", DateTime.UtcNow));
            }
            catch (SqliteException e) { throw new IOException("Database error saving the email preference: " + e.Message, e); }
        }
    }
}
