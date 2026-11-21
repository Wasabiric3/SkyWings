using Microsoft.Data.Sqlite;

namespace SkyWings;

public sealed partial class BookingStore
{
    private static int ExecFeature(SqliteConnection con, SqliteTransaction tx, string sql,
        params (string Name, object? Value)[] args)
    {
        using var cmd = Cmd(con, sql, tx, args);
        return cmd.ExecuteNonQuery();
    }

    private static void EnsureFeatureSchema(SqliteConnection con)
    {
        string[] ddl =
        {
            "CREATE TABLE IF NOT EXISTS booking_legs (booking_ref VARCHAR(20) NOT NULL, leg_order INTEGER NOT NULL, flight_no VARCHAR(10) NOT NULL, origin VARCHAR(60) NOT NULL, destination VARCHAR(60) NOT NULL, flight_date DATE NOT NULL, domestic INTEGER NOT NULL, realtime_est VARCHAR(80) NOT NULL, PRIMARY KEY (booking_ref, leg_order), FOREIGN KEY (booking_ref) REFERENCES bookings(reference) ON DELETE CASCADE)",
            "CREATE TABLE IF NOT EXISTS booking_options (booking_ref VARCHAR(20) PRIMARY KEY, baggage_option VARCHAR(60) NOT NULL DEFAULT 'Standard', meal_option VARCHAR(60) NOT NULL DEFAULT 'Standard meal', promo_code VARCHAR(40) NOT NULL DEFAULT '', profile_ref VARCHAR(36) NOT NULL DEFAULT '', FOREIGN KEY (booking_ref) REFERENCES bookings(reference) ON DELETE CASCADE)",
            "CREATE TABLE IF NOT EXISTS passenger_profiles (reference VARCHAR(36) PRIMARY KEY, name VARCHAR(120) NOT NULL, address VARCHAR(200) NOT NULL, contact VARCHAR(30) NOT NULL, age INTEGER NOT NULL DEFAULT 0, birthday VARCHAR(12) NOT NULL DEFAULT '', id_number VARCHAR(40) NOT NULL, senior INTEGER NOT NULL DEFAULT 0, pwd INTEGER NOT NULL DEFAULT 0, loyalty_number VARCHAR(32) NOT NULL UNIQUE, miles INTEGER NOT NULL DEFAULT 0, created_at DATETIME NOT NULL)",
            "CREATE TABLE IF NOT EXISTS loyalty_transactions (id INTEGER PRIMARY KEY AUTOINCREMENT, profile_ref VARCHAR(36) NOT NULL, booking_ref VARCHAR(20) NOT NULL, miles INTEGER NOT NULL, created_at DATETIME NOT NULL, UNIQUE(profile_ref, booking_ref))",
            "CREATE TABLE IF NOT EXISTS promo_codes (code VARCHAR(40) PRIMARY KEY, discount_type VARCHAR(16) NOT NULL, discount_value DECIMAL(10,2) NOT NULL, starts_on DATE NOT NULL, ends_on DATE NOT NULL, minimum_total DECIMAL(10,2) NOT NULL DEFAULT 0, active INTEGER NOT NULL DEFAULT 1)",
            "CREATE TABLE IF NOT EXISTS promo_redemptions (id INTEGER PRIMARY KEY AUTOINCREMENT, code VARCHAR(40) NOT NULL, booking_ref VARCHAR(20) NOT NULL UNIQUE, profile_ref VARCHAR(36) NOT NULL DEFAULT '', redeemed_at DATETIME NOT NULL)",
            "CREATE TABLE IF NOT EXISTS waitlist_entries (id INTEGER PRIMARY KEY AUTOINCREMENT, booking_ref VARCHAR(20) NOT NULL, flight_no VARCHAR(10) NOT NULL, flight_date DATE NOT NULL, added_at DATETIME NOT NULL, status VARCHAR(16) NOT NULL DEFAULT 'WAITING', UNIQUE(booking_ref, flight_no, flight_date))",
            "CREATE TABLE IF NOT EXISTS check_ins (id INTEGER PRIMARY KEY AUTOINCREMENT, booking_ref VARCHAR(20) NOT NULL, passenger_name VARCHAR(120) NOT NULL, flight_no VARCHAR(10) NOT NULL, flight_date DATE NOT NULL, seat_code VARCHAR(8) NOT NULL DEFAULT '', checked_in_at DATETIME NOT NULL, UNIQUE(booking_ref, passenger_name, flight_no, flight_date))",
            "CREATE INDEX IF NOT EXISTS idx_waitlist_flight ON waitlist_entries(flight_no, flight_date, status, added_at)",
            "CREATE INDEX IF NOT EXISTS idx_checkins_booking ON check_ins(booking_ref, flight_no, flight_date)"
        };

        foreach (string sql in ddl) Exec(con, sql);
        EnsureEmailSchema(con);
        EnsureRefundRequestSchema(con);
        EnsureWaitlistSchema(con);
        EnsureColumn(con, "ALTER TABLE bookings ADD COLUMN profile_ref VARCHAR(36) NOT NULL DEFAULT ''");
        EnsureColumn(con, "ALTER TABLE bookings ADD COLUMN promo_code VARCHAR(40) NOT NULL DEFAULT ''");
        EnsureColumn(con, "ALTER TABLE bookings ADD COLUMN waitlisted INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(con, "ALTER TABLE passengers ADD COLUMN return_seat_code VARCHAR(8) NOT NULL DEFAULT ''");
        Exec(con, "INSERT OR IGNORE INTO promo_codes (code,discount_type,discount_value,starts_on,ends_on,minimum_total,active) VALUES ('SKY10','PERCENT',10,'2025-01-01','2035-12-31',0,1)");
        Exec(con, "INSERT OR IGNORE INTO promo_codes (code,discount_type,discount_value,starts_on,ends_on,minimum_total,active) VALUES ('EARLY500','FIXED',500,'2025-01-01','2035-12-31',5000,1)");
    }

    public PassengerProfile SavePassengerProfile(PassengerProfile profile)
    {
        string reference = string.IsNullOrWhiteSpace(profile.Reference) ? Guid.NewGuid().ToString("N") : profile.Reference;
        string loyaltyNumber = string.IsNullOrWhiteSpace(profile.LoyaltyNumber)
            ? "SW" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant()
            : profile.LoyaltyNumber;
        lock (_lock)
        {
            try
            {
                using var con = OpenConnection();
                Exec(con,
                    "INSERT INTO passenger_profiles (reference,name,address,contact,age,birthday,id_number,senior,pwd,loyalty_number,miles,created_at) VALUES (@ref,@name,@address,@contact,@age,@birthday,@id,@senior,@pwd,@loyalty,@miles,@created) ON CONFLICT(reference) DO UPDATE SET name=excluded.name,address=excluded.address,contact=excluded.contact,age=excluded.age,birthday=excluded.birthday,id_number=excluded.id_number,senior=excluded.senior,pwd=excluded.pwd",
                    ("@ref", reference), ("@name", profile.Name.Trim()), ("@address", profile.Address.Trim()),
                    ("@contact", profile.Contact.Trim()), ("@age", profile.Age), ("@birthday", profile.Birthday),
                    ("@id", profile.IdNumber.Trim()), ("@senior", profile.Senior ? 1 : 0), ("@pwd", profile.Pwd ? 1 : 0),
                    ("@loyalty", loyaltyNumber), ("@miles", profile.Miles), ("@created", DateTime.UtcNow));
                return profile with { Reference = reference, LoyaltyNumber = loyaltyNumber };
            }
            catch (SqliteException e) { throw new IOException("Database error saving passenger profile: " + e.Message, e); }
        }
    }

    public List<PassengerProfile> LoadPassengerProfiles()
    {
        lock (_lock)
        {
            var profiles = new List<PassengerProfile>();
            try
            {
                using var con = OpenConnection();
                using var cmd = Cmd(con, "SELECT * FROM passenger_profiles ORDER BY name COLLATE NOCASE");
                using var rs = cmd.ExecuteReader();
                while (rs.Read())
                    profiles.Add(new PassengerProfile(Str(rs, "reference"), Str(rs, "name"), Str(rs, "address"),
                        Str(rs, "contact"), Int(rs, "age"), Str(rs, "birthday"), Str(rs, "id_number"),
                        Int(rs, "senior") == 1, Int(rs, "pwd") == 1, Str(rs, "loyalty_number"), Long(rs, "miles")));
            }
            catch (Exception e) { LogError(e); throw; }
            return profiles;
        }
    }

    public void DeletePassengerProfile(string reference)
    {
        lock (_lock)
        {
            using var con = OpenConnection();
            Exec(con, "DELETE FROM passenger_profiles WHERE reference=@ref", ("@ref", reference));
        }
    }

    public WaitlistEntry AddToWaitlist(string bookingReference, string flightNo, DateTime flightDate)
    {
        lock (_lock)
        {
            using var con = OpenConnection();
            Exec(con, "INSERT OR IGNORE INTO waitlist_entries (booking_ref,flight_no,flight_date,added_at,status) VALUES (@ref,@flight,@date,@at,'WAITING')",
                ("@ref", bookingReference), ("@flight", flightNo), ("@date", flightDate.Date), ("@at", DateTime.UtcNow));
            using var cmd = Cmd(con, "SELECT id, added_at, status FROM waitlist_entries WHERE booking_ref=@ref AND flight_no=@flight AND flight_date=@date",
                null, ("@ref", bookingReference), ("@flight", flightNo), ("@date", flightDate.Date));
            using var rs = cmd.ExecuteReader();
            if (!rs.Read()) throw new IOException("Unable to create waitlist entry.");
            long id = Long(rs, "id");
            DateTime added = Dt(rs, "added_at");
            string status = Str(rs, "status");
            rs.Close();
            using var position = Cmd(con, "SELECT COUNT(*) FROM waitlist_entries WHERE flight_no=@flight AND flight_date=@date AND status='WAITING' AND (added_at < @at OR (added_at=@at AND id<=@id))",
                null, ("@flight", flightNo), ("@date", flightDate.Date), ("@at", added), ("@id", id));
            return new WaitlistEntry(Convert.ToInt64(position.ExecuteScalar()), bookingReference, flightNo, flightDate.Date, added, status);
        }
    }

    public List<WaitlistEntry> LoadWaitlist(string flightNo, DateTime flightDate)
    {
        lock (_lock)
        {
            var entries = new List<WaitlistEntry>();
            using var con = OpenConnection();
            using var cmd = Cmd(con, "SELECT id,booking_ref,flight_no,flight_date,added_at,status FROM waitlist_entries WHERE flight_no=@flight AND flight_date=@date AND status='WAITING' ORDER BY added_at,id",
                null, ("@flight", flightNo), ("@date", flightDate.Date));
            using var rs = cmd.ExecuteReader();
            long position = 1;
            while (rs.Read()) entries.Add(new WaitlistEntry(position++, Str(rs, "booking_ref"), Str(rs, "flight_no"), Dt(rs, "flight_date"), Dt(rs, "added_at"), Str(rs, "status")));
            return entries;
        }
    }

    public bool MarkWaitlistEntry(string bookingReference, string status)
    {
        lock (_lock)
        {
            using var con = OpenConnection();
            return Exec(con, "UPDATE waitlist_entries SET status=@status WHERE booking_ref=@ref AND status='WAITING'",
                ("@status", status), ("@ref", bookingReference)) > 0;
        }
    }

    public bool IsCheckInOpen(string bookingReference, string flightNo, DateTime flightDate, DateTime now)
    {
        lock (_lock)
        {
            using var con = OpenConnection();
            using var cmd = Cmd(con, "SELECT status,payment_status,flight_no,flight_date FROM bookings WHERE reference=@ref",
                null, ("@ref", bookingReference));
            using var rs = cmd.ExecuteReader();
            if (!rs.Read() || Str(rs, "status") != AppData.StatusConfirmed || Str(rs, "payment_status") != AppData.PaymentPaid) return false;
            bool primaryMatch = Str(rs, "flight_no") == flightNo && Dt(rs, "flight_date").Date == flightDate.Date;
            rs.Close();
            bool legMatch = primaryMatch;
            if (!legMatch)
            {
                using var leg = Cmd(con, "SELECT 1 FROM booking_legs WHERE booking_ref=@ref AND flight_no=@flight AND flight_date=@date LIMIT 1",
                    null, ("@ref", bookingReference), ("@flight", flightNo), ("@date", flightDate.Date));
                legMatch = leg.ExecuteScalar() is not null;
            }
            if (!legMatch) return false;
            TimeOnly departureTime = AppData.FlightScheduledTimes.GetValueOrDefault(flightNo, new TimeOnly(8, 0));
            DateTime departure = flightDate.Date.Add(departureTime.ToTimeSpan());
            return now >= AppData.CheckInOpensAt(departure) && now < departure;
        }
    }

    public bool CheckInPassenger(string bookingReference, string passengerName, string flightNo,
        DateTime flightDate, string seatCode, DateTime now)
    {
        if (!IsCheckInOpen(bookingReference, flightNo, flightDate, now)) return false;
        lock (_lock)
        {
            using var con = OpenConnection();
            return Exec(con, "INSERT OR IGNORE INTO check_ins (booking_ref,passenger_name,flight_no,flight_date,seat_code,checked_in_at) VALUES (@ref,@name,@flight,@date,@seat,@at)",
                ("@ref", bookingReference), ("@name", passengerName), ("@flight", flightNo),
                ("@date", flightDate.Date), ("@seat", seatCode), ("@at", now)) > 0;
        }
    }

    public List<CheckInRecord> LoadCheckIns(string bookingReference)
    {
        lock (_lock)
        {
            var records = new List<CheckInRecord>();
            using var con = OpenConnection();
            using var cmd = Cmd(con, "SELECT * FROM check_ins WHERE booking_ref=@ref ORDER BY flight_date,passenger_name", null, ("@ref", bookingReference));
            using var rs = cmd.ExecuteReader();
            while (rs.Read()) records.Add(new CheckInRecord(Str(rs, "booking_ref"), Str(rs, "passenger_name"),
                Str(rs, "flight_no"), Dt(rs, "flight_date"), Str(rs, "seat_code"), Dt(rs, "checked_in_at")));
            return records;
        }
    }

    /// <summary>
    /// Every check-in, grouped by booking reference, in one query (for showing many bookings' trip status at once).
    /// With <paramref name="includeSimulated"/> false, simulated bookings' check-ins are left out.
    /// </summary>
    public Dictionary<string, List<CheckInRecord>> LoadCheckInsByBooking(bool includeSimulated = true)
        => LoadCheckInsGrouped(includeSimulated
            ? "SELECT * FROM check_ins"
            : "SELECT * FROM check_ins WHERE booking_ref IN (SELECT reference FROM bookings WHERE is_simulated = 0)");

    /// <summary>The check-ins (both legs) of the bookings whose outbound flight is on <paramref name="flightDate"/>, by reference.</summary>
    public Dictionary<string, List<CheckInRecord>> LoadCheckInsForDate(DateTime flightDate)
        => LoadCheckInsGrouped("SELECT * FROM check_ins WHERE booking_ref IN (SELECT reference FROM bookings WHERE flight_date = @d)",
            ("@d", flightDate.Date));

    private Dictionary<string, List<CheckInRecord>> LoadCheckInsGrouped(string sql, params (string, object?)[] args)
    {
        lock (_lock)
        {
            var result = new Dictionary<string, List<CheckInRecord>>(StringComparer.Ordinal);
            try
            {
                using var con = OpenConnection();
                using var cmd = Cmd(con, sql, null, args);
                using var rs = cmd.ExecuteReader();
                while (rs.Read())
                {
                    var record = new CheckInRecord(Str(rs, "booking_ref"), Str(rs, "passenger_name"), Str(rs, "flight_no"),
                        Dt(rs, "flight_date"), Str(rs, "seat_code"), Dt(rs, "checked_in_at"));
                    if (!result.TryGetValue(record.BookingReference, out List<CheckInRecord>? list))
                        result[record.BookingReference] = list = new List<CheckInRecord>();
                    list.Add(record);
                }
            }
            catch (SqliteException e)
            {
                LogError(e);
                throw new IOException("Error loading check-ins: " + e.Message, e);
            }
            return result;
        }
    }

    public bool ValidatePromoCode(string code, DateTime date, decimal subtotal, out decimal discount)
    {
        discount = 0;
        if (string.IsNullOrWhiteSpace(code)) return false;
        lock (_lock)
        {
            using var con = OpenConnection();
            using var cmd = Cmd(con, "SELECT discount_type,discount_value,minimum_total FROM promo_codes WHERE code=@code AND active=1 AND starts_on<=@date AND ends_on>=@date",
                null, ("@code", code.Trim().ToUpperInvariant()), ("@date", date.Date));
            using var rs = cmd.ExecuteReader();
            if (!rs.Read() || subtotal < Money(rs, "minimum_total")) return false;
            decimal value = Money(rs, "discount_value");
            discount = Str(rs, "discount_type") == "PERCENT" ? subtotal * value / 100m : value;
            discount = Math.Round(Math.Clamp(discount, 0, subtotal), 2);
            return discount > 0;
        }
    }

    public void RecordPromoRedemption(string code, string bookingReference, string profileReference)
    {
        lock (_lock)
        {
            using var con = OpenConnection();
            Exec(con, "INSERT OR IGNORE INTO promo_redemptions (code,booking_ref,profile_ref,redeemed_at) VALUES (@code,@ref,@profile,@at)",
                ("@code", code.Trim().ToUpperInvariant()), ("@ref", bookingReference), ("@profile", profileReference), ("@at", DateTime.UtcNow));
        }
    }

    public bool RecordLoyaltyMiles(string profileReference, string bookingReference, long miles)
    {
        if (miles <= 0) return false;
        lock (_lock)
        {
            using var con = OpenConnection();
            using var tx = con.BeginTransaction();
            int inserted = Exec(con, "INSERT OR IGNORE INTO loyalty_transactions (profile_ref,booking_ref,miles,created_at) VALUES (@profile,@booking,@miles,@at)",
                ("@profile", profileReference), ("@booking", bookingReference), ("@miles", miles), ("@at", DateTime.UtcNow));
            if (inserted > 0) Exec(con, "UPDATE passenger_profiles SET miles=miles+@miles WHERE reference=@profile",
                ("@miles", miles), ("@profile", profileReference));
            tx.Commit();
            return inserted > 0;
        }
    }

    public bool RedeemLoyaltyMiles(string profileReference, long miles)
    {
        if (miles <= 0) return false;
        lock (_lock)
        {
            using var con = OpenConnection();
            return Exec(con, "UPDATE passenger_profiles SET miles=miles-@miles WHERE reference=@profile AND miles>=@miles",
                ("@miles", miles), ("@profile", profileReference)) > 0;
        }
    }

    public void SaveBookingFeatures(BookingRecord booking)
    {
        lock (_lock)
        {
            using var con = OpenConnection();
            using var tx = con.BeginTransaction();
            ExecFeature(con, tx, "DELETE FROM booking_legs WHERE booking_ref=@ref", ("@ref", booking.Reference));
            if (!string.IsNullOrWhiteSpace(booking.ReturnFlightNo))
                ExecFeature(con, tx, "INSERT INTO booking_legs (booking_ref,leg_order,flight_no,origin,destination,flight_date,domestic,realtime_est) VALUES (@ref,2,@flight,@origin,@destination,@date,@domestic,@est)",
                    ("@ref", booking.Reference), ("@flight", booking.ReturnFlightNo), ("@origin", booking.ReturnOrigin ?? ""),
                    ("@destination", booking.ReturnDestination ?? ""), ("@date", booking.ReturnFlightDateValue?.Date ?? booking.FlightDateValue),
                    ("@domestic", booking.ReturnDomestic ? 1 : 0), ("@est", "Scheduled"));
            ExecFeature(con, tx, "INSERT INTO booking_options (booking_ref,baggage_option,meal_option,promo_code,profile_ref) VALUES (@ref,@baggage,@meal,@promo,@profile) ON CONFLICT(booking_ref) DO UPDATE SET baggage_option=excluded.baggage_option,meal_option=excluded.meal_option,promo_code=excluded.promo_code,profile_ref=excluded.profile_ref",
                ("@ref", booking.Reference), ("@baggage", booking.BaggageOption), ("@meal", booking.MealOption),
                ("@promo", booking.PromoCode), ("@profile", booking.ProfileReference));
            tx.Commit();
        }
    }
}
    