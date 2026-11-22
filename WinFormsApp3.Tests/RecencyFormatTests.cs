using SkyWings.UI;

namespace SkyWings.Tests;

[TestClass]
public sealed class RecencyFormatTests
{
    private static readonly DateTime Now = new(2026, 11, 22, 11, 30, 0);

    [TestMethod]
    public void Relative_SaysHowLongAgo_ForTheLastHour()
    {
        Assert.AreEqual("just now", RecencyFormat.Relative(Now.AddSeconds(-20), Now));
        Assert.AreEqual("1 min ago", RecencyFormat.Relative(Now.AddSeconds(-61), Now));
        Assert.AreEqual("12 min ago", RecencyFormat.Relative(Now.AddMinutes(-12), Now));
        Assert.AreEqual("59 min ago", RecencyFormat.Relative(Now.AddMinutes(-59).AddSeconds(-10), Now));
    }

    [TestMethod]
    public void Relative_AfterAnHour_GivesTodayYesterdayOrTheDate()
    {
        Assert.AreEqual("Today, 9:07 AM", RecencyFormat.Relative(new DateTime(2026, 11, 22, 9, 7, 0), Now));
        Assert.AreEqual("Today, 12:05 AM", RecencyFormat.Relative(new DateTime(2026, 11, 22, 0, 5, 0), Now), "midnight hour reads 12, not 0");
        Assert.AreEqual("Yesterday, 4:45 PM", RecencyFormat.Relative(new DateTime(2026, 11, 21, 16, 45, 0), Now));
        Assert.AreEqual("Nov 20, 9:07 AM", RecencyFormat.Relative(new DateTime(2026, 11, 20, 9, 7, 0), Now));
        Assert.AreEqual("Dec 31, 2025, 11:59 PM", RecencyFormat.Relative(new DateTime(2025, 12, 31, 23, 59, 0), Now), "another year shows the year");
    }

    [TestMethod]
    public void Relative_ATimeInTheFuture_IsJustNow_NotNegative()
        => Assert.AreEqual("just now", RecencyFormat.Relative(Now.AddMinutes(5), Now));

    [TestMethod]
    public void Relative_JustAfterMidnight_StillSaysMinutesForTheLastHour_ThenYesterday()
    {
        DateTime justAfterMidnight = new(2026, 11, 22, 0, 10, 0);
        Assert.AreEqual("40 min ago", RecencyFormat.Relative(new DateTime(2026, 11, 21, 23, 30, 0), justAfterMidnight), "40 minutes is 40 minutes even across midnight");
        Assert.AreEqual("Yesterday, 10:30 PM", RecencyFormat.Relative(new DateTime(2026, 11, 21, 22, 30, 0), justAfterMidnight));
    }

    [TestMethod]
    public void Stamp_IsAlwaysAnAbsoluteTime_WithTodayAndYesterday()
    {
        Assert.AreEqual("Today, 11:25 AM", RecencyFormat.Stamp(Now.AddMinutes(-5), Now), "no 'min ago' in a stamp");
        Assert.AreEqual("Yesterday, 4:45 PM", RecencyFormat.Stamp(new DateTime(2026, 11, 21, 16, 45, 0), Now));
        Assert.AreEqual("Nov 3, 8:00 AM", RecencyFormat.Stamp(new DateTime(2026, 11, 3, 8, 0, 0), Now));
    }

    [TestMethod]
    public void IsNew_MeansBookedAfterTheBaseline_AndNothingIsNewWithoutOne()
    {
        Assert.IsTrue(RecencyFormat.IsNew(Now, Now.AddMinutes(-1)));
        Assert.IsFalse(RecencyFormat.IsNew(Now, Now), "booked exactly at the baseline was already seen");
        Assert.IsFalse(RecencyFormat.IsNew(Now.AddMinutes(-5), Now));
        Assert.IsFalse(RecencyFormat.IsNew(Now, null), "the first load has nothing to compare with");
    }
}
