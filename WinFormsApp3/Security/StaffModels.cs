namespace SkyWings;

public enum StaffRole { Agent, Admin }

/// <summary>What a staff member may do. Roles are mapped to these in <see cref="RolePermissions"/>.</summary>
[Flags]
public enum Permission
{
    None = 0,
    ViewBookings = 1,
    CancelBookings = 2,
    ViewReports = 4,
    ManageFlights = 8,
    BackupRestore = 16,
    ManageStaff = 32,
    ViewAudit = 64,
    All = ViewBookings | CancelBookings | ViewReports | ManageFlights | BackupRestore | ManageStaff | ViewAudit
}

public static class RolePermissions
{
    public static Permission For(StaffRole role) => role switch
    {
        StaffRole.Admin => Permission.All,
        StaffRole.Agent => Permission.ViewBookings | Permission.CancelBookings | Permission.ViewReports,
        _ => Permission.None
    };

    public static string ToDb(StaffRole role) => role == StaffRole.Admin ? "ADMIN" : "AGENT";

    public static StaffRole FromDb(string value)
        => string.Equals(value, "ADMIN", StringComparison.OrdinalIgnoreCase) ? StaffRole.Admin : StaffRole.Agent;
}

/// <summary>A staff account as shown in lists. The password hash is never exposed.</summary>
public sealed record StaffAccount(
    int Id, string Username, string DisplayName, StaffRole Role, bool Active, bool MustChangePassword,
    DateTime? LockedUntilUtc, DateTime CreatedAtUtc, DateTime? LastLoginAtUtc);

public sealed record AuditEntry(long Id, DateTime AtUtc, string Actor, string Role, string Action, string Target, string Detail);

/// <summary>The signed-in staff member. Every staff action asks this object for permission first.</summary>
public sealed class StaffSession
{
    public StaffSession(int id, string username, string displayName, StaffRole role, bool mustChangePassword)
    {
        Id = id; Username = username; DisplayName = displayName; Role = role; MustChangePassword = mustChangePassword;
        SignedInAtUtc = DateTime.UtcNow;
    }

    public int Id { get; }
    public string Username { get; }
    public string DisplayName { get; }
    public StaffRole Role { get; }
    public bool MustChangePassword { get; set; }
    public DateTime SignedInAtUtc { get; }
    public Permission Permissions => RolePermissions.For(Role);

    public bool Has(Permission permission) => (Permissions & permission) == permission;

    /// <summary>Throws when the signed-in role lacks <paramref name="permission"/>.</summary>
    public void Require(Permission permission)
    {
        if (!Has(permission))
            throw new UnauthorizedAccessException($"Your role ({Role}) is not allowed to do this.");
    }
}

public sealed record AuthResult(StaffSession? Session, string Message)
{
    public bool Success => Session is not null;
}

/// <summary>Outcome of changing a flight's aircraft; <see cref="Message"/> explains a refusal or confirms the change.</summary>
public sealed record AircraftChangeResult(bool Ok, string Message);
