namespace SkyWings;

/// <summary>
/// Every staff action goes through here: it checks the signed-in role's permission first, performs the action, and writes
/// an audit entry. The admin window uses only this class, so a hidden button is never the only protection.
/// </summary>
public sealed class AdminService
{
    private readonly BookingStore _store;

    public AdminService(BookingStore store, StaffSession session)
    {
        _store = store;
        Session = session;
    }

    public StaffSession Session { get; }

    private void Audit(string action, string target = "", string detail = "")
        => _store.WriteAudit(Session.Username, RolePermissions.ToDb(Session.Role), action, target, detail);

    // ------------------------------------------------------------------ bookings and reports

    public List<BookingRecord> LoadBookings()
    {
        Session.Require(Permission.ViewBookings);
        return _store.LoadBookings();
    }

    public BookingRecord? FindBooking(string reference)
    {
        Session.Require(Permission.ViewBookings);
        return _store.FindByReference(reference);
    }

    /// <summary>The bookings whose outbound flight is on <paramref name="date"/> (what the Bookings tab shows).</summary>
    public List<BookingRecord> LoadBookingsForDate(DateTime date)
    {
        Session.Require(Permission.ViewBookings);
        return _store.LoadBookingsForDate(date);
    }

    /// <summary>The trip status of a date's bookings, reading only that date's check-ins.</summary>
    public Dictionary<string, Controllers.TripStatusInfo> LoadTripStatusesForDate(IReadOnlyList<BookingRecord> bookings, DateTime date, DateTime now)
    {
        Session.Require(Permission.ViewBookings);
        Dictionary<string, List<CheckInRecord>> checkIns = _store.LoadCheckInsForDate(date);
        var none = new List<CheckInRecord>();
        return bookings.ToDictionary(b => b.Reference,
            b => Controllers.TripStatus.Of(b, checkIns.GetValueOrDefault(b.Reference) ?? none, now), StringComparer.Ordinal);
    }

    private static readonly System.Text.RegularExpressions.Regex ReferencePattern =
        new("^SKY-[0-9A-F]{12}$", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>The search text as a booking reference ("SKY-" + 12 hex, any case, spaces ignored), or null when it is not one.</summary>
    internal static string? AsReference(string search)
    {
        string text = search.Trim().ToUpperInvariant();
        return ReferencePattern.IsMatch(text) ? text : null;
    }

    /// <summary>Why staff cannot cancel this booking, or null when they can.</summary>
    public string? CancelRefusal(BookingRecord booking)
        => booking.Status == AppData.StatusCanceled ? "This booking is already canceled." : null;

    /// <summary>The live trip status of one booking, reading only its own check-ins.</summary>
    public Controllers.TripStatusInfo LoadTripStatus(BookingRecord booking, DateTime now)
    {
        Session.Require(Permission.ViewBookings);
        return Controllers.TripStatus.Of(booking, _store.LoadCheckIns(booking.Reference), now);
    }

    /// <summary>The live trip status of each booking (by reference), with every check-in read in one query.</summary>
    public Dictionary<string, Controllers.TripStatusInfo> LoadTripStatuses(IReadOnlyList<BookingRecord> bookings, DateTime now)
    {
        Session.Require(Permission.ViewBookings);
        // Demo check-ins are only needed when the Bookings tab shows demo bookings.
        Dictionary<string, List<CheckInRecord>> checkIns = _store.LoadCheckInsByBooking(_store.IncludeSimulatedInReports);
        var none = new List<CheckInRecord>();
        return bookings.ToDictionary(b => b.Reference,
            b => Controllers.TripStatus.Of(b, checkIns.GetValueOrDefault(b.Reference) ?? none, now), StringComparer.Ordinal);
    }

    /// <summary>Cancels a confirmed booking with the given refund. False when it was no longer confirmed.</summary>
    public bool CancelBooking(string reference, decimal refund)
    {
        Session.Require(Permission.CancelBookings);
        bool done = _store.CancelBooking(reference, refund, Session.Username);
        Audit(done ? "BOOKING_CANCELED" : "BOOKING_CANCEL_SKIPPED", reference, done ? $"Refund PHP {refund:N2}" : "Already canceled or changed");
        return done;
    }

    public List<WaitlistEntry> LoadWaitlist()
    {
        Session.Require(Permission.ViewBookings);
        return _store.LoadAllLiveWaitlist();
    }

    /// <summary>
    /// Frees seats in a sold-out cabin (by cancelling a booking in it that is not a real customer's) so the waitlist logic offers them
    /// to the first traveller in line. Returns null when the cabin has no such booking; <see cref="WaitlistSeatRelease.OfferedTo"/>
    /// is null when the seats were freed but nobody could be offered them (for example the flight is inside the booking cutoff).
    /// </summary>
    public WaitlistSeatRelease? ReleaseSeatForWaitlist(string flightNo, DateTime flightDate, string className)
    {
        Session.Require(Permission.CancelBookings);
        DateTime started = DateTime.Now.AddSeconds(-1);
        var freed = new SimulationService(_store).FreeSeatsInCabin(flightNo, flightDate, className);
        if (freed is null)
        {
            Audit("SEAT_RELEASE_SKIPPED", flightNo, $"{flightDate:yyyy-MM-dd} {className}: nothing to release");
            return null;
        }
        WaitlistEntry? offered = _store.LoadWaitlistDetails(flightNo, flightDate)
            .Where(e => e.ClassName == className && e.Status == BookingStore.WaitlistOffered && e.OfferedAt >= started)
            .OrderBy(e => e.OfferedAt).FirstOrDefault();
        Audit("SEAT_RELEASED", flightNo, $"{flightDate:yyyy-MM-dd} {className} x{freed.Value.Seats}" + (offered is null ? "" : $", offered to {offered.BookingReference}"));
        return new WaitlistSeatRelease(freed.Value.Seats, offered);
    }

    /// <summary>Removes a waitlist entry. The seats it held (if it was offered) go to the next traveller in line.</summary>
    public bool RemoveWaitlistEntry(string code)
    {
        Session.Require(Permission.CancelBookings);
        bool done = _store.CancelWaitlistEntry(code);
        Audit(done ? "WAITLIST_REMOVED" : "WAITLIST_REMOVE_SKIPPED", code);
        return done;
    }

    public ReportStats? LoadReport()
    {
        Session.Require(Permission.ViewReports);
        return _store.LoadReportStats();
    }

    // ------------------------------------------------------------------ flights

    public List<FlightAdminRow> LoadFlights()
    {
        Session.Require(Permission.ManageFlights);
        _store.SeedDefaultFlights();
        return _store.LoadAllFlights();
    }

    public FlightInventory LoadFlightInventory(string flightNo, DateTime flightDate)
    {
        Session.Require(Permission.ManageFlights);
        return _store.LoadFlightInventory(flightNo, flightDate);
    }

    public int CountActiveBookingsForFlight(string flightNo)
    {
        Session.Require(Permission.ManageFlights);
        return _store.CountActiveBookingsForFlight(flightNo);
    }

    public bool FlightExists(string flightNo)
    {
        Session.Require(Permission.ManageFlights);
        return _store.FlightExists(flightNo);
    }

    public void SaveFlight(string flightNo, string origin, string destination, decimal basePrice,
        bool domestic, int capacity, bool active, TimeOnly? departureTime)
    {
        Session.Require(Permission.ManageFlights);
        bool isNew = !_store.FlightExists(flightNo);
        _store.SaveFlight(flightNo, origin, destination, basePrice, domestic, capacity, active, departureTime);
        Audit(isNew ? "FLIGHT_ADDED" : "FLIGHT_EDITED", flightNo,
            $"{origin} to {destination}, fare PHP {basePrice:N2}, capacity {capacity}, {(active ? "active" : "inactive")}");
    }

    /// <summary>The refusal (if any) a change of aircraft would meet, without changing anything.</summary>
    public AircraftChangeResult CheckAircraftChange(string flightNo, string? aircraftId)
    {
        Session.Require(Permission.ManageFlights);
        if (!_store.FlightExists(flightNo)) return new AircraftChangeResult(false, $"{flightNo} does not exist.");
        Aircraft? target = Aircraft.Find(aircraftId);
        if (aircraftId is not null && target is null) return new AircraftChangeResult(false, "Unknown aircraft.");
        if (target is not null)
        {
            List<string> stranded = _store.LoadBookedSeatCodes(flightNo, realOnly: true).Where(code => !target.HasSeat(code)).OrderBy(c => c).ToList();
            if (stranded.Count > 0)
                return new AircraftChangeResult(false,
                    $"{stranded.Count} booked seat(s) would not exist on the {target.Model} ({string.Join(", ", stranded.Take(5))}" +
                    $"{(stranded.Count > 5 ? ", …" : "")}). Move those passengers to other seats first.");
        }
        return new AircraftChangeResult(true, "");
    }

    /// <summary>
    /// Changes (or clears, with null) a flight's aircraft and sets its capacity to the aircraft's seats. Refused when a booked seat
    /// would not exist on the new aircraft, because that passenger would be left without a seat.
    /// </summary>
    public AircraftChangeResult ChangeAircraft(string flightNo, string? aircraftId)
    {
        AircraftChangeResult check = CheckAircraftChange(flightNo, aircraftId);
        if (!check.Ok) return check;
        Aircraft? target = Aircraft.Find(aircraftId);

        string before = _store.LoadAircraft(flightNo).Model;
        _store.SetFlightAircraft(flightNo, target?.Id);
        _store.DeleteSimulatedBookings(flightNo);   // demo seats were laid out for the old aircraft; they are generated again for the new one
        Audit("FLIGHT_AIRCRAFT_CHANGED", flightNo, $"{before} to {target?.Model ?? "standard layout"}");
        return new AircraftChangeResult(true, $"{flightNo} now uses {target?.Model ?? "the standard layout"}.");
    }

    // ------------------------------------------------------------------ demo activity (admin only)

    public DemoStatus LoadDemoStatus()
    {
        Session.Require(Permission.ManageFlights);
        return new DemoStatus(_store.DemoActivityEnabled, _store.IncludeSimulatedInReports, _store.CountSimulatedBookings());
    }

    /// <summary>Switches the simulated live activity on or off. Existing demo bookings stay; they just stop changing.</summary>
    public void SetDemoActivity(bool on)
    {
        Session.Require(Permission.ManageFlights);
        _store.SetSetting(BookingStore.SettingDemoActivity, on ? "1" : "0");
        Audit(on ? "DEMO_ACTIVITY_ON" : "DEMO_ACTIVITY_OFF");
    }

    /// <summary>Whether reports, revenue and the Bookings tab also count demo bookings (off by default).</summary>
    public void SetIncludeDemoInReports(bool on)
    {
        Session.Require(Permission.ManageFlights);
        _store.SetSetting(BookingStore.SettingDemoInReports, on ? "1" : "0");
        Audit(on ? "DEMO_REPORTS_ON" : "DEMO_REPORTS_OFF");
    }

    /// <summary>Fills the flights and takes one live step right away (used when the switch is turned on). Null while the demo is off.</summary>
    public SimulationTick? RunDemoNow()
    {
        Session.Require(Permission.ManageFlights);
        return new SimulationService(_store).RunIfEnabled(DateTime.Now);
    }

    /// <summary>Deletes every demo booking and hold and, if the demo is on, generates them again. Real bookings are never touched.</summary>
    public int ResetDemoData()
    {
        Session.Require(Permission.ManageFlights);
        // With the demo switched off, a reset only clears: it must not quietly fill the flights again.
        int generated;
        if (_store.DemoActivityEnabled) generated = new SimulationService(_store).Reset(DateTime.Now);
        else { _store.DeleteSimulatedBookings(); generated = 0; }
        Audit("DEMO_RESET", detail: $"{generated} flight dates regenerated");
        return generated;
    }

    public void SetFlightActive(string flightNo, bool active)
    {
        Session.Require(Permission.ManageFlights);
        _store.SetFlightActive(flightNo, active);
        Audit(active ? "FLIGHT_ACTIVATED" : "FLIGHT_DEACTIVATED", flightNo);
    }

    // ------------------------------------------------------------------ staff

    public List<StaffAccount> LoadStaff()
    {
        Session.Require(Permission.ManageStaff);
        return _store.LoadStaff();
    }

    public StaffAccount CreateStaff(string username, string displayName, string temporaryPassword, StaffRole role)
    {
        Session.Require(Permission.ManageStaff);
        RequireCanAssign(role);
        StaffAccount created = _store.CreateStaff(username, displayName, temporaryPassword, role, mustChangePassword: true);
        Audit("STAFF_CREATED", created.Username, $"Role {role}");
        return created;
    }

    public void ResetPassword(int staffId, string temporaryPassword)
    {
        Session.Require(Permission.ManageStaff);
        StaffAccount target = FindStaffOrThrow(staffId);
        RequireCanManage(target);
        _store.ResetPassword(staffId, temporaryPassword);
        Audit("STAFF_PASSWORD_RESET", target.Username, "Temporary password set; change required at next sign-in");
    }

    public void SetRole(int staffId, StaffRole role)
    {
        Session.Require(Permission.ManageStaff);
        StaffAccount target = FindStaffOrThrow(staffId);
        RequireCanManage(target);
        RequireCanAssign(role);
        _store.SetStaffRole(staffId, role);
        Audit("STAFF_ROLE_CHANGED", target.Username, $"{target.Role} to {role}");
    }

    public void SetActive(int staffId, bool active)
    {
        Session.Require(Permission.ManageStaff);
        StaffAccount target = FindStaffOrThrow(staffId);
        RequireCanManage(target);
        if (!active && target.Id == Session.Id) throw new InvalidOperationException("You can't deactivate your own account.");
        _store.SetStaffActive(staffId, active);
        Audit(active ? "STAFF_ACTIVATED" : "STAFF_DEACTIVATED", target.Username);
    }

    private const string AdminsOnlyMessage = "Only a Super Admin can change administrator accounts.";

    /// <summary>Administrator accounts need <see cref="Permission.ManageAdmins"/>; the Super Admin account is never changed from the staff screens.</summary>
    private void RequireCanManage(StaffAccount target)
    {
        if (target.Role == StaffRole.SuperAdmin) throw new UnauthorizedAccessException("The Super Admin account can't be changed from here.");
        if (RolePermissions.IsAdministrator(target.Role) && !Session.Has(Permission.ManageAdmins))
            throw new UnauthorizedAccessException(AdminsOnlyMessage);
    }

    /// <summary>Giving someone the Admin role needs <see cref="Permission.ManageAdmins"/>; nobody is made Super Admin from the screens.</summary>
    private void RequireCanAssign(StaffRole role)
    {
        if (role == StaffRole.SuperAdmin) throw new UnauthorizedAccessException("The Super Admin role can't be given from here.");
        if (role == StaffRole.Admin && !Session.Has(Permission.ManageAdmins)) throw new UnauthorizedAccessException(AdminsOnlyMessage);
    }

    private StaffAccount FindStaffOrThrow(int staffId)
        => _store.LoadStaff().FirstOrDefault(s => s.Id == staffId) ?? throw new InvalidOperationException("Staff account not found.");

    /// <summary>Lets any signed-in person change their own password after proving they know the current one.</summary>
    public void ChangeOwnPassword(string currentPassword, string newPassword)
    {
        if (!_store.Authenticate(Session.Username, currentPassword, reauthentication: true).Success)
            throw new UnauthorizedAccessException("The current password is not correct.");
        if (string.Equals(currentPassword, newPassword, StringComparison.Ordinal))
            throw new ArgumentException("Choose a password you haven't used before.");
        _store.ChangePassword(Session.Id, newPassword);
        Session.MustChangePassword = false;
        Audit("PASSWORD_CHANGED", Session.Username);
    }

    /// <summary>Asks for the person's password again before a risky action such as a restore.</summary>
    public bool Reauthenticate(string password)
        => _store.Authenticate(Session.Username, password, reauthentication: true).Success;

    // ------------------------------------------------------------------ backup and restore

    /// <summary>Writes an encrypted backup. <paramref name="password"/> is needed again to restore it.</summary>
    public void Backup(string destination, string password)
    {
        Session.Require(Permission.BackupRestore);
        DatabaseBackup.Backup(_store.ConnectionString, destination, password);
        Audit("BACKUP_CREATED", Path.GetFileName(destination));
    }

    /// <summary>
    /// Restores booking data from an encrypted backup. Needs the backup password and the signed-in person's own password.
    /// Staff accounts and the audit log are never taken from the backup.
    /// </summary>
    public void Restore(string source, string backupPassword, string staffPassword)
    {
        Session.Require(Permission.BackupRestore);
        string file = Path.GetFileName(source);
        if (!Reauthenticate(staffPassword))
        {
            Audit("RESTORE_REFUSED", file, "Wrong staff password");
            throw new UnauthorizedAccessException("Your password is not correct.");
        }
        try
        {
            DatabaseBackup.Restore(_store.ConnectionString, source, backupPassword, AppPaths.SafetyBackupDirectory);
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or InvalidDataException or IOException)
        {
            Audit("RESTORE_FAILED", file, ex.Message);
            throw;
        }
        Audit("RESTORE_COMPLETED", file, "Booking data replaced; a safety copy of the previous data was saved");
    }

    // ------------------------------------------------------------------ audit

    public List<AuditEntry> LoadAudit(string? search = null, int limit = 500)
    {
        Session.Require(Permission.ViewAudit);
        return _store.LoadAudit(limit, search);
    }

    /// <summary>Cancelled bookings and the refunds paid back (what the Refunds tab shows).</summary>
    public RefundList LoadRefundList(string? search = null, bool includeNoRefund = false, DateTime? since = null,
        string sort = RefundSort.Newest, string handledBy = RefundHandler.Anyone)
    {
        Session.Require(Permission.ViewBookings);
        return _store.LoadRefundList(search, includeNoRefund, 1000, since, sort, handledBy);
    }

    /// <summary>A remembered choice about how a list is shown (the Refunds tab period, sort and so on). Only "view_*" settings.</summary>
    public string GetViewSetting(string key, string fallback)
    {
        Session.Require(Permission.ViewBookings);
        return key.StartsWith("view_", StringComparison.Ordinal) ? _store.GetSetting(key, fallback) : fallback;
    }

    public void SetViewSetting(string key, string value)
    {
        Session.Require(Permission.ViewBookings);
        if (key.StartsWith("view_", StringComparison.Ordinal)) _store.SetSetting(key, BookingStore.Clean(value, 40));
    }

    // ------------------------------------------------------------------ refund requests

    /// <summary>Writes down that a customer asked for a refund. Nothing is cancelled or refunded; staff do that from the booking.</summary>
    public RefundRequestResult LogRefundRequest(string reference)
    {
        Session.Require(Permission.ViewBookings);
        RefundRequestResult result = _store.LogRefundRequest(reference, Session.Username);
        if (result.Ok) Audit("REFUND_REQUEST_LOGGED", reference.Trim().ToUpperInvariant());
        return result;
    }

    public List<RefundRequestRow> LoadRefundRequests(bool includeClosed = false, DateTime? since = null)
    {
        Session.Require(Permission.ViewBookings);
        return _store.LoadRefundRequests(includeClosed, 500, since);
    }

    public int CountOpenRefundRequests()
    {
        Session.Require(Permission.ViewBookings);
        return _store.CountOpenRefundRequests();
    }

    /// <summary>
    /// Closes an open request without cancelling the booking (a decline). With <paramref name="notifyCustomer"/> the customer is emailed the
    /// reason. False when it was already closed.
    /// </summary>
    public bool DismissRefundRequest(long id, string reference, string note, bool notifyCustomer = false)
    {
        Session.Require(Permission.ViewBookings);
        bool done = _store.DismissRefundRequest(id, Session.Username, note, notifyCustomer);
        if (done) Audit("REFUND_REQUEST_DISMISSED", reference, (notifyCustomer ? "Customer told. " : "") + BookingStore.Clean(note, 190));
        return done;
    }

    /// <summary>Approves a request: cancels the booking and pays the refund back to the original payment method. Needs the right to cancel bookings.</summary>
    public RefundApprovalResult ApproveRefundRequest(long id, string reference)
    {
        Session.Require(Permission.CancelBookings);
        RefundApprovalResult result = _store.ApproveRefundRequest(id, Session.Username);
        Audit(result.Ok ? "REFUND_REQUEST_APPROVED" : "REFUND_REQUEST_APPROVAL_SKIPPED", reference,
            result.Ok ? $"PHP {result.Refund:N2} {result.RefundReference}" : result.Message);
        return result;
    }

    /// <summary>Records that a list was exported to a file (who and how many rows, never the contents).</summary>
    public void RecordExport(string action, string detail)
    {
        Session.Require(Permission.ViewBookings);
        Audit(action, "", detail);
    }

    // ------------------------------------------------------------------ database browser

    public List<string> ListTables()
    {
        Session.Require(Permission.BrowseData);
        return _store.ListTables();
    }

    public DataTableInfo DescribeTable(string table)
    {
        Session.Require(Permission.BrowseData);
        return _store.DescribeTable(table);
    }

    public System.Data.DataTable QueryTable(string table, string? search = null)
    {
        Session.Require(Permission.BrowseData);
        return _store.QueryTable(table, search);
    }

    private static string KeyText(IReadOnlyDictionary<string, string?> key)
        => string.Join(", ", key.Select(k => $"{k.Key}={k.Value}"));

    public void InsertRow(string table, IReadOnlyDictionary<string, string?> values)
    {
        Session.Require(Permission.BrowseData);
        string added = _store.InsertRow(table, values);
        Audit("DB_INSERT", table, added);   // secret and customer-detail columns show as (hidden)
    }

    public void UpdateRow(string table, IReadOnlyDictionary<string, string?> key, IReadOnlyDictionary<string, string?> values)
    {
        Session.Require(Permission.BrowseData);
        string changes = _store.UpdateRow(table, key, values);
        Audit("DB_UPDATE", table, KeyText(key) + " | " + changes);
    }

    public void DeleteRow(string table, IReadOnlyDictionary<string, string?> key)
    {
        Session.Require(Permission.BrowseData);
        string removed = _store.DeleteRow(table, key);
        Audit("DB_DELETE", table, KeyText(key) + " | was " + removed);
    }

    public void SignedOut(string reason) => Audit("SIGN_OUT", "", reason);
}
