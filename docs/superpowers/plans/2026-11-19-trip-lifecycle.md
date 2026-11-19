# Trip Lifecycle Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Every booking shows a live trip status (Upcoming → … → Completed / No-show / Canceled), and demo travellers check in by themselves during the 24-hour window.

**Architecture:** A pure `TripStatus` function derives the phase from `FlightStatusEngine.Evaluate` per leg plus the booking's check-ins; nothing new is stored on bookings. `SimulationService.ProgressCheckIns` writes back-dated demo check-ins into the existing `check_ins` table each minute. Screens (Staff Bookings, booking detail, Manage booking, Check-in) read the derived status.

**Tech Stack:** .NET 10 WinForms, SQLite (Microsoft.Data.Sqlite), MSTest 4.

**Spec:** `docs/superpowers/specs/2026-11-19-trip-lifecycle-design.md`

## Global Constraints

- Follow `.claude/skills/skywings-dev/SKILL.md`: all SQL in `BookingStore` partials under `lock (_lock)`, `@parameters` only, multi-step writes in one transaction, colours from `Theme`.
- `bookings.status` keeps only `CONFIRMED` / `CANCELED`; no schema change to `bookings`.
- The simulator touches only `is_simulated = 1` / `SIM-` rows; a real booking is never checked in by it.
- Labels exactly: `Upcoming`, `Check-in open`, `Checked in`, `Boarding`, `In flight`, `Completed`, `No-show`, `Canceled`; return-leg phases before Completed are prefixed `Return · `.
- Demo no-show rate 8% of bookings; check-in moments always strictly inside (departure − 24 h, departure).
- Catch-up lookback: flights from 7 days ago up to 24 h ahead.
- Build/test from repo root: `dotnet build SkyWings.slnx`, `dotnet test SkyWings.slnx`.

## Review Focus

1. Flight that departs late evening and lands after midnight: stays `In flight` until the arrival the next day, then `Completed` (test in Task 1).
2. Round trip where nobody checked in on the outbound: `No-show` is final, even though the return is still ahead (Task 1).
3. App closed for several days: on the next run, past demo flights get back-dated check-ins, not a wall of No-shows (Task 3).
4. A real booking on a demo-filled flight is never checked in by the simulator, even via the save method directly (Task 2).
5. Delayed flight past its scheduled time: still `Boarding`/`Check-in open`, not `In flight` (Task 1).

---

### Task 1: TripStatus (pure)

**Files:**
- Create: `WinFormsApp3/Controllers/TripStatus.cs`
- Test: `WinFormsApp3.Tests/Controllers/TripStatusTests.cs`

**Interfaces:**
- Consumes: `FlightStatusEngine.Evaluate(string flightNo, DateTime date, DateTime now) -> FlightStatusInfo` (`Kind`, `EstimatedArrival`), `BookingRecord` (`Status`, `FlightNo`, `FlightDateValue`, `ReturnFlightNo`, `ReturnFlightDateValue`, `Destination`, `Origin`), `CheckInRecord`.
- Produces:
  - `public enum TripPhase { Upcoming, CheckInOpen, CheckedIn, Boarding, InFlight, Completed, NoShow, Canceled }`
  - `public sealed record TripStatusInfo(TripPhase Phase, string Label, bool ReturnLeg, DateTime? LandedAt, string Destination)`
  - `public static TripStatusInfo TripStatus.Of(BookingRecord booking, IReadOnlyCollection<CheckInRecord> checkIns, DateTime now)`
  - `public static readonly string[] TripStatus.Filters = { "All", "Upcoming", "In progress", "Completed", "No-show", "Canceled" }`
  - `public static bool TripStatus.Matches(TripPhase phase, string filter)` ("In progress" = CheckInOpen, CheckedIn, Boarding, InFlight)

Leg mapping (engine kind → phase): Scheduled → Upcoming; CheckInOpen/Delayed → CheckedIn if any check-in on that leg (same flight no and date) else CheckInOpen; Boarding/GateClosing → Boarding; Departed/InFlight → InFlight, or NoShow if nobody checked in on that leg; Landed → Completed (LandedAt = EstimatedArrival), or NoShow if nobody checked in. Canceled booking → Canceled first. Round trip: if outbound is NoShow → NoShow; if outbound not Completed → outbound phase; else return leg phase with `ReturnLeg = true` and `Destination = booking.Origin`; Completed only after the return lands. Build test bookings with times relative to `AppData.DepartureOf(flightNo, date)` and `FlightStatusEngine.Evaluate` so delays are honoured.

- [ ] **Step 1: Write failing tests** — one per row: `Canceled_WinsOverEverything`, `BeforeCheckInOpens_IsUpcoming`, `CheckInOpen_NobodyCheckedIn`, `CheckInOpen_OneOfTwoCheckedIn_IsCheckedIn`, `Boarding_WithoutCheckIns_IsStillBoarding`, `InFlight_WithCheckIn`, `Departed_NobodyCheckedIn_IsNoShow`, `Landed_IsCompleted_WithLandedAtEqualToEstimatedArrival`, `OvernightFlight_StaysInFlightUntilArrivalNextDay`, `DelayedFlight_PastScheduledTime_IsNotYetInFlight` (pick a flight/date whose `Evaluate(...).DelayMinutes > 0` by scanning dates in the test), `RoundTrip_OutboundLanded_ShowsReturnLegPrefixed` (Label starts with `"Return · "`), `RoundTrip_OutboundNoShow_IsFinal`, `RoundTrip_CompletedOnlyAfterReturnLands`, `Matches_InProgressCoversFourPhases`.
- [ ] **Step 2: Run** `dotnet test SkyWings.slnx --filter FullyQualifiedName~TripStatusTests` — expect build failure (types missing).
- [ ] **Step 3: Implement** `TripStatus.cs` per the mapping above.
- [ ] **Step 4: Run** the same filter — expect all PASS.
- [ ] **Step 5: Commit** `git commit -m "Derive a live trip status for every booking from the flight timeline and check-ins"`

### Task 2: Store — bulk check-ins, demo check-in candidates, reset clears check-ins

**Files:**
- Modify: `WinFormsApp3/BookingStore.Simulation.cs` (new methods; `DeleteSimulatedBookings` at ~line 160)
- Modify: `WinFormsApp3/BookingStore.Features.cs` (bulk loader next to `LoadCheckIns`)
- Modify: `WinFormsApp3/Models.cs` (new record)
- Test: `WinFormsApp3.Tests/SimulatedCheckInStoreTests.cs`

**Interfaces:**
- Produces:
  - `public sealed record SimCheckInCandidate(string Reference, string FlightNo, DateTime FlightDate, string PassengerName, string SeatCode)` (Models.cs)
  - `public Dictionary<string, List<CheckInRecord>> BookingStore.LoadCheckInsByBooking()` — every check-in, grouped by booking reference, one query.
  - `public List<SimCheckInCandidate> BookingStore.LoadSimulatedCheckInCandidates(DateTime fromDate, DateTime toDate)` — confirmed `is_simulated = 1` bookings with `flight_date` in [fromDate, toDate], one row per passenger with a non-empty seat and no `check_ins` row for that booking/passenger/flight/date (LEFT JOIN … IS NULL).
  - `public int BookingStore.SaveSimulatedCheckIns(IReadOnlyList<CheckInRecord> checkIns)` — one transaction, `INSERT OR IGNORE`, skips any record whose reference does not start with `SimulatedPrefix`; returns rows inserted.
  - `DeleteSimulatedBookings` also runs `DELETE FROM check_ins WHERE booking_ref IN (<refs>)` in its transaction, before deleting bookings.

- [ ] **Step 1: Write failing tests**: `Candidates_ListOnlyDemoTravellersNotYetCheckedIn` (EnsureLoad 2 days, save one check-in, that traveller disappears; real booking from `RealBooking` helper pattern in `SimulationServiceTests` never listed); `SaveSimulatedCheckIns_IgnoresRealReferences` (a record for `SKY-…` returns 0 and `LoadCheckIns` stays empty); `LoadCheckInsByBooking_GroupsByReference`; `DeleteSimulatedBookings_RemovesDemoCheckIns_KeepsRealOnes` (real check-in via `CheckInPassenger` inside its window survives).
- [ ] **Step 2: Run** `--filter FullyQualifiedName~SimulatedCheckInStoreTests` — expect FAIL.
- [ ] **Step 3: Implement** the four store changes.
- [ ] **Step 4: Run** the filter, then `SimulationServiceTests` and `DemoAdminTests` — expect PASS.
- [ ] **Step 5: Commit** `git commit -m "Store support for demo check-ins; resetting demo data also removes their check-ins"`

### Task 3: Simulator — demo travellers check in

**Files:**
- Modify: `WinFormsApp3/SimulationService.cs` (`SimulationTick` record; new methods; `RunIfEnabled`)
- Test: `WinFormsApp3.Tests/SimulationCheckInTests.cs`

**Interfaces:**
- Consumes: Task 2 methods; `AppData.DepartureOf`, `AppData.CheckInWindowHours`, `FlightStatusEngine.StableSeed`, `JavaRandom`.
- Produces:
  - `SimulationTick` gains a trailing `int CheckInsAdded = 0`.
  - `internal const int NoShowPercent = 8;`
  - `internal static DateTime? PlannedCheckIn(string reference, string passengerName, DateTime departure)` — null when the booking is a no-show.
  - `public int ProgressCheckIns(DateTime now)` — returns check-ins written.
  - `RunIfEnabled` calls `ProgressCheckIns(now)` after `EnsureLoad` and reports it in `CheckInsAdded`.

PlannedCheckIn algorithm (booking-level timing so a party checks in together):
```
bookingRng = new JavaRandom(StableSeed($"checkin|{reference}"))
if bookingRng.NextInt(100) < NoShowPercent: return null
roll = bookingRng.NextInt(100); u = bookingRng.NextDouble()
hoursBefore = roll < 15 ? 20 + 3.9*u      // soon after check-in opens
            : roll < 40 ? 12 + 8*u        // the evening before
            :             1 + 11*u        // the last 12 hours
jitter = new JavaRandom(StableSeed($"checkin|{reference}|{passengerName}")).NextInt(5)   // 0-4 minutes
return min(departure - hoursBefore h + jitter min, departure - 1 min)
```
ProgressCheckIns: candidates for `[now.Date-7, now.Date+1]`; departure = `AppData.DepartureOf(FlightNo, FlightDate)`; skip if departure > now + 24 h; take planned ≤ now; save all in one `SaveSimulatedCheckIns` call with `CheckedInAt = planned`.

- [ ] **Step 1: Write failing tests**: `PlannedCheckIn_IsAlwaysInsideTheWindow` (1,000 references); `PlannedCheckIn_NoShowRateIsAboutEightPercent` (5–11% over 2,000 refs); `PlannedCheckIn_IsStable`; `NobodyChecksInBeforeTheWindowOpens` (EnsureLoad at Now, ProgressCheckIns at Now; no check-in for flights departing > 24 h later); `ByDeparture_AboutNinetyTwoPercentOfBookingsCheckedIn` (EnsureLoad, then ProgressCheckIns at a moment after today's last departure; share of demo bookings on today's flights with ≥1 check-in in 87–97%); `CatchUp_AfterAGap_BackDatesCheckIns` (EnsureLoad at Now, ProgressCheckIns at Now+3 days; every `CheckedInAt` < its flight's departure and > departure − 24 h); `RealBookings_AreNeverCheckedIn`; `RunIfEnabled_ReportsCheckInsAdded`; `ProgressCheckIns_ForAllFlights_FinishesInReasonableTime` (< 5 s for a 7-day load, same style as the existing timing test).
- [ ] **Step 2: Run** `--filter FullyQualifiedName~SimulationCheckInTests` — expect FAIL.
- [ ] **Step 3: Implement** per the interfaces and algorithm.
- [ ] **Step 4: Run** the filter plus `SimulationServiceTests`, `DemoAdminTests` — expect PASS.
- [ ] **Step 5: Commit** `git commit -m "Demo travellers check in by themselves during the 24-hour window; about 8% are no-shows"`

### Task 4: Staff Bookings tab and booking detail

**Files:**
- Modify: `WinFormsApp3/Security/AdminService.cs` (next to `LoadBookings`)
- Modify: `WinFormsApp3/AdminForm.cs` (`BuildBookingsPage` ~348, `LoadBookingsFromStore` ~398, `ApplyBookingFilter` ~403, `ShowBookingDetails` ~425, status cell colour)
- Modify: `WinFormsApp3/BookingDetailForm.cs` (constructor, status pill ~28)
- Create: `WinFormsApp3/UI/TripColors.cs`
- Test: `WinFormsApp3.Tests/DemoAdminTests.cs` (or new `TripStatusAdminTests.cs`)

**Interfaces:**
- Consumes: Task 1 `TripStatus`, Task 2 `LoadCheckInsByBooking`.
- Produces:
  - `public Dictionary<string, TripStatusInfo> AdminService.LoadTripStatuses(IReadOnlyList<BookingRecord> bookings, DateTime now)` — requires `Permission.ViewBookings`; one `LoadCheckInsByBooking()` call.
  - `internal static Color TripColors.For(TripPhase phase)`: Completed → `Theme.Accent`; Boarding, InFlight → `Theme.Info`; CheckInOpen, CheckedIn → `Theme.Primary`; Upcoming → `Theme.TextSecondary`; NoShow → `Theme.Warning`; Canceled → `Theme.DangerRed`.
  - `BookingDetailForm(BookingRecord booking, TripStatusInfo trip)`.
- Status filter items = `TripStatus.Filters`; the grid's Status column shows `trip.Label`, coloured with `TripColors.For` in `CellFormatting`.

- [ ] **Step 1: Write failing tests**: `LoadTripStatuses_GivesCompletedForLandedAndUpcomingForFuture` (a real booking dated a week ago with a check-in → Completed; one next week → Upcoming); `LoadTripStatuses_AgentsWithoutViewBookingsAreRefused` (only if such a role exists in `RolePermissions`; otherwise skip this test).
- [ ] **Step 2: Run** the filter — expect FAIL.
- [ ] **Step 3: Implement** AdminService, TripColors, AdminForm and BookingDetailForm changes.
- [ ] **Step 4: Run** `dotnet build SkyWings.slnx` (no new warnings) and the filter — expect PASS.
- [ ] **Step 5: Commit** `git commit -m "Staff Bookings tab and booking detail show the live trip status with Completed and No-show filters"`

### Task 5: Manage booking and Check-in banners

**Files:**
- Modify: `WinFormsApp3/Controllers/ManageBookingController.cs` (`BannerKind`, `DescribeBooking` ~85, new `CurrentTrip`)
- Modify: `WinFormsApp3/ManageBookingForm.cs` (~174: pass the trip, colours for new kinds)
- Modify: `WinFormsApp3/Controllers/CheckInController.cs` (`Load` ~69, Closed banner text)
- Test: `WinFormsApp3.Tests/Controllers/ManageBookingControllerTests.cs`, `WinFormsApp3.Tests/Controllers/CheckInAndHomeControllerTests.cs`

**Interfaces:**
- Produces:
  - `public enum BannerKind { Confirmed, Canceled, Departed, Completed, NoShow }`
  - `public static BookingBanner DescribeBooking(BookingRecord booking, TripStatusInfo? trip = null)` — existing callers unchanged.
  - `public TripStatusInfo? CurrentTrip(DateTime now)` on `ManageBookingController` (null when no booking is open).
- Exact copy: Completed → `$"This trip is completed. It landed in {trip.Destination} at {trip.LandedAt:h:mm tt} on {trip.LandedAt:MMM d}."`; NoShow → `"No-show: nobody on this booking checked in, so the flight left without them."`; Check-in Closed when the leg's `Evaluate(...).Kind == Landed` → `$"Check-in has closed. This flight landed at {arrival:h:mm tt}."` (otherwise the existing text). ManageBookingForm colours: Completed → `Theme.Accent`, NoShow → `Theme.Warning`.

- [ ] **Step 1: Write failing tests**: `DescribeBooking_CompletedTrip_SaysWhereAndWhenItLanded`; `DescribeBooking_NoShow`; `DescribeBooking_WithoutTrip_KeepsOldBehaviour` (existing tests stay green); `CheckIn_ClosedBanner_SaysLandedAfterArrival`.
- [ ] **Step 2: Run** the two test classes — expect FAIL.
- [ ] **Step 3: Implement**.
- [ ] **Step 4: Run** the two test classes — expect PASS.
- [ ] **Step 5: Commit** `git commit -m "Manage booking and check-in say when a trip is completed or a no-show"`

### Task 6: Help, README, final verification

**Files:**
- Modify: `WinFormsApp3/HelpForm.cs`, `WinFormsApp3.Tests/HelpContentTests.cs` (if it checks topics), `README.md`, `WinFormsApp3/AdminForm.cs` demo info text (~667: add "demo travellers check in during the 24-hour window; about 8% are no-shows").

- [ ] **Step 1:** Add a short "Trip status" help entry listing the eight labels and one line on demo check-ins; mirror in README.
- [ ] **Step 2: Run** `dotnet test SkyWings.slnx` — expect all PASS, count = 446 + new tests.
- [ ] **Step 3: Visual check:** render the Staff Bookings tab and a Manage booking page with a throwaway offscreen test (same technique as the board screenshot: STA thread, offscreen form, `DrawToBitmap`, PNG to the scratchpad), look at the images, delete the throwaway test.
- [ ] **Step 4: Commit** `git commit -m "Help and README describe trip statuses and demo check-ins"`
