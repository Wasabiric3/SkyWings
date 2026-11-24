---
name: skywings-dev
description: Working guide for the SkyWings airline booking desk (WinForms, .NET 10, SQLite) in this repo. Use this whenever you change, add, or debug anything in SkyWings: booking wizard, check-in, manage booking, seat map, flights and aircraft admin, staff accounts, audit log, backups, receipt emails and QR codes, waitlist, the demo simulator, or the database schema. Also use it for build and test commands, adding a database column or table, adding a form or control, or adding a unit test, even if the user does not say "SkyWings" or name a file.
---

# SkyWings development guide

SkyWings is a Windows Forms desktop app for Philippine airline booking staff. It runs on .NET 10 with a local SQLite database. The code is in `WinFormsApp3/`, the tests are in `WinFormsApp3.Tests/` (MSTest), and the README covers the user-facing behaviour.

## Build, run, test

Run these from the repo root:

```
dotnet build SkyWings.slnx
dotnet run --project WinFormsApp3/SkyWings.csproj
dotnet test SkyWings.slnx
```

Run the full test suite after any change to the store, the schema, or the business rules. Tests set `SKYWINGS_DATA_DIR` to a temp folder in `TestEnvironment.cs`, so they never touch real data. Do not remove that setup.

Before you say something works, run the build and the relevant tests and report the actual output. Don't assume a passing build means the feature works.

## Where things live

| Area | Files |
|------|-------|
| Database access | `BookingStore.cs` (schema, connection, helpers) and partial files `BookingStore.<Topic>.cs` (Admin, Aircraft, Audit, CheckIn, Confirm, Email, Features, Manage, Seats, Simulation, Staff, Waitlist, Seed) |
| Screens | `Forms/*.cs` (`HomeForm`, `BookingWizardForm`, `ManageBookingForm`, `CheckInForm`, `AdminForm`, `FlightEditorForm`, `StaffLoginForm`, ...) |
| Screen logic | `Controllers/*.cs` (`BookingWizardController`, `CheckInController`, `ManageBookingController`, `HomeController`, `FlightSearch`, `FlightLoad`, `CustomerLookup`, ...) |
| Rules and security | `Security/` (`BookingRules`, `PasswordPolicy`, `AdminService`, `BackupCrypto`, `AppPaths`, `SecurityHelpers`, `StaffModels`) |
| Email and QR | `Email/` (`BookingEmailBuilder`, `BookingEmailService`, `SmtpEmailSender`, `FileOutboxEmailSender`, `FallbackEmailSender`, `QrCodeRenderer`) |
| Shared UI | `UI/` (`GlassControls`, `GlassForm`, `SeatMap`, `SeatPalette`, `Layouts`, `GridStyle`, `StatTile`, `ReceiptPrinter`, ...) |
| Look and colours | `Theme.cs` (colours, fonts, spacing) and `UI/Colors.cs` |
| Models | `Models.cs`, `Aircraft.cs`, `SeatLayout.cs`, `BookingSummary.cs`, `FarePricing.cs` |
| Logging | `AppLogger.cs`, written to `%LocalAppData%\SkyWings\logs\skywings.log` |

Before you edit a file, read the nearby code and follow its style. Don't reorganise files that were not part of the task.

## Database rules

- All SQL goes through `BookingStore` and its partial files. Screens and controllers should not open SQLite connections themselves.
- Each public store method takes `lock (_lock)`, matching the existing methods. Follow that pattern.
- Use the existing helpers: `Cmd(con, sql, tx, params...)` for parameterised commands, `Exec(con, sql, params...)` for non-queries, and the readers `Str`, `StrOrNull`, `Int`, `Long`, `Money`, and `Dt`. `Money` converts to `decimal` with the invariant culture, so use it for every money column.
- Always use `@parameters`. Never build SQL by concatenating values.
- Wrap multi-step writes in one transaction. Use the `tx` argument on `Cmd`.
- Log failures with `LogError(e)` and rethrow a meaningful exception when the caller needs to know. Existing code does this, for example in `LoadFlightInventory`. Don't turn an error into a silent zero.

### Adding a column or table

Schema setup happens in `BookingStore.InitSchema()` and in the partial files' schema methods.

- New tables go in the DDL array as `CREATE TABLE IF NOT EXISTS ...`.
- New columns on existing tables go through `EnsureColumn(con, "ALTER TABLE ... ADD COLUMN ... NOT NULL DEFAULT ...")`. Always give a default, so old databases still open.
- Make sure a database created before your change still upgrades cleanly. The commit history has a fix for an old-table upgrade test. Add a test that creates the old shape and then opens the store.

## Business rules

Booking rules are in `Security/BookingRules.cs`, and seat-hold timing is covered by `SeatHoldClockTests`. Store times with `DateTime.Now`, the same local clock the holds are written with. Mixing UTC and local time caused a past bug (see git history for "seat-hold expiry").

Demo and simulator data are marked with `is_simulated = 1` in `bookings`. Keep this marker when you write any code that creates demo rows, and filter it out where real reports need only real bookings. The simulator logic lives in `BookingStore.Simulation.cs` and `SimulationService.cs`.

## UI rules

- Take colours, fonts, and spacing from `Theme` and `Colors`. Don't hard-code colours in forms.
- Prefer the shared controls in `UI/` (`GlassButton`, `GlassCard`, `GlassForm`, `StatTile`, `SeatMap`) over new custom painting.
- Keep form logic in the controller where one exists. Forms should handle layout and events.
- Keep the app usable at its current size. Check layouts with `Layouts.cs` and the tests `PanelPaintTests` and `GlassButtonTests` when you change them.
- Help text lives in `HelpForm` and `HelpContentTests` checks it. Update both when behaviour the help describes changes.

## Tests

- Framework: MSTest 4. Use `[TestClass]` and `[TestMethod]`, and the global `using` is already in the project.
- Put a new store test in a new file named after the feature, such as `FlightLoadTests.cs`. Create the store on a fresh temp database, as `AircraftSeatCountTests.cs` does: `new BookingStore(new SqliteConnectionStringBuilder { DataSource = path, ForeignKeys = true }.ConnectionString)`.
- Test pure logic (`BookingRules`, `FarePricing`, `SeatLayout`, controllers) directly, without a form.
- Don't write tests that depend on the real `%LocalAppData%` data folder.

## Security and privacy

- Passwords are hashed by `PasswordHasher` and checked against `PasswordPolicy`. Never store or log a plain password.
- Backups are AES-256-GCM encrypted (`BackupCrypto`). Don't add a path that writes an unencrypted backup.
- `db.properties` can hold SMTP passwords in plain text. Don't commit it, and don't print its values in logs or test output.
- Customer data (names, ID numbers, birthdays) must not appear in logs or in example data that is committed. Use made-up names in tests.

## Workflow for a change

1. Read the relevant store partial, controller, and form before you edit.
2. Make the smallest change that does the job. Put logic in the store or controller, and keep forms thin.
3. If the schema changes, add an `EnsureColumn` or DDL entry and a test that upgrades an old database.
4. Add or update tests next to the feature's existing tests.
5. Run `dotnet build SkyWings.slnx` and `dotnet test SkyWings.slnx`. Report the results, including any failures.
6. If the change affects what users see, update `README.md` or `HelpForm` text.
