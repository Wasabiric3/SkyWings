using Microsoft.Data.Sqlite;

namespace SkyWings.Tests;

/// <summary>Waitlist with time-limited offers. Each test runs against its own temporary SQLite file.</summary>
[TestClass]
[DoNotParallelize]
public sealed class WaitlistTests
{
    private const string Flight = "PR-101";
    private string _dbPath = "";
    private BookingStore _store = null!;
    private readonly DateTime _date = DateTime.Today.AddDays(30);

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"skywings-wl-{Guid.NewGuid():N}.db");
        var csb = new SqliteConnectionStringBuilder { DataSource = _dbPath, ForeignKeys = true, Pooling = false };
        _store = new BookingStore(csb.ConnectionString);
        _store.SeedFlightsIfEmpty(AppData.DomesticFlights, AppData.InternationalFlights,
            AppData.DefaultDomesticCapacity, AppData.DefaultInternationalCapacity);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    /// <summary>Books every Business seat on the flight with one-passenger bookings; returns the references.</summary>
    private List<string> FillBusiness()
    {
        int total = SeatLayout.RowCount("Business", _store.LoadAllFlights().Single(f => f.FlightNo == Flight).Capacity)
                    * AppData.ClassSeatLetters[SeatLayout.ClassIndex("Business")].Length;
        string prefix = AppData.SeatPrefixForClass("Business");
        var refs = new List<string>();
        int n = 0;
        foreach (string seat in AllSeats(prefix, total))
        {
            string reference = $"SKY-WL{n++:D5}";
            var p = new PassengerRecord("Ana Cruz", "1 Test St", "09171234567", 30, "1995-01-01", "PSP-0000001",
                false, false, new SeatAssignment("Ana Cruz", seat), Array.Empty<string>(), 1000m);
            var b = new BookingRecord(reference, DateTime.Now, Flight, "Manila (MNL)", "Cebu (CEB)", _date, true,
                "Business", "Credit Card", "**** 4521", 1000m, "Scheduled", new[] { p },
                AppData.StatusConfirmed, "PAID", "TXN-" + reference, DateTime.Now, 0m);
            _store.SaveConfirmedBooking(b, false, 1000m, "Credit Card", "**** 4521", "TXN-" + reference, DateTime.Now);
            refs.Add(reference);
        }
        return refs;
    }

    private static IEnumerable<string> AllSeats(string prefix, int total)
    {
        var letters = AppData.ClassSeatLetters[SeatLayout.ClassIndex("Business")];
        for (int i = 0; i < total; i++)
            yield return $"{prefix}-{i / letters.Length + 1:D2}{letters[i % letters.Length]}";
    }

    [TestMethod]
    public void Join_IsRejected_WhenSeatsAreAvailable()
    {
        Assert.IsNull(_store.JoinWaitlist(Flight, _date, "Economy", "Ana Cruz", "ana@example.com", 1));
    }

    [TestMethod]
    public void Join_RejectsDuplicateEmail_AndQueuesInOrder()
    {
        FillBusiness();
        WaitlistEntry? first = _store.JoinWaitlist(Flight, _date, "Business", "Ana", "a@example.com", 1);
        WaitlistEntry? second = _store.JoinWaitlist(Flight, _date, "Business", "Ben", "b@example.com", 1);
        WaitlistEntry? dup = _store.JoinWaitlist(Flight, _date, "Business", "Ana", "A@example.com", 1);

        Assert.IsNotNull(first);
        Assert.IsNotNull(second);
        Assert.AreEqual(1, first.Position);
        Assert.AreEqual(2, second.Position);
        Assert.IsNull(dup);
    }

    [TestMethod]
    public void Cancel_OffersFreedSeatToFirstInLine_AndRaisesEvent()
    {
        List<string> refs = FillBusiness();
        WaitlistEntry first = _store.JoinWaitlist(Flight, _date, "Business", "Ana", "a@example.com", 1)!;
        _store.JoinWaitlist(Flight, _date, "Business", "Ben", "b@example.com", 1);
        var raised = new List<WaitlistEntry>();
        _store.WaitlistOfferMade += raised.Add;

        Assert.IsTrue(_store.CancelBooking(refs[0], 0m));

        Assert.HasCount(1, raised);
        Assert.AreEqual(first.BookingReference, raised[0].BookingReference);
        WaitlistEntry? now = _store.FindWaitlistEntry(first.BookingReference);
        Assert.AreEqual(BookingStore.WaitlistOffered, now!.Status);
        Assert.IsNotNull(now.OfferExpiresAt);
    }

    [TestMethod]
    public void LiveOffer_ReservesSeatAgainstOtherBookers_ButNotForHolder()
    {
        List<string> refs = FillBusiness();
        WaitlistEntry entry = _store.JoinWaitlist(Flight, _date, "Business", "Ana", "a@example.com", 1)!;
        _store.CancelBooking(refs[0], 0m);

        Assert.AreEqual(0, _store.CabinSeatsLeft(Flight, _date, "Business"));
        Assert.AreEqual(1, _store.CabinSeatsLeft(Flight, _date, "Business", entry.BookingReference));
        Assert.IsTrue(_store.HasLiveOffer(entry.BookingReference, Flight, _date, "Business", 1, DateTime.Now));
        Assert.IsFalse(_store.HasLiveOffer(entry.BookingReference, Flight, _date, "Economy", 1, DateTime.Now));
    }

    [TestMethod]
    public void ExpiredOffer_PassesToNextInLine()
    {
        List<string> refs = FillBusiness();
        WaitlistEntry first = _store.JoinWaitlist(Flight, _date, "Business", "Ana", "a@example.com", 1)!;
        WaitlistEntry second = _store.JoinWaitlist(Flight, _date, "Business", "Ben", "b@example.com", 1)!;
        _store.CancelBooking(refs[0], 0m);

        List<WaitlistEntry> next = _store.ExpireWaitlistOffers(DateTime.Now.AddHours(AppData.WaitlistOfferHours + 1));

        Assert.HasCount(1, next);
        Assert.AreEqual(second.BookingReference, next[0].BookingReference);
        Assert.AreEqual(BookingStore.WaitlistExpired, _store.FindWaitlistEntry(first.BookingReference)!.Status);
    }

    [TestMethod]
    public void PartyTooLargeForFreedSeats_IsSkipped_SmallerPartyIsOffered()
    {
        List<string> refs = FillBusiness();
        _store.JoinWaitlist(Flight, _date, "Business", "Family", "fam@example.com", 3);
        WaitlistEntry solo = _store.JoinWaitlist(Flight, _date, "Business", "Solo", "solo@example.com", 1)!;

        _store.CancelBooking(refs[0], 0m);

        Assert.AreEqual(BookingStore.WaitlistOffered, _store.FindWaitlistEntry(solo.BookingReference)!.Status);
    }

    [TestMethod]
    public void CancellingTwice_DoesNotOfferTwice()
    {
        List<string> refs = FillBusiness();
        _store.JoinWaitlist(Flight, _date, "Business", "Ana", "a@example.com", 1);
        _store.JoinWaitlist(Flight, _date, "Business", "Ben", "b@example.com", 1);
        var raised = new List<WaitlistEntry>();
        _store.WaitlistOfferMade += raised.Add;

        Assert.IsTrue(_store.CancelBooking(refs[0], 0m));
        Assert.IsFalse(_store.CancelBooking(refs[0], 0m));

        Assert.HasCount(1, raised);
    }


    [TestMethod]
    public void OfferNearDeparture_IsCappedAtTheBookingCutoff()
    {
        List<string> refs = FillBusiness();
        _store.JoinWaitlist(Flight, _date, "Business", "Ana", "a@example.com", 1);
        WaitlistEntry second = _store.JoinWaitlist(Flight, _date, "Business", "Ben", "b@example.com", 1)!;
        _store.CancelBooking(refs[0], 0m);   // Ana is offered the seat

        DateTime departure = AppData.DepartureOf(Flight, _date);
        DateTime nearDeparture = departure.AddHours(-5);   // the next offer is made 5 hours before the flight
        List<WaitlistEntry> next = _store.ExpireWaitlistOffers(nearDeparture);

        Assert.HasCount(1, next);
        Assert.AreEqual(second.BookingReference, next[0].BookingReference);
        DateTime until = _store.FindWaitlistEntry(second.BookingReference)!.OfferExpiresAt!.Value;
        Assert.AreEqual(departure.AddHours(-AppData.BookingCutoffHours), until, "the seats must not stay held past the booking cutoff");
    }

    [TestMethod]
    public void NoOfferIsMade_OnceBookingsHaveClosed_AndTheTravellerKeepsWaiting()
    {
        List<string> refs = FillBusiness();
        _store.JoinWaitlist(Flight, _date, "Business", "Ana", "a@example.com", 1);
        WaitlistEntry second = _store.JoinWaitlist(Flight, _date, "Business", "Ben", "b@example.com", 1)!;
        _store.CancelBooking(refs[0], 0m);   // Ana is offered the seat

        DateTime afterCutoff = AppData.DepartureOf(Flight, _date).AddHours(-1);   // bookings closed an hour ago
        List<WaitlistEntry> next = _store.ExpireWaitlistOffers(afterCutoff);

        Assert.IsEmpty(next, "no one can still book, so nothing should be offered or held");
        Assert.AreEqual(BookingStore.WaitlistWaiting, _store.FindWaitlistEntry(second.BookingReference)!.Status);
        Assert.AreEqual(1, _store.CabinSeatsLeft(Flight, _date, "Business"), "the freed seat is not locked behind a dead offer");
    }

    [TestMethod]
    public void WaitingEntries_AreClosed_OnceTheFlightHasDeparted_ButNotBefore()
    {
        FillBusiness();
        WaitlistEntry entry = _store.JoinWaitlist(Flight, _date, "Business", "Ana", "a@example.com", 1)!;
        DateTime departure = AppData.DepartureOf(Flight, _date);

        _store.ExpireWaitlistOffers(departure.AddMinutes(-30));
        Assert.AreEqual(BookingStore.WaitlistWaiting, _store.FindWaitlistEntry(entry.BookingReference)!.Status);

        _store.ExpireWaitlistOffers(departure.AddMinutes(1));
        Assert.AreEqual(BookingStore.WaitlistExpired, _store.FindWaitlistEntry(entry.BookingReference)!.Status);
        Assert.IsEmpty(_store.LoadAllLiveWaitlist().Where(e => e.BookingReference == entry.BookingReference).ToList());
    }
    [TestMethod]
    public void Convert_AndLookupByCodeAlone()
    {
        List<string> refs = FillBusiness();
        WaitlistEntry entry = _store.JoinWaitlist(Flight, _date, "Business", "Ana", "a@example.com", 1)!;
        _store.CancelBooking(refs[0], 0m);

        Assert.IsNotNull(_store.FindWaitlistEntry(entry.BookingReference));
        Assert.IsNull(_store.FindWaitlistEntry("WL-NOPE0000"));
        Assert.IsNull(_store.FindWaitlistEntry(""));
        Assert.IsTrue(_store.MarkWaitlistConverted(entry.BookingReference));
        Assert.IsFalse(_store.HasLiveOffer(entry.BookingReference, Flight, _date, "Business", 1, DateTime.Now));
    }
}
