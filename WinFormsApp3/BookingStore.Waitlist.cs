using Microsoft.Data.Sqlite;

namespace SkyWings;

/// <summary>
/// Cabin waitlist with time-limited offers. A waitlist entry is a request (name, email, cabin, party size), not a
/// booking. When seats free up the first entry that fits is OFFERED those seats for <see cref="AppData.WaitlistOfferHours"/>;
/// while the offer is live the seats are reserved (see <see cref="CabinSeatsLeft"/>), and when it lapses the next
/// entry is offered. Status flow: WAITING -> OFFERED -> CONVERTED | EXPIRED | CANCELED.
/// </summary>
public sealed partial class BookingStore
{
    public const string WaitlistWaiting = "WAITING";
    public const string WaitlistOffered = "OFFERED";
    public const string WaitlistConverted = "CONVERTED";
    public const string WaitlistExpired = "EXPIRED";
    public const string WaitlistCanceled = "CANCELED";

    /// <summary>Raised (outside the store lock) for each entry that has just been offered seats.</summary>
    public event Action<WaitlistEntry>? WaitlistOfferMade;

    private static void EnsureWaitlistSchema(SqliteConnection con)
    {
        EnsureColumn(con, "ALTER TABLE waitlist_entries ADD COLUMN passenger_name VARCHAR(120) NOT NULL DEFAULT ''");
        EnsureColumn(con, "ALTER TABLE waitlist_entries ADD COLUMN email VARCHAR(200) NOT NULL DEFAULT ''");
        EnsureColumn(con, "ALTER TABLE waitlist_entries ADD COLUMN class_name VARCHAR(20) NOT NULL DEFAULT 'Economy'");
        EnsureColumn(con, "ALTER TABLE waitlist_entries ADD COLUMN seats_wanted INTEGER NOT NULL DEFAULT 1");
        EnsureColumn(con, "ALTER TABLE waitlist_entries ADD COLUMN offer_expires_at DATETIME NULL");
    }

    private const string WaitlistColumns =
        "id,booking_ref,flight_no,flight_date,added_at,status,passenger_name,email,class_name,seats_wanted,offer_expires_at";

    private static WaitlistEntry ReadWaitlist(SqliteDataReader rs, long position)
    {
        int exp = rs.GetOrdinal("offer_expires_at");
        return new WaitlistEntry(position, Str(rs, "booking_ref"), Str(rs, "flight_no"), Dt(rs, "flight_date"),
            Dt(rs, "added_at"), Str(rs, "status"))
        {
            PassengerName = Str(rs, "passenger_name"),
            Email = Str(rs, "email"),
            ClassName = Str(rs, "class_name"),
            SeatsWanted = Int(rs, "seats_wanted"),
            OfferExpiresAt = rs.IsDBNull(exp) ? null : rs.GetDateTime(exp),
            Id = Long(rs, "id")
        };
    }

    /// <summary>
    /// Adds a traveller to the waitlist of a full cabin. Returns null when the cabin still has room for the party
    /// (they should just book) or when this email is already waiting for that flight, date and cabin.
    /// </summary>
    public WaitlistEntry? JoinWaitlist(string flightNo, DateTime flightDate, string className, string passengerName,
        string email, int seatsWanted)
    {
        seatsWanted = Math.Max(1, seatsWanted);
        lock (_lock)
        {
            try
            {
                if (CabinSeatsLeft(flightNo, flightDate, className) >= seatsWanted) return null;
                using var con = OpenConnection();
                using (var dup = Cmd(con,
                    "SELECT COUNT(*) FROM waitlist_entries WHERE flight_no=@f AND flight_date=@d AND class_name=@c " +
                    "AND lower(email)=lower(@e) AND status IN ('WAITING','OFFERED')", null,
                    ("@f", flightNo), ("@d", flightDate.Date), ("@c", className), ("@e", email.Trim())))
                    if (Convert.ToInt32(dup.ExecuteScalar()) > 0) return null;

                string code = "WL-" + Guid.NewGuid().ToString("N")[..8].ToUpperInvariant();
                Exec(con,
                    "INSERT INTO waitlist_entries (booking_ref,flight_no,flight_date,added_at,status,passenger_name,email,class_name,seats_wanted) " +
                    "VALUES (@ref,@f,@d,@at,'WAITING',@name,@e,@c,@n)",
                    ("@ref", code), ("@f", flightNo), ("@d", flightDate.Date), ("@at", DateTime.Now),
                    ("@name", passengerName.Trim()), ("@e", email.Trim()), ("@c", className), ("@n", seatsWanted));
                return FindWaitlistCore(con, code);
            }
            catch (SqliteException e)
            {
                throw new IOException("Database error joining waitlist: " + e.Message, e);
            }
        }
    }

    private static WaitlistEntry? FindWaitlistCore(SqliteConnection con, string code)
    {
        using var cmd = Cmd(con, $"SELECT {WaitlistColumns} FROM waitlist_entries WHERE booking_ref=@ref", null, ("@ref", code));
        using var rs = cmd.ExecuteReader();
        if (!rs.Read()) return null;
        WaitlistEntry entry = ReadWaitlist(rs, 0);
        rs.Close();
        if (entry.Status != WaitlistWaiting) return entry;
        using var pos = Cmd(con,
            "SELECT COUNT(*) FROM waitlist_entries WHERE flight_no=@f AND flight_date=@d AND class_name=@c AND status='WAITING' AND id<=@id",
            null, ("@f", entry.FlightNo), ("@d", entry.FlightDate.Date), ("@c", entry.ClassName), ("@id", entry.Id));
        return entry with { Position = Convert.ToInt64(pos.ExecuteScalar()) };
    }

    /// <summary>Looks up an entry by its code. Requires the matching email so codes cannot be guessed.</summary>
    public WaitlistEntry? FindWaitlistEntry(string code, string email)
    {
        lock (_lock)
        {
            using var con = OpenConnection();
            WaitlistEntry? entry = FindWaitlistCore(con, code.Trim().ToUpperInvariant());
            return entry is not null && string.Equals(entry.Email, email.Trim(), StringComparison.OrdinalIgnoreCase)
                ? entry : null;
        }
    }

    /// <summary>Every live entry (waiting or offered) for a flight and date, in queue order. Used by the admin tab.</summary>
    public List<WaitlistEntry> LoadWaitlistDetails(string flightNo, DateTime flightDate)
    {
        lock (_lock)
        {
            var list = new List<WaitlistEntry>();
            using var con = OpenConnection();
            using var cmd = Cmd(con,
                $"SELECT {WaitlistColumns} FROM waitlist_entries WHERE flight_no=@f AND flight_date=@d AND status IN ('WAITING','OFFERED') " +
                "ORDER BY CASE status WHEN 'OFFERED' THEN 0 ELSE 1 END, added_at, id", null,
                ("@f", flightNo), ("@d", flightDate.Date));
            using var rs = cmd.ExecuteReader();
            long position = 1;
            while (rs.Read())
            {
                WaitlistEntry e = ReadWaitlist(rs, 0);
                list.Add(e.Status == WaitlistWaiting ? e with { Position = position++ } : e);
            }
            return list;
        }
    }

    /// <summary>Every live entry (waiting or offered) on every flight, soonest flight first. Used by the staff Waitlist tab.</summary>
    public List<WaitlistEntry> LoadAllLiveWaitlist()
    {
        lock (_lock)
        {
            var list = new List<WaitlistEntry>();
            using var con = OpenConnection();
            using var cmd = Cmd(con,
                $"SELECT {WaitlistColumns} FROM waitlist_entries WHERE status IN ('WAITING','OFFERED') " +
                "ORDER BY flight_date, flight_no, class_name, CASE status WHEN 'OFFERED' THEN 0 ELSE 1 END, added_at, id");
            using var rs = cmd.ExecuteReader();
            var position = new Dictionary<string, long>();
            while (rs.Read())
            {
                WaitlistEntry e = ReadWaitlist(rs, 0);
                if (e.Status != WaitlistWaiting) { list.Add(e); continue; }
                string key = $"{e.FlightNo}|{e.FlightDate:yyyyMMdd}|{e.ClassName}";
                position[key] = position.GetValueOrDefault(key) + 1;
                list.Add(e with { Position = position[key] });
            }
            return list;
        }
    }

    /// <summary>Seats currently reserved by live offers in one cabin, optionally ignoring one offer (the booker's own).</summary>
    private static int ReservedByOffers(SqliteConnection con, string flightNo, DateTime flightDate, string className,
        string? exceptCode, DateTime now)
    {
        using var cmd = Cmd(con,
            "SELECT COALESCE(SUM(seats_wanted),0) FROM waitlist_entries WHERE flight_no=@f AND flight_date=@d AND class_name=@c " +
            "AND status='OFFERED' AND offer_expires_at > @now AND booking_ref <> @code", null,
            ("@f", flightNo), ("@d", flightDate.Date), ("@c", className), ("@now", now), ("@code", exceptCode ?? ""));
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    /// <summary>
    /// Free seats in a cabin, minus seats reserved for a live waitlist offer. <paramref name="waitlistCode"/> is the
    /// booker's own offer (its seats count as free for them).
    /// </summary>
    public int CabinSeatsLeft(string flightNo, DateTime flightDate, string className, string? waitlistCode)
    {
        lock (_lock)
        {
            int free = CabinSeatsRaw(flightNo, flightDate, className);
            using var con = OpenConnection();
            return Math.Max(0, free - ReservedByOffers(con, flightNo, flightDate, className, waitlistCode, DateTime.Now));
        }
    }

    /// <summary>
    /// Offers freed seats to the queue: for each cabin, entries are visited first-come first-served and the first
    /// ones whose party fits the remaining seats become OFFERED. Returns the entries just offered.
    /// </summary>
    public List<WaitlistEntry> PromoteWaitlist(string flightNo, DateTime flightDate, DateTime now)
    {
        var offered = new List<WaitlistEntry>();
        // Offers last up to WaitlistOfferHours but never past the booking cutoff; after the cutoff nobody can book, so no offer.
        if (AppData.WaitlistOfferDeadline(now, AppData.DepartureOf(flightNo, flightDate)) is not { } offerDeadline) return offered;
        lock (_lock)
        {
            try
            {
                using var con = OpenConnection();
                var waiting = new List<(long Id, string Class, int Seats)>();
                using (var cmd = Cmd(con,
                    "SELECT id,class_name,seats_wanted FROM waitlist_entries WHERE flight_no=@f AND flight_date=@d AND status='WAITING' ORDER BY added_at,id",
                    null, ("@f", flightNo), ("@d", flightDate.Date)))
                using (var rs = cmd.ExecuteReader())
                    while (rs.Read()) waiting.Add((Long(rs, "id"), Str(rs, "class_name"), Int(rs, "seats_wanted")));

                var free = new Dictionary<string, int>();
                foreach (var (id, cls, seats) in waiting)
                {
                    if (!free.TryGetValue(cls, out int left))
                        left = Math.Max(0, CabinSeatsRaw(flightNo, flightDate, cls) -
                                           ReservedByOffers(con, flightNo, flightDate, cls, null, now));
                    if (seats > left) { free[cls] = left; continue; }
                    free[cls] = left - seats;
                    Exec(con, "UPDATE waitlist_entries SET status='OFFERED', offer_expires_at=@exp WHERE id=@id AND status='WAITING'",
                        ("@exp", offerDeadline), ("@id", id));
                    using var read = Cmd(con, $"SELECT {WaitlistColumns} FROM waitlist_entries WHERE id=@id", null, ("@id", id));
                    using var r = read.ExecuteReader();
                    if (r.Read()) offered.Add(ReadWaitlist(r, 0));
                }
            }
            catch (SqliteException e)
            {
                throw new IOException("Database error promoting waitlist: " + e.Message, e);
            }
        }

        foreach (WaitlistEntry entry in offered)
        {
            try { WriteAudit("system", "", "WAITLIST_OFFERED", entry.BookingReference, $"{entry.FlightNo} {entry.FlightDate:yyyy-MM-dd} {entry.ClassName} x{entry.SeatsWanted}"); }
            catch (Exception ex) { LogError(ex); }
            try { WaitlistOfferMade?.Invoke(entry); }
            catch (Exception ex) { LogError(ex); }
        }
        return offered;
    }

    /// <summary>Expires lapsed offers and offers the freed seats to the next in line. Returns the new offers.</summary>
    public List<WaitlistEntry> ExpireWaitlistOffers(DateTime now)
    {
        var flights = new List<(string FlightNo, DateTime Date)>();
        lock (_lock)
        {
            using var con = OpenConnection();
            CloseEntriesOfDepartedFlights(con, now);
            using (var cmd = Cmd(con,
                "SELECT DISTINCT flight_no,flight_date FROM waitlist_entries WHERE status='OFFERED' AND offer_expires_at <= @now", null, ("@now", now)))
            using (var rs = cmd.ExecuteReader())
                while (rs.Read()) flights.Add((Str(rs, "flight_no"), Dt(rs, "flight_date")));
            if (flights.Count == 0) return new List<WaitlistEntry>();
            Exec(con, "UPDATE waitlist_entries SET status='EXPIRED' WHERE status='OFFERED' AND offer_expires_at <= @now", ("@now", now));
        }
        var all = new List<WaitlistEntry>();
        foreach (var (flightNo, date) in flights) all.AddRange(PromoteWaitlist(flightNo, date, now));
        return all;
    }

    /// <summary>
    /// Closes (EXPIRED) every live entry of a flight that has already left. Travellers who kept waiting after the booking cutoff
    /// stay in the queue until then, because a late cancellation can still free seats for an earlier offer; after departure
    /// there is nothing left to wait for.
    /// </summary>
    private void CloseEntriesOfDepartedFlights(SqliteConnection con, DateTime now)
    {
        var departed = new List<(string FlightNo, DateTime Date)>();
        using (var cmd = Cmd(con, "SELECT DISTINCT flight_no,flight_date FROM waitlist_entries WHERE status IN ('WAITING','OFFERED')"))
        using (var rs = cmd.ExecuteReader())
            while (rs.Read())
            {
                string flightNo = Str(rs, "flight_no");
                DateTime date = Dt(rs, "flight_date");
                if (AppData.DepartureOf(flightNo, date) <= now) departed.Add((flightNo, date));
            }
        foreach (var (flightNo, date) in departed)
            Exec(con, "UPDATE waitlist_entries SET status='EXPIRED' WHERE flight_no=@f AND flight_date=@d AND status IN ('WAITING','OFFERED')",
                ("@f", flightNo), ("@d", date.Date));
    }

    /// <summary>True when the code is a live (unexpired) offer for exactly this flight, date and cabin.</summary>
    public bool HasLiveOffer(string code, string flightNo, DateTime flightDate, string className, int seats, DateTime now)
    {
        lock (_lock)
        {
            using var con = OpenConnection();
            using var cmd = Cmd(con,
                "SELECT COUNT(*) FROM waitlist_entries WHERE booking_ref=@c AND flight_no=@f AND flight_date=@d AND class_name=@cl " +
                "AND status='OFFERED' AND offer_expires_at > @now AND seats_wanted >= @n", null,
                ("@c", code.Trim().ToUpperInvariant()), ("@f", flightNo), ("@d", flightDate.Date), ("@cl", className),
                ("@now", now), ("@n", seats));
            return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
        }
    }

    /// <summary>Marks an offered entry as used once its holder has booked.</summary>
    public bool MarkWaitlistConverted(string code)
    {
        lock (_lock)
        {
            using var con = OpenConnection();
            return Exec(con, "UPDATE waitlist_entries SET status='CONVERTED' WHERE booking_ref=@c AND status='OFFERED'",
                ("@c", code.Trim().ToUpperInvariant())) > 0;
        }
    }

    /// <summary>Removes a waiting or offered entry (staff action or the traveller leaving). Freed offer seats go to the next in line.</summary>
    public bool CancelWaitlistEntry(string code)
    {
        string flightNo; DateTime date;
        lock (_lock)
        {
            using var con = OpenConnection();
            WaitlistEntry? entry = FindWaitlistCore(con, code.Trim().ToUpperInvariant());
            if (entry is null || (entry.Status != WaitlistWaiting && entry.Status != WaitlistOffered)) return false;
            Exec(con, "UPDATE waitlist_entries SET status='CANCELED' WHERE booking_ref=@c", ("@c", entry.BookingReference));
            flightNo = entry.FlightNo; date = entry.FlightDate;
        }
        PromoteWaitlist(flightNo, date, DateTime.Now);
        return true;
    }
}
