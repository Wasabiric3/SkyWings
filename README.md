# SkyWings

An airline booking desk for Philippine staff, built with Windows Forms on .NET 10 and SQLite. It is a port of an earlier Java app.

Staff sign in with their own accounts, then can search flights, run the six-step booking wizard (flight, passengers, class and extras, seats, payment, receipt), manage bookings, check passengers in, and use the admin console (flights, staff, audit log, reports, encrypted backup and restore).

The booking wizard follows the Figma design: the six steps are pills at the top (a step already visited can be clicked to go back), a trip summary with the running total stays at the right, and the footer has Back (on the first step it closes the wizard) and the main action. Fields are rounded and dates open a month calendar. The same look is being rolled out to the other screens one at a time.

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

## Publish

```
dotnet publish WinFormsApp3/SkyWings.csproj -p:PublishProfile=win-x64 -o publish
```

This makes one self-contained `publish/SkyWings.exe` (about 50 MB) that runs on Windows 10/11 x64 without .NET installed. The profile is `WinFormsApp3/Properties/PublishProfiles/win-x64.pubxml`. GitHub Actions (`.github/workflows/ci.yml`) builds, runs the tests and publishes this exe on every push to `main` and every pull request.
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
- `db.properties` holds the mail password. Do not commit it or share screenshots of it. To stop it sitting in plain text, start the app from a terminal with `SkyWings.exe --protect-smtp-password`: it replaces `smtp.password` with a `dpapi:` value that only the same Windows user on the same PC can read (copy the file to another PC or user and the password must be entered again as plain text, then protected again).
- Every email has an **Unsubscribe** button. SkyWings has no public website, so the button opens a ready-written email to the `smtp.from` address (subject "Unsubscribe <reference>"); staff then stop the emails from Manage booking. Without mail settings (outbox only) the button is left out and the email tells customers to contact staff.

## Simulated traffic (developer note)

The store seeds sample flights and bookings (`BookingStore.Seed.cs`). Review this before using the app with real customers.

A simulation runs unseen in the background so the system behaves like a live operation: each day's flights are filled to 60–90% until about 500 bookings are reached (the `DailyBookingBudget` option; the other flights stay quiet), a few sold-out cabins a day (mostly Business or First, sometimes a full Economy cabin) have a waitlist of made-up travellers (flagged by `waitlist_entries.is_simulated`, never emailed) that keeps growing and getting offers; each offered traveller books (about 60%), declines (15%) or never answers (25%), so entries move on to converted, cancelled or expired, travellers check in during the 24-hour window (about 8% of bookings end as no-shows), seat holds come and go, and bookings are made and cancelled once a minute while the home screen is open. Simulated bookings look like any other (`SKY-` references, realistic passenger details) and count in the Bookings tab, reports and revenue. The only marker is the `bookings.is_simulated` column; simulator code never creates, changes, checks in or deletes a booking with `is_simulated = 0`.

To wipe and regenerate the simulated traffic (real bookings are kept), start the app from a terminal with:

```
SkyWings.exe --reset-simulation
```

It shows how many flight dates were regenerated and exits.

There are three kinds of staff account: **Agent**, **Admin** and **Super Admin**. Sign-in is the same for everyone: enter your username and password and SkyWings opens with the rights of your account. Only a Super Admin can create or change Admin accounts. From a terminal, `SkyWings.exe --make-superadmin <username>` makes an existing account a Super Admin and `SkyWings.exe --rename-staff <old> <new>` changes a sign-in name; neither changes the password.

Super Admins also get a **Database** tab in the staff window. Pick any table to see its rows, click a row to load it into the fields on the left, and use INSERT, SEARCH, UPDATE, DELETE and CLEAR. Password hashes are hidden; the staff, audit and migration tables are view-only; inserts, updates and deletes are recorded in the audit log (without the values).

The staff **Bookings** tab shows one flight date at a time (default today); typing a full booking reference in its search box finds that booking on any date.

## Trip status

Each booking shows where its trip is right now, worked out from the flight's times (delays included) and its check-ins: **Upcoming → Check-in open → Checked in → Boarding → In flight → Completed**, or **No-show** (the flight left and nobody on the booking checked in), or **Canceled**. Return trips show the return leg as "Return · …" once the outbound has landed. The status appears in the staff Bookings tab (with filters), the booking detail, Manage booking and Check-in. Nothing extra is stored: the status is always current.

## Changing a flight and refunds

In **Manage booking**, **Change flight** moves a confirmed booking's outbound leg to another flight or date on the same route and in the same cabin. The traveller pays any increase in base fare plus 12% tax (none for all-senior/PWD bookings) and a PHP 500 change fee per traveller; a cheaper flight is not refunded, and add-ons or promo codes are not recalculated. Seats are assigned automatically (the first free seats in the cabin), the old seats go back to the waitlist, and the charge is saved as a `CHG-...` row in `booking_payments`. It is refused after any traveller has checked in, after departure, and when the outbound flight would not leave before a round trip's return flight. The fee is `FlightChangeRules.FeePerPassenger`.

The admin **Refunds** tab lists cancelled bookings with the refund paid back (reference, original payment method, who handled it) and the total refunded; it can search, include cancellations with no refund, and export CSV. A second toolbar row sets how the list is shown: **period** (any time, today, last 7 days, last 30 days, this month), **sort** (newest, oldest, largest, smallest refund) and **handled by** (anyone, staff, customer desk, system). The refund's own time is used; older cancellations that have none use their flight date and always come after refunds with a recorded time. In the Refund requests view the period hides old closed requests, never an open one. The choices are remembered (`app_settings`, `view_refund_*`). Cancellations saved before refunds had their own record appear without a refund reference.

Cancelling a booking saves the refund as an `RFD-...` row in `booking_payments` (status `REFUND`, same transaction as the status change), and Manage booking shows that reference. Cancellations by staff, customers and the simulator all record it.

## Staff list exports and the audit trail

The Bookings tab (the rows currently shown) and the Waitlist tab have **Export CSV**; each export is audited (who, how many rows, never the contents). Edits made in the Super Admin **Database** tab are audited with the changed columns and their before and after values; secrets and customer details (ID numbers, phone, email, address, birthday, card) show as `(hidden)`.

## Refund requests

The receipt and boarding pass emails (while the flight has not left, and only when mail settings give a `smtp.from` address) have a **Request cancellation / refund** button, built like Unsubscribe: it opens a ready-written email to the desk address with the subject "Refund request SKY-...", and says what the refund would be today. SkyWings reads that mailbox itself (see below) and writes each request into the Refunds tab (view **Refund requests**). A person can also log one by hand: **Log refund request** in Manage booking, or **Log request** in the Refunds tab.

**Reading the mailbox.** While the app is open it checks the desk mailbox every minute (and when you press **Refresh** in the Refund requests view) over IMAP with TLS (`imap.gmail.com:993` for Gmail). It reuses the sending account: `imap.host` is `imap.` plus the `smtp.host` name, and the user and password are the `smtp.*` ones, so a Gmail account with an app password needs nothing extra. Optional keys in `db.properties`: `imap.enabled=false` (switch reading off), `imap.host`, `imap.port`, `imap.user`, `imap.password` (another mailbox). Personal Gmail has IMAP always on; the account needs 2-Step Verification and an app password. To test the login without saving anything, start the app from a terminal with `SkyWings.exe --check-refund-mail`.

What it does and does not do: it is read-only (nothing is marked as read, moved or deleted); it only looks at messages whose subject contains `Refund request SKY-` and a booking reference; it downloads only the plain-text part (no HTML, no attachments, nothing over 256 KB) and keeps the first 1,000 characters; it remembers where it stopped (mailbox UID validity and last UID in `app_settings`), so a message is handled once, even after a restart. The sender is compared with the booking's own email address, and a request from another address is still logged but flagged in the list and in the Approve dialog. The customer gets an acknowledgement email at the booking's address (never at the sender's, so a forged email cannot make SkyWings write to a stranger). A request is only ever written down; nothing is cancelled or refunded until staff press Approve. If the mailbox cannot be reached (no network, wrong password) the app logs it and carries on, and Refresh says so.

A logged request only writes the request down (`refund_requests` table); the booking stays active. The Refunds tab button shows how many are waiting, e.g. `Refunds (2)`. From the Requests view staff choose:

- **Approve** shows the request, the customer's message (when one was recorded), and the refund the policy gives today; after a yes it cancels the booking, pays the refund back to the original payment method (a demo payment), closes the request and emails the customer that the refund is **approved and refunded**, with the amount and the `RFD-...` reference. It needs the right to cancel bookings.
- **Decline** keeps the booking, records a reason, and (ticked by default) emails the customer that the request could not be approved and why.
- Cancelling the booking yourself from Manage booking or the Bookings tab also closes the request (status Done). One open request per booking; none can be logged for a booking that is cancelled, has departed, or is a simulated booking. Logging, dismissing and closing are audited.

Every real cancellation (staff, desk or Manage booking) then emails the customer a cancellation notice with the refund amount, the `RFD-...` reference and the payment method refunded to. It follows the usual email rules: no address, an unsubscribed address, or a simulated booking means no email, and a mail problem never undoes the cancellation.