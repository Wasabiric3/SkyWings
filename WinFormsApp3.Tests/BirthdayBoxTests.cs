using SkyWings.UI;

namespace SkyWings.Tests;

[TestClass]
public class BirthdayBoxTests
{
    [TestMethod]
    [DataRow("", "")]
    [DataRow("1", "1")]
    [DataRow("1990", "1990")]               // the dash waits for the next digit, so Backspace can always delete it
    [DataRow("19900", "1990-0")]
    [DataRow("199004", "1990-04")]
    [DataRow("1990042", "1990-04-2")]
    [DataRow("19900423", "1990-04-23")]
    [DataRow("1990-04-23", "1990-04-23")]   // pasted with dashes
    [DataRow("1990/04/23", "1990-04-23")]
    [DataRow("23 Apr 1990", "2319-90")]     // not a date, but only digits get through
    [DataRow("199004239999", "1990-04-23")] // nothing past eight digits
    [DataRow("abcd", "")]
    public void FormatDigits_KeepsOnlyDigitsAndAddsTheDashes(string typed, string expected)
        => Assert.AreEqual(expected, BirthdayBox.FormatDigits(typed));

    [TestMethod]
    public void FormatDigits_NullIsEmpty() => Assert.AreEqual("", BirthdayBox.FormatDigits(null));

    [TestMethod]
    public void SettingTheText_FormatsIt()
    {
        using var box = new BirthdayBox { Text = "19900423" };
        Assert.AreEqual("1990-04-23", box.Text);
    }

    [TestMethod]
    public void TypedDigits_ProduceAnAcceptedBirthday()
    {
        using var box = new BirthdayBox { Text = "19900423" };
        Assert.IsTrue(AppData.TryParseBirthday(box.Text, out DateTime day));
        Assert.AreEqual(new DateTime(1990, 4, 23), day);
    }

    [TestMethod]
    public void Pick_WritesTheDayInTheBookingFormat()
    {
        using var box = new BirthdayBox();
        box.Pick(new DateTime(1985, 1, 9));
        Assert.AreEqual("1985-01-09", box.Text);
    }

    [TestMethod]
    public void Clear_EmptiesTheBox()
    {
        using var box = new BirthdayBox { Text = "19900423" };
        box.Clear();
        Assert.AreEqual("", box.Text);
    }
}
