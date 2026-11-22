using Microsoft.Data.Sqlite;
using SkyWings.Controllers;

namespace SkyWings.Tests.Controllers;

/// <summary>What the home board shows and what its details panel needs, decided without a window.</summary>
[TestClass]
public sealed class HomeBoardTests
{
    private string _dbPath = "";
    private BookingStore _store = null!;
    private HomeController _controller = null!;
    private readonly DateTime _day = DateTime.Today.AddDays(30);

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"skywings-board-{Guid.NewGuid():N}.db");
        var csb = new SqliteConnectionStringBuilder { DataSource = _dbPath, ForeignKeys = true, Pooling = false };
        _store = new BookingStore(csb.ConnectionString);
        _store.SeedFlightsIfEmpty(AppData.DomesticFlights, AppData.InternationalFlights, AppData.DefaultDomesticCapacity, AppData.DefaultInternationalCapacity);
        _store.SetFlightAircraft("PR-101", "A320");                                  // a narrow-body: Economy and Business
        _store.SetFlightAircraft(AppData.EuropeFlights.First().FlightNo, "B777");     // a wide-body: all four cabins
        _controller = new HomeController(_store, new StaffSession(1, "agent1", "Ana Reyes", StaffRole.Agent, false));
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    // ------------------------------------------------------------------ regions and the board query

    [TestMethod]
    [DataRow("PR-101", "Domestic")]
    [DataRow("PR-501", "Domestic")]
    [DataRow("PR-201", "Southeast Asia")]
    [DataRow("PR-602", "Southeast Asia")]
    [DataRow("PR-304", "Europe")]
    [DataRow("PR-704", "Europe")]
    [DataRow("PR-402", "North America")]
    [DataRow("PR-802", "North America")]
    [DataRow("XX-9", "Other")]
    public void Region_OfAFlight_FollowsItsNumber(string flightNo, string region)
        => Assert.AreEqual(region, FlightRegions.Of(new Flight(flightNo, "Manila (MNL)", "Somewhere", 1000m)));

    [TestMethod]
    public void TheRegionNames_AreTheFourOnTheHomeScreen()
        => CollectionAssert.AreEqual(new[] { "Domestic", "Southeast Asia", "Europe", "North America" }, FlightRegions.Names.ToArray());

    [TestMethod]
    public void Board_FilteredByRegion_ListsOnlyThatRegion()
    {
        List<FlightRow> all = _controller.LoadBoard("", "", _day);
        List<FlightRow> europe = _controller.LoadBoard("Europe", "", _day);
        Assert.IsNotEmpty(europe);
        Assert.IsLessThan(all.Count, europe.Count);
        Assert.IsTrue(europe.All(r => FlightRegions.Of(r.Flight) == "Europe"));
        Assert.IsTrue(all.All(r => r.Flight.Origin.StartsWith("Manila")), "the board lists flights leaving Manila");
    }

    [TestMethod]
    [DataRow(" ceb ")]
    [DataRow("CEBU")]
    public void Board_FilteredByText_MatchesACity_IgnoringCaseAndSpaces(string text)
    {
        List<FlightRow> rows = _controller.LoadBoard("", text, _day);
        Assert.IsNotEmpty(rows);
        Assert.IsTrue(rows.All(r => r.Flight.Destination.Contains("Cebu", StringComparison.OrdinalIgnoreCase)));
    }

    [TestMethod]
    public void Board_FilteredByText_MatchesAFlightNumber() => Assert.AreEqual("PR-304", _controller.LoadBoard("", "pr-304", _day).Single().FlightNo);

    [TestMethod]
    [DataRow("zzzz")]
    [DataRow("%")]
    [DataRow("' OR 1=1 --")]
    public void Board_WithNoMatch_IsEmpty_AndDoesNotThrow(string text) => Assert.IsEmpty(_controller.LoadBoard("", text, _day));

    [TestMethod]
    public void Board_TextAndRegion_Combine()
    {
        Assert.IsNotEmpty(_controller.LoadBoard("Domestic", "cebu", _day));
        Assert.IsEmpty(_controller.LoadBoard("Europe", "cebu", _day), "Cebu is not in Europe");
    }

    [TestMethod]
    public void Board_ShowsTheEconomyFare_AndTotalSeatsAcrossTheAircraftsCabins()
    {
        FlightRow row = _controller.LoadBoard("", "PR-101", _day).Single();
        Aircraft aircraft = _store.LoadAircraft("PR-101");
        Assert.AreEqual($"PHP {row.Flight.BasePrice:N0}", row.Price, "the Economy fare, from");
        int total = aircraft.Cabins.Sum(c => _store.CabinSeatsLeft("PR-101", _day, c.ClassName));
        Assert.AreEqual(total, row.Seats);
        Assert.AreEqual(total.ToString("N0"), row.SeatsLeft);
    }

    [TestMethod]
    public void Board_IsOrderedByEstimatedDeparture()
    {
        List<FlightRow> rows = _controller.LoadBoard("", "", _day);
        CollectionAssert.AreEqual(rows.Select(r => r.Status.Estimated).OrderBy(t => t).ToList(), rows.Select(r => r.Status.Estimated).ToList());
    }

    // ------------------------------------------------------------------ the details of a flight

    private static Flight Pr101 => AppData.DomesticFlights.First(f => f.FlightNo == "PR-101");

    /// <summary>The flight's status on its own day, from which "now" values at known distances from departure are worked out.</summary>
    private FlightStatusInfo Baseline(Flight flight, DateTime day) => FlightStatusEngine.Evaluate(flight.FlightNo, day, day.AddDays(-30));

    private void SellOutEconomy(string flightNo, DateTime day)
    {
        Aircraft aircraft = _store.LoadAircraft(flightNo);
        CabinLayout economy = aircraft.Cabin("Economy")!;
        var people = new List<PassengerRecord>();
        for (int row = 1; row <= economy.Rows; row++)
            foreach (char letter in economy.Letters)
            {
                string name = $"Guest {row}{letter}";
                people.Add(new PassengerRecord(name, "1 Test St", "09171234567", 30, "1995-01-01", "P" + people.Count.ToString("D7"), false, false,
                    new SeatAssignment(name, economy.Code(row, letter)), Array.Empty<string>(), 1000m));
            }
        // a booking holds at most 9 travellers, so the cabin is filled by several
        int n = 0;
        foreach (PassengerRecord[] group in people.Chunk(9))
        {
            string reference = $"SKY-SELLOUT{++n:D4}";
            var booking = new BookingRecord(reference, DateTime.Now, flightNo, "Manila (MNL)", "Cebu (CEB)", day, false, "Economy", "Credit Card", "**** 4521",
                1000m * group.Length, "Scheduled", group, AppData.StatusConfirmed, "PAID", "TXN-" + reference, DateTime.Now, 0m);
            _store.SaveConfirmedBooking(booking, false, booking.TotalAmount, "Credit Card", "**** 4521", "TXN-" + reference, DateTime.Now);
        }
        Assert.AreEqual(0, _store.CabinSeatsLeft(flightNo, day, "Economy"), "the helper really sold the cabin out");
    }

    [TestMethod]
    [DataRow("scheduled", 0)]
    [DataRow("checkin", 1)]
    [DataRow("boarding", 2)]
    [DataRow("gateclosing", 2)]
    [DataRow("departed", 3)]
    [DataRow("inflight", 3)]
    [DataRow("landed", 4)]
    public void Timeline_MarksTheCurrentStage_ForEachStatus(string moment, int current)
    {
        Flight flight = Pr101;
        FlightStatusInfo b = Baseline(flight, _day);
        DateTime now = moment switch
        {
            "scheduled" => b.Estimated.AddDays(-3),
            "checkin" => b.Estimated.AddHours(-5),
            "boarding" => b.Estimated.AddMinutes(-30),
            "gateclosing" => b.Estimated.AddMinutes(-5),
            "departed" => b.Estimated.AddMinutes(5),
            "inflight" => b.Estimated.AddMinutes(30),
            _ => b.EstimatedArrival.AddMinutes(1)
        };
        FlightDetails details = _controller.Describe(flight, _day, now);
        Assert.HasCount(5, details.Timeline);
        CollectionAssert.AreEqual(new[] { "Scheduled", "Check-in open", "Boarding", "Departed", "Landed" }, details.Timeline.Select(t => t.Label).ToArray());
        for (int i = 0; i < 5; i++)
            Assert.AreEqual(i < current ? StageState.Done : i == current ? StageState.Current : StageState.Upcoming, details.Timeline[i].State, $"stage {i} at {moment}");
    }

    [TestMethod]
    public void Timeline_ShowsTheEstimatedTimes_AndNamesTheDelay()
    {
        Flight flight = Pr101;
        DateTime delayedDay = Enumerable.Range(1, 90).Select(n => DateTime.Today.AddDays(n)).First(d => Baseline(flight, d).DelayMinutes > 0);
        FlightStatusInfo b = Baseline(flight, delayedDay);
        FlightDetails details = _controller.Describe(flight, delayedDay, b.Estimated.AddHours(-5));
        Assert.AreEqual($"Delayed {b.DelayMinutes} min", details.DelayText);
        Assert.AreEqual(b.Estimated.AddMinutes(-FlightStatusEngine.BoardingMinutes).ToString("h:mm tt"), details.Timeline[2].Time);
        Assert.AreEqual(b.Estimated.ToString("h:mm tt"), details.Timeline[3].Time);

        DateTime onTimeDay = Enumerable.Range(1, 90).Select(n => DateTime.Today.AddDays(n)).First(d => Baseline(flight, d).DelayMinutes == 0);
        Assert.AreEqual("", _controller.Describe(flight, onTimeDay, onTimeDay).DelayText);
    }

    [TestMethod]
    public void TheCheckInTime_OnTheDayBefore_CarriesItsDate()
    {
        FlightDetails details = _controller.Describe(Pr101, _day, _day.AddDays(-30));
        StringAssert.Contains(details.Timeline[1].Time, ",", "check-in opens the day before, so the date is shown");
        Assert.DoesNotContain(",", details.Timeline[0].Time, "the scheduled time is on the flight's own day");
    }

    [TestMethod]
    public void Cabins_AreTheAircraftsOwn_InClassOrder_WithFareAndSeats()
    {
        FlightDetails details = _controller.Describe(Pr101, _day, _day.AddDays(-30));
        Aircraft aircraft = _store.LoadAircraft("PR-101");
        CollectionAssert.AreEqual(new[] { "Economy", "Business" }, details.Cabins.Select(c => c.ClassName).ToArray(), "an A320 has Economy and Business, Economy first");
        CabinOption economy = details.Cabins[0], business = details.Cabins[1];
        Assert.AreEqual(AppData.Round2(Pr101.BasePrice * AppData.ClassMultipliers[0]), economy.Fare);
        Assert.AreEqual(AppData.Round2(Pr101.BasePrice * AppData.ClassMultipliers[2]), business.Fare);
        Assert.AreEqual(_store.CabinSeatsLeft("PR-101", _day, "Business"), business.SeatsLeft);
        Assert.AreEqual(aircraft.Cabin("Business")!.Seats, business.Seats);
        Assert.AreEqual(aircraft.Model, details.Aircraft);
        StringAssert.StartsWith(details.Duration, "about ");
    }

    [TestMethod]
    public void ABigAircraft_ListsAllFourCabins_EconomyFirstAndFirstClassLast()
    {
        Flight europe = AppData.EuropeFlights.First();
        FlightDetails details = _controller.Describe(europe, _day, _day.AddDays(-30));
        CollectionAssert.AreEqual(new[] { "Economy", "Premium Economy", "Business", "First Class" }, details.Cabins.Select(c => c.ClassName).ToArray());
    }

    [TestMethod]
    public void DefaultCabin_IsTheFirstWithSeats_ElseEconomy()
    {
        Assert.AreEqual(0, _controller.Describe(Pr101, _day, _day.AddDays(-30)).DefaultCabin, "Economy has seats");
        SellOutEconomy("PR-101", _day);
        FlightDetails details = _controller.Describe(Pr101, _day, _day.AddDays(-30));
        Assert.AreEqual(0, details.Cabins[0].SeatsLeft);
        Assert.AreEqual(1, details.DefaultCabin, "Economy is sold out, so Business is the default");
    }

    [TestMethod]
    public void TheBoard_StaysSensible_WhenOneCabinIsSoldOutAndTheOtherIsNot()
    {
        SellOutEconomy("PR-101", _day);
        FlightRow row = _controller.LoadBoard("", "PR-101", _day).Single();
        Assert.IsGreaterThan(0, row.Seats, "Business still has seats, so the flight is not sold out");
        Assert.IsFalse(row.SoldOut);
        Assert.AreEqual($"PHP {Pr101.BasePrice:N0}", row.Price, "the board still shows the Economy fare");
    }

    // ------------------------------------------------------------------ the one booking action

    private static CabinOption Cabin(string name = "Economy", int left = 12) => new(name, 3500m, left, 168, 90);

    [TestMethod]
    public void Action_Book_ForABookableCabin_NamesTheFlightAndCabin()
    {
        BookingAction action = _controller.ActionFor(Pr101, _day, Cabin("Business"), _day.AddDays(-30));
        Assert.AreEqual(BookAction.Book, action.Kind);
        Assert.AreEqual("Book PR-101 · Business →", action.Label);
        Assert.AreEqual("", action.Reason);
    }

    [TestMethod]
    public void Action_JoinWaitlist_ForASoldOutCabin()
    {
        BookingAction action = _controller.ActionFor(Pr101, _day, Cabin(left: 0), _day.AddDays(-30));
        Assert.AreEqual(BookAction.JoinWaitlist, action.Kind);
        Assert.AreEqual("Join waitlist →", action.Label);
    }

    [TestMethod]
    public void Action_None_AfterDeparture_SaysTheFlightLeft()
    {
        FlightStatusInfo b = Baseline(Pr101, _day);
        BookingAction action = _controller.ActionFor(Pr101, _day, Cabin(), b.Estimated.AddMinutes(10));
        Assert.AreEqual(BookAction.None, action.Kind);
        Assert.AreEqual("This flight has already left.", action.Reason);
        Assert.AreEqual("Book PR-101 · Economy →", action.Label, "the label still names the flight, but the button is off");
    }

    [TestMethod]
    public void Action_None_InsideTheCutoff_SaysWhenBookingsClose()
    {
        FlightStatusInfo b = Baseline(Pr101, _day);
        BookingAction action = _controller.ActionFor(Pr101, _day, Cabin(), b.Scheduled.AddHours(-1));
        Assert.AreEqual(BookAction.None, action.Kind);
        Assert.AreEqual($"Bookings close {AppData.BookingCutoffHours} hours before departure.", action.Reason);
    }

    [TestMethod]
    public void Action_ASoldOutCabin_OnADepartedFlight_IsNotAWaitlistOffer()
    {
        FlightStatusInfo b = Baseline(Pr101, _day);
        Assert.AreEqual(BookAction.None, _controller.ActionFor(Pr101, _day, Cabin(left: 0), b.Estimated.AddHours(1)).Kind);
    }

    [TestMethod]
    public void Action_None_WithoutACabin_AsksToChooseOne()
    {
        BookingAction action = _controller.ActionFor(Pr101, _day, null, _day.AddDays(-30));
        Assert.AreEqual(BookAction.None, action.Kind);
        Assert.AreEqual("Choose a cabin.", action.Reason);
    }
}
