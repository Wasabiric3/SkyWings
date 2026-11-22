using SkyWings.Controllers;
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

    // ------------------------------------------------------------------ step 3: class and extras

    private static void ClickCard(ChoiceCard card)
        => typeof(Control).GetMethod("OnClick", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(card, new object[] { EventArgs.Empty });

    [TestMethod]
    public void Options_ClickingACard_SelectsThatClass_AndRefillsBaggageAndMeal() => WizardHarness.OnStaThread(() =>
    {
        using var wizard = new WizardHarness();
        wizard.AddTraveller();
        wizard.GoTo(2);
        var cards = wizard.Field<ChoiceCard[]>("_classCards");
        Assert.AreEqual(4, cards.Length);
        int index = Array.FindIndex(cards, c => c.Enabled && !c.Selected);
        Assert.IsGreaterThanOrEqualTo(0, index, "another cabin is offered");
        ClickCard(cards[index]);
        string className = AppData.ClassNames[index];
        Assert.AreEqual(className, wizard.Field<string>("_selectedClass"));
        Assert.IsTrue(cards[index].Selected);
        CollectionAssert.AreEqual(AppData.BaggageOptionsForClass(className).ToArray(), wizard.Field<ComboBox>("_baggage").Items.Cast<object>().Select(o => o.ToString()).ToArray());
    });

    [TestMethod]
    public void Options_ACabinTheAircraftDoesNotHave_IsDisabled_WithNotOfferedText() => WizardHarness.OnStaThread(() =>
    {
        using var wizard = new WizardHarness();
        // The A320 has only Business and Economy cabins.
        var flight = (Flight)wizard.Field<ComboBox>("_flight").SelectedItem!;
        wizard.Store.SetFlightAircraft(flight.FlightNo, "A320");
        wizard.Call("SyncSession");
        var cards = wizard.Field<ChoiceCard[]>("_classCards");
        for (int i = 0; i < cards.Length; i++)
        {
            bool offered = AppData.ClassNames[i] is "Business" or "Economy";
            Assert.AreEqual(offered, cards[i].Enabled, AppData.ClassNames[i]);
            if (!offered)
            {
                Assert.AreEqual("Not offered on this aircraft", cards[i].Subtitle);
                Assert.IsFalse(cards[i].Selected, "a cabin that is not offered is never the selected one");
            }
        }
    });

    [TestMethod]
    public void Options_FieldsAreRounded_AndAddOnsAreRoundedCheckBoxes() => WizardHarness.OnStaThread(() =>
    {
        using var wizard = new WizardHarness();
        foreach (string name in new[] { "_baggageRow", "_mealRow", "_promoRow" })
            Assert.IsNotNull(All(wizard.Field<FieldRow>(name)).OfType<RoundedInput>().SingleOrDefault(), name + " is a rounded field");
        var boxes = wizard.Field<List<CheckBox>>("_addonBoxes");
        Assert.IsNotEmpty(boxes);
        Assert.IsTrue(boxes.All(b => b is GlassCheckBox));
    });

    [TestMethod]
    public void Options_ABadPromoCode_ShowsAMessage_WithARedOutline() => WizardHarness.OnStaThread(() =>
    {
        using var wizard = new WizardHarness();
        wizard.AddTraveller();
        wizard.GoTo(2);
        wizard.Field<TextBox>("_promoCode").Text = "NOT-A-CODE";
        All(wizard.Form).OfType<GlassButton>().Single(b => b.Text == "Apply").PerformClick();
        var row = wizard.Field<FieldRow>("_promoRow");
        Assert.IsFalse(string.IsNullOrEmpty(row.Message));
        Assert.IsTrue(All(row).OfType<RoundedInput>().Single().HasError);
    });

    [TestMethod]
    public void Options_TheCardsShowThePrice_AndTheHintSaysItIsPerTraveller() => WizardHarness.OnStaThread(() =>
    {
        using var wizard = new WizardHarness();
        wizard.GoTo(2);
        var cards = wizard.Field<ChoiceCard[]>("_classCards");
        for (int i = 0; i < cards.Length; i++)
        {
            if (!cards[i].Enabled) continue;
            StringAssert.StartsWith(cards[i].Subtitle, "PHP ");
            Assert.IsFalse(cards[i].Detail.EndsWith("…"), "the detail is short enough for the card");
            Assert.IsLessThanOrEqualTo(24, cards[i].Detail.Length, $"{AppData.ClassNames[i]}: \"{cards[i].Detail}\" would be cut off");
        }
        StringAssert.Contains(wizard.Field<Label>("_pageHint").Text, "per traveller");
    });

    [TestMethod]
    public void Options_TheCabinCards_LineUpWithTheCardBelow() => WizardHarness.OnStaThread(() =>
    {
        using var wizard = new WizardHarness();
        wizard.GoTo(2);
        var cards = wizard.Field<ChoiceCard[]>("_classCards");
        GlassCard below = All(wizard.Form).OfType<GlassCard>().First(c => c is not PriceSummaryPanel && c.Visible && c.Controls.Count > 0);
        // The white card draws its border ShadowSize inside its bounds; a cabin card draws 2px inside its own.
        int cardLeft = wizard.InForm(below).Left + Gfx.S(below, below.ShadowSize);
        Assert.AreEqual(cardLeft, wizard.InForm(cards[0]).Left + 2, 1, "left edges line up");
        int cardRight = wizard.InForm(below).Right - Gfx.S(below, below.ShadowSize);
        Assert.AreEqual(cardRight, wizard.InForm(cards[3]).Right - 3, 2, "right edges line up");
    });

    // ------------------------------------------------------------------ step 4: seats

    [TestMethod]
    public void Seats_ThePageShowsTheMapTheLegendAndTheSelectedSeats() => WizardHarness.OnStaThread(() =>
    {
        using var wizard = new WizardHarness();
        wizard.AddTraveller();
        wizard.Call("PrepareSeatStep");
        wizard.GoTo(3);
        Assert.AreEqual("Outbound: 0 of 1 selected", wizard.Field<Label>("_seatInfo").Text);
        Assert.IsTrue(All(wizard.Form).OfType<SeatLegend>().Any(l => l.Visible && l.Grid), "a grid legend is shown");
        var card = wizard.Field<GlassCard>("_seatCard");
        StringAssert.Contains(card.Heading, "·", "the card heading is the aircraft summary");
        Assert.IsTrue(wizard.Field<SeatMap>("_outboundMap").Visible);
    });

    [TestMethod]
    public void Seats_PickingASeat_UpdatesTheSelectedSeatsCard() => WizardHarness.OnStaThread(() =>
    {
        using var wizard = new WizardHarness();
        wizard.AddTraveller();
        wizard.Call("PrepareSeatStep");
        wizard.GoTo(3);
        var map = wizard.Field<SeatMap>("_outboundMap");
        map.SetSelected(new[] { SeatLayout.Code("Economy", 1, 'A') });
        StringAssert.StartsWith(wizard.Field<Label>("_seatInfo").Text, "Outbound: 1 of 1 selected");
    });

    [TestMethod]
    public void Seats_AtMinimumSize_TheFooterAndSummaryStillFit_OneWayAndRoundTrip() => WizardHarness.OnStaThread(() =>
    {
        foreach (bool roundTrip in new[] { false, true })
        {
            using var wizard = new WizardHarness();
            wizard.Form.Size = wizard.Form.MinimumSize;
            wizard.AddTraveller();
            wizard.Field<CheckBox>("_roundTrip").Checked = roundTrip;
            Application.DoEvents();
            wizard.Call("PrepareSeatStep");
            wizard.GoTo(3);
            Application.DoEvents();
            Rectangle client = wizard.Form.ClientRectangle;
            Assert.IsTrue(client.Contains(wizard.InForm(wizard.Field<GlassButton>("_next"))), $"round trip {roundTrip}: next button");
            Assert.IsTrue(client.Contains(wizard.InForm(wizard.Field<GlassButton>("_back"))), $"round trip {roundTrip}: back button");
            Assert.IsTrue(client.Contains(wizard.InForm(wizard.Field<PriceSummaryPanel>("_price"))), $"round trip {roundTrip}: summary");
            Assert.AreEqual(roundTrip, wizard.Field<FlowLayoutPanel>("_seatTabs").Visible, "the Outbound/Return tabs show only for a round trip");
        }
    });

    // ------------------------------------------------------------------ step 5: review and pay

    [TestMethod]
    public void Payment_ShowsTheDemoNotice_AndTwoHeadedCards() => WizardHarness.OnStaThread(() =>
    {
        using var wizard = new WizardHarness();
        wizard.AddTraveller();
        wizard.GoTo(4);
        InfoStrip notice = All(wizard.Form).OfType<InfoStrip>().Single(s => s.Visible && s.Text.Contains("DEMO ONLY"));
        Assert.AreEqual(Theme.AmberSoft.ToArgb(), notice.FillColor.ToArgb());
        var headings = All(wizard.Form).OfType<GlassCard>().Where(c => c.Visible).Select(c => c.Heading.ToUpperInvariant()).ToList();
        CollectionAssert.Contains(headings, "BOOKING SUMMARY");
        CollectionAssert.Contains(headings, "PAYMENT DETAILS");
    });

    [TestMethod]
    public void Payment_BookingSummary_ListsTheControllersRows() => WizardHarness.OnStaThread(() =>
    {
        using var wizard = new WizardHarness();
        wizard.AddTraveller("Brisias G. Labuson");
        wizard.GoTo(4);
        var list = wizard.Field<SummaryList>("_reviewList");
        CollectionAssert.AreEqual(wizard.Controller.ReviewRows().ToArray(), list.Rows.ToArray());
        Assert.IsTrue(list.Rows.Any(r => r.Value == "Brisias G. Labuson"));
    });

    [TestMethod]
    public void Payment_ShortAccount_ShowsTheMessage_WithARedOutline() => WizardHarness.OnStaThread(() =>
    {
        using var wizard = new WizardHarness();
        wizard.AddTraveller();
        wizard.Call("PrepareSeatStep");
        Assert.AreEqual(SeatHoldOutcome.Held, wizard.Controller.HoldSeats(new[] { SeatLayout.Code("Economy", 1, 'A') }, Array.Empty<string>()).Outcome);
        wizard.GoTo(4);
        wizard.Field<TextBox>("_account").Text = "12";
        Assert.IsFalse((bool)typeof(BookingWizardForm).GetMethod("ConfirmBooking", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(wizard.Form, null)!);
        var row = wizard.Field<FieldRow>("_accountRow");
        Assert.AreEqual("Enter at least 4 characters (demo account).", row.Message);
        Assert.IsTrue(All(row).OfType<RoundedInput>().Single().HasError);
    });

    [TestMethod]
    public void Payment_FieldsAreRounded_AndTheCardNumberStaysMasked() => WizardHarness.OnStaThread(() =>
    {
        using var wizard = new WizardHarness();
        foreach (string name in new[] { "_methodRow", "_accountRow" })
            Assert.IsNotNull(All(wizard.Field<FieldRow>(name)).OfType<RoundedInput>().SingleOrDefault(), name + " is a rounded field");
        Assert.IsTrue(wizard.Field<TextBox>("_account").UseSystemPasswordChar, "card digits are not readable over a shoulder");
    });

    // ------------------------------------------------------------------ step 6: receipt

    [TestMethod]
    public void Receipt_ShowsTheRealTotalInABand_OnADeepGreenHeader() => WizardHarness.OnStaThread(() =>
    {
        using var wizard = new WizardHarness();
        wizard.AddTraveller();
        wizard.Call("PrepareSeatStep");
        Assert.AreEqual(SeatHoldOutcome.Held, wizard.Controller.HoldSeats(new[] { SeatLayout.Code("Economy", 1, 'A') }, Array.Empty<string>()).Outcome);
        SubmitResult submit = wizard.Controller.SubmitBooking("Credit Card", "4111111111111111");
        Assert.IsTrue(submit.Succeeded);
        typeof(BookingWizardForm).GetMethod("BuildReceipt", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .Invoke(wizard.Form, new object[] { submit.Booking! });
        wizard.GoTo(5);

        var receipt = wizard.Field<ReceiptPanel>("_receipt");
        Assert.AreEqual($"PHP {submit.Booking!.TotalAmount:N2}", receipt.TotalPaid, "the total comes from the stored booking");
        Assert.AreEqual(Theme.Brand.ToArgb(), receipt.BandColor.ToArgb());
        var rows = wizard.Field<List<(string Label, string Value)>>("_receiptRows");
        Assert.IsFalse(rows.Any(r => r.Label == "Total paid"), "the total is in the band, not in the grid");
        Assert.IsTrue(rows.Any(r => r.Label == "Payment"));
        Assert.IsTrue(wizard.Field<GlassButton>("_print").Visible);
        Assert.IsTrue(wizard.Form.ClientRectangle.Contains(wizard.InForm(wizard.Field<GlassButton>("_next"))));
        Assert.IsFalse(wizard.Field<Panel[]>("_pages")[5].VerticalScroll.Visible, "a one-traveller receipt fits the page at the default window size");
    });

    [TestMethod]
    public void Payment_EveryRowOfTheSummary_CanBeReached_ForABigRoundTripAtTheMinimumSize() => WizardHarness.OnStaThread(() =>
    {
        using var wizard = new WizardHarness();
        wizard.Form.Size = wizard.Form.MinimumSize;
        wizard.AddTraveller("Ana Example", "P1234567A");
        wizard.AddTraveller("Ben Example", "P2345678B");
        wizard.AddTraveller("Cara Example", "P3456789C");
        wizard.Field<CheckBox>("_roundTrip").Checked = true;
        Application.DoEvents();
        wizard.GoTo(4);
        Application.DoEvents();

        var list = wizard.Field<SummaryList>("_reviewList");
        Assert.AreEqual(wizard.Controller.ReviewRows().Count, list.Rows.Count);
        Assert.AreEqual(12, list.Rows.Count, "flight, departure, return, return departure, 3 travellers, cabin, baggage, meal, seats, return seats");
        Assert.AreEqual(list.Rows.Count * list.RowHeight, list.Height, "the list is as tall as all its rows");
        var host = (Panel)list.Parent!;
        Assert.IsTrue(host.AutoScroll, "what does not fit is reached by scrolling, not dropped");
        Assert.IsTrue(host.VerticalScroll.Visible, "at the minimum size the rows exceed the card, so it scrolls");
    });

    [TestMethod]
    public void Payment_AFewRows_FitWithoutScrolling_AtTheDefaultSize() => WizardHarness.OnStaThread(() =>
    {
        using var wizard = new WizardHarness();
        wizard.AddTraveller();
        wizard.GoTo(4);
        Application.DoEvents();
        var list = wizard.Field<SummaryList>("_reviewList");
        Assert.IsFalse(((Panel)list.Parent!).VerticalScroll.Visible);
    });
}
