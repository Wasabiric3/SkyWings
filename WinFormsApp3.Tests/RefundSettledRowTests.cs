using System.Reflection;
using Microsoft.Data.Sqlite;
using SkyWings.Email;
using SkyWings.UI;

namespace SkyWings.Tests;

/// <summary>An email about a booking that was already cancelled leaves a closed "Settled" row on the Refunds tab, once a day per booking.</summary>
[TestClass]
[DoNotParallelize]
public sealed class RefundSettledRowTests
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static readonly DateTime Day = new(2030, 3, 5);

    private string _dbPath = "";
    private string _connection = "";
    private BookingStore _store = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"skywings-settled-{Guid.NewGuid():N}.db");
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

    private void Sql(string sql)
    {
        using var con = new SqliteConnection(_connection);
        con.Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    [TestMethod]
    public void AnEmailForACancelledBooking_LeavesOneClosedSettledRow_WithTheRefund()
    {
        Save("SKY-CCCC00000001");
        Assert.IsTrue(_store.CancelBooking("SKY-CCCC00000001", 100m));
        RefundRecord? refund = _store.LoadRefund("SKY-CCCC00000001");

        new RefundMailProcessor(_store).Process(new[] { Mail(1, "SKY-CCCC00000001") });

        RefundRequestRow row = _store.LoadRefundRequests().Single();
        Assert.AreEqual(BookingStore.RequestSettled, row.Status);
        Assert.AreEqual("EMAIL", row.Source);
        Assert.IsTrue(row.SenderMatches);
        Assert.IsNotNull(row.ClosedAt, "closed at once: there is nothing left to decide");
        Assert.AreEqual("system", row.ClosedBy);
        Assert.AreEqual("Please cancel and refund.", row.CustomerMessage);
        StringAssert.Contains(row.Note, refund is null ? "Already cancelled" : refund.Reference);
        Assert.AreEqual(0, _store.CountOpenRefundRequests(), "a settled row is never a pending request");
    }

    [TestMethod]
    public void ASettledRowFromAnotherAddress_IsFlagged()
    {
        Save("SKY-CCCC00000002");
        Assert.IsTrue(_store.CancelBooking("SKY-CCCC00000002", 100m));

        new RefundMailProcessor(_store).Process(new[] { Mail(1, "SKY-CCCC00000002", "someone.else@example.com") });

        Assert.IsFalse(_store.LoadRefundRequests().Single().SenderMatches, "someone who is not the booking's owner knows this reference");
    }

    [TestMethod]
    public void WritingAgainTheSameDay_DoesNotAddAnotherRow()
    {
        Save("SKY-CCCC00000003");
        Assert.IsTrue(_store.CancelBooking("SKY-CCCC00000003", 100m));
        var processor = new RefundMailProcessor(_store);

        processor.Process(new[] { Mail(1, "SKY-CCCC00000003") });
        processor.Process(new[] { Mail(2, "SKY-CCCC00000003"), Mail(3, "SKY-CCCC00000003", "spam@example.com") });

        Assert.HasCount(1, _store.LoadRefundRequests(includeClosed: true));
    }

    [TestMethod]
    public void ASettledRow_StaysInTheDefaultListForADay_ThenOnlyUnderShowClosed()
    {
        Save("SKY-CCCC00000004");
        Assert.IsTrue(_store.CancelBooking("SKY-CCCC00000004", 100m));
        new RefundMailProcessor(_store).Process(new[] { Mail(1, "SKY-CCCC00000004") });
        Assert.HasCount(1, _store.LoadRefundRequests());

        Sql($"UPDATE refund_requests SET logged_at = '{DateTime.Now.AddDays(-2):yyyy-MM-dd HH:mm:ss}', closed_at = '{DateTime.Now.AddDays(-2):yyyy-MM-dd HH:mm:ss}' WHERE status = 'SETTLED'");

        Assert.IsEmpty(_store.LoadRefundRequests(), "two days on it is only history");
        Assert.HasCount(1, _store.LoadRefundRequests(includeClosed: true));
    }

    [TestMethod]
    public void NothingIsKept_ForAnUnknownReference_ADemoBooking_OrAStaffEntry()
    {
        Save("SKY-CCCC00000005");
        Save("SKY-CCCC00000006");
        Assert.IsTrue(_store.CancelBooking("SKY-CCCC00000005", 100m));
        Assert.IsTrue(_store.CancelBooking("SKY-CCCC00000006", 100m));
        Sql("UPDATE bookings SET is_simulated = 1 WHERE reference = 'SKY-CCCC00000006'");

        new RefundMailProcessor(_store).Process(new[] { Mail(1, "SKY-CCCC99999999"), Mail(2, "SKY-CCCC00000006") });
        _store.LogRefundRequest("SKY-CCCC00000005", "agent1");   // typed in at the desk: staff know, no note is needed

        Assert.IsEmpty(_store.LoadRefundRequests(includeClosed: true));
    }

    [TestMethod]
    public void ApprovedAndDeclinedRequests_AreNotAffected()
    {
        Save("SKY-CCCC00000007");
        Assert.IsTrue(_store.LogRefundRequest("SKY-CCCC00000007", "email", null, "hi", "EMAIL", true).Ok);

        RefundRequestRow open = _store.LoadRefundRequests().Single();
        Assert.AreEqual(BookingStore.RequestOpen, open.Status);
        Assert.AreEqual(1, _store.CountOpenRefundRequests());
    }

    // ---------------------------------------------------------------- the screen

    private static IEnumerable<Control> All(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (Control nested in All(child)) yield return nested;
        }
    }

    private static T Field<T>(AdminForm form, string name) where T : class => (T)typeof(AdminForm).GetField(name, Any)!.GetValue(form)!;

    [TestMethod]
    public void TheRefundsTab_ShowsTheSettledRows_AmberWhenTheSenderDiffers() => FormHarness.OnStaThread(() =>
    {
        using var harness = new FormHarness();
        BookingRecord booking = harness.SampleBooking("SKY-AAAA00000099");   // a real-looking reference: SKY- and 12 hex characters
        harness.Store.SaveBookingContact(booking.Reference, "ana@example.com");
        Assert.IsTrue(harness.Store.CancelBooking(booking.Reference, 100m));
        new RefundMailProcessor(harness.Store).Process(new[] { Mail(1, booking.Reference, "stranger@example.com") });

        using var form = new AdminForm(harness.Admin) { StartPosition = FormStartPosition.Manual, Location = new Point(20, 20) };
        form.Show();
        Application.DoEvents();
        All(form).OfType<GlassButton>().First(b => b.Text.StartsWith("Refunds", StringComparison.Ordinal) && b.Parent is FlowLayoutPanel).PerformClick();
        Application.DoEvents();
        Field<ComboBox>(form, "_refundView").SelectedIndex = 1;   // the requests view
        Application.DoEvents();

        var grid = Field<DataGridView>(form, "_requests");
        Assert.AreEqual(1, grid.Rows.Count);
        var item = grid.Rows[0].DataBoundItem!;
        Assert.AreEqual("Settled", item.GetType().GetProperty("Status", Any)!.GetValue(item));
        Assert.AreEqual(true, item.GetType().GetProperty("SenderDiffers", Any)!.GetValue(item), "the row is amber: someone else asked");
        StringAssert.Contains((string)item.GetType().GetProperty("Note", Any)!.GetValue(item)!, "Sender differs");
        StringAssert.Contains(Field<Label>(form, "_refundCount").Text, "1 settled");
        FormHarness.SaveSnapshot(form, "refunds-tab-settled-row");
    });
}
