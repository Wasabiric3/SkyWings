using System.Reflection;
using Microsoft.Data.Sqlite;
using SkyWings.UI;

namespace SkyWings.Tests;

/// <summary>Click-a-header sorting (remembered between sessions) and "how long ago" times on the staff tabs other than Bookings.</summary>
[TestClass]
[DoNotParallelize]
public sealed class OtherTabsSortingTests
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static readonly DateTime FlightDay = new(2030, 3, 5);

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
    private static T Prop<T>(object row, string name) => (T)row.GetType().GetProperty(name, Any)!.GetValue(row)!;

    private sealed class Desk : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"skywings-othertabs-{Guid.NewGuid():N}.db");
        private int _seat;

        public Desk()
        {
            Store = new BookingStore(new SqliteConnectionStringBuilder { DataSource = _path, ForeignKeys = true, Pooling = false }.ConnectionString);
            Store.SeedFlightsIfEmpty(AppData.DomesticFlights, AppData.InternationalFlights, AppData.DefaultDomesticCapacity, AppData.DefaultInternationalCapacity);
            Service = new AdminService(Store, new StaffSession(1, "admin", "Admin", StaffRole.SuperAdmin, false));
        }

        public BookingStore Store { get; }
        public AdminService Service { get; }

        public void Book(string reference, DateTime bookedAt, decimal total = 4000m)
        {
            var person = new PassengerRecord("Ana Example", "1 St", "09171234567", 35, "1991-01-01", "P1234567A", false, false,
                new SeatAssignment("Ana Example", $"ECO{++_seat}A"), Array.Empty<string>(), total);
            var booking = new BookingRecord(reference, bookedAt, "PR-101", "Manila (MNL)", "Cebu (CEB)", FlightDay, true,
                "Economy", "Credit Card", "**** 4521", total, "Scheduled", new[] { person }, AppData.StatusConfirmed, "PAID", "TXN-" + reference, bookedAt, 0m);
            Store.SaveConfirmedBooking(booking, false, total, "Credit Card", "**** 4521", "TXN-" + reference, bookedAt);
        }

        public AdminForm Open(string tab)
        {
            var form = new AdminForm(Service);
            form.CreateControl();
            form.Show();
            Application.DoEvents();
            All(form).OfType<GlassButton>().First(b => b.Text.StartsWith(tab, StringComparison.Ordinal)).PerformClick();
            Application.DoEvents();
            return form;
        }

        public void Dispose()
        {
            try { if (File.Exists(_path)) File.Delete(_path); } catch (IOException) { /* a temp file */ }
        }
    }

    private static void ClickHeader(AdminForm form, string grid, string dataProperty)
    {
        var view = Field<DataGridView>(form, grid);
        int index = view.Columns.Cast<DataGridViewColumn>().First(c => c.DataPropertyName == dataProperty).Index;
        typeof(DataGridView).GetMethod("OnColumnHeaderMouseClick", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(view, new object[] { new DataGridViewCellMouseEventArgs(index, -1, 5, 5, new MouseEventArgs(MouseButtons.Left, 1, 5, 5, 0)) });
        Application.DoEvents();
    }

    private static List<object> Rows(AdminForm form, string grid)
        => Field<DataGridView>(form, grid).Rows.Cast<DataGridViewRow>().Select(r => r.DataBoundItem!).ToList();

    private static SortOrder Arrow(AdminForm form, string grid, string dataProperty)
        => Field<DataGridView>(form, grid).Columns.Cast<DataGridViewColumn>().First(c => c.DataPropertyName == dataProperty).HeaderCell.SortGlyphDirection;

    // ------------------------------------------------------------------ flights

    [TestMethod]
    public void Flights_SortByPrice_ThenByDepartureTime_AndTheChoiceIsRemembered() => OnStaThread(() =>
    {
        using var desk = new Desk();
        using (AdminForm form = desk.Open("Flights"))
        {
            Assert.IsGreaterThan(5, Rows(form, "_flights").Count, "the seeded schedule");
            ClickHeader(form, "_flights", "BasePrice");
            decimal[] down = Rows(form, "_flights").Select(r => Prop<decimal>(r, "BasePrice")).ToArray();
            CollectionAssert.AreEqual(down.OrderByDescending(p => p).ToArray(), down, "the first click on a price is dearest first");
            ClickHeader(form, "_flights", "BasePrice");
            decimal[] up = Rows(form, "_flights").Select(r => Prop<decimal>(r, "BasePrice")).ToArray();
            CollectionAssert.AreEqual(up.OrderBy(p => p).ToArray(), up);
            Assert.AreEqual(SortOrder.Ascending, Arrow(form, "_flights", "BasePrice"));

            ClickHeader(form, "_flights", "DepartureDisplay");
            TimeOnly[] times = Rows(form, "_flights").Select(r => AppData.FlightScheduledTimes.GetValueOrDefault(Prop<string>(r, "FlightNo"))).ToArray();
            CollectionAssert.AreEqual(times.OrderBy(t => t).ToArray(), times, "departure sorts by the time of day, not as text");
        }
        Assert.AreEqual("DepartureDisplay:asc", desk.Service.GetViewSetting("view_flights_sort", ""));
        using AdminForm again = desk.Open("Flights");
        Assert.AreEqual(SortOrder.Ascending, Arrow(again, "_flights", "DepartureDisplay"), "the next session starts sorted the same way");
    });

    // ------------------------------------------------------------------ staff

    [TestMethod]
    public void Staff_SortByName_AndTheChoiceIsRemembered() => OnStaThread(() =>
    {
        using var desk = new Desk();
        desk.Service.CreateStaff("zed.agent", "Zed Agent", "Temp-Pass-12345", StaffRole.Agent);
        desk.Service.CreateStaff("amy.agent", "Amy Agent", "Temp-Pass-12345", StaffRole.Agent);
        using (AdminForm form = desk.Open("Staff"))
        {
            ClickHeader(form, "_staff", "Username");
            string[] names = Rows(form, "_staff").Select(r => Prop<string>(r, "Username")).ToArray();
            CollectionAssert.AreEqual(names.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToArray(), names);
            Assert.IsGreaterThanOrEqualTo(2, names.Length);
            ClickHeader(form, "_staff", "Username");
            CollectionAssert.AreEqual(names.Reverse().ToArray(), Rows(form, "_staff").Select(r => Prop<string>(r, "Username")).ToArray());
        }
        Assert.AreEqual("Username:desc", desk.Service.GetViewSetting("view_staff_sort", ""));
    });

    // ------------------------------------------------------------------ audit

    [TestMethod]
    public void Audit_KeepsNewestFirst_UntilAHeaderIsClicked_ThenSortsByIt() => OnStaThread(() =>
    {
        using var desk = new Desk();
        desk.Store.WriteAudit("mike", "Agent", "LOGIN", "mike");
        Thread.Sleep(1100);   // the audit time has a one-second resolution
        desk.Store.WriteAudit("anna", "Agent", "CANCEL", "SKY-1");
        using AdminForm form = desk.Open("Audit");
        string[] first = Rows(form, "_audit").Select(r => Prop<string>(r, "Who")).Where(w => w is "mike" or "anna").ToArray();
        CollectionAssert.AreEqual(new[] { "anna", "mike" }, first, "newest first, as the log has always been");
        Assert.AreEqual(SortOrder.None, Arrow(form, "_audit", "When"));

        ClickHeader(form, "_audit", "Who");
        string[] byWho = Rows(form, "_audit").Select(r => Prop<string>(r, "Who")).ToArray();
        CollectionAssert.AreEqual(byWho.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToArray(), byWho);

        ClickHeader(form, "_audit", "When");   // newest first on the first click
        CollectionAssert.AreEqual(new[] { "anna", "mike" }, Rows(form, "_audit").Select(r => Prop<string>(r, "Who")).Where(w => w is "mike" or "anna").ToArray());
        ClickHeader(form, "_audit", "When");   // then oldest first
        CollectionAssert.AreEqual(new[] { "mike", "anna" }, Rows(form, "_audit").Select(r => Prop<string>(r, "Who")).Where(w => w is "mike" or "anna").ToArray());
    });

    [TestMethod]
    public void Audit_TheTimeColumnStaysExact_NotAsHowLongAgo() => OnStaThread(() =>
    {
        using var desk = new Desk();
        desk.Store.WriteAudit("mike", "Agent", "LOGIN", "mike");
        using AdminForm form = desk.Open("Audit");
        string when = Prop<string>(Rows(form, "_audit").First(r => Prop<string>(r, "Who") == "mike"), "When");
        StringAssert.Matches(when, new System.Text.RegularExpressions.Regex(@"\d{1,2}:\d{2}:\d{2} (AM|PM)"), "an audit trail keeps the exact second");
    });

    // ------------------------------------------------------------------ waitlist

    [TestMethod]
    public void Waitlist_SortByCode_AndTheChoiceIsRemembered() => OnStaThread(() =>
    {
        using var desk = new Desk();
        foreach (string code in new[] { "WL-CCCCCC01", "WL-AAAAAA01", "WL-BBBBBB01" }) desk.Store.AddToWaitlist(code, "PR-101", FlightDay);
        using (AdminForm form = desk.Open("Waitlist"))
        {
            ClickHeader(form, "_waitlist", "Code");
            CollectionAssert.AreEqual(new[] { "WL-AAAAAA01", "WL-BBBBBB01", "WL-CCCCCC01" }, Rows(form, "_waitlist").Select(r => Prop<string>(r, "Code")).ToArray());
            ClickHeader(form, "_waitlist", "Code");
            CollectionAssert.AreEqual(new[] { "WL-CCCCCC01", "WL-BBBBBB01", "WL-AAAAAA01" }, Rows(form, "_waitlist").Select(r => Prop<string>(r, "Code")).ToArray());
        }
        Assert.AreEqual("Code:desc", desk.Service.GetViewSetting("view_waitlist_sort", ""));
    });

    [TestMethod]
    public void Waitlist_HasNoExtraColumns_TheSortValuesAreHidden() => OnStaThread(() =>
    {
        using var desk = new Desk();
        desk.Store.AddToWaitlist("WL-AAAAAA01", "PR-101", FlightDay);
        using AdminForm form = desk.Open("Waitlist");
        string[] columns = Field<DataGridView>(form, "_waitlist").Columns.Cast<DataGridViewColumn>().Select(c => c.DataPropertyName).ToArray();
        CollectionAssert.AreEqual(new[] { "Flight", "Date", "Cabin", "Traveller", "Seats", "Joined", "Status", "Offer", "Code" }, columns);
    });

    // ------------------------------------------------------------------ refund requests

    [TestMethod]
    public void RefundRequests_KeepPendingFirst_UntilAHeaderIsClicked_AndLoggedReadsAsHowLongAgo() => OnStaThread(() =>
    {
        using var desk = new Desk();
        desk.Book("SKY-OTHER0000001", DateTime.Now.AddHours(-3), total: 9000m);
        desk.Book("SKY-OTHER0000002", DateTime.Now.AddHours(-2), total: 3000m);
        Assert.IsTrue(desk.Store.LogRefundRequest("SKY-OTHER0000001", "agent1").Ok);
        Assert.IsTrue(desk.Store.LogRefundRequest("SKY-OTHER0000002", "agent1").Ok);
        using (AdminForm form = desk.Open("Refunds"))
        {
            Field<ComboBox>(form, "_refundView").SelectedIndex = 1;   // Refund requests
            Application.DoEvents();
            Assert.AreEqual("just now", Prop<string>(Rows(form, "_requests")[0], "Logged"));
            Assert.AreEqual(SortOrder.None, Arrow(form, "_requests", "Logged"));

            ClickHeader(form, "_requests", "TotalAmount");
            decimal[] totals = Rows(form, "_requests").Select(r => Prop<decimal>(r, "TotalAmount")).ToArray();
            CollectionAssert.AreEqual(totals.OrderByDescending(t => t).ToArray(), totals);
        }
        Assert.AreEqual("TotalAmount:desc", desk.Service.GetViewSetting("view_requests_sort", ""));
    });

    // ------------------------------------------------------------------ refunded bookings

    [TestMethod]
    public void RefundedBookings_SortByAmount_TheFilterDropdownResetsIt_AndTheTimeReadsAsHowLongAgo() => OnStaThread(() =>
    {
        using var desk = new Desk();
        desk.Book("SKY-OTHER0000011", DateTime.Now.AddHours(-3), total: 9000m);
        desk.Book("SKY-OTHER0000012", DateTime.Now.AddHours(-2), total: 3000m);
        Assert.IsTrue(desk.Store.CancelBooking("SKY-OTHER0000011", 1800m, "agent1"));
        Assert.IsTrue(desk.Store.CancelBooking("SKY-OTHER0000012", 600m, "agent1"));
        using AdminForm form = desk.Open("Refunds");
        Assert.AreEqual("just now", Prop<string>(Rows(form, "_refunds")[0], "Refunded"));
        Assert.AreEqual(2, Rows(form, "_refunds").Count);

        ClickHeader(form, "_refunds", "RefundAmount");
        CollectionAssert.AreEqual(new[] { 1800m, 600m }, Rows(form, "_refunds").Select(r => Prop<decimal>(r, "RefundAmount")).ToArray());
        ClickHeader(form, "_refunds", "RefundAmount");
        CollectionAssert.AreEqual(new[] { 600m, 1800m }, Rows(form, "_refunds").Select(r => Prop<decimal>(r, "RefundAmount")).ToArray());
        Assert.AreEqual(SortOrder.Ascending, Arrow(form, "_refunds", "RefundAmount"));

        // Choosing "Newest first" (or any other order) in the dropdown is the order the user now wants: the header sort steps aside.
        var sort = Field<ComboBox>(form, "_refundSort");
        sort.SelectedIndex = sort.SelectedIndex == 0 ? 1 : 0;
        Application.DoEvents();
        Assert.AreEqual(SortOrder.None, Arrow(form, "_refunds", "RefundAmount"));
    });

    [TestMethod]
    public void RefundedBookings_HaveNoExtraColumns_TheSortValuesAreHidden() => OnStaThread(() =>
    {
        using var desk = new Desk();
        desk.Book("SKY-OTHER0000021", DateTime.Now.AddHours(-3));
        Assert.IsTrue(desk.Store.CancelBooking("SKY-OTHER0000021", 100m, "agent1"));
        using AdminForm form = desk.Open("Refunds");
        string[] refunded = Field<DataGridView>(form, "_refunds").Columns.Cast<DataGridViewColumn>().Select(c => c.DataPropertyName).ToArray();
        CollectionAssert.AreEqual(new[] { "Reference", "Refunded", "FlightNo", "Route", "FlightDate", "TotalAmount", "RefundAmount", "RefundRef", "Method", "HandledBy" }, refunded);
        Field<ComboBox>(form, "_refundView").SelectedIndex = 1;
        Application.DoEvents();
        string[] requests = Field<DataGridView>(form, "_requests").Columns.Cast<DataGridViewColumn>().Select(c => c.DataPropertyName).ToArray();
        CollectionAssert.AreEqual(new[] { "Reference", "Status", "Source", "Logged", "FlightNo", "Route", "FlightDate", "TotalAmount", "EstimatedRefund", "Note" }, requests);
    });
}
