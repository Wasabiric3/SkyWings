# Booking Wizard Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the six-step booking wizard look like the owner's Figma (phases 0 and 1 of the spec), without changing any booking behaviour.

**Architecture:** New look tokens go in `Theme`. Repeated pieces become opt-in additions to the existing `UI/` controls (so the other forms look unchanged until phase 2). Then each wizard step is rebuilt from those pieces inside `BookingWizardForm`. `BookingWizardController` and the booking rules are not touched, except one small read-only helper in Task 10.

**Tech Stack:** C# / .NET 10 Windows Forms, MSTest 4, SQLite (tests use a temp database).

**Spec:** `docs/superpowers/specs/2026-11-22-wizard-redesign-design.md`. Phase 2 (the other forms) gets its own plan after the owner has reviewed the finished wizard.

## Global Constraints

- Colours, fonts and spacing come from `Theme` and `UI/Colors.cs`; no hard-coded colours in forms. Sizes go through `Gfx.S(control, px)` so they scale with the display.
- Keep Segoe UI; do not bundle Inter. Keep the native window title bar.
- Prefer shared controls in `UI/`; form code handles layout and events; logic stays in `BookingWizardController`.
- Do not copy the Figma's sample numbers; every figure comes from the live quote (`FareQuote`, `BookingSession`).
- Existing guard tests must keep passing: `PanelPaintTests`, `GlassButtonTests`, `GlassCardTests`, `ManageBookingFormLayoutTests`, `RefundsTabTests`, `SeatLayoutTests`, `HelpContentTests`.
- Update `HelpForm` and `README.md` if anything they describe changes.
- Run `dotnet build SkyWings.slnx` and the relevant `dotnet test` after every task; report real output.
- Work on a branch named `wizard-redesign` (create it in Task 1; `main` is the default branch).

## Review Focus

Inputs and conditions the spec implies but does not spell out. Each has a test in the task named.

1. A long total, such as `PHP 1,234,567.89`, must fit the total box on one line (Task 5).
2. Keyboard only: Tab walks the fields in reading order, Space toggles check boxes and cabin cards, and the focus ring is visible (Tasks 2, 3, 7, 8).
3. Validation messages ("Select a flight to continue.", "Add at least one traveller to continue.", promo errors) still show, with a red outline, now that fields are rounded (Tasks 2, 7).
4. A cabin the aircraft does not offer shows as a disabled card, and a round trip adds the return rows without pushing the footer off screen (Tasks 6, 8, 9).
5. At the minimum window size (960x580) every footer button and the summary card are fully inside the window on every step (Task 6, re-checked in 7 to 11).

---

### Task 1: Look tokens and pill buttons

**Files:**
- Modify: `WinFormsApp3/Theme.cs`, `WinFormsApp3/UI/GlassControls.cs` (`GlassButton`, `GlassButtonStyle`)
- Test: `WinFormsApp3.Tests/GlassButtonTests.cs`

**Interfaces:**
- Produces: `Theme.PageBg`, `Theme.Brand`, `Theme.BrandSoft`, `Theme.AmberSoft`, `Theme.AmberText`, `Theme.SeatHeld`, `Theme.SeatTaken` (all `Color`); `GlassButton.Pill` (`bool`); `GlassButtonStyle.Outline` (appended after `Danger`).

- [ ] **Step 1: Create the branch** — `git switch -c wizard-redesign`.
- [ ] **Step 2: Write failing tests** in `GlassButtonTests.cs`: `Brand_WhiteText_ContrastAtLeast4_5` (`Contrast.Ratio(Color.White, Theme.Brand) >= 4.5`); `Brand_OnBrandSoft_ContrastAtLeast4_5`; `PillPrimary_FillsWithBrand` (pixel at x=14, y=20 of a 120x40 pill button equals `Theme.Brand`); `Pill_CornerShowsSurface` (pixel at 2,2 of a pill button equals `Theme.BgMain`, which `Gfx.Surface` returns when there is no parent); `Outline_FillsWhite` (pixel at 14,20 is white).
- [ ] **Step 3: Run** `dotnet test SkyWings.slnx --filter FullyQualifiedName~GlassButtonTests`. Expected: build error or FAIL (members missing).
- [ ] **Step 4: Add the tokens to `Theme`.** Starting values, read from the screenshots; the owner confirms them at the Task 6 checkpoint: `PageBg #F6F5F0`, `Brand #1B4D3E`, `BrandSoft #D9E8DA`, `AmberSoft #FDF1DA`, `AmberText #7A5A1E`, `SeatHeld #E8C183`, `SeatTaken #C9C9C9`.
- [ ] **Step 5: Extend `GlassButton`.** `Pill` makes the corner radius `Height / 2`. With `Pill`, `Primary` fills with `Theme.Brand` (hover `Gfx.Mix(Brand, Color.Black, 0.15f)`, pressed 0.3). `Outline` fills white with a `Theme.Border` outline (hover: `Theme.Brand` outline) and `Theme.Brand` text, in both pill and normal shape. Disabled and focus-ring behaviour stay as they are. Non-pill buttons must paint exactly as before.
- [ ] **Step 6: Run** the filter again, then `dotnet build SkyWings.slnx`. Expected: PASS, 0 errors.
- [ ] **Step 7: Commit** — `git add -A && git commit -m "Add look tokens and pill buttons"`.

### Task 2: Rounded input fields (the risky piece)

**Files:**
- Create: `WinFormsApp3/UI/RoundedInput.cs`, `WinFormsApp3.Tests/RoundedInputTests.cs`
- Modify: `WinFormsApp3/UI/GlassControls.cs` (`FieldRow`)

**Interfaces:**
- Produces: `internal sealed class RoundedInput : Control, ISelfStyled` with `RoundedInput(Control input)`, `Control Input { get; }`, `bool HasError { get; set; }`, `bool FocusVisible { get; }` (true while `Input.ContainsFocus`). `FieldRow(string caption, Control input, bool rounded = false)`; with `rounded: true` the input sits inside a `RoundedInput`, `FieldRow.Input` still returns the original control, and `SetMessage` drives `HasError`.

- [ ] **Step 1: Write failing tests** in `RoundedInputTests.cs` (STA thread helper as in `RefundsTabTests`):
  - `TextBox_IsBorderless_AndCornerShowsSurface` — hosted `TextBox` has `BorderStyle.None`; `DrawToBitmap` pixel at 1,1 is the surface colour, the centre-left interior pixel is white.
  - `ComboBox_And_DateTimePicker_NativeBorderIsClipped` — same corner check for both; the host places the native control 2 px up and left and 4 px larger so the host clips the native border.
  - `ErrorMessage_DrawsRedOutline` — `new FieldRow("NAME", new TextBox(), rounded: true)`, `SetMessage("Required")`, render; some pixel in the top edge band equals `Theme.DangerRed`.
  - `Focus_ShowsRing_AndTabGoesStraightToInput` — in a shown form with two rounded rows, `Focus()` on the first input gives `FocusVisible == true`; the host itself has `TabStop == false`; `SelectNextControl` moves from the first input to the second.
  - `RoundedRow_KeepsInputInstance_AndMessage` — `row.Input` is the same instance passed in; `row.Message` equals the text passed to `SetMessage`.
- [ ] **Step 2: Run** `--filter FullyQualifiedName~RoundedInputTests`. Expected: FAIL (types missing).
- [ ] **Step 3: Implement `RoundedInput`** in `UI/RoundedInput.cs`. Fill white (`Theme.DisabledBack` when `Input` is disabled), 1 px `Theme.Border` outline, radius 12 (scaled), 2 px `Theme.Brand` outline while `FocusVisible`, `Theme.DangerRed` while `HasError`. Height = input height + 2 * 8 (scaled), at least 44 (scaled). Text boxes are made borderless and centred vertically; combo boxes and date pickers use the clipping offset from the test above. Repaint on the input's `Enter`, `Leave`, `EnabledChanged`.
- [ ] **Step 4: Extend `FieldRow`** with the `rounded` parameter. In rounded mode it skips its own square focus/error rectangle in `OnPaint`. Default stays `false`, so every other form is unchanged.
- [ ] **Step 5: Run** the filter, then the full `FieldRow` users' tests (`dotnet test SkyWings.slnx --filter "FullyQualifiedName~Panel|FullyQualifiedName~Layout|FullyQualifiedName~RefundsTab"`). Expected: PASS.
- [ ] **Step 6: Visual gate.** Render a rounded text box, combo box and date picker to PNG in the scratchpad and look at them. If a combo box or date picker still shows a square native border or arrow button outside the rounded shape, **stop and ask the owner** whether square-cornered drop-downs inside rounded cards are acceptable. Do not invent a fallback.
- [ ] **Step 7: Commit** — `git commit -am "Add rounded input fields (opt-in)"` (include the new files with `git add`).

### Task 3: Section card heading and the rounded check box

**Files:**
- Modify: `WinFormsApp3/UI/GlassControls.cs` (`GlassCard`)
- Create: `WinFormsApp3/UI/GlassCheckBox.cs`
- Test: `WinFormsApp3.Tests/GlassCardTests.cs`, `WinFormsApp3.Tests/GlassCheckBoxTests.cs`

**Interfaces:**
- Produces: `GlassCard.Heading` (`string`, default `""`). When not empty the card paints it as small bold capitals in `Theme.Brand` at the top-left inside the padding, and `DisplayRectangle` starts `Gfx.S(30)` lower. `GlassCheckBox : CheckBox, ISelfStyled`.

- [ ] **Step 1: Write failing tests.** `Card_WithHeading_DisplayRectangleIsShorterBy30Scaled`; `Card_WithoutHeading_DisplayRectangleUnchanged` (guards every existing card); `CheckBox_Checked_PaintsBrandBox`, `CheckBox_Unchecked_PaintsWhiteBox` (pixel at the box centre); `CheckBox_SpaceKey_Toggles` (invoke the key handler or `Checked` via `PerformClick`-style call; assert `Checked` flips); `CheckBox_Focused_DrawsFocusCue`.
- [ ] **Step 2: Run** both filters. Expected: FAIL.
- [ ] **Step 3: Implement.** `GlassCard.Heading` as above. `GlassCheckBox`: owner-painted 18 px (scaled) rounded square, `Theme.Brand` fill with a white tick when checked, `Theme.Border` outline otherwise, label in `Theme.TextPrimary`, `AutoSize` honoured, focus cue only when `ShowFocusCues`.
- [ ] **Step 4: Run** `--filter "FullyQualifiedName~GlassCard|FullyQualifiedName~GlassCheckBox|FullyQualifiedName~PanelPaint"`. Expected: PASS.
- [ ] **Step 5: Commit** — `Add section card heading and rounded check box`.

### Task 4: Pill step bar with the logo

**Files:**
- Modify: `WinFormsApp3/UI/GlassControls.cs` (`StepBar`, only the wizard uses it)
- Test: `WinFormsApp3.Tests/StepBarTests.cs`

**Interfaces:**
- Produces: `StepBar` keeps its constructor, `Current`, `Furthest` and `StepClicked`. Adds `bool ShowLogo { get; set; }`, `internal int HitStep(Point p)` (index of the pill under `p`, else -1) and `internal Rectangle PillBounds(int index)`.

- [ ] **Step 1: Write failing tests.** `HitStep_ReturnsPillUnderPoint_AndMinusOneInGaps`; `ClickOnEarlierStep_RaisesStepClicked` and `ClickOnCurrentOrLaterStep_DoesNothing` (call `OnMouseClick` through reflection with a `MouseEventArgs` at a pill centre, after setting `Current = 2`); `Pills_FitTheWidth_At900px` (`ShowLogo = true`, width 900: `PillBounds(5).Right <= Width`, and `PillBounds(0).Left` is right of the logo area).
- [ ] **Step 2: Run** `--filter FullyQualifiedName~StepBarTests`. Expected: FAIL.
- [ ] **Step 3: Rewrite the painting.** Band height 76 (scaled), 1 px `Theme.Border` line along the bottom. With `ShowLogo`, `Assets.Logo(LogoVariant.Green, DeviceDpi)` at the left, about 40 px tall, drawn through `BrandArt.DrawScaled` with a `BackdropCache` like `ReceiptPanel` does. Pills are 36 px tall and fully rounded, each as wide as its text plus 36 px, laid out left to right after the logo with 10 px gaps. Text is "1. Flight" and so on. Current: `Theme.Brand` fill, white bold text. Done: white fill, `Theme.Border` outline, `Theme.Brand` text. Pending: white fill, outline, `Theme.TextPrimary` text. Cursor and click rules are unchanged (only earlier, already visited steps are clickable). Dispose cached fonts as the current code does.
- [ ] **Step 4: Run** the filter. Expected: PASS.
- [ ] **Step 5: Commit** — `Restyle the step bar as pills with the logo`.

### Task 5: Trip summary card and total box

**Files:**
- Modify: `WinFormsApp3/UI/PriceSummaryPanel.cs`
- Test: `WinFormsApp3.Tests/PriceSummaryPanelTests.cs`

**Interfaces:**
- Produces: `PriceLineKind` gains `Hero` (label large and bold, value small and muted underneath) and `Stat` (label small and muted, value bold underneath). `internal static Font FitFont(string text, Font start, int maxWidth)` returns a font no larger than `start` and no smaller than 8 pt whose measured width fits `maxWidth`; the caller disposes it. `SetLines` and the other kinds keep working.

- [ ] **Step 1: Write failing tests.** `FitFont_ShortText_KeepsStartSize`; `FitFont_LongAmount_FitsTheWidth` (`"PHP 1,234,567.89"` in a 150 px box: measured width `<= 150`); `Total_LongAmount_PaintsInsideTheBox` (render a 300 px panel with a `Total` line of `PHP 1,234,567.89`; no pixel that differs from the card colour lies right of the card's inner edge); `Hero_And_Stat_Paint_WithoutThrowing`. The existing `StatTile_And_PriceSummary_PaintOnCardColour` must still pass.
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~PriceSummaryPanelTests|FullyQualifiedName~PanelPaint"`. Expected: FAIL.
- [ ] **Step 3: Implement.** Title painted as small bold `YOUR TRIP` in `Theme.Brand`; thin dividers between the trip block, the stat block and the fare rows; `Item` rows keep label left (muted) and value right (bold); `Total` becomes a `Theme.BrandSoft` rounded box with a small `TOTAL AMOUNT` caption and the amount in `FitFont` (start `Theme.Subtitle`). Keep `Heading`, `Discount`, `Note` painting working for callers.
- [ ] **Step 4: Run** the filter. Expected: PASS.
- [ ] **Step 5: Commit** — `Restyle the trip summary and total box`.

### Task 6: Wizard frame, window size and the test harness

**Files:**
- Modify: `WinFormsApp3/BookingWizardForm.cs` (`BuildFrame`, `GoTo`, `StepTitles`, `StepHints`, `RefreshPrice`, constructor sizing)
- Create: `WinFormsApp3.Tests/WizardHarness.cs`, `WinFormsApp3.Tests/WizardLayoutTests.cs`

**Interfaces:**
- Produces (tests): `static class WizardHarness { static BookingWizardForm Open(out BookingStore store, Size? size = null); static void GoTo(BookingWizardForm form, int step); static Bitmap Snapshot(BookingWizardForm form); static IEnumerable<Control> All(Control root); static T Field<T>(object owner, string name); }`. `Open` seeds a temp database as `RefundsTabTests` does and shows the form; `GoTo` calls the private `GoTo` by reflection; `Snapshot` uses `DrawToBitmap`.

- [ ] **Step 1: Write the harness and failing tests** in `WizardLayoutTests.cs`: `EveryStep_FooterAndSummary_FitAtMinimumSize` (open at 960x580, for steps 0 to 4: `_back`/`_next` fully inside the client area, `_price` right edge inside the client area and not overlapping the page host); `Header_ShowsLogoAndSixPills`; `StepTexts_FollowTheFigma` (step 0 next button text is `Continue to Passengers →`, step 4 `Confirm and pay →`, step 5 `Done →`); `BackOnFirstStep_ClosesTheWizard` (the button is visible on step 0, its text is `← Back`); `Summary_ShowsRealFares_NotSampleNumbers` (add a traveller through `_controller`, assert the total line equals `Php(Session.Quote.Total)`).
- [ ] **Step 2: Run** `--filter FullyQualifiedName~WizardLayoutTests`. Expected: FAIL.
- [ ] **Step 3: Restructure the frame.** After `Theme.Apply(this)` set `BackColor = Theme.PageBg`. Remove the big `_card`; stack the frame as: `_stepBar` (`ShowLogo = true`) docked top; footer docked bottom with a 1 px divider above it and `Back` (`Outline`, pill) left, `Print receipt` (`Outline`, pill, receipt step only) beside it and the primary pill button right; `_price` docked right at 300 px (scaled) with 16 px gaps; the left column holds the page title (`Theme.Title`, `Theme.Brand`) and hint docked top, then the page host filling the rest. Footer texts: `← Back`, `← Back to <previous step>`, `Continue to <next step> →`, `Confirm and pay →`, `Done →`. Back on step 0 calls `Close()` (the existing `FormClosing` prompt only appears once travellers exist). Window: `SizeForScreen(1280, 800, 960, 580)`.
- [ ] **Step 4: Update titles and hints.** Figma wording for steps 1, 2, 3 and 6 (British "traveller"); keep the existing hints for steps 4 and 5 because they state rules (10-minute seat hold, demo payment). Title for step 6 is `You're all set!`.
- [ ] **Step 5: Rebuild `RefreshPrice`** from the new line kinds: a `Hero` for each leg (flight number, route), `Stat` lines for Departure, Travellers (`1 adult`-style count from `Session.Passengers`) and Cabin, then the existing fare lines and `Total`. Every number still comes from `s.Quote`.
- [ ] **Step 6: Run** the new tests, then `dotnet test SkyWings.slnx --filter "FullyQualifiedName~Wizard|FullyQualifiedName~Panel|FullyQualifiedName~Layout"`. Expected: PASS.
- [ ] **Step 7: Checkpoint.** Save `Snapshot` PNGs of steps 1 to 6 to `%TEMP%\skywings-wizard-snapshots` and show them to the owner next to the Figma. Ask them to confirm the colour values from Task 1.
- [ ] **Step 8: Commit** — `Restyle the wizard frame`.

### Task 7: Steps 1 and 2 (Flight, Passengers)

**Files:**
- Modify: `WinFormsApp3/BookingWizardForm.cs` (`BuildFlightPage`, `BuildPassengerPage`, `DrawFlightItem`, `RefreshPassengerList`)
- Create: `WinFormsApp3/UI/InfoStrip.cs`
- Test: `WinFormsApp3.Tests/WizardStepTests.cs`

**Interfaces:**
- Consumes: `FieldRow(caption, input, rounded: true)`, `GlassCard.Heading`, `GlassCheckBox`, pill `GlassButton` (Tasks 1 to 3), `WizardHarness` (Task 6).
- Produces: `InfoStrip : Control, ISelfStyled` with `string Text` (rounded `Theme.BrandSoft` strip, one line, ellipsis).

- [ ] **Step 1: Write failing tests** in `WizardStepTests.cs`: `Flight_HasNoFromToFields` (no `FieldRow` with caption `FROM` or `TO`); `Flight_ReturnRowsAppear_OnlyWhenRoundTripIsTicked` and the footer still fits at 960x580 with it ticked; `Flight_NoFlightSelected_ShowsMessageWithRedOutline` (clear the selection, press next, `_flightRow.Message` is `Select a flight to continue.` and the host `HasError` is true); `Passengers_TabOrder_IsReadingOrder` (start in `_name`, `SelectNextControl` five times, `ActiveControl` visits `_idNumber`, `_birthday`, `_contact`, `_address`, `_email`); `Passengers_EmptyAdd_ShowsMessageOnName`; `Passengers_AddedTraveller_AppearsInTheList_AndCountInHeading` (heading text contains `(1)`).
- [ ] **Step 2: Run** `--filter FullyQualifiedName~WizardStepTests`. Expected: FAIL.
- [ ] **Step 3: Implement Step 1.** One `GlassCard` with the flight `FieldRow` (rounded), the `InfoStrip` showing the existing `_controller.DescribeFlight` text, departure and return date rows side by side, the `Add a return flight` `GlassCheckBox`, and the return-flight row when ticked. All existing events and `SyncSession` calls stay.
- [ ] **Step 4: Implement Step 2.** One `GlassCard` with the six fields in the existing two-column order (rounded rows), `Senior citizen (60+)` and `Person with disability (PWD)` as `GlassCheckBox`, a `TRAVELLERS (n)` caption, and a list of rows styled as soft-green strips (`01  Name  ·  Adult  ·  ID`) with the `Add traveller` pill button at the right and `Remove selected` as a ghost button. Enter on this step still adds the traveller.
- [ ] **Step 5: Run** the filter, plus `WizardLayoutTests`. Expected: PASS.
- [ ] **Step 6: Checkpoint** — snapshots of steps 1 and 2 for the owner.
- [ ] **Step 7: Commit** — `Restyle wizard steps 1 and 2`.

### Task 8: Step 3 (Class and extras)

**Files:**
- Modify: `WinFormsApp3/UI/GlassControls.cs` (`ChoiceCard`, only the wizard uses it), `WinFormsApp3/BookingWizardForm.cs` (`BuildOptionsPage`, `UpdateClassCards`)
- Test: `WinFormsApp3.Tests/WizardStepTests.cs`, `WinFormsApp3.Tests/ChoiceCardTests.cs`

**Interfaces:**
- Produces: `ChoiceCard` keeps `Title`, `Subtitle`, `Detail`, `Selected`, `Enabled`. New `internal Rectangle TickBounds` (the circle shown when selected).

- [ ] **Step 1: Write failing tests.** `Card_Selected_FillsBrandSoft_AndDrawsBrandTick` (pixel at the tick centre equals `Theme.Brand`); `Card_Disabled_FillsDisabledBack`; `Card_Space_Selects` (key handler raises `Click`); `Options_FourCards_ClickSelectsClass` (harness: click the Business card, `_selectedClass` is `Business`, baggage and meal lists refill); `Options_UnavailableCabin_IsDisabled_WithNotOfferedText` (call `UpdateClassCards` with a session whose `_controller.AvailableClasses` omits a class; the card is disabled and its subtitle is `Not offered on this aircraft`); `Options_PromoError_ShowsMessage` (apply a bad code, `_promoRow.Message` non-empty).
- [ ] **Step 2: Run** both filters. Expected: FAIL.
- [ ] **Step 3: Restyle `ChoiceCard`.** Card about 110 px tall: class name in bold capitals, price (`Subtitle`) large, `Detail` small and muted. Selected: `Theme.BrandSoft` fill, 2 px `Theme.Brand` outline and a `Theme.Brand` tick circle at the top right. Disabled: `Theme.DisabledBack`. No artwork.
- [ ] **Step 4: Rebuild the page.** Four cards in a row; below, one `GlassCard` with baggage and meal (rounded rows), add-ons as `GlassCheckBox` (existing `_addonBoxes` and prices), and the promo row with a pill `Apply` button. All events unchanged.
- [ ] **Step 5: Run** the filters plus `WizardLayoutTests`. Expected: PASS.
- [ ] **Step 6: Checkpoint and commit** — snapshot for the owner; `git commit -am "Restyle wizard step 3"`.

### Task 9: Step 4 (Seats)

**Files:**
- Modify: `WinFormsApp3/UI/SeatPalette.cs`, `WinFormsApp3/UI/SeatMap.cs` (`SeatLegend`, selected-seat label), `WinFormsApp3/BookingWizardForm.cs` (`BuildSeatPage`, `UpdateSeatInfo`)
- Test: `WinFormsApp3.Tests/SeatPaletteTests.cs`, `WinFormsApp3.Tests/WizardStepTests.cs`

**Interfaces:**
- Consumes: `Theme.BrandSoft`, `Theme.Brand`, `Theme.SeatHeld`, `Theme.SeatTaken`. `SeatPalette.For(SeatState, bool)` keeps its signature. `SeatMap` and `SeatLegend` are also used by `SeatChangeForm`, so that screen gets the new colours too (intended: one seat look).

- [ ] **Step 1: Write failing tests.** `Palette_Available_IsBrandSoft`, `Palette_Selected_IsBrand`, `Palette_Held_IsSeatHeld`, `Palette_Taken_IsSeatTaken`; `Palette_TextContrast_AtLeast3ForEveryState` (the existing readability rule in `SeatPalette`'s comment); `SeatMap_SelectedSeat_LabelHasTick` (selected seat label ends with `✓`); `SeatLayoutTests` still pass; `Seats_PageShowsMapLegendAndSelectedSeats` (harness: add a traveller, reach step 4, `_seatInfo` text reads `Outbound: 0 of 1 selected`, a `SeatLegend` is present).
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~SeatPalette|FullyQualifiedName~WizardStepTests"`. Expected: FAIL.
- [ ] **Step 3: Implement.** New palette values; selected seat shows `<code> ✓`. `SeatLegend` becomes a 2x2 grid (Available, Selected, Held, Taken) inside a white card.
- [ ] **Step 4: Rebuild the seat page.** Left: a `GlassCard` whose `Heading` is the existing `_seatAircraft` summary (uppercased) containing the existing `SeatMap` (all aircraft layouts keep working) and the Outbound/Return tabs when a return flight exists. Right (about 280 px, scaled): a card titled `Selected seats` showing the existing `_seatInfo` text, and the legend card. No per-traveller assignment card (the app assigns seats in selection order); no cabin illustration.
- [ ] **Step 5: Run** the filter plus `SeatLayoutTests` and `WizardLayoutTests` (the map must stay scrollable inside its card at 960x580). Expected: PASS.
- [ ] **Step 6: Checkpoint and commit** — snapshot (one-way and round-trip); `git commit -am "Restyle wizard step 4 and seat colours"`.

### Task 10: Step 5 (Review and pay)

**Files:**
- Modify: `WinFormsApp3/Controllers/BookingWizardController.cs` (add `ReviewRows`, rebuild `BuildReviewText` from it), `WinFormsApp3/BookingWizardForm.cs` (`BuildPaymentPage`, `UpdateReview`)
- Test: `WinFormsApp3.Tests/BookingWizardControllerTests.cs` (find the existing file with `Grep BuildReviewText` first; create it if none), `WinFormsApp3.Tests/WizardStepTests.cs`

**Interfaces:**
- Produces: `IReadOnlyList<(string Label, string Value)> BookingWizardController.ReviewRows()` with rows `Flight`, `Departure`, `Return` (only on a round trip), `Traveller` (one per passenger: name and seat codes), `Cabin`, `Baggage`, `Meal`. `BuildReviewText()` returns the same text as today (the confirmation `MessageBox` uses it, so its output must not change).

- [ ] **Step 1: Write failing tests.** `ReviewRows_OneWay_HasFlightDepartureTravellerCabinBaggageMeal` (assert labels and the values for a seeded session); `ReviewRows_RoundTrip_AddsReturnRow`; `BuildReviewText_IsUnchanged` (a golden string captured from the current implementation before the change); `Payment_DemoNoticeAndTwoCardsArePresent` (harness: step 5 shows a notice containing `DEMO ONLY`, cards headed `BOOKING SUMMARY` and `PAYMENT DETAILS`); `Payment_ShortAccount_ShowsMessage` (account `12` then confirm: `_accountRow.Message` is `Enter at least 4 characters (demo account).`); `Payment_ConfirmButtonText` (`Confirm and pay →`).
- [ ] **Step 2: Run** the controller and step filters. Expected: FAIL.
- [ ] **Step 3: Implement `ReviewRows`** in the controller; keep the `BuildReviewText` output byte for byte.
- [ ] **Step 4: Rebuild the page.** An amber notice strip (`Theme.AmberSoft` fill, `Theme.AmberText` text): `DEMO ONLY — No real payment will be processed.` Below, two `GlassCard`s side by side: `BOOKING SUMMARY` (label/value rows from `ReviewRows`, long values ellipsised) and `PAYMENT DETAILS` (method and card or account as rounded rows, plus the existing demo note). No cardholder field: the app does not store one.
- [ ] **Step 5: Run** the filters plus `WizardLayoutTests`. Expected: PASS.
- [ ] **Step 6: Checkpoint and commit** — snapshot; `git commit -am "Restyle wizard step 5"`.

### Task 11: Step 6 (Receipt) and wrap-up of phase 1

**Files:**
- Modify: `WinFormsApp3/UI/ReceiptPanel.cs`, `WinFormsApp3/BookingWizardForm.cs` (`BuildReceiptPage`, `BuildReceipt`), `WinFormsApp3/HelpForm.cs`, `README.md`
- Test: `WinFormsApp3.Tests/PanelPaintTests.cs`, `WinFormsApp3.Tests/WizardStepTests.cs`, `WinFormsApp3.Tests/WizardSnapshotTests.cs`

**Interfaces:**
- Produces: `ReceiptPanel.BandColor` (`Color`, default `Theme.Primary`, so `BookingDetailForm` and the boarding pass look as before) and an optional `totalPaid` argument on `SetReceipt(string reference, IEnumerable<(string, string)> rows, string? totalPaid = null)`. When given, the `Total paid` row is left out of the grid and painted as a `Theme.BrandSoft` band at the bottom of the card with `TOTAL PAID` and the amount.

- [ ] **Step 1: Write failing tests.** `Receipt_BandColor_Default_IsPrimary` (guards the other screens); `Receipt_WithBrandBand_PaintsBrandInHeader`; `Receipt_TotalPaidBand_PaintsBrandSoft_AndAmountIsNotInGrid`; `Receipt_StepShowsRealTotal` (complete a booking through the harness with a temp database, assert the receipt total equals `booking.TotalAmount`); `WizardSnapshots_WriteSixNonBlankImages` (in `WizardSnapshotTests.cs`: for steps 1 to 6 render to `%TEMP%\skywings-wizard-snapshots\step<N>.png` and assert the bitmap is not a single colour).
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~PanelPaint|FullyQualifiedName~WizardStepTests|FullyQualifiedName~WizardSnapshot"`. Expected: FAIL.
- [ ] **Step 3: Implement `ReceiptPanel` changes.** The wizard sets `BandColor = Theme.Brand` and passes `totalPaid`. Keep the reference, Copy button and bar pattern in the right-hand stub (decision: they are useful and the Figma does not forbid them). Page title `You're all set!` with hint `Your booking is confirmed. Keep the receipt for your records.`; footer `Print receipt` and `Done →`.
- [ ] **Step 4: Update `HelpForm` and `README.md`** wherever they describe the booking steps, buttons or colours, then run `--filter FullyQualifiedName~HelpContent`.
- [ ] **Step 5: Full verification.** `dotnet build SkyWings.slnx` (0 errors) and `dotnet test SkyWings.slnx` (all pass; report the counts). Open the six snapshots and compare them with the Figma. Then, by hand, run the app at 100% and 150% display scaling and book a flight end to end, one-way and round trip.
- [ ] **Step 6: Commit** — `Restyle the wizard receipt; update help and README`.
- [ ] **Step 7: Hand over.** Tell the owner phase 1 is done, list anything that differs from the Figma on purpose, and ask whether to plan phase 2 (the other forms).

---

## Self-review notes

- **Spec coverage:** shared kit (Tasks 1 to 5), frame and window (6), the six steps (7 to 11), verification and owner checkpoints (6 to 11), docs (11). Phase 2 is deliberately a separate plan.
- **Known spec adjustments made here:** the Figma's "Selected passenger" card became "Selected seats" (no per-traveller assignment exists), no cardholder field (not stored), Back on step 1 closes the wizard (the Figma shows a Back button there), the receipt keeps the reference and Copy stub.
- **Names checked across tasks:** `Theme.Brand`, `BrandSoft`, `PageBg`, `FieldRow(..., rounded)`, `GlassCard.Heading`, `PriceLineKind.Hero/Stat`, `WizardHarness` members, `ReceiptPanel.BandColor`.
