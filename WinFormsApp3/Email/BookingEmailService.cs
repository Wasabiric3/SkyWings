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

    /// <summary>
    /// The PDFs sent with the email: always the booking receipt (the same A4 page "Print receipt" shows), and the boarding passes
    /// (the same pages the boarding pass print preview shows) once at least one traveller has checked in. A drawing problem must not
    /// stop the email, so each file that fails is logged and left out, and the email still goes out.
    /// </summary>
    private List<EmailAttachment> BuildAttachments(BookingRecord booking)
    {
        var files = new List<EmailAttachment>();
        try
        {
            IEnumerable<Flight> flights = _store.LoadActiveFlights(domestic: true).Concat(_store.LoadActiveFlights(domestic: false));
            byte[] pdf = ReceiptPdf.Create(ReceiptData.FromStored(booking, flights));
            files.Add(new EmailAttachment($"SkyWings-receipt-{booking.Reference}.pdf", pdf, "application/pdf", "booking receipt"));
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { AppLogger.Error("Email", ex); }

        try
        {
            List<BoardingPassData> passes = BoardingPassesOf(booking);
            if (passes.Count > 0)
                files.Add(new EmailAttachment($"SkyWings-boarding-pass-{booking.Reference}.pdf", BoardingPassPdf.Create(passes, _qr),
                    "application/pdf", passes.Count == 1 ? "boarding pass" : "boarding passes"));
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { AppLogger.Error("Email", ex); }
        return files;
    }

    /// <summary>One pass for every check-in on the booking, outbound first and then the return leg (none before anyone has checked in).</summary>
    private List<BoardingPassData> BoardingPassesOf(BookingRecord booking)
        => _store.LoadCheckIns(booking.Reference)
            .Select(record => (Record: record, ReturnLeg: IsReturnLeg(booking, record)))
            .OrderBy(x => x.ReturnLeg).ThenBy(x => x.Record.CheckedInAt)
            .Select(x => BoardingPassData.From(booking, x.ReturnLeg, x.Record))
            .ToList();

    private static bool IsReturnLeg(BookingRecord booking, CheckInRecord record)
        => booking.ReturnFlightNo is { } returnNo && record.FlightNo == returnNo && record.FlightDate.Date == booking.ReturnFlightDateValue?.Date
           && !(record.FlightNo == booking.FlightNo && record.FlightDate.Date == booking.FlightDateValue.Date);

    public async Task<EmailSendResult> SendReceiptAsync(BookingRecord booking, string toEmail)
    {
        try
        {
            if (!BookingRules.IsValidEmail(toEmail))
                return new EmailSendResult(false, "Skipped", "The email address is not valid.");
            if (!_store.IsEmailSubscribed(toEmail))
                return new EmailSendResult(false, "Skipped", "This address has unsubscribed from SkyWings emails.");

            byte[] png = _qr.RenderPng(QrPayload.Build(booking.Reference));
            EmailMessage message = BookingEmailBuilder.Build(booking, toEmail.Trim(), png, _unsubscribeMailbox, BuildAttachments(booking));
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

    /// <summary>Sends the cancellation email whenever a booking is cancelled (staff, customer desk or simulator), from now on.</summary>
    public void Attach()
    {
        _store.BookingCanceled += reference => _ = SendCancellationForAsync(reference);
        _store.RefundRequestDeclined += (reference, reason) => _ = SendDeclinedForAsync(reference, reason);
        _store.RefundRequestReceived += reference => _ = SendRequestReceivedForAsync(reference);
    }

    private async Task SendRequestReceivedForAsync(string reference)
    {
        try
        {
            BookingRecord? booking = _store.FindByReference(reference);
            string? email = booking is null ? null : _store.LoadBookingContact(reference);
            if (booking is null || string.IsNullOrEmpty(email)) return;
            await SendRefundRequestReceivedAsync(booking, email).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { AppLogger.Error("Email", ex); }
    }

    /// <summary>
    /// Tells the booking's own address that a refund request was received. It goes to the address on the booking, never to the sender of
    /// the email, so a forged request cannot make SkyWings write to a stranger. Same rules as the other emails.
    /// </summary>
    public async Task<EmailSendResult> SendRefundRequestReceivedAsync(BookingRecord booking, string toEmail)
    {
        try
        {
            if (!BookingRules.IsValidEmail(toEmail))
                return new EmailSendResult(false, "Skipped", "The email address is not valid.");
            if (!_store.IsEmailSubscribed(toEmail))
                return new EmailSendResult(false, "Skipped", "This address has unsubscribed from SkyWings emails.");
            if (_store.IsSimulatedBooking(booking.Reference))
                return new EmailSendResult(false, "Skipped", "Demo bookings do not send email.");

            EmailSendResult result = await _sender.SendAsync(
                BookingEmailBuilder.BuildRefundRequestReceived(booking, toEmail.Trim(), _unsubscribeMailbox)).ConfigureAwait(false);
            _store.WriteAudit("system", "SYSTEM", "EMAIL_REFUND_RECEIVED", booking.Reference, result.Channel);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppLogger.Error("Email", ex);
            return new EmailSendResult(false, "Failed", "The email could not be sent or saved.");
        }
    }

    private async Task SendDeclinedForAsync(string reference, string reason)
    {
        try
        {
            BookingRecord? booking = _store.FindByReference(reference);
            string? email = booking is null ? null : _store.LoadBookingContact(reference);
            if (booking is null || string.IsNullOrEmpty(email)) return;
            await SendRefundDeclinedAsync(booking, email, reason).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { AppLogger.Error("Email", ex); }
    }

    /// <summary>Emails that the refund request was declined and why. Same rules as the other emails; the booking is unaffected.</summary>
    public async Task<EmailSendResult> SendRefundDeclinedAsync(BookingRecord booking, string toEmail, string reason)
    {
        try
        {
            if (!BookingRules.IsValidEmail(toEmail))
                return new EmailSendResult(false, "Skipped", "The email address is not valid.");
            if (!_store.IsEmailSubscribed(toEmail))
                return new EmailSendResult(false, "Skipped", "This address has unsubscribed from SkyWings emails.");
            if (_store.IsSimulatedBooking(booking.Reference))
                return new EmailSendResult(false, "Skipped", "Demo bookings do not send email.");

            EmailSendResult result = await _sender.SendAsync(
                BookingEmailBuilder.BuildRefundDeclined(booking, reason, toEmail.Trim(), _unsubscribeMailbox)).ConfigureAwait(false);
            _store.WriteAudit("system", "SYSTEM", "EMAIL_REFUND_DECLINED", booking.Reference, result.Channel);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppLogger.Error("Email", ex);
            return new EmailSendResult(false, "Failed", "The email could not be sent or saved.");
        }
    }

    private async Task SendCancellationForAsync(string reference)
    {
        try
        {
            BookingRecord? booking = _store.FindByReference(reference);
            string? email = booking is null ? null : _store.LoadBookingContact(reference);
            if (booking is null || string.IsNullOrEmpty(email)) return;
            await SendCancellationAsync(booking, email).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { AppLogger.Error("Email", ex); }
    }

    /// <summary>
    /// Emails that the booking is cancelled, with the refund amount and reference. Same rules as the other emails: invalid or unsubscribed
    /// addresses are skipped, demo bookings never send, and no problem here can affect the cancellation.
    /// </summary>
    public async Task<EmailSendResult> SendCancellationAsync(BookingRecord booking, string toEmail)
    {
        try
        {
            if (!BookingRules.IsValidEmail(toEmail))
                return new EmailSendResult(false, "Skipped", "The email address is not valid.");
            if (!_store.IsEmailSubscribed(toEmail))
                return new EmailSendResult(false, "Skipped", "This address has unsubscribed from SkyWings emails.");
            if (_store.IsSimulatedBooking(booking.Reference))
                return new EmailSendResult(false, "Skipped", "Demo bookings do not send email.");
            if (booking.Status != AppData.StatusCanceled)
                return new EmailSendResult(false, "Skipped", "The booking is not cancelled.");

            EmailSendResult result = await _sender.SendAsync(
                BookingEmailBuilder.BuildCancellation(booking, _store.LoadRefund(booking.Reference), toEmail.Trim(), _unsubscribeMailbox,
                    approvedRequest: _store.HasCompletedRefundRequest(booking.Reference))).ConfigureAwait(false);
            _store.WriteAudit("system", "SYSTEM", "EMAIL_CANCELLATION", booking.Reference, result.Channel);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppLogger.Error("Email", ex);
            return new EmailSendResult(false, "Failed", "The cancellation email could not be sent or saved.");
        }
    }

    /// <summary>
    /// Emails the boarding pass PDF for the given check-ins. Same rules as the receipt: invalid or unsubscribed addresses are skipped,
    /// demo bookings never send, and no problem here can affect the check-in itself.
    /// </summary>
    public async Task<EmailSendResult> SendBoardingPassAsync(BookingRecord booking, string toEmail, IReadOnlyList<CheckInRecord> records)
    {
        try
        {
            if (!BookingRules.IsValidEmail(toEmail))
                return new EmailSendResult(false, "Skipped", "The email address is not valid.");
            if (!_store.IsEmailSubscribed(toEmail))
                return new EmailSendResult(false, "Skipped", "This address has unsubscribed from SkyWings emails.");
            if (_store.IsSimulatedBooking(booking.Reference))
                return new EmailSendResult(false, "Skipped", "Demo bookings do not send email.");
            if (records.Count == 0)
                return new EmailSendResult(false, "Skipped", "No one has checked in yet.");

            var passes = records
                .OrderBy(r => r.CheckedInAt)
                .Select(r => BoardingPassData.From(booking, IsReturnLeg(booking, r), r))
                .ToList();
            var attachments = new List<EmailAttachment>
            {
                new($"SkyWings-boarding-pass-{booking.Reference}.pdf", BoardingPassPdf.Create(passes, _qr),
                    "application/pdf", passes.Count == 1 ? "boarding pass" : "boarding passes")
            };
            byte[] png = _qr.RenderPng(QrPayload.Build(booking.Reference));
            EmailSendResult result = await _sender.SendAsync(
                BookingEmailBuilder.BuildBoardingPass(booking, toEmail.Trim(), png, _unsubscribeMailbox, attachments)).ConfigureAwait(false);
            _store.WriteAudit("system", "SYSTEM", "EMAIL_BOARDING_PASS", booking.Reference, result.Channel);
            return result;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppLogger.Error("Email", ex);
            return new EmailSendResult(false, "Failed", "The boarding pass email could not be sent or saved.");
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

    /// <summary>The running mailbox reader, or null when mail settings are missing or reading is switched off. Set once at start-up.</summary>
    public static RefundMailPoller? RefundMail { get; set; }

    public static BookingEmailService Create(BookingStore store)
        => new(store, CreateSender(AppConfig.Smtp), new QrCodeRenderer(), AppConfig.Smtp.IsConfigured ? AppConfig.Smtp.From : null);
}
