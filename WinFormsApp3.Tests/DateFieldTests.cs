using SkyWings.UI;

namespace SkyWings.Tests;

/// <summary>The date field that replaces the native DateTimePicker in the rounded look (it can be as tall as other fields and has no square button).</summary>
[TestClass]
[DoNotParallelize]
public sealed class DateFieldTests
{
    [TestMethod]
    public void Value_BelowMinDate_ClampsToMinDate()
    {
        using var field = new DateField { MinDate = new DateTime(2030, 5, 10) };
        field.Value = new DateTime(2030, 5, 1);
        Assert.AreEqual(new DateTime(2030, 5, 10), field.Value);
    }

    [TestMethod]
    public void RaisingMinDate_PastTheValue_MovesTheValueUp_AndRaisesValueChanged()
    {
        using var field = new DateField { Value = new DateTime(2030, 5, 1) };
        int raised = 0;
        field.ValueChanged += (_, _) => raised++;
        field.MinDate = new DateTime(2030, 5, 3);
        Assert.AreEqual(new DateTime(2030, 5, 3), field.Value);
        Assert.AreEqual(1, raised);
    }

    [TestMethod]
    public void Pick_SetsValue_AndRaisesValueChangedOnce_AndSameDateRaisesNothing()
    {
        using var field = new DateField { Value = new DateTime(2030, 5, 1) };
        int raised = 0;
        field.ValueChanged += (_, _) => raised++;
        field.Pick(new DateTime(2030, 5, 9, 14, 30, 0));
        Assert.AreEqual(new DateTime(2030, 5, 9), field.Value, "the time of day is dropped");
        field.Pick(new DateTime(2030, 5, 9));
        Assert.AreEqual(1, raised);
    }

    [TestMethod]
    public void DisplayText_UsesTheFormat()
    {
        using var field = new DateField { CustomFormat = "MMM d, yyyy", Value = new DateTime(2026, 11, 21) };
        Assert.AreEqual("Nov 21, 2026", field.DisplayText);
    }

    [TestMethod]
    public void Disabled_DoesNotOpen()
    {
        using var field = new DateField { Enabled = false };
        field.OpenDropDown();
        Assert.IsFalse(field.IsOpen);
    }
}
