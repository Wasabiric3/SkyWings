namespace SkyWings.Tests;

/// <summary>The Audit tab's readable sentences, groups and severities.</summary>
[TestClass]
public class AuditPresentationTests
{
    private static AuditEntry Entry(string action, string detail = "", string target = "SKY-AAAA00000001")
        => new(1, DateTime.UtcNow, "system", "SYSTEM", action, target, detail);

    [TestMethod]
    [DataRow("SIGN_IN", AuditCategory.SignIns, AuditSeverity.Routine, "Signed in")]
    [DataRow("SIGN_OUT", AuditCategory.SignIns, AuditSeverity.Routine, "Signed out")]
    [DataRow("SIGN_IN_FAILED", AuditCategory.Security, AuditSeverity.Security, "Failed sign-in")]
    [DataRow("LOOKUP_FAILED", AuditCategory.Security, AuditSeverity.Attention, "Booking look-up failed")]
    [DataRow("REFUND_REQUEST_APPROVED", AuditCategory.Refunds, AuditSeverity.Done, "Refund approved and paid back")]
    [DataRow("EMAIL_CANCELLATION", AuditCategory.Email, AuditSeverity.Done, "Cancellation / refund email sent")]
    [DataRow("EMAIL_REFUND_ALREADY", AuditCategory.Email, AuditSeverity.Done, "\"Already cancelled\" reply sent")]
    [DataRow("BOOKING_CANCELED", AuditCategory.Bookings, AuditSeverity.Done, "Booking cancelled")]
    [DataRow("DB_DELETE", AuditCategory.Security, AuditSeverity.Security, "Database record deleted by hand")]
    [DataRow("FLIGHT_EDITED", AuditCategory.Admin, AuditSeverity.Done, "Flight edited")]
    public void KnownEvents_GetASentenceAGroupAndASeverity(string code, AuditCategory category, AuditSeverity severity, string title)
    {
        AuditView view = AuditPresentation.Describe(Entry(code));
        Assert.AreEqual(category, view.Category);
        Assert.AreEqual(severity, view.Severity);
        Assert.AreEqual(title, view.Title);
    }

    [TestMethod]
    [DataRow("Only a confirmed booking can have a refund request.", "Refund email ignored · booking already cancelled")]
    [DataRow("No booking has that reference.", "Refund email ignored · no such booking")]
    [DataRow("This booking already has an open refund request.", "Refund email ignored · a request is already open")]
    [DataRow("This flight has already left, so the booking can no longer be cancelled or refunded.", "Refund email ignored · flight already left")]
    [DataRow("Refund requests cannot be logged for this booking.", "Refund email ignored · demo booking")]
    [DataRow("something new", "Refund email ignored")]
    public void AnIgnoredRefundEmail_SaysWhy(string detail, string expected)
    {
        AuditView view = AuditPresentation.Describe(Entry("REFUND_EMAIL_IGNORED", detail));
        Assert.AreEqual(expected, view.Title);
        Assert.AreEqual(AuditSeverity.Attention, view.Severity);
        Assert.AreEqual(AuditCategory.Refunds, view.Category);
    }

    [TestMethod]
    public void ARefundRequestFromAnotherAddress_NeedsAttention_ButAMatchingOneDoesNot()
    {
        AuditView other = AuditPresentation.Describe(Entry("REFUND_REQUEST_LOGGED", "Received by email; sender differs from the booking"));
        AuditView same = AuditPresentation.Describe(Entry("REFUND_REQUEST_LOGGED", "Received by email; sender matches the booking"));
        Assert.AreEqual(AuditSeverity.Attention, other.Severity);
        StringAssert.Contains(other.Title, "sender differs");
        Assert.AreEqual(AuditSeverity.Routine, same.Severity);
        Assert.AreEqual("Refund request received", same.Title);
    }

    [TestMethod]
    [DataRow("EMAIL_RECEIPT", "SMTP", AuditSeverity.Done, "Receipt email sent")]
    [DataRow("EMAIL_RECEIPT", "Outbox", AuditSeverity.Attention, "Receipt email saved to the outbox only (not sent)")]
    [DataRow("EMAIL_REFUND_ALREADY", "SMTP + Outbox", AuditSeverity.Attention, "\"Already cancelled\" reply saved to the outbox only (not sent)")]
    public void AnEmailThatOnlyReachedTheOutbox_IsNotShownAsSent(string code, string channel, AuditSeverity severity, string title)
    {
        AuditView view = AuditPresentation.Describe(Entry(code, channel));
        Assert.AreEqual(severity, view.Severity);
        Assert.AreEqual(title, view.Title);
    }

    [TestMethod]
    [DataRow("EXPORT_BOOKINGS", AuditCategory.Admin, "Export bookings")]
    [DataRow("EMAIL_SOMETHING_NEW", AuditCategory.Email, "Email something new")]
    [DataRow("SEAT_MOVED", AuditCategory.Bookings, "Seat moved")]
    [DataRow("TOTALLY_UNKNOWN", AuditCategory.Other, "Totally unknown")]
    public void AnUnlistedCode_IsStillShown_AsATidyVersionOfTheCode(string code, AuditCategory category, string title)
    {
        AuditView view = AuditPresentation.Describe(Entry(code));
        Assert.AreEqual(category, view.Category);
        Assert.AreEqual(title, view.Title);
        Assert.AreEqual(AuditSeverity.Routine, view.Severity);
    }

    [TestMethod]
    public void NoCodeAtAll_DoesNotThrow()
    {
        AuditView view = AuditPresentation.Describe(Entry(""));
        Assert.AreEqual(AuditCategory.Other, view.Category);
        Assert.AreEqual("", view.Title);
    }

    [TestMethod]
    public void EverySeverityHasAMark_AndOnlyRoutineIsQuiet()
    {
        Assert.AreEqual("✔", new AuditView(AuditCategory.Email, AuditSeverity.Done, "x").Symbol);
        Assert.AreEqual("⚠", new AuditView(AuditCategory.Email, AuditSeverity.Attention, "x").Symbol);
        Assert.AreEqual("⚠", new AuditView(AuditCategory.Security, AuditSeverity.Security, "x").Symbol);
        Assert.AreEqual("·", new AuditView(AuditCategory.SignIns, AuditSeverity.Routine, "x").Symbol);
    }

    [TestMethod]
    public void ChipNames_AreReadable_AndNeverStartLikeASectionButton()
    {
        Assert.AreEqual("Sign-ins", AuditPresentation.CategoryName(AuditCategory.SignIns));
        string[] sections = { "Flights", "Bookings", "Refunds", "Waitlist", "Reports", "Staff", "Audit", "Database" };
        foreach (AuditCategory category in Enum.GetValues<AuditCategory>())
            foreach (string section in sections)
                Assert.IsFalse((AuditPresentation.CategoryName(category) + " 0").StartsWith(section, StringComparison.Ordinal),
                    $"the {category} chip would be mistaken for the {section} button");
    }
}
