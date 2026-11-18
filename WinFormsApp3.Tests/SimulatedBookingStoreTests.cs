using Microsoft.Data.Sqlite;

namespace SkyWings.Tests;

/// <summary>Simulated (demo) bookings block seats like real ones but stay out of staff reports unless switched on.</summary>
[TestClass]
public sealed class SimulatedBookingStoreTests
{
    private string _dbPath = "";
    private string _connection = "";
    private BookingStore _store = null!;
    private readonly DateTime _date = DateTime.Today.AddDays(10);

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"skywings-sim-{Guid.NewGuid():N}.db");
        _connection = new SqliteConnectionStringBuilder { DataSource = _dbPath, ForeignKeys = true }.ConnectionString;
        _store = new BookingStore(_connection);
        _store.SeedDefaultFlights();
    }

    [TestCleanup]
    public void Cleanup()
    {
        SqliteConnection.ClearAllPools();
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private BookingRecord Booking(string reference, string seat, string status = AppData.StatusConfirmed, string flightNo = "PR-101", decimal total = 4000m)
    {
        var passenger = new PassengerRecord("Maria Santos", "1 St", "0917", 35, "1991-01-01", "PSP-1", false, false,
            new SeatAssignment("Maria Santos", seat), Array.Empty<string>(), total);
        return new BookingRecord(reference, DateTime.Now, flightNo, "Manila (MNL)", "Cebu (CEB)", _date, true, "Economy",
            "Credit Card", "**** 4521", total, "Scheduled", new[] { passenger }, status, "PAID", "TXN-" + reference, DateTime.Now, 0m);
    }

    private void SaveReal(string reference, string seat)
    {
        BookingRecord b = Booking(reference, seat);
        _store.SaveConfirmedBooking(b, false, b.TotalAmount, "Credit Card", "**** 4521", b.PaymentTransactionRef, DateTime.Now);
    }

    [TestMethod]
    public void ASimulatedBooking_BlocksItsSeatAndCountsAsBooked()
    {
        int before = _store.CabinSeatsLeft("PR-101", _date, "Economy");

        _store.SaveSimulatedBooking(Booking("SIM-000000000001", "ECO5A"));

        Assert.IsTrue(_store.LoadTakenSeats("PR-101", _date).Contains("ECO5A"));
        Assert.AreEqual(before - 1, _store.CabinSeatsLeft("PR-101", _date, "Economy"));
    }

    [TestMethod]
    public void SimulatedBookings_StayOutOfReports_ByDefault()
    {
        SaveReal("SKY-REAL00000001", "ECO1A");
        _store.SaveSimulatedBooking(Booking("SIM-000000000002", "ECO5B"));
        _store.SaveSimulatedBooking(Booking("SIM-000000000003", "ECO5C", AppData.StatusCanceled));

        ReportStats stats = _store.LoadReportStats()!;
        Assert.AreEqual(1, stats.TotalBookings);
        Assert.AreEqual(1, stats.ConfirmedCount);
        Assert.AreEqual(4000m, stats.TotalRevenue);
        Assert.HasCount(1, _store.LoadBookings());
        Assert.AreEqual(1L, stats.TopRoutes.Sum(r => r.Count));
        Assert.AreEqual(1L, stats.ByClass.Sum(c => c.Count));
        Assert.IsEmpty(_store.LoadCancelledBookings());
    }

    [TestMethod]
    public void TurningTheSettingOn_IncludesThemInReports()
    {
        SaveReal("SKY-REAL00000002", "ECO1A");
        _store.SaveSimulatedBooking(Booking("SIM-000000000004", "ECO5D"));

        _store.SetSetting(BookingStore.SettingDemoInReports, "1");

        Assert.IsTrue(_store.IncludeSimulatedInReports);
        Assert.AreEqual(2, _store.LoadReportStats()!.TotalBookings);
        Assert.HasCount(2, _store.LoadBookings());
    }

    [TestMethod]
    public void CountActiveBookingsForFlight_CountsRealBookingsOnly()
    {
        SaveReal("SKY-REAL00000003", "ECO1A");
        _store.SaveSimulatedBooking(Booking("SIM-000000000005", "ECO5E"));
        Assert.AreEqual(1, _store.CountActiveBookingsForFlight("PR-101"), "demo bookings must not block editing a flight");
    }

    [TestMethod]
    public void LoadBookedSeatCodes_CanExcludeSimulatedSeats()
    {
        SaveReal("SKY-REAL00000004", "ECO1A");
        _store.SaveSimulatedBooking(Booking("SIM-000000000006", "ECO5F"));

        CollectionAssert.AreEquivalent(new[] { "ECO1A", "ECO5F" }, _store.LoadBookedSeatCodes("PR-101").ToArray());
        CollectionAssert.AreEquivalent(new[] { "ECO1A" }, _store.LoadBookedSeatCodes("PR-101", realOnly: true).ToArray());
    }

    [TestMethod]
    public void OnlyReferencesStartingWithSIM_CanBeSavedAsSimulated()
        => Assert.ThrowsExactly<ArgumentException>(() => _store.SaveSimulatedBooking(Booking("SKY-REAL00000005", "ECO1B")));

    [TestMethod]
    public void DeleteSimulatedBookings_RemovesOnlyDemoRows_AndTheirPassengers()
    {
        SaveReal("SKY-REAL00000006", "ECO1A");
        _store.SaveSimulatedBooking(Booking("SIM-000000000007", "ECO6A"));
        _store.SaveSimulatedBooking(Booking("SIM-000000000008", "ECO6B"));

        int removed = _store.DeleteSimulatedBookings();

        Assert.AreEqual(2, removed);
        Assert.IsNotNull(_store.FindByReference("SKY-REAL00000006"));
        Assert.IsNull(_store.FindByReference("SIM-000000000007"));
        CollectionAssert.AreEquivalent(new[] { "ECO1A" }, _store.LoadTakenSeats("PR-101", _date).ToArray());
    }

    [TestMethod]
    public void DeleteSimulatedBookings_ForOneFlight_LeavesOtherFlightsAlone()
    {
        _store.SaveSimulatedBooking(Booking("SIM-000000000009", "ECO6A", flightNo: "PR-101"));
        _store.SaveSimulatedBooking(Booking("SIM-00000000000A", "ECO6A", flightNo: "PR-102"));

        Assert.AreEqual(1, _store.DeleteSimulatedBookings("PR-101"));
        Assert.IsNotNull(_store.FindByReference("SIM-00000000000A"));
    }

    [TestMethod]
    public void ASimulatedBooking_CanBeCancelled_AndFreesItsSeat()
    {
        _store.SaveSimulatedBooking(Booking("SIM-00000000000B", "ECO7A"));

        Assert.IsTrue(_store.CancelBooking("SIM-00000000000B", 0m));

        Assert.IsFalse(_store.LoadTakenSeats("PR-101", _date).Contains("ECO7A"));
    }

    [TestMethod]
    public void Settings_DefaultOff_AndRoundTrip()
    {
        Assert.IsFalse(_store.DemoActivityEnabled);
        Assert.IsFalse(_store.IncludeSimulatedInReports);
        Assert.AreEqual("x", _store.GetSetting("nothing.here", "x"));

        _store.SetSetting(BookingStore.SettingDemoActivity, "1");
        Assert.IsTrue(_store.DemoActivityEnabled);
        _store.SetSetting(BookingStore.SettingDemoActivity, "0");
        Assert.IsFalse(_store.DemoActivityEnabled);
    }

    [TestMethod]
    public void AnOldBookingsTable_IsUpgraded_WithExistingBookingsCountedAsReal()
    {
        string path = Path.Combine(Path.GetTempPath(), $"skywings-old-{Guid.NewGuid():N}.db");
        string cs = new SqliteConnectionStringBuilder { DataSource = path, ForeignKeys = true }.ConnectionString;
        try
        {
            var first = new BookingStore(cs);
            first.SeedDefaultFlights();
            using (var con = new SqliteConnection(cs))
            {
                con.Open();
                using var cmd = con.CreateCommand();
                cmd.CommandText = "ALTER TABLE bookings DROP COLUMN is_simulated";   // an install from before the column existed
                cmd.ExecuteNonQuery();
            }
            SqliteConnection.ClearAllPools();

            var upgraded = new BookingStore(cs);   // opening upgrades the schema

            BookingRecord b = Booking("SKY-REAL00000007", "ECO1A");
            upgraded.SaveConfirmedBooking(b, false, b.TotalAmount, "Credit Card", "**** 4521", b.PaymentTransactionRef, DateTime.Now);
            Assert.AreEqual(1, upgraded.LoadReportStats()!.TotalBookings);
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
