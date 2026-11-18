using Microsoft.Data.Sqlite;

namespace SkyWings;

public sealed partial class BookingStore
{
    /// <summary>
    /// The aircraft (and so the cabins and seat maps) of a flight. A flight with no aircraft, an unknown aircraft id or no row
    /// at all gets the original capacity-based layout, so older databases keep working unchanged.
    /// </summary>
    public Aircraft LoadAircraft(string flightNo)
    {
        lock (_lock)
        {
            try
            {
                using var con = OpenConnection();
                using var cmd = Cmd(con, "SELECT aircraft, capacity FROM flights WHERE flight_no = @no", null, ("@no", flightNo));
                using var rs = cmd.ExecuteReader();
                if (!rs.Read()) return Aircraft.Legacy(AppData.DefaultDomesticCapacity);
                string? id = rs.IsDBNull(0) ? null : rs.GetString(0);
                return Aircraft.Find(id) ?? Aircraft.Legacy(Int(rs, "capacity"));
            }
            catch (Exception e) { LogError(e); throw; }
        }
    }


    /// <summary>True when the flights table exists and has at least one flight.</summary>
    public bool HasFlights()
    {
        lock (_lock)
        {
            try
            {
                using var con = OpenConnection();
                using var cmd = Cmd(con, "SELECT 1 FROM flights LIMIT 1");
                using var rs = cmd.ExecuteReader();
                return rs.Read();
            }
            catch (SqliteException) { return false; }   // no flights table yet: a brand-new database
        }
    }

    /// <summary>
    /// Seeds the standard schedule. A brand-new database gets realistic aircraft (and so wide-body seat maps); a database that
    /// already has flights keeps its existing layouts, and anything new is added the same way it always was.
    /// </summary>
    public void SeedDefaultFlights()
        => SeedFlightsIfEmpty(AppData.DomesticFlights, AppData.InternationalFlights,
            AppData.DefaultDomesticCapacity, AppData.DefaultInternationalCapacity, assignAircraft: !HasFlights());

    /// <summary>
    /// Every seat code assigned on confirmed bookings of a flight, on any date (outbound seats and return-leg seats), normalized.
    /// Used to check that a change of aircraft would not strand a booked seat. <paramref name="realOnly"/> leaves out demo bookings.
    /// </summary>
    public HashSet<string> LoadBookedSeatCodes(string flightNo, bool realOnly = false)
    {
        lock (_lock)
        {
            try
            {
                var codes = new HashSet<string>(StringComparer.Ordinal);
                using var con = OpenConnection();
                using (var outbound = Cmd(con,
                    "SELECT p.seat_code FROM passengers p JOIN bookings b ON b.reference = p.booking_ref " +
                    "WHERE b.status = 'CONFIRMED' AND b.flight_no = @no AND p.seat_code <> ''" + (realOnly ? " AND b.is_simulated = 0" : ""), null, ("@no", flightNo)))
                using (var rs = outbound.ExecuteReader())
                    while (rs.Read()) codes.Add(SeatLayout.Normalize(rs.GetString(0)));
                using (var inbound = Cmd(con,
                    "SELECT p.return_seat_code FROM passengers p JOIN bookings b ON b.reference = p.booking_ref " +
                    "JOIN booking_legs l ON l.booking_ref = b.reference " +
                    "WHERE b.status = 'CONFIRMED' AND l.flight_no = @no AND p.return_seat_code <> ''" + (realOnly ? " AND b.is_simulated = 0" : ""), null, ("@no", flightNo)))
                using (var rs = inbound.ExecuteReader())
                    while (rs.Read()) codes.Add(SeatLayout.Normalize(rs.GetString(0)));
                codes.Remove("");
                return codes;
            }
            catch (SqliteException e)
            {
                LogError(e);
                throw new IOException("Unable to load booked seats: " + e.Message, e);
            }
        }
    }

    /// <summary>
    /// Sets or clears a flight's aircraft. Setting one also sets the flight's capacity to the aircraft's seat count; clearing it
    /// leaves the capacity as it is. Callers check that existing seat assignments still exist on the new aircraft.
    /// </summary>
    public void SetFlightAircraft(string flightNo, string? aircraftId)
    {
        lock (_lock)
        {
            try
            {
                using var con = OpenConnection();
                Aircraft? aircraft = Aircraft.Find(aircraftId);
                if (aircraft is null)
                    Exec(con, "UPDATE flights SET aircraft = @ac WHERE flight_no = @no",
                        ("@ac", aircraftId is null ? DBNull.Value : aircraftId), ("@no", flightNo));
                else
                    Exec(con, "UPDATE flights SET aircraft = @ac, capacity = @cap WHERE flight_no = @no",
                        ("@ac", aircraft.Id), ("@cap", aircraft.TotalSeats), ("@no", flightNo));
            }
            catch (SqliteException e)
            {
                throw new IOException("Error saving the flight's aircraft: " + e.Message, e);
            }
        }
    }
}
