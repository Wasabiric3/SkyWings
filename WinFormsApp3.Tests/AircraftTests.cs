using SkyWings;

namespace SkyWings.Tests;

[TestClass]
public class AircraftTests
{
    [TestMethod]
    public void Airbus320_Has180Seats_InBusinessAndEconomyOnly()
    {
        Aircraft a = Aircraft.Find("A320")!;
        Assert.AreEqual(180, a.TotalSeats);
        Assert.AreEqual(12, a.Cabin("Business")!.Seats);
        Assert.AreEqual(168, a.Cabin("Economy")!.Seats);
        Assert.IsNull(a.Cabin("First Class"), "a narrow-body has no First cabin");
        Assert.IsNull(a.Cabin("Premium Economy"));
    }

    [TestMethod]
    public void Airbus330_And_Boeing777_HaveTheDocumentedTotals()
    {
        Assert.AreEqual(224, Aircraft.Find("A330")!.TotalSeats);
        Aircraft b = Aircraft.Find("B777")!;
        Assert.AreEqual(314, b.TotalSeats);
        Assert.AreEqual(8, b.Cabin("First Class")!.Seats);
        Assert.AreEqual(230, b.Cabin("Economy")!.Seats);
    }

    [TestMethod]
    public void EveryAircraft_HasUniqueIdAndModel_AndOnlyKnownClasses()
    {
        Assert.AreEqual(Aircraft.All.Count, Aircraft.All.Select(a => a.Id).Distinct().Count());
        foreach (Aircraft a in Aircraft.All)
        {
            Assert.IsFalse(string.IsNullOrWhiteSpace(a.Model));
            foreach (CabinLayout c in a.Cabins) CollectionAssert.Contains(AppData.ClassNames, c.ClassName);
        }
    }

    [TestMethod]
    [DataRow(new[] { 3, 4, 3 }, "ABCDEFGHIJ", new[] { 2, 6 })]
    [DataRow(new[] { 2, 4, 2 }, "ABCDEFGH", new[] { 1, 5 })]
    [DataRow(new[] { 3, 3 }, "ABCDEF", new[] { 2 })]
    [DataRow(new[] { 1, 2, 1 }, "ABCD", new[] { 0, 2 })]
    [DataRow(new[] { 2, 2 }, "ABCD", new[] { 1 })]
    public void Groups_DeriveLettersAndAisles(int[] groups, string letters, int[] aisles)
    {
        var cabin = new CabinLayout("Economy", 10, groups);
        Assert.AreEqual(letters, new string(cabin.Letters));
        CollectionAssert.AreEqual(aisles, cabin.AislesAfter);
        Assert.AreEqual(groups.Sum(), cabin.SeatsPerRow);
        Assert.AreEqual(10 * groups.Sum(), cabin.Seats);
        Assert.AreEqual(string.Join("-", groups), cabin.LayoutText);
    }

    [TestMethod]
    public void Position_WithTwoAisles_MarksWindowAisleAndMiddle()
    {
        var cabin = new CabinLayout("Economy", 1, new[] { 3, 4, 3 });
        string[] expected = { "Window", "Middle", "Aisle", "Aisle", "Middle", "Middle", "Aisle", "Aisle", "Middle", "Window" };
        for (int c = 0; c < expected.Length; c++) Assert.AreEqual(expected[c], cabin.Position(c), $"column {c}");
    }

    [TestMethod]
    [DataRow("PR-101", "A320")]
    [DataRow("PR-121", "A320")]
    [DataRow("PR-511", "A320")]
    [DataRow("PR-205", "A330")]
    [DataRow("PR-605", "A330")]
    [DataRow("PR-301", "B777")]
    [DataRow("PR-405", "B777")]
    [DataRow("PR-701", "B777")]
    [DataRow("PR-805", "B777")]
    public void DefaultFor_FollowsTheRouteGroupOfTheFlightNumber(string flightNo, string expected)
        => Assert.AreEqual(expected, Aircraft.DefaultFor(flightNo, domestic: false).Id);

    [TestMethod]
    public void DefaultFor_UnknownFlightNumber_FallsBackOnTheDomesticFlag()
    {
        Assert.AreEqual("A320", Aircraft.DefaultFor("XX-9", domestic: true).Id);
        Assert.AreEqual("A330", Aircraft.DefaultFor("XX-9", domestic: false).Id);
    }

    [TestMethod]
    [DataRow(180)]
    [DataRow(220)]
    [DataRow(300)]
    [DataRow(1)]
    public void Legacy_ReproducesTheCurrentRatioLayoutExactly(int capacity)
    {
        Aircraft legacy = Aircraft.Legacy(capacity);
        foreach (string className in AppData.ClassNames)
        {
            CabinLayout cabin = legacy.Cabin(className)!;
            int index = SeatLayout.ClassIndex(className);
            Assert.AreEqual(SeatLayout.RowCount(className, capacity), cabin.Rows, className);
            CollectionAssert.AreEqual(AppData.ClassSeatLetters[index], cabin.Letters, className);
            CollectionAssert.AreEqual(new[] { AppData.ClassAisleAfter[index] }, cabin.AislesAfter, className);
            for (int c = 0; c < cabin.SeatsPerRow; c++)
                Assert.AreEqual(SeatLayout.Position(className, c), cabin.Position(c), $"{className} column {c}");
        }
    }

    [TestMethod]
    public void Find_UnknownOrEmpty_ReturnsNull()
    {
        Assert.IsNull(Aircraft.Find("nope"));
        Assert.IsNull(Aircraft.Find(""));
        Assert.IsNull(Aircraft.Find(null));
    }
}

[TestClass]
public class AircraftSeatExistenceTests
{
    [TestMethod]
    [DataRow("ECO1A", true)]
    [DataRow("ECO28F", true)]
    [DataRow("ECO-01A", true)]
    [DataRow("eco3b", true)]
    [DataRow("ECO29A", false)]     // the A320 has 28 economy rows
    [DataRow("ECO3G", false)]      // only letters A-F
    [DataRow("BIZ3D", true)]
    [DataRow("BIZ4A", false)]
    [DataRow("FST1A", false)]      // no First cabin on an A320
    [DataRow("PRE1A", false)]
    [DataRow("ECO0A", false)]
    [DataRow("", false)]
    [DataRow("XYZ", false)]
    public void Airbus320_HasSeat(string code, bool expected) => Assert.AreEqual(expected, Aircraft.A320.HasSeat(code));

    [TestMethod]
    public void Boeing777_HasItsFirstAndWideEconomySeats()
    {
        Assert.IsTrue(Aircraft.B777.HasSeat("FST2D"));
        Assert.IsFalse(Aircraft.B777.HasSeat("FST3A"));
        Assert.IsTrue(Aircraft.B777.HasSeat("ECO23J"));
        Assert.IsFalse(Aircraft.B777.HasSeat("ECO23K"));
    }

    [TestMethod]
    public void EveryGeneratedSeatCode_ExistsOnItsOwnAircraft()
    {
        foreach (Aircraft a in Aircraft.All)
            foreach (CabinLayout cabin in a.Cabins)
                for (int row = 1; row <= cabin.Rows; row++)
                    foreach (char letter in cabin.Letters)
                        Assert.IsTrue(a.HasSeat(cabin.Code(row, letter)), $"{a.Id} {cabin.Code(row, letter)}");
    }
}
