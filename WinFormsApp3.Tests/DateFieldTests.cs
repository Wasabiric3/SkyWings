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

    // ---- keyboard: the date can be changed and committed without the mouse ----

    private static void Key(DateField field, Keys key)
        => typeof(DateField).GetMethod("OnKeyDown", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(field, new object[] { new KeyEventArgs(key) });

    [TestMethod]
    public void UpAndDown_StepTheDateByADay_PageUpAndPageDownByAMonth()
    {
        using var field = new DateField { MinDate = new DateTime(2026, 1, 1), Value = new DateTime(2026, 11, 21) };
        int raised = 0;
        field.ValueChanged += (_, _) => raised++;
        Key(field, Keys.Up);
        Assert.AreEqual(new DateTime(2026, 11, 22), field.Value);
        Key(field, Keys.Down);
        Key(field, Keys.Down);
        Assert.AreEqual(new DateTime(2026, 11, 20), field.Value);
        Key(field, Keys.PageUp);
        Assert.AreEqual(new DateTime(2026, 12, 20), field.Value);
        Key(field, Keys.PageDown);
        Key(field, Keys.PageDown);
        Assert.AreEqual(new DateTime(2026, 10, 20), field.Value);
        Assert.AreEqual(6, raised, "each step is one change");
    }

    [TestMethod]
    public void Stepping_NeverGoesBeforeTheMinimumDate()
    {
        using var field = new DateField { MinDate = new DateTime(2026, 11, 21), Value = new DateTime(2026, 11, 21) };
        Key(field, Keys.Down);
        Key(field, Keys.PageDown);
        Assert.AreEqual(new DateTime(2026, 11, 21), field.Value);
    }

    [TestMethod]
    public void TheArrowKeys_AreInputKeys_SoTheFormDoesNotSwallowThem()
    {
        using var field = new DateField();
        var isInputKey = typeof(DateField).GetMethod("IsInputKey", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        foreach (Keys key in new[] { Keys.Up, Keys.Down, Keys.PageUp, Keys.PageDown, Keys.Space })
            Assert.IsTrue((bool)isInputKey.Invoke(field, new object[] { key })!, key.ToString());
    }

    [TestMethod]
    public void InTheOpenCalendar_EnterCommitsTheHighlightedDay_AndEscapeJustCloses() => WizardHarness.OnStaThread(() =>
    {
        using var form = new Form { Width = 400, Height = 300 };
        var field = new DateField { MinDate = new DateTime(2026, 1, 1), Value = new DateTime(2026, 11, 21), Left = 10, Top = 10, Width = 200 };
        form.Controls.Add(field);
        form.Show();
        Application.DoEvents();

        field.OpenDropDown();
        Assert.IsTrue(field.IsOpen);
        Assert.IsTrue(field.HandleCalendarKey(Keys.Escape, new DateTime(2026, 12, 25)));
        Application.DoEvents();
        Assert.IsFalse(field.IsOpen);
        Assert.AreEqual(new DateTime(2026, 11, 21), field.Value, "Escape does not change the date");

        field.OpenDropDown();
        Assert.IsTrue(field.HandleCalendarKey(Keys.Enter, new DateTime(2026, 12, 25)));
        Application.DoEvents();
        Assert.IsFalse(field.IsOpen);
        Assert.AreEqual(new DateTime(2026, 12, 25), field.Value, "Enter commits the highlighted day");
        Assert.IsFalse(field.HandleCalendarKey(Keys.A, DateTime.Today), "other keys are left to the calendar");
    });

    [TestMethod]
    public void TheCalendarPopup_CanBeOpenedAndClosedRepeatedly_WithoutDisposingItWhileItIsStillHandlingAClick() => WizardHarness.OnStaThread(() =>
    {
        using var form = new Form { Width = 400, Height = 300 };
        var field = new DateField { Left = 10, Top = 10, Width = 200 };
        form.Controls.Add(field);
        form.Show();
        Application.DoEvents();
        for (int i = 0; i < 3; i++)
        {
            field.OpenDropDown();
            Assert.IsTrue(field.IsOpen);
            field.HandleCalendarKey(Keys.Enter, DateTime.Today.AddDays(i + 1));
            Application.DoEvents();   // the popup is disposed after the close has finished
            Assert.IsFalse(field.IsOpen);
        }
        Assert.AreEqual(DateTime.Today.AddDays(3), field.Value);
    });

    // ---- accessibility: a screen reader gets the date as the field's text ----

    [TestMethod]
    public void TheFieldsText_IsTheDisplayedDate_AndFollowsTheValueAndTheFormat()
    {
        using var field = new DateField { CustomFormat = "MMM d, yyyy", Value = new DateTime(2026, 11, 21) };
        Assert.AreEqual("Nov 21, 2026", field.Text);
        field.Value = new DateTime(2026, 11, 22);
        Assert.AreEqual("Nov 22, 2026", field.Text);
        field.CustomFormat = "yyyy-MM-dd";
        Assert.AreEqual("2026-11-22", field.Text);
    }
}
