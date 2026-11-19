namespace SkyWings.Controllers;

/// <summary>What the traveller form fields contain, trimmed by the view.</summary>
public sealed record PassengerInput(
    string Name, string Address, string Contact, string IdNumber, string Birthday, string Email, bool Senior, bool Pwd);

/// <summary>Field-by-field result of checking a traveller. A null message means that field is fine.</summary>
public sealed record PassengerValidation
{
    /// <summary>A problem with the booking as a whole (too many travellers, control characters). Shown on the name row.</summary>
    public string? General { get; init; }
    public string? Name { get; init; }
    public string? IdNumber { get; init; }
    public string? Address { get; init; }
    public string? Contact { get; init; }
    public string? Email { get; init; }
    public string? Birthday { get; init; }
    public int Age { get; init; }

    public bool IsValid => General is null && Name is null && IdNumber is null && Address is null
                           && Contact is null && Email is null && Birthday is null;
}

public enum SeatHoldOutcome { Held, WrongSeatCount, SeatTaken, StoreError, TooManyHolds }

/// <summary>Result of holding seats. <see cref="Message"/> is user-facing; <see cref="Error"/> is set for <see cref="SeatHoldOutcome.StoreError"/>.</summary>
public sealed record SeatHoldResult(SeatHoldOutcome Outcome, string Message = "", Exception? Error = null)
{
    public bool Succeeded => Outcome == SeatHoldOutcome.Held;
}

/// <summary>Result of saving a booking. On failure <see cref="Error"/> explains why and nothing was recorded.</summary>
public sealed record SubmitResult(BookingRecord? Booking, Exception? Error = null)
{
    public bool Succeeded => Booking is not null;
}

/// <summary>
/// Controller for <c>BookingWizardForm</c>. Owns the booking session, the seat holds and the rules for flights, travellers
/// and payment so the form only draws controls and forwards what the user did. Has no WinForms dependency.
/// </summary>
public sealed class BookingWizardController
{
    private readonly BookingStore _store;
    private readonly List<(string FlightNo, DateTime Date)> _heldLegs = new();

    public BookingWizardController(BookingStore store)
    {
        _store = store;
    }

    public BookingSession Session { get; } = new();

    /// <summary>The id that marks this wizard's seat holds, or null if none were placed yet.</summary>
    public string? HoldOwner { get; private set; }

    public bool BookingSubmitted { get; private set; }

    // ------------------------------------------------------------------ flights

    /// <summary>
    /// Active flights grouped by place: for each destination, the flights out of Manila (earliest first) and then the ones coming back.
    /// </summary>
    /// <exception cref="IOException">The database could not be read.</exception>
    public List<Flight> LoadFlights()
    {
        _store.SeedDefaultFlights();
        DateTime today = DateTime.Today;
        return _store.LoadActiveFlights(true).Concat(_store.LoadActiveFlights(false))
            .OrderBy(f => IsFromManila(f) ? f.Destination : f.Origin)
            .ThenBy(f => IsFromManila(f) ? 0 : 1)
            .ThenBy(f => AppData.DepartureOf(f.FlightNo, today))
            .ToList();
    }

    private static bool IsFromManila(Flight f) => f.Origin.StartsWith("Manila", StringComparison.Ordinal);

    /// <summary>
    /// One line about a flight on a date: departure time, duration and the seats still open in <paramref name="className"/> that
    /// day (or "bookings closed" inside the cutoff). A database problem only drops the seat count.
    /// </summary>
    public string DescribeFlight(Flight flight, DateTime date, string className, DateTime now)
    {
        string time = AppData.FlightScheduledTimes.TryGetValue(flight.FlightNo, out TimeOnly t) ? t.ToString("h:mm tt") : "—";
        int minutes = AppData.FlightEstDurationMin.GetValueOrDefault(flight.FlightNo, 0);
        string duration = minutes > 0 ? $" · about {minutes / 60}h {minutes % 60:00}m" : "";
        if (!AppData.IsBookable(flight.FlightNo, date, now)) return $"Departs {time}{duration} · bookings closed";
        string seats = "";
        try
        {
            if (_store.LoadAircraft(flight.FlightNo).Cabin(className) is { } cabin)
                seats = $" · {_store.CabinSeatsLeft(flight.FlightNo, date, className)} of {cabin.Seats} {className} seats open";
        }
        catch (IOException ex)
        {
            AppLogger.Error("Wizard", ex);
        }
        return $"Departs {time}{duration}{seats}";
    }

    /// <summary>
    /// Why a flight cannot be booked for a date, or null when it can: bookings close <see cref="AppData.BookingCutoffHours"/> hours
    /// before departure, so a same-day flight is fine until then.
    /// </summary>
    public string? FlightDateProblem(Flight flight, DateTime date, DateTime now)
        => AppData.IsBookable(flight.FlightNo, date, now)
            ? null
            : $"Bookings for {flight.FlightNo} on {date:MMM d} closed at {AppData.BookingCutoff(flight.FlightNo, date):h:mm tt}, " +
              $"{AppData.BookingCutoffHours} hours before departure. Choose a later flight or date.";

    // ------------------------------------------------------------------ travellers

    /// <summary>Checks a traveller against the booking rules without changing the session.</summary>
    public PassengerValidation ValidatePassenger(PassengerInput input, DateTime today)
    {
        if (Session.Passengers.Count >= BookingRules.MaxPassengersPerBooking)
            return new PassengerValidation { General = $"A booking can have at most {BookingRules.MaxPassengersPerBooking} travellers." };
        if (new[] { input.Name, input.Address, input.Contact, input.IdNumber, input.Birthday, input.Email }
            .Any(BookingRules.HasControlCharacters))
            return new PassengerValidation { General = "Remove any line breaks or special control characters." };

        string? name = null, id = null, address = null, contact = null, email = null, birthdayMsg = null;
        if (input.Name.Length == 0) name = "Enter the traveller's full name.";
        else if (Session.Passengers.Any(p => string.Equals(p.Name, input.Name, StringComparison.OrdinalIgnoreCase)))
            name = "A traveller with this name was already added.";
        if (input.IdNumber.Length == 0) id = "Enter an ID number.";
        if (input.Address.Length == 0) address = "Enter the home address.";
        if (input.Contact.Length == 0) contact = "Enter a contact number.";
        else if (input.Contact.Any(c => !char.IsDigit(c) && c is not '+' and not '-' and not ' '))
            contact = "Use digits, spaces, + or - only.";
        if (input.Email.Length > 0 && !BookingRules.IsValidEmail(input.Email))
            email = "Enter an address like name@example.com, or leave it empty.";

        int age = 0;
        if (!AppData.TryParseBirthday(input.Birthday, out DateTime birthday) || birthday.Date > today)
            birthdayMsg = "Use yyyy-MM-dd, e.g. 1990-04-23";
        else
        {
            age = AppData.AgeOn(birthday, today);
            if (age > AppData.MaxPassengerAge) birthdayMsg = "Enter a valid birthday.";
            else if (input.Senior && age < AppData.SeniorMinimumAge)
                birthdayMsg = $"Senior discount needs {AppData.SeniorMinimumAge}+ (age {age})";
        }

        return new PassengerValidation
        {
            Name = name, IdNumber = id, Address = address, Contact = contact, Email = email, Birthday = birthdayMsg, Age = age
        };
    }

    /// <summary>Validates and adds a traveller. Returns the validation so the view can show what is wrong.</summary>
    public PassengerValidation AddPassenger(PassengerInput input, DateTime today)
    {
        PassengerValidation result = ValidatePassenger(input, today);
        if (!result.IsValid) return result;
        Session.Passengers.Add(new PassengerDraft
        {
            Name = input.Name, Address = input.Address, Contact = input.Contact, Birthday = input.Birthday,
            IdNumber = input.IdNumber, Senior = input.Senior, Pwd = input.Pwd, Age = result.Age
        });
        // One receipt address per booking: the latest entry wins.
        if (input.Email.Length > 0) Session.ContactEmail = input.Email;
        return result;
    }

    public bool RemovePassenger(int index)
    {
        if (index < 0 || index >= Session.Passengers.Count) return false;
        Session.Passengers.RemoveAt(index);
        return true;
    }

    // ------------------------------------------------------------------ seats

    public void PurgeExpiredHolds() => _store.PurgeExpiredSeatHolds();

    /// <summary>The cabin to draw for a flight in the session's class (rows, seat letters, aisles), or null if its aircraft has none.</summary>
    public CabinLayout? SeatCabin(Flight flight) => _store.LoadAircraft(flight.FlightNo).Cabin(Session.ClassName);


    /// <summary>"Airbus A320 · Business cabin 2-2 · 3 rows" for the seat step (both aircraft when a round trip uses two types).</summary>
    public string AircraftSummary(Flight outbound, Flight? returnFlight)
    {
        string One(Flight flight)
        {
            Aircraft aircraft = _store.LoadAircraft(flight.FlightNo);
            CabinLayout? cabin = aircraft.Cabin(Session.ClassName);
            return cabin is null ? aircraft.Model : $"{aircraft.Model} · {cabin.ClassName} cabin {cabin.LayoutText} · {cabin.Rows} rows";
        }
        string text = One(outbound);
        if (returnFlight is not null && _store.LoadAircraft(returnFlight.FlightNo).Id != _store.LoadAircraft(outbound.FlightNo).Id)
            text += "   |   Return: " + One(returnFlight);
        return text;
    }
    /// <summary>The classes a trip offers, in the usual order: the cabins that the outbound aircraft (and the return aircraft, if any) have.</summary>
    public IReadOnlyList<string> AvailableClasses(Flight outbound, Flight? returnFlight)
    {
        Aircraft outAircraft = _store.LoadAircraft(outbound.FlightNo);
        Aircraft? backAircraft = returnFlight is null ? null : _store.LoadAircraft(returnFlight.FlightNo);
        return AppData.ClassNames
            .Where(name => outAircraft.Cabin(name) is not null && (backAircraft is null || backAircraft.Cabin(name) is not null))
            .ToList();
    }

    public IReadOnlyCollection<string> TakenSeats(Flight flight, DateTime date) => _store.LoadTakenSeats(flight.FlightNo, date);

    /// <summary>Seats on this leg held by other people (not by this wizard).</summary>
    public IEnumerable<string> SeatsHeldByOthers(Flight flight, DateTime date)
        => _store.GetActiveHoldsForFlightDate(flight.FlightNo, date)
            .Where(h => h.Value.Owner != HoldOwner).Select(h => h.Key);

    /// <summary>
    /// Holds the chosen seats for 10 minutes: one per traveller on each leg. Earlier holds from this wizard are released first,
    /// and any failure releases everything this wizard holds so a retry starts clean.
    /// </summary>
    public SeatHoldResult HoldSeats(IReadOnlyList<string> outbound, IReadOnlyList<string> returnLeg)
    {
        int need = Session.Passengers.Count;
        bool hasReturn = Session.ReturnFlight is not null && Session.ReturnDate is not null;
        if (outbound.Count != need || (hasReturn && returnLeg.Count != need))
            return new SeatHoldResult(SeatHoldOutcome.WrongSeatCount, $"Select exactly {need} seat(s) for each flight leg.");

        string owner = HoldOwner ??= Guid.NewGuid().ToString();
        ReleaseOwnHolds();   // going back and continuing again must not collide with our own earlier holds
        Session.SeatAssignments.Clear();
        Session.ReturnSeatAssignments.Clear();
        try
        {
            SeatHoldResult? failure = TryHoldSeats(Session.SelectedFlight!, Session.FlightDate, outbound, Session.SeatAssignments, owner);
            if (failure is not null) return failure;
            if (hasReturn)
            {
                failure = TryHoldSeats(Session.ReturnFlight!, Session.ReturnDate!.Value, returnLeg, Session.ReturnSeatAssignments, owner);
                if (failure is not null)
                {
                    ReleaseOwnHolds();
                    Session.SeatAssignments.Clear();
                    return failure;
                }
            }
        }
        catch (IOException ex)
        {
            ReleaseOwnHolds();
            return new SeatHoldResult(SeatHoldOutcome.StoreError, ex.Message, ex);
        }
        catch (InvalidOperationException ex)
        {
            // Too many seats held at once (see BookingRules.MaxActiveSeatHoldsPerOwner).
            ReleaseOwnHolds();
            return new SeatHoldResult(SeatHoldOutcome.TooManyHolds, ex.Message);
        }
        return new SeatHoldResult(SeatHoldOutcome.Held);
    }

    private SeatHoldResult? TryHoldSeats(Flight flight, DateTime date, IReadOnlyList<string> seats,
        Dictionary<string, string> assignments, string owner)
    {
        for (int i = 0; i < seats.Count; i++)
        {
            if (!_store.AddSeatHold(flight.FlightNo, date, seats[i], owner))
            {
                ReleaseOwnHolds();
                assignments.Clear();
                return new SeatHoldResult(SeatHoldOutcome.SeatTaken,
                    $"Seat {seats[i]} on {flight.Origin} → {flight.Destination} was just taken. Please choose another seat.");
            }
            _heldLegs.Add((flight.FlightNo, date));
            assignments[Session.Passengers[i].Name] = seats[i];
        }
        return null;
    }

    /// <summary>Gives back every seat this wizard holds but keeps the owner id, so the wizard can hold again.</summary>
    public void ReleaseOwnHolds()
    {
        if (HoldOwner is null) { _heldLegs.Clear(); return; }
        foreach (var leg in _heldLegs.Distinct())
        {
            try { _store.RemoveHoldsForOwnerAndFlight(HoldOwner, leg.FlightNo, leg.Date); }
            catch (IOException ex) { AppLogger.Error("Wizard", ex); }
        }
        _heldLegs.Clear();
    }

    /// <summary>Releases all holds and forgets the owner id (the wizard is done or closing).</summary>
    public void ReleaseSeatHolds()
    {
        ReleaseOwnHolds();
        HoldOwner = null;
    }

    // ------------------------------------------------------------------ payment

    /// <summary>True when flight, travellers and one seat per traveller on each leg are all in place.</summary>
    public bool IsReadyForPayment()
        => Session.SelectedFlight is not null && Session.Passengers.Count > 0
           && Session.SeatAssignments.Count == Session.Passengers.Count
           && (Session.ReturnFlight is null || Session.ReturnSeatAssignments.Count == Session.Passengers.Count);

    /// <summary>Builds the review text shown before the traveller pays.</summary>
    public string BuildReviewText()
    {
        var s = Session;
        var lines = new List<string>
        {
            $"{s.SelectedFlight?.Origin} → {s.SelectedFlight?.Destination}   ·   {s.SelectedFlight?.FlightNo}   ·   {s.FlightDate:ddd, MMM d, yyyy}"
        };
        if (s.ReturnFlight is not null)
            lines.Add($"{s.ReturnFlight.Origin} → {s.ReturnFlight.Destination}   ·   {s.ReturnFlight.FlightNo}   ·   {s.ReturnDate:ddd, MMM d, yyyy}");
        lines.Add("");
        foreach (var p in s.Passengers)
        {
            string seat = s.SeatAssignments.GetValueOrDefault(p.Name, "—");
            string back = s.ReturnFlight is null ? "" : $"  ·  return {s.ReturnSeatAssignments.GetValueOrDefault(p.Name, "—")}";
            lines.Add($"{p.Name}   ·   seat {seat}{back}");
        }
        lines.Add("");
        lines.Add($"{s.ClassName}   ·   {s.BaggageOption}   ·   {s.MealOption}");
        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>
    /// Records the (demo) payment and saves the booking in one transaction. Only the last four characters of the account are kept.
    /// On success the seat holds are released and the session holds the booking reference. Call only when
    /// <see cref="IsReadyForPayment"/> is true and the account has at least four characters.
    /// </summary>
    public SubmitResult SubmitBooking(string paymentMethod, string account)
    {
        if (BookingSubmitted) throw new InvalidOperationException("This booking was already submitted.");
        if (!IsReadyForPayment()) throw new InvalidOperationException("The booking is incomplete.");
        if (account.Length < 4) throw new ArgumentException("Account needs at least 4 characters.", nameof(account));

        var s = Session;
        s.PaymentMethod = paymentMethod;
        s.MaskedAccount = "**** " + account[^4..];
        // 12 hex characters (48 bits): much harder to guess than the old 8, and the save refuses a duplicate.
        string reference = "SKY-" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant();
        decimal perPassenger = AppData.Round2(s.Total / s.Passengers.Count);
        var passengers = s.Passengers.Select(p => p.ToRecord(
            s.SeatAssignments.GetValueOrDefault(p.Name, ""), s.Addons, perPassenger,
            s.ReturnSeatAssignments.GetValueOrDefault(p.Name, ""))).ToList();
        Flight flight = s.SelectedFlight!;
        string est = AppData.FlightScheduledTimes.TryGetValue(flight.FlightNo, out TimeOnly time)
            ? $"{s.FlightDate:MMM dd, yyyy} {time:h:mm tt}" : "Scheduled";
        DateTime now = DateTime.Now;
        var booking = new BookingRecord(reference, now, flight.FlightNo, flight.Origin, flight.Destination, s.FlightDate,
            flight.Domestic, s.ClassName, s.PaymentMethod, s.MaskedAccount, s.Total, est, passengers,
            AppData.StatusConfirmed, AppData.PaymentPaid,
            "TXN-" + Guid.NewGuid().ToString("N")[..10].ToUpperInvariant(), now, 0m)
        {
            ReturnFlightNo = s.ReturnFlight?.FlightNo,
            ReturnOrigin = s.ReturnFlight?.Origin,
            ReturnDestination = s.ReturnFlight?.Destination,
            ReturnDomestic = s.ReturnFlight?.Domestic ?? false,
            ReturnFlightDateValue = s.ReturnDate,
            BaggageOption = s.BaggageOption,
            MealOption = s.MealOption,
            PromoCode = s.PromoCode
        };

        bool promoRedeemed = !string.IsNullOrWhiteSpace(s.PromoCode) && s.Quote.PromoDiscount > 0;
        try
        {
            // One transaction: booking, travellers, legs, options, promo and payment are saved together or not at all.
            _store.SaveConfirmedBooking(booking, promoRedeemed, s.Total, s.PaymentMethod, s.MaskedAccount,
                booking.PaymentTransactionRef, now);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException)
        {
            return new SubmitResult(null, ex);
        }

        if (s.WaitlistCode.Length > 0)
        {
            // The offered seats are now a real booking; a failure here must never undo the paid booking.
            try { _store.MarkWaitlistConverted(s.WaitlistCode); }
            catch (Exception ex) { AppLogger.Error("Waitlist", ex); }
        }

        ReleaseSeatHolds();
        BookingSubmitted = true;
        s.BookingReference = reference;
        return new SubmitResult(booking);
    }

    /// <summary>
    /// Saves the receipt address and sends the email. Runs after the booking is already stored, so a mail problem
    /// only produces a note for the receipt page and never undoes the booking.
    /// </summary>
    public async Task<string> EmailReceiptAsync(BookingRecord booking)
    {
        string email = Session.ContactEmail;
        if (email.Length == 0) return "";
        try
        {
            _store.SaveBookingContact(booking.Reference, email);
            SkyWings.Email.EmailSendResult result = await SkyWings.Email.EmailServices.Create(_store).SendReceiptAsync(booking, email);
            return result.Channel switch
            {
                "SMTP" => "A receipt with a QR code was emailed to " + email + ".",
                "Outbox" => "A receipt with a QR code was saved as a file: " + result.Detail,
                _ => "No email was sent. " + result.Detail
            };
        }
        catch (Exception ex) when (ex is IOException or ArgumentException)
        {
            AppLogger.Error("Email", ex);
            return "The booking is saved, but the receipt email could not be prepared.";
        }
    }
}
