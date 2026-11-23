namespace SkyWings.Tests;

[TestClass]
public class PhContactTests
{
    [TestMethod]
    [DataRow("+63 917 123 4567", "+63 917 123 4567")]
    [DataRow("0917 123 4567", "+63 917 123 4567")]
    [DataRow("09171234567", "+63 917 123 4567")]
    [DataRow("+639171234567", "+63 917 123 4567")]
    [DataRow("63-917-123-4567", "+63 917 123 4567")]
    [DataRow("(0917) 123-4567", "+63 917 123 4567")]
    [DataRow("02 8123 4567", "+63 281234567")]      // Metro Manila landline
    [DataRow("+63 32 234 5678", "+63 322345678")]   // Cebu landline
    public void AcceptsPhilippineNumbers_AndWritesThemWithTheCountryCode(string typed, string expected)
    {
        Assert.IsTrue(BookingRules.TryNormalizePhContact(typed, out string normalized), typed);
        Assert.AreEqual(expected, normalized);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("1")]
    [DataRow("0917")]                    // too short
    [DataRow("0917 123 45678")]          // too long
    [DataRow("0817 123 4567")]           // a mobile number starts with 9
    [DataRow("+1 415 555 0132")]         // not a Philippine number
    [DataRow("+63 0917 123 4567")]       // a leading 0 after the country code
    [DataRow("0917-123-45ab")]           // letters
    [DataRow("917 123 4567")]            // no 0 or +63 in front
    [DataRow("++63 917 123 4567")]
    public void RejectsEverythingElse(string typed)
    {
        Assert.IsFalse(BookingRules.TryNormalizePhContact(typed, out string normalized), typed);
        Assert.AreEqual("", normalized);
    }

    [TestMethod]
    public void NullIsRejected() => Assert.IsFalse(BookingRules.TryNormalizePhContact(null, out _));
}
