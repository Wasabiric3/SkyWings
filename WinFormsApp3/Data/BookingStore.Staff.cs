using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace SkyWings;

public sealed partial class BookingStore
{
    public const int MaxFailedSignIns = 5;
    public static readonly TimeSpan SignInLockout = TimeSpan.FromMinutes(15);

    private const string InvalidCredentials = "Invalid username or password.";
    private static readonly Regex UsernamePattern = new("^[A-Za-z0-9._-]{3,40}$", RegexOptions.Compiled);

    // Failures for names that do not exist are counted too, so a locked-out message does not reveal which names are real.
    private static readonly Dictionary<string, (int Count, DateTime LockedUntilUtc)> UnknownUserFailures =
        new(StringComparer.OrdinalIgnoreCase);
    private static string? s_dummyHash;

    private sealed record StaffRow(StaffAccount Account, string Hash, int FailedAttempts);

    private static StaffAccount ReadStaff(SqliteDataReader r)
    {
        int locked = r.GetOrdinal("locked_until"), last = r.GetOrdinal("last_login_at");
        return new StaffAccount(Int(r, "id"), Str(r, "username"), Str(r, "display_name"), RolePermissions.FromDb(Str(r, "role")),
            Int(r, "active") == 1, Int(r, "must_change_password") == 1,
            r.IsDBNull(locked) ? null : DateTime.SpecifyKind(r.GetDateTime(locked), DateTimeKind.Utc),
            DateTime.SpecifyKind(Dt(r, "created_at"), DateTimeKind.Utc),
            r.IsDBNull(last) ? null : DateTime.SpecifyKind(r.GetDateTime(last), DateTimeKind.Utc));
    }

    private const string StaffColumns =
        "id, username, display_name, password_hash, role, active, must_change_password, failed_attempts, locked_until, created_at, last_login_at";

    private StaffRow? ReadStaffRow(SqliteConnection con, string username)
    {
        using var cmd = Cmd(con, $"SELECT {StaffColumns} FROM staff_users WHERE username = @u", null, ("@u", username));
        using var rs = cmd.ExecuteReader();
        return rs.Read() ? new StaffRow(ReadStaff(rs), Str(rs, "password_hash"), Int(rs, "failed_attempts")) : null;
    }

    public int CountStaff()
    {
        lock (_lock)
        {
            try
            {
                using var con = OpenConnection();
                using var cmd = Cmd(con, "SELECT COUNT(*) FROM staff_users");
                return Convert.ToInt32(cmd.ExecuteScalar());
            }
            catch (SqliteException e) { throw new IOException("Unable to read staff accounts: " + e.Message, e); }
        }
    }

    public List<StaffAccount> LoadStaff()
    {
        lock (_lock)
        {
            var list = new List<StaffAccount>();
            try
            {
                using var con = OpenConnection();
                using var cmd = Cmd(con, $"SELECT {StaffColumns} FROM staff_users ORDER BY username COLLATE NOCASE");
                using var rs = cmd.ExecuteReader();
                while (rs.Read()) list.Add(ReadStaff(rs));
            }
            catch (SqliteException e) { throw new IOException("Unable to read staff accounts: " + e.Message, e); }
            return list;
        }
    }

    public StaffAccount? FindStaff(string username)
    {
        lock (_lock)
        {
            using var con = OpenConnection();
            return ReadStaffRow(con, username.Trim())?.Account;
        }
    }

    /// <summary>Creates a staff account. Throws <see cref="ArgumentException"/> for rule violations.</summary>
    public StaffAccount CreateStaff(string username, string displayName, string password, StaffRole role, bool mustChangePassword)
    {
        username = username.Trim();
        displayName = Clean(displayName, 80);
        if (!UsernamePattern.IsMatch(username))
            throw new ArgumentException("Username must be 3 to 40 characters: letters, digits, dot, dash or underscore.");
        if (displayName.Length == 0) throw new ArgumentException("Enter the person's name.");
        if (PasswordPolicy.Check(password, username) is { } problem) throw new ArgumentException(problem);
        return InsertStaff(username, displayName, PasswordHasher.Hash(password), role, mustChangePassword);
    }

    private StaffAccount InsertStaff(string username, string displayName, string hash, StaffRole role, bool mustChange)
    {
        lock (_lock)
        {
            try
            {
                using var con = OpenConnection();
                Exec(con,
                    "INSERT INTO staff_users (username, display_name, password_hash, role, active, must_change_password, created_at) " +
                    "VALUES (@u, @n, @h, @r, 1, @m, @at)",
                    ("@u", username), ("@n", displayName), ("@h", hash), ("@r", RolePermissions.ToDb(role)),
                    ("@m", mustChange ? 1 : 0), ("@at", DateTime.UtcNow));
                return ReadStaffRow(con, username)!.Account;
            }
            catch (SqliteException e) when (e.SqliteErrorCode == 19)
            {
                throw new InvalidOperationException("That username is already taken.");
            }
            catch (SqliteException e) { throw new IOException("Unable to create the staff account: " + e.Message, e); }
        }
    }

    /// <summary>
    /// One-time migration of the old single admin from <c>db.properties</c>. Does nothing when staff accounts already
    /// exist. The imported account must choose a new password at first sign-in.
    /// </summary>
    public bool ImportLegacyAdmin(string username, string passwordHash, string plainPassword)
    {
        if (CountStaff() > 0) return false;
        username = string.IsNullOrWhiteSpace(username) ? "admin" : username.Trim();
        string hash = passwordHash.Length > 0 ? passwordHash : plainPassword.Length > 0 ? PasswordHasher.Hash(plainPassword) : "";
        if (hash.Length == 0 || !UsernamePattern.IsMatch(username)) return false;
        InsertStaff(username, username, hash, StaffRole.Admin, mustChange: true);
        WriteAudit("system", "", "LEGACY_ADMIN_IMPORTED", username, "Imported from db.properties; password change required.");
        return true;
    }

    // ------------------------------------------------------------------ sign-in

    /// <summary>
    /// Checks a username and password. Failures are counted in the database (and for unknown names in memory), so
    /// closing the dialog or restarting the program does not reset the lockout. The message is the same whether the
    /// name or the password was wrong.
    /// </summary>
    public AuthResult Authenticate(string username, string password, bool reauthentication = false)
    {
        string name = Clean(username, 40);
        string okAction = reauthentication ? "REAUTH_OK" : "SIGN_IN";
        string failAction = reauthentication ? "REAUTH_FAILED" : "SIGN_IN_FAILED";
        DateTime now = DateTime.UtcNow;

        lock (_lock)
        {
            try
            {
                using var con = OpenConnection();
                StaffRow? row = ReadStaffRow(con, name);
                if (row is null) return AuthenticateUnknown(con, name, password, now, failAction);

                StaffAccount account = row.Account;
                if (account.LockedUntilUtc is { } until && until > now)
                {
                    WriteAuditCore(con, account.Username, RolePermissions.ToDb(account.Role), failAction, "", "Blocked: account locked");
                    return new AuthResult(null, LockedMessage(until - now));
                }

                bool passwordOk = PasswordHasher.Verify(password, row.Hash);
                if (passwordOk && account.Active)
                {
                    Exec(con, "UPDATE staff_users SET failed_attempts = 0, locked_until = NULL" +
                              (reauthentication ? "" : ", last_login_at = @now") + " WHERE id = @id",
                        ("@now", now), ("@id", account.Id));
                    if (PasswordHasher.NeedsRehash(row.Hash))
                        Exec(con, "UPDATE staff_users SET password_hash = @h WHERE id = @id",
                            ("@h", PasswordHasher.Hash(password)), ("@id", account.Id));
                    WriteAuditCore(con, account.Username, RolePermissions.ToDb(account.Role), okAction, "", "");
                    return new AuthResult(new StaffSession(account.Id, account.Username, account.DisplayName, account.Role,
                        account.MustChangePassword), "");
                }

                if (!account.Active)
                {
                    WriteAuditCore(con, account.Username, RolePermissions.ToDb(account.Role), failAction, "", "Blocked: account deactivated");
                    return new AuthResult(null, InvalidCredentials);
                }

                int attempts = row.FailedAttempts + 1;
                if (attempts >= MaxFailedSignIns)
                {
                    Exec(con, "UPDATE staff_users SET failed_attempts = 0, locked_until = @until WHERE id = @id",
                        ("@until", now + SignInLockout), ("@id", account.Id));
                    WriteAuditCore(con, account.Username, RolePermissions.ToDb(account.Role), "ACCOUNT_LOCKED", "",
                        $"{MaxFailedSignIns} wrong passwords; locked for {(int)SignInLockout.TotalMinutes} minutes");
                    return new AuthResult(null, LockedMessage(SignInLockout));
                }
                Exec(con, "UPDATE staff_users SET failed_attempts = @a WHERE id = @id", ("@a", attempts), ("@id", account.Id));
                WriteAuditCore(con, account.Username, RolePermissions.ToDb(account.Role), failAction, "", $"Wrong password (attempt {attempts})");
                return new AuthResult(null, InvalidCredentials);
            }
            catch (SqliteException e) { throw new IOException("Sign-in is unavailable: " + e.Message, e); }
        }
    }

    private AuthResult AuthenticateUnknown(SqliteConnection con, string name, string password, DateTime now, string failAction)
    {
        // Do the same amount of hashing work as for a real account so response time does not reveal which names exist.
        PasswordHasher.Verify(password, s_dummyHash ??= PasswordHasher.Hash("not-a-real-account-password"));

        lock (UnknownUserFailures)
        {
            if (UnknownUserFailures.Count > 1000) UnknownUserFailures.Clear();
            UnknownUserFailures.TryGetValue(name, out var state);
            if (state.LockedUntilUtc > now)
            {
                WriteAuditCore(con, name, "", failAction, "", "Blocked: too many attempts");
                return new AuthResult(null, LockedMessage(state.LockedUntilUtc - now));
            }
            int count = state.Count + 1;
            if (count >= MaxFailedSignIns)
            {
                UnknownUserFailures[name] = (0, now + SignInLockout);
                WriteAuditCore(con, name, "", "ACCOUNT_LOCKED", "", "Too many attempts for an unknown username");
                return new AuthResult(null, LockedMessage(SignInLockout));
            }
            UnknownUserFailures[name] = (count, DateTime.MinValue);
        }
        WriteAuditCore(con, name, "", failAction, "", "Unknown username");
        return new AuthResult(null, InvalidCredentials);
    }

    private static string LockedMessage(TimeSpan remaining)
        => $"Too many failed attempts. Try again in {Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes))} minute(s).";

    // ------------------------------------------------------------------ account changes

    private void UpdateStaff(int id, string sql, params (string, object?)[] args)
    {
        lock (_lock)
        {
            try
            {
                using var con = OpenConnection();
                Exec(con, sql, args.Append(("@id", (object?)id)).ToArray());
            }
            catch (SqliteException e) { throw new IOException("Unable to update the staff account: " + e.Message, e); }
        }
    }

    /// <summary>Sets a new password chosen by the account's owner and clears the "must change" flag.</summary>
    public void ChangePassword(int staffId, string newPassword)
    {
        StaffAccount account = LoadStaff().FirstOrDefault(s => s.Id == staffId) ?? throw new InvalidOperationException("Staff account not found.");
        if (PasswordPolicy.Check(newPassword, account.Username) is { } problem) throw new ArgumentException(problem);
        UpdateStaff(staffId, "UPDATE staff_users SET password_hash = @h, must_change_password = 0, failed_attempts = 0, locked_until = NULL WHERE id = @id",
            ("@h", PasswordHasher.Hash(newPassword)));
    }

    /// <summary>Admin sets a temporary password; the person must replace it at next sign-in.</summary>
    public void ResetPassword(int staffId, string temporaryPassword)
    {
        StaffAccount account = LoadStaff().FirstOrDefault(s => s.Id == staffId) ?? throw new InvalidOperationException("Staff account not found.");
        if (PasswordPolicy.Check(temporaryPassword, account.Username) is { } problem) throw new ArgumentException(problem);
        UpdateStaff(staffId, "UPDATE staff_users SET password_hash = @h, must_change_password = 1, failed_attempts = 0, locked_until = NULL WHERE id = @id",
            ("@h", PasswordHasher.Hash(temporaryPassword)));
    }

    public void SetStaffRole(int staffId, StaffRole role)
    {
        lock (_lock)
        {
            StaffAccount account = LoadStaff().FirstOrDefault(s => s.Id == staffId) ?? throw new InvalidOperationException("Staff account not found.");
            if (RolePermissions.IsAdministrator(account.Role) && account.Active && !RolePermissions.IsAdministrator(role)) RequireAnotherActiveAdmin(staffId);
            UpdateStaff(staffId, "UPDATE staff_users SET role = @r WHERE id = @id", ("@r", RolePermissions.ToDb(role)));
        }
    }

    public void SetStaffActive(int staffId, bool active)
    {
        lock (_lock)
        {
            StaffAccount account = LoadStaff().FirstOrDefault(s => s.Id == staffId) ?? throw new InvalidOperationException("Staff account not found.");
            if (!active && RolePermissions.IsAdministrator(account.Role) && account.Active) RequireAnotherActiveAdmin(staffId);
            UpdateStaff(staffId, "UPDATE staff_users SET active = @a, failed_attempts = 0, locked_until = NULL WHERE id = @id", ("@a", active ? 1 : 0));
        }
    }

    private void RequireAnotherActiveAdmin(int exceptId)
    {
        if (!LoadStaff().Any(s => s.Id != exceptId && RolePermissions.IsAdministrator(s.Role) && s.Active))
            throw new InvalidOperationException("At least one active administrator is required.");
    }

    /// <summary>
    /// Renames a sign-in name. Only the username changes; password, role and history stay. Reached only from the hidden
    /// command-line switch.
    /// </summary>
    public StaffAccount RenameStaff(string oldUsername, string newUsername)
    {
        newUsername = (newUsername ?? "").Trim();
        if (!UsernamePattern.IsMatch(newUsername))
            throw new ArgumentException("Username must be 3 to 40 characters: letters, digits, dot, dash or underscore.");
        lock (_lock)
        {
            StaffAccount account = FindStaff(oldUsername ?? "")
                ?? throw new InvalidOperationException($"There is no staff account called '{oldUsername}'. Existing accounts: {string.Join(", ", LoadStaff().Select(s => s.Username))}.");
            try
            {
                UpdateStaff(account.Id, "UPDATE staff_users SET username = @u WHERE id = @id", ("@u", newUsername));
            }
            catch (IOException e) when (e.InnerException is SqliteException { SqliteErrorCode: 19 })
            {
                throw new InvalidOperationException("That username is already taken.");
            }
            WriteAudit("command-line", "system", "STAFF_RENAMED", newUsername, $"{account.Username} to {newUsername}");
            return account with { Username = newUsername };
        }
    }

    /// <summary>
    /// Makes an existing account a Super Admin. Only the role changes: the password, lockout and "must change password" flag are
    /// left exactly as they are. Reached only from the hidden command-line switch, never from the staff screens.
    /// </summary>
    public StaffAccount PromoteToSuperAdmin(string username)
    {
        lock (_lock)
        {
            List<StaffAccount> all = LoadStaff();
            StaffAccount? account = all.FirstOrDefault(s => string.Equals(s.Username, username?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (account is null)
                throw new InvalidOperationException($"There is no staff account called '{username}'. Existing accounts: {string.Join(", ", all.Select(s => s.Username))}.");
            if (!account.Active) throw new InvalidOperationException($"'{account.Username}' is deactivated; activate it first.");
            if (account.Role == StaffRole.SuperAdmin) return account;
            UpdateStaff(account.Id, "UPDATE staff_users SET role = @r WHERE id = @id", ("@r", RolePermissions.ToDb(StaffRole.SuperAdmin)));
            WriteAudit("command-line", "system", "SUPERADMIN_GRANTED", account.Username, $"{account.Role} to SuperAdmin");
            return account with { Role = StaffRole.SuperAdmin };
        }
    }
}
