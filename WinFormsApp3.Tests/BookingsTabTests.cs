using System.Reflection;
using Microsoft.Data.Sqlite;
using SkyWings.UI;

namespace SkyWings.Tests;

/// <summary>The staff Bookings tab: newest first with the time shown, sortable headers, the "Recently booked" views and the "new" marks.</summary>
[TestClass]
[DoNotParallelize]
public sealed class BookingsTabTests
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

    private sealed class Desk : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"skywings-bookingstab-{Guid.NewGuid():N}.db");
        private int _seat;

        public Desk()
        {
            Store = new BookingStore(new SqliteConnectionStringBuilder { DataSource = _path, ForeignKeys = true, Pooling = false }.ConnectionString);
            Store.SeedFlightsIfEmpty(AppData.DomesticFlights, AppData.InternationalFlights, AppData.DefaultDomesticCapacity, AppData.DefaultInternationalCapacity);
            Service = new AdminService(Store, new StaffSession(1, "admin", "Admin", StaffRole.SuperAdmin, false));
        }

        public BookingStore Store { get; }
        public AdminService Service { get; }

        public void Book(string reference, DateTime bookedAt, DateTime flightDate, decimal total = 4000m)
        {
            var person = new PassengerRecord("Ana Example", "1 St", "09171234567", 35, "1991-01-01", "P1234567A", false, false,
                new SeatAssignment("Ana Example", $"ECO{++_seat}A"), Array.Empty<string>(), total);
            var booking = new BookingRecord(reference, bookedAt, "PR-101", "Manila (MNL)", "Cebu (CEB)", flightDate, true,
                "Economy", "Credit Card", "**** 4521", total, "Scheduled", new[] { person }, AppData.StatusConfirmed, "PAID", "TXN-" + reference, bookedAt, 0m);
            Store.SaveConfirmedBooking(booking, false, total, "Credit Card", "**** 4521", "TXN-" + reference, bookedAt);
        }

        public AdminForm OpenBookings()
        {
            var form = new AdminForm(Service);
            form.CreateControl();
            form.Show();
            Application.DoEvents();
            All(form).OfType<GlassButton>().First(b => b.Text == "Bookings").PerformClick();
            Application.DoEvents();
            return form;
        }

        public void Dispose()
        {
            try { if (File.Exists(_path)) File.Delete(_path); } catch (IOException) { /* a temp file */ }
        }
    }

    private static void ShowDate(AdminForm form, DateTime date)
    {
        Field<DateField>(form, "_bookingDate").Value = date;
        Application.DoEvents();
    }

    private static List<object> Rows(AdminForm form)
        => Field<DataGridView>(form, "_bookings").Rows.Cast<DataGridViewRow>().Select(r => r.DataBoundItem!).ToList();

    private static T Prop<T>(object row, string name) => (T)row.GetType().GetProperty(name, Any)!.GetValue(row)!;

    private static string[] Refs(AdminForm form) => Rows(form).Select(r => Prop<string>(r, "Reference")).ToArray();

    private static void ClickHeader(AdminForm form, string dataProperty)
    {
        var grid = Field<DataGridView>(form, "_bookings");
        int index = grid.Columns.Cast<DataGridViewColumn>().First(c => c.DataPropertyName == dataProperty).Index;
        typeof(DataGridView).GetMethod("OnColumnHeaderMouseClick", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(grid, new object[] { new DataGridViewCellMouseEventArgs(index, -1, 5, 5, new MouseEventArgs(MouseButtons.Left, 1, 5, 5, 0)) });
        Application.DoEvents();
    }

    private static void ChooseView(AdminForm form, string label)
    {
        var view = Field<ComboBox>(form, "_bookingView");
        view.SelectedIndex = view.Items.IndexOf(label);
        Application.DoEvents();
    }

    // ------------------------------------------------------------------ order and time

    [TestMethod]
    public void ByFlightDate_ListsTheNewestBookingFirst_AndTheBookedColumnShowsTheTime() => OnStaThread(() =>
    {
        using var desk = new Desk();
        DateTime now = DateTime.Now;
        desk.Book("SKY-TABTEST000001", now.AddDays(-3), FlightDay);
        desk.Book("SKY-TABTEST000002", now.AddMinutes(-12), FlightDay);
        desk.Book("SKY-TABTEST000003", now.AddHours(-5), FlightDay);
        using AdminForm form = desk.OpenBookings();
        ShowDate(form, FlightDay);

        CollectionAssert.AreEqual(new[] { "SKY-TABTEST000002", "SKY-TABTEST000003", "SKY-TABTEST000001" }, Refs(form));
        Assert.AreEqual("12 min ago", Prop<string>(Rows(form)[0], "BookedAt"));
        StringAssert.Matches(Prop<string>(Rows(form)[2], "BookedAt"), new System.Text.RegularExpressions.Regex(@"\d{1,2}:\d{2} (AM|PM)"), "an older booking shows the time of day");
    });

    [TestMethod]
    public void TheListHasNoExtraColumns_TheSortValuesAreHidden() => OnStaThread(() =>
    {
        using var desk = new Desk();
        desk.Book("SKY-TABTEST000011", DateTime.Now.AddHours(-1), FlightDay);
        using AdminForm form = desk.OpenBookings();
        ShowDate(form, FlightDay);
        var grid = Field<DataGridView>(form, "_bookings");
        string[] columns = grid.Columns.Cast<DataGridViewColumn>().Select(c => c.DataPropertyName).ToArray();
        CollectionAssert.AreEqual(new[] { "Reference", "BookedAt", "FlightNo", "Route", "FlightDate", "Passengers", "TotalAmount", "Status", "PaymentStatus", "RefundAmount" }, columns);
    });

    // ------------------------------------------------------------------ header sorting

    [TestMethod]
    public void ClickingAHeader_SortsByIt_ClickingAgainReverses_AndTheChoiceIsRemembered() => OnStaThread(() =>
    {
        using var desk = new Desk();
        DateTime now = DateTime.Now;
        desk.Book("SKY-TABTEST000021", now.AddHours(-1), FlightDay, total: 5000m);
        desk.Book("SKY-TABTEST000022", now.AddHours(-2), FlightDay, total: 3000m);
        desk.Book("SKY-TABTEST000023", now.AddHours(-3), FlightDay, total: 9000m);
        using (AdminForm form = desk.OpenBookings())
        {
            ShowDate(form, FlightDay);
            ClickHeader(form, "TotalAmount");
            CollectionAssert.AreEqual(new[] { "SKY-TABTEST000023", "SKY-TABTEST000021", "SKY-TABTEST000022" }, Refs(form), "amounts start largest first");
            ClickHeader(form, "TotalAmount");
            CollectionAssert.AreEqual(new[] { "SKY-TABTEST000022", "SKY-TABTEST000021", "SKY-TABTEST000023" }, Refs(form), "then smallest first");
            ClickHeader(form, "Reference");
            CollectionAssert.AreEqual(new[] { "SKY-TABTEST000021", "SKY-TABTEST000022", "SKY-TABTEST000023" }, Refs(form), "text starts A to Z");
            ClickHeader(form, "BookedAt");
            CollectionAssert.AreEqual(new[] { "SKY-TABTEST000021", "SKY-TABTEST000022", "SKY-TABTEST000023" }, Refs(form), "the booking time starts newest first");
            ClickHeader(form, "TotalAmount");
            ClickHeader(form, "TotalAmount");
        }
        Assert.AreEqual("TotalAmount:asc", desk.Service.GetViewSetting("view_booking_sort", ""));

        using AdminForm again = desk.OpenBookings();
        ShowDate(again, FlightDay);
        CollectionAssert.AreEqual(new[] { "SKY-TABTEST000022", "SKY-TABTEST000021", "SKY-TABTEST000023" }, Refs(again), "the next session starts with the remembered sort");
    });

    [TestMethod]
    public void TheSortedColumn_ShowsAnArrow() => OnStaThread(() =>
    {
        using var desk = new Desk();
        desk.Book("SKY-TABTEST000031", DateTime.Now.AddHours(-1), FlightDay);
        using AdminForm form = desk.OpenBookings();
        ShowDate(form, FlightDay);
        var grid = Field<DataGridView>(form, "_bookings");
        DataGridViewColumn booked = grid.Columns.Cast<DataGridViewColumn>().First(c => c.DataPropertyName == "BookedAt");
        Assert.AreEqual(SortOrder.Descending, booked.HeaderCell.SortGlyphDirection, "the default is newest first, and it is marked");
        ClickHeader(form, "FlightNo");
        Assert.AreEqual(SortOrder.None, booked.HeaderCell.SortGlyphDirection);
        Assert.AreEqual(SortOrder.Ascending, grid.Columns.Cast<DataGridViewColumn>().First(c => c.DataPropertyName == "FlightNo").HeaderCell.SortGlyphDirection);
    });

    [TestMethod]
    public void TheSortSurvivesSearchingAndFiltering() => OnStaThread(() =>
    {
        using var desk = new Desk();
        DateTime now = DateTime.Now;
        desk.Book("SKY-TABTEST000041", now.AddHours(-1), FlightDay, total: 5000m);
        desk.Book("SKY-TABTEST000042", now.AddHours(-2), FlightDay, total: 3000m);
        desk.Book("SKY-TABTEST000043", now.AddHours(-3), FlightDay, total: 9000m);
        using AdminForm form = desk.OpenBookings();
        ShowDate(form, FlightDay);
        ClickHeader(form, "TotalAmount");
        Field<TextBox>(form, "_bookingSearch").Text = "ana";
        typeof(AdminForm).GetMethod("ApplyBookingFilter", Any)!.Invoke(form, null);
        Application.DoEvents();
        CollectionAssert.AreEqual(new[] { "SKY-TABTEST000043", "SKY-TABTEST000041", "SKY-TABTEST000042" }, Refs(form));
    });

    // ------------------------------------------------------------------ the recently booked views

    [TestMethod]
    public void RecentlyBooked_ListsBookingsFromEveryFlightDate_NewestFirst_AndTheDatePickerIsOff() => OnStaThread(() =>
    {
        using var desk = new Desk();
        DateTime now = DateTime.Now;
        desk.Book("SKY-TABTEST000051", now.AddMinutes(-10), new DateTime(2030, 3, 5));
        desk.Book("SKY-TABTEST000052", now.AddHours(-3), new DateTime(2030, 6, 9));
        desk.Book("SKY-TABTEST000053", now.AddDays(-2), new DateTime(2030, 9, 1));
        desk.Book("SKY-TABTEST000054", now.AddDays(-20), new DateTime(2030, 12, 24));
        using AdminForm form = desk.OpenBookings();

        ChooseView(form, "Booked in the last 7 days");
        CollectionAssert.AreEqual(new[] { "SKY-TABTEST000051", "SKY-TABTEST000052", "SKY-TABTEST000053" }, Refs(form));
        Assert.IsFalse(Field<DateField>(form, "_bookingDate").Enabled, "the flight date does not apply to this view");
        StringAssert.Contains(Field<Label>(form, "_bookingCount").Text, "last 7 days");

        ChooseView(form, "Booked in the last 24 hours");
        CollectionAssert.AreEqual(new[] { "SKY-TABTEST000051", "SKY-TABTEST000052" }, Refs(form));
        ChooseView(form, "Booked in the last 30 days");
        Assert.HasCount(4, Refs(form));

        ChooseView(form, "By flight date");
        Assert.IsTrue(Field<DateField>(form, "_bookingDate").Enabled);
    });

    [TestMethod]
    public void RecentlyBooked_AlsoSortsByHeader_AndFiltersByStatusAndSearch() => OnStaThread(() =>
    {
        using var desk = new Desk();
        DateTime now = DateTime.Now;
        desk.Book("SKY-TABTEST000061", now.AddMinutes(-10), new DateTime(2030, 3, 5), total: 7000m);
        desk.Book("SKY-TABTEST000062", now.AddHours(-3), new DateTime(2030, 6, 9), total: 2000m);
        using AdminForm form = desk.OpenBookings();
        ChooseView(form, "Booked in the last 7 days");
        ClickHeader(form, "TotalAmount");
        ClickHeader(form, "TotalAmount");
        CollectionAssert.AreEqual(new[] { "SKY-TABTEST000062", "SKY-TABTEST000061" }, Refs(form), "smallest first");
        Field<TextBox>(form, "_bookingSearch").Text = "SKY-TABTEST000061";
        typeof(AdminForm).GetMethod("ApplyBookingFilter", Any)!.Invoke(form, null);
        CollectionAssert.AreEqual(new[] { "SKY-TABTEST000061" }, Refs(form));
    });

    [TestMethod]
    public void TheChosenView_IsRemembered() => OnStaThread(() =>
    {
        using var desk = new Desk();
        desk.Book("SKY-TABTEST000071", DateTime.Now.AddHours(-1), FlightDay);
        using (AdminForm form = desk.OpenBookings()) ChooseView(form, "Booked in the last 30 days");
        Assert.AreEqual("30d", desk.Service.GetViewSetting("view_booking_view", ""));
        using AdminForm again = desk.OpenBookings();
        Assert.AreEqual("Booked in the last 30 days", Field<ComboBox>(again, "_bookingView").SelectedItem);
        Assert.HasCount(1, Refs(again));
    });

    // ------------------------------------------------------------------ new bookings

    [TestMethod]
    public void BookingsMadeSinceTheLastLoad_AreMarkedNew_UntilTheNextOne() => OnStaThread(() =>
    {
        using var desk = new Desk();
        desk.Book("SKY-TABTEST000081", DateTime.Now.AddHours(-2), FlightDay);
        using AdminForm form = desk.OpenBookings();
        ShowDate(form, FlightDay);
        Assert.IsFalse(Rows(form).Any(r => Prop<bool>(r, "IsNew")), "the first look has nothing to compare with");

        Thread.Sleep(20);
        desk.Book("SKY-TABTEST000082", DateTime.Now, FlightDay);
        typeof(AdminForm).GetMethod("LoadBookingsFromStore", Any)!.Invoke(form, null);   // what Refresh does
        Application.DoEvents();
        var newOnes = Rows(form).Where(r => Prop<bool>(r, "IsNew")).Select(r => Prop<string>(r, "Reference")).ToArray();
        CollectionAssert.AreEqual(new[] { "SKY-TABTEST000082" }, newOnes);
        StringAssert.Contains(Field<Label>(form, "_bookingCount").Text, "1 new");

        typeof(AdminForm).GetMethod("LoadBookingsFromStore", Any)!.Invoke(form, null);
        Assert.IsFalse(Rows(form).Any(r => Prop<bool>(r, "IsNew")), "seen now, so no longer new");
        StringAssert.DoesNotMatch(Field<Label>(form, "_bookingCount").Text, new System.Text.RegularExpressions.Regex("new"));
    });

    [TestMethod]
    public void ChangingTheDateOrTheView_StartsAFreshComparison_SoNothingIsFalselyNew() => OnStaThread(() =>
    {
        using var desk = new Desk();
        desk.Book("SKY-TABTEST000091", DateTime.Now.AddMinutes(-5), FlightDay);
        desk.Book("SKY-TABTEST000092", DateTime.Now.AddMinutes(-1), new DateTime(2030, 8, 8));
        using AdminForm form = desk.OpenBookings();
        ShowDate(form, FlightDay);
        ShowDate(form, new DateTime(2030, 8, 8));
        Assert.IsFalse(Rows(form).Any(r => Prop<bool>(r, "IsNew")), "a different flight date is a different list");
        ChooseView(form, "Booked in the last 7 days");
        Assert.IsFalse(Rows(form).Any(r => Prop<bool>(r, "IsNew")), "a different view is a different list");
    });

    // ------------------------------------------------------------------ layout

    [TestMethod]
    public void BothToolbarRows_FitAtTheMinimumWindowWidth() => OnStaThread(() =>
    {
        using var desk = new Desk();
        desk.Book("SKY-TABTEST000101", DateTime.Now.AddHours(-1), FlightDay);
        using AdminForm form = desk.OpenBookings();
        form.Size = form.MinimumSize;
        Application.DoEvents();
        // Only what the Bookings tab shows (the other tabs have buttons with the same names, hidden).
        var shownButtons = All(form).OfType<GlassButton>().Where(b => b.Visible && b.Text is "Details" or "Refresh" or "Export CSV" or "Cancel booking").ToList();
        Assert.IsGreaterThanOrEqualTo(3, shownButtons.Count);
        foreach (Control control in new Control[] { Field<ComboBox>(form, "_bookingView"), Field<DateField>(form, "_bookingDate"),
                     Field<ComboBox>(form, "_bookingStatus"), Field<TextBox>(form, "_bookingSearch") }.Concat(shownButtons))
        {
            Assert.IsTrue(control.Visible, control.GetType().Name + " is showing");
            Rectangle r = form.RectangleToClient(control.RectangleToScreen(control.ClientRectangle));
            Assert.IsTrue(form.ClientRectangle.Contains(r), $"{control.GetType().Name} {(control as GlassButton)?.Text} is inside the window ({r})");
        }
    });

    // ------------------------------------------------------------------ the count text

    [TestMethod]
    public void TheCountText_SaysWhatIsShown_AndHowManyAreNew()
    {
        MethodInfo count = typeof(AdminForm).GetMethod("BookingCountText", BindingFlags.Static | BindingFlags.NonPublic)!;
        string Text(string view, int shown, int loaded, int total, int fresh, string day = "Nov 22")
            => (string)count.Invoke(null, new object[] { view, shown, loaded, total, fresh, day })!;

        Assert.AreEqual("540 bookings on Nov 22", Text("date", 540, 540, 540, 0));
        Assert.AreEqual("3 of 540 bookings on Nov 22", Text("date", 3, 540, 540, 0));
        Assert.AreEqual("540 bookings on Nov 22 · 4 new", Text("date", 540, 540, 540, 4));
        Assert.AreEqual("12 bookings from the last 7 days", Text("7d", 12, 12, 12, 0));
        Assert.AreEqual("1 booking from the last 24 hours", Text("24h", 1, 1, 1, 0));
        Assert.AreEqual("newest 1,000 of 3,412 bookings from the last 30 days", Text("30d", 1000, 1000, 3412, 0));
        Assert.AreEqual("2 of 1,000 (newest of 3,412) bookings from the last 30 days · 1 new", Text("30d", 2, 1000, 3412, 1));
    }

    [TestMethod]
    public void RecentSince_IsTheStartOfTheChosenWindow()
    {
        DateTime now = new(2026, 11, 22, 12, 0, 0);
        MethodInfo since = typeof(AdminForm).GetMethod("RecentSince", BindingFlags.Static | BindingFlags.NonPublic)!;
        Assert.AreEqual(now.AddHours(-24), (DateTime)since.Invoke(null, new object[] { "24h", now })!);
        Assert.AreEqual(now.AddDays(-7), (DateTime)since.Invoke(null, new object[] { "7d", now })!);
        Assert.AreEqual(now.AddDays(-30), (DateTime)since.Invoke(null, new object[] { "30d", now })!);
    }
}
