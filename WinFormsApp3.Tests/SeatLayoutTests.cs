using SkyWings;

namespace SkyWings.Tests;

[TestClass]
public sealed class SeatLayoutTests
{
    [TestMethod]
    [DataRow("ECO-01A", "ECO1A")]
    [DataRow("eco3b", "ECO3B")]
    [DataRow("BIZ-12C", "BIZ12C")]
    [DataRow(" PRE7D ", "PRE7D")]
    [DataRow("", "")]
    [DataRow(null, "")]
    public void Normalize_BringsSeedAndWizardFormatsTogether(string? input, string expected)
    {
        Assert.AreEqual(expected, SeatLayout.Normalize(input));
    }

    [TestMethod]
    public void RowCount_FollowsCabinShareOfCapacity()
    {
        // 180 seats: 60% economy in rows of 6, 5% first class in rows of 2.
        Assert.AreEqual(18, SeatLayout.RowCount("Economy", 180));
        Assert.AreEqual(5, SeatLayout.RowCount("First Class", 180));
        Assert.AreEqual(1, SeatLayout.RowCount("Business", 0));
    }

    [TestMethod]
    public void Code_UsesCabinPrefixAndSeatLetter()
    {
        Assert.AreEqual("ECO3B", SeatLayout.Code("Economy", 3, 'B'));
        Assert.AreEqual("FST1A", SeatLayout.Code("First Class", 1, 'A'));
    }

    [TestMethod]
    [DataRow("Economy", 0, "Window")]
    [DataRow("Economy", 1, "Middle")]
    [DataRow("Economy", 2, "Aisle")]
    [DataRow("Economy", 3, "Aisle")]
    [DataRow("Economy", 5, "Window")]
    [DataRow("Business", 1, "Aisle")]
    public void Position_DescribesWindowMiddleAndAisle(string className, int column, string expected)
    {
        Assert.AreEqual(expected, SeatLayout.Position(className, column));
    }
}
