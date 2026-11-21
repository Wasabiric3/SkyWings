# SkyWings

An airline booking desk for Philippine staff, built with Windows Forms on .NET 10 and SQLite. It is a port of an earlier Java app.

Staff sign in with their own accounts, then can search flights, run the six-step booking wizard (flight, passengers, class and extras, seats, payment, receipt), manage bookings, check passengers in, and use the admin console (flights, staff, audit log, reports, encrypted backup and restore).

## Requirements

- Windows
- .NET 10 SDK

## Build and run

```
dotnet build SkyWings.slnx
dotnet run --project WinFormsApp3/SkyWings.csproj
```

On the first run you are asked to create an admin account.

## Tests

```
dotnet test SkyWings.slnx
```

Tests use a temporary data folder, so they never touch real data.

## Project layout

| Path | What is in it |
|------|---------------|
| `WinFormsApp3/` | The app (project `SkyWings.csproj`, namespace `SkyWings`) |
| `WinFormsApp3/Security/` | Passwords, backup encryption, roles, booking rules, data folder protection |
| `WinFormsApp3/UI/` | Custom "glass" controls, seat map, shared UI helpers |
| `WinFormsApp3/BookingStore*.cs` | All database access (one partial class, split by topic) |
| `WinFormsApp3.Tests/` | MSTest unit tests |

## Data and settings

- The database is `skywings.db` in `%LocalAppData%\SkyWings`. The folder is restricted to the current Windows user.
- Set the `SKYWINGS_DATA_DIR` environment variable to store data somewhere else.
- Logs go to `%LocalAppData%\SkyWings\logs\skywings.log`. They rotate at 5 MB and old files are removed after 30 days.
- Optional settings live in `db.properties`, next to the exe or in the data folder. Only `db.url` is still used. The `admin.*` keys are read once to import an old admin account and can then be deleted.
- Backups are encrypted `.skybak` files (AES-256-GCM). The backup password cannot be recovered.

## Receipt emails and QR codes

If a customer gives an email address while booking, SkyWings sends a receipt with a QR code (the booking reference). Staff can send the receipt again, to the address saved on the booking, with **Send this receipt** in **Manage booking**. The email carries the same one-page A4 receipt as **Print receipt**, as a PDF attachment. Check-in and Manage booking also accept a scanned QR code in the reference box.

When travellers are checked in, SkyWings also emails their boarding pass (a PDF, with the same QR code) to the address on the booking. It follows the same rules as receipts: no address or an unsubscribed address means no email, demo bookings never send, and a mail problem never undoes the check-in. On the check-in screen, **Email boarding pass** sends the passes again for everyone checked in on that flight, and shows a message when the booking has no email address.

- With no mail settings, each email is saved as an `.html` file in `%LocalAppData%\SkyWings\outbox`. Open it in a browser.
- To send real emails, add these keys to `db.properties` (next to the exe or in the data folder). For Gmail use an app password, not your normal password.

```
smtp.host=smtp.gmail.com
smtp.port=587
smtp.ssl=true
smtp.user=your.address@gmail.com
smtp.password=your-app-password
smtp.from=your.address@gmail.com
```

- If the mail server cannot be reached, the email is saved to the outbox instead, so nothing is lost.
- `db.properties` holds a password in plain text. Do not commit it or share screenshots of it.
- Every email has an **Unsubscribe** button. SkyWings has no public website, so the button opens a ready-written email to the `smtp.from` address (subject "Unsubscribe <reference>"); staff then stop the emails from Manage booking. Without mail settings (outbox only) the button is left out and the email tells customers to contact staff.

## Simulated traffic (developer note)

The store seeds sample flights and bookings (`BookingStore.Seed.cs`). Review this before using the app with real customers.

A simulation runs unseen in the background so the system behaves like a live operation: each day's flights are filled to 60–90% until about 500 bookings are reached (the `DailyBookingBudget` option; the other flights stay quiet), a few sold-out cabins a day (mostly Business or First, sometimes a full Economy cabin) have a waitlist of made-up travellers (flagged by `waitlist_entries.is_simulated`, never emailed) that keeps growing and getting offers; each offered traveller books (about 60%), declines (15%) or never answers (25%), so entries move on to converted, cancelled or expired, travellers check in during the 24-hour window (about 8% of bookings end as no-shows), seat holds come and go, and bookings are made and cancelled once a minute while the home screen is open. Simulated bookings look like any other (`SKY-` references, realistic passenger details) and count in the Bookings tab, reports and revenue. The only marker is the `bookings.is_simulated` column; simulator code never creates, changes, checks in or deletes a booking with `is_simulated = 0`.

To wipe and regenerate the simulated traffic (real bookings are kept), start the app from a terminal with:

```
SkyWings.exe --reset-simulation
```

It shows how many flight dates were regenerated and exits.

The staff **Bookings** tab shows one flight date at a time (default today); typing a full booking reference in its search box finds that booking on any date.

## Trip status

Each booking shows where its trip is right now, worked out from the flight's times (delays included) and its check-ins: **Upcoming → Check-in open → Checked in → Boarding → In flight → Completed**, or **No-show** (the flight left and nobody on the booking checked in), or **Canceled**. Return trips show the return leg as "Return · …" once the outbound has landed. The status appears in the staff Bookings tab (with filters), the booking detail, Manage booking and Check-in. Nothing extra is stored: the status is always current.
