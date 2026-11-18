using System.Data;
using System.Globalization;
using Microsoft.Data.Sqlite;

namespace SkyWings;

/// <summary>
/// SQLite data access layer. Port of the BookingStore inner class (JDBC -> Microsoft.Data.Sqlite).
/// Every public method takes the same lock the Java "synchronized" methods did.
/// </summary>
public sealed partial class BookingStore
{
    private readonly string _connectionString;
    private readonly object _lock = new();

    public BookingStore() : this(AppConfig.BuildConnectionString())
    {
    }

    /// <summary>The connection string this store uses (needed by backup and restore).</summary>
    public string ConnectionString => _connectionString;

    /// <summary>Opens a store on an explicit connection string (used by tests with a temporary database).</summary>
    public BookingStore(string connectionString)
    {
        _connectionString = connectionString;
        InitSchema();
    }

    public FlightInventory LoadFlightInventory(string flightNo)
    {
        lock (_lock)
        {
            try
            {
                using var con = OpenConnection();
                using var capacity = Cmd(con, "SELECT capacity FROM flights WHERE flight_no = @no", null, ("@no", flightNo));
                int total = Convert.ToInt32(capacity.ExecuteScalar() ?? 0);
                using var bookings = Cmd(con, "SELECT COUNT(*) FROM bookings WHERE flight_no = @no AND status = 'CONFIRMED'", null, ("@no", flightNo));
                int confirmed = Convert.ToInt32(bookings.ExecuteScalar() ?? 0);
                using var holds = Cmd(con, "SELECT COUNT(*) FROM seat_holds WHERE flight_no = @no AND status = 'ACTIVE' AND expires_at > @now", null,
                    ("@no", flightNo), ("@now", DateTime.Now));
                int held = Convert.ToInt32(holds.ExecuteScalar() ?? 0);
                return new FlightInventory(total, confirmed, held);
            }
            catch (Exception e)
            {
                LogError(e);
                // Surface the failure instead of reporting "0 seats" (which looked like a sold-out flight).
                throw new IOException("Unable to load seat inventory: " + e.Message, e);
            }
        }
    }

    private SqliteConnection OpenConnection()
    {
        var con = new SqliteConnection(_connectionString);
        con.Open();
        return con;
    }

    // ---------- small helpers ----------
    private static SqliteCommand Cmd(SqliteConnection con, string sql, SqliteTransaction? tx = null,
        params (string Name, object? Value)[] args)
    {
        var cmd = new SqliteCommand(sql, con, tx);
        foreach (var (name, value) in args)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return cmd;
    }

    private static int Exec(SqliteConnection con, string sql, params (string, object?)[] args)
    {
        using var cmd = Cmd(con, sql, null, args);
        return cmd.ExecuteNonQuery();
    }

    private static string Str(SqliteDataReader r, string col)
    {
        int i = r.GetOrdinal(col);
        return r.IsDBNull(i) ? "" : r.GetString(i);
    }

    private static string? StrOrNull(SqliteDataReader r, string col)
    {
        int i = r.GetOrdinal(col);
        return r.IsDBNull(i) ? null : r.GetString(i);
    }

    private static int Int(SqliteDataReader r, string col) => Convert.ToInt32(r.GetValue(r.GetOrdinal(col)));
    private static long Long(SqliteDataReader r, string col) => Convert.ToInt64(r.GetValue(r.GetOrdinal(col)));

    /// <summary>Reads a money column. SQLite stores amounts as REAL/NUMERIC; they are converted to decimal here.</summary>
    private static decimal Money(SqliteDataReader r, string col)
    {
        int i = r.GetOrdinal(col);
        return r.IsDBNull(i) ? 0m : Convert.ToDecimal(r.GetValue(i), CultureInfo.InvariantCulture);
    }

    private static DateTime Dt(SqliteDataReader r, string col) => r.GetDateTime(r.GetOrdinal(col));

    private static void LogError(Exception e) => AppLogger.Error("BookingStore", e);

    // ================================================================
    // SCHEMA
    // ================================================================
    private void InitSchema()
    {
        string[] ddl =
        {
            "CREATE TABLE IF NOT EXISTS bookings (" +
            "  reference      VARCHAR(20)   PRIMARY KEY," +
            "  booked_at      DATETIME      NOT NULL," +
            "  flight_no      VARCHAR(10)   NOT NULL," +
            "  origin         VARCHAR(60)   NOT NULL," +
            "  destination    VARCHAR(60)   NOT NULL," +
            "  flight_date    DATE          NOT NULL," +
            "  domestic       INTEGER       NOT NULL," +
            "  class_name     VARCHAR(30)   NOT NULL," +
            "  payment_method VARCHAR(40)   NOT NULL," +
            "  masked_account VARCHAR(30)   NOT NULL," +
            "  total_amount   DECIMAL(10,2) NOT NULL," +
            "  realtime_est   VARCHAR(80)   NOT NULL," +
            "  status         VARCHAR(15)   NOT NULL," +
            "  payment_status VARCHAR(20)   NOT NULL DEFAULT 'UNPAID'," +
            "  payment_txn_ref VARCHAR(40)  NULL," +
            "  payment_confirmed_at DATETIME NULL" +
            ")",
            "CREATE TABLE IF NOT EXISTS passengers (" +
            "  id           INTEGER PRIMARY KEY AUTOINCREMENT," +
            "  booking_ref  VARCHAR(20)   NOT NULL," +
            "  name         VARCHAR(120)  NOT NULL," +
            "  address      VARCHAR(200)  NOT NULL," +
            "  contact      VARCHAR(20)   NOT NULL," +
            "  age          INT           NOT NULL," +
            "  birthday     VARCHAR(12)   NOT NULL," +
            "  id_number    VARCHAR(40)   NOT NULL," +
            "  senior       INTEGER       NOT NULL," +
            "  pwd          INTEGER       NOT NULL," +
            "  seat_code    VARCHAR(8)    NOT NULL," +
            "  addons       VARCHAR(300)  NOT NULL," +
            "  total_amount DECIMAL(10,2) NOT NULL," +
            "  FOREIGN KEY (booking_ref) REFERENCES bookings(reference) ON DELETE CASCADE" +
            ")",
            "CREATE TABLE IF NOT EXISTS seat_holds (" +
            "  id          VARCHAR(36)  PRIMARY KEY," +
            "  flight_no   VARCHAR(10)  NOT NULL," +
            "  flight_date DATE         NOT NULL," +
            "  seat_code   VARCHAR(8)   NOT NULL," +
            "  owner       VARCHAR(36)  NOT NULL," +
            "  expires_at  DATETIME     NOT NULL," +
            "  status      VARCHAR(20)  NOT NULL DEFAULT 'ACTIVE'" +
            ")",
            "CREATE TABLE IF NOT EXISTS booking_payments (" +
            "  id              INTEGER PRIMARY KEY AUTOINCREMENT," +
            "  booking_ref     VARCHAR(20)   NOT NULL," +
            "  amount          DECIMAL(10,2) NOT NULL," +
            "  method          VARCHAR(60)   NOT NULL," +
            "  account_masked  VARCHAR(40)   NOT NULL," +
            "  status          VARCHAR(20)   NOT NULL," +
            "  transaction_ref VARCHAR(40)   NULL," +
            "  detail_message  VARCHAR(255)  NULL," +
            "  created_at      DATETIME      NOT NULL" +
            ")",
            "CREATE TABLE IF NOT EXISTS schema_migrations (version INTEGER PRIMARY KEY, applied_at DATETIME NOT NULL)"
        };

        try
        {
            using var con = OpenConnection();
            foreach (var sql in ddl) Exec(con, sql);
            EnsureBookingColumns(con);
            EnsureFeatureSchema(con);
            EnsureSecuritySchema(con);
            Exec(con, "CREATE INDEX IF NOT EXISTS idx_bookings_flight_date ON bookings(flight_no, flight_date, status)");
            Exec(con, "CREATE INDEX IF NOT EXISTS idx_bookings_status ON bookings(status, booked_at)");
            Exec(con, "CREATE INDEX IF NOT EXISTS idx_passengers_booking_ref ON passengers(booking_ref)");
            Exec(con, "CREATE INDEX IF NOT EXISTS idx_seat_holds_lookup ON seat_holds(flight_no, flight_date, seat_code, status, expires_at)");
            Exec(con, "INSERT OR IGNORE INTO schema_migrations(version, applied_at) VALUES (1, @at)", ("@at", DateTime.UtcNow));
        }
        catch (SqliteException e)
        {
            MessageBox.Show(
                "Cannot open SQLite database.\n" + e.Message +
                "\n\nCheck the database path in db.properties.",
                "Database Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void EnsureBookingColumns(SqliteConnection con)
    {
        EnsureColumn(con, "ALTER TABLE bookings ADD COLUMN payment_status VARCHAR(20) NOT NULL DEFAULT 'UNPAID'");
        EnsureColumn(con, "ALTER TABLE bookings ADD COLUMN payment_txn_ref VARCHAR(40) NULL");
        EnsureColumn(con, "ALTER TABLE bookings ADD COLUMN payment_confirmed_at DATETIME NULL");
        EnsureColumn(con, "ALTER TABLE bookings ADD COLUMN refund_amount DECIMAL(10,2) NOT NULL DEFAULT 0");
        // NOTE: on a brand-new database the flights table does not exist yet (SeedFlightsIfEmpty creates it
        // with this column already), so error 1146 is ignored here. The Java version would show a
        // "Database Error" popup on the very first run in that case.
        EnsureColumn(con, "ALTER TABLE flights ADD COLUMN departure_time TIME NOT NULL DEFAULT '08:00:00'");
    }

    private static void EnsureColumn(SqliteConnection con, string alterSql)
    {
        try
        {
            Exec(con, alterSql);
        }
        catch (SqliteException)
        {
            // already there / table will be created later
        }
    }

    // ================================================================
    // BOOKINGS
    // ================================================================
    private sealed record BookingHeader(
        string Reference, DateTime BookedAt, string FlightNo, string Origin, string Destination,
        DateTime FlightDate, bool Domestic, string ClassName, string PaymentMethod, string MaskedAccount,
        decimal TotalAmount, string RealtimeEst, string Status, string? PaymentStatus,
        string? PaymentTxnRef, DateTime? PaymentConfirmedAt, decimal RefundAmount);

    private static BookingHeader ReadHeader(SqliteDataReader rs)
    {
        int pc = rs.GetOrdinal("payment_confirmed_at");
        return new BookingHeader(
            Str(rs, "reference"),
            Dt(rs, "booked_at"),
            Str(rs, "flight_no"),
            Str(rs, "origin"),
            Str(rs, "destination"),
            Dt(rs, "flight_date"),
            Int(rs, "domestic") == 1,
            Str(rs, "class_name"),
            Str(rs, "payment_method"),
            Str(rs, "masked_account"),
            Money(rs, "total_amount"),
            Str(rs, "realtime_est"),
            Str(rs, "status"),
            StrOrNull(rs, "payment_status"),
            StrOrNull(rs, "payment_txn_ref"),
            rs.IsDBNull(pc) ? null : rs.GetDateTime(pc),
            Money(rs, "refund_amount"));
    }

    // SQLite commands cannot run while a reader is open on the same connection,
    // so headers are read first and passengers are loaded afterwards.
    private BookingRecord BuildBookingRecord(SqliteConnection con, BookingHeader h)
    {
        var passengers = LoadPassengers(con, h.Reference);

        // Return leg and extras live in their own tables; older (seeded) bookings simply have no rows there.
        string? returnNo = null, returnOrigin = null, returnDestination = null;
        bool returnDomestic = false;
        DateTime? returnDate = null;
        using (var cmd = Cmd(con,
            "SELECT flight_no, origin, destination, flight_date, domestic FROM booking_legs WHERE booking_ref = @ref AND leg_order = 2",
            null, ("@ref", h.Reference)))
        using (var rs = cmd.ExecuteReader())
            if (rs.Read())
            {
                returnNo = Str(rs, "flight_no");
                returnOrigin = Str(rs, "origin");
                returnDestination = Str(rs, "destination");
                returnDate = Dt(rs, "flight_date");
                returnDomestic = Int(rs, "domestic") == 1;
            }

        string baggage = "Standard", meal = "Standard meal", promo = "", profile = "";
        using (var cmd = Cmd(con,
            "SELECT baggage_option, meal_option, promo_code, profile_ref FROM booking_options WHERE booking_ref = @ref",
            null, ("@ref", h.Reference)))
        using (var rs = cmd.ExecuteReader())
            if (rs.Read())
            {
                baggage = Str(rs, "baggage_option");
                meal = Str(rs, "meal_option");
                promo = Str(rs, "promo_code");
                profile = Str(rs, "profile_ref");
            }

        return new BookingRecord(h.Reference, h.BookedAt, h.FlightNo, h.Origin, h.Destination, h.FlightDate, h.Domestic,
            h.ClassName, h.PaymentMethod, h.MaskedAccount, h.TotalAmount, h.RealtimeEst,
            passengers, h.Status, h.PaymentStatus, h.PaymentTxnRef, h.PaymentConfirmedAt, h.RefundAmount)
        {
            ReturnFlightNo = returnNo, ReturnOrigin = returnOrigin, ReturnDestination = returnDestination,
            ReturnDomestic = returnDomestic, ReturnFlightDateValue = returnDate,
            BaggageOption = baggage, MealOption = meal, PromoCode = promo, ProfileReference = profile
        };
    }

    public List<BookingRecord> LoadBookings()
    {
        lock (_lock)
        {
            var list = new List<BookingRecord>();
            try
            {
                using var con = OpenConnection();
                var headers = new List<BookingHeader>();
                using (var cmd = Cmd(con, "SELECT * FROM bookings ORDER BY booked_at DESC"))
                using (var rs = cmd.ExecuteReader())
                    while (rs.Read()) headers.Add(ReadHeader(rs));
                foreach (var h in headers) list.Add(BuildBookingRecord(con, h));
            }
            catch (Exception e) { LogError(e); throw; }
            return list;
        }
    }

    private static List<PassengerRecord> LoadPassengers(SqliteConnection con, string bookingRef)
    {
        var list = new List<PassengerRecord>();
        using var cmd = Cmd(con, "SELECT * FROM passengers WHERE booking_ref = @ref", null, ("@ref", bookingRef));
        using var rs = cmd.ExecuteReader();
        while (rs.Read())
        {
            string addonsStr = Str(rs, "addons");
            var addons = addonsStr.Length == 0 ? new List<string>() : new List<string>(addonsStr.Split(','));
            string seat = Str(rs, "seat_code");
            SeatAssignment? sa = seat.Length == 0 ? null : new SeatAssignment("", seat);
            list.Add(new PassengerRecord(
                Str(rs, "name"), Str(rs, "address"), Str(rs, "contact"), Int(rs, "age"),
                Str(rs, "birthday"), Str(rs, "id_number"),
                Int(rs, "senior") == 1, Int(rs, "pwd") == 1,
                sa, addons, Money(rs, "total_amount"), Str(rs, "return_seat_code")));
        }
        return list;
    }

    public void UpsertBooking(BookingRecord booking)
    {
        const string upsertSql =
            "INSERT INTO bookings (reference,booked_at,flight_no,origin,destination,flight_date," +
            "domestic,class_name,payment_method,masked_account,total_amount,realtime_est,status," +
            "payment_status,payment_txn_ref,payment_confirmed_at,refund_amount) " +
            "VALUES (@reference,@booked_at,@flight_no,@origin,@destination,@flight_date," +
            "@domestic,@class_name,@payment_method,@masked_account,@total_amount,@realtime_est,@status," +
            "@payment_status,@payment_txn_ref,@payment_confirmed_at,@refund_amount) " +
            "ON CONFLICT(reference) DO UPDATE SET " +
            "booked_at=excluded.booked_at,flight_no=excluded.flight_no," +
            "origin=excluded.origin,destination=excluded.destination," +
            "flight_date=excluded.flight_date,domestic=excluded.domestic," +
            "class_name=excluded.class_name,payment_method=excluded.payment_method," +
            "masked_account=excluded.masked_account,total_amount=excluded.total_amount," +
            "realtime_est=excluded.realtime_est,status=excluded.status," +
            "payment_status=excluded.payment_status,payment_txn_ref=excluded.payment_txn_ref," +
            "payment_confirmed_at=excluded.payment_confirmed_at," +
            "refund_amount=excluded.refund_amount";

        lock (_lock)
        {
            try
            {
                using var con = OpenConnection();
                using var tx = con.BeginTransaction();

                using (var ps = Cmd(con, upsertSql, tx,
                    ("@reference", booking.Reference),
                    ("@booked_at", booking.BookedAtValue),
                    ("@flight_no", booking.FlightNo),
                    ("@origin", booking.Origin),
                    ("@destination", booking.Destination),
                    ("@flight_date", booking.FlightDateValue.Date),
                    ("@domestic", booking.Domestic ? 1 : 0),
                    ("@class_name", booking.ClassName),
                    ("@payment_method", booking.PaymentMethod),
                    ("@masked_account", booking.MaskedAccount),
                    ("@total_amount", booking.TotalAmount),
                    ("@realtime_est", booking.RealtimeEst),
                    ("@status", booking.Status),
                    ("@payment_status", booking.PaymentStatus),
                    ("@payment_txn_ref", booking.PaymentTransactionRef.Length == 0 ? null : booking.PaymentTransactionRef),
                    ("@payment_confirmed_at", booking.PaymentConfirmedAtValue),
                    ("@refund_amount", booking.RefundAmount)))
                {
                    ps.ExecuteNonQuery();
                }

                using (var del = Cmd(con, "DELETE FROM passengers WHERE booking_ref = @ref", tx,
                           ("@ref", booking.Reference)))
                {
                    del.ExecuteNonQuery();
                }

                const string insPax =
                    "INSERT INTO passengers (booking_ref,name,address,contact,age,birthday," +
                    "id_number,senior,pwd,seat_code,return_seat_code,addons,total_amount) " +
                    "VALUES (@ref,@name,@address,@contact,@age,@birthday,@id,@senior,@pwd,@seat,@returnSeat,@addons,@total)";
                foreach (var p in booking.Passengers)
                {
                    using var pp = Cmd(con, insPax, tx,
                        ("@ref", booking.Reference),
                        ("@name", p.Name),
                        ("@address", p.Address),
                        ("@contact", p.Contact),
                        ("@age", p.Age),
                        ("@birthday", p.Birthday),
                        ("@id", p.IdNumber),
                        ("@senior", p.Senior ? 1 : 0),
                        ("@pwd", p.Pwd ? 1 : 0),
                        ("@seat", p.SeatCode),
                        ("@returnSeat", p.ReturnSeatCode),
                        ("@addons", string.Join(",", p.Addons)),
                        ("@total", p.TotalAmount));
                    pp.ExecuteNonQuery();
                }
                tx.Commit();
            }
            catch (SqliteException e)
            {
                throw new IOException("Database error saving booking: " + e.Message, e);
            }
        }
    }

    public void RecordPaymentAttempt(string bookingReference, decimal amount, string method,
        string maskedAccount, string status, string? transactionRef, DateTime createdAt, string? detailMessage)
    {
        const string sql =
            "INSERT INTO booking_payments " +
            "(booking_ref,amount,method,account_masked,status,transaction_ref,detail_message,created_at) " +
            "VALUES (@ref,@amount,@method,@acct,@status,@txn,@detail,@created)";
        lock (_lock)
        {
            try
            {
                using var con = OpenConnection();
                using var cmd = Cmd(con, sql, null,
                    ("@ref", bookingReference), ("@amount", amount), ("@method", method),
                    ("@acct", maskedAccount), ("@status", status), ("@txn", transactionRef),
                    ("@detail", detailMessage), ("@created", createdAt));
                cmd.ExecuteNonQuery();
            }
            catch (SqliteException e)
            {
                throw new IOException("Database error saving payment attempt: " + e.Message, e);
            }
        }
    }

    public BookingRecord? FindByReference(string reference)
    {
        lock (_lock)
        {
            try
            {
                using var con = OpenConnection();
                BookingHeader? header = null;
                using (var cmd = Cmd(con, "SELECT * FROM bookings WHERE reference = @ref", null, ("@ref", reference)))
                using (var rs = cmd.ExecuteReader())
                    if (rs.Read()) header = ReadHeader(rs);
                return header == null ? null : BuildBookingRecord(con, header);
            }
            catch (Exception e) { LogError(e); throw; }
        }
    }

    public BookingRecord? UpdateStatus(string reference, string status) => UpdateStatus(reference, status, 0m);

    public BookingRecord? UpdateStatus(string reference, string status, decimal refundAmount)
    {
        lock (_lock)
        {
            try
            {
                using var con = OpenConnection();
                using var cmd = Cmd(con,
                    "UPDATE bookings SET status = @status, refund_amount = @refund WHERE reference = @ref", null,
                    ("@status", status), ("@refund", refundAmount), ("@ref", reference));
                cmd.ExecuteNonQuery();
            }
            catch (SqliteException e)
            {
                throw new IOException("Database error updating status: " + e.Message, e);
            }
        }
        return FindByReference(reference);
    }

    // ================================================================
    // FLIGHTS
    // ================================================================
    public void SeedFlightsIfEmpty(IEnumerable<Flight> domestic, IEnumerable<Flight> international,
        int domCap, int intlCap)
    {
        const string ddl =
            "CREATE TABLE IF NOT EXISTS flights (" +
            "  flight_no      VARCHAR(10)   PRIMARY KEY," +
            "  origin         VARCHAR(60)   NOT NULL," +
            "  destination    VARCHAR(60)   NOT NULL," +
            "  base_price     DECIMAL(10,2) NOT NULL," +
            "  domestic       INTEGER       NOT NULL," +
            "  capacity       INT           NOT NULL," +
            "  active         INTEGER       NOT NULL DEFAULT 1," +
            "  departure_time TIME          NOT NULL DEFAULT '08:00:00'" +
            ")";
        // INSERT OR IGNORE skips rows whose flight_no already exists, so this is safe on every startup.
        const string ins =
            "INSERT OR IGNORE INTO flights (flight_no,origin,destination,base_price,domestic,capacity,active,departure_time) " +
            "VALUES (@no,@origin,@dest,@price,@dom,@cap,1,@dep)";

        lock (_lock)
        {
            try
            {
                using var con = OpenConnection();
                Exec(con, ddl);
                void Insert(Flight f, int dom, int cap)
                {
                    var dep = AppData.FlightScheduledTimes.TryGetValue(f.FlightNo, out var t) ? t : new TimeOnly(8, 0);
                    Exec(con, ins, ("@no", f.FlightNo), ("@origin", f.Origin), ("@dest", f.Destination),
                        ("@price", f.BasePrice), ("@dom", dom), ("@cap", cap),
                        ("@dep", dep.ToString("HH:mm:ss", CultureInfo.InvariantCulture)));
                }
                foreach (var f in domestic)
                {
                    Insert(f, 1, domCap);
                    Insert(AppData.CreateReverseFlight(f, true), 1, domCap);
                }
                foreach (var f in international)
                {
                    Insert(f, 0, intlCap);
                    Insert(AppData.CreateReverseFlight(f, false), 0, intlCap);
                }
                // Extra daily departures on domestic and Southeast Asia routes (INSERT OR IGNORE, so existing
                // databases simply gain them at the next start and nothing already there is changed).
                foreach (var f in AppData.ExtraDomesticFlights)
                {
                    Insert(f, 1, domCap);
                    Insert(AppData.CreateReverseFlight(f, true), 1, domCap);
                }
                foreach (var f in AppData.ExtraInternationalFlights)
                {
                    Insert(f, 0, intlCap);
                    Insert(AppData.CreateReverseFlight(f, false), 0, intlCap);
                }
                // Long-haul routes are advertised on the home screen, so they must exist to be bookable.
                foreach (var f in AppData.EuropeFlights)
                {
                    Insert(f, 0, AppData.DefaultEuropeCapacity);
                    Insert(AppData.CreateReverseFlight(f, false), 0, AppData.DefaultEuropeCapacity);
                }
                foreach (var f in AppData.NorthAmericaFlights)
                {
                    Insert(f, 0, AppData.DefaultNorthAmericaCapacity);
                    Insert(AppData.CreateReverseFlight(f, false), 0, AppData.DefaultNorthAmericaCapacity);
                }
            }
            catch (SqliteException e)
            {
                throw new IOException("Error seeding flights: " + e.Message, e);
            }
        }
    }

    public List<Flight> LoadActiveFlights(bool domestic)
    {
        lock (_lock)
        {
            var list = new List<Flight>();
            try
            {
                using var con = OpenConnection();
                using var cmd = Cmd(con,
                    "SELECT flight_no, origin, destination, base_price, domestic FROM flights " +
                    "WHERE domestic = @dom AND active = 1 ORDER BY flight_no", null, ("@dom", domestic ? 1 : 0));
                using var rs = cmd.ExecuteReader();
                while (rs.Read())
                    list.Add(new Flight(Str(rs, "flight_no"), Str(rs, "origin"), Str(rs, "destination"), Money(rs, "base_price"))
                    {
                        Domestic = Int(rs, "domestic") == 1
                    });
            }
            catch (Exception e) { LogError(e); throw; }
            return list;
        }
    }

    public List<FlightAdminRow> LoadAllFlights()
    {
        lock (_lock)
        {
            var list = new List<FlightAdminRow>();
            try
            {
                using var con = OpenConnection();
                using var cmd = Cmd(con,
                    "SELECT flight_no, origin, destination, base_price, domestic, capacity, active, departure_time " +
                    "FROM flights ORDER BY domestic DESC, flight_no");
                using var rs = cmd.ExecuteReader();
                while (rs.Read())
                {
                    int ti = rs.GetOrdinal("departure_time");
                    string depDisplay = rs.IsDBNull(ti)
                        ? "08:00 AM"
                        : DateTime.Today.Add(rs.GetTimeSpan(ti)).ToString("h:mm tt", CultureInfo.InvariantCulture);
                    list.Add(new FlightAdminRow(
                        Str(rs, "flight_no"), Str(rs, "origin"), Str(rs, "destination"), Money(rs, "base_price"),
                        Int(rs, "domestic") == 1 ? "Domestic" : "International",
                        Int(rs, "capacity"),
                        Int(rs, "active") == 1 ? "Active" : "Inactive",
                        depDisplay));
                }
            }
            catch (Exception e) { LogError(e); throw; }
            return list;
        }
    }

    public Dictionary<string, int> LoadFlightCapacities()
    {
        lock (_lock)
        {
            var map = new Dictionary<string, int>();
            try
            {
                using var con = OpenConnection();
                using var cmd = Cmd(con, "SELECT flight_no, capacity FROM flights WHERE active = 1");
                using var rs = cmd.ExecuteReader();
                while (rs.Read()) map[Str(rs, "flight_no")] = Int(rs, "capacity");
            }
            catch (Exception e) { LogError(e); throw; }
            return map;
        }
    }

    public bool FlightExists(string flightNo)
    {
        lock (_lock)
        {
            try
            {
                using var con = OpenConnection();
                using var cmd = Cmd(con, "SELECT 1 FROM flights WHERE flight_no = @no LIMIT 1", null, ("@no", flightNo));
                using var rs = cmd.ExecuteReader();
                return rs.Read();
            }
            catch (Exception e) { LogError(e); throw; }
        }
    }

    public Dictionary<string, TimeOnly> LoadFlightDepartureTimes()
    {
        lock (_lock)
        {
            var map = new Dictionary<string, TimeOnly>();
            try
            {
                using var con = OpenConnection();
                using var cmd = Cmd(con, "SELECT flight_no, departure_time FROM flights WHERE active = 1");
                using var rs = cmd.ExecuteReader();
                while (rs.Read())
                {
                    int ti = rs.GetOrdinal("departure_time");
                    if (!rs.IsDBNull(ti))
                        map[Str(rs, "flight_no")] = TimeOnly.FromTimeSpan(rs.GetTimeSpan(ti));
                }
            }
            catch (Exception e) { LogError(e); throw; }
            return map;
        }
    }

    public void SaveFlight(string flightNo, string origin, string destination, decimal basePrice,
        bool domestic, int capacity, bool active, TimeOnly? departureTime)
    {
        const string sql =
            "INSERT INTO flights (flight_no,origin,destination,base_price,domestic,capacity,active,departure_time) " +
            "VALUES (@no,@origin,@dest,@price,@dom,@cap,@active,@dep) " +
            "ON CONFLICT(flight_no) DO UPDATE SET origin=excluded.origin, destination=excluded.destination, " +
            "base_price=excluded.base_price, domestic=excluded.domestic, " +
            "capacity=excluded.capacity, active=excluded.active, departure_time=excluded.departure_time";
        lock (_lock)
        {
            try
            {
                using var con = OpenConnection();
                var dep = (departureTime ?? new TimeOnly(8, 0)).ToString("HH:mm:ss", CultureInfo.InvariantCulture);
                Exec(con, sql, ("@no", flightNo), ("@origin", origin), ("@dest", destination),
                    ("@price", basePrice), ("@dom", domestic ? 1 : 0), ("@cap", capacity),
                    ("@active", active ? 1 : 0), ("@dep", dep));
            }
            catch (SqliteException e)
            {
                throw new IOException("Error saving flight: " + e.Message, e);
            }
        }
    }

    public int CountActiveBookingsForFlight(string flightNo)
    {
        lock (_lock)
        {
            try
            {
                using var con = OpenConnection();
                using var cmd = Cmd(con,
                    "SELECT COUNT(*) FROM bookings WHERE flight_no = @no AND status = 'CONFIRMED'", null, ("@no", flightNo));
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
            catch (Exception e) { LogError(e); throw; }
        }
    }

    public void DeactivateFlight(string flightNo)
    {
        lock (_lock)
        {
            try
            {
                using var con = OpenConnection();
                Exec(con, "UPDATE flights SET active = 0 WHERE flight_no = @no", ("@no", flightNo));
            }
            catch (SqliteException e)
            {
                throw new IOException("Error deactivating flight: " + e.Message, e);
            }
        }
    }

    // ================================================================
    // REPORTS
    // ================================================================
    /// <summary>Returns null if the query fails (same as the Java version).</summary>
    public ReportStats? LoadReportStats()
    {
        lock (_lock)
        {
            var result = new ReportStats();
            try
            {
                using var con = OpenConnection();

                const string sumSql =
                    "SELECT COUNT(*) AS total," +
                    " SUM(CASE WHEN status='CONFIRMED' THEN 1 ELSE 0 END) AS confirmed," +
                    " SUM(CASE WHEN status='CANCELED'  THEN 1 ELSE 0 END) AS cancelled," +
                    " SUM(CASE WHEN status='CONFIRMED' THEN total_amount ELSE 0 END) AS revenue," +
                    " SUM(CASE WHEN status='CANCELED'  THEN refund_amount ELSE 0 END) AS refunded" +
                    " FROM bookings";
                using (var cmd = Cmd(con, sumSql))
                using (var rs = cmd.ExecuteReader())
                {
                    if (rs.Read())
                    {
                        result.TotalBookings = Long(rs, "total");
                        result.ConfirmedCount = rs.IsDBNull(rs.GetOrdinal("confirmed")) ? 0 : Long(rs, "confirmed");
                        result.CancelledCount = rs.IsDBNull(rs.GetOrdinal("cancelled")) ? 0 : Long(rs, "cancelled");
                        result.TotalRevenue = Money(rs, "revenue");
                        result.TotalRefunded = Money(rs, "refunded");
                    }
                }

                const string routeSql =
                    "SELECT flight_no, origin, destination, COUNT(*) AS cnt" +
                    " FROM bookings WHERE status='CONFIRMED'" +
                    " GROUP BY flight_no, origin, destination ORDER BY cnt DESC LIMIT 10";
                using (var cmd = Cmd(con, routeSql))
                using (var rs = cmd.ExecuteReader())
                    while (rs.Read())
                        result.TopRoutes.Add(new RouteStat(Str(rs, "flight_no"), Str(rs, "origin"),
                            Str(rs, "destination"), Long(rs, "cnt")));

                const string classSql =
                    "SELECT class_name, COUNT(*) AS cnt, SUM(total_amount) AS rev" +
                    " FROM bookings WHERE status='CONFIRMED'" +
                    " GROUP BY class_name ORDER BY cnt DESC";
                using (var cmd = Cmd(con, classSql))
                using (var rs = cmd.ExecuteReader())
                    while (rs.Read())
                        result.ByClass.Add(new ClassStat(Str(rs, "class_name"), Long(rs, "cnt"), Money(rs, "rev")));
            }
            catch (Exception e)
            {
                LogError(e);
                return null;
            }
            return result;
        }
    }

    public List<CompletedBookingRow> LoadCompletedFlightBookings()
    {
        lock (_lock)
        {
            var rows = new List<CompletedBookingRow>();
            const string sql =
                "SELECT b.reference, b.flight_no, b.origin, b.destination," +
                " b.flight_date, b.class_name, b.total_amount, b.payment_status," +
                " COUNT(p.id) AS pax_count" +
                " FROM bookings b" +
                " LEFT JOIN passengers p ON p.booking_ref = b.reference" +
                " WHERE b.status = 'CONFIRMED' AND b.flight_date < date('now')" +
                " GROUP BY b.reference, b.flight_no, b.origin, b.destination," +
                " b.flight_date, b.class_name, b.total_amount, b.payment_status" +
                " ORDER BY b.flight_date DESC";
            try
            {
                using var con = OpenConnection();
                using var cmd = Cmd(con, sql);
                using var rs = cmd.ExecuteReader();
                while (rs.Read())
                    rows.Add(new CompletedBookingRow(
                        Str(rs, "reference"), Str(rs, "flight_no"),
                        Str(rs, "origin") + " → " + Str(rs, "destination"),
                        Dt(rs, "flight_date").ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        Str(rs, "class_name"), Long(rs, "pax_count"), Money(rs, "total_amount"),
                        Str(rs, "payment_status")));
            }
            catch (Exception e) { LogError(e); throw; }
            return rows;
        }
    }

    public List<CancelledBookingRow> LoadCancelledBookings()
    {
        lock (_lock)
        {
            var rows = new List<CancelledBookingRow>();
            const string sql =
                "SELECT b.reference, b.flight_no, b.origin, b.destination," +
                " b.flight_date, b.class_name, b.total_amount, b.payment_status," +
                " b.refund_amount, COUNT(p.id) AS pax_count" +
                " FROM bookings b" +
                " LEFT JOIN passengers p ON p.booking_ref = b.reference" +
                " WHERE b.status = 'CANCELED'" +
                " GROUP BY b.reference, b.flight_no, b.origin, b.destination," +
                " b.flight_date, b.class_name, b.total_amount, b.payment_status, b.refund_amount" +
                " ORDER BY b.flight_date DESC";
            try
            {
                using var con = OpenConnection();
                using var cmd = Cmd(con, sql);
                using var rs = cmd.ExecuteReader();
                while (rs.Read())
                    rows.Add(new CancelledBookingRow(
                        Str(rs, "reference"), Str(rs, "flight_no"),
                        Str(rs, "origin") + " → " + Str(rs, "destination"),
                        Dt(rs, "flight_date").ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        Str(rs, "class_name"), Long(rs, "pax_count"), Money(rs, "total_amount"),
                        Str(rs, "payment_status"), Money(rs, "refund_amount")));
            }
            catch (Exception e) { LogError(e); throw; }
            return rows;
        }
    }

    // ================================================================
    // SEAT HOLDS
    // ================================================================
    public void PurgeExpiredSeatHolds()
    {
        lock (_lock)
        {
            try
            {
                using var con = OpenConnection();
                Exec(con, "UPDATE seat_holds SET status='EXPIRED' WHERE expires_at < datetime('now') AND status='ACTIVE'");
            }
            catch (SqliteException e)
            {
                throw new IOException("Database error purging seat holds: " + e.Message, e);
            }
        }
    }

    public Dictionary<string, SeatHoldInfo> GetActiveHoldsForFlightDate(string flightNo, DateTime flightDate)
    {
        lock (_lock)
        {
            var map = new Dictionary<string, SeatHoldInfo>();
            try
            {
                using var con = OpenConnection();
                using var cmd = Cmd(con,
                    "SELECT seat_code, owner, expires_at FROM seat_holds " +
                    "WHERE flight_no = @no AND flight_date = @date AND expires_at >= datetime('now') AND status='ACTIVE'", null,
                    ("@no", flightNo), ("@date", flightDate.Date));
                using var rs = cmd.ExecuteReader();
                while (rs.Read())
                    map[Str(rs, "seat_code")] = new SeatHoldInfo(Str(rs, "owner"), Dt(rs, "expires_at"));
            }
            catch (Exception e) { LogError(e); throw; }
            return map;
        }
    }

    /// <summary>Returns false if the seat is already actively held by someone.</summary>
    public bool AddSeatHold(string flightNo, DateTime flightDate, string seatCode, string owner)
    {
        lock (_lock)
        {
            try
            {
                using var con = OpenConnection();
                using (var chk = Cmd(con,
                    "SELECT COUNT(*) FROM seat_holds " +
                    "WHERE flight_no=@no AND flight_date=@date AND seat_code=@seat AND expires_at >= datetime('now') AND status='ACTIVE'",
                    null, ("@no", flightNo), ("@date", flightDate.Date), ("@seat", seatCode)))
                {
                    if (Convert.ToInt32(chk.ExecuteScalar()) > 0) return false;
                }

                // One session may hold only so many seats at once, so nobody can reserve a whole cabin.
                using (var held = Cmd(con,
                    "SELECT COUNT(*) FROM seat_holds WHERE owner = @owner AND status = 'ACTIVE' AND expires_at >= datetime('now')",
                    null, ("@owner", owner)))
                    if (Convert.ToInt32(held.ExecuteScalar()) >= BookingRules.MaxActiveSeatHoldsPerOwner)
                        throw new InvalidOperationException(
                            $"You can hold at most {BookingRules.MaxActiveSeatHoldsPerOwner} seats at a time. Finish or cancel your current booking first.");

                // A seat that is already assigned on a confirmed booking can never be held again.
                if (LoadTakenSeatsCore(con, flightNo, flightDate).Contains(SeatLayout.Normalize(seatCode))) return false;

                // Mark any stale ACTIVE hold for this seat as EXPIRED before inserting
                Exec(con,
                    "UPDATE seat_holds SET status='EXPIRED' WHERE flight_no=@no AND flight_date=@date AND seat_code=@seat AND status='ACTIVE'",
                    ("@no", flightNo), ("@date", flightDate.Date), ("@seat", seatCode));

                Exec(con,
                    "INSERT INTO seat_holds (id,flight_no,flight_date,seat_code,owner,expires_at,status) " +
                    "VALUES (@id,@no,@date,@seat,@owner,@exp,'ACTIVE')",
                    ("@id", Guid.NewGuid().ToString()), ("@no", flightNo), ("@date", flightDate.Date),
                    ("@seat", seatCode), ("@owner", owner),
                    ("@exp", DateTime.Now.AddMinutes(AppData.SeatHoldMinutes)));
            }
            catch (SqliteException e)
            {
                throw new IOException("Database error adding seat hold: " + e.Message, e);
            }
            return true;
        }
    }

    public bool RemoveSeatHoldBySeat(string flightNo, DateTime flightDate, string seatCode, string owner)
    {
        lock (_lock)
        {
            try
            {
                using var con = OpenConnection();
                return Exec(con,
                    "UPDATE seat_holds SET status='RELEASED' WHERE flight_no=@no AND flight_date=@date AND seat_code=@seat AND owner=@owner AND status='ACTIVE'",
                    ("@no", flightNo), ("@date", flightDate.Date), ("@seat", seatCode), ("@owner", owner)) > 0;
            }
            catch (SqliteException e)
            {
                throw new IOException("Database error removing seat hold: " + e.Message, e);
            }
        }
    }

    public void RemoveHoldsForOwnerAndFlight(string owner, string flightNo, DateTime flightDate)
    {
        lock (_lock)
        {
            try
            {
                using var con = OpenConnection();
                Exec(con,
                    "UPDATE seat_holds SET status='COMPLETED' WHERE owner=@owner AND flight_no=@no AND flight_date=@date AND status='ACTIVE'",
                    ("@owner", owner), ("@no", flightNo), ("@date", flightDate.Date));
            }
            catch (SqliteException e)
            {
                throw new IOException("Database error removing seat holds: " + e.Message, e);
            }
        }
    }
}
