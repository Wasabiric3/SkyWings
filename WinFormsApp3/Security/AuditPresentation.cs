namespace SkyWings;

/// <summary>Which group an audit event belongs to (the chips on the Audit tab).</summary>
public enum AuditCategory { Refunds, Email, SignIns, Security, Bookings, Admin, Other }

/// <summary>How much attention an audit event needs: nothing, "this went well", "look at this", or "this touches security".</summary>
public enum AuditSeverity { Routine, Done, Attention, Security }

/// <summary>What the Audit tab shows for one event: its group, its colour, and a sentence instead of the raw code.</summary>
public sealed record AuditView(AuditCategory Category, AuditSeverity Severity, string Title)
{
    /// <summary>A small mark in front of the sentence, so the importance reads without colour too.</summary>
    public string Symbol => Severity switch
    {
        AuditSeverity.Done => "✔",
        AuditSeverity.Attention => "⚠",
        AuditSeverity.Security => "⚠",
        _ => "·"
    };
}

/// <summary>
/// Turns an audit entry's code (<c>REFUND_EMAIL_IGNORED</c>) into a readable sentence, a group and a severity for the Audit tab. Nothing
/// is hidden: a code that is not listed still gets a tidy sentence made from the code, and the full record shows the raw code.
/// </summary>
public static class AuditPresentation
{
    private static readonly Dictionary<string, AuditView> Known = new(StringComparer.OrdinalIgnoreCase)
    {
        // sign-ins
        ["SIGN_IN"] = new(AuditCategory.SignIns, AuditSeverity.Routine, "Signed in"),
        ["SIGN_OUT"] = new(AuditCategory.SignIns, AuditSeverity.Routine, "Signed out"),
        ["REAUTH_OK"] = new(AuditCategory.SignIns, AuditSeverity.Routine, "Confirmed their password again"),

        // security
        ["SIGN_IN_FAILED"] = new(AuditCategory.Security, AuditSeverity.Security, "Failed sign-in"),
        ["ACCOUNT_LOCKED"] = new(AuditCategory.Security, AuditSeverity.Security, "Staff account locked"),
        ["LOOKUP_LOCKED"] = new(AuditCategory.Security, AuditSeverity.Security, "Booking look-up locked after too many tries"),
        ["LOOKUP_FAILED"] = new(AuditCategory.Security, AuditSeverity.Attention, "Booking look-up failed"),
        ["PASSWORD_CHANGED"] = new(AuditCategory.Security, AuditSeverity.Security, "Password changed"),
        ["STAFF_PASSWORD_RESET"] = new(AuditCategory.Security, AuditSeverity.Security, "Staff password reset"),
        ["STAFF_ROLE_CHANGED"] = new(AuditCategory.Security, AuditSeverity.Security, "Staff role changed"),
        ["SUPERADMIN_GRANTED"] = new(AuditCategory.Security, AuditSeverity.Security, "Super Admin rights granted"),
        ["FIRST_ADMIN_CREATED"] = new(AuditCategory.Security, AuditSeverity.Security, "First admin account created"),
        ["LEGACY_ADMIN_IMPORTED"] = new(AuditCategory.Security, AuditSeverity.Security, "Old admin account imported"),
        ["RESTORE_COMPLETED"] = new(AuditCategory.Security, AuditSeverity.Security, "Database restored from a backup"),
        ["RESTORE_FAILED"] = new(AuditCategory.Security, AuditSeverity.Attention, "Database restore failed"),
        ["RESTORE_REFUSED"] = new(AuditCategory.Security, AuditSeverity.Attention, "Database restore refused"),
        ["DB_INSERT"] = new(AuditCategory.Security, AuditSeverity.Security, "Database record added by hand"),
        ["DB_UPDATE"] = new(AuditCategory.Security, AuditSeverity.Security, "Database record changed by hand"),
        ["DB_DELETE"] = new(AuditCategory.Security, AuditSeverity.Security, "Database record deleted by hand"),

        // refunds
        ["REFUND_REQUEST_LOGGED"] = new(AuditCategory.Refunds, AuditSeverity.Routine, "Refund request received"),
        ["REFUND_REQUEST_APPROVED"] = new(AuditCategory.Refunds, AuditSeverity.Done, "Refund approved and paid back"),
        ["REFUND_REQUEST_DISMISSED"] = new(AuditCategory.Refunds, AuditSeverity.Done, "Refund request declined"),
        ["REFUND_REQUEST_CLOSED"] = new(AuditCategory.Refunds, AuditSeverity.Routine, "Refund request closed"),
        ["REFUND_REQUEST_APPROVAL_SKIPPED"] = new(AuditCategory.Refunds, AuditSeverity.Attention, "Refund approval skipped"),
        ["REFUND_EMAIL_IGNORED"] = new(AuditCategory.Refunds, AuditSeverity.Attention, "Refund email ignored"),

        // email
        ["EMAIL_RECEIPT"] = new(AuditCategory.Email, AuditSeverity.Done, "Receipt email sent"),
        ["EMAIL_BOARDING_PASS"] = new(AuditCategory.Email, AuditSeverity.Done, "Boarding pass email sent"),
        ["EMAIL_CANCELLATION"] = new(AuditCategory.Email, AuditSeverity.Done, "Cancellation / refund email sent"),
        ["EMAIL_REFUND_RECEIVED"] = new(AuditCategory.Email, AuditSeverity.Done, "\"We received your request\" email sent"),
        ["EMAIL_REFUND_DECLINED"] = new(AuditCategory.Email, AuditSeverity.Done, "\"Request declined\" email sent"),
        ["EMAIL_REFUND_ALREADY"] = new(AuditCategory.Email, AuditSeverity.Done, "\"Already cancelled\" reply sent"),
        ["EMAIL_WAITLIST_OFFER"] = new(AuditCategory.Email, AuditSeverity.Done, "Waitlist offer email sent"),
        ["EMAIL_UNSUBSCRIBE"] = new(AuditCategory.Email, AuditSeverity.Routine, "Customer unsubscribed from emails"),
        ["EMAIL_RESUME"] = new(AuditCategory.Email, AuditSeverity.Routine, "Customer turned emails back on"),

        // bookings
        ["BOOKING_CANCELED"] = new(AuditCategory.Bookings, AuditSeverity.Done, "Booking cancelled"),
        ["BOOKING_CANCEL_SKIPPED"] = new(AuditCategory.Bookings, AuditSeverity.Attention, "Booking cancellation skipped"),
        ["BOOKING_REBOOKED"] = new(AuditCategory.Bookings, AuditSeverity.Done, "Booking moved to another flight"),
        ["WAITLIST_OFFERED"] = new(AuditCategory.Bookings, AuditSeverity.Done, "Waitlist offer made"),
        ["WAITLIST_REMOVED"] = new(AuditCategory.Bookings, AuditSeverity.Done, "Removed from the waitlist"),
        ["WAITLIST_REMOVE_SKIPPED"] = new(AuditCategory.Bookings, AuditSeverity.Attention, "Waitlist removal skipped"),
        ["SEAT_RELEASED"] = new(AuditCategory.Bookings, AuditSeverity.Done, "Seats released"),
        ["SEAT_RELEASE_SKIPPED"] = new(AuditCategory.Bookings, AuditSeverity.Attention, "Seat release skipped"),

        // admin
        ["STAFF_CREATED"] = new(AuditCategory.Admin, AuditSeverity.Done, "Staff account created"),
        ["STAFF_RENAMED"] = new(AuditCategory.Admin, AuditSeverity.Done, "Staff account renamed"),
        ["STAFF_ACTIVATED"] = new(AuditCategory.Admin, AuditSeverity.Done, "Staff account activated"),
        ["STAFF_DEACTIVATED"] = new(AuditCategory.Admin, AuditSeverity.Done, "Staff account deactivated"),
        ["FLIGHT_ADDED"] = new(AuditCategory.Admin, AuditSeverity.Done, "Flight added"),
        ["FLIGHT_EDITED"] = new(AuditCategory.Admin, AuditSeverity.Done, "Flight edited"),
        ["FLIGHT_ACTIVATED"] = new(AuditCategory.Admin, AuditSeverity.Done, "Flight activated"),
        ["FLIGHT_DEACTIVATED"] = new(AuditCategory.Admin, AuditSeverity.Done, "Flight deactivated"),
        ["FLIGHT_AIRCRAFT_CHANGED"] = new(AuditCategory.Admin, AuditSeverity.Done, "Flight's aircraft changed"),
        ["BACKUP_CREATED"] = new(AuditCategory.Admin, AuditSeverity.Done, "Database backup created"),
        ["DEMO_REPORTS_ON"] = new(AuditCategory.Admin, AuditSeverity.Routine, "Demo data included in reports"),
        ["DEMO_REPORTS_OFF"] = new(AuditCategory.Admin, AuditSeverity.Routine, "Demo data left out of reports"),
        ["DEMO_ACTIVITY_ON"] = new(AuditCategory.Admin, AuditSeverity.Routine, "Demo activity switched on"),
        ["DEMO_ACTIVITY_OFF"] = new(AuditCategory.Admin, AuditSeverity.Routine, "Demo activity switched off"),
        ["DEMO_RESET"] = new(AuditCategory.Admin, AuditSeverity.Routine, "Demo data reset"),
        ["MAINTENANCE_RESET"] = new(AuditCategory.Admin, AuditSeverity.Routine, "Demo data regenerated"),
    };

    /// <summary>The group, severity and sentence for one audit entry.</summary>
    public static AuditView Describe(AuditEntry entry)
    {
        string code = (entry.Action ?? "").Trim();
        string detail = entry.Detail ?? "";

        if (code.Equals("REFUND_EMAIL_IGNORED", StringComparison.OrdinalIgnoreCase))
            return Known["REFUND_EMAIL_IGNORED"] with { Title = "Refund email ignored" + IgnoredReason(detail) };

        if (code.Equals("REFUND_REQUEST_LOGGED", StringComparison.OrdinalIgnoreCase) && detail.Contains("differs", StringComparison.OrdinalIgnoreCase))
            return new AuditView(AuditCategory.Refunds, AuditSeverity.Attention, "Refund request received · sender differs from the booking");

        if (Known.TryGetValue(code, out AuditView? known))
        {
            // An email that was only saved to the outbox folder was never sent.
            if (known.Category == AuditCategory.Email && known.Severity == AuditSeverity.Done && detail.Contains("Outbox", StringComparison.OrdinalIgnoreCase))
                return new AuditView(AuditCategory.Email, AuditSeverity.Attention, known.Title.Replace(" sent", " saved to the outbox only (not sent)"));
            return known;
        }

        return new AuditView(CategoryByPrefix(code), AuditSeverity.Routine, Tidy(code));
    }

    /// <summary>
    /// The name shown on a chip for a group. "Refund" and "Booking" are singular on purpose: a chip must never read like the Refunds or
    /// Bookings section button above it.
    /// </summary>
    public static string CategoryName(AuditCategory category) => category switch
    {
        AuditCategory.SignIns => "Sign-ins",
        AuditCategory.Refunds => "Refund",
        AuditCategory.Bookings => "Booking",
        _ => category.ToString()
    };

    private static string IgnoredReason(string detail)
    {
        if (detail.Contains("confirmed booking", StringComparison.OrdinalIgnoreCase)) return " · booking already cancelled";
        if (detail.Contains("No booking has that reference", StringComparison.OrdinalIgnoreCase)) return " · no such booking";
        if (detail.Contains("open refund request", StringComparison.OrdinalIgnoreCase)) return " · a request is already open";
        if (detail.Contains("already left", StringComparison.OrdinalIgnoreCase)) return " · flight already left";
        if (detail.Contains("cannot be logged", StringComparison.OrdinalIgnoreCase)) return " · demo booking";
        return "";
    }

    private static AuditCategory CategoryByPrefix(string code)
    {
        string[] groups = { "EXPORT_", "STAFF_", "FLIGHT_", "DEMO_", "BACKUP_", "AIRCRAFT_" };
        if (code.StartsWith("REFUND_", StringComparison.OrdinalIgnoreCase)) return AuditCategory.Refunds;
        if (code.StartsWith("EMAIL_", StringComparison.OrdinalIgnoreCase)) return AuditCategory.Email;
        if (code.StartsWith("SIGN_", StringComparison.OrdinalIgnoreCase)) return AuditCategory.SignIns;
        if (code.StartsWith("BOOKING_", StringComparison.OrdinalIgnoreCase) || code.StartsWith("WAITLIST_", StringComparison.OrdinalIgnoreCase)
            || code.StartsWith("SEAT_", StringComparison.OrdinalIgnoreCase)) return AuditCategory.Bookings;
        if (code.StartsWith("LOOKUP_", StringComparison.OrdinalIgnoreCase) || code.StartsWith("RESTORE_", StringComparison.OrdinalIgnoreCase)
            || code.StartsWith("DB_", StringComparison.OrdinalIgnoreCase)) return AuditCategory.Security;
        if (groups.Any(g => code.StartsWith(g, StringComparison.OrdinalIgnoreCase))) return AuditCategory.Admin;
        return AuditCategory.Other;
    }

    /// <summary>"EXPORT_REFUND_REQUESTS" becomes "Export refund requests"; empty stays empty.</summary>
    internal static string Tidy(string code)
    {
        string words = code.Replace('_', ' ').Trim().ToLowerInvariant();
        return words.Length == 0 ? "" : char.ToUpperInvariant(words[0]) + words[1..];
    }
}
