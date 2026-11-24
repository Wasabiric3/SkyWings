using System.Globalization;
using SkyWings.Controllers;

namespace SkyWings;

/// <summary>Tuning for the demo simulator. The defaults suit a demo; tests use extreme values to force each behaviour.</summary>
public sealed record SimulationOptions(
    double BookingChance = 0.35, double CancelChance = 0.20, int MaxHolds = 3, int HoldCap = 30, int HorizonDays = 14,
    int DailyBookingBudget = 500, double WaitlistJoinChance = 0.08, double WaitlistFreeChance = 0.04);

/// <summary>What one simulation step did (for tests and the staff screen).</summary>
public sealed record SimulationTick(int HoldsAdded, int BookingsAdded, int BookingsCancelled, int LoadsGenerated, int CheckInsAdded = 0,
    int WaitlistJoined = 0, int WaitlistSeatsFreed = 0, int WaitlistResolved = 0);

/// <summary>
/// Makes the demo look like a live operations system. <see cref="EnsureLoad"/> pre-fills every flight over the next days with
/// realistic bookings (seats taken, some cancelled and freed, some cabins sold out); <see cref="Tick"/> then keeps things moving:
/// seat holds appear and expire, bookings are made and cancelled. Everything is deterministic for a given flight, date and seed,
/// is tagged (<c>SIM-</c> references, <c>is_simulated</c>) and never touches real bookings. No WinForms dependency.
/// </summary>
public sealed class SimulationService
{
    private static readonly string[] FirstNames =
    {
        "Maria", "Jose", "Ana", "Juan", "Rosa", "Carlo", "Lea", "Miguel", "Grace", "Paolo", "Bea", "Rafael",
        "Clara", "Noel", "Tina", "Ramon", "Joy", "Eric", "Nina", "Victor", "Alma", "Dante", "Mara", "Luis"
    };

    private static readonly string[] LastNames =
    {
        "Santos", "Reyes", "Cruz", "Bautista", "Ocampo", "Garcia", "Mendoza", "Torres", "Flores", "Ramos", "Aquino", "Castillo",
        "Villanueva", "Navarro", "Salazar", "Dela Cruz", "Fernandez", "Domingo", "Pascual", "Santiago", "Lim", "Tan", "Go", "Uy"
    };

    private static readonly string[] PaymentMethods = { "Credit Card", "Debit Card", "GCash" };

    // Made-up but plausible Philippine addresses for simulated travellers.
    private static readonly string[] Streets =
    {
        "Rizal Street", "Mabini Street", "Bonifacio Avenue", "Luna Street", "Del Pilar Street", "Quezon Avenue", "Aguinaldo Highway",
        "Magsaysay Boulevard", "Osmeña Street", "Burgos Street", "Jacinto Street", "Roxas Avenue"
    };

    private static readonly string[] Cities =
    {
        "Quezon City", "Makati City", "Pasig City", "Taguig City", "Manila", "Cebu City", "Davao City", "Iloilo City",
        "Bacolod City", "Cagayan de Oro", "Baguio City", "Antipolo City"
    };

    private readonly BookingStore _store;
    private readonly SimulationOptions _options;

    public SimulationService(BookingStore store, SimulationOptions? options = null)
    {
        _store = store;
        _options = options ?? new SimulationOptions();
    }

    private readonly record struct Seat(int Row, int Col);

    // ------------------------------------------------------------------ pre-filled load

    /// <summary>Generated cabins are always between these: busy enough to look real, never so full that nothing can be booked.</summary>
    internal const double MinLoad = 0.60, MaxLoad = 0.90;

    /// <summary>Bumped whenever the load formula changes, so demo bookings made by the old one are regenerated once.</summary>
    public const string LoadVersion = "5";

    /// <summary>Per day, at most this many flights that have a sold-out premium cabin are generated first, so the waitlist always has something to show.</summary>
    private const int WaitlistShowcaseFlightsPerDay = 2;

    /// <summary>Setting key prefix marking a day whose load reached the daily booking budget (so it is not topped up again).</summary>
    private const string LoadedDayKeyPrefix = "demo.loaded.";

    private static string LoadedDayKey(DateTime date) => LoadedDayKeyPrefix + date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    /// <summary>About one flight in twelve has a premium cabin that is completely sold out. Stable for a flight and date.</summary>
    internal static bool IsSoldOutDay(string flightNo, DateTime date)
        => FlightStatusEngine.StableSeed($"sim|{flightNo}|{date:yyyyMMdd}") % 12 == 0;

    /// <summary>About one flight in thirty is completely sold out in Economy (a busy flight, which is what real waitlists are mostly for).</summary>
    internal static bool IsEconomySoldOutDay(string flightNo, DateTime date)
        => FlightStatusEngine.StableSeed($"sim-eco|{flightNo}|{date:yyyyMMdd}") % 30 == 0;

    /// <summary>The cabin that sells out on a sold-out day: Business, or First Class where there is no Business; null if neither.</summary>
    internal static string? SoldOutCabinName(Aircraft aircraft)
        => aircraft.Cabin("Business") is not null ? "Business" : aircraft.Cabin("First Class") is not null ? "First Class" : null;

    /// <summary>
    /// How full a flight tends to be, before randomness: about 85% on the day, easing to about 66% two weeks out, domestic a
    /// little fuller. Always within <see cref="MinLoad"/>..<see cref="MaxLoad"/>.
    /// </summary>
    internal static double BaseLoad(int daysOut, bool domestic)
        => Math.Clamp(0.84 - 0.013 * Math.Max(0, daysOut) + (domestic ? 0.02 : -0.02), 0.66, 0.86);

    /// <summary>Premium cabins run a little emptier than Economy.</summary>
    private static double CabinFactor(string className) => className switch
    {
        "Economy" => 1.0,
        "Premium Economy" => 0.96,
        "Business" => 0.92,
        _ => 0.88
    };

    /// <summary>The seats to fill for an occupancy, kept so the cabin itself shows 60–90% (rounding a small cabin can't push it out).</summary>
    internal static int TargetSeats(double occupancy, int seats)
    {
        int low = (int)Math.Ceiling(MinLoad * seats), high = (int)Math.Floor(MaxLoad * seats);
        return Math.Clamp((int)Math.Round(occupancy * seats), Math.Min(low, high), high);
    }

    private List<Flight> AllFlights() => _store.LoadActiveFlights(true).Concat(_store.LoadActiveFlights(false)).ToList();

    /// <summary>
    /// Generates the load for every active flight on every date in the horizon that has none yet (one transaction per day).
    /// Returns how many flight/date pairs were generated; calling it again generates nothing.
    /// </summary>
    public int EnsureLoad(DateTime now, int? horizonDays = null)
    {
        int horizon = horizonDays ?? _options.HorizonDays;
        HashSet<string> existing = _store.LoadSimulatedFlightDates();
        List<Flight> flights = AllFlights();
        var aircraft = new Dictionary<string, Aircraft>();
        int generated = 0;
        DateTime? earliestDeparture = null;
        Dictionary<string, HashSet<string>> realSeats = _store.LoadRealTakenSeatsByFlightDate(now.Date, now.Date.AddDays(Math.Max(0, horizon - 1)));
        bool capped = _options.DailyBookingBudget < int.MaxValue;
        for (int d = 0; d < horizon; d++)
        {
            DateTime date = now.Date.AddDays(d);
            // A day that already reached its budget is finished: it must not be topped up with more flights every minute.
            if (capped && _store.GetSetting(LoadedDayKey(date), "") == "1") continue;
            var batch = new List<BookingRecord>();

            // Which flights get a load today: the waitlist showcase flights first, then the rest in a stable per-day order.
            List<Flight> ranked = flights.Where(f => !existing.Contains(BookingStore.SimulatedKey(f.FlightNo, date)))
                .OrderBy(f => FlightStatusEngine.StableSeed($"pick|{f.FlightNo}|{date:yyyyMMdd}")).ToList();
            List<Flight> showcase = ranked.Where(f => IsSoldOutDay(f.FlightNo, date)).Take(WaitlistShowcaseFlightsPerDay)
                .Concat(ranked.Where(f => IsEconomySoldOutDay(f.FlightNo, date)).Take(1)).Distinct().ToList();
            int booked = 0;
            bool reachedBudget = false;
            foreach (Flight flight in showcase.Concat(ranked.Except(showcase)))
            {
                if (booked >= _options.DailyBookingBudget) { reachedBudget = true; break; }
                DateTime departure = AppData.DepartureOf(flight.FlightNo, date);
                if (earliestDeparture is null || departure < earliestDeparture) earliestDeparture = departure;
                if (!aircraft.TryGetValue(flight.FlightNo, out Aircraft? plane)) aircraft[flight.FlightNo] = plane = _store.LoadAircraft(flight.FlightNo);
                List<BookingRecord> load = GenerateLoad(flight, plane, date, now,
                    realSeats.GetValueOrDefault(BookingStore.SimulatedKey(flight.FlightNo, date)) ?? new HashSet<string>());
                batch.AddRange(load);
                booked += load.Count(b => b.Status == AppData.StatusConfirmed);
                generated++;
            }
            if (capped && (reachedBudget || booked >= _options.DailyBookingBudget)) _store.SetSetting(LoadedDayKey(date), "1");
            // Small transactions: the store has one lock, and the board must not wait long for it while this runs in the background.
            foreach (BookingRecord[] chunk in batch.Chunk(400)) _store.SaveSimulatedBookings(chunk);
        }
        if (earliestDeparture is { } earliest) LookBackTo(earliest);
        return generated;
    }

    /// <summary>
    /// A load was just generated for a flight leaving at <paramref name="departure"/> (e.g. a flight added or reactivated after it
    /// left today): if the check-in watermark is already past it, move it back so the next step checks those travellers in.
    /// </summary>
    private void LookBackTo(DateTime departure)
    {
        if (DateTime.TryParse(_store.GetSetting(BookingStore.SettingDemoCheckInsThrough, ""), CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out DateTime through) && through >= departure)
            _store.SetSetting(BookingStore.SettingDemoCheckInsThrough, departure.AddMinutes(-1).ToString("o", CultureInfo.InvariantCulture));
    }

    private List<BookingRecord> GenerateLoad(Flight flight, Aircraft aircraft, DateTime date, DateTime now, HashSet<string> realTaken)
    {
        long seed = FlightStatusEngine.StableSeed($"sim|{flight.FlightNo}|{date:yyyyMMdd}");
        var rng = new JavaRandom(seed);
        int daysOut = (date.Date - now.Date).Days;
        double load = BaseLoad(daysOut, flight.Domestic) + (rng.NextDouble() - 0.5) * 0.12;   // each flight its own ±6%
        // About one flight in twelve has a premium cabin that is completely sold out (the waitlist needs something to show).
        // Economy never sells out here, so the board's load stays in the 60–90% range.
        bool soldOutDay = IsSoldOutDay(flight.FlightNo, date);
        string? soldOutCabin = SoldOutCabinName(aircraft);
        bool economyDay = IsEconomySoldOutDay(flight.FlightNo, date);

        var result = new List<BookingRecord>();
        int counter = 0;
        foreach (CabinLayout cabin in aircraft.Cabins)
        {
            List<Seat> free = AllSeats(cabin).Where(s => !realTaken.Contains(SeatLayout.Normalize(cabin.Code(s.Row + 1, cabin.Letters[s.Col])))).ToList();
            Shuffle(free, rng);
            bool soldOut = (soldOutDay && cabin.ClassName == soldOutCabin) || (economyDay && cabin.ClassName == "Economy");
            double occupancy = load * CabinFactor(cabin.ClassName) + (rng.NextDouble() - 0.5) * 0.06;
            // Seats real bookings already took count towards the load.
            int realInCabin = cabin.Seats - free.Count;
            int target = soldOut ? free.Count : Math.Clamp(TargetSeats(occupancy, cabin.Seats) - realInCabin, 0, Math.Max(0, free.Count - 1));

            var remaining = new HashSet<Seat>(free);
            int confirmedBookings = 0, used = 0;
            foreach (Seat start in free)
            {
                if (used >= target) break;
                if (!remaining.Contains(start)) continue;
                List<Seat> party = TakeParty(cabin, start, Math.Min(PartySize(rng), target - used), remaining);
                result.Add(MakeBooking(flight, date, cabin, party, now, rng, ++counter, cancelled: false));
                used += party.Count;
                confirmedBookings++;
            }

            // Some bookings were cancelled: their seats are free again, so they use seats that are still unassigned.
            int cancelTarget = Math.Max(1, (int)Math.Round(confirmedBookings * (0.05 + rng.NextDouble() * 0.04)));
            for (int i = 0; i < cancelTarget; i++)
            {
                List<Seat> pool = remaining.Count > 0 ? new List<Seat> { remaining.First() } : new List<Seat> { new(0, 0) };
                result.Add(MakeBooking(flight, date, cabin, pool, now, rng, ++counter, cancelled: true));
            }
        }
        return result;
    }

    private static IEnumerable<Seat> AllSeats(CabinLayout cabin)
    {
        for (int row = 0; row < cabin.Rows; row++)
            for (int col = 0; col < cabin.SeatsPerRow; col++)
                yield return new Seat(row, col);
    }

    private static void Shuffle<T>(IList<T> list, JavaRandom rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.NextInt(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    /// <summary>1 traveller most of the time, sometimes a couple or a small group.</summary>
    private static int PartySize(JavaRandom rng)
    {
        int roll = rng.NextInt(100);
        return roll < 55 ? 1 : roll < 80 ? 2 : roll < 92 ? 3 : 4;
    }

    /// <summary>Takes a seat and, for a party, free seats right next to it in the same row (not across an aisle).</summary>
    private static List<Seat> TakeParty(CabinLayout cabin, Seat start, int size, HashSet<Seat> remaining)
    {
        var party = new List<Seat> { start };
        remaining.Remove(start);
        int[] aisles = cabin.AislesAfter;
        Seat current = start;
        while (party.Count < size && current.Col + 1 < cabin.SeatsPerRow && !aisles.Contains(current.Col))
        {
            var next = new Seat(current.Row, current.Col + 1);
            if (!remaining.Remove(next)) break;
            party.Add(next);
            current = next;
        }
        return party;
    }

    private BookingRecord MakeBooking(Flight flight, DateTime date, CabinLayout cabin, List<Seat> seats, DateTime now, JavaRandom rng,
        int counter, bool cancelled, string? referenceSalt = null, DateTime? bookedAtOverride = null, string? leadName = null)
    {
        // Same shape as a booking made at the desk ("SKY-" + 12 hex); the is_simulated column is what marks it as simulated.
        string reference = "SKY-" +
            ((ulong)FlightStatusEngine.StableSeed($"{flight.FlightNo}|{date:yyyyMMdd}|{referenceSalt}|{counter}")).ToString("X16", CultureInfo.InvariantCulture)[..12];
        // Contact details come from their own generator (seeded by the reference), so the seat pattern and loads stay exactly as
        // the main generator makes them.
        var details = new JavaRandom(FlightStatusEngine.StableSeed("details|" + reference));
        int classIndex = Math.Max(0, Array.IndexOf(AppData.ClassNames, cabin.ClassName));
        decimal fare = AppData.Round2(flight.BasePrice * AppData.ClassMultipliers[classIndex] * 1.12m);
        var passengers = new List<PassengerRecord>();
        foreach (Seat seat in seats)
        {
            string name = $"{FirstNames[rng.NextInt(FirstNames.Length)]} {LastNames[rng.NextInt(LastNames.Length)]}";
            if (leadName is not null && passengers.Count == 0) name = leadName;   // a waitlisted traveller books under their own name
            string code = cabin.Code(seat.Row + 1, cabin.Letters[seat.Col]);
            int age = 18 + rng.NextInt(52);
            string address = $"{1 + details.NextInt(250)} {Streets[details.NextInt(Streets.Length)]}, {Cities[details.NextInt(Cities.Length)]}";
            string phone = "09" + (100_000_000 + details.NextInt(900_000_000)).ToString(CultureInfo.InvariantCulture);
            string passport = "P" + (1_000_000 + details.NextInt(9_000_000)).ToString(CultureInfo.InvariantCulture) + (char)('A' + details.NextInt(26));
            passengers.Add(new PassengerRecord(name, address, phone, age,
                now.Date.AddYears(-age).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), passport, false, false,
                new SeatAssignment(name, code), Array.Empty<string>(), fare));
        }
        string paymentRef = "TXN-" + ((ulong)FlightStatusEngine.StableSeed("txn|" + reference)).ToString("X16", CultureInfo.InvariantCulture)[..10];
        decimal total = fare * seats.Count;
        decimal refund = cancelled ? AppData.Round2(total * 0.5m) : 0m;
        DateTime bookedAt = now.AddDays(-(1 + rng.NextInt(30))).AddMinutes(-rng.NextInt(1440));
        if (bookedAtOverride is { } at) bookedAt = at;
        string method = PaymentMethods[rng.NextInt(PaymentMethods.Length)];
        return new BookingRecord(reference, bookedAt, flight.FlightNo, flight.Origin, flight.Destination, date.Date, flight.Domestic,
            cabin.ClassName, method, "**** " + (1000 + rng.NextInt(9000)), total, "Scheduled", passengers,
            cancelled ? AppData.StatusCanceled : AppData.StatusConfirmed,
            cancelled ? "REFUNDED" : AppData.PaymentPaid, paymentRef, bookedAt, refund);
    }

    // ------------------------------------------------------------------ live changes

    /// <summary>
    /// One step of live activity: expires old demo holds, adds a few new ones, sometimes makes a booking and sometimes cancels one.
    /// A cancellation frees the seat and runs the normal waitlist logic, so real waitlisted travellers really are offered the seat.
    /// </summary>
    public SimulationTick Tick(DateTime now, long seed)
    {
        var rng = new JavaRandom(seed);
        _store.PurgeExpiredSeatHolds(now);
        List<(string FlightNo, DateTime Date)> upcoming = UpcomingLoads(now, days: 7);
        Dictionary<string, Flight> flights = AllFlights().ToDictionary(f => f.FlightNo);

        int holds = 0;
        int active = _store.CountActiveSimulatedHolds(now);
        int wanted = rng.NextInt(_options.MaxHolds + 1);
        for (int i = 0; i < wanted && active < _options.HoldCap && upcoming.Count > 0; i++)
            if (TryAddHold(now, rng, upcoming, 3)) { holds++; active++; }

        int booked = 0;
        if (rng.NextDouble() < _options.BookingChance && TryAddBooking(now, rng, upcoming, flights, seed)) booked++;

        int cancelled = 0;
        if (rng.NextDouble() < _options.CancelChance && TryCancelOne(now, rng)) cancelled++;

        int joined = 0, freed = 0;
        if (rng.NextDouble() < _options.WaitlistJoinChance && TryJoinWaitlist(now, rng, upcoming)) joined++;
        if (rng.NextDouble() < _options.WaitlistFreeChance && TryFreeWaitlistSeat(now, rng, upcoming)) freed++;
        return new SimulationTick(holds, booked, cancelled, 0, 0, joined, freed);
    }

    // ------------------------------------------------------------------ waitlist demand

    /// <summary>At most this many simulated travellers wait for one cabin (Economy has more seats, so a longer queue).</summary>
    private static int MaxSimulatedWaiting(string cabin) => cabin == "Economy" ? 12 : 8;

    /// <summary>The cabins the simulator sells out completely on a flight and date: the premium cabin on some days, Economy on others.</summary>
    private static IEnumerable<string> SoldOutCabinNames(string flightNo, DateTime date, Aircraft aircraft)
    {
        if (IsSoldOutDay(flightNo, date) && SoldOutCabinName(aircraft) is { } premium) yield return premium;
        if (IsEconomySoldOutDay(flightNo, date) && aircraft.Cabin("Economy") is not null) yield return "Economy";
    }

    /// <summary>
    /// The cabins that are sold out right now on a showcase flight within a week, as (flight, date, cabin). Only the sold-out days
    /// are looked at, so this stays a handful of cheap checks even though it runs every minute.
    /// </summary>
    private List<(string FlightNo, DateTime Date, string Cabin)> SoldOutCabins(DateTime now, List<(string FlightNo, DateTime Date)> upcoming)
    {
        var result = new List<(string, DateTime, string)>();
        foreach (var (flightNo, date) in upcoming)
        {
            if (!IsSoldOutDay(flightNo, date) && !IsEconomySoldOutDay(flightNo, date)) continue;
            if (AppData.DepartureOf(flightNo, date) <= now.AddHours(AppData.BookingCutoffHours)) continue;   // nobody can book any more
            foreach (string cabin in SoldOutCabinNames(flightNo, date, _store.LoadAircraft(flightNo)))
                if (_store.CabinSeatsLeft(flightNo, date, cabin) == 0) result.Add((flightNo, date, cabin));
        }
        return result;
    }

    private static readonly string[] WaitlistFirstNames =
    {
        "Maria", "Jose", "Ana", "Juan", "Rosa", "Carlo", "Lea", "Miguel", "Grace", "Paolo", "Bea", "Rafael", "Clara", "Noel", "Tina",
        "Ramon", "Joy", "Eric", "Nina", "Victor", "Alma", "Dante", "Mara", "Luis", "Kristine", "Jericho", "Angelica", "Mark", "Jasmine",
        "Francis", "Camille", "Renato", "Liza", "Arnel", "Patricia", "Joel", "Danica", "Ferdinand", "Maricel", "Vince", "Trisha",
        "Gabriel", "Celine", "Emmanuel", "Hazel", "Rowena", "Julius", "Sheila", "Benjie", "Lorna"
    };

    private static readonly string[] WaitlistLastNames =
    {
        "Santos", "Reyes", "Cruz", "Bautista", "Ocampo", "Garcia", "Mendoza", "Torres", "Flores", "Ramos", "Aquino", "Castillo",
        "Villanueva", "Navarro", "Salazar", "Dela Cruz", "Fernandez", "Domingo", "Pascual", "Santiago", "Lim", "Tan", "Go", "Uy",
        "Manalo", "Soriano", "Estrada", "Valdez", "Magno", "Lacson", "Evangelista", "Gonzales", "Cabrera", "Alcantara", "Velasco",
        "Policarpio", "Macaraeg", "De Guzman", "Panganiban", "Tolentino", "Rivera", "Agbayani", "Zamora", "Sarmiento", "Bernardo"
    };

    private (string Name, string Email) MadeUpTraveller(JavaRandom rng)
    {
        string first = WaitlistFirstNames[rng.NextInt(WaitlistFirstNames.Length)], last = WaitlistLastNames[rng.NextInt(WaitlistLastNames.Length)];
        string local = (first + "." + last).Replace(" ", "").ToLowerInvariant() + (10 + rng.NextInt(90)).ToString(CultureInfo.InvariantCulture);
        return ($"{first} {last}", local + "@example.com");   // example.com: a reserved address that nothing is ever sent to
    }

    /// <summary>
    /// Gives every sold-out showcase cabin its first waiting travellers: 3–6 in a premium cabin, 6–10 in Economy, parties of 1–4. They
    /// joined over the days before now, further back for flights further away and mostly closer to the flight, in join order. A
    /// cabin is only seeded once. Returns how many entries were added. Never touches real entries.
    /// </summary>
    public int SeedWaitlist(DateTime now)
    {
        int added = 0;
        foreach (var (flightNo, date, cabin) in SoldOutCabins(now, UpcomingLoads(now, 7)))
        {
            if (_store.CountSimulatedWaitlist(flightNo, date, cabin) > 0) continue;
            var rng = new JavaRandom(FlightStatusEngine.StableSeed($"wl|{flightNo}|{date:yyyyMMdd}|{cabin}"));
            int count = cabin == "Economy" ? 6 + rng.NextInt(5) : 3 + rng.NextInt(4);

            // How far back people could have joined: up to 60% of the time left to departure, between 3 hours and 5 days.
            double hoursOut = (AppData.DepartureOf(flightNo, date) - now).TotalHours;
            int windowMinutes = (int)Math.Clamp(hoursOut * 60 * 0.6, 180, 5 * 24 * 60);
            // u*u bunches joins towards the present (more people join as the flight gets close), but some are days old.
            List<DateTime> joined = Enumerable.Range(0, count)
                .Select(_ => { double u = rng.NextDouble(); return now.AddMinutes(-(10 + (int)(windowMinutes * u * u))); })
                .OrderBy(t => t).ToList();   // the earliest joiner is first in line
            foreach (DateTime joinedAt in joined)
            {
                (string name, string email) = MadeUpTraveller(rng);
                if (_store.AddSimulatedWaitlistEntry(flightNo, date, cabin, name, email, PartySize(rng), joinedAt)) added++;
            }
        }
        return added;
    }

    /// <summary>A new traveller joins the queue of a sold-out cabin (capped), so the Waitlist tab keeps changing.</summary>
    private bool TryJoinWaitlist(DateTime now, JavaRandom rng, List<(string FlightNo, DateTime Date)> upcoming)
    {
        var cabins = SoldOutCabins(now, upcoming)
            .Where(c => _store.CountSimulatedWaitlist(c.FlightNo, c.Date, c.Cabin, BookingStore.WaitlistWaiting) is > 0
                        && _store.CountSimulatedWaitlist(c.FlightNo, c.Date, c.Cabin, BookingStore.WaitlistWaiting) < MaxSimulatedWaiting(c.Cabin)).ToList();
        if (cabins.Count == 0) return false;
        var (flightNo, date, cabin) = cabins[rng.NextInt(cabins.Count)];
        (string name, string email) = MadeUpTraveller(rng);
        return _store.AddSimulatedWaitlistEntry(flightNo, date, cabin, name, email, PartySize(rng), now);
    }

    /// <summary>
    /// A demo traveller cancels a booking in a sold-out cabin that has people waiting. The normal cancellation frees the seats and the
    /// normal waitlist logic offers them to the first entry that fits, so an OFFERED entry appears on the tab.
    /// </summary>
    private bool TryFreeWaitlistSeat(DateTime now, JavaRandom rng, List<(string FlightNo, DateTime Date)> upcoming)
    {
        var cabins = SoldOutCabins(now, upcoming)
            .Where(c => _store.CountSimulatedWaitlist(c.FlightNo, c.Date, c.Cabin, BookingStore.WaitlistWaiting) > 0
                        && _store.CountSimulatedWaitlist(c.FlightNo, c.Date, c.Cabin, BookingStore.WaitlistOffered) == 0).ToList();
        if (cabins.Count == 0) return false;
        var (flightNo, date, cabin) = cabins[rng.NextInt(cabins.Count)];
        List<string> references = _store.LoadSimulatedConfirmedReferences(flightNo, date, cabin);
        return references.Count > 0 && CancelSimulatedBooking(references[rng.NextInt(references.Count)]);
    }

    /// <summary>
    /// Demo travellers answer their offers. Each one has a fixed fate, decided by their code: about 60% book the seats a few minutes
    /// after the offer (the entry becomes CONVERTED and a booking appears), about 15% decline (the offer passes to the next in line),
    /// and about 25% never answer, so the offer lapses after its deadline and the next in line is offered. Returns how many answered.
    /// </summary>
    public int ProgressWaitlistOffers(DateTime now)
    {
        int answered = 0;
        foreach (WaitlistEntry entry in _store.LoadSimulatedOffers())
        {
            if (entry.OfferedAt is not { } offeredAt || entry.OfferExpiresAt is not { } until || until <= now) continue;
            int roll = WaitlistFate(entry.BookingReference);
            int delay = (int)((((FlightStatusEngine.StableSeed("fate|" + entry.BookingReference) / 100) % 25) + 25) % 25);
            try
            {
                if (roll < 60) { if (now >= offeredAt.AddMinutes(2 + delay) && BookForWaitlist(entry, now)) answered++; }
                else if (roll < 75) { if (now >= offeredAt.AddMinutes(4 + delay) && _store.CancelWaitlistEntry(entry.BookingReference)) answered++; }
            }
            catch (IOException ex) { AppLogger.Error("Demo", ex); }   // one traveller's problem never stops the others
        }
        return answered;
    }

    /// <summary>
    /// Frees seats in one cabin on demand by cancelling demo bookings with the normal cancellation rules, so the normal waitlist
    /// logic offers them. The first booking chosen is the first one large enough for the traveller first in line (or the largest there
    /// is); when that is still too small for their party (every demo booking in the cabin is a single traveller, say), more demo
    /// bookings are cancelled, largest first, until the party fits or there are none left.
    /// Returns the first cancelled reference and how many seats were freed in all, or null when the cabin has no demo booking to cancel.
    /// Real bookings are never touched.
    /// </summary>
    public (string Reference, int Seats)? FreeSeatsInCabin(string flightNo, DateTime date, string cabin)
    {
        int needed = _store.LoadWaitlistDetails(flightNo, date)
            .Where(e => e.ClassName == cabin && e.Status == BookingStore.WaitlistWaiting)
            .Select(e => e.SeatsWanted).FirstOrDefault();
        var bookings = new List<BookingRecord>();
        foreach (string reference in _store.LoadSimulatedConfirmedReferences(flightNo, date, cabin).Take(60))
            if (_store.FindByReference(reference) is { } booking) bookings.Add(booking);

        BookingRecord? pick = null;
        foreach (BookingRecord booking in bookings)
        {
            if (pick is null || booking.Passengers.Count > pick.Passengers.Count) pick = booking;
            if (pick.Passengers.Count >= needed) break;
        }
        if (pick is null || !CancelSimulatedBooking(pick.Reference)) return null;

        int freed = pick.Passengers.Count;
        foreach (BookingRecord extra in bookings.Where(b => b.Reference != pick.Reference).OrderByDescending(b => b.Passengers.Count))
        {
            if (freed >= needed) break;
            if (CancelSimulatedBooking(extra.Reference)) freed += extra.Passengers.Count;
        }
        return (pick.Reference, freed);
    }

    /// <summary>0–99, fixed for a waitlist code: below 60 the traveller books, 60–74 declines, 75 and above never answers.</summary>
    internal static int WaitlistFate(string code)
        => (int)(((FlightStatusEngine.StableSeed("fate|" + code) % 100) + 100) % 100);

    /// <summary>The waitlisted demo traveller books the offered seats under their own name; the entry is marked CONVERTED.</summary>
    private bool BookForWaitlist(WaitlistEntry entry, DateTime now)
    {
        Flight? flight = AllFlights().FirstOrDefault(f => f.FlightNo == entry.FlightNo);
        CabinLayout? cabin = _store.LoadAircraft(entry.FlightNo).Cabin(entry.ClassName);
        if (flight is null || cabin is null) return false;

        HashSet<string> blocked = _store.LoadTakenSeats(entry.FlightNo, entry.FlightDate);
        foreach (string held in _store.GetActiveHoldsForFlightDate(entry.FlightNo, entry.FlightDate, now).Keys) blocked.Add(SeatLayout.Normalize(held));
        List<Seat> free = AllSeats(cabin).Where(s => !blocked.Contains(SeatLayout.Normalize(cabin.Code(s.Row + 1, cabin.Letters[s.Col])))).ToList();
        if (free.Count < entry.SeatsWanted) return false;

        var rng = new JavaRandom(FlightStatusEngine.StableSeed("wlbook|" + entry.BookingReference));
        Shuffle(free, rng);
        var remaining = new HashSet<Seat>(free);
        var seats = new List<Seat>();
        while (seats.Count < entry.SeatsWanted && remaining.Count > 0)
            seats.AddRange(TakeParty(cabin, free.First(remaining.Contains), entry.SeatsWanted - seats.Count, remaining));

        BookingRecord booking = MakeBooking(flight, entry.FlightDate, cabin, seats, now, rng, counter: 0, cancelled: false,
            referenceSalt: "wl" + entry.BookingReference, bookedAtOverride: now, leadName: entry.PassengerName);
        _store.SaveSimulatedBooking(booking);
        return _store.MarkWaitlistConverted(entry.BookingReference);
    }

    private List<(string FlightNo, DateTime Date)> UpcomingLoads(DateTime now, int days)
    {
        var list = new List<(string, DateTime)>();
        foreach (string key in _store.LoadSimulatedFlightDates().OrderBy(k => k, StringComparer.Ordinal))
        {
            string[] parts = key.Split('|');
            DateTime date = DateTime.ParseExact(parts[1], "yyyyMMdd", CultureInfo.InvariantCulture);
            if (date >= now.Date && date <= now.Date.AddDays(days)) list.Add((parts[0], date));
        }
        return list;
    }

    private (CabinLayout Cabin, List<Seat> Free)? PickFreeSeats(string flightNo, DateTime date, DateTime now, JavaRandom rng)
    {
        Aircraft aircraft = _store.LoadAircraft(flightNo);
        if (aircraft.Cabins.Count == 0) return null;
        CabinLayout cabin = aircraft.Cabins[rng.NextInt(aircraft.Cabins.Count)];
        HashSet<string> blocked = _store.LoadTakenSeats(flightNo, date);
        foreach (string held in _store.GetActiveHoldsForFlightDate(flightNo, date, now).Keys) blocked.Add(SeatLayout.Normalize(held));
        List<Seat> free = AllSeats(cabin).Where(s => !blocked.Contains(SeatLayout.Normalize(cabin.Code(s.Row + 1, cabin.Letters[s.Col])))).ToList();
        return free.Count == 0 ? null : (cabin, free);
    }

    private bool TryAddHold(DateTime now, JavaRandom rng, List<(string FlightNo, DateTime Date)> upcoming, int days)
    {
        List<(string FlightNo, DateTime Date)> near = upcoming.Where(u => u.Date <= now.Date.AddDays(days)).ToList();
        if (near.Count == 0) return false;
        var (flightNo, date) = near[rng.NextInt(near.Count)];
        if (PickFreeSeats(flightNo, date, now, rng) is not { } pick) return false;
        Seat seat = pick.Free[rng.NextInt(pick.Free.Count)];
        string owner = BookingStore.SimulatedHoldPrefix + rng.NextInt(0x7FFFFFFF).ToString("X8", CultureInfo.InvariantCulture);
        try { return _store.AddSeatHold(flightNo, date, pick.Cabin.Code(seat.Row + 1, pick.Cabin.Letters[seat.Col]), owner, now); }
        catch (InvalidOperationException) { return false; }   // the per-owner hold limit: not worth more than skipping
    }

    private bool TryAddBooking(DateTime now, JavaRandom rng, List<(string FlightNo, DateTime Date)> upcoming, Dictionary<string, Flight> flights, long seed)
    {
        if (upcoming.Count == 0) return false;
        var (flightNo, date) = upcoming[rng.NextInt(upcoming.Count)];
        if (!flights.TryGetValue(flightNo, out Flight? flight) || AppData.DepartureOf(flightNo, date) <= now.AddHours(AppData.BookingCutoffHours)) return false;
        if (PickFreeSeats(flightNo, date, now, rng) is not { } pick) return false;
        var remaining = new HashSet<Seat>(pick.Free);
        Seat start = pick.Free[rng.NextInt(pick.Free.Count)];
        List<Seat> party = TakeParty(pick.Cabin, start, Math.Min(PartySize(rng), 3), remaining);
        BookingRecord booking = MakeBooking(flight, date, pick.Cabin, party, now, rng, counter: 0, cancelled: false, referenceSalt: $"tick{seed}");
        _store.SaveSimulatedBooking(booking);
        return true;
    }

    private bool TryCancelOne(DateTime now, JavaRandom rng)
    {
        int count = _store.CountSimulatedConfirmed(now.Date);
        if (count == 0) return false;
        if (_store.GetSimulatedConfirmedAt(rng.NextInt(count), now.Date) is not { } pick) return false;
        if (AppData.DepartureOf(pick.FlightNo, pick.Date) <= now.AddHours(AppData.BookingCutoffHours)) return false;   // too late to cancel
        return CancelSimulatedBooking(pick.Reference);
    }

    /// <summary>
    /// Cancels one demo booking with the normal cancellation rules (50% refund): the seat is freed and the waitlist is offered it.
    /// Refuses anything that is not a demo booking.
    /// </summary>
    public bool CancelSimulatedBooking(string reference)
    {
        if (!_store.IsSimulatedBooking(reference)) return false;   // never a real booking
        BookingRecord? booking = _store.FindByReference(reference);
        return booking is not null && _store.CancelBooking(reference, AppData.Round2(booking.TotalAmount * 0.5m), "system");
    }

    // ------------------------------------------------------------------ switch and reset

    /// <summary>
    /// Brings a database made by an older version up to date, once: its simulated bookings (an older formula, or SIM- references
    /// and placeholder passenger details) are deleted to be generated again, and the simulation is switched on and counted in
    /// reports — since version 3 it always runs and counts like any other traffic, and there is no screen to change either.
    /// Real bookings are not touched. Returns true when it did something.
    /// </summary>
    public bool MigrateIfNeeded()
    {
        if (_store.GetSetting(BookingStore.SettingDemoLoadVersion, "1") == LoadVersion) return false;
        _store.DeleteSimulatedBookings();
        _store.SetSetting(BookingStore.SettingDemoActivity, "1");
        _store.SetSetting(BookingStore.SettingDemoInReports, "1");
        _store.SetSetting(BookingStore.SettingDemoLoadVersion, LoadVersion);
        return true;
    }

    /// <summary>
    /// Called once a minute by the home screen: brings an older database up to date (which also switches the simulation on), then,
    /// unless the simulation is off, makes sure the flights are filled and takes one live step. Returns null when it is off.
    /// </summary>
    public SimulationTick? RunIfEnabled(DateTime now)
    {
        MigrateIfNeeded();   // before the switch: an old database may have it off, and nothing on screen can turn it back on
        if (!_store.DemoActivityEnabled) return null;
        int loads = EnsureLoad(now);
        SeedWaitlist(now);
        int resolved = ProgressWaitlistOffers(now);
        int checkIns = ProgressCheckIns(now);
        SimulationTick tick = Tick(now, FlightStatusEngine.StableSeed($"tick|{now:yyyyMMddHHmm}"));
        return tick with { LoadsGenerated = loads, CheckInsAdded = checkIns, WaitlistResolved = resolved };
    }

    // ------------------------------------------------------------------ demo check-ins

    /// <summary>About this share of demo bookings never check in, so their trip ends as a no-show.</summary>
    internal const int NoShowPercent = 8;

    /// <summary>How far back missed demo check-ins are caught up (e.g. after the app was closed over a weekend).</summary>
    private const int CheckInLookbackDays = 7;

    /// <summary>
    /// When a demo traveller checks in for a flight leaving at <paramref name="departure"/>, or null when the whole booking is a
    /// no-show. The booking decides the moment, so a party checks in together (each traveller 0–4 minutes apart): some right after
    /// check-in opens, some the evening before, most in the last 12 hours. Always strictly inside the 24-hour window. Stable.
    /// </summary>
    internal static DateTime? PlannedCheckIn(string reference, string passengerName, DateTime departure)
    {
        var booking = new JavaRandom(FlightStatusEngine.StableSeed($"checkin|{reference}"));
        if (booking.NextInt(100) < NoShowPercent) return null;
        int roll = booking.NextInt(100);
        double u = booking.NextDouble();
        double hoursBefore = roll < 15 ? 20 + 3.9 * u : roll < 40 ? 12 + 8 * u : 1 + 11 * u;
        int jitter = new JavaRandom(FlightStatusEngine.StableSeed($"checkin|{reference}|{passengerName}")).NextInt(5);
        DateTime at = departure.AddHours(-hoursBefore).AddMinutes(jitter);
        DateTime latest = departure.AddMinutes(-1);
        return at < latest ? at : latest;
    }

    /// <summary>
    /// Checks in every demo traveller whose planned moment has passed. A planned moment is always before its flight's departure,
    /// so a traveller due since the previous run is on a flight departing after that run and at most 24 hours from now: only
    /// those flights are scanned (this runs every minute while holding the store's lock). The first run, or one after a reset,
    /// looks back a week. Each check-in is stamped with its planned moment, so catching up still looks natural.
    /// Returns how many were added.
    /// </summary>
    public int ProgressCheckIns(DateTime now)
    {
        DateTime after = now.AddDays(-CheckInLookbackDays);
        string read = _store.GetSetting(BookingStore.SettingDemoCheckInsThrough, "");
        if (DateTime.TryParse(read, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out DateTime through)
            && through <= now && through > after)
            after = through;
        DateTime until = now.AddHours(AppData.CheckInWindowHours);

        // Every flight that has demo bookings, active or not: a deactivated flight still flies with the bookings it has.
        var flights = new List<(string FlightNo, DateTime Date)>();
        foreach (string key in _store.LoadSimulatedFlightDates())
        {
            string[] parts = key.Split('|');
            DateTime date = DateTime.ParseExact(parts[1], "yyyyMMdd", CultureInfo.InvariantCulture);
            DateTime departure = AppData.DepartureOf(parts[0], date);
            if (departure > after && departure <= until) flights.Add((parts[0], date));
        }

        var due = new List<CheckInRecord>();
        foreach (SimCheckInCandidate c in _store.LoadSimulatedCheckInCandidates(flights))
        {
            DateTime departure = AppData.DepartureOf(c.FlightNo, c.FlightDate);
            if (PlannedCheckIn(c.Reference, c.PassengerName, departure) is { } at && at <= now)
                due.Add(new CheckInRecord(c.Reference, c.PassengerName, c.FlightNo, c.FlightDate, c.SeatCode, at));
        }
        // Saved together with the watermark, which only moves if nothing (a reset, a newly generated departed flight) changed it
        // since it was read above.
        int saved = _store.SaveSimulatedCheckIns(due, read, now);
        return saved;
    }

    /// <summary>Removes all demo bookings and holds and generates them again. Real bookings are never touched.</summary>
    public int Reset(DateTime now, int? horizonDays = null)
    {
        _store.DeleteSimulatedBookings();
        return EnsureLoad(now, horizonDays);
    }
}
