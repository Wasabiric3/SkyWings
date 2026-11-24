using Microsoft.Data.Sqlite;
using SkyWings.Controllers;

namespace SkyWings.Tests.Controllers;

[TestClass]
[DoNotParallelize]
public sealed class ManageBookingControllerTests
{
    private string _dbPath = "";
    private BookingStore _store = null!;
    private ManageBookingController _controller = null!;

    [TestInitialize]
    public void Setup()
    {
        LookupGuard.Reset();
        _dbPath = Path.Combine(Path.GetTempPath(), $"skywings-mng-{Guid.NewGuid():N}.db");
        var csb = new SqliteConnectionStringBuilder { DataSource = _dbPath, ForeignKeys = true, Pooling = false };
        _store = new BookingStore(csb.ConnectionString);
        _store.SeedFlightsIfEmpty(AppData.DomesticFlights, AppData.InternationalFlights,
            AppData.DefaultDomesticCapacity, AppData.DefaultInternationalCapacity);
        _controller = new ManageBookingController(_store);
    }

    [TestCleanup]
    public void Cleanup()
    {
        LookupGuard.Reset();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private BookingRecord Save(string reference, DateTime flightDate, decimal total = 4_000m)
    {
        var passenger = new PassengerRecord("Maria Santos", "1 Test St", "09171234567", 35, "1991-01-01",
            "PSP-0000001", false, false, new SeatAssignment("Maria Santos", "ECO1A"), Array.Empty<string>(), total);
        var booking = new BookingRecord(reference, DateTime.Now, "PR-101", "Manila (MNL)", "Cebu (CEB)", flightDate,
            true, "Economy", "Credit Card", "**** 4521", total, "Scheduled", new[] { passenger },
            AppData.StatusConfirmed, "PAID", "TXN-" + reference, DateTime.Now, 0m);
        _store.SaveConfirmedBooking(booking, false, total, "Credit Card", "**** 4521", booking.PaymentTransactionRef, DateTime.Now);
        return booking;
    }


    [TestMethod]
    public void BuildReceipt_WithNoBookingOpen_ReturnsNull() => Assert.IsNull(_controller.BuildReceipt());

    [TestMethod]
    public void BuildReceipt_ForTheOpenBooking_CarriesItsDetailsAndAFareThatAddsUp()
    {
        BookingRecord saved = Save("SKY-MNG000099", DateTime.Today.AddDays(30), total: 4_000m);
        Assert.IsTrue(_controller.Find("SKY-MNG000099").Found);

        ReceiptData? receipt = _controller.BuildReceipt();

        Assert.IsNotNull(receipt);
        Assert.AreEqual("SKY-MNG000099", receipt.Reference);
        Assert.AreEqual("PR-101", receipt.FlightNo);
        Assert.AreEqual("MNL", receipt.OriginCode);
        Assert.AreEqual("PHP 4,000.00", receipt.TotalPaid);
        decimal sum = receipt.FareLines.Sum(l => l.Discount ? -l.Amount : l.Amount);
        Assert.AreEqual(saved.TotalAmount, sum, 0.05m, "whatever breakdown is shown must equal what was paid");
    }
    [TestMethod]
    public void Find_CorrectDetails_ReturnsBookingAndSetsCurrent()
    {
        Save("SKY-MNG000001", DateTime.Today.AddDays(30));

        LookupResult result = _controller.Find("SKY-MNG000001");

        Assert.IsTrue(result.Found);
        Assert.AreEqual("SKY-MNG000001", _controller.Current!.Reference);
    }

    [TestMethod]
    public void Find_UnknownReference_GivesGenericMessage()
    {
        Save("SKY-MNG000002", DateTime.Today.AddDays(30));

        LookupResult result = _controller.Find("SKY-MNG000009");

        Assert.IsFalse(result.Found);
        Assert.AreEqual("We couldn't find a booking with that reference.", result.Error);
        Assert.IsNull(_controller.Current);
    }

    [TestMethod]
    public void Find_LocksAfterRepeatedFailures()
    {
        for (int i = 0; i < 5; i++) _controller.Find("SKY-NOPE00000");

        LookupResult result = _controller.Find("SKY-NOPE00000");

        StringAssert.StartsWith(result.Error, "Too many attempts");
    }

    [TestMethod]
    public void DescribeBooking_CompletedTrip_SaysWhereAndWhenItLanded()
    {
        BookingRecord booking = Save("SKY-MNG000101", DateTime.Today.AddDays(-3));
        DateTime landed = new(2026, 11, 16, 10, 25, 0);
        var trip = new TripStatusInfo(TripPhase.Completed, "Completed", false, landed, "Cebu (CEB)");

        BookingBanner banner = ManageBookingController.DescribeBooking(booking, trip);

        Assert.AreEqual(BannerKind.Completed, banner.Kind);
        Assert.AreEqual("This trip is completed. It landed in Cebu (CEB) at 10:25 AM on Nov 16.", banner.Text);
        Assert.IsFalse(banner.CanModify);
    }

    [TestMethod]
    public void DescribeBooking_NoShow()
    {
        BookingRecord booking = Save("SKY-MNG000102", DateTime.Today.AddDays(-3));
        var trip = new TripStatusInfo(TripPhase.NoShow, "No-show", false, null, "Cebu (CEB)");

        BookingBanner banner = ManageBookingController.DescribeBooking(booking, trip);

        Assert.AreEqual(BannerKind.NoShow, banner.Kind);
        Assert.AreEqual("No-show: nobody on this booking checked in, so the flight left without them.", banner.Text);
    }

    [TestMethod]
    public void DescribeBooking_WithoutTrip_KeepsOldBehaviour()
    {
        BookingRecord booking = Save("SKY-MNG000103", DateTime.Today.AddDays(-3));
        Assert.AreEqual(BannerKind.Departed, ManageBookingController.DescribeBooking(booking).Kind);
    }

    [TestMethod]
    public void CurrentTrip_UsesTheBookingsCheckIns()
    {
        Assert.IsNull(_controller.CurrentTrip(DateTime.Now), "no booking open");
        DateTime date = DateTime.Today.AddDays(-3);
        Save("SKY-MNG000104", date);
        DateTime departure = AppData.DepartureOf("PR-101", date);
        Assert.IsTrue(_store.CheckInPassenger("SKY-MNG000104", "Maria Santos", "PR-101", date, "ECO1A", departure.AddHours(-2)));
        _controller.Find("SKY-MNG000104");

        Assert.AreEqual(TripPhase.Completed, _controller.CurrentTrip(DateTime.Now)!.Phase);
    }

    [TestMethod]
    public void DescribeBooking_FutureConfirmed_CanModifyAndShowsRefundPolicy()
    {
        BookingRecord booking = Save("SKY-MNG000003", DateTime.Today.AddDays(30));

        BookingBanner banner = ManageBookingController.DescribeBooking(booking);

        Assert.AreEqual(BannerKind.Confirmed, banner.Kind);
        Assert.IsTrue(banner.CanModify);
        StringAssert.Contains(banner.PolicyText, "80%");
    }

    [TestMethod]
    public void PreviewCancel_UsesRefundRuleForTheFlightDate()
    {
        BookingRecord booking = Save("SKY-MNG000004", DateTime.Today.AddDays(30), 4_000m);
        _controller.Find(booking.Reference);

        CancelPreview? preview = _controller.PreviewCancel();

        Assert.IsNotNull(preview);
        Assert.AreEqual(AppData.ComputeRefundAmount(4_000m, booking.FlightDateValue), preview.Refund);
        Assert.AreEqual(AppData.RefundRate(booking.FlightDateValue), preview.Rate);
        StringAssert.Contains(preview.ConfirmationText, booking.Reference);
    }

    [TestMethod]
    public void PreviewCancel_WithoutBooking_IsNull()
    {
        Assert.IsNull(_controller.PreviewCancel());
    }

    [TestMethod]
    public void Cancel_CancelsOnceAndRecordsRefund()
    {
        BookingRecord booking = Save("SKY-MNG000005", DateTime.Today.AddDays(30), 4_000m);
        _controller.Find(booking.Reference);

        CancelResult first = _controller.Cancel();
        BookingRecord? fresh = _controller.Reload();

        Assert.AreEqual(CancelOutcome.Canceled, first.Outcome);
        Assert.AreEqual(AppData.StatusCanceled, fresh!.Status);
        Assert.AreEqual(AppData.ComputeRefundAmount(4_000m, booking.FlightDateValue), fresh.RefundAmount);
        Assert.AreEqual(BannerKind.Canceled, ManageBookingController.DescribeBooking(fresh).Kind);
        Assert.IsNull(_controller.PreviewCancel());
    }

    [TestMethod]
    public void Cancel_RecordsARefundThatTheBannerShows()
    {
        BookingRecord booking = Save("SKY-MNG000050", DateTime.Today.AddDays(30), 4_000m);
        _controller.Find(booking.Reference);
        Assert.IsNull(_store.LoadRefund(booking.Reference), "nothing is refunded before the cancellation");

        Assert.AreEqual(CancelOutcome.Canceled, _controller.Cancel().Outcome);
        BookingRecord fresh = _controller.Reload()!;
        RefundRecord? refund = _controller.CurrentRefund();

        Assert.IsNotNull(refund);
        Assert.AreEqual(fresh.RefundAmount, refund.Amount);
        Assert.AreEqual("Credit Card", refund.Method);
        Assert.AreEqual("**** 4521", refund.AccountMasked);
        StringAssert.Matches(refund.Reference, new System.Text.RegularExpressions.Regex("^RFD-[0-9A-F]{12}$"));
        StringAssert.Contains(refund.Detail, "customer");
        StringAssert.Contains(ManageBookingController.DescribeBooking(fresh, null, refund).Text, refund.Reference);
        Assert.HasCount(1, _store.LoadRefunds(booking.Reference));
    }

    [TestMethod]
    public void Cancel_WithNoRefundDue_WritesNoRefundRecord()
    {
        BookingRecord booking = Save("SKY-MNG000051", DateTime.Today.AddDays(30), 4_000m);
        Assert.IsTrue(_store.CancelBooking(booking.Reference, 0m));
        Assert.IsNull(_store.LoadRefund(booking.Reference));
        Assert.IsFalse(_store.CancelBooking(booking.Reference, 100m), "a second cancel is refused");
        Assert.IsNull(_store.LoadRefund(booking.Reference), "and never writes a second refund");
    }

    [TestMethod]
    public void Cancel_AlreadyCanceledElsewhere_ReportsAlreadyChanged()
    {
        BookingRecord booking = Save("SKY-MNG000006", DateTime.Today.AddDays(30));
        _controller.Find(booking.Reference);
        _store.CancelBooking(booking.Reference, 0m);

        CancelResult result = _controller.Cancel();

        Assert.AreEqual(CancelOutcome.AlreadyChanged, result.Outcome);
    }

    [TestMethod]
    public void RefreshEmail_NoAddress_ReportsNone()
    {
        BookingRecord booking = Save("SKY-MNG000007", DateTime.Today.AddDays(30));
        _controller.Find(booking.Reference);

        EmailBarState state = _controller.RefreshEmail();

        Assert.IsFalse(state.HasEmail);
    }

    [TestMethod]
    public void ToggleEmail_SwitchesSubscriptionAndWritesAudit()
    {
        BookingRecord booking = Save("SKY-MNG000008", DateTime.Today.AddDays(30));
        _store.SaveBookingContact(booking.Reference, "maria@example.com");
        _controller.Find(booking.Reference);

        EmailBarState before = _controller.RefreshEmail();
        Assert.IsTrue(_controller.ToggleEmail());
        EmailBarState after = _controller.RefreshEmail();

        Assert.IsTrue(before.HasEmail);
        Assert.IsTrue(before.Subscribed);
        StringAssert.Contains(before.StatusText, "m***@example.com");
        Assert.IsFalse(after.Subscribed);
        Assert.AreEqual("Resume emails", after.ToggleText);
    }

    [TestMethod]
    public void Reload_DropsBookingThatNoLongerExists()
    {
        BookingRecord booking = Save("SKY-MNG000009", DateTime.Today.AddDays(30));
        _controller.Find(booking.Reference);
        _controller.Clear();

        Assert.IsNull(_controller.Reload());
        Assert.IsNull(_controller.Current);
    }
}
