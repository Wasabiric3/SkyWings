# Trip lifecycle: bookings that finish, and demo travellers who check in

Date: 2026-11-19 · Branch: `aircraft-sim`

## Why

For the thesis defense the system should look like a live airline. Today the departures board already moves a flight from
Scheduled to Landed using its real duration, but a **booking** never moves: it is `CONFIRMED` forever, so a trip that landed
yesterday looks unfinished in the Bookings tab, the booking detail and Manage booking. Demo travellers also never check in,
so nothing ever shows "Checked in", no boarding passes exist for them, and nobody is ever a no-show.

## Goals

- Every booking shows where its trip is **right now**, derived from the clock and the flight's timeline (departure + delay +
  duration): **Upcoming → Check-in open → Checked in → Boarding → In flight → Completed**, or **No-show**, or **Canceled**.
- Demo travellers check in by themselves during the 24-hour window, spread realistically over it, about 92% in total;
  about 8% of demo bookings never check in and become No-show once the flight leaves.
- Correct even if the app was closed for a day: statuses are computed, and missed demo check-ins catch up with back-dated times.

## Non-goals

- No new booking status stored in the database (`CONFIRMED` keeps meaning "holds a seat" for seat counts, waitlist and reports).
- No per-passenger no-show (a booking where some travellers checked in counts as flown).
- No gate/boarding scan simulation, no changes to reports or revenue.
- Demo bookings stay one-way, as today.

## Anti-goals

- A real booking must never be checked in by the simulator, and no real data may change.
- Demo travellers must never appear checked in before check-in opens.

## Approaches considered

- **A. Computed trip status + simulator checks in through the normal store path** — chosen. Always right, nothing new stored,
  existing `CONFIRMED` logic untouched.
- B. Background sweep writing COMPLETED / NO_SHOW into `bookings.status` — rejected: dozens of queries treat `CONFIRMED` as
  "holds a seat", and statuses would be wrong until the sweep ran.
- C. Pre-create all check-ins when demo loads are generated — rejected: travellers would look checked in days early.

## Design

### 1. Trip status (pure, `Controllers/TripStatus.cs`)
`TripStatus.Of(BookingRecord booking, IReadOnlyCollection<CheckInRecord> checkIns, DateTime now)` returns a phase, a label and
a colour key. It uses `FlightStatusEngine.Evaluate` for each leg, so delays and durations match the board exactly.

| Flight timeline (active leg) | Trip phase / label |
|---|---|
| Canceled booking | Canceled |
| Before check-in opens | Upcoming |
| Check-in open, nobody on the booking checked in | Check-in open |
| Check-in open, at least one checked in | Checked in |
| Boarding / gate closing | Boarding (check-in stays open until departure, so no No-show yet) |
| Departed / in flight | In flight (No-show if nobody checked in) |
| Landed | Completed (No-show if nobody checked in) |

Round trips: the outbound leg decides until it lands; then the return leg decides, labelled e.g. "Return · In flight";
Completed only after the return lands. No-show on either leg is final.

### 2. Where it shows
- **Staff › Bookings tab:** the Status column shows the trip label; the filter becomes All / Upcoming / In progress / Completed /
  No-show / Canceled. Check-ins for all listed bookings come from **one** bulk query (new store method), not one per row.
- **Booking detail:** the status pill shows the trip label with its colour.
- **Manage booking:** the banner says "This trip is completed. It landed in Cebu at 10:25 AM." / "No-show: nobody on this
  booking checked in." instead of only "already departed".
- **Check-in screen:** the closed banner says "landed" once the flight has landed.

### 3. Demo travellers check in (`SimulationService`, runs inside `RunIfEnabled` each minute)
- Candidates: confirmed `SIM-` bookings whose flight departs between 7 days ago and 24 h from now, with travellers not yet
  checked in (one store query).
- Per booking, a stable seed (reference) decides **no-show** (~8%). Otherwise each traveller gets a stable check-in moment
  inside the window: most in the last 12 hours, some right when it opens, never after departure.
- If that moment ≤ now, the traveller is checked in **with that moment as `checked_in_at`** (so catching up after a closed app
  looks natural). All inserts in one transaction.
- Real bookings are never candidates (`is_simulated = 1` only).

### 4. Fix found while designing
`DeleteSimulatedBookings` (used by Reset demo data and the load-version regeneration) does not delete demo **check-ins**. It
will, in the same transaction.

### 5. Help and README
Short note on the trip statuses and that demo travellers check in by themselves.

## Testing
- TripStatus: every row of the table, a delayed flight, a round trip on each leg, no-show, partial check-in, canceled.
- Simulator: none checked in before the window; about 92% by departure (± a few %); stable for the same seed; back-dated times
  fall inside the window; catch-up after a long gap; real bookings untouched; reset removes demo check-ins.
- Admin: the new filter values pick the right bookings.
- Full suite stays green; screenshot of the Bookings tab and Manage booking as a final visual check.

## Left to the builder
Exact label colours (from `Theme`), the check-in time distribution's exact shape, and the bulk check-in query's shape.
