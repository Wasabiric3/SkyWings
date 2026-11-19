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
    private readonly Label _inventoryLabel = new() { AutoSize = true };
    private readonly GlassButton _toggleActive = new() { Text = "Deactivate", Style = GlassButtonStyle.Secondary };

    // bookings
    private readonly DataGridView _bookings = new() { Dock = DockStyle.Fill, AutoGenerateColumns = true };
    private readonly TextBox _bookingSearch = new() { PlaceholderText = "Search reference, flight or passenger" };
    private readonly ComboBox _bookingStatus = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    // ShowCheckBox pickers start checked, which would silently filter to today's date; start unchecked.
    private readonly DateTimePicker _bookingDate = new() { Format = DateTimePickerFormat.Short, Value = DateTime.Today };   // the flight date shown
    private readonly Label _bookingCount = new() { AutoSize = true };
    private readonly System.Windows.Forms.Timer _searchTimer = new() { Interval = 250 };
    private List<BookingRecord> _allBookings = new();
    private Dictionary<string, TripStatusInfo> _allTrips = new();

    // reports
    private readonly DataGridView _routeGrid = new() { Dock = DockStyle.Fill, AutoGenerateColumns = true };
    private readonly StatTile _tileTotal = new() { Title = "Total bookings" };
    private readonly StatTile _tileConfirmed = new() { Title = "Confirmed" };
    private readonly StatTile _tileCancelled = new() { Title = "Cancelled", Accent = Theme.DangerRed };
    private readonly StatTile _tileRevenue = new() { Title = "Revenue (PHP)" };
    private readonly StatTile _tileRefunded = new() { Title = "Refunded (PHP)", Accent = Theme.Warning };
    private ReportStats? _lastReport;

    // staff
    private readonly DataGridView _staff = new() { Dock = DockStyle.Fill, AutoGenerateColumns = true };
    private readonly GlassButton _toggleStaff = new() { Text = "Deactivate", Style = GlassButtonStyle.Secondary };

    // audit
    private readonly DataGridView _audit = new() { Dock = DockStyle.Fill, AutoGenerateColumns = true };
    private readonly TextBox _auditSearch = new() { PlaceholderText = "Search who, what or which item" };
    private readonly System.Windows.Forms.Timer _auditTimer = new() { Interval = 300 };
    private List<AuditEntry> _auditEntries = new();

    private StaffSession Session => _service.Session;

    public AdminForm(AdminService service)
    {
        _service = service;
        Text = "SkyWings Staff";
        StartPosition = FormStartPosition.CenterParent;
        SizeForScreen(1120, 640, 900, 520);
        Padding = new Padding(D(24), D(12), D(24), D(16));

        Panel? first = null;
        var nav = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(D(8), D(6), 0, 0) };
        void AddSection(string title, Permission needed, Func<Panel> build, Action? onShow = null)
        {
            if (!Session.Has(needed)) return;
            var button = new GlassButton { Text = title, Style = GlassButtonStyle.Ghost };
            Panel page = build();
            _sections[button] = page;
            _host.Controls.Add(page);
            button.Click += (_, _) => { ShowSection(button); onShow?.Invoke(); };
            nav.Controls.Add(button);
            first ??= page;
        }
        AddSection("Flights", Permission.ManageFlights, BuildFlightsPage);
        AddSection("Bookings", Permission.ViewBookings, BuildBookingsPage, LoadBookingsFromStore);
        AddSection("Waitlist", Permission.ViewBookings, BuildWaitlistPage, LoadWaitlist);
        AddSection("Reports", Permission.ViewReports, BuildReportsPage, LoadReport);
        AddSection("Staff", Permission.ManageStaff, BuildStaffPage, LoadStaff);
        AddSection("Audit", Permission.ViewAudit, BuildAuditPage, LoadAudit);

        Controls.Add(_host);
        Controls.Add(BuildHeader(nav));

        Theme.Apply(this);
        foreach (var label in new[] { _inventoryLabel, _bookingCount })
        {
            label.Font = Theme.Small;
            label.ForeColor = Theme.TextSecondary;
        }

        _searchTimer.Tick += (_, _) => { _searchTimer.Stop(); ApplyBookingFilter(); };
        _auditTimer.Tick += (_, _) => { _auditTimer.Stop(); LoadAudit(); };
        FormClosed += (_, _) =>
        {
            _searchTimer.Dispose();
            _auditTimer.Dispose();
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

    private Control BuildHeader(Control nav)
    {
        var title = new Label { Text = "SkyWings Staff", Font = Theme.Header, ForeColor = Theme.TextPrimary, AutoSize = false, Height = D(30), Dock = DockStyle.Top };
        var who = new Label
        {
            Text = $"{Session.DisplayName} · {Session.Role}", Font = Theme.Small, ForeColor = Theme.TextSecondary,
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
        bar.Controls.Add(nav);
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
        _inventoryLabel.Margin = new Padding(D(12), D(12), 0, 0);
        toolbar.Controls.Add(_inventoryLabel);

        GridStyle.Apply(_flights);
        _flights.SelectionChanged += (_, _) => UpdateFlightSelection();
        return Section(toolbar, _flights);
    }

    private void LoadFlights() => Try("Unable to load flights", () => _flights.DataSource = _service.LoadFlights());

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
        var toolbar = Toolbar();
        _bookingSearch.Width = D(200);
        _bookingSearch.Margin = new Padding(0, D(5), D(8), 0);
        _bookingStatus.Items.AddRange(TripStatus.Filters.Cast<object>().ToArray());
        _bookingStatus.SelectedIndex = 0;
        _bookingStatus.Width = D(130);
        _bookingStatus.Margin = new Padding(0, D(5), D(8), 0);
        _bookingDate.Width = D(128);
        _bookingDate.Margin = new Padding(0, D(5), D(8), 0);
        _bookingCount.Margin = new Padding(D(4), D(12), 0, 0);

        _bookingSearch.TextChanged += (_, _) => { _searchTimer.Stop(); _searchTimer.Start(); };
        _bookingStatus.SelectedIndexChanged += (_, _) => ApplyBookingFilter();
        _bookingDate.ValueChanged += (_, _) => LoadBookingsFromStore();

        toolbar.Controls.Add(_bookingSearch);
        toolbar.Controls.Add(_bookingStatus);
        toolbar.Controls.Add(_bookingDate);
        toolbar.Controls.Add(ToolButton("Details", (_, _) => ShowBookingDetails()));
        if (Session.Has(Permission.CancelBookings))
            toolbar.Controls.Add(ToolButton("Cancel booking", (_, _) => CancelSelectedBooking(), GlassButtonStyle.Danger));
        toolbar.Controls.Add(ToolButton("Refresh", (_, _) => LoadBookingsFromStore(), GlassButtonStyle.Ghost));
        toolbar.Controls.Add(_bookingCount);

        GridStyle.Apply(_bookings);
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
        content.Controls.Add(new TripLegend { Dock = DockStyle.Bottom });
        return Section(toolbar, content);
    }

    /// <summary>Column widths matched to what each column holds, so routes and dates are not cut off.</summary>
    private void SizeBookingColumns()
    {
        var weights = new Dictionary<string, (float Weight, string? Header)>
        {
            ["Reference"] = (1.0f, null), ["BookedAt"] = (1.3f, null), ["FlightNo"] = (0.9f, null),
            ["Route"] = (2.8f, null), ["FlightDate"] = (1.3f, null), ["Passengers"] = (0.6f, "Pax"),
            ["TotalAmount"] = (1.0f, null), ["Status"] = (1.6f, null),   // room for the widest pill, "Check-in open" ["PaymentStatus"] = (1.0f, null),
            ["RefundAmount"] = (0.9f, null)
        };
        foreach (DataGridViewColumn column in _bookings.Columns)
        {
            if (!weights.TryGetValue(column.DataPropertyName, out var entry)) continue;
            column.FillWeight = entry.Weight * 100;
            if (entry.Header is not null) column.HeaderText = entry.Header;
        }
    }

    /// <summary>
    /// Reads the chosen flight date's bookings (a busy day is thousands; all dates together would be far too many to load) and
    /// where each trip is right now. Filtering then works on this copy.
    /// </summary>
    private void LoadBookingsFromStore()
    {
        DateTime date = _bookingDate.Value.Date;
        if (Try("Unable to load bookings", () =>
            {
                _allBookings = _service.LoadBookingsForDate(date);
                _allTrips = _service.LoadTripStatusesForDate(_allBookings, date, DateTime.Now);
            }))
            ApplyBookingFilter();
    }

    private TripStatusInfo TripOf(BookingRecord b)
        => _allTrips.TryGetValue(b.Reference, out TripStatusInfo? trip) ? trip : TripStatus.Of(b, Array.Empty<CheckInRecord>(), DateTime.Now);

    private static BookingAdminRow ToRow(BookingRecord b, TripStatusInfo trip)
        => new(b.Reference, b.BookedAtValue.ToString("MMM d, yyyy", CultureInfo.InvariantCulture), b.FlightNo,
            $"{b.Origin} → {b.Destination}", b.FlightDate, b.Passengers.Count, b.TotalAmount, trip.Label, b.PaymentStatus, b.RefundAmount);

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
        var rows = _allBookings
            .Where(b => TripStatus.Matches(TripOf(b).Phase, status))
            .Where(b => search.Length == 0 ||
                        b.Reference.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                        b.FlightNo.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                        b.Passengers.Any(p => p.Name.Contains(search, StringComparison.OrdinalIgnoreCase)))
            .Select(b => ToRow(b, TripOf(b)))
            .ToList();
        _bookings.DataSource = rows;
        string day = _bookingDate.Value.ToString("MMM d", CultureInfo.InvariantCulture);
        _bookingCount.Text = rows.Count == _allBookings.Count
            ? $"{rows.Count:N0} bookings on {day}"
            : $"{rows.Count:N0} of {_allBookings.Count:N0} bookings on {day}";
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

    // ------------------------------------------------------------------ reports

    private Panel BuildReportsPage()
    {
        var tiles = new TableLayoutPanel { Dock = DockStyle.Top, Height = D(104), ColumnCount = 5, RowCount = 1 };
        var all = new[] { _tileTotal, _tileConfirmed, _tileCancelled, _tileRevenue, _tileRefunded };
        for (int i = 0; i < all.Length; i++)
        {
            tiles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
            all[i].Dock = DockStyle.Fill;
            all[i].Margin = new Padding(0, 0, i < all.Length - 1 ? D(6) : 0, 0);
            tiles.Controls.Add(all[i], i, 0);
        }
        tiles.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var toolbar = Toolbar();
        toolbar.Controls.Add(ToolButton("Refresh", (_, _) => LoadReport(), GlassButtonStyle.Ghost));
        toolbar.Controls.Add(ToolButton("Export CSV", (_, _) => ExportReport()));

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
            foreach (var tile in new[] { _tileTotal, _tileConfirmed, _tileCancelled, _tileRevenue, _tileRefunded }) tile.Value = "—";
            _routeGrid.DataSource = null;
            return;
        }
        _tileTotal.Value = stats.TotalBookings.ToString("N0");
        _tileConfirmed.Value = stats.ConfirmedCount.ToString("N0");
        _tileCancelled.Value = stats.CancelledCount.ToString("N0");
        _tileRevenue.Value = stats.TotalRevenue.ToString("N2");
        _tileRefunded.Value = stats.TotalRefunded.ToString("N2");
        _routeGrid.DataSource = stats.TopRoutes.ToList();
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
        string Local(DateTime? utc) => utc is { } t ? t.ToLocalTime().ToString("MMM d, yyyy h:mm tt", CultureInfo.InvariantCulture) : "—";
        _staff.DataSource = accounts.Select(a => new StaffRowView(a.Username, a.DisplayName, a.Role.ToString(),
            a.Active ? "Active" : "Inactive", Local(a.LastLoginAtUtc),
            a.LockedUntilUtc is { } until && until > DateTime.UtcNow ? "Until " + Local(until) : "—", a)).ToList();
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

    private sealed record WaitlistRow(string Flight, string Date, string Cabin, string Traveller, string Seats, string Status, string Offer, string Code);

    private readonly DataGridView _waitlist = new() { Dock = DockStyle.Fill, AutoGenerateColumns = true };

    private Panel BuildWaitlistPage()
    {
        var toolbar = Toolbar();
        toolbar.Controls.Add(ToolButton("Refresh", (_, _) => LoadWaitlist(), GlassButtonStyle.Ghost));
        if (Session.Has(Permission.CancelBookings))
            toolbar.Controls.Add(ToolButton("Remove selected", (_, _) => RemoveSelectedWaitlist(), GlassButtonStyle.Danger));
        GridStyle.Apply(_waitlist);
        _waitlist.DefaultCellStyle.Font = Theme.Small;
        _waitlist.DefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
        return Section(toolbar, _waitlist);
    }

    private void LoadWaitlist()
    {
        List<WaitlistEntry> entries = new();
        if (!Try("Unable to load the waitlist", () => entries = _service.LoadWaitlist())) return;
        _waitlist.DataSource = entries.Select(e => new WaitlistRow(
            e.FlightNo, e.FlightDate.ToString("ddd, d MMM yyyy", CultureInfo.InvariantCulture), e.ClassName, e.PassengerName,
            e.SeatsWanted.ToString(CultureInfo.InvariantCulture),
            e.Status == BookingStore.WaitlistWaiting ? $"Waiting · #{e.Position}" : "Offered",
            e.OfferExpiresAt is { } until ? "until " + until.ToString("MMM d, h:mm tt", CultureInfo.InvariantCulture) : "",
            e.BookingReference)).ToList();
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
        var toolbar = Toolbar();
        _auditSearch.Width = D(280);
        _auditSearch.Margin = new Padding(0, D(5), D(8), 0);
        _auditSearch.TextChanged += (_, _) => { _auditTimer.Stop(); _auditTimer.Start(); };
        toolbar.Controls.Add(_auditSearch);
        toolbar.Controls.Add(ToolButton("Refresh", (_, _) => LoadAudit(), GlassButtonStyle.Ghost));
        toolbar.Controls.Add(ToolButton("Export CSV", (_, _) => ExportAudit()));

        GridStyle.Apply(_audit);
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
        _audit.DataSource = _auditEntries.Select(e => new AuditRow(AuditTime(e), e.Actor, e.Role, e.Action, e.Target, e.Detail)).ToList();
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

    private sealed record BookingAdminRow(string Reference, string BookedAt, string FlightNo,
        string Route, string FlightDate, int Passengers, decimal TotalAmount, string Status,
        string PaymentStatus, decimal RefundAmount);
}
