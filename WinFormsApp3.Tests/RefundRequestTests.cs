using Microsoft.Data.Sqlite;
using SkyWings.Email;

namespace SkyWings.Tests;

/// <summary>Refund requests (a customer asks by email, staff log it, cancelling the booking closes it) and the cancellation email.</summary>
[TestClass]
[DoNotParallelize]
public sealed class RefundRequestTests
{
    private static readonly DateTime Day = new(2030, 3, 5);
    private const string Mailbox = "desk@skywings.example";

    private string _dbPath = "";
    private string _connection = "";
    private BookingStore _store = null!;

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
        public byte[] RenderPng(string text, int pixelsPerModule = 8) => new byte[] { 0x89, 0x50, 0x4E, 0x47 };
    }

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"skywings-reqs-{Guid.NewGuid():N}.db");
        _connection = new SqliteConnectionStringBuilder { DataSource = _dbPath, ForeignKeys = true }.ConnectionString;
        _store = new BookingStore(_connection);
        _store.SeedFlightsIfEmpty(AppData.DomesticFlights, AppData.InternationalFlights,
            AppData.DefaultDomesticCapacity, AppData.DefaultInternationalCapacity);
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private BookingRecord Save(string reference, string? email = null, decimal total = 4_000m, DateTime? date = null)
    {
        var person = new PassengerRecord("Ana Example", "1 St", "09171234567", 35, "1991-01-01", "P1234567A", false, false,
            new SeatAssignment("Ana Example", "ECO1A"), Array.Empty<string>(), total);
        var booking = new BookingRecord(reference, DateTime.Now, "PR-101", "Manila (MNL)", "Cebu (CEB)", date ?? Day, true, "Economy",
            "Credit Card", "**** 4521", total, "Scheduled", new[] { person }, AppData.StatusConfirmed, "PAID", "TXN-" + reference, DateTime.Now, 0m);
        _store.SaveConfirmedBooking(booking, false, total, "Credit Card", "**** 4521", "TXN-" + reference, DateTime.Now);
        if (email is not null) _store.SaveBookingContact(reference, email);
        return booking;
    }

    private void Sql(string sql)
    {
        using var con = new SqliteConnection(_connection);
        con.Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    // ---------------------------------------------------------------- logging a request

    [TestMethod]
    public void Log_RecordsTheRequestWithTheRefundIfCancelledToday_AndCancelsNothing()
    {
        Save("SKY-REQ000001");

        RefundRequestResult result = _store.LogRefundRequest("sky-req000001 ", "agent1");

        Assert.IsTrue(result.Ok, result.Message);
        StringAssert.Contains(result.Message, "3,200.00", "80% of 4,000, a week or more ahead");
        RefundRequestRow row = _store.LoadRefundRequests().Single();
        Assert.AreEqual("SKY-REQ000001", row.Reference);
        Assert.AreEqual(BookingStore.RequestOpen, row.Status);
        Assert.AreEqual(3_200m, row.EstimatedRefund);
        Assert.AreEqual("agent1", row.LoggedBy);
        Assert.AreEqual("Manila (MNL) → Cebu (CEB)", row.Route);
        Assert.AreEqual(1, _store.CountOpenRefundRequests());
        Assert.AreEqual(AppData.StatusConfirmed, _store.FindByReference("SKY-REQ000001")!.Status, "only staff cancelling does that");
        Assert.IsNull(_store.LoadRefund("SKY-REQ000001"));
    }

    [TestMethod]
    public void Log_IsRefusedWithAReason_ForEveryBookingThatCannotBeRefunded()
    {
        Save("SKY-REQ000010");
        Save("SKY-REQ000011");
        Save("SKY-REQ000012");
        Assert.IsTrue(_store.LogRefundRequest("SKY-REQ000010", "a").Ok);

        StringAssert.Contains(_store.LogRefundRequest("SKY-REQ000010", "a").Message, "already has an open", "a second request for the same booking");
        StringAssert.Contains(_store.LogRefundRequest("SKY-NOSUCHBOOK", "a").Message, "No booking");
        Assert.IsTrue(_store.CancelBooking("SKY-REQ000011", 100m));
        StringAssert.Contains(_store.LogRefundRequest("SKY-REQ000011", "a").Message, "confirmed");
        StringAssert.Contains(_store.LogRefundRequest("SKY-REQ000012", "a", now: Day.AddDays(1)).Message, "already left");
        Sql("UPDATE bookings SET is_simulated = 1 WHERE reference = 'SKY-REQ000012'");
        Assert.IsFalse(_store.LogRefundRequest("SKY-REQ000012", "a").Ok, "demo bookings have no real customer");
        Assert.AreEqual(1, _store.CountOpenRefundRequests());
    }

    [TestMethod]
    public void Cancelling_ClosesTheOpenRequest_AndAuditsIt()
    {
        Save("SKY-REQ000020");
        Save("SKY-REQ000021");
        Assert.IsTrue(_store.LogRefundRequest("SKY-REQ000020", "agent1").Ok);

        Assert.IsTrue(_store.CancelBooking("SKY-REQ000020", 3_200m, "agent2"));
        Assert.IsTrue(_store.CancelBooking("SKY-REQ000021", 3_200m, "agent2"));

        Assert.AreEqual(0, _store.CountOpenRefundRequests());
        RefundRequestRow done = _store.LoadRefundRequests(includeClosed: true).Single();
        Assert.AreEqual(BookingStore.RequestDone, done.Status);
        Assert.AreEqual("agent2", done.ClosedBy);
        Assert.IsNotNull(done.ClosedAt);
        Assert.AreEqual(1, _store.LoadAudit().Count(a => a.Action == "REFUND_REQUEST_CLOSED"), "only the booking that had a request");
        Assert.AreEqual("SKY-REQ000020", _store.LoadAudit().Single(a => a.Action == "REFUND_REQUEST_CLOSED").Target);
        // Once cancelled, no new request can be made for it.
        Assert.IsFalse(_store.LogRefundRequest("SKY-REQ000020", "a").Ok);
    }

    [TestMethod]
    public void Dismiss_ClosesAnOpenRequestOnce_WithTheReason_AndLeavesTheBooking()
    {
        Save("SKY-REQ000030");
        Assert.IsTrue(_store.LogRefundRequest("SKY-REQ000030", "a").Ok);
        long id = _store.LoadRefundRequests().Single().Id;

        Assert.IsTrue(_store.DismissRefundRequest(id, "agent1", "Customer changed their mind"));
        Assert.IsFalse(_store.DismissRefundRequest(id, "agent1", "again"), "already closed");

        Assert.IsEmpty(_store.LoadRefundRequests());
        RefundRequestRow row = _store.LoadRefundRequests(includeClosed: true).Single();
        Assert.AreEqual(BookingStore.RequestDismissed, row.Status);
        Assert.AreEqual("Customer changed their mind", row.Note);
        Assert.AreEqual(AppData.StatusConfirmed, _store.FindByReference("SKY-REQ000030")!.Status);
        Assert.IsTrue(_store.LogRefundRequest("SKY-REQ000030", "a").Ok, "after a dismissal the customer may ask again");
    }

    [TestMethod]
    public void TheList_ShowsOpenRequestsFirst()
    {
        Save("SKY-REQ000040");
        Save("SKY-REQ000041");
        Assert.IsTrue(_store.LogRefundRequest("SKY-REQ000040", "a").Ok);
        Assert.IsTrue(_store.CancelBooking("SKY-REQ000040", 1m));   // closes the first
        Assert.IsTrue(_store.LogRefundRequest("SKY-REQ000041", "a").Ok);

        CollectionAssert.AreEqual(new[] { "SKY-REQ000041", "SKY-REQ000040" }, _store.LoadRefundRequests(includeClosed: true).Select(r => r.Reference).ToArray());
        CollectionAssert.AreEqual(new[] { "SKY-REQ000041" }, _store.LoadRefundRequests().Select(r => r.Reference).ToArray());
    }

    [TestMethod]
    public void AnOlderDatabaseWithoutTheTable_IsUpgradedWhenOpened()
    {
        Sql("DROP TABLE refund_requests");
        var reopened = new BookingStore(_connection);
        Assert.AreEqual(0, reopened.CountOpenRefundRequests());
        Save("SKY-REQ000050");
        Assert.IsTrue(reopened.LogRefundRequest("SKY-REQ000050", "a").Ok);
    }

    [TestMethod]
    public void AdminService_LogsDismissesAndAudits_AndNeedsTheViewBookingsPermission()
    {
        Save("SKY-REQ000060");
        var agent = new AdminService(_store, new StaffSession(1, "agent1", "Agent", StaffRole.Agent, false));

        Assert.IsTrue(agent.LogRefundRequest("SKY-REQ000060").Ok);
        Assert.AreEqual(1, agent.CountOpenRefundRequests());
        RefundRequestRow row = agent.LoadRefundRequests().Single();
        Assert.IsTrue(agent.DismissRefundRequest(row.Id, row.Reference, "Duplicate email"));
        CollectionAssert.IsSubsetOf(new[] { "REFUND_REQUEST_LOGGED", "REFUND_REQUEST_DISMISSED" }, _store.LoadAudit().Select(a => a.Action).ToArray());
        Assert.AreEqual("agent1", _store.LoadAudit().First(a => a.Action == "REFUND_REQUEST_LOGGED").Actor);
    }

    // ---------------------------------------------------------------- the event

    [TestMethod]
    public void BookingCanceled_IsRaisedOnceForARealCancellation_AndNotForARefusedOne()
    {
        Save("SKY-REQ000070");
        var seen = new List<string>();
        _store.BookingCanceled += seen.Add;

        Assert.IsTrue(_store.CancelBooking("SKY-REQ000070", 100m));
        Assert.IsFalse(_store.CancelBooking("SKY-REQ000070", 100m), "already cancelled");

        CollectionAssert.AreEqual(new[] { "SKY-REQ000070" }, seen);
    }

    // ---------------------------------------------------------------- the emails

    [TestMethod]
    public void ReceiptAndBoardingPassEmails_OfferARefundRequest_OnlyWhileTheFlightIsAhead_AndAMailboxExists()
    {
        BookingRecord booking = Save("SKY-REQ000080", total: 4_000m);
        byte[] qr = { 1 };
        var attachments = new List<EmailAttachment> { new("pass.pdf", new byte[] { 1 }, "application/pdf", "boarding pass") };

        EmailMessage receipt = BookingEmailBuilder.Build(booking, "ana@example.com", qr, Mailbox, null, Day.AddDays(-20));
        StringAssert.Contains(receipt.HtmlBody, "Request cancellation / refund");
        StringAssert.Contains(receipt.HtmlBody, "mailto:" + Mailbox + "?subject=Refund%20request%20SKY-REQ000080");
        StringAssert.Contains(receipt.HtmlBody, "PHP 3,200.00");
        StringAssert.Contains(receipt.TextBody, "Refund request SKY-REQ000080");
        StringAssert.Contains(receipt.TextBody, "stays active until SkyWings staff confirm");

        EmailMessage pass = BookingEmailBuilder.BuildBoardingPass(booking, "ana@example.com", qr, Mailbox, attachments, Day.AddDays(-1));
        StringAssert.Contains(pass.HtmlBody, "Request cancellation / refund");
        StringAssert.Contains(pass.HtmlBody, "PHP 3,200.00", "the amount follows the refund policy for the real date; only the has-it-left check uses the given time");

        Assert.DoesNotContain("Request cancellation", BookingEmailBuilder.Build(booking, "a@b.co", qr, null, null, Day.AddDays(-20)).HtmlBody, "outbox-only mode has no mailbox");
        Assert.DoesNotContain("Request cancellation", BookingEmailBuilder.Build(booking, "a@b.co", qr, Mailbox, null, Day.AddDays(1)).HtmlBody, "the flight has left");
        Assert.DoesNotContain("refund", BookingEmailBuilder.Build(booking, "a@b.co", qr, Mailbox, null, Day.AddDays(1)).TextBody, StringComparison.OrdinalIgnoreCase);
        Assert.IsTrue(_store.CancelBooking(booking.Reference, 100m));
        Assert.DoesNotContain("Request cancellation", BookingEmailBuilder.Build(_store.FindByReference(booking.Reference)!, "a@b.co", qr, Mailbox, null, Day.AddDays(-20)).HtmlBody, "already cancelled");
    }

    [TestMethod]
    public void CancellationEmail_CarriesTheAmountTheReferenceAndWhereItWentBack()
    {
        Save("SKY-REQ000090", "ana@example.com");
        Assert.IsTrue(_store.CancelBooking("SKY-REQ000090", 3_200m));
        BookingRecord canceled = _store.FindByReference("SKY-REQ000090")!;
        RefundRecord refund = _store.LoadRefund("SKY-REQ000090")!;

        EmailMessage message = BookingEmailBuilder.BuildCancellation(canceled, refund, "ana@example.com", Mailbox);

        Assert.AreEqual("SkyWings booking SKY-REQ000090 is cancelled", message.Subject);
        foreach (string expected in new[] { "SKY-REQ000090", "PHP 3,200.00", refund.Reference, "Credit Card **** 4521", "PR-101" })
        {
            StringAssert.Contains(message.HtmlBody, expected);
            StringAssert.Contains(message.TextBody, expected);
        }
        StringAssert.Contains(message.HtmlBody, "Unsubscribe");

        Save("SKY-REQ000091");
        Assert.IsTrue(_store.CancelBooking("SKY-REQ000091", 0m));
        EmailMessage none = BookingEmailBuilder.BuildCancellation(_store.FindByReference("SKY-REQ000091")!, null, "ana@example.com");
        StringAssert.Contains(none.TextBody, "No refund is due");
    }

    [TestMethod]
    public async Task EverySuccessfulCancellation_EmailsTheCustomer_ButNotWhenItShouldNot()
    {
        var sender = new RecordingSender();
        var service = new BookingEmailService(_store, sender, new FakeQr(), Mailbox);
        service.Attach();
        Save("SKY-REQ000100", "ana@example.com");          // gets the email
        Save("SKY-REQ000101");                              // no address on the booking
        Save("SKY-REQ000102", "stop@example.com");          // unsubscribed
        Save("SKY-REQ000103", "demo@example.com");          // a demo booking
        _store.SetEmailSubscribed("stop@example.com", false);
        Sql("UPDATE bookings SET is_simulated = 1 WHERE reference = 'SKY-REQ000103'");

        foreach (string reference in new[] { "SKY-REQ000100", "SKY-REQ000101", "SKY-REQ000102", "SKY-REQ000103" })
            Assert.IsTrue(_store.CancelBooking(reference, 3_200m));
        await Task.Delay(500);   // the handler sends in the background

        EmailMessage only = sender.Sent.Single();
        Assert.AreEqual("ana@example.com", only.To);
        StringAssert.Contains(only.HtmlBody, "SKY-REQ000100");
        Assert.IsTrue(_store.LoadAudit().Any(a => a.Action == "EMAIL_CANCELLATION" && a.Target == "SKY-REQ000100"));
    }

    [TestMethod]
    public async Task SendCancellation_SkipsABookingThatIsNotCancelled_AndNeverThrowsOnAMailFailure()
    {
        BookingRecord confirmed = Save("SKY-REQ000110", "ana@example.com");
        var service = new BookingEmailService(_store, new RecordingSender(), new FakeQr(), Mailbox);
        EmailSendResult skipped = await service.SendCancellationAsync(confirmed, "ana@example.com");
        Assert.AreEqual("Skipped", skipped.Channel);

        Assert.IsTrue(_store.CancelBooking(confirmed.Reference, 100m));
        var failing = new BookingEmailService(_store, new ThrowingSender(), new FakeQr(), Mailbox);
        EmailSendResult failed = await failing.SendCancellationAsync(_store.FindByReference(confirmed.Reference)!, "ana@example.com");
        Assert.AreEqual("Failed", failed.Channel);
    }

    // ---------------------------------------------------------------- approve and decline

    [TestMethod]
    public async Task Approve_CancelsRefundsClosesTheRequest_AndEmailsThatTheRefundIsApproved()
    {
        var sender = new RecordingSender();
        new BookingEmailService(_store, sender, new FakeQr(), Mailbox).Attach();
        Save("SKY-REQ000200", "ana@example.com");
        Assert.IsTrue(_store.LogRefundRequest("SKY-REQ000200", "agent1", customerMessage: "I cannot travel any more, sorry.").Ok);
        long id = _store.LoadRefundRequests().Single().Id;

        RefundApprovalResult result = _store.ApproveRefundRequest(id, "agent2");

        Assert.IsTrue(result.Ok, result.Message);
        Assert.AreEqual(3_200m, result.Refund);
        StringAssert.StartsWith(result.RefundReference, "RFD-");
        BookingRecord after = _store.FindByReference("SKY-REQ000200")!;
        Assert.AreEqual(AppData.StatusCanceled, after.Status);
        Assert.AreEqual(3_200m, after.RefundAmount);
        Assert.AreEqual(result.RefundReference, _store.LoadRefund("SKY-REQ000200")!.Reference);
        RefundRequestRow done = _store.LoadRefundRequests(includeClosed: true).Single();
        Assert.AreEqual(BookingStore.RequestDone, done.Status);
        Assert.AreEqual("agent2", done.ClosedBy);
        Assert.IsTrue(_store.HasCompletedRefundRequest("SKY-REQ000200"));

        await Task.Delay(500);
        EmailMessage email = sender.Sent.Single();
        Assert.AreEqual("ana@example.com", email.To);
        Assert.AreEqual("SkyWings refund approved for booking SKY-REQ000200", email.Subject);
        foreach (string expected in new[] { "approved", "has been refunded to your original payment method", "PHP 3,200.00", result.RefundReference, "Credit Card **** 4521" })
            StringAssert.Contains(email.HtmlBody, expected);
        StringAssert.Contains(email.TextBody, result.RefundReference);
    }

    [TestMethod]
    public void Approve_IsRefusedWhenTheRequestIsClosedTheBookingIsGoneOrTheFlightHasLeft_AndChangesNothing()
    {
        Save("SKY-REQ000210");
        Save("SKY-REQ000211");
        Assert.IsTrue(_store.LogRefundRequest("SKY-REQ000210", "a").Ok);
        Assert.IsTrue(_store.LogRefundRequest("SKY-REQ000211", "a").Ok);
        long first = _store.LoadRefundRequests(includeClosed: true).Single(r => r.Reference == "SKY-REQ000210").Id;
        long second = _store.LoadRefundRequests(includeClosed: true).Single(r => r.Reference == "SKY-REQ000211").Id;

        // The flight has left: refused, still open.
        RefundApprovalResult late = _store.ApproveRefundRequest(first, "a", now: Day.AddDays(1));
        Assert.IsFalse(late.Ok);
        StringAssert.Contains(late.Message, "already left");
        Assert.AreEqual(AppData.StatusConfirmed, _store.FindByReference("SKY-REQ000210")!.Status);

        // Another desk cancelled it first (that already closed the request), so approving again is refused and refunds nothing twice.
        Assert.IsTrue(_store.CancelBooking("SKY-REQ000211", 1_000m, "other"));
        RefundApprovalResult again = _store.ApproveRefundRequest(second, "a");
        Assert.IsFalse(again.Ok);
        Assert.AreEqual(1, _store.LoadRefunds("SKY-REQ000211").Count);
        Assert.IsFalse(_store.ApproveRefundRequest(99999, "a").Ok, "no such request");
    }

    [TestMethod]
    public async Task Decline_WithNotice_EmailsTheReason_AndTheBookingStaysConfirmed()
    {
        var sender = new RecordingSender();
        new BookingEmailService(_store, sender, new FakeQr(), Mailbox).Attach();
        Save("SKY-REQ000220", "ana@example.com");
        Assert.IsTrue(_store.LogRefundRequest("SKY-REQ000220", "a").Ok);
        long id = _store.LoadRefundRequests().Single().Id;

        Assert.IsTrue(_store.DismissRefundRequest(id, "agent1", "Flight is inside the no-refund window", notifyCustomer: true));
        Assert.IsFalse(_store.DismissRefundRequest(id, "agent1", "twice", notifyCustomer: true), "already closed: no second email");
        await Task.Delay(500);

        EmailMessage email = sender.Sent.Single();
        Assert.AreEqual("SkyWings refund request for booking SKY-REQ000220", email.Subject);
        StringAssert.Contains(email.HtmlBody, "Flight is inside the no-refund window");
        StringAssert.Contains(email.HtmlBody, "still confirmed");
        StringAssert.Contains(email.TextBody, "Reason: Flight is inside the no-refund window");
        Assert.AreEqual(AppData.StatusConfirmed, _store.FindByReference("SKY-REQ000220")!.Status);
        Assert.IsNull(_store.LoadRefund("SKY-REQ000220"));
        Assert.IsTrue(_store.LoadAudit().Any(a => a.Action == "EMAIL_REFUND_DECLINED" && a.Target == "SKY-REQ000220"));
    }

    [TestMethod]
    public async Task Decline_WithoutNotice_SendsNothing_AndNoAddressMeansNoEmail()
    {
        var sender = new RecordingSender();
        new BookingEmailService(_store, sender, new FakeQr(), Mailbox).Attach();
        Save("SKY-REQ000230", "ana@example.com");
        Save("SKY-REQ000231");   // no address on the booking
        Assert.IsTrue(_store.LogRefundRequest("SKY-REQ000230", "a").Ok);
        Assert.IsTrue(_store.LogRefundRequest("SKY-REQ000231", "a").Ok);
        foreach (RefundRequestRow row in _store.LoadRefundRequests())
            Assert.IsTrue(_store.DismissRefundRequest(row.Id, "agent1", "Duplicate", notifyCustomer: row.Reference == "SKY-REQ000231"));
        await Task.Delay(500);
        Assert.IsEmpty(sender.Sent);
    }

    [TestMethod]
    public void TheCustomersMessage_IsKeptAndShown_AndAnOlderTableGainsTheColumn()
    {
        Save("SKY-REQ000240");
        Assert.IsTrue(_store.LogRefundRequest("SKY-REQ000240", "a", customerMessage: new string('x', 1500)).Ok);
        RefundRequestRow row = _store.LoadRefundRequests().Single();
        Assert.AreEqual(1000, row.CustomerMessage.Length, "capped");

        Sql("ALTER TABLE refund_requests DROP COLUMN customer_message");
        var reopened = new BookingStore(_connection);   // opening an older database adds the column back
        Assert.AreEqual("", reopened.LoadRefundRequests().Single().CustomerMessage);
    }

    [TestMethod]
    public void AdminService_ApproveAndDecline_AreAuditedWithTheStaffMember()
    {
        Save("SKY-REQ000250");
        Save("SKY-REQ000251");
        var agent = new AdminService(_store, new StaffSession(1, "agent1", "Agent", StaffRole.Agent, false));
        Assert.IsTrue(agent.LogRefundRequest("SKY-REQ000250").Ok);
        Assert.IsTrue(agent.LogRefundRequest("SKY-REQ000251").Ok);
        RefundRequestRow a = agent.LoadRefundRequests().Single(r => r.Reference == "SKY-REQ000250");
        RefundRequestRow b = agent.LoadRefundRequests().Single(r => r.Reference == "SKY-REQ000251");

        RefundApprovalResult approved = agent.ApproveRefundRequest(a.Id, a.Reference);
        Assert.IsTrue(approved.Ok);
        Assert.IsTrue(agent.DismissRefundRequest(b.Id, b.Reference, "Not eligible", notifyCustomer: true));

        var audit = _store.LoadAudit();
        Assert.IsTrue(audit.Any(x => x.Action == "REFUND_REQUEST_APPROVED" && x.Actor == "agent1" && x.Detail.Contains(approved.RefundReference)));
        Assert.IsTrue(audit.Any(x => x.Action == "REFUND_REQUEST_DISMISSED" && x.Detail.StartsWith("Customer told.")));
        Assert.IsFalse(agent.ApproveRefundRequest(a.Id, a.Reference).Ok, "a second approval is refused");
        Assert.IsTrue(_store.LoadAudit().Any(x => x.Action == "REFUND_REQUEST_APPROVAL_SKIPPED"));
    }
    private sealed class ThrowingSender : IEmailSender
    {
        public string Name => "Throwing";
        public Task<EmailSendResult> SendAsync(EmailMessage message, CancellationToken cancellationToken = default)
            => throw new IOException("mail server down");
    }
}
