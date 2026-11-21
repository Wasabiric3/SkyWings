using Microsoft.Data.Sqlite;
using SkyWings.Controllers;

namespace SkyWings.Tests;

/// <summary>The staff Bookings tab gets every booking's live trip status in one go.</summary>
[TestClass]
[DoNotParallelize]
public sealed class TripStatusAdminTests
{
    private const string GoodPassword = "a long enough password 1!";
    private string _dbPath = "";
    private BookingStore _store = null!;
    private AdminService _admin = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"skywings-tripadmin-{Guid.NewGuid():N}.db");
        _store = new BookingStore(new SqliteConnectionStringBuilder { DataSource = _dbPath, ForeignKeys = true }.ConnectionString);
        _store.SeedDefaultFlights();
        StaffAccount staff = _store.CreateStaff("boss", "boss", GoodPassword, StaffRole.Admin, false);
        _admin = new AdminService(_store, new StaffSession(staff.Id, staff.Username, staff.DisplayName, staff.Role, false));
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private BookingRecord SaveReal(string reference, DateTime date, string seat)
    {
        var p = new PassengerRecord("Real Person", "1 St", "0917", 35, "1991-01-01", "PSP-1", false, false,
            new SeatAssignment("Real Person", seat), Array.Empty<string>(), 4000m);
        var b = new BookingRecord(reference, date.AddDays(-20), "PR-101", "Manila (MNL)", "Cebu (CEB)", date, true, "Economy",
            "Credit Card", "**** 4521", 4000m, "Scheduled", new[] { p }, AppData.StatusConfirmed, "PAID", "TXN-" + reference, date.AddDays(-20), 0m);
        _store.SaveConfirmedBooking(b, false, b.TotalAmount, "Credit Card", "**** 4521", b.PaymentTransactionRef, date.AddDays(-20));
        return b;
    }

    [TestMethod]
    public void AsReference_AcceptsLowercaseAndSpaces_RejectsOtherText()
    {
        Assert.AreEqual("SKY-3CAD9C0FFEE1", AdminService.AsReference(" sky-3cad9c0ffee1  "));
        Assert.IsNull(AdminService.AsReference("PR-101"));
        Assert.IsNull(AdminService.AsReference("Maria"));
        Assert.IsNull(AdminService.AsReference("SKY-3CAD9"));
    }

    [TestMethod]
    public void LoadTripStatusesForDate_MatchesLoadTripStatuses()
    {
        DateTime lastWeek = DateTime.Today.AddDays(-7);
        SaveReal("SKY-TRIPADMIN011", lastWeek, "ECO3A");
        SaveReal("SKY-TRIPADMIN012", lastWeek, "ECO3B");
        Assert.IsTrue(_store.CheckInPassenger("SKY-TRIPADMIN011", "Real Person", "PR-101", lastWeek, "ECO3A",
            AppData.DepartureOf("PR-101", lastWeek).AddHours(-3)));
        DateTime now = DateTime.Now;

        List<BookingRecord> day = _admin.LoadBookingsForDate(lastWeek);
        Dictionary<string, TripStatusInfo> byDate = _admin.LoadTripStatusesForDate(day, lastWeek, now);
        Dictionary<string, TripStatusInfo> all = _admin.LoadTripStatuses(day, now);

        Assert.HasCount(2, day);
        foreach (BookingRecord b in day) Assert.AreEqual(all[b.Reference], byDate[b.Reference], b.Reference);
        Assert.AreEqual(TripPhase.Completed, byDate["SKY-TRIPADMIN011"].Phase);
        Assert.AreEqual(TripPhase.NoShow, byDate["SKY-TRIPADMIN012"].Phase);
    }

    [TestMethod]
    public void LoadTripStatus_ForOneBooking_ReadsItsOwnCheckIns()
    {
        DateTime lastWeek = DateTime.Today.AddDays(-7);
        BookingRecord booking = SaveReal("SKY-TRIPADMIN010", lastWeek, "ECO2B");
        Assert.IsTrue(_store.CheckInPassenger("SKY-TRIPADMIN010", "Real Person", "PR-101", lastWeek, "ECO2B",
            AppData.DepartureOf("PR-101", lastWeek).AddHours(-3)));

        Assert.AreEqual(TripPhase.Completed, _admin.LoadTripStatus(booking, DateTime.Now).Phase);
    }

    [TestMethod]
    public void CancelRefusal_RecognisesAnAlreadyCanceledBooking()
    {
        BookingRecord booking = SaveReal("SKY-TRIPADMIN009", DateTime.Today.AddDays(10), "ECO2A");
        Assert.IsNull(_admin.CancelRefusal(booking), "a confirmed booking can be cancelled");

        _store.CancelBooking(booking.Reference, 0m);

        Assert.AreEqual("This booking is already canceled.", _admin.CancelRefusal(_admin.FindBooking(booking.Reference)!));
    }

    [TestMethod]
    public void EveryTripLabel_IsDrawnAsAPill_IncludingReturnLegs()
    {
        foreach (string label in new[] { "Upcoming", "Check-in open", "Checked in", "Boarding", "In flight", "Completed", "No-show",
                     "Canceled", "Return · Upcoming", "Return · In flight" })
            Assert.IsNotNull(SkyWings.UI.GridStyle.StatusStyle(label), label);
        Assert.AreEqual(SkyWings.UI.GridStyle.StatusStyle("CONFIRMED"), SkyWings.UI.GridStyle.StatusStyle("Completed"), "completed reads as good");
        Assert.AreEqual(SkyWings.UI.GridStyle.StatusStyle("In flight"), SkyWings.UI.GridStyle.StatusStyle("Return · In flight"));
    }

    [TestMethod]
    public void LoadTripStatuses_GivesCompletedForLandedAndUpcomingForFuture()
    {
        DateTime lastWeek = DateTime.Today.AddDays(-7), nextWeek = DateTime.Today.AddDays(7);
        SaveReal("SKY-TRIPADMIN001", lastWeek, "ECO1A");
        SaveReal("SKY-TRIPADMIN002", lastWeek, "ECO1B");   // nobody checked in: a no-show
        SaveReal("SKY-TRIPADMIN003", nextWeek, "ECO1C");
        DateTime departure = AppData.DepartureOf("PR-101", lastWeek);
        Assert.IsTrue(_store.CheckInPassenger("SKY-TRIPADMIN001", "Real Person", "PR-101", lastWeek, "ECO1A", departure.AddHours(-3)));
        List<BookingRecord> bookings = _admin.LoadBookings();

        Dictionary<string, TripStatusInfo> trips = _admin.LoadTripStatuses(bookings, DateTime.Now);

        Assert.AreEqual(TripPhase.Completed, trips["SKY-TRIPADMIN001"].Phase);
        Assert.AreEqual(TripPhase.NoShow, trips["SKY-TRIPADMIN002"].Phase);
        Assert.AreEqual(TripPhase.Upcoming, trips["SKY-TRIPADMIN003"].Phase);
        Assert.HasCount(bookings.Count, trips);
    }
}
