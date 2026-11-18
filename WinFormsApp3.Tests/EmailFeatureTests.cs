using Microsoft.Data.Sqlite;
using SkyWings;
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
        var csb = new SqliteConnectionStringBuilder { DataSource = _dbPath, ForeignKeys = true };
        _store = new BookingStore(csb.ConnectionString);
        _store.SeedFlightsIfEmpty(AppData.DomesticFlights, AppData.InternationalFlights,
            AppData.DefaultDomesticCapacity, AppData.DefaultInternationalCapacity);
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
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
        StringAssert.Contains(message.HtmlBody, "Manage booking");
        StringAssert.Contains(message.HtmlBody, "Maria &lt;Santos&gt;");   // names are escaped, not injected as markup
        Assert.IsFalse(message.HtmlBody.Contains("Maria <Santos>"));
        StringAssert.Contains(message.TextBody, "SKY-AB12CD34EF56");
        Assert.AreEqual(1, message.InlineImages.Count);
        Assert.IsFalse(message.HtmlBody.Contains("PSP-0000001") || message.TextBody.Contains("PSP-0000001"), "ID numbers must not be emailed");
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
