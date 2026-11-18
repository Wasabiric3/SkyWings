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

    /// <summary>Cancels a confirmed booking with the given refund. False when it was no longer confirmed.</summary>
    public bool CancelBooking(string reference, decimal refund)
    {
        Session.Require(Permission.CancelBookings);
        bool done = _store.CancelBooking(reference, refund);
        Audit(done ? "BOOKING_CANCELED" : "BOOKING_CANCEL_SKIPPED", reference, done ? $"Refund PHP {refund:N2}" : "Already canceled or changed");
        return done;
    }

    public List<WaitlistEntry> LoadWaitlist()
    {
        Session.Require(Permission.ViewBookings);
        return _store.LoadAllLiveWaitlist();
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
        _store.SeedFlightsIfEmpty(AppData.DomesticFlights, AppData.InternationalFlights,
            AppData.DefaultDomesticCapacity, AppData.DefaultInternationalCapacity);
        return _store.LoadAllFlights();
    }

    public FlightInventory LoadFlightInventory(string flightNo)
    {
        Session.Require(Permission.ManageFlights);
        return _store.LoadFlightInventory(flightNo);
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
        StaffAccount created = _store.CreateStaff(username, displayName, temporaryPassword, role, mustChangePassword: true);
        Audit("STAFF_CREATED", created.Username, $"Role {role}");
        return created;
    }

    public void ResetPassword(int staffId, string temporaryPassword)
    {
        Session.Require(Permission.ManageStaff);
        StaffAccount target = FindStaffOrThrow(staffId);
        _store.ResetPassword(staffId, temporaryPassword);
        Audit("STAFF_PASSWORD_RESET", target.Username, "Temporary password set; change required at next sign-in");
    }

    public void SetRole(int staffId, StaffRole role)
    {
        Session.Require(Permission.ManageStaff);
        StaffAccount target = FindStaffOrThrow(staffId);
        _store.SetStaffRole(staffId, role);
        Audit("STAFF_ROLE_CHANGED", target.Username, $"{target.Role} to {role}");
    }

    public void SetActive(int staffId, bool active)
    {
        Session.Require(Permission.ManageStaff);
        StaffAccount target = FindStaffOrThrow(staffId);
        if (!active && target.Id == Session.Id) throw new InvalidOperationException("You can't deactivate your own account.");
        _store.SetStaffActive(staffId, active);
        Audit(active ? "STAFF_ACTIVATED" : "STAFF_DEACTIVATED", target.Username);
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

    public void SignedOut(string reason) => Audit("SIGN_OUT", "", reason);
}
