namespace SkyWings.Email;

/// <summary>
/// Sends a booking's receipt email. Respects the unsubscribe list, never lets an email problem affect the booking, and
/// records an audit entry that names the booking and the channel but never the address or the body.
/// </summary>
public sealed class BookingEmailService
{
    private readonly BookingStore _store;
    private readonly IEmailSender _sender;
    private readonly IQrRenderer _qr;
    private readonly string? _unsubscribeMailbox;

    public BookingEmailService(BookingStore store, IEmailSender sender, IQrRenderer qr, string? unsubscribeMailbox = null)
    {
        _store = store;
        _sender = sender;
        _qr = qr;
        _unsubscribeMailbox = unsubscribeMailbox;
    }

    public async Task<EmailSendResult> SendReceiptAsync(BookingRecord booking, string toEmail)
    {
        try
        {
            if (!BookingRules.IsValidEmail(toEmail))
                return new EmailSendResult(false, "Skipped", "The email address is not valid.");
            if (!_store.IsEmailSubscribed(toEmail))
                return new EmailSendResult(false, "Skipped", "This address has unsubscribed from SkyWings emails.");

            byte[] png = _qr.RenderPng(QrPayload.Build(booking.Reference));
            EmailMessage message = BookingEmailBuilder.Build(booking, toEmail.Trim(), png, _unsubscribeMailbox);
            EmailSendResult result = await _sender.SendAsync(message).ConfigureAwait(false);
            _store.WriteAudit("system", "SYSTEM", "EMAIL_RECEIPT", booking.Reference, result.Channel);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppLogger.Error("Email", ex);
            return new EmailSendResult(false, "Failed", "The receipt email could not be sent or saved.");
        }
    }
}

/// <summary>Sends the waitlist offer email. Same rules as receipts: unsubscribed addresses are skipped, failures never propagate.</summary>
public sealed class WaitlistEmailService
{
    private readonly BookingStore _store;
    private readonly IEmailSender _sender;
    private readonly string? _mailbox;

    public WaitlistEmailService(BookingStore store, IEmailSender sender, string? mailbox = null)
    {
        _store = store;
        _sender = sender;
        _mailbox = mailbox;
    }

    /// <summary>Sends the offer for every entry the store offers seats to, from now on.</summary>
    public void Attach() => _store.WaitlistOfferMade += entry => _ = SendOfferAsync(entry);

    public async Task<EmailSendResult> SendOfferAsync(WaitlistEntry entry)
    {
        try
        {
            if (!BookingRules.IsValidEmail(entry.Email))
                return new EmailSendResult(false, "Skipped", "The email address is not valid.");
            if (!_store.IsEmailSubscribed(entry.Email))
                return new EmailSendResult(false, "Skipped", "This address has unsubscribed from SkyWings emails.");
            EmailSendResult result = await _sender.SendAsync(BookingEmailBuilder.BuildWaitlistOffer(entry, _mailbox)).ConfigureAwait(false);
            _store.WriteAudit("system", "SYSTEM", "EMAIL_WAITLIST_OFFER", entry.BookingReference, result.Channel);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppLogger.Error("Email", ex);
            return new EmailSendResult(false, "Failed", "The waitlist email could not be sent or saved.");
        }
    }
}

/// <summary>Builds the email sender and service from the settings in <c>db.properties</c>.</summary>
public static class EmailServices
{
    public static IEmailSender CreateSender(SmtpSettings settings)
    {
        var outbox = new FileOutboxEmailSender();
        return settings.IsConfigured ? new FallbackEmailSender(new SmtpEmailSender(settings), outbox) : outbox;
    }

    public static WaitlistEmailService CreateWaitlist(BookingStore store)
        => new(store, CreateSender(AppConfig.Smtp), AppConfig.Smtp.IsConfigured ? AppConfig.Smtp.From : null);

    public static BookingEmailService Create(BookingStore store)
        => new(store, CreateSender(AppConfig.Smtp), new QrCodeRenderer(), AppConfig.Smtp.IsConfigured ? AppConfig.Smtp.From : null);
}
