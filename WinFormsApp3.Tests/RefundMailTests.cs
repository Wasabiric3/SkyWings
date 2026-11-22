using Microsoft.Data.Sqlite;
using SkyWings.Email;

namespace SkyWings.Tests;

/// <summary>Reading refund request emails: subject matching, the sender check, no message handled twice, failures, and the acknowledgement.</summary>
[TestClass]
[DoNotParallelize]
public sealed class RefundMailTests
{
    private static readonly DateTime Day = new(2030, 3, 5);
    private const string Mailbox = "desk@skywings.example";

    private string _dbPath = "";
    private string _connection = "";
    private BookingStore _store = null!;

    private sealed class FakeSource : IRefundMailSource
    {
        public List<IncomingRefundMail> Mails { get; } = new();
        public long Validity { get; set; } = 7;
        public Exception? Failure { get; set; }
        public int Calls { get; private set; }
        public long LastAfterUid { get; private set; }
        public TaskCompletionSource<bool>? Gate { get; set; }

        public async Task<RefundMailBatch> FetchAsync(long afterUid, DateTime since, int max, CancellationToken cancellationToken = default)
        {
            Calls++;
            LastAfterUid = afterUid;
            if (Gate is not null) await Gate.Task;
            if (Failure is not null) throw Failure;
            return new RefundMailBatch(Validity, Mails.Where(m => m.Uid > afterUid).OrderBy(m => m.Uid).Take(max).ToList());
        }
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

    private sealed class FakeQr : IQrRenderer
    {
        public byte[] RenderPng(string text, int pixelsPerModule = 8) => new byte[] { 1 };
    }

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"skywings-refundmail-{Guid.NewGuid():N}.db");
        _connection = new SqliteConnectionStringBuilder { DataSource = _dbPath, ForeignKeys = true, Pooling = false }.ConnectionString;
        _store = new BookingStore(_connection);
        _store.SeedFlightsIfEmpty(AppData.DomesticFlights, AppData.InternationalFlights,
            AppData.DefaultDomesticCapacity, AppData.DefaultInternationalCapacity);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private BookingRecord Save(string reference, string? email = "ana@example.com")
    {
        var person = new PassengerRecord("Ana Example", "1 St", "09171234567", 35, "1991-01-01", "P1234567A", false, false,
            new SeatAssignment("Ana Example", "ECO1A"), Array.Empty<string>(), 4000m);
        var booking = new BookingRecord(reference, DateTime.Now, "PR-101", "Manila (MNL)", "Cebu (CEB)", Day, true, "Economy",
            "Credit Card", "**** 4521", 4000m, "Scheduled", new[] { person }, AppData.StatusConfirmed, "PAID", "TXN-" + reference, DateTime.Now, 0m);
        _store.SaveConfirmedBooking(booking, false, 4000m, "Credit Card", "**** 4521", "TXN-" + reference, DateTime.Now);
        if (email is not null) _store.SaveBookingContact(reference, email);
        return booking;
    }

    private static IncomingRefundMail Mail(long uid, string reference, string from = "ana@example.com", string body = "Please cancel and refund.", string? subject = null)
        => new(uid, from, subject ?? "Refund request " + reference, DateTime.Now, body);

    private void Sql(string sql)
    {
        using var con = new SqliteConnection(_connection);
        con.Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    // ---------------------------------------------------------------- reading the subject and the body

    [TestMethod]
    [DataRow("Refund request SKY-A9F278052D6A", "SKY-A9F278052D6A")]
    [DataRow("Re: refund request sky-a9f278052d6a please", "SKY-A9F278052D6A")]
    [DataRow("FWD: Refund request SKY-0123456789AB", "SKY-0123456789AB")]
    public void ReferenceIn_FindsTheBookingReference(string subject, string expected)
        => Assert.AreEqual(expected, RefundMailProcessor.ReferenceIn(subject));

    [TestMethod]
    [DataRow("")]
    [DataRow("Hello there")]
    [DataRow("Refund request SKY-123")]                                           // too short
    [DataRow("Refund request SKY-ZZZZZZZZZZZZ")]                                   // not hex
    [DataRow("SkyWings: we received your refund request for booking SKY-A9F278052D6A")]   // our own acknowledgement
    [DataRow("SkyWings refund approved for booking SKY-A9F278052D6A")]
    [DataRow("SkyWings refund request for booking SKY-A9F278052D6A")]
    public void ReferenceIn_IgnoresEverythingElse_IncludingSkyWingsOwnEmails(string subject)
        => Assert.IsNull(RefundMailProcessor.ReferenceIn(subject));

    [TestMethod]
    public void Excerpt_IsOnePlainLine_AndShort()
    {
        Assert.AreEqual("Line one Line two", RefundMailProcessor.Excerpt("Line one\r\n\r\n   Line\ttwo  "));
        Assert.AreEqual(1000, RefundMailProcessor.Excerpt(new string('x', 5000)).Length);
        Assert.AreEqual("", RefundMailProcessor.Excerpt(null!));
    }

    // ---------------------------------------------------------------- turning mail into requests

    [TestMethod]
    public void AnEmailFromTheBookingsOwnAddress_BecomesAnOpenRequestWithTheMessage_AndNothingIsCancelled()
    {
        Save("SKY-AAAA00000001");
        var processor = new RefundMailProcessor(_store);

        (int logged, int ignored) = processor.Process(new[] { Mail(1, "SKY-AAAA00000001", "ANA@Example.com", "I cannot travel, sorry.") });

        Assert.AreEqual(1, logged);
        Assert.AreEqual(0, ignored);
        RefundRequestRow row = _store.LoadRefundRequests().Single();
        Assert.AreEqual("EMAIL", row.Source);
        Assert.IsTrue(row.SenderMatches, "the address matches the booking, ignoring case");
        Assert.AreEqual("I cannot travel, sorry.", row.CustomerMessage);
        Assert.AreEqual("email", row.LoggedBy);
        Assert.AreEqual(AppData.StatusConfirmed, _store.FindByReference("SKY-AAAA00000001")!.Status, "a request never cancels anything");
        Assert.IsNull(_store.LoadRefund("SKY-AAAA00000001"));
        Assert.IsTrue(_store.LoadAudit().Any(a => a.Action == "REFUND_REQUEST_LOGGED" && a.Actor == "email" && a.Detail.Contains("matches")));
    }

    [TestMethod]
    public void AnEmailFromAnotherAddress_IsStillLogged_ButFlagged()
    {
        Save("SKY-AAAA00000002");
        new RefundMailProcessor(_store).Process(new[] { Mail(1, "SKY-AAAA00000002", "someone.else@example.com") });

        RefundRequestRow row = _store.LoadRefundRequests().Single();
        Assert.IsFalse(row.SenderMatches);
        Assert.IsTrue(_store.LoadAudit().Any(a => a.Action == "REFUND_REQUEST_LOGGED" && a.Detail.Contains("differs")));
        Assert.IsFalse(_store.LoadAudit().Any(a => a.Detail.Contains("someone.else")), "addresses are not written to the audit log");
    }

    [TestMethod]
    public void BookingsThatCannotHaveARequest_AreIgnoredWithAReason()
    {
        Save("SKY-AAAA00000010");                 // will be cancelled
        Save("SKY-AAAA00000011");                 // a demo booking
        Save("SKY-AAAA00000012");                 // asked twice
        Assert.IsTrue(_store.CancelBooking("SKY-AAAA00000010", 100m));
        Sql("UPDATE bookings SET is_simulated = 1 WHERE reference = 'SKY-AAAA00000011'");
        var processor = new RefundMailProcessor(_store);

        (int logged, int ignored) = processor.Process(new[]
        {
            Mail(1, "SKY-AAAA00000010"), Mail(2, "SKY-AAAA00000011"), Mail(3, "SKY-AAAA00000012"), Mail(4, "SKY-AAAA00000012"),
            Mail(5, "SKY-AAAA99999999"),                                  // no such booking
            new IncomingRefundMail(6, "a@b.co", "Hello", DateTime.Now, "not a refund email")
        });

        Assert.AreEqual(1, logged);
        Assert.AreEqual(5, ignored);
        Assert.AreEqual(1, _store.CountOpenRefundRequests());
        Assert.AreEqual(4, _store.LoadAudit().Count(a => a.Action == "REFUND_EMAIL_IGNORED"), "the unrelated email is not even audited");
    }

    // ---------------------------------------------------------------- the acknowledgement

    [TestMethod]
    public async Task AnEmailRequest_IsAcknowledgedToTheBookingsAddress_NotToTheSender()
    {
        var sender = new RecordingSender();
        new BookingEmailService(_store, sender, new FakeQr(), Mailbox).Attach();
        Save("SKY-AAAA00000020", "ana@example.com");
        Save("SKY-AAAA00000021", null);   // no address on this booking

        new RefundMailProcessor(_store).Process(new[]
        {
            Mail(1, "SKY-AAAA00000020", "forger@example.com"),
            Mail(2, "SKY-AAAA00000021", "ana@example.com")
        });
        await Task.Delay(500);

        EmailMessage ack = sender.Sent.Single();
        Assert.AreEqual("ana@example.com", ack.To, "the address on the booking, even though a forger wrote");
        Assert.AreEqual("SkyWings: we received your refund request for booking SKY-AAAA00000020", ack.Subject);
        StringAssert.Contains(ack.HtmlBody, "staff will review it");
        StringAssert.Contains(ack.HtmlBody, "PHP 3,200.00");
        Assert.IsNull(RefundMailProcessor.ReferenceIn(ack.Subject), "our own email can never be read back as a request");
        Assert.IsTrue(_store.LoadAudit().Any(a => a.Action == "EMAIL_REFUND_RECEIVED" && a.Target == "SKY-AAAA00000020"));
    }

    [TestMethod]
    public async Task ARequestLoggedByStaff_SendsNoAcknowledgement()
    {
        var sender = new RecordingSender();
        new BookingEmailService(_store, sender, new FakeQr(), Mailbox).Attach();
        Save("SKY-AAAA00000030");
        Assert.IsTrue(_store.LogRefundRequest("SKY-AAAA00000030", "agent1").Ok);
        await Task.Delay(300);
        Assert.IsEmpty(sender.Sent);
    }

    // ---------------------------------------------------------------- the poller

    [TestMethod]
    public async Task Poller_HandlesEachMessageOnce_RemembersItsPlace_AndPicksUpNewOnes()
    {
        Save("SKY-AAAA00000040");
        Save("SKY-AAAA00000041");
        var source = new FakeSource();
        source.Mails.Add(Mail(10, "SKY-AAAA00000040"));
        using var poller = new RefundMailPoller(_store, source, TimeSpan.FromMinutes(1));
        int raised = 0;
        poller.RequestsLogged += _ => raised++;

        RefundMailCheck first = await poller.CheckNowAsync();
        Assert.IsTrue(first.Ok);
        Assert.AreEqual(1, first.Logged);
        Assert.AreEqual(1, raised);

        RefundMailCheck second = await poller.CheckNowAsync();
        Assert.AreEqual(0, second.Found, "nothing new");
        Assert.AreEqual(10, source.LastAfterUid, "it asks only for messages after the last one it handled");
        Assert.AreEqual(1, raised);

        source.Mails.Add(Mail(11, "SKY-AAAA00000041"));
        RefundMailCheck third = await poller.CheckNowAsync();
        Assert.AreEqual(1, third.Logged);
        Assert.AreEqual(2, _store.CountOpenRefundRequests());

        // A new poller (the app restarted) carries on from the saved place.
        using var restarted = new RefundMailPoller(_store, source, TimeSpan.FromMinutes(1));
        Assert.AreEqual(0, (await restarted.CheckNowAsync()).Found);
        Assert.AreEqual(11, source.LastAfterUid);
    }

    [TestMethod]
    public async Task Poller_StartsOverWhenTheMailboxHasANewUidValidity()
    {
        Save("SKY-AAAA00000050");
        var source = new FakeSource { Validity = 1 };
        source.Mails.Add(Mail(5, "SKY-AAAA00000050"));
        using var poller = new RefundMailPoller(_store, source, TimeSpan.FromMinutes(1));
        Assert.AreEqual(1, (await poller.CheckNowAsync()).Logged);

        source.Validity = 2;                 // the mailbox was rebuilt: UIDs restart
        source.Mails.Clear();
        Save("SKY-AAAA00000051");
        source.Mails.Add(Mail(1, "SKY-AAAA00000051"));
        RefundMailCheck check = await poller.CheckNowAsync();
        Assert.AreEqual(1, check.Logged, "UID 1 is new even though UID 5 was handled before");
    }

    [TestMethod]
    public async Task Poller_ReportsAFailureWithoutThrowing_AndDoesNotMoveItsPlace()
    {
        Save("SKY-AAAA00000060");
        var source = new FakeSource { Failure = new IOException("no network") };
        source.Mails.Add(Mail(3, "SKY-AAAA00000060"));
        using var poller = new RefundMailPoller(_store, source, TimeSpan.FromMinutes(1));

        RefundMailCheck failed = await poller.CheckNowAsync();
        Assert.IsFalse(failed.Ok);
        StringAssert.Contains(failed.Message, "no network");

        source.Failure = null;
        Assert.AreEqual(1, (await poller.CheckNowAsync()).Logged, "the message is read once the mailbox is back");
    }

    [TestMethod]
    public async Task Poller_DoesNotRunTwoChecksAtOnce()
    {
        var source = new FakeSource { Gate = new TaskCompletionSource<bool>() };
        using var poller = new RefundMailPoller(_store, source, TimeSpan.FromMinutes(1));

        Task<RefundMailCheck> slow = poller.CheckNowAsync();
        RefundMailCheck overlapping = await poller.CheckNowAsync();
        StringAssert.Contains(overlapping.Message, "already");
        source.Gate.SetResult(true);
        Assert.IsTrue((await slow).Ok);
        Assert.AreEqual(1, source.Calls);
    }

    // ---------------------------------------------------------------- settings

    [TestMethod]
    public void ImapSettings_FollowTheMailAccount_AndNeedEverythingToBeConfigured()
    {
        Assert.AreEqual("imap.gmail.com", ImapSettings.HostFor("smtp.gmail.com"));
        Assert.AreEqual("", ImapSettings.HostFor("mail.example.com"), "not in the smtp.* form: imap.host must be set");
        Assert.IsTrue(new ImapSettings("imap.gmail.com", 993, "u@x.co", "pw", true).IsConfigured);
        Assert.IsFalse(new ImapSettings("imap.gmail.com", 993, "u@x.co", "pw", false).IsConfigured, "imap.enabled=false");
        Assert.IsFalse(new ImapSettings("", 993, "u@x.co", "pw", true).IsConfigured);
        Assert.IsFalse(new ImapSettings("imap.gmail.com", 993, "u@x.co", "", true).IsConfigured);
        Assert.IsFalse(ImapSettings.None.IsConfigured);
    }

    [TestMethod]
    public void TheConnectionCheck_ExplainsWhenReadingIsNotSetUp()
        => StringAssert.Contains(CommandLine.CheckRefundMailbox(ImapSettings.None), "not set up");
}
