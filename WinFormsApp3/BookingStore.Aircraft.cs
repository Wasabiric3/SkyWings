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
