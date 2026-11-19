using SkyWings;
using Microsoft.Data.Sqlite;

namespace SkyWings.Tests;

/// <summary>Data-layer tests. Each test runs against its own temporary SQLite file.</summary>
[TestClass]
public sealed class BookingStoreTests
{
    private string _dbPath = "";
    private BookingStore _store = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"skywings-test-{Guid.NewGuid():N}.db");
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

    private static BookingRecord MakeBooking(string reference, decimal total, DateTime flightDate, string promo = "",
        string seat = "ECO-01A")
    {
        var passenger = new PassengerRecord("Maria Santos", "1 Test St", "09171234567", 35, "1991-01-01",
            "PSP-0000001", false, false, new SeatAssignment("Maria Santos", seat),
            Array.Empty<string>(), total);
        return new BookingRecord(reference, DateTime.Now, "PR-101", "Manila (MNL)", "Cebu (CEB)", flightDate,
            true, "Economy", "Credit Card", "**** 4521", total, "Scheduled", new[] { passenger },
            AppData.StatusConfirmed, "PAID", "TXN-TEST0001", DateTime.Now, 0m)
        {
            PromoCode = promo
        };
    }

    [TestMethod]
    public void SeededFlights_ReportFullCapacity()
    {
        FlightInventory inventory = _store.LoadFlightInventory("PR-101");

        Assert.AreEqual(AppData.DefaultDomesticCapacity, inventory.Capacity);
        Assert.AreEqual(AppData.DefaultDomesticCapacity, inventory.AvailableSeats);
    }

    [TestMethod]
    public void SeatHold_SecondHolderIsRejectedForSameSeat()
    {
        DateTime flightDate = DateTime.Today.AddDays(30);

        bool first = _store.AddSeatHold("PR-101", flightDate, "ECO-01A", "owner-1");
        bool second = _store.AddSeatHold("PR-101", flightDate, "ECO-01A", "owner-2");

        Assert.IsTrue(first);
        Assert.IsFalse(second);
    }

    [TestMethod]
    public void SaveConfirmedBooking_PersistsDecimalAmountsAndPromoRedemption()
    {
        DateTime flightDate = DateTime.Today.AddDays(30);
        var booking = MakeBooking("SKY-TEST0001", 6_160.55m, flightDate, promo: "SKY10");

        _store.SaveConfirmedBooking(booking, promoRedeemed: true, paymentAmount: 6_160.55m,
            paymentMethod: "Credit Card", maskedAccount: "**** 4521", transactionRef: "TXN-TEST0001", paidAt: DateTime.Now);

        BookingRecord? loaded = _store.FindByReference("SKY-TEST0001");
        Assert.IsNotNull(loaded);
        Assert.AreEqual(6_160.55m, loaded.TotalAmount);
        Assert.AreEqual("PAID", loaded.PaymentStatus);
        Assert.HasCount(1, loaded.Passengers);
        Assert.AreEqual("ECO-01A", loaded.Passengers[0].SeatCode);
        Assert.AreEqual(1, CountRows("promo_redemptions", "booking_ref = 'SKY-TEST0001'"));
        Assert.AreEqual(1, CountRows("booking_payments", "booking_ref = 'SKY-TEST0001'"));
    }

    [TestMethod]
    public void SaveConfirmedBooking_WithoutPromo_DoesNotRecordRedemption()
    {
        var booking = MakeBooking("SKY-TEST0002", 3_500m, DateTime.Today.AddDays(30));

        _store.SaveConfirmedBooking(booking, promoRedeemed: false, paymentAmount: 3_500m,
            paymentMethod: "Credit Card", maskedAccount: "**** 4521", transactionRef: "TXN-TEST0002", paidAt: DateTime.Now);

        Assert.AreEqual(0, CountRows("promo_redemptions", "booking_ref = 'SKY-TEST0002'"));
    }

    [TestMethod]
    public void ValidatePromoCode_Sky10ReturnsTenPercent()
    {
        bool valid = _store.ValidatePromoCode("sky10", DateTime.Today.AddDays(30), 1_000m, out decimal discount);

        Assert.IsTrue(valid);
        Assert.AreEqual(100m, discount);
    }

    [TestMethod]
    public void CancelBooking_StoresRefundAmount()
    {
        var booking = MakeBooking("SKY-TEST0003", 10_000m, DateTime.Today.AddDays(30));
        _store.SaveConfirmedBooking(booking, promoRedeemed: false, paymentAmount: 10_000m,
            paymentMethod: "Credit Card", maskedAccount: "**** 4521", transactionRef: "TXN-TEST0003", paidAt: DateTime.Now);

        BookingRecord? canceled = _store.UpdateStatus("SKY-TEST0003", AppData.StatusCanceled, 8_000m);

        Assert.IsNotNull(canceled);
        Assert.AreEqual(AppData.StatusCanceled, canceled.Status);
        Assert.AreEqual(8_000m, canceled.RefundAmount);
    }

    [TestMethod]
    public void LoadTakenSeats_ReturnsNormalizedSeatsFromConfirmedBookingsOnly()
    {
        DateTime flightDate = DateTime.Today.AddDays(30);
        _store.SaveConfirmedBooking(MakeBooking("SKY-TAKEN001", 3_500m, flightDate), false, 3_500m,
            "Credit Card", "**** 4521", "TXN-1", DateTime.Now);
        var canceled = MakeBooking("SKY-TAKEN002", 3_500m, flightDate);
        _store.SaveConfirmedBooking(canceled, false, 3_500m, "Credit Card", "**** 4521", "TXN-2", DateTime.Now);
        _store.UpdateStatus("SKY-TAKEN002", AppData.StatusCanceled, 0m);

        HashSet<string> taken = _store.LoadTakenSeats("PR-101", flightDate);

        // Both bookings used seat ECO-01A; the canceled one must not matter, and the seed format is normalized.
        Assert.Contains("ECO1A", taken);
        Assert.HasCount(1, taken);
        Assert.IsEmpty(_store.LoadTakenSeats("PR-101", flightDate.AddDays(1)));
    }

    [TestMethod]
    public void AddSeatHold_IsRejectedForSeatAlreadyBooked()
    {
        DateTime flightDate = DateTime.Today.AddDays(30);
        _store.SaveConfirmedBooking(MakeBooking("SKY-TAKEN003", 3_500m, flightDate), false, 3_500m,
            "Credit Card", "**** 4521", "TXN-3", DateTime.Now);

        Assert.IsFalse(_store.AddSeatHold("PR-101", flightDate, "ECO1A", "someone-else"));
        Assert.IsTrue(_store.AddSeatHold("PR-101", flightDate, "ECO1B", "someone-else"));
    }

    [TestMethod]
    public void SeedFlights_IncludesLongHaulRoutesAndTheirReturnLegs()
    {
        var international = _store.LoadActiveFlights(false).Select(f => f.FlightNo).ToList();

        CollectionAssert.IsSubsetOf(new[] { "PR-301", "PR-401", "PR-701", "PR-801" }, international);
        Assert.AreEqual(AppData.DefaultNorthAmericaCapacity, _store.LoadFlightInventory("PR-401").Capacity);
    }

    [TestMethod]
    public void SetFlightActive_TogglesAvailabilityForNewBookings()
    {
        _store.SetFlightActive("PR-101", false);
        Assert.IsFalse(_store.LoadActiveFlights(true).Any(f => f.FlightNo == "PR-101"));

        _store.SetFlightActive("PR-101", true);
        Assert.IsTrue(_store.LoadActiveFlights(true).Any(f => f.FlightNo == "PR-101"));
    }

    private BookingRecord SaveRoundTrip(string reference, DateTime outDate, DateTime backDate, string seat, string backSeat)
    {
        var passenger = new PassengerRecord("Maria Santos", "1 Test St", "09171234567", 35, "1991-01-01",
            "PSP-0000001", false, false, new SeatAssignment("Maria Santos", seat),
            Array.Empty<string>(), 7_000m, backSeat);
        var booking = new BookingRecord(reference, DateTime.Now, "PR-101", "Manila (MNL)", "Cebu (CEB)", outDate,
            true, "Economy", "Credit Card", "**** 4521", 7_000m, "Scheduled", new[] { passenger },
            AppData.StatusConfirmed, "PAID", "TXN-RT", DateTime.Now, 0m)
        {
            ReturnFlightNo = "PR-501", ReturnOrigin = "Cebu (CEB)", ReturnDestination = "Manila (MNL)",
            ReturnFlightDateValue = backDate, BaggageOption = "Extra baggage (+PHP 1,500)", MealOption = "Standard meal (+PHP 500)"
        };
        _store.SaveConfirmedBooking(booking, false, 7_000m, "Credit Card", "**** 4521", "TXN-RT", DateTime.Now);
        return booking;
    }

    [TestMethod]
    public void LoadBookingsForDate_ReturnsOnlyThatDate_WithPassengersAndReturnLegs()
    {
        DateTime day = DateTime.Today.AddDays(20);
        _store.SaveConfirmedBooking(MakeBooking("SKY-DATE00000001", 4_000m, day, seat: "ECO7A"), false, 4_000m, "Credit Card", "**** 4521", "TXN-D1", DateTime.Now);
        SaveRoundTrip("SKY-DATE00000002", day, day.AddDays(4), "ECO7B", "ECO8B");
        _store.SaveConfirmedBooking(MakeBooking("SKY-DATE00000003", 4_000m, day.AddDays(1), seat: "ECO7C"), false, 4_000m, "Credit Card", "**** 4521", "TXN-D3", DateTime.Now);

        List<BookingRecord> list = _store.LoadBookingsForDate(day);

        CollectionAssert.AreEquivalent(new[] { "SKY-DATE00000001", "SKY-DATE00000002" }, list.Select(b => b.Reference).ToArray());
        BookingRecord roundTrip = list.Single(b => b.Reference == "SKY-DATE00000002");
        Assert.AreEqual("PR-501", roundTrip.ReturnFlightNo);
        Assert.AreEqual(day.AddDays(4), roundTrip.ReturnFlightDateValue);
        Assert.AreEqual("Extra baggage (+PHP 1,500)", roundTrip.BaggageOption);
        Assert.HasCount(1, roundTrip.Passengers);
        Assert.AreEqual("ECO8B", roundTrip.Passengers[0].ReturnSeatCode);
        Assert.AreEqual("ECO7A", list.Single(b => b.Reference == "SKY-DATE00000001").Passengers[0].SeatCode);
    }

    [TestMethod]
    public void LoadBookingsForDate_EmptyDate_ReturnsEmpty()
        => Assert.IsEmpty(_store.LoadBookingsForDate(DateTime.Today.AddDays(200)));

    [TestMethod]
    public void LoadCheckInsForDate_ReturnsCheckInsOfThatDatesBookings()
    {
        DateTime day = DateTime.Today.AddDays(1);
        _store.SaveConfirmedBooking(MakeBooking("SKY-DATE00000004", 4_000m, day, seat: "ECO7D"), false, 4_000m, "Credit Card", "**** 4521", "TXN-D4", DateTime.Now);
        _store.SaveConfirmedBooking(MakeBooking("SKY-DATE00000005", 4_000m, day.AddDays(1), seat: "ECO7E"), false, 4_000m, "Credit Card", "**** 4521", "TXN-D5", DateTime.Now);
        DateTime departure = AppData.DepartureOf("PR-101", day);
        Assert.IsTrue(_store.CheckInPassenger("SKY-DATE00000004", "Maria Santos", "PR-101", day, "ECO7D", departure.AddHours(-2)));
        Assert.IsTrue(_store.CheckInPassenger("SKY-DATE00000005", "Maria Santos", "PR-101", day.AddDays(1), "ECO7E", departure.AddDays(1).AddHours(-2)));

        Dictionary<string, List<CheckInRecord>> checkIns = _store.LoadCheckInsForDate(day);

        CollectionAssert.AreEqual(new[] { "SKY-DATE00000004" }, checkIns.Keys.ToArray());
    }

    [TestMethod]
    [DoNotParallelize]   // wall-clock bound, measured alone
    public void LoadBookingsForDate_FullSimulatedDay_IsQuick()
    {
        DateTime now = DateTime.Today.AddHours(6);
        new SimulationService(_store).EnsureLoad(now, horizonDays: 1);

        var clock = System.Diagnostics.Stopwatch.StartNew();
        List<BookingRecord> day = _store.LoadBookingsForDate(now.Date);
        clock.Stop();

        Assert.IsGreaterThan(5000, day.Count, "a full simulated day");
        Assert.IsLessThan(1500L, clock.ElapsedMilliseconds, $"{clock.ElapsedMilliseconds} ms for {day.Count} bookings");
    }

    [TestMethod]
    public void LoadedBooking_KeepsReturnLegAndExtras()
    {
        SaveRoundTrip("SKY-RT000001", DateTime.Today.AddDays(30), DateTime.Today.AddDays(35), "ECO5A", "ECO6A");

        BookingRecord? loaded = _store.FindByReference("SKY-RT000001");

        Assert.IsNotNull(loaded);
        Assert.AreEqual("PR-501", loaded.ReturnFlightNo);
        Assert.AreEqual(DateTime.Today.AddDays(35), loaded.ReturnFlightDateValue);
        Assert.AreEqual("Extra baggage (+PHP 1,500)", loaded.BaggageOption);
        Assert.AreEqual("ECO6A", loaded.Passengers[0].ReturnSeatCode);
    }

    [TestMethod]
    public void FindForCustomer_FindsByReferenceAlone()
    {
        SaveRoundTrip("SKY-FIND0001", DateTime.Today.AddDays(30), DateTime.Today.AddDays(35), "ECO5A", "ECO6A");

        Assert.IsNotNull(_store.FindForCustomer("sky-find0001"));
        Assert.IsNotNull(_store.FindForCustomer("  SKY-FIND0001 "));
        Assert.IsNull(_store.FindForCustomer("SKY-NOPE0000"));
        Assert.IsNull(_store.FindForCustomer(""));
    }

    [TestMethod]
    public void CancelBooking_WorksOnceAndMarksPaymentRefunded()
    {
        SaveRoundTrip("SKY-CNCL0001", DateTime.Today.AddDays(30), DateTime.Today.AddDays(35), "ECO5A", "ECO6A");

        Assert.IsTrue(_store.CancelBooking("SKY-CNCL0001", 5_600m));
        Assert.IsFalse(_store.CancelBooking("SKY-CNCL0001", 5_600m));   // second attempt must not record another refund

        BookingRecord? loaded = _store.FindByReference("SKY-CNCL0001");
        Assert.AreEqual(AppData.StatusCanceled, loaded!.Status);
        Assert.AreEqual("REFUNDED", loaded.PaymentStatus);
        Assert.AreEqual(5_600m, loaded.RefundAmount);
        // Canceling frees the seats again.
        Assert.IsEmpty(_store.LoadTakenSeats("PR-101", DateTime.Today.AddDays(30)));
    }

    [TestMethod]
    public void ChangeSeat_MovesPassengerOnlyToFreeSeatsInTheirCabin()
    {
        DateTime outDate = DateTime.Today.AddDays(30), backDate = DateTime.Today.AddDays(35);
        SaveRoundTrip("SKY-SEAT0001", outDate, backDate, "ECO5A", "ECO6A");
        // Another booking occupies ECO7C on the outbound flight.
        _store.SaveConfirmedBooking(MakeBooking("SKY-SEAT0002", 3_500m, outDate, seat: "ECO7C"), false, 3_500m,
            "Credit Card", "**** 4521", "TXN-S2", DateTime.Now);

        Assert.IsTrue(_store.ChangeSeat("SKY-SEAT0001", "Maria Santos", false, "ECO8B"));
        Assert.IsTrue(_store.ChangeSeat("SKY-SEAT0001", "Maria Santos", true, "ECO9D"));
        Assert.IsFalse(_store.ChangeSeat("SKY-SEAT0001", "Maria Santos", false, "ECO7C"));  // taken by someone else
        Assert.IsFalse(_store.ChangeSeat("SKY-SEAT0001", "Maria Santos", false, "BIZ1A"));  // wrong cabin
        Assert.IsFalse(_store.ChangeSeat("SKY-SEAT0001", "Nobody Else", false, "ECO10A"));  // not on the booking

        BookingRecord loaded = _store.FindByReference("SKY-SEAT0001")!;
        Assert.AreEqual("ECO8B", loaded.Passengers[0].SeatCode);
        Assert.AreEqual("ECO9D", loaded.Passengers[0].ReturnSeatCode);
        // The old outbound seat is free again.
        Assert.DoesNotContain("ECO5A", _store.LoadTakenSeats("PR-101", outDate));
    }

    [TestMethod]
    public void ChangeSeat_RefusesSeatHeldBySomeoneElse()
    {
        DateTime outDate = DateTime.Today.AddDays(30);
        SaveRoundTrip("SKY-SEAT0003", outDate, DateTime.Today.AddDays(35), "ECO5A", "ECO6A");
        _store.AddSeatHold("PR-101", outDate, "ECO11A", "another-session");

        Assert.IsFalse(_store.ChangeSeat("SKY-SEAT0003", "Maria Santos", false, "ECO11A"));
    }

    [TestMethod]
    public void CheckIn_IsAllowedOnlyInsideTheTwentyFourHourWindow()
    {
        DateTime flightDate = DateTime.Today.AddDays(30);
        SaveRoundTrip("SKY-CHK00001", flightDate, flightDate.AddDays(5), "ECO5A", "ECO6A");
        DateTime departure = AppData.DepartureOf("PR-101", flightDate);

        Assert.AreEqual(CheckInOutcome.NotOpen, _store.CheckIn("SKY-CHK00001", "Maria Santos", false, departure.AddHours(-25)));
        Assert.AreEqual(CheckInOutcome.CheckedIn, _store.CheckIn("SKY-CHK00001", "Maria Santos", false, departure.AddHours(-23)));
        Assert.AreEqual(CheckInOutcome.AlreadyCheckedIn, _store.CheckIn("SKY-CHK00001", "Maria Santos", false, departure.AddHours(-2)));
        Assert.AreEqual(CheckInOutcome.NotOpen, _store.CheckIn("SKY-CHK00001", "Maria Santos", false, departure.AddMinutes(1)));
    }

    [TestMethod]
    public void CheckIn_UsesTheSeatAndFlightOfTheChosenLeg()
    {
        DateTime flightDate = DateTime.Today.AddDays(30), backDate = flightDate.AddDays(5);
        SaveRoundTrip("SKY-CHK00002", flightDate, backDate, "ECO5A", "ECO6A");
        DateTime backDeparture = AppData.DepartureOf("PR-501", backDate);

        Assert.AreEqual(CheckInOutcome.CheckedIn, _store.CheckIn("SKY-CHK00002", "Maria Santos", true, backDeparture.AddHours(-3)));

        CheckInRecord record = _store.LoadCheckIns("SKY-CHK00002").Single();
        Assert.AreEqual("PR-501", record.FlightNo);
        Assert.AreEqual("ECO6A", record.SeatCode);
        Assert.IsTrue(_store.IsCheckedIn("SKY-CHK00002", "Maria Santos", "PR-501", backDate));
        Assert.IsFalse(_store.IsCheckedIn("SKY-CHK00002", "Maria Santos", "PR-101", flightDate));
    }

    [TestMethod]
    public void CheckIn_RejectsUnknownTravellersAndCanceledBookings()
    {
        DateTime flightDate = DateTime.Today.AddDays(30);
        SaveRoundTrip("SKY-CHK00003", flightDate, flightDate.AddDays(5), "ECO5A", "ECO6A");
        DateTime inWindow = AppData.DepartureOf("PR-101", flightDate).AddHours(-3);

        Assert.AreEqual(CheckInOutcome.NotFound, _store.CheckIn("SKY-CHK00003", "Someone Else", false, inWindow));
        Assert.AreEqual(CheckInOutcome.NotFound, _store.CheckIn("SKY-NOPE0000", "Maria Santos", false, inWindow));

        _store.CancelBooking("SKY-CHK00003", 0m);
        Assert.AreEqual(CheckInOutcome.NotOpen, _store.CheckIn("SKY-CHK00003", "Maria Santos", false, inWindow));
    }

    [TestMethod]
    public void ChangeSeat_IsRefusedAfterCheckIn()
    {
        DateTime flightDate = DateTime.Today.AddDays(30);
        SaveRoundTrip("SKY-CHK00004", flightDate, flightDate.AddDays(5), "ECO5A", "ECO6A");
        _store.CheckIn("SKY-CHK00004", "Maria Santos", false, AppData.DepartureOf("PR-101", flightDate).AddHours(-3));

        Assert.IsFalse(_store.ChangeSeat("SKY-CHK00004", "Maria Santos", false, "ECO12A"));
        Assert.IsTrue(_store.ChangeSeat("SKY-CHK00004", "Maria Santos", true, "ECO12A"));   // return leg not checked in yet
    }

    [TestMethod]
    public void CabinSeatsLeft_IsPerDateAndPerCabin_AndCountsBookedAndHeldSeats()
    {
        DateTime day = DateTime.Today.AddDays(30), otherDay = day.AddDays(1);
        int economyTotal = SeatLayout.RowCount("Economy", AppData.DefaultDomesticCapacity) * 6;
        int businessTotal = SeatLayout.RowCount("Business", AppData.DefaultDomesticCapacity) * 3;
        Assert.AreEqual(economyTotal, _store.CabinSeatsLeft("PR-101", day, "Economy"));

        _store.SaveConfirmedBooking(MakeBooking("SKY-LEFT00001", 3_500m, day, seat: "ECO5A"), false, 3_500m,
            "Credit Card", "**** 1111", "TXN-L1", DateTime.Now);
        _store.AddSeatHold("PR-101", day, "ECO6A", "someone-else");

        Assert.AreEqual(economyTotal - 2, _store.CabinSeatsLeft("PR-101", day, "Economy"), "One booked and one held seat.");
        Assert.AreEqual(economyTotal, _store.CabinSeatsLeft("PR-101", otherDay, "Economy"), "Another date is unaffected.");
        Assert.AreEqual(businessTotal, _store.CabinSeatsLeft("PR-101", day, "Business"), "Another cabin is unaffected.");

        _store.CancelBooking("SKY-LEFT00001", 0m);
        Assert.AreEqual(economyTotal - 1, _store.CabinSeatsLeft("PR-101", day, "Economy"), "A canceled booking frees its seat.");
    }

    private int CountRows(string table, string where)
    {
        using var con = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _dbPath }.ConnectionString);
        con.Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {table} WHERE {where}";
        return Convert.ToInt32(cmd.ExecuteScalar());
    }
}

[TestClass]
public sealed class PasswordAndAgeTests
{
    [TestMethod]
    public void PasswordHasher_VerifiesCorrectPasswordOnly()
    {
        string hash = PasswordHasher.Hash("correct horse");

        Assert.IsTrue(PasswordHasher.Verify("correct horse", hash));
        Assert.IsFalse(PasswordHasher.Verify("wrong", hash));
        Assert.IsFalse(PasswordHasher.Verify("correct horse", "not-a-hash"));
    }

    [TestMethod]
    public void PasswordHasher_UsesRandomSalt()
    {
        Assert.AreNotEqual(PasswordHasher.Hash("same"), PasswordHasher.Hash("same"));
    }

    [TestMethod]
    public void AgeOn_HandlesBirthdayNotYetReached()
    {
        Assert.AreEqual(59, AppData.AgeOn(new DateTime(1960, 6, 15), new DateTime(2020, 6, 14)));
        Assert.AreEqual(60, AppData.AgeOn(new DateTime(1960, 6, 15), new DateTime(2020, 6, 15)));
    }
}
