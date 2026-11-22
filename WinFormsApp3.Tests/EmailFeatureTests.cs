using Microsoft.Data.Sqlite;
using SkyWings;
using SkyWings.Controllers;
using SkyWings.Email;

namespace SkyWings.Tests;

/// <summary>Receipt email, QR payload and unsubscribe behaviour. Uses a temporary database and a fake sender.</summary>
[TestClass]
public sealed class EmailFeatureTests
{
    private string _dbPath = "";
    private BookingStore _store = null!;

    private sealed class FakeQr : IQrRenderer
    {
        public byte[] RenderPng(string text, int pixelsPerModule = 8) => new byte[] { 0x89, 0x50, 0x4E, 0x47 };
    }

    private sealed class RecordingSender : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = new();
        public string Name => "Recording";
        public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            Sent.Add(message);
            return Task.FromResult(new EmailSendResult(true, "SMTP", "ok"));
        }
    }

    private sealed class FailingSender : IEmailSender
    {
        public string Name => "Failing";
        public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
            => throw new IOException("mail server down");
    }

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"skywings-email-test-{Guid.NewGuid():N}.db");
        var csb = new SqliteConnectionStringBuilder { DataSource = _dbPath, ForeignKeys = true, Pooling = false };
        _store = new BookingStore(csb.ConnectionString);
        _store.SeedFlightsIfEmpty(AppData.DomesticFlights, AppData.InternationalFlights,
            AppData.DefaultDomesticCapacity, AppData.DefaultInternationalCapacity);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private BookingRecord SaveBooking(string reference = "SKY-AB12CD34EF56")
    {
        var passenger = new PassengerRecord("Maria <Santos>", "1 Test St", "09171234567", 35, "1991-01-01",
            "PSP-0000001", false, false, new SeatAssignment("Maria <Santos>", "ECO-01A"), Array.Empty<string>(), 5000m);
        var booking = new BookingRecord(reference, DateTime.Now, "PR-101", "Manila (MNL)", "Cebu (CEB)",
            DateTime.Today.AddDays(30), true, "Economy", "Credit Card", "**** 4521", 5000m, "Scheduled",
            new[] { passenger }, AppData.StatusConfirmed, "PAID", "TXN-TEST0001", DateTime.Now, 0m);
        _store.SaveConfirmedBooking(booking, false, 5000m, "Credit Card", "**** 4521", "TXN-TEST0001", DateTime.Now);
        return booking;
    }

    // ---- QR payload ----

    [TestMethod]
    public void QrPayload_RoundTripsAReference()
    {
        Assert.IsTrue(QrPayload.TryParse(QrPayload.Build("sky-ab12cd34ef56"), out string reference));
        Assert.AreEqual("SKY-AB12CD34EF56", reference);
    }

    [TestMethod]
    public void QrPayload_FindsTheReferenceInsideScannerNoise()
    {
        Assert.IsTrue(QrPayload.TryParse("  skywings:SKY-1A2B3C4D\r\n", out string reference));
        Assert.AreEqual("SKY-1A2B3C4D", reference);
        Assert.AreEqual("SKY-1A2B3C4D", QrPayload.Normalize("SKY-1A2B3C4D\n"));
    }

    [TestMethod]
    public void QrPayload_RejectsTextWithoutAReference()
    {
        Assert.IsFalse(QrPayload.TryParse("hello", out _));
        Assert.IsFalse(QrPayload.TryParse(null, out _));
        Assert.AreEqual("hello", QrPayload.Normalize(" hello "));
    }

    // ---- the real QR renderer and the full send path (no fake QR) ----

    [TestMethod]
    [DoNotParallelize]   // GDI+ and the shared logo bitmap are not thread-safe
    public void QrCodeRenderer_ProducesARealPng()
    {
        byte[] png = new QrCodeRenderer().RenderPng(QrPayload.Build("SKY-AB12CD34EF56"));

        CollectionAssert.AreEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, png.Take(8).ToArray());
        Assert.IsTrue(png.Length > 200);
    }

    [TestMethod]
    [DoNotParallelize]   // draws the receipt and tickets with the shared logo bitmap (GDI+ is not thread-safe)
    public async Task RealQr_ReceiptAndBoardingPassEmailsBothSend()
    {
        BookingRecord booking = SaveBooking();
        List<CheckInRecord> records = CheckInOne(booking.Reference);
        var sender = new RecordingSender();
        var service = new BookingEmailService(_store, sender, new QrCodeRenderer());

        Assert.IsTrue((await service.SendReceiptAsync(booking, "maria@example.com")).Delivered);
        Assert.IsTrue((await service.SendBoardingPassAsync(booking, "maria@example.com", records)).Delivered);
        Assert.HasCount(2, sender.Sent);
    }

    // ---- boarding pass email after check-in ----

    private List<CheckInRecord> CheckInOne(string reference)
    {
        DateTime departure = AppData.DepartureOf("PR-101", DateTime.Today.AddDays(30));
        Assert.AreEqual(CheckInOutcome.CheckedIn, _store.CheckIn(reference, "Maria <Santos>", false, departure.AddHours(-3)));
        return _store.LoadCheckIns(reference);
    }

    [TestMethod]
    [DoNotParallelize]   // shares the SQLite pool cleanup with the drawing tests
    public async Task EmailBoardingPassButton_ExplainsWhenTheBookingHasNoAddress()
    {
        BookingRecord booking = SaveBooking();
        CheckInOne(booking.Reference);
        var controller = new CheckInController(_store);
        Assert.IsTrue(controller.Find(booking.Reference).Found);

        EmailSendResult? result = await controller.SendBoardingPassAsync(null, returnLeg: false);

        Assert.IsNotNull(result);
        Assert.IsFalse(result.Delivered);
        Assert.AreEqual("Skipped", result.Channel);
        StringAssert.Contains(result.Detail, "no email address");
    }

    [TestMethod]
    [DoNotParallelize]   // draws the ticket with the shared logo bitmap (GDI+ is not thread-safe)
    public async Task BoardingPassEmail_SendsOnePdfAndTheQr()
    {
        BookingRecord booking = SaveBooking();
        List<CheckInRecord> records = CheckInOne(booking.Reference);
        var sender = new RecordingSender();
        var service = new BookingEmailService(_store, sender, new FakeQr());

        EmailSendResult result = await service.SendBoardingPassAsync(booking, "maria@example.com", records);

        Assert.IsTrue(result.Delivered);
        EmailMessage message = sender.Sent.Single();
        StringAssert.Contains(message.Subject, "boarding pass");
        Assert.AreEqual(1, message.InlineImages.Count);
        EmailAttachment pdf = message.Attachments!.Single();
        Assert.AreEqual("application/pdf", pdf.MediaType);
        Assert.IsTrue(pdf.Bytes.Length > 100);
        Assert.IsFalse(message.HtmlBody.Contains("Maria <Santos>"));   // names are HTML-encoded, never raw
    }

    [TestMethod]
    [DoNotParallelize]   // shares the SQLite pool cleanup with the drawing tests
    public async Task BoardingPassEmail_SkipsBadUnsubscribedAndEmptyCases()
    {
        BookingRecord booking = SaveBooking();
        List<CheckInRecord> records = CheckInOne(booking.Reference);
        var sender = new RecordingSender();
        var service = new BookingEmailService(_store, sender, new FakeQr());

        Assert.AreEqual("Skipped", (await service.SendBoardingPassAsync(booking, "not an email", records)).Channel);
        Assert.AreEqual("Skipped", (await service.SendBoardingPassAsync(booking, "maria@example.com", Array.Empty<CheckInRecord>())).Channel);
        _store.SetEmailSubscribed("maria@example.com", false);
        Assert.AreEqual("Skipped", (await service.SendBoardingPassAsync(booking, "maria@example.com", records)).Channel);
        Assert.IsEmpty(sender.Sent);
    }

    [TestMethod]
    [DoNotParallelize]   // draws the ticket with the shared logo bitmap (GDI+ is not thread-safe)
    public async Task BoardingPassEmail_ServerFailureReturnsFailedAndDoesNotThrow()
    {
        BookingRecord booking = SaveBooking();
        List<CheckInRecord> records = CheckInOne(booking.Reference);
        var service = new BookingEmailService(_store, new FailingSender(), new FakeQr());

        EmailSendResult result = await service.SendBoardingPassAsync(booking, "maria@example.com", records);

        Assert.IsFalse(result.Delivered);
        Assert.AreEqual("Failed", result.Channel);
    }

    // ---- address validation ----

    [TestMethod]
    [DataRow("maria@example.com", true)]
    [DataRow("  maria@example.com ", true)]
    [DataRow("maria@localhost", false)]
    [DataRow("not an email", false)]
    [DataRow("Maria <maria@example.com>", false)]
    [DataRow("a@b.com\r\nBcc: x@y.com", false)]
    [DataRow("", false)]
    [DataRow(null, false)]
    public void IsValidEmail_AcceptsOnlyPlainAddresses(string? input, bool expected)
        => Assert.AreEqual(expected, BookingRules.IsValidEmail(input));

    // ---- contacts and preferences (the Create / Read / Update / Delete part) ----

    [TestMethod]
    public void BookingContact_CanBeCreatedReadUpdatedAndDeleted()
    {
        SaveBooking();

        Assert.IsNull(_store.LoadBookingContact("SKY-AB12CD34EF56"));
        _store.SaveBookingContact("SKY-AB12CD34EF56", "Maria@Example.com");
        Assert.AreEqual("maria@example.com", _store.LoadBookingContact("SKY-AB12CD34EF56"));
        _store.SaveBookingContact("SKY-AB12CD34EF56", "new@example.com");
        Assert.AreEqual("new@example.com", _store.LoadBookingContact("SKY-AB12CD34EF56"));
        _store.DeleteBookingContact("SKY-AB12CD34EF56");
        Assert.IsNull(_store.LoadBookingContact("SKY-AB12CD34EF56"));
    }

    [TestMethod]
    public void SaveBookingContact_RejectsABadAddress()
    {
        SaveBooking();
        Assert.ThrowsExactly<ArgumentException>(() => _store.SaveBookingContact("SKY-AB12CD34EF56", "nope"));
    }

    [TestMethod]
    public void EmailPreference_DefaultsToSubscribedAndCanBeToggled()
    {
        Assert.IsTrue(_store.IsEmailSubscribed("someone@example.com"));
        _store.SetEmailSubscribed("Someone@Example.com", false);
        Assert.IsFalse(_store.IsEmailSubscribed("someone@example.com"));
        _store.SetEmailSubscribed("someone@example.com", true);
        Assert.IsTrue(_store.IsEmailSubscribed("someone@example.com"));
    }

    // ---- building and sending ----

    [TestMethod]
    public void Builder_IncludesReferenceQrAndUnsubscribeFooter_AndEscapesHtml()
    {
        BookingRecord booking = SaveBooking();

        EmailMessage message = BookingEmailBuilder.Build(booking, "maria@example.com", new byte[] { 1, 2, 3 }, "desk@example.com");

        StringAssert.Contains(message.Subject, "SKY-AB12CD34EF56");
        StringAssert.Contains(message.HtmlBody, "cid:" + BookingEmailBuilder.QrContentId);
        StringAssert.Contains(message.HtmlBody, "contact SkyWings staff");
        StringAssert.Contains(message.HtmlBody, "Maria &lt;Santos&gt;");   // names are escaped, not injected as markup
        Assert.IsFalse(message.HtmlBody.Contains("Maria <Santos>"));
        StringAssert.Contains(message.TextBody, "SKY-AB12CD34EF56");
        Assert.AreEqual(1, message.InlineImages.Count);
        Assert.IsFalse(message.HtmlBody.Contains("PSP-0000001") || message.TextBody.Contains("PSP-0000001"), "ID numbers must not be emailed");
    }


    [TestMethod]
    public void Emails_HaveAnUnsubscribeButton_WhenASenderAddressIsConfigured()
    {
        BookingRecord booking = SaveBooking();
        var attachments = new List<EmailAttachment> { new("pass.pdf", new byte[] { 1 }, "application/pdf", "boarding pass") };

        foreach (EmailMessage message in new[]
        {
            BookingEmailBuilder.Build(booking, "maria@example.com", new byte[] { 1 }, "desk@example.com"),
            BookingEmailBuilder.BuildBoardingPass(booking, "maria@example.com", new byte[] { 1 }, "desk@example.com", attachments)
        })
        {
            StringAssert.Contains(message.HtmlBody, ">Unsubscribe</a>");
            StringAssert.Contains(message.HtmlBody, "mailto:desk@example.com?subject=Unsubscribe%20SKY-AB12CD34EF56");
            StringAssert.Contains(message.TextBody, "To unsubscribe, email desk@example.com");
        }

        EmailMessage noMailbox = BookingEmailBuilder.Build(booking, "maria@example.com", new byte[] { 1 });
        Assert.IsFalse(noMailbox.HtmlBody.Contains(">Unsubscribe</a>"));
    }
    [TestMethod]
    public async Task Service_SendsToSubscribedAddress_AndWritesAnAuditEntryWithoutTheAddress()
    {
        BookingRecord booking = SaveBooking();
        var sender = new RecordingSender();
        var service = new BookingEmailService(_store, sender, new FakeQr());

        EmailSendResult result = await service.SendReceiptAsync(booking, "maria@example.com");

        Assert.IsTrue(result.Delivered);
        Assert.AreEqual(1, sender.Sent.Count);
        var audit = _store.LoadAudit(50, booking.Reference).Where(a => a.Action == "EMAIL_RECEIPT").ToList();
        Assert.AreEqual(1, audit.Count);
        Assert.IsFalse(audit[0].Detail.Contains('@') || audit[0].Target.Contains('@'));
    }

    [TestMethod]
    [DoNotParallelize]   // draws the receipt, which uses the shared cached logo bitmap (GDI+ is not thread-safe)
    public async Task Service_AttachesTheSamePdfReceiptThatPrintReceiptShows()
    {
        BookingRecord booking = SaveBooking();
        var sender = new RecordingSender();
        var service = new BookingEmailService(_store, sender, new FakeQr());

        await service.SendReceiptAsync(booking, "maria@example.com");

        EmailAttachment pdf = sender.Sent[0].Attachments!.Single();
        Assert.AreEqual("SkyWings-receipt-SKY-AB12CD34EF56.pdf", pdf.FileName);
        Assert.AreEqual("application/pdf", pdf.MediaType);
        Assert.AreEqual("%PDF-", System.Text.Encoding.ASCII.GetString(pdf.Bytes, 0, 5));
        StringAssert.Contains(sender.Sent[0].TextBody, "Attached (PDF): booking receipt.");
    }

    [TestMethod]
    [DoNotParallelize]   // draws the receipt and tickets, which use the shared cached logo bitmap (GDI+ is not thread-safe)
    public async Task Service_AddsTheBoardingPassPdfOnceSomeoneHasCheckedIn()
    {
        BookingRecord booking = SaveBooking();
        var sender = new RecordingSender();
        var service = new BookingEmailService(_store, sender, new FakeQr());

        await service.SendReceiptAsync(booking, "maria@example.com");   // nobody has checked in yet
        Assert.AreEqual(1, sender.Sent[0].Attachments!.Count);

        DateTime openNow = AppData.DepartureOf("PR-101", booking.FlightDateValue).AddHours(-2);
        Assert.AreEqual(CheckInOutcome.CheckedIn, _store.CheckIn(booking.Reference, "Maria <Santos>", false, openNow));
        await service.SendReceiptAsync(booking, "maria@example.com");

        var files = sender.Sent[1].Attachments!;
        CollectionAssert.AreEqual(
            new[] { "SkyWings-receipt-SKY-AB12CD34EF56.pdf", "SkyWings-boarding-pass-SKY-AB12CD34EF56.pdf" },
            files.Select(f => f.FileName).ToArray());
        Assert.IsTrue(files.All(f => f.MediaType == "application/pdf" && f.Bytes.Length > 1000));
        StringAssert.Contains(sender.Sent[1].TextBody, "Attached (PDF): booking receipt, boarding pass.");
    }

    [TestMethod]
    public async Task OutboxSender_SavesAttachmentsBesideTheHtml()
    {
        string folder = Path.Combine(Path.GetTempPath(), "skywings-outbox-" + Guid.NewGuid().ToString("N"));
        try
        {
            BookingRecord booking = SaveBooking();
            var attachment = new EmailAttachment("SkyWings-receipt-X.pdf", new byte[] { 1, 2, 3 }, "application/pdf");
            EmailMessage message = BookingEmailBuilder.Build(booking, "maria@example.com", new byte[] { 1, 2, 3 }, null, new[] { attachment });

            EmailSendResult result = await new FileOutboxEmailSender(folder).SendAsync(message);

            string[] pdfs = Directory.GetFiles(folder, "*-SkyWings-receipt-X.pdf");
            Assert.AreEqual(1, pdfs.Length);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(pdfs[0]));
            Assert.AreEqual(Path.ChangeExtension(result.Detail, null), pdfs[0][..^"-SkyWings-receipt-X.pdf".Length]);
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }

    [TestMethod]
    public async Task Service_SkipsAnUnsubscribedAddress()
    {
        BookingRecord booking = SaveBooking();
        _store.SetEmailSubscribed("maria@example.com", false);
        var sender = new RecordingSender();
        var service = new BookingEmailService(_store, sender, new FakeQr());

        EmailSendResult result = await service.SendReceiptAsync(booking, "maria@example.com");

        Assert.IsFalse(result.Delivered);
        Assert.AreEqual("Skipped", result.Channel);
        Assert.AreEqual(0, sender.Sent.Count);
    }

    [TestMethod]
    public async Task Service_ReportsAFailureInsteadOfThrowing()
    {
        BookingRecord booking = SaveBooking();
        var service = new BookingEmailService(_store, new FailingSender(), new FakeQr());

        EmailSendResult result = await service.SendReceiptAsync(booking, "maria@example.com");

        Assert.IsFalse(result.Delivered);
        Assert.AreEqual("Failed", result.Channel);
    }

    // ---- delivery channels ----

    [TestMethod]
    public async Task OutboxSender_SavesAReadableHtmlFileWithTheQrEmbedded()
    {
        string folder = Path.Combine(Path.GetTempPath(), "skywings-outbox-" + Guid.NewGuid().ToString("N"));
        try
        {
            BookingRecord booking = SaveBooking();
            EmailMessage message = BookingEmailBuilder.Build(booking, "maria@example.com", new byte[] { 1, 2, 3 });

            EmailSendResult result = await new FileOutboxEmailSender(folder).SendAsync(message);

            Assert.AreEqual("Outbox", result.Channel);
            string saved = await File.ReadAllTextAsync(result.Detail);
            StringAssert.Contains(saved, "data:image/png;base64,");
            Assert.IsFalse(saved.Contains("cid:"));
            StringAssert.Contains(saved, "SKY-AB12CD34EF56");
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }

    [TestMethod]
    public async Task FallbackSender_UsesTheOutboxWhenTheMailServerFails()
    {
        string folder = Path.Combine(Path.GetTempPath(), "skywings-outbox-" + Guid.NewGuid().ToString("N"));
        try
        {
            BookingRecord booking = SaveBooking();
            EmailMessage message = BookingEmailBuilder.Build(booking, "maria@example.com", new byte[] { 1 });
            var sender = new FallbackEmailSender(new FailingSender(), new FileOutboxEmailSender(folder));

            EmailSendResult result = await sender.SendAsync(message);

            Assert.IsTrue(result.Delivered);
            Assert.AreEqual("Outbox", result.Channel);
            StringAssert.Contains(result.Detail, "Could not reach the mail server");
        }
        finally
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }

    [TestMethod]
    public void SmtpSettings_AreOnlyConfiguredWithAHostAndAValidFromAddress()
    {
        Assert.IsFalse(SmtpSettings.None.IsConfigured);
        Assert.IsFalse(new SmtpSettings("smtp.gmail.com", 587, "u", "p", "", true).IsConfigured);
        Assert.IsTrue(new SmtpSettings("smtp.gmail.com", 587, "u", "p", "desk@example.com", true).IsConfigured);
    }

    [TestMethod]
    public void MaskEmail_HidesMostOfTheAddress()
        => Assert.AreEqual("m***@example.com", SkyWings.Controllers.ManageBookingController.MaskEmail("maria@example.com"));
}
