# Live Operations Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The simulation runs unseen; its bookings look like and count as real traffic, the Demo tab and "demo" wording are gone, and the Bookings tab loads one flight date at a time.

**Architecture:** `bookings.is_simulated` becomes the only simulation marker (guards move off the `SIM-` reference prefix). Simulated bookings get `SKY-` references and realistic passenger data, and are regenerated once (load version 3), which also switches "include in reports" on. The staff Bookings tab reads one date through a bulk store query. A `--reset-simulation` command-line switch replaces the Reset button.

**Tech Stack:** .NET 10 WinForms, SQLite, MSTest 4.

**Spec:** `docs/superpowers/specs/2026-11-19-live-operations-design.md`

## Global Constraints

- Follow `.claude/skills/skywings-dev/SKILL.md` (SQL only in `BookingStore` partials under `lock (_lock)`, `@parameters`, transactions, `Theme` colours).
- Real bookings (`is_simulated = 0`) are never created, changed, checked in or deleted by simulator code.
- Simulated booking references: `SKY-` + 12 uppercase hex, the same shape as `BookingWizardController` (`"SKY-" + Guid…[..12]`).
- Seat-hold owners keep their internal `SIM-` prefix (never shown to staff); rename the constant to `SimulatedHoldPrefix`.
- No user-visible text says "demo"/"Demo" (staff window, help). README may keep a developer note.
- Build/test: `dotnet build SkyWings.slnx`, `dotnet test SkyWings.slnx`.

## Review Focus

1. An existing database with `demo.in_reports = 0` stored and `SIM-` bookings: after the next home-screen tick, simulated bookings are regenerated with `SKY-` refs and Reports include them (Task 2).
2. A real booking whose reference happens to equal a simulated one cannot be overwritten (simulated saves are `INSERT OR IGNORE`) — keep it that way (Task 1 test).
3. Search box with a lowercase or padded reference (`" sky-3cad9…  "`) still finds the booking on another date (Task 3).
4. `--reset-simulation` on a database with real bookings and real check-ins leaves them intact (Task 4).
5. Bookings tab on a date with no bookings shows an empty list and "0 bookings", not an error (Task 3).

---

### Task 1: The `is_simulated` column is the only marker

**Files:**
- Modify: `WinFormsApp3/BookingStore.Simulation.cs` (prefix check in `SaveSimulatedBookings` ~83; `SaveSimulatedCheckIns` ~278; holds ~177, ~408; new `IsSimulatedBooking`)
- Modify: `WinFormsApp3/BookingStore.Features.cs` (`LoadCheckInsByBooking` ~206)
- Modify: `WinFormsApp3/SimulationService.cs` (`CancelSimulatedBooking` ~328, hold owner ~294)
- Modify tests that assert the prefix: `SimulatedCheckInStoreTests.cs` (51, 117, 136)
- Test: `WinFormsApp3.Tests/SimulatedCheckInStoreTests.cs`, `WinFormsApp3.Tests/SimulatedBookingStoreTests.cs`

**Interfaces:**
- Produces: `public bool BookingStore.IsSimulatedBooking(string reference)`; `public const string BookingStore.SimulatedHoldPrefix = "SIM-"` (replaces `SimulatedPrefix`, which is removed).
- `SaveSimulatedBookings` no longer requires a prefix (still writes `is_simulated = 1`, still `INSERT OR IGNORE`).
- `SaveSimulatedCheckIns` inserts a record only when its booking has `is_simulated = 1` (e.g. `INSERT OR IGNORE … SELECT … WHERE EXISTS (SELECT 1 FROM bookings WHERE reference = @ref AND is_simulated = 1)`).
- `LoadCheckInsByBooking(includeSimulated: false)` filters with `booking_ref IN (SELECT reference FROM bookings WHERE is_simulated = 0)`.
- `CancelSimulatedBooking` refuses unless `IsSimulatedBooking(reference)`.

- [ ] **Step 1: Failing tests:** `SaveSimulatedBooking_AcceptsASkyReference_AndMarksItSimulated` (save `SKY-00000000S001`, `IsSimulatedBooking` true; a real `SKY-` booking → false); `SaveSimulatedBooking_NeverOverwritesARealBookingWithTheSameReference`; `SaveSimulatedCheckIns_IgnoresRealBookings_EvenWithASkyReference`; `CancelSimulatedBooking_RefusesARealBooking` (in `SimulationServiceTests`); update the three prefix assertions to `IsSimulatedBooking`.
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~SimulatedCheckInStoreTests|FullyQualifiedName~SimulatedBookingStoreTests|FullyQualifiedName~SimulationServiceTests"` — expect FAIL.
- [ ] **Step 3: Implement** per Interfaces.
- [ ] **Step 4: Run** same filter plus `SimulationCheckInTests`, `DemoAdminTests` — expect PASS.
- [ ] **Step 5: Commit** `"The is_simulated column is the only simulation marker; references no longer need SIM-"`

### Task 2: Simulated bookings look real and count everywhere

**Files:**
- Modify: `WinFormsApp3/SimulationService.cs` (`MakeBooking` ~210-235; `LoadVersion` → `"3"`; version step in `RunIfEnabled`)
- Modify: `WinFormsApp3/BookingStore.Simulation.cs` (`IncludeSimulatedInReports` default)
- Modify tests that assume reports exclude simulated bookings: `SimulationServiceTests.EnsureLoad_LeavesRealBookingsAndReportsUntouched`, `DemoAdminTests.TheDemoIsOnByDefault_ButKeptOutOfReports`, `SimulatedBookingStoreTests` settings test
- Test: `WinFormsApp3.Tests/SimulationServiceTests.cs`

**Interfaces:**
- `IncludeSimulatedInReports => GetSetting(SettingDemoInReports, "1") == "1"`.
- `RunIfEnabled` version step (stored version ≠ `"3"`): delete simulated bookings, set `SettingDemoInReports = "1"`, set version `"3"`.
- `MakeBooking`: reference `"SKY-" + <same stable hash hex>[..12]`; address `"{1-250} {street}, {city}"` from small fixed lists of Philippine streets/cities; contact `"09" + 9 digits`; ID number `"P" + 7 digits + letter`; payment ref `"TXN-" + 10 hex`. All from the existing seeded `rng` (stays deterministic). Names stay as they are.

- [ ] **Step 1: Failing tests:** `GeneratedBookings_LookLikeRealOnes` (every simulated booking after `EnsureLoad(Now, 1)`: reference matches `^SKY-[0-9A-F]{12}$`; no passenger address `"Demo data"`, no ID `"DEMO"`; contact matches `^09\d{9}$`; payment ref starts `"TXN-"`); `Reports_CountSimulatedBookings_ByDefault` (`LoadReportStats().TotalBookings` > real count); `RunIfEnabled_Version3_RegeneratesOldSimBookings_AndIncludesThemInReports` (seed a `SIM-` booking via `SaveSimulatedBooking`, set version `"2"` and `demo.in_reports = "0"`, run → the `SIM-` booking is gone, setting is `"1"`).
- [ ] **Step 2: Run** `--filter FullyQualifiedName~SimulationServiceTests` — expect FAIL.
- [ ] **Step 3: Implement**; flip the three tests that asserted the old "kept out of reports" default (keep their real-booking-untouched assertions).
- [ ] **Step 4: Run** `--filter "FullyQualifiedName~SimulationServiceTests|FullyQualifiedName~DemoAdminTests|FullyQualifiedName~SimulatedBookingStoreTests|FullyQualifiedName~SimulationCheckInTests"` — expect PASS.
- [ ] **Step 5: Commit** `"Simulated bookings get real-looking references and passenger details and count in reports"`

### Task 3: Bookings tab — one flight date at a time; Demo tab removed

**Files:**
- Modify: `WinFormsApp3/BookingStore.cs` (new bulk loader next to `LoadBookings` ~300; reuse `BuildBookingRecord`'s mapping)
- Modify: `WinFormsApp3/Security/AdminService.cs` (new methods; `LoadTripStatuses` reads check-ins for the listed bookings only)
- Modify: `WinFormsApp3/AdminForm.cs` (remove `AddSection("Demo"…)` line 83 and the demo members ~652-700; `_bookingDate` always on, default today; `LoadBookingsFromStore`, `ApplyBookingFilter`, count text)
- Test: `WinFormsApp3.Tests/TripStatusAdminTests.cs`, `WinFormsApp3.Tests/BookingStoreTests.cs`

**Interfaces:**
- `public List<BookingRecord> BookingStore.LoadBookingsForDate(DateTime flightDate)` — bookings whose outbound `flight_date` is that date (all, simulated included), newest `booked_at` first. Passengers, return legs and options come from **one query each** for the date (`booking_ref IN (SELECT reference FROM bookings WHERE flight_date = @d)`), not per booking.
- `public Dictionary<string, List<CheckInRecord>> BookingStore.LoadCheckInsForDate(DateTime flightDate)` — check-ins of those bookings (both legs).
- `public List<BookingRecord> AdminService.LoadBookingsForDate(DateTime date)` and `public Dictionary<string, TripStatusInfo> AdminService.LoadTripStatusesForDate(IReadOnlyList<BookingRecord> bookings, DateTime date, DateTime now)` (both `ViewBookings`).
- `internal static string? AdminService.AsReference(string search)` — trimmed, upper-cased text when it matches `^SKY-[0-9A-F]{12}$`, else null. When non-null, the tab shows `FindBooking(...)` alone (any date).
- Count label: `"{rows} bookings on {date:MMM d}"` (or `"1 booking found"` for a reference search).

- [ ] **Step 1: Failing tests:** `LoadBookingsForDate_ReturnsOnlyThatDate_WithPassengersAndReturnLegs` (real one-way + real round trip on date D, one on D+1); `LoadBookingsForDate_EmptyDate_ReturnsEmpty`; `LoadBookingsForDate_FullSimulatedDay_IsQuick` (`[DoNotParallelize]`, `EnsureLoad(Now, 1)`, < 1500 ms); `AsReference_AcceptsLowercaseAndSpaces_RejectsOtherText` (`" sky-3cad9c0ffee1 "` → `"SKY-3CAD9C0FFEE1"`, `"PR-101"` → null); `LoadTripStatusesForDate_MatchesLoadTripStatuses`.
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~TripStatusAdminTests|FullyQualifiedName~BookingStoreTests"` — expect FAIL.
- [ ] **Step 3: Implement** store, service and form changes; delete the Demo section and its fields/methods from `AdminForm` (the `AdminService` demo methods stay — Task 4 and tests use them).
- [ ] **Step 4: Run** the filter and `dotnet build SkyWings.slnx` (no new warnings) — expect PASS.
- [ ] **Step 5: Commit** `"Staff Bookings tab loads one flight date at a time and finds any reference; Demo tab removed"`

### Task 4: `--reset-simulation`, help wording, README, final check

**Files:**
- Create: `WinFormsApp3/CommandLine.cs`
- Modify: `WinFormsApp3/Program.cs` (before the sign-in loop)
- Modify: `WinFormsApp3/HelpForm.cs` (topic title/body), `WinFormsApp3.Tests/HelpContentTests.cs`, `README.md`
- Test: `WinFormsApp3.Tests/CommandLineTests.cs`

**Interfaces:**
- `internal static class CommandLine { public static string? Run(string[] args, BookingStore store, DateTime now); }` — `"--reset-simulation"` (case-insensitive) → `new SimulationService(store).Reset(now)`, writes an audit entry (`"SIMULATION_RESET"`, user `"command-line"`), returns `$"Simulated bookings regenerated for {n} flight dates."`; any other args → null.
- `Program.Main`: `if (CommandLine.Run(Environment.GetCommandLineArgs().Skip(1).ToArray(), store, DateTime.Now) is { } message) { MessageBox.Show(message, "SkyWings"); return; }`
- Help: topic "About this demo: aircraft and seat maps" → "Aircraft and seat maps" without the "demonstration system"/"for demonstration only" sentences; "Trip status" loses its demo paragraph.

- [ ] **Step 1: Failing tests:** `ResetSimulation_RegeneratesOnlySimulatedBookings` (real booking + real check-in survive; simulated bookings exist afterwards); `UnknownArguments_DoNothing`; `NoHelpTopicMentionsDemo` (no topic title/body contains "demo", case-insensitive); update `TripStatusTopic_NamesEveryStatus` (drop the "demo travellers" assertion).
- [ ] **Step 2: Run** `--filter "FullyQualifiedName~CommandLineTests|FullyQualifiedName~HelpContentTests"` — expect FAIL.
- [ ] **Step 3: Implement**; README: replace the "Demo data" section with a short "Simulated traffic" developer note (always on, `is_simulated` marker, `--reset-simulation`).
- [ ] **Step 4: Run** `dotnet test SkyWings.slnx` — expect all PASS.
- [ ] **Step 5: Visual check:** render the staff window (Bookings tab, today) offscreen with a throwaway STA test, as before; confirm no Demo tab, `SKY-` references, legend intact; delete the throwaway test.
- [ ] **Step 6: Commit** `"Hidden --reset-simulation switch; help and README no longer mention a demo"`
