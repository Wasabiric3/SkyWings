using Microsoft.Data.Sqlite;

namespace SkyWings;

public sealed partial class BookingStore
{
    /// <summary>Turns a flight on or off for new bookings. Existing bookings are not touched.</summary>
    public void SetFlightActive(string flightNo, bool active)
    {
        lock (_lock)
        {
            try
            {
                using var con = OpenConnection();
                Exec(con, "UPDATE flights SET active = @active WHERE flight_no = @no",
                    ("@active", active ? 1 : 0), ("@no", flightNo));
            }
            catch (SqliteException e)
            {
                throw new IOException("Error updating flight status: " + e.Message, e);
            }
        }
    }
}
