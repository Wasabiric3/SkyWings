using System.Globalization;
using SkyWings.Controllers;
using SkyWings.UI;

namespace SkyWings;

/// <summary>
/// Staff workspace. It talks only to <see cref="AdminService"/>, which checks the signed-in role before every action,
/// and shows only the sections that role may use.
/// </summary>
public sealed class AdminForm : GlassForm
{
    private readonly AdminService _service;

    // navigation
    private readonly Dictionary<GlassButton, Panel> _sections = new();
    private readonly Panel _host = new() { Dock = DockStyle.Fill };

    // flights
    private readonly DataGridView _flights = new() { Dock = DockStyle.Fill, AutoGenerateColumns = true };
    private static readonly IReadOnlyDictionary<string, SortKey<FlightAdminRow>> FlightSortKeys = new Dictionary<string, SortKey<FlightAdminRow>>
    {
        ["FlightNo"] = new(r => r.FlightNo), ["Origin"] = new(r => r.Origin), ["Destination"] = new(r => r.Destination),
        ["BasePrice"] = new(r => r.BasePrice, FirstDescending: true), ["Type"] = new(r => r.Type),
        ["Capacity"] = new(r => r.Capacity, FirstDescending: true), ["Status"] = new(r => r.Status),
        // The departure column is text ("9:00 PM"); it sorts by the time of day.
        ["DepartureDisplay"] = new(r => AppData.FlightScheduledTimes.TryGetValue(r.FlightNo, out TimeOnly t) ? t : TimeOnly.MinValue),
    };
    private readonly GridSorter<FlightAdminRow> _flightSorter = new(FlightSortKeys);
    private readonly Label _inventoryLabel = new() { AutoSize = true };
    private readonly GlassButton _toggleActive = new() { Text = "Deactivate", Style = GlassButtonStyle.Secondary };

    // bookings
    private readonly DataGridView _bookings = new() { Dock = DockStyle.Fill, AutoGenerateColumns = true };
    private readonly TextBox _bookingSearch = new() { PlaceholderText = "Search reference, flight or passenger" };
    private readonly ComboBox _bookingStatus = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    // ShowCheckBox pickers start checked, which would silently filter to today's date; start unchecked.
    private readonly DateField _bookingDate = new() { Value = DateTime.Today };   // the flight date shown
    private readonly Label _bookingCount = new() { AutoSize = true };
    private readonly System.Windows.Forms.Timer _searchTimer = new() { Interval = 250 };
    private List<BookingRecord> _allBookings = new();
    private Dictionary<string, TripStatusInfo> _allTrips = new();
    // The list can be by flight date (the default) or "recently booked" (whatever the flight date).
    private readonly ComboBox _bookingView = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private static readonly (string Label, string Key)[] BookingViewChoices =
    {
        ("By flight date", "date"), ("Booked in the last 24 hours", "24h"), ("Booked in the last 7 days", "7d"), ("Booked in the last 30 days", "30d")
    };
    private const int RecentBookingLimit = 1000;   // the newest this many are listed; the count says how many there were in all
    private int _recentTotal;                      // bookings made in the window (the list may hold fewer)
    private string _bookingsLoadKey = "";          // which list the previous load was (view + date)
    private DateTime? _bookingsLoadedAt;           // when that load ran
    private DateTime? _newBaseline;                // bookings made after this, in the same list, are marked "new"
    private DateTime _bookingsNow = DateTime.Now;  // the clock the "12 min ago" wording is worked out from
    // Click a header to sort; the default (and the first click on Booked) is newest first.
    private static readonly IReadOnlyDictionary<string, SortKey<BookingAdminRow>> BookingSortKeys = new Dictionary<string, SortKey<BookingAdminRow>>
    {
        ["Reference"] = new(r => r.Reference),
        ["BookedAt"] = new(r => r.BookedAtValue, FirstDescending: true),
        ["FlightNo"] = new(r => r.FlightNo),
        ["Route"] = new(r => r.Route),
        ["FlightDate"] = new(r => r.FlightDateValue, FirstDescending: true),
        ["Passengers"] = new(r => r.Passengers, FirstDescending: true),
        ["TotalAmount"] = new(r => r.TotalAmount, FirstDescending: true),
        ["Status"] = new(r => r.Status),
        ["PaymentStatus"] = new(r => r.PaymentStatus),
        ["RefundAmount"] = new(r => r.RefundAmount, FirstDescending: true),
    };
    private readonly GridSorter<BookingAdminRow> _bookingSorter = new(BookingSortKeys, "BookedAt", descending: true);

    // reports
    private readonly DataGridView _routeGrid = new() { Dock = DockStyle.Fill, AutoGenerateColumns = true };
    private readonly StatTile _tileTotal = new() { Title = "Total bookings" };
    private readonly StatTile _tileConfirmed = new() { Title = "Confirmed" };
    private readonly StatTile _tileCancelled = new() { Title = "Cancelled", Accent = Theme.Warning };
    private readonly StatTile _tileRevenue = new() { Title = "Revenue (PHP)" };
    private readonly StatTile _tileRefunded = new() { Title = "Refunded (PHP)", Accent = Theme.Warning };
    private readonly StatTile _tileAverage = new() { Title = "Avg booking (PHP)" };
    private readonly BarChart _routeChart = new() { Title = "Bookings by route" };
    private readonly BarChart _classChart = new() { Title = "Bookings by cabin", BarColor = Theme.Info };
    private readonly Label _lblRefreshed =new() { AutoSize = true, ForeColor = Theme.TextSecondary };
    private ReportStats? _lastReport;

    // staff
    private readonly DataGridView _staff = new() { Dock = DockStyle.Fill, AutoGenerateColumns = true };
    private static readonly IReadOnlyDictionary<string, SortKey<StaffRowView>> StaffSortKeys = new Dictionary<string, SortKey<StaffRowView>>
    {
        ["Username"] = new(r => r.Username), ["Name"] = new(r => r.Name), ["Role"] = new(r => r.Role), ["Status"] = new(r => r.Status),
        ["LastSignIn"] = new(r => r.Account.LastLoginAtUtc, FirstDescending: true),   // the text is "12 min ago": sort by the moment
        ["Locked"] = new(r => r.Account.LockedUntilUtc, FirstDescending: true),
    };
    private readonly GridSorter<StaffRowView> _staffSorter = new(StaffSortKeys);
    private readonly GlassButton _toggleStaff = new() { Text = "Deactivate", Style = GlassButtonStyle.Secondary };

    // audit
    private readonly DataGridView _refunds = new() { Dock = DockStyle.Fill, AutoGenerateColumns = true };
    private readonly TextBox _refundSearch = new() { PlaceholderText = "Reference, flight or refund ref" };
    private readonly CheckBox _refundShowAll = new() { Text = "Include no-refund", AutoSize = true };
    private readonly Label _refundCount = new() { AutoSize = true };
    private readonly System.Windows.Forms.Timer _refundTimer = new() { Interval = 300 };
    private RefundList _refundList = new(Array.Empty<RefundListRow>(), 0, 0m);
    private readonly ComboBox _refundView = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _refundPeriod = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _refundSort = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _refundHandled = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private bool _loadingViewSettings;
    private readonly DataGridView _requests = new() { Dock = DockStyle.Fill, AutoGenerateColumns = true, Visible = false };
    private readonly CheckBox _requestShowClosed = new() { Text = "Show closed", AutoSize = true };
    private readonly List<Control> _refundOnlyControls = new();
    private readonly List<Control> _requestOnlyControls = new();
    private readonly System.Windows.Forms.Timer _requestPoll = new() { Interval = 60_000 };
    private List<RefundRequestRow> _requestRows = new();
    private GlassButton? _refundsNav;
    private readonly DataGridView _audit = new() { Dock = DockStyle.Fill, AutoGenerateColumns = true };
    private readonly TextBox _auditSearch = new() { PlaceholderText = "Search who, what or which item" };
    private readonly System.Windows.Forms.Timer _auditTimer = new() { Interval = 300 };
    private List<AuditEntry> _auditEntries = new();
    private static readonly IReadOnlyDictionary<string, SortKey<AuditEntry>> AuditSortKeys = new Dictionary<string, SortKey<AuditEntry>>
    {
        ["When"] = new(e => e.AtUtc, FirstDescending: true), ["Who"] = new(e => e.Actor), ["Role"] = new(e => e.Role),
        ["Action"] = new(e => e.Action), ["Item"] = new(e => e.Target), ["Detail"] = new(e => e.Detail),
    };
    private readonly GridSorter<AuditEntry> _auditSorter = new(AuditSortKeys);   // no sort chosen: newest first, as the log is loaded

    private Label _frameTitle = null!;   // coloured after Theme.Apply, which resets label colours
    private StaffSession Session => _service.Session;

    public AdminForm(AdminService service)
    {
        _service = service;
        Text = "SkyWings Staff";
        StartPosition = FormStartPosition.CenterParent;
        SizeForScreen(1120, 640, 900, 520);
        Padding = new Padding(D(24), D(12), D(24), D(16));

        Panel? first = null;
        // The section buttons get a row of their own under the title and account buttons, so all eight fit at the minimum window size.
        var nav = new FlowLayoutPanel { Dock = DockStyle.Top, Height = D(54), WrapContents = false, Padding = new Padding(0, D(6), 0, D(4)) };
        void AddSection(string title, Permission needed, Func<Panel> build, Action? onShow = null)
        {
            if (!Session.Has(needed)) return;
            var button = new GlassButton { Text = title, Style = GlassButtonStyle.Ghost };
            button.Width = Math.Max(D(76), TextRenderer.MeasureText(title, button.Font).Width + D(28));   // fit the caption, not the 104px minimum
            button.Margin = new Padding(0, 0, D(4), 0);
            Panel page = build();
            _sections[button] = page;
            if (title == "Refunds") _refundsNav = button;
            _host.Controls.Add(page);
            button.Click += (_, _) => { ShowSection(button); onShow?.Invoke(); };
            nav.Controls.Add(button);
            first ??= page;
        }
        AddSection("Flights", Permission.ManageFlights, BuildFlightsPage);
        AddSection("Bookings", Permission.ViewBookings, BuildBookingsPage, LoadBookingsFromStore);
        AddSection("Refunds", Permission.ViewBookings, BuildRefundsPage, ReloadRefundsView);
        AddSection("Waitlist", Permission.ViewBookings, BuildWaitlistPage, LoadWaitlist);
        AddSection("Reports", Permission.ViewReports, BuildReportsPage, LoadReport);
        AddSection("Staff", Permission.ManageStaff, BuildStaffPage, LoadStaff);
        AddSection("Audit", Permission.ViewAudit, BuildAuditPage, LoadAudit);
        DatabaseBrowserPage? browser = null;
        AddSection("Database", Permission.BrowseData, () => browser = new DatabaseBrowserPage(_service), () => browser?.OnShow());

        Controls.Add(_host);
        Controls.Add(nav);               // docked below the title row (the last-added control docks first)
        Controls.Add(BuildHeader());

        Theme.Apply(this);
        _frameTitle.ForeColor = Theme.Brand;
        foreach (var label in new[] { _inventoryLabel, _bookingCount, _refundCount })
        {
            label.Font = Theme.Small;
            label.ForeColor = Theme.TextSecondary;
        }

        _searchTimer.Tick += (_, _) => { _searchTimer.Stop(); ApplyBookingFilter(); };
        _auditTimer.Tick += (_, _) => { _auditTimer.Stop(); LoadAudit(); };
        _refundTimer.Tick += (_, _) => { _refundTimer.Stop(); LoadRefunds(); };
        _requestPoll.Tick += (_, _) => RefreshRequestBadge();
        if (SkyWings.Email.EmailServices.RefundMail is { } reader) reader.RequestsLogged += OnRefundEmailsLogged;
        if (_refundsNav is not null) { _requestPoll.Start(); RefreshRequestBadge(); }
        FormClosed += (_, _) =>
        {
            _searchTimer.Dispose();
            _auditTimer.Dispose();
            _refundTimer.Dispose();
            _requestPoll.Dispose();
            if (SkyWings.Email.EmailServices.RefundMail is { } reader) reader.RequestsLogged -= OnRefundEmailsLogged;
        };

        // Load whatever the first visible section needs.
        var firstButton = _sections.First(kv => kv.Value == first).Key;
        ShowSection(firstButton);
        if (Session.Has(Permission.ManageFlights)) LoadFlights();
        if (Session.Has(Permission.ViewBookings)) LoadBookingsFromStore();
    }

    private int D(int px) => Gfx.S(this, px);

    /// <summary>True when the person pressed Sign out here, so the dashboard should sign them out too.</summary>
    public bool SignOutRequested { get; private set; }

    // ------------------------------------------------------------------ frame

    private Control BuildHeader()
    {
        var title = _frameTitle = new Label { Text = "SkyWings Staff", Font = Theme.Header, ForeColor = Theme.Brand, AutoSize = false, Height = D(30), Dock = DockStyle.Top };
        var who = new Label
        {
            Text = $"{Session.DisplayName} · {RolePermissions.Label(Session.Role)}", Font = Theme.Small, ForeColor = Theme.TextSecondary,
            AutoSize = false, Height = D(22), Dock = DockStyle.Top
        };
        var identity = new Panel { Dock = DockStyle.Left, Width = D(190) };
        identity.Controls.Add(who);
        identity.Controls.Add(title);

        var account = new FlowLayoutPanel
        {
            Dock = DockStyle.Right, Width = D(250), FlowDirection = FlowDirection.RightToLeft, WrapContents = false,
            Padding = new Padding(0, D(6), 0, 0)
        };
        var signOut = new GlassButton { Text = "Sign out", Style = GlassButtonStyle.Secondary, Height = D(38) };
        signOut.Click += (_, _) => RequestSignOut();
        var password = new GlassButton { Text = "Password", Style = GlassButtonStyle.Ghost, Height = D(38) };
        password.Click += (_, _) =>
        {
            using var form = new ChangePasswordForm(_service, forced: false);
            form.ShowDialog(this);
        };
        account.Controls.Add(signOut);
        account.Controls.Add(password);

        var bar = new Panel { Dock = DockStyle.Top, Height = D(56) };
        bar.Controls.Add(account);
        bar.Controls.Add(identity);
        return bar;
    }

    private void ShowSection(GlassButton active)
    {
        foreach (var (button, page) in _sections)
        {
            page.Visible = button == active;
            button.Style = button == active ? GlassButtonStyle.Primary : GlassButtonStyle.Ghost;
        }
    }

    private void RequestSignOut()
    {
        SignOutRequested = true;
        DialogResult = DialogResult.Abort;
        Close();
    }

    private GlassButton ToolButton(string text, EventHandler click, GlassButtonStyle style = GlassButtonStyle.Secondary)
    {
        var button = new GlassButton { Text = text, Style = style, Height = D(38) };
        button.Click += click;
        return button;
    }

    private Panel Section(Control toolbar, Control content)
    {
        var page = new Panel { Dock = DockStyle.Fill, Visible = false };
        var card = new GlassCard { Dock = DockStyle.Fill, ContentPadding = 12 };
        card.Controls.Add(content);
        card.Controls.Add(toolbar);
        page.Controls.Add(card);
        return page;
    }

    private FlowLayoutPanel Toolbar() => new() { Dock = DockStyle.Top, Height = D(54), WrapContents = false, Padding = new Padding(0, D(6), 0, D(6)) };

    /// <summary>A toolbar row for the filter boxes: a little taller than the button rows, because a rounded box is 44px high.</summary>
    private FlowLayoutPanel FilterRow() => new() { Dock = DockStyle.Top, Height = D(58), WrapContents = false, Padding = new Padding(0, D(2), 0, D(2)) };

    /// <summary>Puts a filter box (text box, drop-down or date) in the wizard's rounded field. It keeps the input's width and right margin; use the returned box where the input would go.</summary>
    private RoundedInput Rounded(Control input)
    {
        int width = input.Width, gap = input.Margin.Right;   // read first: wrapping resizes the input
        var host = new RoundedInput(input) { Width = width, Margin = new Padding(0, 0, gap, 0) };
        input.Margin = Padding.Empty;
        return host;
    }

    /// <summary>Runs a staff action and turns the expected failures into a message instead of a crash.</summary>
    private bool Try(string failureTitle, Action action)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception ex) when (ReportFailure(ex, failureTitle))
        {
            return false;
        }
    }

    /// <summary>Like <see cref="Try"/>, but runs the slow action on a worker thread so the window stays responsive.</summary>
    private async Task<bool> TryAsync(string failureTitle, Action action)
    {
        UseWaitCursor = true;
        Enabled = false;
        try
        {
            await Task.Run(action);
            return true;
        }
        catch (Exception ex) when (ReportFailure(ex, failureTitle))
        {
            return false;
        }
        finally
        {
            Enabled = true;
            UseWaitCursor = false;
        }
    }

    /// <summary>Shows a message for the expected failures and returns true; returns false for anything unexpected so it still surfaces.</summary>
    private bool ReportFailure(Exception ex, string failureTitle)
    {
        switch (ex)
        {
            case UnauthorizedAccessException:
                MessageBox.Show(this, ex.Message, "Not allowed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return true;
            case ArgumentException or InvalidOperationException:
                MessageBox.Show(this, ex.Message, failureTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return true;
            case IOException or System.Security.Cryptography.CryptographicException:
                AppLogger.Error("Admin", ex);
                MessageBox.Show(this, $"{failureTitle}.\n\n{ex.Message}", "SkyWings Staff", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return true;
            default:
                return false;
        }
    }

    // ------------------------------------------------------------------ flights

    private Panel BuildFlightsPage()
    {
        var toolbar = Toolbar();
        _toggleActive.Click += (_, _) => ToggleSelectedFlight();
        toolbar.Controls.Add(ToolButton("Add flight", (_, _) => AddFlight(), GlassButtonStyle.Primary));
        toolbar.Controls.Add(ToolButton("Edit", (_, _) => EditSelectedFlight()));
        toolbar.Controls.Add(_toggleActive);
        toolbar.Controls.Add(ToolButton("Refresh", (_, _) => LoadFlights(), GlassButtonStyle.Ghost));
        if (Session.Has(Permission.BackupRestore))
        {
            toolbar.Controls.Add(ToolButton("Backup", async (_, _) => await BackupDatabase(), GlassButtonStyle.Ghost));
            toolbar.Controls.Add(ToolButton("Restore", async (_, _) => await RestoreDatabase(), GlassButtonStyle.Ghost));
        }
        // The inventory line sits under the table: in the toolbar it was pushed out of the window at the minimum size.
        _inventoryLabel.AutoSize = false;
        _inventoryLabel.Dock = DockStyle.Bottom;
        _inventoryLabel.Height = D(30);
        _inventoryLabel.TextAlign = ContentAlignment.MiddleLeft;
        _inventoryLabel.Padding = new Padding(D(4), 0, 0, 0);

        GridStyle.Apply(_flights);
        _flights.SelectionChanged += (_, _) => UpdateFlightSelection();
        WireSorter(_flightSorter, _flights, "view_flights_sort", LoadFlights);
        var tableArea = new Panel { Dock = DockStyle.Fill };
        tableArea.Controls.Add(_flights);
        tableArea.Controls.Add(_inventoryLabel);   // docked to the bottom of the table area
        return Section(toolbar, tableArea);
    }

    private void LoadFlights() => Try("Unable to load flights", () => _flights.DataSource = _flightSorter.Sort(_service.LoadFlights()).ToList());

    /// <summary>
    /// Click-a-header sorting for one grid: the sort is remembered between sessions (a view setting) and <paramref name="reload"/>
    /// shows the rows again in the new order.
    /// </summary>
    private void WireSorter<T>(GridSorter<T> sorter, DataGridView grid, string settingKey, Action reload)
    {
        sorter.LoadSetting(_service.GetViewSetting(settingKey, ""));
        sorter.Attach(grid);
        sorter.Changed += (_, _) =>
        {
            Try("Unable to save the view setting", () => _service.SetViewSetting(settingKey, sorter.ToSetting()));
            reload();
        };
    }

    private FlightAdminRow? SelectedFlight() => _flights.CurrentRow?.DataBoundItem as FlightAdminRow;

    private void UpdateFlightSelection()
    {
        var flight = SelectedFlight();
        if (flight is null) { _inventoryLabel.Text = "Select a flight to view its inventory"; return; }
        _toggleActive.Text = flight.Status == "Inactive" ? "Activate" : "Deactivate";
        if (!Try("Unable to load seat inventory", () =>
            {
                var inventory = _service.LoadFlightInventory(flight.FlightNo, DateTime.Today);
                _inventoryLabel.Text = $"{flight.FlightNo} today: {inventory.ConfirmedBookings} booked · {inventory.HeldSeats} held · {inventory.AvailableSeats} available";
            }))
            _inventoryLabel.Text = "Inventory unavailable";
    }

    private void AddFlight()
    {
        if (FlightEditorForm.ShowEditor(this, _service, null)) LoadFlights();
    }

    private void EditSelectedFlight()
    {
        if (SelectedFlight() is not { } flight) { ShowInfo("Select a flight first."); return; }
        if (FlightEditorForm.ShowEditor(this, _service, flight)) LoadFlights();
    }

    private void ToggleSelectedFlight()
    {
        if (SelectedFlight() is not { } flight) { ShowInfo("Select a flight first."); return; }
        bool activate = flight.Status == "Inactive";
        if (!activate)
        {
            int confirmed = 0;
            if (!Try("Unable to check bookings", () => confirmed = _service.CountActiveBookingsForFlight(flight.FlightNo))) return;
            string extra = confirmed > 0 ? $"\n\n{confirmed} confirmed booking(s) stay valid." : "";
            if (MessageBox.Show(this, $"Deactivate {flight.FlightNo}? New bookings will no longer be allowed.{extra}",
                "Confirm deactivation", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        }
        if (Try(activate ? "Unable to activate flight" : "Unable to deactivate flight", () => _service.SetFlightActive(flight.FlightNo, activate)))
            LoadFlights();
    }

    private async Task BackupDatabase()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "SkyWings encrypted backup (*.skybak)|*.skybak",
            FileName = $"skywings-backup-{DateTime.Now:yyyyMMdd-HHmmss}.skybak"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        using var password = new BackupPasswordForm();
        if (password.ShowDialog(this) != DialogResult.OK) return;
        string file = dialog.FileName, backupPassword = password.BackupPassword;
        if (await TryAsync("Unable to create the backup", () => _service.Backup(file, backupPassword)))
            MessageBox.Show(this, "Encrypted backup created. Keep the password safe: it can't be recovered.", "Backup complete",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async Task RestoreDatabase()
    {
        using var dialog = new OpenFileDialog { Filter = "SkyWings encrypted backup (*.skybak)|*.skybak" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        using var credentials = new RestoreCredentialsForm(Path.GetFileName(dialog.FileName));
        if (credentials.ShowDialog(this) != DialogResult.OK) return;
        if (MessageBox.Show(this, "Replace the current bookings and flights with this backup?\n\nA safety copy of the current data is saved first.",
            "Confirm restore", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

        string file = dialog.FileName, backupPassword = credentials.BackupPassword, staffPassword = credentials.StaffPassword;
        if (!await TryAsync("Unable to restore the backup", () => _service.Restore(file, backupPassword, staffPassword))) return;
        LoadFlights();
        LoadBookingsFromStore();
        LoadReport();
        MessageBox.Show(this, "Backup restored. The previous data was saved as a safety copy.", "Restore complete",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // ------------------------------------------------------------------ bookings

    private Panel BuildBookingsPage()
    {
        // Two toolbar rows, like the Refunds tab: the actions on top, "which bookings to show" below.
        var toolbar = new Panel { Dock = DockStyle.Top, Height = D(112) };
        FlowLayoutPanel actions = Toolbar();
        FlowLayoutPanel view = FilterRow();
        toolbar.Controls.Add(view);       // docked first = lower row
        toolbar.Controls.Add(actions);

        _bookingSearch.Width = D(200);
        _bookingSearch.Margin = new Padding(0, D(5), D(8), 0);
        _bookingStatus.Items.AddRange(TripStatus.Filters.Cast<object>().ToArray());
        _bookingStatus.SelectedIndex = 0;
        _bookingStatus.Width = D(160);
        _bookingStatus.Margin = new Padding(0, D(5), D(8), 0);
        _bookingDate.Width = D(170);
        _bookingDate.Margin = new Padding(0, D(5), D(8), 0);
        FillChoices(_bookingView, BookingViewChoices, D(230), new Padding(0, D(5), D(8), 0));
        _bookingCount.Margin = new Padding(D(4), D(12), 0, 0);

        // What the screen remembers from last time: which list, and how it was sorted.
        _loadingViewSettings = true;
        SelectByKey(_bookingView, BookingViewChoices, _service.GetViewSetting("view_booking_view", "date"));
        _bookingSorter.LoadSetting(_service.GetViewSetting("view_booking_sort", ""));
        _loadingViewSettings = false;
        _bookingDate.Enabled = KeyOf(_bookingView, BookingViewChoices) == "date";

        _bookingSearch.TextChanged += (_, _) => { _searchTimer.Stop(); _searchTimer.Start(); };
        _bookingStatus.SelectedIndexChanged += (_, _) => ApplyBookingFilter();
        _bookingDate.ValueChanged += (_, _) => LoadBookingsFromStore();
        _bookingView.SelectedIndexChanged += (_, _) =>
        {
            string key = KeyOf(_bookingView, BookingViewChoices);
            _bookingDate.Enabled = key == "date";   // a flight date only means something to the by-date list
            if (_loadingViewSettings) return;
            Try("Unable to save the view setting", () => _service.SetViewSetting("view_booking_view", key));
            LoadBookingsFromStore();
        };
        _bookingSorter.Attach(_bookings);
        _bookingSorter.Changed += (_, _) =>
        {
            Try("Unable to save the view setting", () => _service.SetViewSetting("view_booking_sort", _bookingSorter.ToSetting()));
            ApplyBookingFilter();
        };

        view.Controls.Add(Rounded(_bookingView));
        view.Controls.Add(Rounded(_bookingDate));
        view.Controls.Add(Rounded(_bookingStatus));
        view.Controls.Add(Rounded(_bookingSearch));
        actions.Controls.Add(ToolButton("Details", (_, _) => ShowBookingDetails()));
        if (Session.Has(Permission.CancelBookings))
            actions.Controls.Add(ToolButton("Cancel booking", (_, _) => CancelSelectedBooking(), GlassButtonStyle.Danger));
        actions.Controls.Add(ToolButton("Refresh", (_, _) => LoadBookingsFromStore(), GlassButtonStyle.Ghost));
        actions.Controls.Add(ToolButton("Export CSV", (_, _) => ExportBookings(), GlassButtonStyle.Secondary));
        actions.Controls.Add(_bookingCount);

        GridStyle.Apply(_bookings);
        // Bookings made since the list was last loaded stand out (amber), so a fresh Refresh shows what came in.
        _bookings.CellFormatting += (_, e) =>
        {
            if (e.RowIndex < 0 || e.CellStyle is null) return;
            DataGridViewRow row = _bookings.Rows[e.RowIndex];
            if (row.DataBoundItem is BookingAdminRow { IsNew: true } && !row.Selected) e.CellStyle.BackColor = Theme.AmberSoft;
        };
        // Nine columns: use the compact font and tighter cell padding so nothing is truncated.
        _bookings.DefaultCellStyle.Font = Theme.Small;
        _bookings.DefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
        _bookings.ColumnHeadersDefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
        _bookings.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) ShowBookingDetails(); };
        _bookings.DataBindingComplete += (_, _) => SizeBookingColumns();
        // The list, with a legend of what each trip status means underneath it.
        var content = new Panel { Dock = DockStyle.Fill };
        _bookings.Dock = DockStyle.Fill;
        content.Controls.Add(_bookings);
        var legend = new TripLegend { Dock = DockStyle.Bottom };
        content.Controls.Add(legend);
        // On a small window the legend would crowd the list out (and, at the minimum size, cover the filters): show it only when the list keeps room.
        content.Resize += (_, _) => legend.Visible = content.Height >= legend.GetPreferredSize(new Size(content.Width, 0)).Height + D(180);
        return Section(toolbar, content);
    }

    /// <summary>Column widths matched to what each column holds, so routes and dates are not cut off.</summary>
    private void SizeBookingColumns()
    {
        var weights = new Dictionary<string, (float Weight, string? Header)>
        {
            ["Reference"] = (1.0f, null), ["BookedAt"] = (1.75f, null), ["FlightNo"] = (0.9f, null),   // BookedAt: "Yesterday, 4:45 PM"
            ["Route"] = (2.8f, null), ["FlightDate"] = (1.3f, null), ["Passengers"] = (0.6f, "Pax"),
            ["TotalAmount"] = (1.0f, null), ["Status"] = (1.6f, null),   // room for the widest pill, "Check-in open"
            ["PaymentStatus"] = (1.4f, null),   // and for "PAID · not used"
            ["RefundAmount"] = (0.9f, null)
        };
        foreach (DataGridViewColumn column in _bookings.Columns)
        {
            if (!weights.TryGetValue(column.DataPropertyName, out var entry)) continue;
            column.FillWeight = entry.Weight * 100;
            if (entry.Header is not null) column.HeaderText = entry.Header;
        }
    }

    /// <summary>The start of a "booked in the last ..." window (24h, 7d or 30d).</summary>
    internal static DateTime RecentSince(string view, DateTime now) => view switch
    {
        "24h" => now.AddHours(-24),
        "30d" => now.AddDays(-30),
        _ => now.AddDays(-7)
    };

    /// <summary>
    /// Reads the list for the chosen view and where each trip is right now: the chosen flight date's bookings (a busy day is thousands;
    /// all dates together would be far too many), or the newest bookings made in the chosen window whatever their flight date.
    /// Filtering and sorting then work on this copy. Bookings made since the previous load of the same list are marked new.
    /// </summary>
    private void LoadBookingsFromStore()
    {
        DateTime now = DateTime.Now;
        DateTime date = _bookingDate.Value.Date;
        string view = KeyOf(_bookingView, BookingViewChoices);
        string loadKey = view == "date" ? $"date:{date:yyyyMMdd}" : view;
        DateTime? baseline = loadKey == _bookingsLoadKey ? _bookingsLoadedAt : null;   // a different list has nothing to compare with

        if (!Try("Unable to load bookings", () =>
            {
                if (view == "date")
                {
                    _allBookings = _service.LoadBookingsForDate(date);
                    _allTrips = _service.LoadTripStatusesForDate(_allBookings, date, now);
                    _recentTotal = _allBookings.Count;
                }
                else
                {
                    (_allBookings, _recentTotal) = _service.LoadRecentBookings(RecentSince(view, now), RecentBookingLimit);
                    _allTrips = _service.LoadTripStatuses(_allBookings, now);
                }
            }))
            return;
        _bookingsLoadKey = loadKey;
        _bookingsLoadedAt = now;
        _newBaseline = baseline;
        _bookingsNow = now;
        ApplyBookingFilter();
    }

    private TripStatusInfo TripOf(BookingRecord b)
        => _allTrips.TryGetValue(b.Reference, out TripStatusInfo? trip) ? trip : TripStatus.Of(b, Array.Empty<CheckInRecord>(), DateTime.Now);

    private BookingAdminRow ToRow(BookingRecord b, TripStatusInfo trip)
        => new(b.Reference, RecencyFormat.Relative(b.BookedAtValue, _bookingsNow), b.FlightNo,
            $"{b.Origin} → {b.Destination}", b.FlightDate, b.Passengers.Count, b.TotalAmount, trip.Label, BookingSummary.PaymentLabel(b.PaymentStatus, trip.Phase), b.RefundAmount,
            b.BookedAtValue, b.FlightDateValue, RecencyFormat.IsNew(b.BookedAtValue, _newBaseline));

    /// <summary>What the count next to the buttons says: how many are shown, of how many, for which list, and how many are new.</summary>
    internal static string BookingCountText(string view, int shown, int loaded, int total, int fresh, string day)
    {
        static string N(int n) => n.ToString("N0", CultureInfo.InvariantCulture);
        static string Noun(int n) => n == 1 ? "booking" : "bookings";
        string where = view switch
        {
            "24h" => "from the last 24 hours",
            "7d" => "from the last 7 days",
            "30d" => "from the last 30 days",
            _ => $"on {day}"
        };
        bool cutShort = view != "date" && loaded < total;   // the newest bookings only; there were more in the window
        string text = shown == loaded
            ? cutShort ? $"newest {N(loaded)} of {N(total)} bookings {where}" : $"{N(shown)} {Noun(shown)} {where}"
            : cutShort ? $"{N(shown)} of {N(loaded)} (newest of {N(total)}) bookings {where}" : $"{N(shown)} of {N(loaded)} bookings {where}";
        return fresh > 0 ? $"{text} · {N(fresh)} new" : text;
    }

    private void ApplyBookingFilter()
    {
        string search = _bookingSearch.Text.Trim();

        // A full booking reference finds that booking on any date.
        if (AdminService.AsReference(search) is { } reference)
        {
            BookingRecord? found = null;
            TripStatusInfo? trip = null;
            if (!Try("Unable to look up the booking", () =>
                {
                    found = _service.FindBooking(reference);
                    if (found is not null) trip = _service.LoadTripStatus(found, DateTime.Now);
                })) return;
            _bookings.DataSource = found is null ? new List<BookingAdminRow>() : new List<BookingAdminRow> { ToRow(found, trip!) };
            _bookingCount.Text = found is null ? $"No booking {reference}" : "1 booking found";
            return;
        }

        string status = _bookingStatus.SelectedItem?.ToString() ?? "All";
        var matching = _allBookings
            .Where(b => TripStatus.Matches(TripOf(b).Phase, status))
            .Where(b => search.Length == 0 ||
                        b.Reference.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                        b.FlightNo.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                        b.Passengers.Any(p => p.Name.Contains(search, StringComparison.OrdinalIgnoreCase)))
            .Select(b => ToRow(b, TripOf(b)));
        List<BookingAdminRow> rows = _bookingSorter.Sort(matching).ToList();   // newest first unless a header was clicked
        _bookings.DataSource = rows;
        _bookingCount.Text = BookingCountText(KeyOf(_bookingView, BookingViewChoices), rows.Count, _allBookings.Count, _recentTotal,
            rows.Count(r => r.IsNew), _bookingDate.Value.ToString("MMM d", CultureInfo.InvariantCulture));
    }

    /// <summary>Saves the bookings currently shown (after the date, status and search filters) as a CSV file.</summary>
    private void ExportBookings()
    {
        if (_bookings.DataSource is not List<BookingAdminRow> shown || shown.Count == 0) { ShowInfo("There are no bookings in the list to export."); return; }
        string fileDay = KeyOf(_bookingView, BookingViewChoices) == "date" ? $"{_bookingDate.Value:yyyyMMdd}" : $"booked-{DateTime.Today:yyyyMMdd}";
        using var dialog = new SaveFileDialog { Filter = "CSV files (*.csv)|*.csv", FileName = $"skywings-bookings-{fileDay}.csv" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var lines = CsvExport.Build(
            new[] { "Reference", "Booked", "Flight", "Route", "Flight date", "Passengers", "Total", "Status", "Payment", "Refund" },
            // The file gets the exact time, not the "12 min ago" wording, and the rows in the order the list shows them.
            shown.Select(r => new[] { r.Reference, r.BookedAtValue.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), r.FlightNo, r.Route, r.FlightDate, r.Passengers.ToString(CultureInfo.InvariantCulture),
                CsvExport.Money(r.TotalAmount), r.Status, r.PaymentStatus, CsvExport.Money(r.RefundAmount) }));
        if (Try("Unable to export the bookings", () => File.WriteAllLines(dialog.FileName, lines)))
        {
            AuditExport("EXPORT_BOOKINGS", $"{shown.Count} bookings");
            ShowInfo($"{shown.Count:N0} bookings exported.");
        }
    }

    private void AuditExport(string action, string detail)
        => Try("Unable to record the export", () => _service.RecordExport(action, detail));

    private void ExportWaitlist()
    {
        if (_waitlistEntries.Count == 0) { ShowInfo("The waitlist is empty, so there is nothing to export."); return; }
        using var dialog = new SaveFileDialog { Filter = "CSV files (*.csv)|*.csv", FileName = $"skywings-waitlist-{DateTime.Today:yyyyMMdd}.csv" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var inv = CultureInfo.InvariantCulture;
        var lines = CsvExport.Build(
            new[] { "Flight", "Flight date", "Cabin", "Traveller", "Seats", "Joined", "Status", "Position", "Offer expires", "Code" },
            _waitlistEntries.Select(e => new[] { e.FlightNo, e.FlightDate.ToString("yyyy-MM-dd", inv), e.ClassName, e.PassengerName,
                e.SeatsWanted.ToString(inv), e.AddedAt.ToString("yyyy-MM-dd HH:mm", inv), e.Status, e.Position.ToString(inv),
                e.OfferExpiresAt?.ToString("yyyy-MM-dd HH:mm", inv), e.BookingReference }));
        if (Try("Unable to export the waitlist", () => File.WriteAllLines(dialog.FileName, lines)))
        {
            AuditExport("EXPORT_WAITLIST", $"{_waitlistEntries.Count} entries");
            ShowInfo($"{_waitlistEntries.Count:N0} waitlist entries exported.");
        }
    }

    private BookingAdminRow? SelectedBooking() => _bookings.CurrentRow?.DataBoundItem as BookingAdminRow;

    private void ShowBookingDetails()
    {
        if (SelectedBooking() is not { } row) { ShowInfo("Select a booking first."); return; }
        BookingRecord? booking = null;
        if (!Try("Unable to load the booking", () => booking = _service.FindBooking(row.Reference))) return;
        if (booking is null) { ShowInfo("The selected booking is no longer available."); return; }
        TripStatusInfo? trip = null;
        if (!Try("Unable to load the booking", () => trip = _service.LoadTripStatus(booking, DateTime.Now))) return;
        using var details = new BookingDetailForm(booking, trip!);
        details.ShowDialog(this);
    }

    private void CancelSelectedBooking()
    {
        if (SelectedBooking() is not { } row) { ShowInfo("Select a booking first."); return; }
        BookingRecord? booking = null;
        if (!Try("Unable to load the booking", () => booking = _service.FindBooking(row.Reference))) return;
        if (booking is null) { ShowInfo("The selected booking is no longer available."); return; }
        // The row shows the trip label ("Canceled"), not the stored status, so the check reads the booking itself.
        if (_service.CancelRefusal(booking) is { } refusal) { ShowInfo(refusal); return; }
        decimal refund = AppData.ComputeRefundAmount(booking.TotalAmount, booking.FlightDateValue);
        if (MessageBox.Show(this, $"Cancel {booking.Reference}?\nEstimated refund: PHP {refund:N2}",
            "Confirm cancellation", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

        bool done = false;
        if (!Try("Unable to cancel booking", () => done = _service.CancelBooking(booking.Reference, refund))) return;
        if (!done) ShowInfo("This booking was already canceled or changed elsewhere. The list has been refreshed.");
        LoadBookingsFromStore();
    }

    // ------------------------------------------------------------------ refunds

    private sealed record RefundRow(string Reference, string Refunded, string FlightNo, string Route, string FlightDate,
        decimal TotalAmount, decimal RefundAmount, string RefundRef, string Method, string HandledBy,
        [property: System.ComponentModel.Browsable(false)] DateTime? RefundedAt,
        [property: System.ComponentModel.Browsable(false)] DateTime FlightDateValue);

    private static readonly IReadOnlyDictionary<string, SortKey<RefundRow>> RefundSortKeys = new Dictionary<string, SortKey<RefundRow>>
    {
        ["Reference"] = new(r => r.Reference), ["Refunded"] = new(r => r.RefundedAt, FirstDescending: true), ["FlightNo"] = new(r => r.FlightNo),
        ["Route"] = new(r => r.Route), ["FlightDate"] = new(r => r.FlightDateValue, FirstDescending: true),
        ["TotalAmount"] = new(r => r.TotalAmount, FirstDescending: true), ["RefundAmount"] = new(r => r.RefundAmount, FirstDescending: true),
        ["RefundRef"] = new(r => r.RefundRef), ["Method"] = new(r => r.Method), ["HandledBy"] = new(r => r.HandledBy),
    };
    // Only for this session: the order dropdown above the list is what is remembered, and choosing one clears a header sort.
    private readonly GridSorter<RefundRow> _refundSorter = new(RefundSortKeys);

    // The choices of the "view" row: label, what it does. Remembered between sessions (app_settings view_* keys).
    private static readonly (string Label, string Key)[] PeriodChoices =
        { ("Any time", "all"), ("Today", "today"), ("Last 7 days", "7d"), ("Last 30 days", "30d"), ("This month", "month") };
    private static readonly (string Label, string Key)[] SortChoices =
        { ("Newest first", RefundSort.Newest), ("Oldest first", RefundSort.Oldest), ("Largest refund", RefundSort.Largest), ("Smallest refund", RefundSort.Smallest) };
    private static readonly (string Label, string Key)[] HandledChoices =
        { ("Handled by anyone", RefundHandler.Anyone), ("Handled by staff", RefundHandler.Staff), ("Customer desk", RefundHandler.Customer), ("System", RefundHandler.System) };

    /// <summary>The start of the chosen period (local time), or null for any time.</summary>
    internal static DateTime? PeriodStart(string key, DateTime now) => key switch
    {
        "today" => now.Date,
        "7d" => now.AddDays(-7),
        "30d" => now.AddDays(-30),
        "month" => new DateTime(now.Year, now.Month, 1),
        _ => null
    };

    private static void FillChoices(ComboBox box, (string Label, string Key)[] choices, int width, Padding margin)
    {
        box.Items.AddRange(choices.Select(c => (object)c.Label).ToArray());
        box.Width = width;
        box.Margin = margin;
    }

    private static string KeyOf(ComboBox box, (string Label, string Key)[] choices)
        => box.SelectedIndex >= 0 && box.SelectedIndex < choices.Length ? choices[box.SelectedIndex].Key : choices[0].Key;

    private void SelectByKey(ComboBox box, (string Label, string Key)[] choices, string key)
    {
        int index = Array.FindIndex(choices, c => c.Key == key);
        box.SelectedIndex = index < 0 ? 0 : index;
    }

    private Panel BuildRefundsPage()
    {
        // Two toolbar rows: actions on top, "how to see the list" below.
        var toolbar = new Panel { Dock = DockStyle.Top, Height = D(116) };
        FlowLayoutPanel actions = FilterRow();
        FlowLayoutPanel view = FilterRow();
        toolbar.Controls.Add(view);       // docked first = lower row
        toolbar.Controls.Add(actions);

        _refundView.Items.AddRange(new object[] { "Refunded bookings", "Refund requests" });
        _refundView.SelectedIndex = 0;
        _refundView.Width = D(210);
        _refundView.Margin = new Padding(0, D(5), D(8), 0);
        _refundView.SelectedIndexChanged += (_, _) => { ApplyRefundView(); ReloadRefundsView(); };

        var gap = new Padding(0, D(5), D(8), 0);
        FillChoices(_refundPeriod, PeriodChoices, D(140), gap);
        FillChoices(_refundSort, SortChoices, D(160), gap);
        FillChoices(_refundHandled, HandledChoices, D(180), gap);
        _loadingViewSettings = true;
        SelectByKey(_refundPeriod, PeriodChoices, _service.GetViewSetting("view_refund_period", "all"));
        SelectByKey(_refundSort, SortChoices, _service.GetViewSetting("view_refund_sort", RefundSort.Newest));
        SelectByKey(_refundHandled, HandledChoices, _service.GetViewSetting("view_refund_handled", RefundHandler.Anyone));
        _loadingViewSettings = false;
        _refundPeriod.SelectedIndexChanged += (_, _) => ViewSettingChanged("view_refund_period", KeyOf(_refundPeriod, PeriodChoices));
        _refundSort.SelectedIndexChanged += (_, _) => ViewSettingChanged("view_refund_sort", KeyOf(_refundSort, SortChoices));
        _refundHandled.SelectedIndexChanged += (_, _) => ViewSettingChanged("view_refund_handled", KeyOf(_refundHandled, HandledChoices));

        _refundSearch.Width = D(130);
        _refundSearch.Margin = gap;
        _refundSearch.TextChanged += (_, _) => { _refundTimer.Stop(); _refundTimer.Start(); };
        _refundShowAll.Margin = new Padding(D(4), D(10), D(8), 0);
        _refundShowAll.CheckedChanged += (_, _) => LoadRefunds();
        _requestShowClosed.Margin = new Padding(D(4), D(10), D(8), 0);
        _requestShowClosed.CheckedChanged += (_, _) => LoadRequests();
        _refundCount.Margin = new Padding(D(4), D(12), 0, 0);

        GlassButton logRequest = ToolButton("Log request", (_, _) => LogRefundRequest(), GlassButtonStyle.Secondary);

        actions.Controls.Add(Rounded(_refundView));
        actions.Controls.Add(logRequest);
        // Approve and Decline live in the request dialog, next to the customer's note.
        actions.Controls.Add(ToolButton("Details", (_, _) => ShowRefundBooking()));
        actions.Controls.Add(ToolButton("Refresh", async (_, _) => await RefreshRefundsViewAsync(), GlassButtonStyle.Ghost));
        actions.Controls.Add(ToolButton("Export CSV", (_, _) => ExportRefundsView(), GlassButtonStyle.Secondary));
        actions.Controls.Add(_refundCount);

        // The rounded boxes are what show and hide with the view (hiding only the input would leave an empty box).
        RoundedInput sortBox = Rounded(_refundSort), handledBox = Rounded(_refundHandled), searchBox = Rounded(_refundSearch);
        view.Controls.Add(Rounded(_refundPeriod));
        view.Controls.Add(sortBox);
        view.Controls.Add(handledBox);
        view.Controls.Add(searchBox);
        view.Controls.Add(_refundShowAll);
        view.Controls.Add(_requestShowClosed);
        _refundOnlyControls.AddRange(new Control[] { sortBox, handledBox, searchBox, _refundShowAll });
        _requestOnlyControls.AddRange(new Control[] { logRequest, _requestShowClosed });

        GridStyle.Apply(_refunds);
        _refundSorter.Attach(_refunds);
        _refundSorter.Changed += (_, _) => LoadRefunds();
        _refunds.DefaultCellStyle.Font = Theme.Small;
        _refunds.DefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
        _refunds.ColumnHeadersDefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
        _refunds.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) ShowRefundBooking(); };
        _refunds.DataBindingComplete += (_, _) =>
        {
            var weights = new Dictionary<string, float>
            {
                ["Reference"] = 1.1f, ["Refunded"] = 1.3f, ["FlightNo"] = 0.7f, ["Route"] = 2.6f, ["FlightDate"] = 1.2f,
                ["TotalAmount"] = 1f, ["RefundAmount"] = 1f, ["RefundRef"] = 1.3f, ["Method"] = 1.6f, ["HandledBy"] = 1f
            };
            foreach (DataGridViewColumn column in _refunds.Columns)
                if (weights.TryGetValue(column.DataPropertyName, out float w)) column.FillWeight = w * 100;
        };

        GridStyle.Apply(_requests);
        WireSorter(_requestSorter, _requests, "view_requests_sort", LoadRequests);
        _requests.DefaultCellStyle.Font = Theme.Small;
        _requests.DefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
        _requests.ColumnHeadersDefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
        _requests.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) ShowRefundBooking(); };
        // A pending request from an address that is not the booking's stands out in amber, so it is not approved by habit.
        _requests.CellFormatting += (_, e) =>
        {
            if (e.RowIndex < 0 || e.RowIndex >= _requests.Rows.Count || _requests.Rows[e.RowIndex].DataBoundItem is not RequestRow { SenderDiffers: true }) return;
            e.CellStyle!.BackColor = Theme.AmberSoft;
            e.CellStyle.ForeColor = Theme.AmberText;
        };
        _requests.DataBindingComplete += (_, _) =>
        {
            var weights = new Dictionary<string, float>
            {
                ["Reference"] = 1.2f, ["Status"] = 1f, ["Source"] = 0.7f, ["Logged"] = 1.4f, ["FlightNo"] = 0.7f, ["Route"] = 2.2f,
                ["FlightDate"] = 1.2f, ["TotalAmount"] = 1f, ["EstimatedRefund"] = 1.1f, ["Note"] = 1.8f
            };
            foreach (DataGridViewColumn column in _requests.Columns)
                if (weights.TryGetValue(column.DataPropertyName, out float w)) column.FillWeight = w * 100;
        };

        var content = new Panel { Dock = DockStyle.Fill };
        content.Controls.Add(_refunds);
        content.Controls.Add(_requests);
        ApplyRefundView();
        return Section(toolbar, content);
    }

    /// <summary>A view choice changed: remember it and reload (not while the saved choices are being put back).</summary>
    private void ViewSettingChanged(string key, string value)
    {
        if (_loadingViewSettings) return;
        Try("Unable to save the view setting", () => _service.SetViewSetting(key, value));
        _refundSorter.Clear();   // choosing an order, period or filter in the row above replaces a header sort
        ReloadRefundsView();
    }

    private DateTime? SelectedPeriodStart() => PeriodStart(KeyOf(_refundPeriod, PeriodChoices), DateTime.Now);
    private bool ShowingRequests => _refundView.SelectedIndex == 1;

    /// <summary>Shows the grid and the toolbar controls that belong to the chosen view.</summary>
    private void ApplyRefundView()
    {
        _refunds.Visible = !ShowingRequests;
        _requests.Visible = ShowingRequests;
        foreach (Control c in _refundOnlyControls) c.Visible = !ShowingRequests;
        foreach (Control c in _requestOnlyControls) c.Visible = ShowingRequests;
    }

    /// <summary>The Refresh button: in the requests view it first reads the desk mailbox for new refund emails, then reloads the list.</summary>
    private async Task RefreshRefundsViewAsync()
    {
        if (ShowingRequests && SkyWings.Email.EmailServices.RefundMail is { } mailbox)
        {
            SkyWings.Email.RefundMailCheck check = await mailbox.CheckNowAsync();
            if (!check.Ok) ShowInfo(check.Message + "\n\nThe list shows what SkyWings already has.");
        }
        ReloadRefundsView();
    }

    /// <summary>Called when the mailbox reader logged new requests (on another thread): show them without a click.</summary>
    private void OnRefundEmailsLogged(SkyWings.Email.RefundMailCheck check)
    {
        if (IsDisposed || !IsHandleCreated) return;
        try { BeginInvoke(new Action(() => { if (ShowingRequests && _requests.Visible) LoadRequests(); else RefreshRequestBadge(); })); }
        catch (InvalidOperationException) { /* the window closed meanwhile */ }
    }

    private void ReloadRefundsView()
    {
        if (ShowingRequests) LoadRequests(); else LoadRefunds();
        RefreshRequestBadge();
    }

    // ------------------------------------------------------------------ refund requests

    private sealed record RequestRow(string Reference, string Status, string Source, string Logged, string FlightNo, string Route,
        string FlightDate, decimal TotalAmount, decimal EstimatedRefund, string Note,
        [property: System.ComponentModel.Browsable(false)] DateTime LoggedAt,
        [property: System.ComponentModel.Browsable(false)] DateTime FlightDateValue,
        [property: System.ComponentModel.Browsable(false)] bool SenderDiffers = false);

    private static readonly IReadOnlyDictionary<string, SortKey<RequestRow>> RequestSortKeys = new Dictionary<string, SortKey<RequestRow>>
    {
        ["Reference"] = new(r => r.Reference), ["Status"] = new(r => r.Status), ["Source"] = new(r => r.Source),
        ["Logged"] = new(r => r.LoggedAt, FirstDescending: true), ["FlightNo"] = new(r => r.FlightNo), ["Route"] = new(r => r.Route),
        ["FlightDate"] = new(r => r.FlightDateValue, FirstDescending: true), ["TotalAmount"] = new(r => r.TotalAmount, FirstDescending: true),
        ["EstimatedRefund"] = new(r => r.EstimatedRefund, FirstDescending: true), ["Note"] = new(r => r.Note),
    };
    private readonly GridSorter<RequestRow> _requestSorter = new(RequestSortKeys);

    private const string SenderWarning = "⚠ Sender differs from the booking's email. ";

    private static string RequestStatusText(string status) => status switch
    {
        BookingStore.RequestOpen => "Pending",
        BookingStore.RequestDone => "Done",
        _ => "Declined"
    };

    private void LoadRequests()
    {
        List<RefundRequestRow>? rows = null;
        if (!Try("Unable to load the refund requests", () => rows = _service.LoadRefundRequests(_requestShowClosed.Checked, SelectedPeriodStart()))) return;
        _requestRows = rows!;
        DateTime now = DateTime.Now;
        // Pending requests first, then the newest, as the store returns them, until a header is clicked.
        _requests.DataSource = _requestSorter.Sort(_requestRows.Select(r => new RequestRow(r.Reference, RequestStatusText(r.Status), r.Source == "EMAIL" ? "Email" : "Desk",
            RecencyFormat.Relative(r.LoggedAt, now), r.FlightNo, r.Route,
            r.FlightDate.ToString("MMM d, yyyy", CultureInfo.InvariantCulture), r.Total, r.EstimatedRefund,
            r.Status == BookingStore.RequestDismissed ? $"Declined: {r.Note}"
                : r.Status == BookingStore.RequestDone ? $"Refunded by {r.ClosedBy}"
                : (r.Source == "EMAIL" && !r.SenderMatches ? SenderWarning : "") + r.CustomerMessage,
            r.LoggedAt, r.FlightDate,
            r.Status == BookingStore.RequestOpen && r.Source == "EMAIL" && !r.SenderMatches))).ToList();
        int open = _requestRows.Count(r => r.Status == BookingStore.RequestOpen);
        _refundCount.Text = open == 0
            ? "No refund requests waiting"
            : $"{open:N0} request(s) waiting · cancel the booking to complete one, or dismiss it";
        UpdateRefundsBadge(open);
    }

    /// <summary>Counts the open requests for the tab button. Quiet on failure: this also runs from a timer.</summary>
    private void RefreshRequestBadge()
    {
        if (_refundsNav is null) return;
        try { UpdateRefundsBadge(_service.CountOpenRefundRequests()); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { AppLogger.Error("RefundRequests", ex); }
    }

    private void UpdateRefundsBadge(int open)
    {
        if (_refundsNav is null) return;
        _refundsNav.Text = open > 0 ? $"Refunds ({open})" : "Refunds";
        _refundsNav.Invalidate();
    }

    private void LogRefundRequest()
    {
        using var prompt = new PromptForm("Log refund request",
            "Use this when a customer asks for a refund (for example from the email button). It only writes the request down. " +
            "Cancel the booking from Bookings or Manage booking to give the refund.",
            "BOOKING REFERENCE", "e.g. SKY-1A2B3C4D5E6F", 20, "Log request",
            text => AdminService.AsReference(text) is null ? "Enter the full booking reference, like SKY-1A2B3C4D5E6F." : null);
        if (prompt.ShowDialog(this) != DialogResult.OK) return;
        RefundRequestResult? result = null;
        if (!Try("Unable to log the request", () => result = _service.LogRefundRequest(prompt.Value))) return;
        ShowInfo(result!.Message);
        if (ShowingRequests) LoadRequests(); else RefreshRequestBadge();
    }

    /// <summary>
    /// Approves the request the dialog showed: asks once more (this cancels the booking, refunds the customer and cannot be undone),
    /// then cancels, refunds and emails the customer. False when the staff member backed out, so the dialog can come back.
    /// </summary>
    private bool ApproveRequest(RefundRequestRow request, BookingRecord booking)
    {
        if (booking.Status != AppData.StatusConfirmed) { ShowInfo("This booking is no longer confirmed, so there is nothing to refund. The list has been refreshed."); LoadRequests(); return true; }

        decimal refund = AppData.ComputeRefundAmount(booking.TotalAmount, booking.FlightDateValue);
        string question = $"Approve the refund for {booking.Reference}?\n\n" +
                          $"PHP {refund:N2} goes back to {booking.PaymentMethod} {booking.MaskedAccount}.\n" +
                          "The booking is cancelled, the seats are freed and the customer is emailed that the refund is approved.";
        // An email from a different address than the booking's: say so once more at the last step, and make "No" the default button.
        bool senderDiffers = request.Source == "EMAIL" && !request.SenderMatches;
        if (senderDiffers)
            question += "\n\n⚠ This request came from an email address that is NOT the one on the booking. Check it before approving.";
        if (MessageBox.Show(this, question, "Approve refund", MessageBoxButtons.YesNo, senderDiffers ? MessageBoxIcon.Warning : MessageBoxIcon.Question,
                senderDiffers ? MessageBoxDefaultButton.Button2 : MessageBoxDefaultButton.Button1) != DialogResult.Yes) return false;

        RefundApprovalResult? result = null;
        if (!Try("Unable to approve the refund", () => result = _service.ApproveRefundRequest(request.Id, request.Reference))) return true;
        MessageBox.Show(this, result!.Ok ? $"{result.Message}\nRefund reference: {result.RefundReference}" : result.Message, "Approve refund",
            MessageBoxButtons.OK, result.Ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        LoadRequests();
        return true;
    }

    /// <summary>Closes the request without cancelling the booking; by default the customer is emailed the reason. False when the staff member backed out.</summary>
    private bool DeclineRequest(RefundRequestRow request)
    {
        using var prompt = new PromptForm("Decline request",
            $"The booking {request.Reference} stays active and nothing is refunded. Say why, for the record.",
            "REASON", "e.g. Flight is within the no-refund window", 200, "Decline",
            text => text.Length == 0 ? "Please give a short reason." : null,
            "Email this reason to the customer", true);
        if (prompt.ShowDialog(this) != DialogResult.OK) return false;
        bool done = false;
        if (!Try("Unable to decline the request", () => done = _service.DismissRefundRequest(request.Id, request.Reference, prompt.Value, prompt.OptionChecked))) return true;
        if (!done) ShowInfo("That request was already closed. The list has been refreshed.");
        LoadRequests();
        return true;
    }
    private void ExportRefundsView()
    {
        if (ShowingRequests) ExportRequests(); else ExportRefunds();
    }

    private void ExportRequests()
    {
        if (_requestRows.Count == 0) { ShowInfo("There are no requests in the list to export."); return; }
        using var dialog = new SaveFileDialog { Filter = "CSV files (*.csv)|*.csv", FileName = $"skywings-refund-requests-{DateTime.Today:yyyyMMdd}.csv" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var lines = CsvExport.Build(
            new[] { "Reference", "Status", "Logged", "Logged by", "Flight", "Route", "Flight date", "Total", "Estimated refund", "Closed", "Closed by", "Note" },
            _requestRows.Select(r => new[] { r.Reference, RequestStatusText(r.Status), r.LoggedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
                r.LoggedBy, r.FlightNo, r.Route, r.FlightDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), CsvExport.Money(r.Total),
                CsvExport.Money(r.EstimatedRefund), r.ClosedAt?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture), r.ClosedBy, r.Note }));
        if (Try("Unable to export the requests", () => File.WriteAllLines(dialog.FileName, lines)))
        {
            AuditExport("EXPORT_REFUND_REQUESTS", $"{_requestRows.Count} requests");
            ShowInfo($"{_requestRows.Count:N0} requests exported.");
        }
    }
    private static string RefundTime(RefundListRow r)
        => r.RefundedAt is { } at ? at.ToString("MMM d, yyyy h:mm tt", CultureInfo.InvariantCulture) : "";

    private void LoadRefunds()
    {
        RefundList? list = null;
        if (!Try("Unable to load the refunds", () => list = _service.LoadRefundList(_refundSearch.Text, _refundShowAll.Checked, SelectedPeriodStart(), KeyOf(_refundSort, SortChoices), KeyOf(_refundHandled, HandledChoices)))) return;
        _refundList = list!;
        DateTime now = DateTime.Now;
        _refunds.DataSource = _refundSorter.Sort(_refundList.Rows.Select(r => new RefundRow(r.Reference,
            r.RefundedAt is { } at ? RecencyFormat.Relative(at, now) : "", r.FlightNo, r.Route,
            r.FlightDate.ToString("MMM d, yyyy", CultureInfo.InvariantCulture), r.Total, r.Refund, r.RefundReference, r.Method, r.HandledBy,
            r.RefundedAt, r.FlightDate))).ToList();
        string shown = _refundList.Rows.Count < _refundList.MatchCount ? $"newest {_refundList.Rows.Count:N0} of {_refundList.MatchCount:N0}" : $"{_refundList.MatchCount:N0}";
        _refundCount.Text = $"{shown} cancelled · PHP {_refundList.MatchRefundTotal:N2} refunded";
    }

    private void ShowRefundBooking()
    {
        string? reference = ShowingRequests
            ? (_requests.CurrentRow?.DataBoundItem as RequestRow)?.Reference
            : (_refunds.CurrentRow?.DataBoundItem as RefundRow)?.Reference;
        if (reference is null) { ShowInfo("Select a booking first."); return; }
        // In the requests view the dialog also carries the request: its note and, while open, Approve and Decline.
        RequestRow? selected = ShowingRequests ? _requests.CurrentRow?.DataBoundItem as RequestRow : null;
        RefundRequestRow? request = selected is null ? null
            : _requestRows.FirstOrDefault(r => r.Reference == reference && RequestStatusText(r.Status) == selected.Status
                && r.LoggedAt == selected.LoggedAt);
        while (true)
        {
            BookingRecord? booking = null;
            if (!Try("Unable to load the booking", () => booking = _service.FindBooking(reference))) return;
            if (booking is null) { ShowInfo("The selected booking is no longer available."); return; }
            TripStatusInfo? trip = null;
            if (!Try("Unable to load the booking", () => trip = _service.LoadTripStatus(booking, DateTime.Now))) return;
            DialogResult choice;
            using (var details = new BookingDetailForm(booking, trip!, request)) choice = details.ShowDialog(this);
            if (request is null || choice is not (DialogResult.Yes or DialogResult.No)) return;
            // Backing out of the confirmation or the reason prompt brings the request back up instead of losing it.
            bool acted = choice == DialogResult.Yes ? ApproveRequest(request, booking) : DeclineRequest(request);
            if (acted) return;
        }
    }

    private void ExportRefunds()
    {
        if (_refundList.Rows.Count == 0) { ShowInfo("There are no refunds in the list to export."); return; }
        using var dialog = new SaveFileDialog { Filter = "CSV files (*.csv)|*.csv", FileName = $"skywings-refunds-{DateTime.Today:yyyyMMdd}.csv" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var lines = CsvExport.Build(
            new[] { "Reference", "Refunded", "Flight", "Route", "Flight date", "Total", "Refund", "Refund ref", "Method", "Handled by" },
            _refundList.Rows.Select(r => new[] { r.Reference, RefundTime(r), r.FlightNo, r.Route, r.FlightDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                CsvExport.Money(r.Total), CsvExport.Money(r.Refund), r.RefundReference, r.Method, r.HandledBy }));
        if (Try("Unable to export the refunds", () => File.WriteAllLines(dialog.FileName, lines)))
        {
            AuditExport("EXPORT_REFUNDS", $"{_refundList.Rows.Count} refunds");
            ShowInfo($"{_refundList.Rows.Count:N0} refunds exported.");
        }
    }

    // ------------------------------------------------------------------ reports

    private Panel BuildReportsPage()
    {
        var all = new[] { _tileTotal, _tileConfirmed, _tileCancelled, _tileRevenue, _tileRefunded, _tileAverage };
        var tiles = new TableLayoutPanel { Dock = DockStyle.Top, Height = D(104), ColumnCount = all.Length, RowCount = 1 };
        for (int i = 0; i < all.Length; i++)
        {
            tiles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / all.Length));
            all[i].Dock = DockStyle.Fill;
            all[i].Margin = new Padding(0, 0, i < all.Length - 1 ? D(6) : 0, 0);
            tiles.Controls.Add(all[i], i, 0);
        }
        tiles.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var toolbar = Toolbar();
        toolbar.Controls.Add(ToolButton("Refresh", (_, _) => LoadReport(), GlassButtonStyle.Ghost));
        toolbar.Controls.Add(ToolButton("Export CSV", (_, _) => ExportReport()));
        _lblRefreshed.Margin = new Padding(D(12), D(10), 0, 0);
        toolbar.Controls.Add(_lblRefreshed);

        var routesTitle = new Label
        {
            Text = "TOP ROUTES BY CONFIRMED BOOKINGS", Font = new Font(Theme.Small, FontStyle.Bold),
            ForeColor = Theme.TextSecondary, AutoSize = false, Height = D(30), Dock = DockStyle.Top,
            TextAlign = ContentAlignment.BottomLeft
        };

        GridStyle.Apply(_routeGrid);
        var page = new Panel { Dock = DockStyle.Fill, Visible = false };
        var card = new GlassCard { Dock = DockStyle.Fill, ContentPadding = 12 };
        card.Controls.Add(_routeGrid);
        var charts = new Panel { Dock = DockStyle.Right, Width = D(440), Padding = new Padding(D(12), 0, 0, 0) };
        _routeChart.Dock = DockStyle.Fill;
        _classChart.Dock = DockStyle.Bottom;
        _classChart.Height = D(170);
        charts.Controls.Add(_routeChart);
        charts.Controls.Add(_classChart);
        card.Controls.Add(charts);
        card.Controls.Add(routesTitle);
        card.Controls.Add(toolbar);

        var tilesHost = new Panel { Dock = DockStyle.Top, Height = D(110) };
        tilesHost.Controls.Add(tiles);
        page.Controls.Add(card);
        page.Controls.Add(tilesHost);
        return page;
    }

    private void LoadReport()
    {
        ReportStats? stats = null;
        if (!Try("Unable to load reports", () => stats = _service.LoadReport())) return;
        _lastReport = stats;
        if (stats is null)
        {
            foreach (var tile in new[] { _tileTotal, _tileConfirmed, _tileCancelled, _tileRevenue, _tileRefunded, _tileAverage }) tile.Value = "—";
            _routeGrid.DataSource = null;
            _routeChart.SetItems([]);
            _classChart.SetItems([]);
            return;
        }
        _tileTotal.Value = stats.TotalBookings.ToString("N0");
        _tileConfirmed.Value = stats.ConfirmedCount.ToString("N0");
        double cancelRate = stats.TotalBookings > 0 ? 100.0 * stats.CancelledCount / stats.TotalBookings : 0;
        _tileCancelled.Value = $"{stats.CancelledCount:N0} ({cancelRate:0.0}%)";
        _tileRevenue.Value = stats.TotalRevenue.ToString("N2");
        _tileRefunded.Value = stats.TotalRefunded.ToString("N2");
        _tileAverage.Value = (stats.ConfirmedCount > 0 ? stats.TotalRevenue / stats.ConfirmedCount : 0m).ToString("N2");
        _routeGrid.DataSource = stats.TopRoutes.ToList();
        _routeChart.SetItems(stats.TopRoutes.Take(6).Select(r => ($"{r.FlightNo}  {AirportCode(r.Origin)}→{AirportCode(r.Destination)}", (double)r.Count, r.Count.ToString("N0"))));
        _classChart.SetItems(stats.ByClass.OrderByDescending(c => c.Count).Select(c => (c.ClassName, (double)c.Count, c.Count.ToString("N0"))));
        _lblRefreshed.Text =$"Last refreshed {DateTime.Now:h:mm tt}";
    }

    /// <summary>"Manila (MNL)" becomes "MNL"; anything without a bracketed code is left as it is.</summary>
    private static string AirportCode(string place)
    {
        int open = place.LastIndexOf('('), close = place.LastIndexOf(')');
        return open >= 0 && close > open + 1 ? place[(open + 1)..close] : place;
    }

    private void ExportReport()
    {
        if (_lastReport is null) { LoadReport(); if (_lastReport is null) return; }
        using var dialog = new SaveFileDialog { Filter = "CSV files (*.csv)|*.csv", FileName = $"skywings-report-{DateTime.Today:yyyyMMdd}.csv" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var inv = CultureInfo.InvariantCulture;
        var lines = new List<string>
        {
            "Metric,Value", $"Total bookings,{_lastReport.TotalBookings}", $"Confirmed,{_lastReport.ConfirmedCount}",
            $"Cancelled,{_lastReport.CancelledCount}", $"Revenue,{_lastReport.TotalRevenue.ToString(inv)}",
            $"Refunded,{_lastReport.TotalRefunded.ToString(inv)}", "", "Flight,Origin,Destination,Bookings"
        };
        lines.AddRange(_lastReport.TopRoutes.Select(r => string.Join(',', CsvSafe.Cell(r.FlightNo), CsvSafe.Cell(r.Origin), CsvSafe.Cell(r.Destination), r.Count)));
        if (Try("Unable to export report", () => File.WriteAllLines(dialog.FileName, lines)))
            MessageBox.Show(this, "Report exported successfully.", "Export complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // ------------------------------------------------------------------ staff

    private sealed record StaffRowView(string Username, string Name, string Role, string Status, string LastSignIn, string Locked,
        [property: System.ComponentModel.Browsable(false)] StaffAccount Account);

    private Panel BuildStaffPage()
    {
        var toolbar = Toolbar();
        _toggleStaff.Click += (_, _) => ToggleSelectedStaff();
        toolbar.Controls.Add(ToolButton("Add staff", (_, _) => AddStaff(), GlassButtonStyle.Primary));
        toolbar.Controls.Add(ToolButton("Reset password", (_, _) => ResetSelectedPassword()));
        toolbar.Controls.Add(ToolButton("Change role", (_, _) => ChangeSelectedRole()));
        toolbar.Controls.Add(_toggleStaff);
        toolbar.Controls.Add(ToolButton("Refresh", (_, _) => LoadStaff(), GlassButtonStyle.Ghost));

        GridStyle.Apply(_staff);
        WireSorter(_staffSorter, _staff, "view_staff_sort", LoadStaff);
        _staff.SelectionChanged += (_, _) =>
        {
            if (SelectedStaff() is { } account) _toggleStaff.Text = account.Active ? "Deactivate" : "Activate";
        };
        return Section(toolbar, _staff);
    }

    private StaffAccount? SelectedStaff() => (_staff.CurrentRow?.DataBoundItem as StaffRowView)?.Account;

    private void LoadStaff()
    {
        List<StaffAccount> accounts = new();
        if (!Try("Unable to load staff", () => accounts = _service.LoadStaff())) return;
        DateTime now = DateTime.Now;
        string Local(DateTime? utc) => utc is { } t ? t.ToLocalTime().ToString("MMM d, yyyy h:mm tt", CultureInfo.InvariantCulture) : "—";
        // The last sign-in reads as "12 min ago" / "Yesterday, 4:45 PM"; a lock that lasts until a future time stays a clock time.
        string SignedIn(DateTime? utc) => utc is { } t ? RecencyFormat.Relative(t.ToLocalTime(), now) : "—";
        _staff.DataSource = _staffSorter.Sort(accounts.Select(a => new StaffRowView(a.Username, a.DisplayName, RolePermissions.Label(a.Role),
            a.Active ? "Active" : "Inactive", SignedIn(a.LastLoginAtUtc),
            a.LockedUntilUtc is { } until && until > DateTime.UtcNow ? "Until " + Local(until) : "—", a))).ToList();
    }

    private void AddStaff()
    {
        using var form = new AddStaffForm(_service);
        if (form.ShowDialog(this) == DialogResult.OK) LoadStaff();
    }

    private void ResetSelectedPassword()
    {
        if (SelectedStaff() is not { } account) { ShowInfo("Select a staff member first."); return; }
        using var form = new ResetPasswordForm(_service, account);
        if (form.ShowDialog(this) == DialogResult.OK) LoadStaff();
    }

    private void ChangeSelectedRole()
    {
        if (SelectedStaff() is not { } account) { ShowInfo("Select a staff member first."); return; }
        StaffRole target = account.Role == StaffRole.Admin ? StaffRole.Agent : StaffRole.Admin;
        if (MessageBox.Show(this, $"Make {account.DisplayName} an {target}?", "Change role", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        if (Try("Unable to change the role", () => _service.SetRole(account.Id, target))) LoadStaff();
    }

    private void ToggleSelectedStaff()
    {
        if (SelectedStaff() is not { } account) { ShowInfo("Select a staff member first."); return; }
        bool activate = !account.Active;
        if (!activate && MessageBox.Show(this, $"Deactivate {account.DisplayName}? They will no longer be able to sign in.", "Deactivate",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        if (Try("Unable to update the account", () => _service.SetActive(account.Id, activate))) LoadStaff();
    }

    // ------------------------------------------------------------------ waitlist

    private sealed record WaitlistRow(string Flight, string Date, string Cabin, string Traveller, string Seats, string Joined, string Status, string Offer, string Code);

    private readonly DataGridView _waitlist = new() { Dock = DockStyle.Fill, AutoGenerateColumns = true };

    private List<WaitlistEntry> _waitlistEntries = new();
    private static readonly IReadOnlyDictionary<string, SortKey<WaitlistEntry>> WaitlistSortKeys = new Dictionary<string, SortKey<WaitlistEntry>>
    {
        ["Flight"] = new(e => e.FlightNo), ["Date"] = new(e => e.FlightDate), ["Cabin"] = new(e => e.ClassName),
        ["Traveller"] = new(e => e.PassengerName), ["Seats"] = new(e => e.SeatsWanted),
        ["Joined"] = new(e => e.AddedAt, FirstDescending: true),   // the text is a date without a year: sort by the moment
        ["Status"] = new(e => e.Status == BookingStore.WaitlistWaiting ? e.Position : long.MaxValue),
        ["Offer"] = new(e => e.OfferExpiresAt), ["Code"] = new(e => e.BookingReference),
    };
    private readonly GridSorter<WaitlistEntry> _waitlistSorter = new(WaitlistSortKeys);

    private readonly Label _waitlistInfo = new()
    {
        Dock = DockStyle.Top, AutoSize = false, Font = Theme.Small, ForeColor = Theme.TextSecondary, TextAlign = ContentAlignment.MiddleLeft
    };

    private Panel BuildWaitlistPage()
    {
        var toolbar = Toolbar();
        toolbar.Controls.Add(ToolButton("Refresh", (_, _) => LoadWaitlist(), GlassButtonStyle.Ghost));
        toolbar.Controls.Add(ToolButton("Export CSV", (_, _) => ExportWaitlist(), GlassButtonStyle.Secondary));
        toolbar.Controls.Add(ToolButton("Details", (_, _) => ShowWaitlistDetails(), GlassButtonStyle.Secondary));
        toolbar.Controls.Add(ToolButton("Copy code", (_, _) => CopyWaitlistCode(), GlassButtonStyle.Secondary));
        if (Session.Has(Permission.CancelBookings))
        {
            toolbar.Controls.Add(ToolButton("Free a seat", (_, _) => FreeSeatForSelected()));
            toolbar.Controls.Add(ToolButton("Remove selected", (_, _) => RemoveSelectedWaitlist(), GlassButtonStyle.Danger));
        }
        GridStyle.Apply(_waitlist);
        WireSorter(_waitlistSorter, _waitlist, "view_waitlist_sort", LoadWaitlist);
        _waitlist.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) ShowWaitlistDetails(); };
        _waitlist.DefaultCellStyle.Font = Theme.Small;
        _waitlist.DefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
        _waitlistInfo.Height = D(58);

        // Same layout as Section, with an explanation under the toolbar: this tab is easy to misread when it is shown to someone new.
        var page = new Panel { Dock = DockStyle.Fill, Visible = false };
        var card = new GlassCard { Dock = DockStyle.Fill, ContentPadding = 12 };
        card.Controls.Add(_waitlist);
        card.Controls.Add(_waitlistInfo);
        card.Controls.Add(toolbar);
        page.Controls.Add(card);
        return page;
    }

    private void LoadWaitlist()
    {
        List<WaitlistEntry> entries = new();
        if (!Try("Unable to load the waitlist", () => entries = _service.LoadWaitlist())) return;
        _waitlistEntries = entries;
        _waitlist.DataSource = _waitlistSorter.Sort(entries).Select(e => new WaitlistRow(
            e.FlightNo, e.FlightDate.ToString("ddd, d MMM yyyy", CultureInfo.InvariantCulture), e.ClassName, e.PassengerName,
            e.SeatsWanted.ToString(CultureInfo.InvariantCulture),
            e.AddedAt.ToString("d MMM, h:mm tt", CultureInfo.InvariantCulture),
            e.Status == BookingStore.WaitlistWaiting ? $"Waiting · #{e.Position}" : "Offered",
            e.OfferExpiresAt is { } until ? "until " + until.ToString("MMM d, h:mm tt", CultureInfo.InvariantCulture) : "",
            e.BookingReference)).ToList();

        int offered = entries.Count(e => e.Status == BookingStore.WaitlistOffered);
        int cabins = entries.Select(e => (e.FlightNo, e.FlightDate, e.ClassName)).Distinct().Count();
        _waitlistInfo.Text =
            "When a cabin is sold out, travellers join its waitlist (first come, first served). When seats free up, the first entry " +
            $"whose party fits is offered them for {AppData.WaitlistOfferHours} hours; if the offer lapses, the next in line gets it." +
            Environment.NewLine +
            $"Now: {entries.Count - offered} waiting · {offered} offered · {cabins} sold-out cabin(s).";
    }

    private WaitlistEntry? SelectedWaitlistEntry()
        => _waitlist.CurrentRow?.DataBoundItem is WaitlistRow row
            ? _waitlistEntries.FirstOrDefault(e => e.BookingReference == row.Code)
            : null;

    private void ShowWaitlistDetails()
    {
        if (SelectedWaitlistEntry() is not { } entry) { ShowInfo("Select a waitlist entry first."); return; }
        List<WaitlistEntry> queue = _waitlistEntries
            .Where(e => e.FlightNo == entry.FlightNo && e.FlightDate == entry.FlightDate && e.ClassName == entry.ClassName).ToList();
        using var details = new WaitlistEntryForm(entry, queue);
        details.ShowDialog(this);
    }

    /// <summary>
    /// Frees seats in the selected entry's cabin so the waitlist offers them to the first traveller in line. Shows who got the offer
    /// and selects that row, so the whole step can be followed on screen.
    /// </summary>
    private void FreeSeatForSelected()
    {
        if (SelectedWaitlistEntry() is not { } entry) { ShowInfo("Select a waitlist entry first. Seats are freed in that entry's cabin."); return; }
        WaitlistSeatRelease? result = null;
        if (!Try("Unable to free a seat", () => result = _service.ReleaseSeatForWaitlist(entry.FlightNo, entry.FlightDate, entry.ClassName))) return;
        LoadWaitlist();

        string cabin = $"{entry.FlightNo} · {entry.FlightDate:ddd, d MMM} · {entry.ClassName}";
        if (result is null)
        {
            ShowInfo($"No seat could be freed on {cabin}: there is no booking in that cabin that can be cancelled here.");
            return;
        }
        if (result.OfferedTo is not { } offered)
        {
            ShowInfo($"{result.SeatsFreed} seat(s) were freed on {cabin}, but nobody was offered them. Offers stop when bookings close " +
                     $"({AppData.BookingCutoffHours} hours before departure), or nobody waiting fits the seats freed.");
            return;
        }
        foreach (DataGridViewRow row in _waitlist.Rows)
            if (row.DataBoundItem is WaitlistRow r && r.Code == offered.BookingReference)
            {
                _waitlist.ClearSelection();
                row.Selected = true;
                _waitlist.CurrentCell = row.Cells[0];
                break;
            }
        ShowInfo($"{result.SeatsFreed} seat(s) were freed on {cabin}.\n\n{offered.PassengerName} ({offered.BookingReference}) was offered " +
                 $"{offered.SeatsWanted} seat(s), held until {offered.OfferExpiresAt:ddd, d MMM h:mm tt}.");
    }

    private void CopyWaitlistCode()
    {
        if (SelectedWaitlistEntry() is not { } entry) { ShowInfo("Select a waitlist entry first."); return; }
        try { Clipboard.SetText(entry.BookingReference); ShowInfo($"Copied {entry.BookingReference}. Paste it into the Waitlist lookup on the home screen."); }
        catch (System.Runtime.InteropServices.ExternalException ex)
        {
            AppLogger.Error("Waitlist", ex);
            ShowInfo("The clipboard is busy. Try again.");
        }
    }

    private void RemoveSelectedWaitlist()
    {
        if (_waitlist.CurrentRow?.DataBoundItem is not WaitlistRow row) { ShowInfo("Select a waitlist entry first."); return; }
        if (MessageBox.Show(this, $"Remove {row.Traveller} ({row.Code}) from the waitlist?", "Remove from waitlist",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        Try("Unable to remove the waitlist entry", () => _service.RemoveWaitlistEntry(row.Code));
        LoadWaitlist();
    }

    // ------------------------------------------------------------------ audit

    private sealed record AuditRow(string When, string Who, string Role, string Action, string Item, string Detail);

    private Panel BuildAuditPage()
    {
        var toolbar = FilterRow();
        _auditSearch.Width = D(280);
        _auditSearch.Margin = new Padding(0, D(5), D(8), 0);
        _auditSearch.TextChanged += (_, _) => { _auditTimer.Stop(); _auditTimer.Start(); };
        toolbar.Controls.Add(Rounded(_auditSearch));
        toolbar.Controls.Add(ToolButton("Refresh", (_, _) => LoadAudit(), GlassButtonStyle.Ghost));
        toolbar.Controls.Add(ToolButton("Export CSV", (_, _) => ExportAudit()));

        GridStyle.Apply(_audit);
        WireSorter(_auditSorter, _audit, "view_audit_sort", LoadAudit);
        _audit.DefaultCellStyle.Font = Theme.Small;
        _audit.DefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
        _audit.DataBindingComplete += (_, _) =>
        {
            var weights = new Dictionary<string, float> { ["When"] = 1.5f, ["Who"] = 1f, ["Role"] = 0.6f, ["Action"] = 1.6f, ["Item"] = 1.3f, ["Detail"] = 3f };
            foreach (DataGridViewColumn column in _audit.Columns)
                if (weights.TryGetValue(column.DataPropertyName, out float w)) column.FillWeight = w * 100;
        };
        return Section(toolbar, _audit);
    }

    private static string AuditTime(AuditEntry e) => e.AtUtc.ToLocalTime().ToString("MMM d, yyyy h:mm:ss tt", CultureInfo.InvariantCulture);

    private void LoadAudit()
    {
        if (!Try("Unable to load the audit log", () => _auditEntries = _service.LoadAudit(_auditSearch.Text))) return;
        _audit.DataSource = _auditSorter.Sort(_auditEntries).Select(e => new AuditRow(AuditTime(e), e.Actor, e.Role, e.Action, e.Target, e.Detail)).ToList();
    }

    private void ExportAudit()
    {
        using var dialog = new SaveFileDialog { Filter = "CSV files (*.csv)|*.csv", FileName = $"skywings-audit-{DateTime.Today:yyyyMMdd}.csv" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        var lines = new List<string> { "When,Who,Role,Action,Item,Detail" };
        lines.AddRange(_auditEntries.Select(e => string.Join(',',
            CsvSafe.Cell(AuditTime(e)), CsvSafe.Cell(e.Actor), CsvSafe.Cell(e.Role), CsvSafe.Cell(e.Action), CsvSafe.Cell(e.Target), CsvSafe.Cell(e.Detail))));
        if (Try("Unable to export the audit log", () => File.WriteAllLines(dialog.FileName, lines)))
            MessageBox.Show(this, "Audit log exported.", "Export complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    // ------------------------------------------------------------------ messages

    private void ShowInfo(string message)
        => MessageBox.Show(this, message, "SkyWings Staff", MessageBoxButtons.OK, MessageBoxIcon.Information);

    /// <summary>One row of the Bookings list. The last three are for sorting and marking, not columns, so they are hidden from the grid.</summary>
    private sealed record BookingAdminRow(string Reference, string BookedAt, string FlightNo,
        string Route, string FlightDate, int Passengers, decimal TotalAmount, string Status,
        string PaymentStatus, decimal RefundAmount,
        [property: System.ComponentModel.Browsable(false)] DateTime BookedAtValue,
        [property: System.ComponentModel.Browsable(false)] DateTime FlightDateValue,
        [property: System.ComponentModel.Browsable(false)] bool IsNew);
}
