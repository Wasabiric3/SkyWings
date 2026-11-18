using SkyWings.UI;

namespace SkyWings.Tests;

[TestClass]
[DoNotParallelize]
public class SeatMapGeometryTests
{
    private static SeatMap Map(CabinLayout cabin)
    {
        var map = new SeatMap();
        map.Configure(cabin);
        return map;
    }

    [TestMethod]
    public void TwoAisles_AddTwoGapsToTheWidth()
    {
        using SeatMap wide = Map(new CabinLayout("Economy", 3, new[] { 3, 4, 3 }));
        using SeatMap narrow = Map(new CabinLayout("Economy", 3, new[] { 10 }));   // same 10 seats, no aisle

        int aisle = Gfx.S(wide, 30);
        Assert.AreEqual(narrow.Width + 2 * aisle, wide.Width);
    }

    [TestMethod]
    public void SeatsAfterAnAisle_AreShiftedByOneGapPerAisle()
    {
        using SeatMap map = Map(new CabinLayout("Economy", 2, new[] { 3, 4, 3 }));
        int aisle = Gfx.S(map, 30), seat = Gfx.S(map, 38), gap = Gfx.S(map, 6);

        Rectangle c2 = map.SeatBounds(0, 2), c3 = map.SeatBounds(0, 3), c6 = map.SeatBounds(0, 6), c7 = map.SeatBounds(0, 7);

        Assert.AreEqual(gap + aisle, c3.Left - c2.Right, "first aisle");
        Assert.AreEqual(gap, c6.Left - map.SeatBounds(0, 5).Right, "no aisle between seats 5 and 6");
        Assert.AreEqual(gap + aisle, c7.Left - c6.Right, "second aisle");
        Assert.AreEqual(seat, c3.Width);
    }

    [TestMethod]
    public void HitTesting_FindsSeatsOnBothSidesOfAnAisle_AndNothingInsideIt()
    {
        using SeatMap map = Map(new CabinLayout("Economy", 2, new[] { 2, 4, 2 }));
        Rectangle left = map.SeatBounds(1, 1), right = map.SeatBounds(1, 2);

        Assert.AreEqual((1, 1), map.SeatAt(new Point(left.Left + left.Width / 2, left.Top + left.Height / 2)));
        Assert.AreEqual((1, 2), map.SeatAt(new Point(right.Left + right.Width / 2, right.Top + right.Height / 2)));
        Assert.AreEqual((-1, -1), map.SeatAt(new Point((left.Right + right.Left) / 2, left.Top + left.Height / 2)));
    }

    [TestMethod]
    public void SingleAisleCabins_LookExactlyAsBefore()
    {
        using SeatMap map = Map(new CabinLayout("Economy", 5, new[] { 3, 3 }));
        int aisle = Gfx.S(map, 30), seat = Gfx.S(map, 38), gap = Gfx.S(map, 6), pad = Gfx.S(map, 8), label = Gfx.S(map, 34);
        Assert.AreEqual(pad * 2 + label * 2 + 6 * seat + 5 * gap + aisle, map.Width);
    }

    [TestMethod]
    public void Configure_SetsHeightFromRows()
    {
        using SeatMap map = Map(new CabinLayout("Business", 6, new[] { 2, 2, 2 }));
        int header = Gfx.S(map, 32), pad = Gfx.S(map, 8), seat = Gfx.S(map, 34), gap = Gfx.S(map, 6);
        Assert.AreEqual(header + pad * 2 + 6 * (seat + gap), map.Height);
    }
}
