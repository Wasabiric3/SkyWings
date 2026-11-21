using Microsoft.Data.Sqlite;

namespace SkyWings.Tests;

/// <summary>The staff Refunds tab: cancelled bookings with the refund paid back, who handled it, search and totals.</summary>
[TestClass]
[DoNotParallelize]
public sealed class RefundListTests
{
    private string _dbPath = "";
    private BookingStore _store = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"skywings-refundlist-{Guid.NewGuid():N}.db");
        _store = new BookingStore(new SqliteConnectionStringBuilder { DataSource = _dbPath, ForeignKeys = true }.ConnectionString);
        _store.SeedFlightsIfEmpty(AppData.DomesticFlights, AppData.InternationalFlights,
            AppData.DefaultDomesticCapacity, AppData.DefaultInternationalCapacity);
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private BookingRecord Save(string reference, decimal total = 4_000m)
    {
        var person = new PassengerRecord("Ana Example", "1 St", "09171234567", 35, "1991-01-01", "P1234567A", false, false,
            new SeatAssignment("Ana Example", "ECO1A"), Array.Empty<string>(), total);
        var booking = new BookingRecord(reference, DateTime.Now, "PR-101", "Manila (MNL)", "Cebu (CEB)", new DateTime(2030, 3, 5), true,
            "Economy", "Credit Card", "**** 4521", total, "Scheduled", new[] { person }, AppData.StatusConfirmed, "PAID", "TXN-" + reference, DateTime.Now, 0m);
        _store.SaveConfirmedBooking(booking, false, total, "Credit Card", "**** 4521", "TXN-" + reference, DateTime.Now);
        return booking;
    }

    [TestMethod]
    public void ListsRefundedCancellations_NewestFirst_WithReferenceMethodAndWhoHandledIt()
    {
        Save("SKY-RFL000001");
        Save("SKY-RFL000002");
        Save("SKY-RFL000003");   // stays confirmed: never listed
        Assert.IsTrue(_store.CancelBooking("SKY-RFL000001", 3_200m, "agent1"));
        Assert.IsTrue(_store.CancelBooking("SKY-RFL000002", 2_000m, "customer"));

        RefundList list = _store.LoadRefundList();

        Assert.AreEqual(2, list.MatchCount);
        Assert.AreEqual(5_200m, list.MatchRefundTotal);
        CollectionAssert.AreEqual(new[] { "SKY-RFL000002", "SKY-RFL000001" }, list.Rows.Select(r => r.Reference).ToArray(), "newest refund first");
        RefundListRow first = list.Rows[1];
        Assert.AreEqual(3_200m, first.Refund);
        Assert.AreEqual(4_000m, first.Total);
        Assert.AreEqual("agent1", first.HandledBy);
        Assert.AreEqual("Credit Card **** 4521", first.Method);
        StringAssert.StartsWith(first.RefundReference, "RFD-");
        Assert.IsNotNull(first.RefundedAt);
        Assert.AreEqual("Manila (MNL) → Cebu (CEB)", first.Route);
    }

    [TestMethod]
    public void NoRefundCancellations_AreHiddenUntilAsked_AndOlderOnesWithoutARefundRecordStillAppear()
    {
        Save("SKY-RFL000010");
        Save("SKY-RFL000011");
        Save("SKY-RFL000012");
        Assert.IsTrue(_store.CancelBooking("SKY-RFL000010", 0m));                 // cancelled, nothing returned
        _store.UpdateStatus("SKY-RFL000011", AppData.StatusCanceled, 1_500m);      // an older cancellation: no refund record
        Assert.IsTrue(_store.CancelBooking("SKY-RFL000012", 800m));

        RefundList defaults = _store.LoadRefundList();
        CollectionAssert.AreEquivalent(new[] { "SKY-RFL000011", "SKY-RFL000012" }, defaults.Rows.Select(r => r.Reference).ToArray());
        RefundListRow legacy = defaults.Rows.Single(r => r.Reference == "SKY-RFL000011");
        Assert.AreEqual("", legacy.RefundReference);
        Assert.IsNull(legacy.RefundedAt);
        Assert.AreEqual(1_500m, legacy.Refund);

        RefundList all = _store.LoadRefundList(includeNoRefund: true);
        Assert.AreEqual(3, all.MatchCount);
        Assert.AreEqual(2_300m, all.MatchRefundTotal);
    }

    [TestMethod]
    public void Search_FindsByBookingReferenceFlightOrRefundReference_AndTotalsFollowTheSearch()
    {
        Save("SKY-RFL000020");
        Save("SKY-RFL000021");
        _store.CancelBooking("SKY-RFL000020", 1_000m);
        _store.CancelBooking("SKY-RFL000021", 2_000m);
        string refundRef = _store.LoadRefund("SKY-RFL000021")!.Reference;

        Assert.AreEqual(1, _store.LoadRefundList("RFL000020").MatchCount);
        Assert.AreEqual(2, _store.LoadRefundList("pr-101").MatchCount);
        RefundList byRef = _store.LoadRefundList(refundRef);
        CollectionAssert.AreEqual(new[] { "SKY-RFL000021" }, byRef.Rows.Select(r => r.Reference).ToArray());
        Assert.AreEqual(2_000m, byRef.MatchRefundTotal);
        Assert.AreEqual(0, _store.LoadRefundList("%").MatchCount, "a wildcard character is searched for literally");
    }

    [TestMethod]
    public void TheRowCap_KeepsTheTotalsOverEveryMatch()
    {
        for (int i = 0; i < 3; i++)
        {
            Save($"SKY-RFL00003{i}");
            _store.CancelBooking($"SKY-RFL00003{i}", 100m);
        }
        RefundList list = _store.LoadRefundList(limit: 2);
        Assert.HasCount(2, list.Rows);
        Assert.AreEqual(3, list.MatchCount);
        Assert.AreEqual(300m, list.MatchRefundTotal);
    }

    [TestMethod]
    public void AdminService_LetsAnAgentLoadTheList()
    {
        var agent = new AdminService(_store, new StaffSession(1, "agent", "Agent", StaffRole.Agent, false));
        Assert.IsNotNull(agent.LoadRefundList());
    }

    private void Sql(string sql)
    {
        using var con = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _dbPath, ForeignKeys = true }.ConnectionString);
        con.Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private void RefundAt(string reference, string when)
        => Sql($"UPDATE booking_payments SET created_at = '{when}' WHERE booking_ref = '{reference}' AND status = 'REFUND'");

    [TestMethod]
    public void Period_KeepsOnlyRefundsFromThenOn_AndTheTotalsFollowIt()
    {
        Save("SKY-RFL000100");
        Save("SKY-RFL000101");
        _store.CancelBooking("SKY-RFL000100", 1_000m, "agent1");
        _store.CancelBooking("SKY-RFL000101", 2_000m, "agent1");
        RefundAt("SKY-RFL000100", DateTime.Now.AddDays(-20).ToString("yyyy-MM-dd HH:mm:ss"));

        Assert.AreEqual(2, _store.LoadRefundList().MatchCount, "any time");
        RefundList week = _store.LoadRefundList(since: DateTime.Now.AddDays(-7));
        CollectionAssert.AreEqual(new[] { "SKY-RFL000101" }, week.Rows.Select(r => r.Reference).ToArray());
        Assert.AreEqual(2_000m, week.MatchRefundTotal);
        Assert.AreEqual(2, _store.LoadRefundList(since: DateTime.Now.AddDays(-30)).MatchCount);
        Assert.AreEqual(1, _store.LoadRefundList(since: DateTime.Today).MatchCount, "today");
    }

    [TestMethod]
    public void Sort_OrdersByTimeOrByAmount()
    {
        foreach (string r in new[] { "SKY-RFL000110", "SKY-RFL000111", "SKY-RFL000112" }) Save(r);
        _store.CancelBooking("SKY-RFL000110", 500m, "a");
        _store.CancelBooking("SKY-RFL000111", 3_000m, "a");
        _store.CancelBooking("SKY-RFL000112", 1_000m, "a");
        RefundAt("SKY-RFL000110", "2026-01-01 10:00:00");
        RefundAt("SKY-RFL000111", "2026-02-01 10:00:00");
        RefundAt("SKY-RFL000112", "2026-03-01 10:00:00");

        string[] Order(string sort) => _store.LoadRefundList(sort: sort).Rows.Select(r => r.Reference[^3..]).ToArray();
        CollectionAssert.AreEqual(new[] { "112", "111", "110" }, Order(RefundSort.Newest));
        CollectionAssert.AreEqual(new[] { "110", "111", "112" }, Order(RefundSort.Oldest));
        CollectionAssert.AreEqual(new[] { "111", "112", "110" }, Order(RefundSort.Largest));
        CollectionAssert.AreEqual(new[] { "110", "112", "111" }, Order(RefundSort.Smallest));
        CollectionAssert.AreEqual(new[] { "112", "111", "110" }, Order("something else"), "an unknown choice means newest first");
    }

    [TestMethod]
    public void HandledBy_SeparatesStaffTheCustomerDeskAndTheSystem()
    {
        foreach (string r in new[] { "SKY-RFL000120", "SKY-RFL000121", "SKY-RFL000122", "SKY-RFL000123" }) Save(r);
        _store.CancelBooking("SKY-RFL000120", 100m, "agent1");
        _store.CancelBooking("SKY-RFL000121", 100m, "customer");
        _store.CancelBooking("SKY-RFL000122", 100m, "system");
        _store.UpdateStatus("SKY-RFL000123", AppData.StatusCanceled, 100m);   // an older cancellation with no refund record

        string[] For(string who) => _store.LoadRefundList(handledBy: who).Rows.Select(r => r.Reference[^3..]).OrderBy(x => x).ToArray();
        CollectionAssert.AreEqual(new[] { "120", "121", "122", "123" }, For(RefundHandler.Anyone));
        CollectionAssert.AreEqual(new[] { "120" }, For(RefundHandler.Staff));
        CollectionAssert.AreEqual(new[] { "121" }, For(RefundHandler.Customer));
        CollectionAssert.AreEqual(new[] { "122" }, For(RefundHandler.System));
    }

    [TestMethod]
    public void RefundsWithARecordedTime_ComeBeforeOlderOnesWithout_EvenWhenTheirFlightDateIsLater()
    {
        Save("SKY-RFL000130");   // flight in 2030
        Save("SKY-RFL000131");
        _store.UpdateStatus("SKY-RFL000130", AppData.StatusCanceled, 100m);   // legacy: no refund time, flight date 2030
        _store.CancelBooking("SKY-RFL000131", 100m, "agent1");                 // refunded just now

        CollectionAssert.AreEqual(new[] { "SKY-RFL000131", "SKY-RFL000130" }, _store.LoadRefundList().Rows.Select(r => r.Reference).ToArray());
        CollectionAssert.AreEqual(new[] { "SKY-RFL000131", "SKY-RFL000130" }, _store.LoadRefundList(sort: RefundSort.Oldest).Rows.Select(r => r.Reference).ToArray(),
            "the ones without a time stay last in either direction");
    }

    [TestMethod]
    public void Requests_PeriodHidesOldClosedOnesButNeverAnOpenOne()
    {
        Save("SKY-RFL000140");
        Save("SKY-RFL000141");
        Assert.IsTrue(_store.LogRefundRequest("SKY-RFL000140", "a").Ok);
        Assert.IsTrue(_store.LogRefundRequest("SKY-RFL000141", "a").Ok);
        Sql("UPDATE refund_requests SET logged_at = '2020-01-01 10:00:00'");     // both are old now
        long second = _store.LoadRefundRequests(includeClosed: true).Single(r => r.Reference == "SKY-RFL000141").Id;
        Assert.IsTrue(_store.DismissRefundRequest(second, "a", "no"));

        var recent = _store.LoadRefundRequests(includeClosed: true, since: DateTime.Now.AddDays(-7));
        CollectionAssert.AreEqual(new[] { "SKY-RFL000140" }, recent.Select(r => r.Reference).ToArray(), "the open one stays, the old closed one is hidden");
        Assert.AreEqual(2, _store.LoadRefundRequests(includeClosed: true).Count, "any time");
    }

    [TestMethod]
    public void PeriodStart_MapsEachChoiceToItsMoment()
    {
        var now = new DateTime(2026, 11, 21, 22, 53, 10);
        Assert.IsNull(AdminForm.PeriodStart("all", now));
        Assert.AreEqual(new DateTime(2026, 11, 21), AdminForm.PeriodStart("today", now));
        Assert.AreEqual(now.AddDays(-7), AdminForm.PeriodStart("7d", now));
        Assert.AreEqual(now.AddDays(-30), AdminForm.PeriodStart("30d", now));
        Assert.AreEqual(new DateTime(2026, 11, 1), AdminForm.PeriodStart("month", now));
        Assert.IsNull(AdminForm.PeriodStart("nonsense", now));
    }
}