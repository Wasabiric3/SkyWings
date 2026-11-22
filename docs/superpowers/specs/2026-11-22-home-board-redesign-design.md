# Home screen: filter row and flight-details panel — design

Date: 2026-11-22 · Status: draft, waiting for approval

## Intent and why

The owner wants the search card on the Home screen gone (To, Departure, Cabin and "Book selected"). It takes a lot of room for what it does, and the flights board underneath already shows what matters. The freed space should make the flight board and its details better, not hold another form.

The owner chose two things from the options: **(2) one thin filter row** in place of the search card, and **(4) a selected-flight details panel** beside the board. The banner and its "Book a flight" button, the region cards and the staff menu stay as they are.

## What the search card does today (and where each part goes)

| Search card part | Where it goes |
|------------------|---------------|
| To (a destination) | Region chips and a small search box in the filter row (city or flight number) |
| Departure date | A day selector in the filter row |
| Cabin | The cabin list in the details panel (the board shows the Economy fare and the total seats left) |
| Book selected / Join waitlist | One button at the bottom of the details panel |

## Goals

- The search card is removed. The board moves up and gets the room.
- **Filter row** above the board: region chips (All, Domestic, Southeast Asia, Europe, North America), a day selector (‹ date ›, a Today button, and a calendar drop-down), and a search box (city or flight number). Choosing anything refreshes the board straight away, as the old fields did.
- **Details panel** to the right of the board for the selected flight: header (flight number, route), the status as a pill with its countdown, a status timeline, the aircraft and flight duration, a list of the cabins the aircraft has (fare per traveller, seats left, a load bar), and one action button.
- **One booking action** in the panel. Its label follows the chosen cabin: "Book PR-111 · Economy →"; "Join waitlist →" when that cabin is sold out; disabled, with the reason written under it, when the flight cannot be booked (inside the 2-hour cutoff, already departed, or no cabin chosen). Double-clicking a board row does the same as pressing it (as today).
- Clicking a region card at the bottom sets the matching region chip (it used to open a separate list window) and moves to the board.
- The board keeps its live behaviour: the clock, the status updating every second, sorting by header click, the status pills.

## Non-goals

- No new flight data and no invented reasons: the status engine knows a delay in minutes, not a cause, so the panel says "Delayed 20 min", never why. No real tracking feed.
- No changes to the banner, its Book a flight button, the staff menu, the booking wizard, prices or seat rules.
- No remembering of the chosen region or day between sessions (it opens on All and today).

## Anti-goals (a failure even if it builds)

- A board that lists flights for a day or region other than the one chosen, or a stale selection (the panel showing a flight that is not on the board).
- A Book button that opens the wizard for a different flight or cabin than the panel shows, or that stays enabled for a flight past the booking cutoff.
- Text clipped or overlapping at the minimum window size (1060 wide), or the board and panel unusable at it.
- A panel that flickers or loses the selected row every second when the board refreshes its status.
- Hard-coded colours; the old search fields left behind unused.

## Constraints

- WinForms on .NET 10, MSTest 4; colours and fonts from `Theme`; rounded fields and pills from the existing shared controls (`RoundedInput`, `DateField`, `GlassButton`, `GlassCard`, `StatusPill`).
- Reuse the existing logic: `FlightSearch.Search`, `FlightStatusEngine.Evaluate`, `BookingStore.CabinSeatsLeft`, `BookingStore.LoadAircraft`, `AppData.ClassMultipliers`, `HomeController.CanBook`. Form logic that is more than layout goes into `HomeController` (testable without a window).
- Existing tests that assert the old look or the removed fields (`HomeLookTests` uses the `_to` drop-down) are updated on purpose and the commit says so. Image test (`AllFormsSnapshotTests`) must still show Home fitting at default and minimum size.
- Update `HelpForm` and `README.md` if they describe the search card.

## Approaches considered

1. **Filter row and details panel (chosen).** The board becomes the centrepiece, filtering is one thin row, and flight details live beside the board.
2. **Remove the card and move the board up, with the region cards as the only filter.** Least work, but no way to pick a day or find a city.
3. **Replace the card with live summary tiles** (flights today, boarding, delayed). Looks lively but does not help find a flight.

## Design

### Layout (top to bottom)

Menu · banner · filter row · [board | details panel] · "Where will you go next?" region cards · footer.

```
 [All][Domestic][Southeast Asia][Europe][North America]   ‹ Today · Nov 22 › [Today]   🔍 city or flight
 ┌ Flights on Sun, Nov 22 · 3 flights ───────────────┐ ┌ PR-111 · Manila → Cebu ───────────┐
 │ Flight To   Sched Est  Gate Status   Fare  Seats  │ │ ● Boarding · departs in 22 min     │
 │ PR-101 Cebu 6:00  6:00 C2   Landed   3,500 108    │ │ ○──●──○──○──○ timeline with times  │
 │ PR-111 Cebu 9:30  9:30 C14  Boarding 3,200 Sold…  │ │ Airbus A320 · about 1h 30m         │
 └───────────────────────────────────────────────────┘ │ Economy   PHP 3,200  Sold out ▓▓▓  │
                                                       │ Business  PHP 8,000  4 left  ▓░░   │
                                                       │ [ Join waitlist → ]                │
                                                       └────────────────────────────────────┘
```

### Filter row

Region chips are `GlassButton` pills (the chosen one filled, like the section buttons). A region is a flight-number group (PR-1xx Domestic, 2xx Southeast Asia, 3xx Europe, 4xx North America), the same grouping the flight picker uses. The day selector is `‹` and `›` buttons, a `DateField` showing the day, and a Today button; it cannot go before today. The search box matches a city or flight number, ignoring case, like the flight picker's search. All three combine: region and search narrow the list for the chosen day.

### Board

Columns: Flight, To, Scheduled, Estimated, Gate, Status, Countdown, Fare (the Economy fare, "from"), Seats left (total across the aircraft's cabins). The load bar moves into the details panel (one per cabin). Sorting, status pills, dimming of flights that have left, and the one-second status refresh stay. If ten columns do not fit beside the panel at the minimum width, Estimated folds into Scheduled ("6:00 AM → 6:20 AM"); that is the builder's call.

### Details panel

Built from the selected row. Timeline stages: Scheduled → Check-in open → Boarding → Departed → Landed, each with its time (from the status engine: check-in window, `BoardingMinutes`, estimated departure and arrival), the current stage marked, a delay shown as "Delayed N min". Cabins come from the flight's aircraft; each cabin shows its fare (base fare × the cabin's multiplier), seats left that day and a load bar. A cabin can be chosen (the first with seats is chosen at the start; Economy when none has). The panel redraws when the selection changes and when the status changes, without taking focus away from the board.

### Region cards

A card click sets the region chip and selects the first flight in it; the old region list window is no longer opened from Home (the dialog class stays, other screens may use it).

## Phases

1. `HomeController`: region of a flight, the filter (region, text, day), the panel's content (timeline, cabin rows, action state). Tested without a window.
2. The filter row and the removal of the search card; the board filtering by the row.
3. The details panel and the one action button; region cards set the chip.
4. Tests updated, image test, Help and README, whole-suite run.

## Verification

- Unit tests for the controller: region grouping, the combined filter, the timeline for each status, cabin rows (fare, seats, sold out), and every action state (book, join waitlist, disabled with a reason).
- Form tests: chips and the day selector refresh the board; selecting a row fills the panel; the button opens the wizard or waitlist with the right flight, date and cabin; the board refresh keeps the selection.
- The image test shows Home at default and minimum size, with nothing clipped.
- `dotnet build` and the whole test suite pass; the owner reviews screenshots before merging.

## Left to the builder

Exact sizes and spacing; the wording of small labels; how the timeline is drawn; whether Estimated folds into Scheduled at the minimum width.

## Decisions made by me, to check

- The banner and its Book a flight button stay (the owner first asked to remove it, then said that was a misunderstanding).
- A search box (city or flight number) is part of the filter row: with the destination drop-down gone, a single city like Cebu would otherwise be unreachable.
- The board shows the Economy fare and total seats left; per-cabin detail is in the panel.
- The load bar moves from the board to the panel.
- Region cards set the chip instead of opening the region list.
- Nothing is remembered between sessions.
