namespace SkyWings.Controllers;

public enum BannerKind { Confirmed, Canceled, Departed, Completed, NoShow }

/// <summary>How a booking is described at the top of the details page.</summary>
public sealed record BookingBanner(BannerKind Kind, string Text, bool CanModify, string PolicyText);

/// <summary>What cancelling would cost and return, shown before the traveller confirms.</summary>
public sealed record CancelPreview(string Reference, decimal TotalPaid, decimal Rate, decimal Refund)
{
    public string ConfirmationText =>
        $"Cancel booking {Reference}?\n\n" +
        $"Total paid: PHP {TotalPaid:N2}\n" +
        $"Refund ({Rate:P0}): PHP {Refund:N2}\n\n" +
        "This cannot be undone and your seats will be released.";
}

public enum CancelOutcome { Canceled, AlreadyChanged, StoreError }

public sealed record CancelResult(CancelOutcome Outcome, Exception? Error = null);

/// <summary>What the email bar should show for the current booking.</summary>
public sealed record EmailBarState(bool HasEmail, bool Subscribed, string StatusText, string ToggleText);

/// <summary>
/// Controller for <c>ManageBookingForm</c>: customer lookup (with the brute-force lock), the booking banner and refund policy,
/// cancelling, and the email preferences. Holds the booking being viewed. Has no WinForms dependency.
/// </summary>
public sealed class ManageBookingController
{
    private const string Screen = "Manage booking";
    private readonly BookingStore _store;

    public ManageBookingController(BookingStore store, string operatorName = "desk")
    {
        _store = store;
        _operator = operatorName;
    }

    private readonly string _operator;

    /// <summary>The booking being viewed, or null while the lookup page is showing.</summary>
    public BookingRecord? Current { get; private set; }

    /// <summary>The address receipts for the current booking go to, or null if none was given.</summary>
    public string? ContactEmail { get; private set; }

    // ------------------------------------------------------------------ lookup

    /// <summary>Finds a booking by its reference. A reference that matches nothing counts towards the lookup lock.</summary>
    public LookupResult Find(string reference)
    {
        LookupResult result = CustomerLookup.Find(_store, Screen, "ManageBooking", reference);
        if (result.Found) Current = result.Booking;
        return result;
    }

    /// <summary>Leaves the booking view.</summary>
    public void Clear()
    {
        Current = null;
        ContactEmail = null;
    }

    /// <summary>Re-reads the current booking from the database. Returns null (and clears) if it is gone.</summary>
    public BookingRecord? Reload()
    {
        if (Current is null) return null;
        BookingRecord? fresh = _store.FindByReference(Current.Reference);
        if (fresh is null) { Clear(); return null; }
        Current = fresh;
        return fresh;
    }

    // ------------------------------------------------------------------ printing

    /// <summary>The printable receipt for the booking being viewed, or null while the lookup page is showing.</summary>
    public ReceiptData? BuildReceipt()
    {
        if (Current is null) return null;
        // The breakdown is rebuilt from the current flight list (see ReceiptData.FromStored).
        IEnumerable<Flight> flights = _store.LoadActiveFlights(domestic: true).Concat(_store.LoadActiveFlights(domestic: false));
        return ReceiptData.FromStored(Current, flights);
    }

    // ------------------------------------------------------------------ banner and refund

    /// <param name="trip">Where the trip is now; when given, a finished trip says it was completed (and where it landed) or a no-show.</param>
    public static BookingBanner DescribeBooking(BookingRecord booking, TripStatusInfo? trip = null, RefundRecord? refund = null)
    {
        bool canModify = BookingSummary.CanModify(booking);
        string policy = canModify
            ? $"If you cancel now you get {AppData.RefundRate(booking.FlightDateValue):P0} of the fare back " +
              $"(PHP {AppData.ComputeRefundAmount(booking.TotalAmount, booking.FlightDateValue):N2}). " +
              "Refund depends on the outbound date: 80% a week or more before, 50% from 1 day, 20% on the day."
            : "";

        if (booking.Status == AppData.StatusCanceled)
            return new BookingBanner(BannerKind.Canceled,
                booking.RefundAmount > 0
                    ? $"This booking was canceled. PHP {booking.RefundAmount:N2} is being refunded" +
                      (refund is null ? "." : $" to {refund.Method} {refund.AccountMasked}. Refund reference {refund.Reference}.")
                    : "This booking was canceled.",
                canModify, policy);
        if (!canModify && trip is { Phase: TripPhase.Completed, LandedAt: { } landed })
            return new BookingBanner(BannerKind.Completed,
                $"This trip is completed. It landed in {trip.Destination} at {landed:h:mm tt} on {landed:MMM d}.", false, policy);
        if (!canModify && trip is { Phase: TripPhase.NoShow })
            return new BookingBanner(BannerKind.NoShow,
                "No-show: nobody on this booking checked in, so the flight left without them.", false, policy);
        if (!canModify)
            return new BookingBanner(BannerKind.Departed,
                "This flight has already departed, so the booking can no longer be changed.", false, policy);
        return new BookingBanner(BannerKind.Confirmed, "Confirmed. You can change seats or cancel below.", true, policy);
    }

    /// <summary>Where the open booking's trip is at <paramref name="now"/>, or null when no booking is open.</summary>
    /// <exception cref="IOException">The database could not be read.</exception>
    public TripStatusInfo? CurrentTrip(DateTime now)
        => Current is null ? null : TripStatus.Of(Current, _store.LoadCheckIns(Current.Reference), now);

    /// <summary>The refund record of the open (canceled) booking, or null when there is none or it cannot be read.</summary>
    public RefundRecord? CurrentRefund()
    {
        if (Current is null || Current.Status != AppData.StatusCanceled) return null;
        try { return _store.LoadRefund(Current.Reference); }
        catch (IOException ex) { AppLogger.Error("ManageBooking", ex); return null; }
    }

    /// <summary>The refund the traveller would get today, or null when the booking cannot be changed.</summary>
    public CancelPreview? PreviewCancel()
    {
        if (Current is null || !BookingSummary.CanModify(Current)) return null;
        return new CancelPreview(Current.Reference, Current.TotalAmount,
            AppData.RefundRate(Current.FlightDateValue),
            AppData.ComputeRefundAmount(Current.TotalAmount, Current.FlightDateValue));
    }

    /// <summary>Cancels the current booking and records the refund. Call after the traveller confirmed the preview.</summary>
    public CancelResult Cancel()
    {
        CancelPreview? preview = PreviewCancel();
        if (preview is null) return new CancelResult(CancelOutcome.AlreadyChanged);
        try
        {
            return _store.CancelBooking(preview.Reference, preview.Refund)
                ? new CancelResult(CancelOutcome.Canceled)
                : new CancelResult(CancelOutcome.AlreadyChanged);
        }
        catch (IOException ex)
        {
            AppLogger.Error("ManageBooking", ex);
            return new CancelResult(CancelOutcome.StoreError, ex);
        }
    }

    // ------------------------------------------------------------------ refund requests

    /// <summary>Writes down that the customer asked for a refund (usually by email). Staff then cancel the booking, which closes the request.</summary>
    public RefundRequestResult LogRefundRequest()
    {
        if (Current is null) return new RefundRequestResult(false, "No booking is open.");
        try
        {
            RefundRequestResult result = _store.LogRefundRequest(Current.Reference, _operator);
            if (result.Ok) _store.WriteAudit(_operator, "STAFF", "REFUND_REQUEST_LOGGED", Current.Reference);
            return result;
        }
        catch (IOException ex)
        {
            AppLogger.Error("ManageBooking", ex);
            return new RefundRequestResult(false, "The request could not be saved. Please try again.");
        }
    }

    /// <summary>True when the open booking already has a refund request waiting.</summary>
    public bool HasOpenRefundRequest()
    {
        if (Current is null) return false;
        try { return _store.LoadRefundRequests().Any(r => r.Reference == Current.Reference); }
        catch (IOException ex) { AppLogger.Error("ManageBooking", ex); return false; }
    }

    // ------------------------------------------------------------------ changing the flight

    /// <summary>Why the open booking's outbound flight cannot be changed, or null when it can.</summary>
    public string? ChangeFlightRefusal(DateTime now)
    {
        if (Current is null) return "No booking is open.";
        try { return FlightChangeRules.BookingRefusal(Current, _store.HasOutboundCheckIn(Current.Reference), now); }
        catch (IOException ex) { AppLogger.Error("ManageBooking", ex); return "The booking could not be checked. Please try again."; }
    }

    /// <summary>The flights on the same route on <paramref name="date"/>: seats left in the booked cabin, the cost, and why one is not possible.</summary>
    /// <exception cref="IOException">The database could not be read.</exception>
    public List<FlightChangeOption> ChangeOptions(DateTime date, DateTime now)
    {
        var options = new List<FlightChangeOption>();
        if (Current is null) return options;
        BookingRecord booking = Current;
        List<FlightAdminRow> all = _store.LoadAllFlights();
        FlightAdminRow? oldRow = all.FirstOrDefault(f => f.FlightNo == booking.FlightNo);
        if (oldRow is null) return options;
        var oldFlight = new Flight(oldRow.FlightNo, oldRow.Origin, oldRow.Destination, oldRow.BasePrice);
        foreach (FlightAdminRow row in all.Where(f => f.Status == "Active" && f.Origin == booking.Origin && f.Destination == booking.Destination)
                     .OrderBy(f => f.FlightNo))
        {
            var flight = new Flight(row.FlightNo, row.Origin, row.Destination, row.BasePrice);
            DateTime departure = AppData.DepartureOf(flight.FlightNo, date);
            int left = _store.CabinSeatsLeft(flight.FlightNo, date, booking.ClassName);
            string? problem = FlightChangeRules.TargetRefusal(booking, flight, date, now);
            if (problem is null && left < booking.Passengers.Count)
                problem = left == 0 ? $"No {booking.ClassName} seats left." : $"Only {left} {booking.ClassName} seat(s) left.";
            options.Add(new FlightChangeOption(flight, departure, left, FlightChangeRules.Quote(booking, oldFlight, flight, date), problem));
        }
        return options;
    }

    /// <summary>Moves the open booking to the chosen flight and date. Call after the traveller confirmed the cost.</summary>
    public FlightChangeResult ChangeFlight(FlightChangeOption option, DateTime date)
    {
        if (Current is null) return FlightChangeResult.Refused("No booking is open.");
        try
        {
            string from = $"{Current.FlightNo} {Current.FlightDateValue:yyyy-MM-dd}";
            FlightChangeResult result = _store.ChangeOutboundFlight(Current.Reference, option.Flight.FlightNo, date, "customer");
            if (result.Outcome == FlightChangeOutcome.Changed)
                _store.WriteAudit("customer", "CUSTOMER", "BOOKING_REBOOKED", Current.Reference,
                    $"{from} to {option.Flight.FlightNo} {date:yyyy-MM-dd}; charged PHP {option.Quote.Total:N2}");
            return result;
        }
        catch (IOException ex)
        {
            AppLogger.Error("ManageBooking", ex);
            return new FlightChangeResult(FlightChangeOutcome.StoreError, "The flight could not be changed. Please try again.", Array.Empty<string>());
        }
    }

    // ------------------------------------------------------------------ email preferences

    /// <summary>Loads the receipt address for the current booking and describes the email bar.</summary>
    public EmailBarState RefreshEmail()
    {
        ContactEmail = null;
        if (Current is not null)
        {
            try { ContactEmail = _store.LoadBookingContact(Current.Reference); }
            catch (Exception ex) { AppLogger.Error("ManageBooking", ex); }
        }

        if (string.IsNullOrEmpty(ContactEmail))
            return new EmailBarState(false, false, "No email address was given for this booking.", "Stop emails");

        bool subscribed = _store.IsEmailSubscribed(ContactEmail);
        return new EmailBarState(true, subscribed,
            subscribed ? $"Receipts go to {MaskEmail(ContactEmail)}." : "Emails are switched off for this address.",
            subscribed ? "Stop emails" : "Resume emails");
    }

    /// <summary>Switches receipt emails on or off for the address and records it in the audit log. False if it could not be saved.</summary>
    public bool ToggleEmail()
    {
        if (Current is null || string.IsNullOrEmpty(ContactEmail)) return true;
        try
        {
            bool nowSubscribed = !_store.IsEmailSubscribed(ContactEmail);
            _store.SetEmailSubscribed(ContactEmail, nowSubscribed);
            _store.WriteAudit("customer", "CUSTOMER", nowSubscribed ? "EMAIL_RESUME" : "EMAIL_UNSUBSCRIBE", Current.Reference);
            return true;
        }
        catch (IOException ex)
        {
            AppLogger.Error("ManageBooking", ex);
            return false;
        }
    }

    /// <summary>Sends the receipt again. Returns null when there is no booking or address to send to.</summary>
    public async Task<SkyWings.Email.EmailSendResult?> SendReceiptAsync()
    {
        if (Current is null || string.IsNullOrEmpty(ContactEmail)) return null;
        return await SkyWings.Email.EmailServices.Create(_store).SendReceiptAsync(Current, ContactEmail);
    }

    /// <summary>Shows only the first letter and the domain, so a screen in front of other people does not reveal the address.</summary>
    public static string MaskEmail(string email)
    {
        int at = email.IndexOf('@');
        return at <= 0 ? email : email[0] + "***" + email[at..];
    }
}
