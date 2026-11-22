# App-wide restyle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every form, dialog and admin tab in SkyWings looks like the restyled booking wizard.

**Architecture:** Make the wizard's look the default in the shared base (`Theme`, `GlassForm`, `GlassButton`, `FieldRow`, `GridStyle`), add two small shared pieces (`PageHeader`, `DialogFrame`), then finish the layout of each screen group by group. A generic snapshot/fit test harness renders every form so each group can be reviewed from images.

**Tech Stack:** WinForms on .NET 10, MSTest 4, SQLite. Build: `dotnet build`; test: `dotnet test` (full suite takes about 7.5 minutes; use `--filter` while iterating).

**Spec:** `docs/superpowers/specs/2026-11-22-app-wide-restyle-design.md` (also see `2026-11-22-wizard-redesign-design.md`, the look being extended). Use the `skywings-dev` skill for repo conventions.

## Global Constraints

- Colours, fonts, spacing come from `Theme` / `Colors` only; no hard-coded colours in forms.
- Segoe UI stays; native title bar with Mica stays; printed receipt and boarding pass are not touched.
- No change to business rules, data or controllers. Forms handle layout and events only.
- Existing tests keep passing. A test may change only when it asserts the old look itself (e.g. `ThemeTests` checking `BgMain` is `#F3F7F4`); say so in that commit.
- Every form keeps Enter / Esc buttons, Tab order and accessible names; nothing clipped at its minimum size.
- Work on branch `app-restyle`, small commits. Update `HelpForm` and `README.md` if anything they describe changes.
- **Review gate:** after Tasks 8, 12, 15 and 19 stop, tell the owner where the snapshot images are (`%TEMP%\skywings-snapshots`) and wait for their review before the next phase.

## Review Focus

1. Long text at the minimum window size (long passenger names, long route names, six-figure amounts) — nothing clipped or overlapping.
2. Enter and Esc still trigger the default and cancel buttons on every dialog after the buttons become pills / move into `DialogFrame`.
3. Tab order and `AccessibleName` of every field once `FieldRow` is rounded by default (including `ComboBox` and `DateTimePicker` rows).
4. Disabled fields and disabled buttons stay readable (contrast) in the new look.
5. Grids with many columns (Admin tabs, Data browser) still fit, sort by header and show status pills after the colour change.

---

## Phase 1 — shared base

### Task 1: Snapshot and fit harness for every form

**Files:**
- Create: `WinFormsApp3.Tests/FormHarness.cs`, `WinFormsApp3.Tests/AllFormsSnapshotTests.cs`

**Interfaces:**
- Produces: `internal static class FormHarness` with
  `static void OnStaThread(Action)` (reuse `WizardHarness.OnStaThread`'s pattern),
  `static Bitmap Snapshot(Form form)` (shown, `DoEvents`, `DrawToBitmap`),
  `static string SaveSnapshot(Form form, string name)` → writes `%TEMP%\skywings-snapshots\{name}.png`, returns the path,
  `static void AssertFits(Form form)`: for every visible leaf control (`Label`, `GlassButton`, `FieldRow`, `CheckBox`) not inside an `AutoScroll` parent, its bounds in form client coordinates lie inside the form's client rectangle; failure message names the control and its text.
  `static IEnumerable<(string Name, Func<Form> Create)> CustomerForms` and `StaffForms`: one entry per form listed in the spec, built on a temporary seeded `BookingStore` (copy the setup from `WizardHarness`); forms that need a booking or a flight take one created through the store.

- [ ] **Step 1:** Write `AllFormsSnapshotTests.EveryForm_RendersNonBlank_AtDefaultAndMinimumSize`: for each entry, create, snapshot at default size and at `MinimumSize`, assert more than 5 distinct sampled colours (as `WizardSnapshotTests` does), call `AssertFits` at both sizes, save PNGs named `{Name}-default` and `{Name}-min`.
- [ ] **Step 2:** Run `dotnet test --filter AllFormsSnapshotTests`. Forms that do not fit today are listed in a `KnownNotFitting` set in the test with a comment "fixed in Task N" (N is the task that restyles that form); the test must pass now and the set shrinks as tasks land. Expected: PASS.
- [ ] **Step 3:** Commit `Add a snapshot and fit harness for every form`.

### Task 2: Page colour is the default

**Files:**
- Modify: `WinFormsApp3/Theme.cs` (`BgMain`, `Apply`), `WinFormsApp3/UI/GlassForm.cs`, `WinFormsApp3.Tests/ThemeTests.cs`
- Test: `WinFormsApp3.Tests/ThemeTests.cs`

**Interfaces:** Produces: `Theme.BgMain` equals `Theme.PageBg`; `GlassForm.BackColor` and every `Form`/`TabPage` handed to `Theme.Apply` get `Theme.PageBg`.

- [ ] **Step 1:** In `ThemeTests`, change the `BgMain` assertion to `0xF6F5F0` and add `Apply_FormAndTabPage_GetThePageColour` and `GlassForm_DefaultsToThePageColour` (assert `BackColor == Theme.PageBg`). Commit message must say the old-colour assertion changed on purpose.
- [ ] **Step 2:** Run `--filter ThemeTests`; expect FAIL.
- [ ] **Step 3:** Set `BgMain = PageBg` in `Theme.cs` (declare `PageBg` first); `GlassForm` and `Apply` use `Theme.PageBg`.
- [ ] **Step 4:** Run `--filter "ThemeTests|PanelPaintTests|GlassCardTests"`; expect PASS. Fix any test that hard-codes the old page colour only if it asserts the old look.
- [ ] **Step 5:** Commit.

### Task 3: Pill buttons in the brand green are the default

**Files:**
- Modify: `WinFormsApp3/UI/GlassControls.cs` (`GlassButton`), `WinFormsApp3.Tests/GlassButtonTests.cs`

**Interfaces:** Produces: `GlassButton.Pill` defaults to `true` (property kept; `Pill = false` gives the old rounded rectangle); Primary fill is `Theme.Brand` (hover/pressed mixed with black as the pill case does now); Secondary uses `Theme.BrandSoft` fill with `Theme.Brand` text; Ghost hover uses `Theme.BrandSoft`; the focus ring uses `Theme.Brand`; Danger and Outline unchanged apart from the pill shape.

- [ ] **Step 1:** Add tests: `NewButton_IsAPill`, `NewPrimaryButton_FillsWithBrandGreen` (render to a bitmap, sample the centre-left pixel), `Secondary_UsesBrandSoftFill`, `PillFalse_KeepsTheOldShape` (corner pixel of the bitmap equals the surface colour for pill, and the fill colour for the old rounded rectangle at a point inside its corner radius but outside the pill's).
- [ ] **Step 2:** Run `--filter GlassButtonTests`; expect FAIL.
- [ ] **Step 3:** Implement in `GlassButton`: `_pill` default `true`, `[DefaultValue(true)]`, remove the non-pill primary branch's use as default; map Secondary/Ghost/focus colours as above.
- [ ] **Step 4:** Run `--filter "GlassButtonTests|WizardStepTests|StepBarTests"`; expect PASS.
- [ ] **Step 5:** Commit.

### Task 4: Rounded fields are the default

**Files:**
- Modify: `WinFormsApp3/UI/GlassControls.cs` (`FieldRow` constructor), every `new FieldRow(..., rounded: true)` call (drop the now redundant argument), `WinFormsApp3.Tests/RoundedInputTests.cs`, layout tests that fail from the new field height

**Interfaces:** Produces: `FieldRow(string caption, Control input, bool rounded = true)`; passing `false` keeps the old square field.

- [ ] **Step 1:** Add `RoundedInputTests.FieldRow_IsRoundedByDefault` (the row contains a `RoundedInput` that hosts the input) and `FieldRow_RoundedFalse_KeepsTheNativeInput`, plus `RoundedByDefault_KeepsTabOrderAndAccessibleName` for a `TextBox`, `ComboBox` and `DateTimePicker` row (accessible name is the caption).
- [ ] **Step 2:** Run `--filter RoundedInputTests`; expect FAIL.
- [ ] **Step 3:** Change the default to `true`.
- [ ] **Step 4:** Run `--filter "ManageBookingFormLayoutTests|PanelPaintTests|WizardLayoutTests|RoundedInputTests|AllFormsSnapshotTests"`. Fix real layout problems (a row taller than before pushing a control off the form) in the form, not in the test. Expected: PASS.
- [ ] **Step 5:** Commit.

### Task 5: `PageHeader` control

**Files:**
- Create: `WinFormsApp3/UI/PageHeader.cs`, `WinFormsApp3.Tests/PageHeaderTests.cs`

**Interfaces:** Produces: `public sealed class PageHeader : Control, ISelfStyled` with `string Title`, `string Hint` (empty hides the hint line); `Dock = Top` by default; paints the title in `Theme.Title`/`Theme.Brand` and the hint in `Theme.Small`/`Theme.TextSecondary` on the parent's surface colour (`Gfx.Surface`); `GetPreferredSize` wraps the hint to the available width; `AccessibleName` is the title.

- [ ] **Step 1:** Tests: `Height_GrowsWhenTheHintWraps`, `EmptyHint_IsOneLine`, `Paints_OnTheParentSurface` (corner pixel equals the parent's `BackColor`), `AccessibleName_IsTheTitle`.
- [ ] **Step 2:** Run `--filter PageHeaderTests`; expect FAIL.
- [ ] **Step 3:** Implement `PageHeader` (self-painting like `InfoStrip`; owner height recomputed in `OnResize`).
- [ ] **Step 4:** Run `--filter PageHeaderTests`; expect PASS.
- [ ] **Step 5:** Commit.

### Task 6: Grids and the old-green audit

**Files:**
- Modify: `WinFormsApp3/UI/GridStyle.cs`, every file where `grep -rn "Theme\.\(Primary\|PrimaryLight\|Accent\|AccentDark\|StepActive\|StepDone\)" WinFormsApp3` finds a use (about 20), `WinFormsApp3.Tests/` tests that assert old colours

- [ ] **Step 1:** Add `GridStyleTests.Selection_UsesBrandSoft_AndHeaderUsesThePageSurface` and `StatusPills_Unchanged` (the existing `StatusStyle` results for `PAID`, `CANCELED`, `IN FLIGHT`).
- [ ] **Step 2:** Run it; expect FAIL on the first test.
- [ ] **Step 3:** In `GridStyle.Apply`, selection back is `Theme.BrandSoft`, header text `Theme.Brand`-tinted secondary as screenshots show best (builder's call within the tokens), grid lines `Theme.Border`. Then go through each grep hit: a use that marks "main action / selected / link" moves to `Brand` / `BrandSoft`; a use that marks status stays. Record the decision per file in the commit message.
- [ ] **Step 4:** Run `--filter "GridStyleTests|GridSorterTests|HomeHeaderTests|OtherTabsSortingTests|BookingsTabTests"`; expect PASS.
- [ ] **Step 5:** Commit.

### Task 7: `DialogFrame`

**Files:**
- Create: `WinFormsApp3/UI/DialogFrame.cs`, `WinFormsApp3.Tests/DialogFrameTests.cs`

**Interfaces:** Produces: `public sealed class DialogFrame : Panel` (docks fill) with `PageHeader Header`, `Panel Content` (fills the middle), `FlowLayoutPanel Buttons` (docked bottom, right-aligned, right-to-left so the first added button is the rightmost primary), `void Attach(Form form, GlassButton accept, GlassButton? cancel)` which sets `AcceptButton`/`CancelButton` and `Padding` from `Theme.Sp20`.

- [ ] **Step 1:** Tests: `Attach_SetsAcceptAndCancelButtons`, `Esc_ClosesWithCancel` (send `CancelButton.PerformClick()` result), `ButtonsAreRightAligned`, `ContentFillsBetweenHeaderAndButtons`, `TabOrder_ContentBeforeButtons`.
- [ ] **Step 2:** Run `--filter DialogFrameTests`; expect FAIL.
- [ ] **Step 3:** Implement.
- [ ] **Step 4:** Run `--filter DialogFrameTests`; expect PASS.
- [ ] **Step 5:** Commit.

### Task 8: Phase 1 gate

- [ ] **Step 1:** Run `dotnet build` (0 errors) and the full `dotnet test`; expect all pass (817 plus the new tests).
- [ ] **Step 2:** Run the snapshot test; list the saved images and any entries still in `KnownNotFitting`.
- [ ] **Step 3:** Commit anything left; tell the owner where the images are and what looks half-finished (expected: layouts not yet reworked). **Stop for review.**

---

## Phase 2 — customer screens

Each task below follows the same cycle: restyle the form with `PageHeader`, `GlassCard` sections, pill buttons in the wizard's footer pattern (secondary at the left, primary at the right); remove its entry from `KnownNotFitting`; add or adjust form-specific tests; run `--filter` for that form plus `AllFormsSnapshotTests`; commit. Existing behaviour tests for the form must keep passing unchanged.

### Task 9: Home
**Files:** Modify `WinFormsApp3/HomeForm.cs` (+ `HomeForm.Designer.cs` if the layout lives there), `WinFormsApp3.Tests/HomeHeaderTests.cs`.
- [ ] Restyle the header, booking entry, destination cards (`DestinationCard`), lookups and departures board; Home keeps all its current content. Test: `Home_FitsAtMinimumSize`, `Home_LookupButtonsStillOpenTheirForms` (the buttons' `Click` opens `CheckInForm` / `ManageBookingForm`, via the existing patterns in `HomeHeaderTests`).

### Task 10: Check-in and Manage booking
**Files:** Modify `CheckInForm.cs`, `ManageBookingForm.cs`, `ManageBookingFormLayoutTests.cs`.
- [ ] Page header replaces the centred heading on the lookup page; traveller list and footer actions in the footer pattern (the five-button check-in footer must still wrap). Tests: `CheckIn_FitsAtMinimumSize_WithFiveFooterButtons`, existing manage-booking layout test updated only if it asserts the old look.

### Task 11: Flight screens and dialogs
**Files:** Modify `FlightResultsForm.cs`, `FlightDetailsForm.cs`, `ChangeFlightForm.cs`, `SeatChangeForm.cs`, `BookingDetailForm.cs`, `BoardingPassForm.cs`; Test `ChangeFlightFormTests.cs`.
- [ ] Restyle each on `DialogFrame` or `PageHeader` as fits; `SeatChangeForm` keeps the restyled `SeatMap`. Tests: one `FitsAtMinimumSize` per form in `AllFormsSnapshotTests`; `ChangeFlightFormTests` pass unchanged.

### Task 12: Help and Policy, then phase gate
**Files:** Modify `HelpForm.cs`, `PolicyInfoForm.cs`; Test `HelpContentTests.cs`.
- [ ] Restyle (page header, cards, topic list selection in `BrandSoft`). `HelpContentTests` pass unchanged. Run the full suite; commit; snapshots to the owner. **Stop for review.**

---

## Phase 3 — staff and admin

### Task 13: Login, first-run, password, prompt
**Files:** Modify `StaffLoginForm.cs`, `FirstRunAdminForm.cs`, `ChangePasswordForm.cs`, `PromptForm.cs`, `StaffDialogs.cs`; Test `StaffSecurityTests.cs`, `DialogFrameTests.cs`.
- [ ] Move each onto `DialogFrame`; `StaffLoginForm` keeps the `LoginBrandPanel`. Tests: Enter accepts and Esc cancels on each (`Enter_And_Esc_StillWork_OnEveryStaffDialog`); security tests pass unchanged.

### Task 14: Waitlist dialogs, flight editor, other dialogs
**Files:** Modify `WaitlistForms.cs`, `FlightEditorForm.cs`, and any remaining dialog the Task 1 list shows still on old styling; Test `WaitlistEntryFormTests.cs`, `WaitlistStatusFormTests.cs`.
- [ ] Restyle on `DialogFrame`; existing waitlist tests pass unchanged; add `FitsAtMinimumSize` entries.

### Task 15: Admin console frame, then gate
**Files:** Modify `AdminForm.cs` (navigation, `Toolbar()`, `ToolButton`, `Section`), `UI/StatTile.cs`; Test `AllFormsSnapshotTests`.
- [ ] Section navigation as pill buttons with the selected one in `Brand`; toolbars use pill buttons; stat tiles use the new tokens. Existing admin tests (`BookingsTabTests`, `OtherTabsSortingTests`, `RefundsTabTests`, `StaffSecurityTests`) pass unchanged. Snapshot every tab (open each section through the same path the admin tests use). **Stop for review.**

### Task 16: Admin tabs
**Files:** Modify `AdminForm.cs` (Flights, Bookings, Staff, Waitlist, Audit, Reports, Simulator tabs).
- [ ] Per tab: page header text, filter row on rounded fields, toolbar order unchanged, grid via the new `GridStyle`. Each tab gets a `FitsAtMinimumSize` snapshot entry; sorting and the remembered sort settings keep working (`OtherTabsSortingTests` unchanged).

### Task 17: Refunds and the data browser
**Files:** Modify the Refunds section of `AdminForm.cs`, `UI/DatabaseBrowserPage.cs`; Test `RefundsTabTests.cs`, `DataBrowserTests.cs`.
- [ ] Restyle; tests pass unchanged; add `FitsAtMinimumSize` entries.

### Task 18: Help, README and the fit sweep
**Files:** Modify `HelpForm.cs` text only if it describes a look that changed, `README.md`, `AllFormsSnapshotTests.cs`.
- [ ] Assert `KnownNotFitting` is empty (delete the set). Update README's screenshots/description if it mentions the old look. Commit.

### Task 19: Final gate
- [ ] Full `dotnet build` and `dotnet test`; expect all pass. Snapshot every form once more and give the owner the folder. Ask for the final review before merging `app-restyle` into `main`. **Stop for review.**
