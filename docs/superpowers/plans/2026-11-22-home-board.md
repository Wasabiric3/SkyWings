# Home filter row and flight-details panel Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Remove the Home search card; add a filter row (region chips, day selector, search box) above the board and a selected-flight details panel beside it, with one booking button.

**Architecture:** All decisions (filtering, timeline, cabin rows, the booking action) live in `HomeController` and are tested without a window. A new self-drawn `FlightDetailsPanel` control shows a `FlightDetails` model. `HomeForm` loses `_to`, `_cabin`, `_bookSelected` and gains the filter row and the panel.

**Tech Stack:** WinForms on .NET 10, MSTest 4, SQLite. `dotnet test --filter` while iterating; the full suite takes about 7 minutes.

**Spec:** `docs/superpowers/specs/2026-11-22-home-board-redesign-design.md`. Use the `skywings-dev` skill for repo conventions.

## Global Constraints

- Reuse `FlightSearch`, `FlightStatusEngine.Evaluate`, `BookingStore.CabinSeatsLeft` and `LoadAircraft`, `AppData.ClassMultipliers`, `AppData.Round2`, `HomeController.CanBook`. No new flight data; no invented delay reasons.
- Colours and fonts from `Theme`; shared controls from `UI/` (`GlassButton`, `GlassCard`, `RoundedInput`, `DateField`, `StatusPill`, `GridStyle`).
- The banner and its "Book a flight" button, the staff menu, the wizard, prices and seat rules do not change.
- Tests that assert the old look or the removed fields (`HomeLookTests` uses the `_to` drop-down) are changed on purpose and the commit says so. Test databases use `Pooling = false` (see `DatabasePoolIsolationTests`); never `ClearAllPools`.
- Work on branch `home-board`, small commits. Update `HelpForm` and `README.md` if they describe the search card.
- **Review gate:** after Task 4 and after Task 7 stop and give the owner the snapshot images (`%TEMP%\skywings-snapshots`, `home-*.png`) before going on.

## Review Focus

1. A flight whose aircraft has no Economy cabin, or a cabin that is sold out while others are not: the board's Fare and Seats left stay sensible and the panel's action is right.
2. The board refreshing every second (status changes) must keep the selected row, the chosen cabin and the scroll position, and not redraw the panel when nothing visible changed.
3. A region with no flights for the chosen day, an empty search result, and a search with odd characters (`%`, quotes, leading spaces): the board is empty, the panel says "No flight selected", the action button is disabled.
4. A flight that already departed or is inside the 2-hour cutoff: the button is disabled and says why; double-clicking the row does nothing.
5. The app left open past midnight: the "Today" button and the day selector use the date at the click, never a stale one; the day cannot go before today.

---

## Phase A — controller (no window)

### Task 1: Regions and the board query

**Files:**
- Create: `WinFormsApp3/Controllers/HomeBoard.cs`, `WinFormsApp3.Tests/Controllers/HomeBoardTests.cs`
- Modify: `WinFormsApp3/Controllers/HomeController.cs`

**Interfaces:**
- Produces: `public static class FlightRegions { public static readonly IReadOnlyList<string> Names; public static string Of(Flight flight); }` — `Names` is `{ "Domestic", "Southeast Asia", "Europe", "North America" }`; `Of` groups by flight number (PR-1xx and returns PR-5xx → Domestic, 2xx/6xx → Southeast Asia, 3xx/7xx → Europe, 4xx/8xx → North America, anything else → "Other").
- Produces: `HomeController.LoadBoard(string region, string text, DateTime day, DateTime? now = null) : List<FlightRow>` — flights leaving Manila on `day`, in `region` ("" for all), whose origin, destination or flight number contains `text` (trimmed, ignoring case); ordered like the old board (estimated departure, then flight number); `Price` is `"PHP " + the Economy fare` ("from"), `Seats` and `SeatsLeft` the total seats left across the aircraft's cabins (`"Sold out"` when 0), `LoadPercent` the total across cabins.

- [ ] **Step 1: Write the failing tests** in `HomeBoardTests`: `Region_OfAFlight_FollowsItsNumber` (PR-101 and PR-501 Domestic, PR-201 Southeast Asia, PR-304 Europe, PR-402 North America, `"XX-9"` Other); `Board_FilteredByRegion_ListsOnlyThatRegion`; `Board_FilteredByText_MatchesCityOrFlightNumber_IgnoringCaseAndSpaces` (`" ceb "`, `"pr-304"`, `"zzz"` gives empty, `"%"` gives empty and does not throw); `Board_ShowsTheEconomyFare_AndTotalSeatsAcrossCabins` (a seeded flight: assert `Price == "PHP " + base fare` and `Seats == sum of CabinSeatsLeft over the aircraft's cabins`); `Board_IsOrderedByEstimatedDeparture`.
- [ ] **Step 2:** Run `dotnet test --filter HomeBoardTests`; expect FAIL (types missing).
- [ ] **Step 3:** Implement `FlightRegions` and `HomeController.LoadBoard` (add a `FlightSearch.Board(BookingStore, string region, string text, DateTime day, DateTime? now)` static helper beside `Search` and have `LoadBoard` call it).
- [ ] **Step 4:** Run the same filter plus `FlightSearchTests`; expect PASS.
- [ ] **Step 5:** Commit `Home board: regions and a board query without a cabin`.

### Task 2: The flight details model

**Files:**
- Modify: `WinFormsApp3/Controllers/HomeBoard.cs`, `WinFormsApp3.Tests/Controllers/HomeBoardTests.cs`

**Interfaces:**
- Produces: `public enum StageState { Done, Current, Upcoming }`; `public sealed record TimelineStage(string Label, string Time, StageState State)`; `public sealed record CabinOption(string ClassName, decimal Fare, int SeatsLeft, int Seats, int LoadPercent)` (`Fare` = base fare × the cabin's multiplier, `AppData.Round2`); `public sealed record FlightDetails(Flight Flight, FlightStatusInfo Status, string Aircraft, string Duration, string DelayText, IReadOnlyList<TimelineStage> Timeline, IReadOnlyList<CabinOption> Cabins, int DefaultCabin)`.
- Produces: `HomeController.Describe(Flight flight, DateTime day, DateTime? now = null) : FlightDetails` — `Aircraft` is the aircraft model, `Duration` like `"about 1h 30m"`, `DelayText` is `"Delayed 20 min"` or `""`. Timeline stages in order `Scheduled`, `Check-in open`, `Boarding`, `Departed`, `Landed` with times: scheduled departure; estimated departure minus `AppData.CheckInWindowHours`; estimated departure minus `FlightStatusEngine.BoardingMinutes`; estimated departure; estimated arrival (all `"h:mm tt"`, with a day marker when the date differs from `day`). The current stage: Scheduled → 0, CheckInOpen or Delayed → 1, Boarding or GateClosing → 2, Departed or InFlight → 3, Landed → 4; earlier stages Done, later Upcoming. `Cabins` are the aircraft's cabins in the app's class order (Economy first … First Class last). `DefaultCabin` is the index of the first cabin with seats left, else the Economy index.

- [ ] **Step 1: Write the failing tests:** `Timeline_Stage_ForEachStatus` as a `DataRow` set that picks `now` relative to a flight's estimated departure (use the flight's own `Status` to compute offsets; days far ahead for Scheduled): assert the current stage index and that earlier stages are Done and later ones Upcoming; `Timeline_ShowsTheEstimatedTimes_WhenDelayed` (find a delayed flight/day by scanning days until `Status.DelayMinutes > 0`; assert `DelayText` and that the Boarding time moved); `Cabins_AreTheAircraftsOwn_WithFareAndSeats` (A320 has Economy and Business only; fare = base × multiplier; seats equal `CabinSeatsLeft`); `DefaultCabin_IsTheFirstWithSeats_ElseEconomy` (sell out Economy by booking its seats or by stubbing through the store helper used in `FlightSearchTests`; the default moves to Business).
- [ ] **Step 2:** Run; expect FAIL.
- [ ] **Step 3:** Implement `Describe`.
- [ ] **Step 4:** Run `HomeBoardTests`; expect PASS.
- [ ] **Step 5:** Commit `Home board: the details of a flight (timeline and cabins)`.

### Task 3: The booking action

**Files:**
- Modify: `WinFormsApp3/Controllers/HomeBoard.cs`, `WinFormsApp3.Tests/Controllers/HomeBoardTests.cs`

**Interfaces:**
- Produces: `public enum BookAction { Book, JoinWaitlist, None }`; `public sealed record BookingAction(BookAction Kind, string Label, string Reason)`; `HomeController.ActionFor(Flight flight, DateTime day, CabinOption? cabin, DateTime? now = null) : BookingAction`.
  Rules: `cabin` null → `None`, label `"Book →"`, reason `"Choose a cabin."`. Not bookable (`AppData.IsBookable` false): `None`, label `"Book {FlightNo} · {Cabin} →"`, reason `"This flight has already left."` when `now` is at or after the estimated departure, else `"Bookings close 2 hours before departure."`. Cabin sold out and bookable: `JoinWaitlist`, label `"Join waitlist →"`, reason empty. Otherwise `Book`, label `"Book {FlightNo} · {Cabin} →"`, reason empty.

- [ ] **Step 1: Write the failing tests:** `Action_Book_ForABookableCabin_NamesTheFlightAndCabin`; `Action_JoinWaitlist_ForASoldOutCabin`; `Action_None_AfterDeparture_SaysItLeft`; `Action_None_InsideTheCutoff_SaysWhen`; `Action_None_WithoutACabin`.
- [ ] **Step 2:** Run; expect FAIL.
- [ ] **Step 3:** Implement `ActionFor` using `HomeController.CanBook` and the status engine.
- [ ] **Step 4:** Run; expect PASS.
- [ ] **Step 5:** Commit `Home board: the one booking action`.

---

## Phase B — the screen

### Task 4: Filter row; the search card goes

**Files:**
- Modify: `WinFormsApp3/HomeForm.cs` (remove `_to`, `_cabin`, `_bookSelected`, `BuildSearch`, the `DestinationOption` list use; add the filter row), `WinFormsApp3.Tests/HomeLookTests.cs`
- Create: `WinFormsApp3.Tests/HomeFilterTests.cs`

**Interfaces:**
- Consumes: `HomeController.LoadBoard`, `FlightRegions.Names` (Task 1).
- Produces on `HomeForm`: private fields `Dictionary<string, GlassButton> _regionChips` (keys `""` for All and each region), `DateField _day`, `GlassButton _dayPrev`, `_dayNext`, `_dayToday`, `TextBox _boardSearch`; private `string _region` (default `""`); private method `void SetRegion(string region)` (updates the chips and refreshes) used by the chips and, in Task 6, by the region cards. The board is filled by `LoadBoard(_region, _boardSearch.Text, _day.Value.Date)`. `_day.MinDate` is today at construction; `_dayToday` sets the day to `DateTime.Today` read at the click; `_dayPrev` is disabled on today.

- [ ] **Step 1: Write the failing tests** in `HomeFilterTests`: `TheSearchCard_IsGone` (no controls named `_to`, `_cabin`, `_bookSelected`; no `FieldRow` captioned `"TO  (FROM MANILA)"`); `Chips_AreAllAndTheFourRegions_AndTheFirstIsChosen`; `ChoosingARegion_FiltersTheBoard` (click the Europe chip: every row's `Flight.FlightNo` is in `FlightRegions.Of == "Europe"`; All restores them); `TheDayArrows_MoveTheDay_AndNeverBeforeToday` (next moves +1; prev on today is disabled; Today returns); `TheSearchBox_NarrowsTheBoard_AndCombinesWithTheRegion`; `ANoMatchSearch_LeavesAnEmptyBoard_WithoutError`; `TheBoardStillRefreshesEverySecond_KeepingTheSelectedFlight` (call the tick method used by `Tick()` via reflection after choosing a row, assert the same flight stays current).
- [ ] **Step 2:** Run `--filter HomeFilterTests`; expect FAIL.
- [ ] **Step 3:** Implement: delete the search card and its fields and wiring; build the filter row (chips as `GlassButton` pills — chosen one `Primary`, others `Outline`; the search box in a `RoundedInput`; the day selector with `DateField` flanked by the arrow buttons and Today); point `RefreshFlights`, `ReloadBoardInBackground`, the `Tick` restamp, `UpdateSummary` and `BookSelectedFlight` at the new fields (the cabin passed on is Economy until Task 6). The `LoadDashboard` no longer fills a destination list. Keep the region cards for now (they still open the old list).
- [ ] **Step 4:** Update `HomeLookTests`: `SearchFields_AreRounded_AndTheDateIsADateField` and `Changing_TheDestination_StillRefreshesTheBoard` assert the removed fields — replace them with the new filter row's equivalents (rounded search box, `DateField` day) and say so in the commit. Run `--filter "HomeFilterTests|HomeLookTests|HomeHeaderTests|AllFormsSnapshotTests"`; expect PASS.
- [ ] **Step 5:** Commit `Home: a filter row replaces the search card`. **Review gate:** hand the owner `home-default.png` and `home-min.png`.

### Task 5: `FlightDetailsPanel`

**Files:**
- Create: `WinFormsApp3/UI/FlightDetailsPanel.cs`, `WinFormsApp3.Tests/FlightDetailsPanelTests.cs`

**Interfaces:**
- Consumes: `FlightDetails`, `CabinOption`, `BookingAction` (Tasks 2–3), `StatusPill`, `GlassButton`, `GlassCard`.
- Produces: `public sealed class FlightDetailsPanel : GlassCard` with `void ShowFlight(FlightDetails? details, int selectedCabin, BookingAction action)` (null shows "No flight selected" and a disabled button); `int SelectedCabin { get; }`; `event EventHandler<int> CabinChosen` (raised with the cabin index when a cabin row is clicked or chosen with the keyboard); `event EventHandler ActionClicked`; `GlassButton ActionButton { get; }` (internal). Draws: header (flight number, route), a `StatusPill` for the status with the countdown, the timeline (five dots on a line, done filled, current ringed, labels and times), the aircraft and duration line, one row per cabin (name, fare, `"N left"` or `"Sold out"`, a load bar; the chosen row highlighted with `BrandSoft`), the action button (label from `BookingAction`, disabled when `Kind` is `None`) and under it the `Reason` in small muted text.

- [ ] **Step 1: Write the failing tests:** `Empty_ShowsNoFlightSelected_AndADisabledButton`; `ShowFlight_FillsTheHeaderTimelineAndCabins` (build a `FlightDetails` through `HomeController.Describe`, assert the button text equals the action label, the cabin count rows, and that the panel is not blank when rendered to a bitmap); `ClickingACabinRow_RaisesCabinChosen_WithItsIndex` (simulate a click at the row's centre); `TheActionButton_IsDisabled_WhenTheActionIsNone_AndShowsTheReason`; `ActionClicked_IsRaised_ByTheButton`; `ShowFlight_WithTheSameContent_DoesNotRedraw` (count `Paint` events after two identical calls: the second adds none); `FitsAtItsMinimumWidth_WithoutClippedText` (a width of 300 and a long route).
- [ ] **Step 2:** Run; expect FAIL.
- [ ] **Step 3:** Implement the control. Compare the new inputs with the last shown ones (flight number, status label and countdown, cabin rows, selected cabin, action) and skip `Invalidate` when equal.
- [ ] **Step 4:** Run `--filter FlightDetailsPanelTests`; expect PASS.
- [ ] **Step 5:** Commit `Add the flight details panel`.

### Task 6: The panel on Home; one action; region cards set the chip

**Files:**
- Modify: `WinFormsApp3/HomeForm.cs`, `WinFormsApp3.Tests/HomeFilterTests.cs`
- Create: `WinFormsApp3.Tests/HomeDetailsTests.cs`

**Interfaces:** Consumes `FlightDetailsPanel`, `HomeController.Describe`, `ActionFor`, `SetRegion` (Task 4).

- [ ] **Step 1: Write the failing tests** in `HomeDetailsTests`: `SelectingARow_FillsThePanel_WithThatFlight`; `TheFirstCabinWithSeats_IsChosenAtFirst`; `ChoosingACabin_ChangesTheButtonLabel` (e.g. "Book PR-101 · Business →"); `ASoldOutCabin_TurnsTheButtonIntoJoinWaitlist`; `TheButton_OpensTheWizard_WithTheFlightDateAndCabin` (stub: assert the call that would be made, using a seam `Func<BookingWizardForm...>` or by checking `Prefill` arguments recorded in a private `LastBooking` field set before `ShowDialog` is called in a test hook; follow the existing hook style in `HomeForm`, otherwise add `internal Action<string, DateTime, string>? BookHook`); `TheButton_OpensJoinWaitlist_ForASoldOutCabin`; `DoubleClickingARow_DoesTheSameAsTheButton`; `ADepartedFlight_DisablesTheButton_AndSaysSo`; `TheBoardRefresh_KeepsTheChosenCabin_AndTheSelection`; `ClickingARegionCard_SetsTheChip_AndSelectsTheFirstFlight` (add to `HomeFilterTests`); `WithNoFlights_ThePanelSaysNoFlightSelected`.
- [ ] **Step 2:** Run; expect FAIL.
- [ ] **Step 3:** Implement: lay the board and the panel side by side (board fills, panel fixed width ≈ 340 logical px, stacked under the board when the window is narrower than 1100 — builder's call, or simply keep both at the 1060 minimum); wire selection → `Describe` → `ShowFlight`; remember the chosen cabin per flight number until the flight changes; `ActionClicked` and the row double-click call one private `StartBooking()`; replace `ShowFlightDetails` on the region cards by `SetRegion(summary.Title)` plus selecting the first row; delete the code of the old `_bookSelected`.
- [ ] **Step 4:** Run `--filter "HomeDetailsTests|HomeFilterTests|HomeLookTests|HomeHeaderTests|AllFormsSnapshotTests"`; expect PASS. Fix clipping the snapshot test reports at the minimum size (if ten board columns do not fit beside the panel, fold Estimated into Scheduled as the spec allows).
- [ ] **Step 5:** Commit `Home: a flight details panel and one booking button`.

### Task 7: Help, README, the whole suite, then the gate

**Files:** Modify `WinFormsApp3/HelpForm.cs` (only if the how-to text describes the search card), `README.md`, `WinFormsApp3.Tests/HelpContentTests.cs` if the help text changed.

- [ ] **Step 1:** `grep -rn -i "search bar\|From Manila\|To (from\|Book selected\|cabin" WinFormsApp3/HelpForm.cs WinFormsApp3/HelpContent*.cs README.md` and update every sentence that describes the removed card (the filter row, the details panel and the one booking button instead).
- [ ] **Step 2:** `dotnet build` (0 errors) and the full `dotnet test`; expect all pass. Re-run the snapshot test and list the `home-*.png` images.
- [ ] **Step 3:** Commit; give the owner the images and **stop for review** before merging.
