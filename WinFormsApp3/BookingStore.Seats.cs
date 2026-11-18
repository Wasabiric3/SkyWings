using Microsoft.Data.Sqlite;

namespace SkyWings;

public sealed partial class BookingStore
{
    /// <summary>
    /// Seats already assigned on confirmed bookings for a flight and date (outbound or return leg),
    /// normalized with <see cref="SeatLayout.Normalize"/>.
    /// </summary>
    public HashSet<string> LoadTakenSeats(string flightNo, DateTime flightDate)
    {
        lock (_lock)
        {
            try
            {
                using var con = OpenConnection();
                return LoadTakenSeatsCore(con, flightNo, flightDate);
            }
            catch (SqliteException e)
            {
                LogError(e);
                throw new IOException("Unable to load taken seats: " + e.Message, e);
            }
        }
    }

    private static HashSet<string> LoadTakenSeatsCore(SqliteConnection con, string flightNo, DateTime flightDate)
    {
        var taken = new HashSet<string>(StringComparer.Ordinal);

        using (var outbound = Cmd(con,
            "SELECT p.seat_code FROM passengers p JOIN bookings b ON b.reference = p.booking_ref " +
            "WHERE b.status = 'CONFIRMED' AND b.flight_no = @no AND b.flight_date = @date AND p.seat_code <> ''",
            null, ("@no", flightNo), ("@date", flightDate.Date)))
        using (var rs = outbound.ExecuteReader())
            while (rs.Read()) taken.Add(SeatLayout.Normalize(rs.GetString(0)));

        using (var inbound = Cmd(con,
            "SELECT p.return_seat_code FROM passengers p " +
            "JOIN bookings b ON b.reference = p.booking_ref " +
            "JOIN booking_legs l ON l.booking_ref = b.reference " +
            "WHERE b.status = 'CONFIRMED' AND l.flight_no = @no AND l.flight_date = @date AND p.return_seat_code <> ''",
            null, ("@no", flightNo), ("@date", flightDate.Date)))
        using (var rs = inbound.ExecuteReader())
            while (rs.Read()) taken.Add(SeatLayout.Normalize(rs.GetString(0)));

        taken.Remove("");
        return taken;
    }

    /// <summary>
    /// Free seats in one cabin of a flight on one date: the cabin's seats minus seats already booked and seats
    /// currently held by someone who is booking. (Unlike <see cref="LoadFlightInventory"/>, this is per date and per cabin.)
    /// </summary>
    public int CabinSeatsLeft(string flightNo, DateTime flightDate, string className)
        => CabinSeatsLeft(flightNo, flightDate, className, null);

    /// <summary>Seats not booked or held, ignoring waitlist offers.</summary>
    private int CabinSeatsRaw(string flightNo, DateTime flightDate, string className)
    {
        int capacity = LoadFlightInventory(flightNo).Capacity;
        int total = SeatLayout.RowCount(className, capacity) * AppData.ClassSeatLetters[SeatLayout.ClassIndex(className)].Length;
        string prefix = AppData.SeatPrefixForClass(className);
        int taken = LoadTakenSeats(flightNo, flightDate).Count(s => s.StartsWith(prefix, StringComparison.Ordinal));
        int held = GetActiveHoldsForFlightDate(flightNo, flightDate).Keys
            .Count(seat => SeatLayout.Normalize(seat).StartsWith(prefix, StringComparison.Ordinal));
        return Math.Max(0, total - taken - held);
    }
}
