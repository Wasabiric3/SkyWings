using SkyWings.UI;

namespace SkyWings.Tests;

/// <summary>The restyled wizard steps, one group per task: the fields and cards are the new look and the existing behaviour still works.</summary>
[TestClass]
[DoNotParallelize]
public sealed class WizardStepTests
{
    private static IEnumerable<Control> All(Control root) => WizardHarness.All(root);

    // ------------------------------------------------------------------ step 1: flight

    [TestMethod]
    public void Flight_HasNoFromToFields_AndItsFieldsAreRounded() => WizardHarness.OnStaThread(() =>
    {
        using var wizard = new WizardHarness();
        wizard.GoTo(0);
        var captions = All(wizard.Form).OfType<FieldRow>().Select(r => r.Caption).ToList();
        CollectionAssert.DoesNotContain(captions, "FROM");
        CollectionAssert.DoesNotContain(captions, "TO");
        foreach (string name in new[] { "_flightRow", "_dateRow", "_returnDateRow", "_returnFlightRow" })
            Assert.IsNotNull(All(wizard.Field<FieldRow>(name)).OfType<RoundedInput>().SingleOrDefault(), name + " is a rounded field");
    });

    [TestMethod]
    public void Flight_Dates_AreDateFields_AndTheReturnDateFollowsTheDeparture() => WizardHarness.OnStaThread(() =>
    {
        using var wizard = new WizardHarness();
        var depart = wizard.Field<DateField>("_flightDate");
        var back = wizard.Field<DateField>("_returnDate");
        depart.Value = DateTime.Today.AddDays(10);
        Assert.AreEqual(DateTime.Today.AddDays(11), back.MinDate, "the return date cannot be before the day after departure");
        Assert.IsGreaterThanOrEqualTo(back.MinDate, back.Value);
    });

    [TestMethod]
    public void Flight_InfoStrip_ShowsTheFlightDescription_AndTheCheckBoxIsRounded() => WizardHarness.OnStaThread(() =>
    {
        using var wizard = new WizardHarness();
        var strip = wizard.Field<InfoStrip>("_flightInfo");
        Assert.IsFalse(string.IsNullOrWhiteSpace(strip.Text), "the first flight is selected, so its description is shown");
        Assert.IsInstanceOfType<GlassCheckBox>(wizard.Field<CheckBox>("_roundTrip"));
    });

    [TestMethod]
    public void Flight_ReturnRowsAppearOnlyWhenRoundTripIsTicked_AndTheFooterStillFitsAtMinimumSize() => WizardHarness.OnStaThread(() =>
    {
        using var wizard = new WizardHarness();
        wizard.Form.Size = wizard.Form.MinimumSize;
        var returnRow = wizard.Field<FieldRow>("_returnFlightRow");
        Assert.IsFalse(returnRow.Visible);
        wizard.Field<CheckBox>("_roundTrip").Checked = true;
        Application.DoEvents();
        Assert.IsTrue(returnRow.Visible);
        Assert.IsTrue(wizard.Form.ClientRectangle.Contains(wizard.InForm(wizard.Field<GlassButton>("_next"))), "the footer is not pushed off screen");
    });

    [TestMethod]
    public void Flight_NoFlightSelected_ShowsTheMessage_WithARedOutline() => WizardHarness.OnStaThread(() =>
    {
        using var wizard = new WizardHarness();
        wizard.Field<ComboBox>("_flight").SelectedIndex = -1;
        wizard.Field<GlassButton>("_next").PerformClick();
        var row = wizard.Field<FieldRow>("_flightRow");
        Assert.AreEqual("Select a flight to continue.", row.Message);
        Assert.IsTrue(All(row).OfType<RoundedInput>().Single().HasError);
    });

    // ------------------------------------------------------------------ step 2: passengers

    [TestMethod]
    public void Passengers_TabOrder_IsReadingOrder() => WizardHarness.OnStaThread(() =>
    {
        using var wizard = new WizardHarness();
        wizard.GoTo(1);
        var name = wizard.Field<TextBox>("_name");
        name.Focus();
        Application.DoEvents();
        string[] expected = { "_idNumber", "_birthday", "_contact", "_address", "_email" };
        foreach (string field in expected)
        {
            wizard.Form.SelectNextControl(wizard.Form.ActiveControl, true, true, true, true);
            Assert.AreSame(wizard.Field<TextBox>(field), wizard.Form.ActiveControl, "Tab should reach " + field);
        }
    });

    [TestMethod]
    public void Passengers_EmptyAdd_ShowsTheMessageOnName_WithARedOutline() => WizardHarness.OnStaThread(() =>
    {
        using var wizard = new WizardHarness();
        wizard.GoTo(1);
        wizard.Field<GlassButton>("_addTraveller").PerformClick();
        var row = wizard.Field<FieldRow>("_nameRow");
        Assert.IsFalse(string.IsNullOrEmpty(row.Message));
        Assert.IsTrue(All(row).OfType<RoundedInput>().Single().HasError);
    });

    [TestMethod]
    public void Passengers_AddingATraveller_ListsThemAndCountsThemInTheHeading() => WizardHarness.OnStaThread(() =>
    {
        using var wizard = new WizardHarness();
        wizard.GoTo(1);
        var heading = wizard.Field<Label>("_travellerHeading");
        Assert.AreEqual("TRAVELLERS (0)", heading.Text);
        wizard.Field<TextBox>("_name").Text = "Brisias G. Labuson";
        wizard.Field<TextBox>("_idNumber").Text = "P12345678";
        wizard.Field<TextBox>("_birthday").Text = "2006-02-13";
        wizard.Field<TextBox>("_contact").Text = "+63 917 123 4567";
        wizard.Field<TextBox>("_address").Text = "East Rembo, Makati City";
        wizard.Field<GlassButton>("_addTraveller").PerformClick();
        var list = wizard.Field<ListBox>("_passengerList");
        Assert.AreEqual(1, list.Items.Count);
        Assert.AreEqual("TRAVELLERS (1)", heading.Text);
        Assert.AreEqual(DrawMode.OwnerDrawFixed, list.DrawMode);
        StringAssert.Contains(list.Items[0].ToString(), "Brisias G. Labuson");
    });

    [TestMethod]
    public void Passengers_CheckBoxes_AreRounded_AndFieldsAreRounded() => WizardHarness.OnStaThread(() =>
    {
        using var wizard = new WizardHarness();
        wizard.GoTo(1);
        Assert.IsInstanceOfType<GlassCheckBox>(wizard.Field<CheckBox>("_senior"));
        Assert.IsInstanceOfType<GlassCheckBox>(wizard.Field<CheckBox>("_pwd"));
        foreach (string name in new[] { "_nameRow", "_idRow", "_birthdayRow", "_contactRow", "_addressRow", "_emailRow" })
            Assert.IsNotNull(All(wizard.Field<FieldRow>(name)).OfType<RoundedInput>().SingleOrDefault(), name + " is a rounded field");
    });
}
