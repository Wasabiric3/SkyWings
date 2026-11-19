using SkyWings.Controllers;

namespace SkyWings.Tests.Controllers;

/// <summary>A booking's live trip status, worked out from the flight timeline and its check-ins.</summary>
[TestClass]
public sealed class TripStatusTests
{
    private const string Ana = "Ana Test", Ben = "Ben Test";
    private static readonly DateTime Base = new(2027, 3, 1);

    private static BookingRecord Booking(string flightNo, DateTime date, string status = AppData.StatusConfirmed,
        string? returnFlightNo = null, DateTime? returnDate = null)
    {
        PassengerRecord P(string name, string seat) => new(name, "Test St", "09170000000", 30, "1996-01-01", "ID", false, false,
            new SeatAssignment(name, seat), Array.Empty<string>(), 1000m);
        return new BookingRecord("SKY-TRIPTEST0001", Base.AddDays(-10), flightNo, "Manila (MNL)", "Cebu (CEB)", date, true,
            "Economy", "Credit Card", "**** 1111", 2000m, "Scheduled", new[] { P(Ana, "ECO1A"), P(Ben, "ECO1B") },
            status, AppData.PaymentPaid, "TXN", Base.AddDays(-10), 0m)
        {
            ReturnFlightNo = returnFlightNo,
            ReturnFlightDateValue = returnDate
        };
    }

    private static CheckInRecord In(string name, string flightNo, DateTime date)
        => new("SKY-TRIPTEST0001", name, flightNo, date, "ECO1A", AppData.DepartureOf(flightNo, date).AddHours(-3));

    /// <summary>A date (from <see cref="Base"/>) on which the flight has, or has not, a delay.</summary>
    private static DateTime DateWith(string flightNo, bool delayed)
    {
        for (int d = 0; d < 400; d++)
        {
            DateTime date = Base.AddDays(d);
            if (FlightStatusEngine.Evaluate(flightNo, date, date).DelayMinutes > 0 == delayed) return date;
        }
        throw new AssertInconclusiveException("no such date");
    }

    private static FlightStatusInfo Timeline(string flightNo, DateTime date) => FlightStatusEngine.Evaluate(flightNo, date, date);

    private static TripStatusInfo At(BookingRecord booking, DateTime now, params CheckInRecord[] checkIns)
        => TripStatus.Of(booking, checkIns, now);

    [TestMethod]
    public void Canceled_WinsOverEverything()
    {
        DateTime date = DateWith("PR-101", false);
        BookingRecord booking = Booking("PR-101", date, AppData.StatusCanceled);
        TripStatusInfo trip = At(booking, Timeline("PR-101", date).EstimatedArrival.AddHours(5));
        Assert.AreEqual(TripPhase.Canceled, trip.Phase);
        Assert.AreEqual("Canceled", trip.Label);
    }

    [TestMethod]
    public void BeforeCheckInOpens_IsUpcoming()
    {
        DateTime date = DateWith("PR-101", false);
        TripStatusInfo trip = At(Booking("PR-101", date), AppData.DepartureOf("PR-101", date).AddHours(-30));
        Assert.AreEqual(TripPhase.Upcoming, trip.Phase);
        Assert.AreEqual("Upcoming", trip.Label);
    }

    [TestMethod]
    public void CheckInOpen_NobodyCheckedIn()
    {
        DateTime date = DateWith("PR-101", false);
        TripStatusInfo trip = At(Booking("PR-101", date), AppData.DepartureOf("PR-101", date).AddHours(-5));
        Assert.AreEqual(TripPhase.CheckInOpen, trip.Phase);
        Assert.AreEqual("Check-in open", trip.Label);
    }

    [TestMethod]
    public void CheckInOpen_OneOfTwoCheckedIn_IsCheckedIn()
    {
        DateTime date = DateWith("PR-101", false);
        TripStatusInfo trip = At(Booking("PR-101", date), AppData.DepartureOf("PR-101", date).AddHours(-2), In(Ana, "PR-101", date));
        Assert.AreEqual(TripPhase.CheckedIn, trip.Phase);
        Assert.AreEqual("Checked in", trip.Label);
    }

    [TestMethod]
    public void ACheckInForAnotherFlight_DoesNotCount()
    {
        DateTime date = DateWith("PR-101", false);
        TripStatusInfo trip = At(Booking("PR-101", date), AppData.DepartureOf("PR-101", date).AddHours(-2), In(Ana, "PR-101", date.AddDays(1)));
        Assert.AreEqual(TripPhase.CheckInOpen, trip.Phase);
    }

    [TestMethod]
    public void Boarding_WithoutCheckIns_IsStillBoarding()
    {
        DateTime date = DateWith("PR-101", false);
        TripStatusInfo trip = At(Booking("PR-101", date), AppData.DepartureOf("PR-101", date).AddMinutes(-20));
        Assert.AreEqual(TripPhase.Boarding, trip.Phase);
        Assert.AreEqual("Boarding", trip.Label);
    }

    [TestMethod]
    public void InFlight_WithCheckIn()
    {
        DateTime date = DateWith("PR-101", false);
        TripStatusInfo trip = At(Booking("PR-101", date), AppData.DepartureOf("PR-101", date).AddMinutes(30), In(Ana, "PR-101", date));
        Assert.AreEqual(TripPhase.InFlight, trip.Phase);
        Assert.AreEqual("In flight", trip.Label);
    }

    [TestMethod]
    public void Departed_NobodyCheckedIn_IsNoShow()
    {
        DateTime date = DateWith("PR-101", false);
        TripStatusInfo trip = At(Booking("PR-101", date), AppData.DepartureOf("PR-101", date).AddMinutes(30));
        Assert.AreEqual(TripPhase.NoShow, trip.Phase);
        Assert.AreEqual("No-show", trip.Label);
    }

    [TestMethod]
    public void Landed_IsCompleted_WithLandedAtEqualToEstimatedArrival()
    {
        DateTime date = DateWith("PR-101", true);   // a delayed flight: the landing time must include the delay
        FlightStatusInfo timeline = Timeline("PR-101", date);
        TripStatusInfo trip = At(Booking("PR-101", date), timeline.EstimatedArrival.AddMinutes(1), In(Ben, "PR-101", date));
        Assert.AreEqual(TripPhase.Completed, trip.Phase);
        Assert.AreEqual("Completed", trip.Label);
        Assert.AreEqual(timeline.EstimatedArrival, trip.LandedAt);
        Assert.AreEqual("Cebu (CEB)", trip.Destination);
    }

    [TestMethod]
    public void OvernightFlight_StaysInFlightUntilArrivalNextDay()
    {
        foreach (string flightNo in AppData.FlightScheduledTimes.Keys)
            for (int d = 0; d < 30; d++)
            {
                DateTime date = Base.AddDays(d);
                FlightStatusInfo timeline = Timeline(flightNo, date);
                if (timeline.EstimatedArrival.Date <= date) continue;
                BookingRecord booking = Booking(flightNo, date);
                CheckInRecord checkIn = In(Ana, flightNo, date);
                Assert.AreEqual(TripPhase.InFlight, At(booking, timeline.EstimatedArrival.AddMinutes(-1), checkIn).Phase);
                Assert.AreEqual(TripPhase.Completed, At(booking, timeline.EstimatedArrival.AddMinutes(1), checkIn).Phase);
                return;
            }
        Assert.Inconclusive("no overnight flight in the schedule");
    }

    [TestMethod]
    public void DelayedFlight_PastScheduledTime_IsNotYetInFlight()
    {
        DateTime date = DateWith("PR-101", true);
        TripStatusInfo trip = At(Booking("PR-101", date), AppData.DepartureOf("PR-101", date).AddMinutes(1), In(Ana, "PR-101", date));
        Assert.AreNotEqual(TripPhase.InFlight, trip.Phase);
        Assert.AreNotEqual(TripPhase.NoShow, trip.Phase);
    }

    private static DateTime DateWithDelayOfAtLeast(string flightNo, int minutes)
    {
        for (int d = 0; d < 400; d++)
        {
            DateTime date = Base.AddDays(d);
            if (FlightStatusEngine.Evaluate(flightNo, date, date).DelayMinutes >= minutes) return date;
        }
        throw new AssertInconclusiveException("no such date");
    }

    [TestMethod]
    public void DelayedFlight_CheckedInRightAfterTheWindowOpens_IsCheckedIn()
    {
        // Check-in opens 24 h before the *scheduled* departure; the delayed timeline would still say "Scheduled" then.
        DateTime date = DateWithDelayOfAtLeast("PR-101", 30);
        DateTime opens = AppData.DepartureOf("PR-101", date).AddHours(-AppData.CheckInWindowHours);
        BookingRecord booking = Booking("PR-101", date);

        Assert.AreEqual(TripPhase.CheckInOpen, At(booking, opens.AddMinutes(5)).Phase);
        Assert.AreEqual(TripPhase.CheckedIn, At(booking, opens.AddMinutes(5), In(Ana, "PR-101", date)).Phase);
        Assert.AreEqual(TripPhase.Upcoming, At(booking, opens.AddMinutes(-5)).Phase);
    }

    [TestMethod]
    public void DelayedFlight_AfterTheScheduledDeparture_NobodyCheckedIn_IsNotCheckInOpen()
    {
        // Check-in closed at the scheduled departure, but the delayed flight is still on the ground.
        DateTime date = DateWithDelayOfAtLeast("PR-101", 50);
        TripStatusInfo trip = At(Booking("PR-101", date), AppData.DepartureOf("PR-101", date).AddMinutes(1));
        Assert.AreEqual(TripPhase.Boarding, trip.Phase);
    }

    [TestMethod]
    public void RoundTrip_OutboundLanded_ShowsReturnLegPrefixed()
    {
        DateTime date = DateWith("PR-101", false);
        BookingRecord booking = Booking("PR-101", date, returnFlightNo: "PR-102", returnDate: date.AddDays(3));
        TripStatusInfo trip = At(booking, date.AddDays(1).AddHours(12), In(Ana, "PR-101", date));
        Assert.AreEqual(TripPhase.Upcoming, trip.Phase);
        Assert.IsTrue(trip.ReturnLeg);
        Assert.AreEqual("Return · Upcoming", trip.Label);
        Assert.AreEqual("Manila (MNL)", trip.Destination);
    }

    [TestMethod]
    public void RoundTrip_OutboundNoShow_IsFinal()
    {
        DateTime date = DateWith("PR-101", false);
        BookingRecord booking = Booking("PR-101", date, returnFlightNo: "PR-102", returnDate: date.AddDays(3));
        TripStatusInfo trip = At(booking, date.AddDays(1).AddHours(12));
        Assert.AreEqual(TripPhase.NoShow, trip.Phase);
        Assert.IsFalse(trip.ReturnLeg);
    }

    [TestMethod]
    public void RoundTrip_CompletedOnlyAfterReturnLands()
    {
        DateTime date = DateWith("PR-101", false);
        DateTime back = date.AddDays(3);
        BookingRecord booking = Booking("PR-101", date, returnFlightNo: "PR-102", returnDate: back);
        CheckInRecord[] checkIns = { In(Ana, "PR-101", date), In(Ana, "PR-102", back) };
        FlightStatusInfo returnTimeline = Timeline("PR-102", back);

        TripStatusInfo flying = At(booking, returnTimeline.EstimatedArrival.AddMinutes(-5), checkIns);
        Assert.AreEqual(TripPhase.InFlight, flying.Phase);
        Assert.AreEqual("Return · In flight", flying.Label);

        TripStatusInfo done = At(booking, returnTimeline.EstimatedArrival.AddMinutes(5), checkIns);
        Assert.AreEqual(TripPhase.Completed, done.Phase);
        Assert.AreEqual("Completed", done.Label);
        Assert.AreEqual(returnTimeline.EstimatedArrival, done.LandedAt);
    }

    [TestMethod]
    public void Matches_InProgressCoversFourPhases()
    {
        TripPhase[] inProgress = { TripPhase.CheckInOpen, TripPhase.CheckedIn, TripPhase.Boarding, TripPhase.InFlight };
        foreach (TripPhase phase in Enum.GetValues<TripPhase>())
        {
            Assert.AreEqual(inProgress.Contains(phase), TripStatus.Matches(phase, "In progress"), phase.ToString());
            Assert.IsTrue(TripStatus.Matches(phase, "All"));
        }
        Assert.IsTrue(TripStatus.Matches(TripPhase.Completed, "Completed"));
        Assert.IsTrue(TripStatus.Matches(TripPhase.NoShow, "No-show"));
        Assert.IsTrue(TripStatus.Matches(TripPhase.Upcoming, "Upcoming"));
        Assert.IsTrue(TripStatus.Matches(TripPhase.Canceled, "Canceled"));
        Assert.IsFalse(TripStatus.Matches(TripPhase.Completed, "Upcoming"));
        CollectionAssert.AreEqual(new[] { "All", "Upcoming", "In progress", "Completed", "No-show", "Canceled" }, TripStatus.Filters);
    }
}
