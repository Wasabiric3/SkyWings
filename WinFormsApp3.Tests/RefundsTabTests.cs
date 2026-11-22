using System.Reflection;
using Microsoft.Data.Sqlite;
using SkyWings.UI;

namespace SkyWings.Tests;

/// <summary>The admin Refunds tab: both views open, their toolbars fit, and the tab button counts open requests.</summary>
[TestClass]
[DoNotParallelize]
public sealed class RefundsTabTests
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic;

    private static void OnStaThread(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new AssertFailedException(error.Message, error);
    }

    private static IEnumerable<Control> All(Control root)
    {
        foreach (Control c in root.Controls)
        {
            yield return c;
            foreach (Control inner in All(c)) yield return inner;
        }
    }

    private static T Field<T>(object owner, string name) => (T)owner.GetType().GetField(name, Any)!.GetValue(owner)!;

    private static void Book(BookingStore store, string reference)
    {
        var person = new PassengerRecord("Ana Example", "1 St", "09171234567", 35, "1991-01-01", "P1234567A", false, false,
            new SeatAssignment("Ana Example", "ECO1A"), Array.Empty<string>(), 4000m);
        var booking = new BookingRecord(reference, DateTime.Now, "PR-101", "Manila (MNL)", "Cebu (CEB)", new DateTime(2030, 3, 5), true,
            "Economy", "Credit Card", "**** 4521", 4000m, "Scheduled", new[] { person }, AppData.StatusConfirmed, "PAID", "TXN-" + reference, DateTime.Now, 0m);
        store.SaveConfirmedBooking(booking, false, 4000m, "Credit Card", "**** 4521", "TXN-" + reference, DateTime.Now);
    }

    [TestMethod]
    public void BothViews_FitTheToolbar_ListTheirRows_AndTheTabButtonCountsOpenRequests()
        => OnStaThread(() =>
        {
            string path = Path.Combine(Path.GetTempPath(), $"skywings-refundstab-{Guid.NewGuid():N}.db");
            try
            {
                var store = new BookingStore(new SqliteConnectionStringBuilder { DataSource = path, ForeignKeys = true, Pooling = false }.ConnectionString);
                store.SeedFlightsIfEmpty(AppData.DomesticFlights, AppData.InternationalFlights,
                    AppData.DefaultDomesticCapacity, AppData.DefaultInternationalCapacity);
                Book(store, "SKY-TAB000000001");
                Book(store, "SKY-TAB000000002");
                Assert.IsTrue(store.CancelBooking("SKY-TAB000000001", 3200m, "agent1"));
                Assert.IsTrue(store.LogRefundRequest("SKY-TAB000000002", "agent1").Ok);

                var session = new StaffSession(1, "admin", "Admin", StaffRole.SuperAdmin, false);
                using var form = new AdminForm(new AdminService(store, session));
                form.CreateControl();
                form.Show();
                Application.DoEvents();

                GlassButton nav = Field<GlassButton?>(form, "_refundsNav")!;
                Assert.AreEqual("Refunds (1)", nav.Text, "one request is waiting");
                nav.PerformClick();
                Application.DoEvents();

                var refunds = Field<DataGridView>(form, "_refunds");
                var requests = Field<DataGridView>(form, "_requests");
                var view = Field<ComboBox>(form, "_refundView");
                Assert.IsTrue(refunds.Visible);
                Assert.IsFalse(requests.Visible);
                Assert.AreEqual(1, refunds.Rows.Count, "the refunded booking");
                AssertToolbarFits(form, refunds, "Details", "Refresh", "Export CSV");

                view.SelectedIndex = 1;
                Application.DoEvents();
                Assert.IsFalse(refunds.Visible);
                Assert.IsTrue(requests.Visible);
                Assert.AreEqual(1, requests.Rows.Count, "the open request");
                AssertToolbarFits(form, requests, "Log request", "Details", "Refresh", "Export CSV");

                // Cancelling the booking answers the request; the count goes away after a refresh.
                Assert.IsTrue(store.CancelBooking("SKY-TAB000000002", 3200m, "agent1"));
                typeof(AdminForm).GetMethod("ReloadRefundsView", Any)!.Invoke(form, null);
                Application.DoEvents();
                Assert.AreEqual("Refunds", nav.Text);
                Assert.AreEqual(0, requests.Rows.Count);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        });

    [TestMethod]
    public void RequestDialog_ShowsTheCustomersNote_AndOnlyOffersApproveAndDeclineWhileOpen()
        => OnStaThread(() =>
        {
            var person = new PassengerRecord("Ana Example", "1 St", "09171234567", 35, "1991-01-01", "P1234567A", false, false,
                new SeatAssignment("Ana Example", "ECO1A"), Array.Empty<string>(), 4000m);
            var booking = new BookingRecord("SKY-DLG000000001", DateTime.Now, "PR-101", "Manila (MNL)", "Cebu (CEB)", new DateTime(2030, 3, 5), true,
                "Economy", "Credit Card", "**** 4521", 4000m, "Scheduled", new[] { person }, AppData.StatusConfirmed, "PAID", "TXN-DLG", DateTime.Now, 0m);
            var trip = new SkyWings.Controllers.TripStatusInfo(SkyWings.Controllers.TripPhase.Upcoming, "Upcoming", false, null, "Cebu (CEB)");
            RefundRequestRow Request(string status) => new(1, booking.Reference, "PR-101", "Manila (MNL) → Cebu (CEB)", booking.FlightDateValue, 4000m,
                3200m, status, "mailbox", DateTime.Now, null, "", "", "Please cancel, my plans changed.", "EMAIL", false);

            using (var open = new BookingDetailForm(booking, trip, Request(BookingStore.RequestOpen)))
            {
                open.CreateControl();
                var texts = AllControls(open).OfType<GlassButton>().Select(b => b.Text).ToList();
                CollectionAssert.Contains(texts, "Approve refund");
                CollectionAssert.Contains(texts, "Decline");
                Assert.IsTrue(AllControls(open).OfType<TextBox>().Any(t => t.Text == "Please cancel, my plans changed."), "the customer's note is shown");
                Assert.IsTrue(AllControls(open).OfType<Label>().Any(l => l.Text.Contains("not the one on the booking")), "a different sender is flagged");
            }

            using var closed = new BookingDetailForm(booking, trip, Request(BookingStore.RequestDismissed));
            closed.CreateControl();
            var closedTexts = AllControls(closed).OfType<GlassButton>().Select(b => b.Text).ToList();
            CollectionAssert.DoesNotContain(closedTexts, "Approve refund");
            CollectionAssert.DoesNotContain(closedTexts, "Decline");
        });

    private static IEnumerable<Control> AllControls(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (Control nested in AllControls(child)) yield return nested;
        }
    }

    private static void AssertToolbarFits(AdminForm form, DataGridView grid, params string[] buttons)
    {
        foreach (string text in buttons)
        {
            GlassButton button = All(form).OfType<GlassButton>().First(b => b.Text == text && b.Visible && IsInside(b, grid));
            Assert.IsGreaterThanOrEqualTo(0, button.Left, text);
            Assert.IsLessThanOrEqualTo(button.Parent!.ClientSize.Width, button.Right, text + " fits in the toolbar");
        }
    }

    // The same button text exists on other tabs; only the ones on the page that holds this grid count.
    private static bool IsInside(Control button, DataGridView grid)
    {
        for (Control? page = grid.Parent; page is not null; page = page.Parent)
            if (page.Controls.Contains(button) || All(page).Contains(button)) return page is not Form;
        return false;
    }

    [TestMethod]
    public void TheViewRow_FiltersTheList_FitsTheWindow_AndIsRememberedNextTime()
        => OnStaThread(() =>
        {
            string path = Path.Combine(Path.GetTempPath(), $"skywings-refundview-{Guid.NewGuid():N}.db");
            try
            {
                var store = new BookingStore(new SqliteConnectionStringBuilder { DataSource = path, ForeignKeys = true, Pooling = false }.ConnectionString);
                store.SeedFlightsIfEmpty(AppData.DomesticFlights, AppData.InternationalFlights,
                    AppData.DefaultDomesticCapacity, AppData.DefaultInternationalCapacity);
                Book(store, "SKY-VIEW00000001");
                Book(store, "SKY-VIEW00000002");
                Assert.IsTrue(store.CancelBooking("SKY-VIEW00000001", 1000m, "agent1"));
                Assert.IsTrue(store.CancelBooking("SKY-VIEW00000002", 2000m, "agent1"));
                using (var con = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ConnectionString))
                {
                    con.Open();
                    using var cmd = con.CreateCommand();
                    cmd.CommandText = "UPDATE booking_payments SET created_at = '2020-01-01 10:00:00' WHERE booking_ref = 'SKY-VIEW00000001' AND status = 'REFUND'";
                    cmd.ExecuteNonQuery();
                }

                var session = new StaffSession(1, "admin", "Admin", StaffRole.SuperAdmin, false);
                using (var form = new AdminForm(new AdminService(store, session)))
                {
                    form.CreateControl();
                    form.Show();
                    Application.DoEvents();
                    Field<GlassButton?>(form, "_refundsNav")!.PerformClick();
                    Application.DoEvents();

                    var refunds = Field<DataGridView>(form, "_refunds");
                    var period = Field<ComboBox>(form, "_refundPeriod");
                    var sort = Field<ComboBox>(form, "_refundSort");
                    var handled = Field<ComboBox>(form, "_refundHandled");
                    Assert.AreEqual(2, refunds.Rows.Count, "any time");

                    foreach (ComboBox box in new[] { period, sort, handled })
                    {
                        Assert.IsTrue(box.Visible, box.Name);
                        // the visible box is the rounded one around the drop-down (the drop-down itself is inset inside it)
                        Control shown = box.Parent!.Parent is SkyWings.UI.RoundedInput host ? host : box;
                        Assert.IsGreaterThanOrEqualTo(0, shown.Left);
                        Assert.IsLessThanOrEqualTo(shown.Parent!.ClientSize.Width, shown.Right, "the view row fits");
                    }

                    period.SelectedIndex = 2;   // Last 7 days
                    Application.DoEvents();
                    Assert.AreEqual(1, refunds.Rows.Count, "the 2020 refund is out of the period");
                    sort.SelectedIndex = 2;     // Largest refund
                    handled.SelectedIndex = 1;  // Handled by staff
                    Application.DoEvents();
                    Assert.AreEqual(1, refunds.Rows.Count);
                }

                using (var again = new AdminForm(new AdminService(store, session)))
                {
                    again.CreateControl();
                    again.Show();
                    Application.DoEvents();
                    Assert.AreEqual(2, Field<ComboBox>(again, "_refundPeriod").SelectedIndex, "the period was remembered");
                    Assert.AreEqual(2, Field<ComboBox>(again, "_refundSort").SelectedIndex);
                    Assert.AreEqual(1, Field<ComboBox>(again, "_refundHandled").SelectedIndex);
                }
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        });
}