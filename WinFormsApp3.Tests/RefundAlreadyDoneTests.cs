using Microsoft.Data.Sqlite;
using SkyWings.Email;

namespace SkyWings.Tests;

/// <summary>A refund request by email for a booking that is already cancelled: its owner is told once, and nothing else changes.</summary>
[TestClass]
[DoNotParallelize]
public sealed class RefundAlreadyDoneTests
{
    private static readonly DateTime Day = new(2030, 3, 5);
    private const string Mailbox = "desk@skywings.example";

    private string _dbPath = "";
    private string _connection = "";
    private BookingStore _store = null!;
    private RecordingSender _sender = null!;

    private sealed class RecordingSender : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = new();
        public string Name => "Recording";
        public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
        {
            lock (Sent) Sent.Add(message);
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
        _dbPath = Path.Combine(Path.GetTempPath(), $"skywings-alreadydone-{Guid.NewGuid():N}.db");
        _connection = new SqliteConnectionStringBuilder { DataSource = _dbPath, ForeignKeys = true, Pooling = false }.ConnectionString;
        _store = new BookingStore(_connection);
        _store.SeedFlightsIfEmpty(AppData.DomesticFlights, AppData.InternationalFlights,
            AppData.DefaultDomesticCapacity, AppData.DefaultInternationalCapacity);
        _sender = new RecordingSender();
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private void Save(string reference, string? email = "ana@example.com")
    {
        var person = new PassengerRecord("Ana Example", "1 St", "09171234567", 35, "1991-01-01", "P1234567A", false, false,
            new SeatAssignment("Ana Example", "ECO1A"), Array.Empty<string>(), 4000m);
        var booking = new BookingRecord(reference, DateTime.Now, "PR-101", "Manila (MNL)", "Cebu (CEB)", Day, true, "Economy",
            "Credit Card", "**** 4521", 4000m, "Scheduled", new[] { person }, AppData.StatusConfirmed, "PAID", "TXN-" + reference, DateTime.Now, 0m);
        _store.SaveConfirmedBooking(booking, false, 4000m, "Credit Card", "**** 4521", "TXN-" + reference, DateTime.Now);
        if (email is not null) _store.SaveBookingContact(reference, email);
    }

    private static IncomingRefundMail Mail(long uid, string reference, string from = "ana@example.com")
        => new(uid, from, "Refund request " + reference, DateTime.Now, "Please cancel and refund.");

    /// <summary>Cancels the booking, then starts listening, so the cancellation notice itself is not part of what is counted.</summary>
    private void CancelThenListen(string reference)
    {
        Assert.IsTrue(_store.CancelBooking(reference, 100m));
        new BookingEmailService(_store, _sender, new FakeQr(), Mailbox).Attach();
    }

    private List<EmailMessage> AlreadyDoneEmails()
    {
        lock (_sender.Sent) return _sender.Sent.Where(m => m.Subject.Contains("is already cancelled")).ToList();
    }

    [TestMethod]
    public async Task ARequestForACancelledBooking_RepliesToTheBookingsOwnAddress_NotTheSender()
    {
        Save("SKY-BBBB00000001");
        CancelThenListen("SKY-BBBB00000001");

        (int logged, int ignored) = new RefundMailProcessor(_store).Process(new[] { Mail(1, "SKY-BBBB00000001", "someone.else@example.com") });
        await Task.Delay(400);

        Assert.AreEqual(0, logged);
        Assert.AreEqual(1, ignored);
        Assert.IsEmpty(_store.LoadRefundRequests(), "nothing is logged for an already cancelled booking");
        EmailMessage reply = AlreadyDoneEmails().Single();
        Assert.AreEqual("ana@example.com", reply.To, "the reply goes to the address on the booking, not to the sender");
        StringAssert.Contains(reply.HtmlBody, "already cancelled");
        StringAssert.Contains(reply.TextBody, "already cancelled");
        Assert.IsNull(RefundMailProcessor.ReferenceIn(reply.Subject), "our own reply can never be read back as a request");
        Assert.IsTrue(_store.LoadAudit().Any(a => a.Action == "EMAIL_REFUND_ALREADY" && a.Target == "SKY-BBBB00000001"));
    }

    [TestMethod]
    public async Task TheReplyNamesTheRefund_WhenThereIsOne()
    {
        Save("SKY-BBBB00000002");
        CancelThenListen("SKY-BBBB00000002");
        RefundRecord? refund = _store.LoadRefund("SKY-BBBB00000002");

        new RefundMailProcessor(_store).Process(new[] { Mail(1, "SKY-BBBB00000002") });
        await Task.Delay(400);

        EmailMessage reply = AlreadyDoneEmails().Single();
        StringAssert.Contains(reply.HtmlBody, refund?.Reference ?? "No refund was due");
    }

    [TestMethod]
    public async Task WritingAgainTheSameDay_DoesNotSendAnotherReply()
    {
        Save("SKY-BBBB00000003");
        CancelThenListen("SKY-BBBB00000003");
        var processor = new RefundMailProcessor(_store);

        processor.Process(new[] { Mail(1, "SKY-BBBB00000003") });
        await Task.Delay(400);
        processor.Process(new[] { Mail(2, "SKY-BBBB00000003"), Mail(3, "SKY-BBBB00000003", "spam@example.com") });
        await Task.Delay(400);

        Assert.HasCount(1, AlreadyDoneEmails(), "one reply per booking per day, however often someone writes");
    }

    [TestMethod]
    public async Task NoReplyWhenThereIsNothingToTellAnyone()
    {
        Save("SKY-BBBB00000004", email: null);                 // no address on the booking
        Save("SKY-BBBB00000005");                              // a demo booking
        Save("SKY-BBBB00000006");                              // not cancelled
        Assert.IsTrue(_store.CancelBooking("SKY-BBBB00000004", 100m));
        Assert.IsTrue(_store.CancelBooking("SKY-BBBB00000005", 100m));
        using (var con = new SqliteConnection(_connection))
        {
            con.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "UPDATE bookings SET is_simulated = 1 WHERE reference = 'SKY-BBBB00000005'";
            cmd.ExecuteNonQuery();
        }
        new BookingEmailService(_store, _sender, new FakeQr(), Mailbox).Attach();

        new RefundMailProcessor(_store).Process(new[]
        {
            Mail(1, "SKY-BBBB00000004"), Mail(2, "SKY-BBBB00000005"), Mail(3, "SKY-BBBB00000006"),
            Mail(4, "SKY-BBBB99999999")                         // no such booking: never confirm it exists
        });
        await Task.Delay(500);

        Assert.IsEmpty(AlreadyDoneEmails());
    }

    [TestMethod]
    public async Task AnUnsubscribedAddress_GetsNoReply()
    {
        Save("SKY-BBBB00000007");
        _store.SetEmailSubscribed("ana@example.com", false);
        CancelThenListen("SKY-BBBB00000007");

        new RefundMailProcessor(_store).Process(new[] { Mail(1, "SKY-BBBB00000007") });
        await Task.Delay(400);

        Assert.IsEmpty(AlreadyDoneEmails());
    }

    [TestMethod]
    public void HasRecentAudit_OnlyCountsTheSameActionAndTargetInsideTheWindow()
    {
        Assert.IsFalse(_store.HasRecentAudit("X_ACTION", "T1", TimeSpan.FromHours(24)));
        _store.WriteAudit("system", "SYSTEM", "X_ACTION", "T1", "SMTP");
        Assert.IsTrue(_store.HasRecentAudit("X_ACTION", "T1", TimeSpan.FromHours(24)));
        Assert.IsFalse(_store.HasRecentAudit("X_ACTION", "T2", TimeSpan.FromHours(24)), "another booking");
        Assert.IsFalse(_store.HasRecentAudit("OTHER", "T1", TimeSpan.FromHours(24)), "another action");
        Assert.IsFalse(_store.HasRecentAudit("X_ACTION", "T1", TimeSpan.FromSeconds(-5)), "outside the window");
    }
}
