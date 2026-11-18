namespace SkyWings.Controllers;

public enum CheckInBannerKind { Canceled, NotYetOpen, Open, Closed }

/// <summary>One traveller on the check-in list. <see cref="Record"/> is set once they are checked in on this leg.</summary>
public sealed record TravellerCheckIn(string Name, string Seat, CheckInRecord? Record)
{
    public bool IsCheckedIn => Record is not null;
    public bool HasSeat => Seat.Length > 0;
}

/// <summary>Everything the check-in page shows for one leg of a booking.</summary>
public sealed record CheckInView(
    CheckInBannerKind Banner, string BannerText, bool Open, IReadOnlyList<TravellerCheckIn> Travellers)
{
    public bool AnyCheckedIn => Travellers.Any(t => t.IsCheckedIn);
}

/// <summary>Result of checking several travellers in: who was checked in and what went wrong for the others.</summary>
public sealed record CheckInSummary(IReadOnlyList<string> Done, IReadOnlyList<string> Problems);

/// <summary>
/// Controller for <c>CheckInForm</c>: lookup, the open/closed window for each leg, and checking travellers in.
/// Check-in opens <see cref="AppData.CheckInWindowHours"/> hours before departure and closes at departure.
/// Has no WinForms dependency.
/// </summary>
public sealed class CheckInController
{
    private readonly BookingStore _store;

    public CheckInController(BookingStore store)
    {
        _store = store;
    }

    public BookingRecord? Current { get; private set; }

    public LookupResult Find(string reference, string lastName)
    {
        LookupResult result = CustomerLookup.Find(_store, "Check-in", "CheckIn", reference, lastName);
        if (result.Found) Current = result.Booking;
        return result;
    }

    public void Clear() => Current = null;

    /// <summary>One label per flight leg: the outbound one, plus the return one for round trips.</summary>
    public IReadOnlyList<string> LegLabels()
    {
        BookingRecord b = Current ?? throw new InvalidOperationException("No booking is open.");
        var labels = new List<string>
        {
            $"Outbound · {b.FlightNo} · {b.Origin} → {b.Destination} · {b.FlightDateValue:MMM d}"
        };
        if (b.ReturnFlightNo is not null)
            labels.Add($"Return · {b.ReturnFlightNo} · {b.ReturnOrigin} → {b.ReturnDestination} · {b.ReturnFlightDateValue:MMM d}");
        return labels;
    }

    public (string FlightNo, DateTime Date) Leg(bool returnLeg)
    {
        BookingRecord b = Current ?? throw new InvalidOperationException("No booking is open.");
        return returnLeg ? (b.ReturnFlightNo!, b.ReturnFlightDateValue!.Value) : (b.FlightNo, b.FlightDateValue);
    }

    /// <summary>Works out the check-in state of a leg at <paramref name="now"/>.</summary>
    /// <exception cref="IOException">The database could not be read.</exception>
    public CheckInView Load(bool returnLeg, DateTime now)
    {
        BookingRecord booking = Current ?? throw new InvalidOperationException("No booking is open.");
        var (flightNo, date) = Leg(returnLeg);
        DateTime departure = AppData.DepartureOf(flightNo, date);
        DateTime opens = departure.AddHours(-AppData.CheckInWindowHours);
        bool open = _store.IsCheckInOpen(booking.Reference, flightNo, date, now);
        List<CheckInRecord> records = _store.LoadCheckIns(booking.Reference);

        (CheckInBannerKind kind, string text) = booking.Status != AppData.StatusConfirmed
            ? (CheckInBannerKind.Canceled, "This booking is canceled, so it can't be checked in.")
            : now < opens
                ? (CheckInBannerKind.NotYetOpen,
                    $"Check-in opens {opens:MMM d, h:mm tt}, {AppData.CheckInWindowHours} hours before departure.")
                : now < departure
                    ? (CheckInBannerKind.Open, $"Check-in is open until departure at {departure:h:mm tt}.")
                    : (CheckInBannerKind.Closed, "Check-in has closed. This flight has already departed.");

        var travellers = booking.Passengers.Select(p => new TravellerCheckIn(
            p.Name,
            returnLeg ? p.ReturnSeatCode : p.SeatCode,
            records.FirstOrDefault(c => c.PassengerName == p.Name && c.FlightNo == flightNo && c.FlightDate.Date == date.Date)))
            .ToList();
        return new CheckInView(kind, text, open, travellers);
    }

    /// <summary>Checks the named travellers in on a leg. One traveller failing does not stop the others.</summary>
    public CheckInSummary CheckIn(IEnumerable<string> names, bool returnLeg, DateTime now)
    {
        BookingRecord booking = Current ?? throw new InvalidOperationException("No booking is open.");
        var done = new List<string>();
        var problems = new List<string>();
        foreach (string name in names)
        {
            try
            {
                switch (_store.CheckIn(booking.Reference, name, returnLeg, now))
                {
                    case CheckInOutcome.CheckedIn: done.Add(name); break;
                    case CheckInOutcome.AlreadyCheckedIn: break;
                    case CheckInOutcome.NotOpen: problems.Add($"{name}: check-in is not open."); break;
                    case CheckInOutcome.NoSeat: problems.Add($"{name}: no seat is assigned."); break;
                    default: problems.Add($"{name}: not found on this booking."); break;
                }
            }
            catch (IOException ex)
            {
                AppLogger.Error("CheckIn", ex);
                problems.Add($"{name}: {ex.Message}");
            }
        }
        return new CheckInSummary(done, problems);
    }

    /// <summary>Check-in records on this leg for boarding passes (all travellers, or only those in <paramref name="only"/>).</summary>
    public List<CheckInRecord> PassRecords(bool returnLeg, IReadOnlyCollection<string>? only)
    {
        BookingRecord booking = Current ?? throw new InvalidOperationException("No booking is open.");
        var (flightNo, date) = Leg(returnLeg);
        return _store.LoadCheckIns(booking.Reference)
            .Where(c => c.FlightNo == flightNo && c.FlightDate.Date == date.Date)
            .Where(c => only is null || only.Contains(c.PassengerName))
            .ToList();
    }
}
