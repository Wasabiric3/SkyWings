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

If a customer gives an email address while booking, SkyWings sends a receipt with a QR code (the booking reference). Customers can switch emails off, or ask for the receipt again, in **Manage booking**. Check-in and Manage booking also accept a scanned QR code in the reference box.

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
- Unsubscribe is done inside the app (no web link), because SkyWings has no public website.

## Demo data

The store seeds sample flights and bookings (`BookingStore.Seed.cs`). Review this before using the app with real customers.
