# Live operations: the simulation runs unseen, and its bookings look and count like real ones

Date: 2026-11-19 · Approved by the user (option A)

## Why
For the thesis defense SkyWings should look and behave like an airline system in real-time operation. The simulation should
keep running, but nothing on screen should say "demo": no Demo tab, no `SIM-` references, no "Demo data" passengers, and the
Bookings tab and Reports should show the simulated traffic like any other traffic.

## Goals
- Remove the staff Demo tab. The simulation always runs (it is already on by default).
- Simulated bookings count everywhere: Bookings tab, Reports, revenue, check-ins.
- Simulated bookings look real: references `SKY-` + 12 hex (same as real), realistic address, phone, ID number and payment
  reference. Existing simulated bookings are regenerated once in the new style.
- The Bookings tab shows one flight date at a time (default today); a full booking reference in the search box finds that
  booking on any date. (Loading all ~167k bookings took 26 s on the UI thread.)
- No "demo" wording on screen: the help topic "About this demo: aircraft and seat maps" becomes "Aircraft and seat maps"; the
  demo sentence leaves "Trip status". The README keeps a short developer note.
- Hidden reset: `SkyWings.exe --reset-simulation` deletes and regenerates only simulated bookings, then exits.

## Non-goals / anti-goals
- Real bookings are never changed by the simulator (the `is_simulated` column stays the marker; guards move from the `SIM-`
  reference prefix to that column).
- No change to how the simulation fills flights or checks travellers in.

## Approach
Keep `is_simulated` as the only marker; drop the reference prefix. Make "include simulated in reports" the default and set it
once during the version-3 regeneration (there is no UI to change it any more). Load the Bookings tab per date with bulk
queries instead of one passenger query per booking.
