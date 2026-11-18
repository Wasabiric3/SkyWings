namespace SkyWings.Controllers;

public enum BannerKind { Confirmed, Canceled, Departed }

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

    public ManageBookingController(BookingStore store)
    {
        _store = store;
    }

    /// <summary>The booking being viewed, or null while the lookup page is showing.</summary>
    public BookingRecord? Current { get; private set; }

    /// <summary>The address receipts for the current booking go to, or null if none was given.</summary>
    public string? ContactEmail { get; private set; }

    // ------------------------------------------------------------------ lookup

    /// <summary>Finds a booking by reference and traveller last name. Wrong details count towards the lookup lock.</summary>
    public LookupResult Find(string reference, string lastName)
    {
        LookupResult result = CustomerLookup.Find(_store, Screen, "ManageBooking", reference, lastName);
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

    // ------------------------------------------------------------------ banner and refund

    public static BookingBanner DescribeBooking(BookingRecord booking)
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
                    ? $"This booking was canceled. PHP {booking.RefundAmount:N2} is being refunded."
                    : "This booking was canceled.",
                canModify, policy);
        if (!canModify)
            return new BookingBanner(BannerKind.Departed,
                "This flight has already departed, so the booking can no longer be changed.", false, policy);
        return new BookingBanner(BannerKind.Confirmed, "Confirmed. You can change seats or cancel below.", true, policy);
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
