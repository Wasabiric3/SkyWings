using SkyWings.Controllers;

namespace SkyWings.Tests.Controllers;

[TestClass]
public sealed class FlightStatusEngineTests
{
    private static readonly string[] Flights = { "PR-101", "PR-102", "PR-201", "PR-301" };
    private static readonly DateTime Base = new(2027, 3, 1);

    /// <summary>Finds a flight and date whose simulated delay matches the wanted kind (on time or late).</summary>
    private static (string FlightNo, DateTime Date, FlightStatusInfo Info) Find(bool delayed)
    {
        foreach (string flight in Flights)
            for (int day = 0; day < 90; day++)
            {
                DateTime date = Base.AddDays(day);
                FlightStatusInfo info = FlightStatusEngine.Evaluate(flight, date, date.AddDays(-5));
                if ((info.DelayMinutes > 0) == delayed) return (flight, date, info);
            }
        throw new AssertFailedException("No matching flight found in 90 days.");
    }

    private static FlightStatusInfo At(string flight, DateTime date, DateTime estimated, TimeSpan offset)
        => FlightStatusEngine.Evaluate(flight, date, estimated + offset);

    [TestMethod]
    public void Evaluate_SameInputs_GiveTheSameAnswerEveryTime()
    {
        var (flight, date, _) = Find(delayed: true);
        DateTime now = date.AddHours(5);

        Assert.AreEqual(FlightStatusEngine.Evaluate(flight, date, now), FlightStatusEngine.Evaluate(flight, date, now));
    }

    [TestMethod]
    public void Evaluate_DelayAndGateDoNotChangeWithTheClock()
    {
        var (flight, date, first) = Find(delayed: true);

        FlightStatusInfo early = FlightStatusEngine.Evaluate(flight, date, first.Estimated.AddHours(-5));
        FlightStatusInfo later = FlightStatusEngine.Evaluate(flight, date, first.Estimated.AddMinutes(-20));

        Assert.AreEqual(first.DelayMinutes, later.DelayMinutes);
        Assert.AreEqual(first.Estimated, later.Estimated);
        Assert.AreEqual(early.Gate, later.Gate);
        StringAssert.Matches(later.Gate, new System.Text.RegularExpressions.Regex("^[A-D][0-9]{1,2}$"));
    }

    [TestMethod]
    public void Evaluate_DelaysAreZeroOrBetween10And75MinutesInFives()
    {
        var delays = new HashSet<int>();
        foreach (string flight in Flights)
            for (int day = 0; day < 60; day++)
                delays.Add(FlightStatusEngine.Evaluate(flight, Base.AddDays(day), Base).DelayMinutes);

        Assert.Contains(0, delays);
        Assert.IsTrue(delays.Any(d => d > 0), "some flights should be late");
        Assert.IsTrue(delays.All(d => d == 0 || (d >= 10 && d <= FlightStatusEngine.MaxDelayMinutes && d % 5 == 0)));
    }

    [TestMethod]
    public void Evaluate_EstimatedIsScheduledPlusDelay()
    {
        var (flight, date, info) = Find(delayed: true);

        Assert.AreEqual(AppData.DepartureOf(flight, date), info.Scheduled);
        Assert.AreEqual(info.Scheduled.AddMinutes(info.DelayMinutes), info.Estimated);
        Assert.IsGreaterThan(info.Estimated, info.EstimatedArrival);
    }

    [TestMethod]
    public void Evaluate_FarInTheFuture_IsScheduledWithNoGate()
    {
        var (flight, date, info) = Find(delayed: false);

        FlightStatusInfo far = At(flight, date, info.Estimated, TimeSpan.FromDays(-3));

        Assert.AreEqual(FlightStatusKind.Scheduled, far.Kind);
        Assert.AreEqual("Scheduled", far.Label);
        Assert.AreEqual("", far.Gate);
        Assert.AreEqual("departs in 3d 0h", far.Countdown);
    }

    [TestMethod]
    public void Evaluate_OnTimeFlightInsideCheckInWindow_IsCheckInOpen()
    {
        var (flight, date, info) = Find(delayed: false);

        FlightStatusInfo s = At(flight, date, info.Estimated, TimeSpan.FromHours(-5.5));

        Assert.AreEqual(FlightStatusKind.CheckInOpen, s.Kind);
        Assert.AreEqual("boards in 4h 50m", s.Countdown);
        Assert.AreNotEqual("", s.Gate);
    }

    [TestMethod]
    public void Evaluate_LateFlightInsideCheckInWindow_IsDelayedWithMinutes()
    {
        var (flight, date, info) = Find(delayed: true);

        FlightStatusInfo s = At(flight, date, info.Estimated, TimeSpan.FromHours(-5));

        Assert.AreEqual(FlightStatusKind.Delayed, s.Kind);
        Assert.AreEqual($"Delayed +{info.DelayMinutes}m", s.Label);
    }

    [TestMethod]
    public void Evaluate_LateFlightFarAhead_IsStillJustScheduled()
    {
        var (flight, date, info) = Find(delayed: true);

        FlightStatusInfo s = At(flight, date, info.Estimated, TimeSpan.FromDays(-2));

        Assert.AreEqual(FlightStatusKind.Scheduled, s.Kind);
    }

    [TestMethod]
    public void Evaluate_WalksThroughBoardingToLanded()
    {
        var (flight, date, info) = Find(delayed: true);
        DateTime est = info.Estimated;
        int duration = AppData.FlightEstDurationMin.GetValueOrDefault(flight, 120);

        Assert.AreEqual(FlightStatusKind.Boarding, At(flight, date, est, TimeSpan.FromMinutes(-FlightStatusEngine.BoardingMinutes)).Kind);
        Assert.AreEqual(FlightStatusKind.Boarding, At(flight, date, est, TimeSpan.FromMinutes(-30)).Kind);
        Assert.AreEqual(FlightStatusKind.GateClosing, At(flight, date, est, TimeSpan.FromMinutes(-FlightStatusEngine.GateClosingMinutes)).Kind);
        Assert.AreEqual(FlightStatusKind.GateClosing, At(flight, date, est, TimeSpan.FromMinutes(-5)).Kind);
        Assert.AreEqual(FlightStatusKind.Departed, At(flight, date, est, TimeSpan.Zero).Kind);
        Assert.AreEqual(FlightStatusKind.Departed, At(flight, date, est, TimeSpan.FromMinutes(5)).Kind);
        Assert.AreEqual(FlightStatusKind.InFlight, At(flight, date, est, TimeSpan.FromMinutes(FlightStatusEngine.DepartedMinutes)).Kind);
        Assert.AreEqual(FlightStatusKind.InFlight, At(flight, date, est, TimeSpan.FromMinutes(duration - 1)).Kind);
        Assert.AreEqual(FlightStatusKind.Landed, At(flight, date, est, TimeSpan.FromMinutes(duration)).Kind);
        Assert.AreEqual(FlightStatusKind.Landed, At(flight, date, est, TimeSpan.FromMinutes(duration + 30)).Kind);
    }

    [TestMethod]
    public void Evaluate_CountdownPhrasesFollowTheState()
    {
        var (flight, date, info) = Find(delayed: false);
        DateTime est = info.Estimated;
        int duration = AppData.FlightEstDurationMin.GetValueOrDefault(flight, 120);

        Assert.AreEqual("departs in 12m", At(flight, date, est, TimeSpan.FromMinutes(-12)).Countdown);
        Assert.AreEqual("departs in 5m", At(flight, date, est, TimeSpan.FromMinutes(-5)).Countdown);
        Assert.AreEqual("departed 3m ago", At(flight, date, est, TimeSpan.FromMinutes(3)).Countdown);
        Assert.AreEqual("lands in " + Fmt(duration - 20), At(flight, date, est, TimeSpan.FromMinutes(20)).Countdown);
        Assert.AreEqual("landed 30m ago", At(flight, date, est, TimeSpan.FromMinutes(duration + 30)).Countdown);
    }

    private static string Fmt(int minutes) => minutes >= 60 ? $"{minutes / 60}h {minutes % 60:00}m" : $"{minutes}m";

    [TestMethod]
    public void Evaluate_NeverShowsZeroMinutes()
    {
        var (flight, date, info) = Find(delayed: false);

        FlightStatusInfo s = At(flight, date, info.Estimated, TimeSpan.FromSeconds(-10));

        Assert.AreEqual("departs in 1m", s.Countdown);
    }
}
