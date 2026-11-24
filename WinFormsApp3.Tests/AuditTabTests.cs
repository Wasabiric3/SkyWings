using System.Reflection;
using SkyWings.UI;

namespace SkyWings.Tests;

/// <summary>The revamped Audit tab: sentences instead of codes, sign-ins hidden by default, chips that filter, tiles for today.</summary>
[TestClass]
[DoNotParallelize]
public sealed class AuditTabTests
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private static IEnumerable<Control> All(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (Control nested in All(child)) yield return nested;
        }
    }

    private static T Field<T>(AdminForm form, string name) where T : class => (T)typeof(AdminForm).GetField(name, Any)!.GetValue(form)!;

    private static void Seed(BookingStore store)
    {
        for (int i = 0; i < 3; i++)
        {
            store.WriteAudit("Patrick", "SUPERADMIN", "SIGN_IN");
            store.WriteAudit("Patrick", "SUPERADMIN", "SIGN_OUT", detail: "Closed the application");
        }
        store.WriteAudit("system", "SYSTEM", "EMAIL_RECEIPT", "SKY-AAAA00000001", "SMTP");
        store.WriteAudit("email", "SYSTEM", "REFUND_REQUEST_LOGGED", "SKY-AAAA00000001", "Received by email; sender differs from the booking");
        store.WriteAudit("Patrick", "SUPERADMIN", "REFUND_REQUEST_APPROVED", "SKY-AAAA00000001", "PHP 7,728.00 RFD-BA0CDD163F92");
        store.WriteAudit("system", "SYSTEM", "EMAIL_CANCELLATION", "SKY-AAAA00000001", "SMTP");
        store.WriteAudit("email", "SYSTEM", "REFUND_EMAIL_IGNORED", "SKY-AAAA00000001", "Only a confirmed booking can have a refund request.");
        store.WriteAudit("system", "SYSTEM", "EMAIL_REFUND_ALREADY", "SKY-AAAA00000001", "SMTP");
        store.WriteAudit("", "", "SIGN_IN_FAILED", "", "Wrong password for 'admin'");
        store.WriteAudit("Patrick", "SUPERADMIN", "SOMETHING_BRAND_NEW", "X-1", "an event this tab has never heard of");
    }

    private static AdminForm OpenAuditTab(FormHarness harness, Size? size = null)
    {
        var form = new AdminForm(harness.Admin) { StartPosition = FormStartPosition.Manual, Location = new Point(20, 20) };
        form.Show();
        if (size is { } s) form.Size = s;
        Application.DoEvents();
        All(form).OfType<GlassButton>().First(b => b.Text == "Audit" && b.Parent is FlowLayoutPanel).PerformClick();
        Application.DoEvents();
        return form;
    }

    private static string[] Shown(AdminForm form)
        => Field<DataGridView>(form, "_audit").Rows.Cast<DataGridViewRow>().Select(r => r.Cells["What"].Value?.ToString() ?? "").ToArray();

    private static void Chip(AdminForm form, string name)
    {
        All(form).OfType<GlassButton>().First(b => b.Text.StartsWith(name + " ", StringComparison.Ordinal) && b.Parent is FlowLayoutPanel).PerformClick();
        Application.DoEvents();
    }

    private static string ChipText(AdminForm form, string name)
        => All(form).OfType<GlassButton>().First(b => b.Text.StartsWith(name + " ", StringComparison.Ordinal) && b.Parent is FlowLayoutPanel).Text;

    [TestMethod]
    public void ByDefault_SignInsAreHidden_AndEventsReadAsSentences() => FormHarness.OnStaThread(() =>
    {
        using var harness = new FormHarness();
        Seed(harness.Store);
        using AdminForm form = OpenAuditTab(harness);

        string[] rows = Shown(form);
        Assert.IsFalse(rows.Any(r => r.Contains("Signed in") || r.Contains("Signed out")), "the sign-ins are behind their own chip");
        Assert.IsTrue(rows.Any(r => r.Contains("Refund approved and paid back")));
        Assert.IsTrue(rows.Any(r => r.Contains("Refund email ignored · booking already cancelled")));
        Assert.IsTrue(rows.Any(r => r.Contains("Refund request received · sender differs from the booking")));
        Assert.IsTrue(rows.Any(r => r.Contains("Failed sign-in")));
        Assert.IsTrue(rows.Any(r => r.Contains("Something brand new")), "an unknown code is still shown, tidied");
        Assert.IsFalse(rows.Any(r => r.Contains("REFUND_")), "no raw codes in the list");
        FormHarness.SaveSnapshot(form, "audit-tab-with-events");
    });

    [TestMethod]
    public void TheChips_FilterTheList_AndCarryTheirCounts() => FormHarness.OnStaThread(() =>
    {
        using var harness = new FormHarness();
        Seed(harness.Store);
        using AdminForm form = OpenAuditTab(harness);

        Assert.AreEqual("Sign-ins 6", ChipText(form, "Sign-ins"));
        Assert.AreEqual("All 14", ChipText(form, "All"));
        Assert.AreEqual("Activity 8", ChipText(form, "Activity"));

        Chip(form, "Sign-ins");
        Assert.HasCount(6, Shown(form));
        Assert.IsTrue(Shown(form).All(r => r.Contains("Signed in") || r.Contains("Signed out")));

        Chip(form, "All");
        Assert.HasCount(14, Shown(form));

        Chip(form, "Security");
        string[] security = Shown(form);
        Assert.HasCount(1, security);
        StringAssert.Contains(security[0], "Failed sign-in");

        Chip(form, "Email");
        Assert.HasCount(3, Shown(form), "receipt, cancellation and the already-cancelled reply");
    });

    [TestMethod]
    public void TheSearchBox_FindsTheFriendlySentences_NotJustTheRawCodes() => FormHarness.OnStaThread(() =>
    {
        using var harness = new FormHarness();
        Seed(harness.Store);
        using AdminForm form = OpenAuditTab(harness);

        Field<TextBox>(form, "_auditSearch").Text = "already cancelled";
        Application.DoEvents();
        Field<System.Windows.Forms.Timer>(form, "_auditTimer").Interval = 1;
        System.Threading.Thread.Sleep(30);
        Application.DoEvents();

        string[] rows = Shown(form);
        Assert.IsGreaterThanOrEqualTo(2, rows.Length, "both the ignored email and the reply mention it");
        Assert.IsTrue(rows.All(r => r.Contains("already cancelled", StringComparison.OrdinalIgnoreCase)));
    });

    [TestMethod]
    public void AttentionAndSecurityRows_AreColouredAndCounted() => FormHarness.OnStaThread(() =>
    {
        using var harness = new FormHarness();
        Seed(harness.Store);
        using AdminForm form = OpenAuditTab(harness);

        var grid = Field<DataGridView>(form, "_audit");
        // The colour itself is set while the grid paints (see the saved snapshot); here the row must carry the severity that drives it.
        AuditSeverity SeverityOf(string text)
        {
            DataGridViewRow row = grid.Rows.Cast<DataGridViewRow>().First(r => r.Cells["What"].Value!.ToString()!.Contains(text));
            return (AuditSeverity)row.DataBoundItem!.GetType().GetProperty("Severity", Any)!.GetValue(row.DataBoundItem)!;
        }
        Assert.AreEqual(AuditSeverity.Attention, SeverityOf("ignored"), "an ignored email is amber");
        Assert.AreEqual(AuditSeverity.Security, SeverityOf("Failed sign-in"), "a failed sign-in is red");
        Assert.AreEqual(AuditSeverity.Done, SeverityOf("Refund approved"), "an approved refund is green");

        Assert.AreEqual("3", Field<StatTile>(form, "_auditTileAttention").Value, "sender differs, ignored email and failed sign-in");
        Assert.AreEqual("1", Field<StatTile>(form, "_auditTileRefunds").Value);
        Assert.AreEqual("3", Field<StatTile>(form, "_auditTileEmails").Value);
    });

    [TestMethod]
    public void TheTabFitsAtTheMinimumSize_WithTheChipsWrapped() => FormHarness.OnStaThread(() =>
    {
        using var harness = new FormHarness();
        Seed(harness.Store);
        using AdminForm form = OpenAuditTab(harness);
        form.Size = form.MinimumSize;
        Application.DoEvents();

        foreach (GlassButton chip in All(form).OfType<GlassButton>().Where(b => b.Parent is FlowLayoutPanel { WrapContents: true }))
        {
            Rectangle r = form.RectangleToClient(chip.Parent!.RectangleToScreen(chip.Bounds));
            Assert.IsLessThanOrEqualTo(form.ClientSize.Width, r.Right, $"{chip.Text} runs past the right edge");
        }
        var tiles = Field<StatTile>(form, "_auditTileToday");
        Assert.IsFalse(tiles.Parent!.Parent!.Visible, "the numbers give way to the list in a short window");
        var grid = Field<DataGridView>(form, "_audit");
        Assert.IsGreaterThanOrEqualTo(2, grid.DisplayedRowCount(false), "the list is not squeezed out, even in the smallest window");
        FormHarness.SaveSnapshot(form, "audit-tab-with-events-min");
    });
}
