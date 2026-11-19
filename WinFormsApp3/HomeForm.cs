using System.Drawing.Drawing2D;
using SkyWings.Controllers;
using SkyWings.UI;

namespace SkyWings;

/// <summary>Home screen: hero, a working flight search, and region cards.</summary>
public partial class HomeForm : GlassForm
{
    private readonly BookingStore _store;
    private readonly FlowLayoutPanel _destinations = new();
    private readonly ComboBox _to = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly DateTimePicker _date = new() { Format = DateTimePickerFormat.Custom, CustomFormat = AppData.DateInputFormat, MinDate = DateTime.Today };
    private readonly ComboBox _cabin = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Label _routeCount = new();
    private readonly GlassButton _bookButton = new();
    private readonly GlassButton _bookSelected = new();
    private readonly DataGridView _flights = new();
    private readonly Label _flightSummary = new();
    private readonly Label _clock = new();
    private readonly Label _live = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    private List<FlightRow> _boardRows = new();
    private DateTime _lastReload = DateTime.Now, _lastRestamp = DateTime.Now;
    private bool _loadingDashboard;

    private sealed record RouteSummary(string Title, string Subtitle, Color Accent, IReadOnlyList<Flight> Flights, string RouteType);

    private static Color AccentFor(string region) => region switch
    {
        "Domestic" => Color.FromArgb(96, 148, 128),
        "Southeast Asia" => Color.FromArgb(101, 146, 160),
        "Europe" => Color.FromArgb(120, 140, 176),
        _ => Color.FromArgb(96, 130, 168)
    };

    private readonly HomeController _controller;
    private readonly StaffSession _session;
    private readonly IdleMonitor _idle = new();
    private string _signOutReason = "Closed the application";

    /// <summary>True when the person chose Sign out (or was timed out), so the program shows the sign-in screen again.</summary>
    public bool SignedOut { get; private set; }

    public HomeForm(BookingStore store, StaffSession session)
    {
        InitializeComponent();
        _store = store;
        _session = session;
        _controller = new HomeController(store, session);
        _simulation = new SimulationService(store);
        Text = "SkyWings Airline";
        StartPosition = FormStartPosition.CenterScreen;
        SizeForScreen(1180, 680, 1060, 560);
        BuildLayout();
        Theme.Flatten(this);   // HomeForm styles itself (no Theme.Apply): drop transparent labels/panels so repaints do not cascade
        LoadDashboard();

        // The departures board ticks once a second; see Tick().
        _timer.Tick += (_, _) => Tick();
        Tick();
        _timer.Start();

        // If the demo activity is on, fill the flights as soon as the window is up instead of waiting for the first minute.
        Shown += (_, _) => { if (_store.DemoActivityEnabled) _lastReload = DateTime.MinValue; };

        // One idle timer for the whole signed-in session: after a quiet spell the screen locks back to the sign-in page.
        _idle.TimedOut += (_, _) => SignOut("Signed out after being idle");
        FormClosed += (_, _) =>
        {
            _timer.Stop();
            _timer.Dispose();
            _idle.Dispose();
            _controller.RecordSignOut(_signOutReason);
        };
    }

    /// <summary>Ends the session and returns to the sign-in screen. Any dialogs this window opened are closed first.</summary>
    private void SignOut(string reason)
    {
        _signOutReason = reason;
        SignedOut = true;
        foreach (Form owned in OwnedForms.ToArray()) owned.Close();
        Close();
    }

    private int D(int px) => Gfx.S(this, px);

    /// <summary>
    /// A label's own Height is only 23px until it is first laid out, so positions that depend on it
    /// must use its preferred height instead.
    /// </summary>
    private static int Below(Label label) => label.Top + label.GetPreferredSize(Size.Empty).Height;

    // ------------------------------------------------------------------ layout

    private void BuildLayout()
    {
        var content = new Panel
        {
            Dock = DockStyle.Fill, AutoScroll = true,
            Padding = new Padding(D(28), D(2), D(28), D(10)), BackColor = Color.Transparent
        };

        Control hero = BuildHero();
        Control search = BuildSearch();
        Control flightList = BuildFlightList();
        Control sectionTitle = BuildSectionTitle();
        _destinations.Dock = DockStyle.Top;
        _destinations.Height = D(172);
        _destinations.WrapContents = false;
        _destinations.AutoScroll = false;
        _destinations.BackColor = Color.Transparent;

        // Docked controls stack in reverse add order.
        content.Controls.Add(_destinations);
        content.Controls.Add(sectionTitle);
        content.Controls.Add(flightList);
        content.Controls.Add(Spacer(D(10)));
        content.Controls.Add(search);
        content.Controls.Add(hero);
        content.AutoScrollMinSize = new Size(0,
            hero.Height + search.Height + flightList.Height + D(10) + sectionTitle.Height + _destinations.Height + D(16));

        Controls.Add(content);
        Controls.Add(BuildFooter());
        Controls.Add(BuildHeader());

        // Start at the top with focus on the main action; focusing a lower field would scroll the page.
        ActiveControl = _bookButton;
        Shown += (_, _) => content.AutoScrollPosition = Point.Empty;
    }

    private static Panel Spacer(int height) => new() { Dock = DockStyle.Top, Height = height, BackColor = Color.Transparent };

    private Control BuildHeader()
    {
        var header = new Panel { Dock = DockStyle.Top, Height = D(62), Padding = new Padding(D(28), D(8), D(20), 0), BackColor = Color.Transparent };

        var brand = new Label
        {
            Text = "SkyWings", Font = new Font("Segoe UI", 20f, FontStyle.Bold), ForeColor = Theme.TextPrimary,
            AutoSize = true, Location = new Point(D(28), D(4)), BackColor = Color.Transparent
        };
        // Transparent labels must not overlap: the upper one would lose its descenders (y, g).
        var tag = new Label
        {
            Text = "A little closer to everywhere.", Font = Theme.Small, ForeColor = Theme.TextSecondary,
            AutoSize = true, Location = new Point(D(30), Below(brand)), BackColor = Color.Transparent
        };

        var links = new FlowLayoutPanel
        {
            Dock = DockStyle.Right, Width = D(800), FlowDirection = FlowDirection.RightToLeft, WrapContents = false,
            BackColor = Color.Transparent, Padding = new Padding(0, D(8), 0, 0)
        };
        GlassButton Link(string text, EventHandler click, GlassButtonStyle style = GlassButtonStyle.Ghost)
        {
            var button = new GlassButton { Text = text, Style = style, Height = D(38), Margin = new Padding(D(2), 0, 0, 0) };
            button.Click += click;
            return button;
        }
        // Who is signed in is shown on the button itself, so there is no extra label to overlap.
        links.Controls.Add(Link($"Sign out · {_controller.FirstName}", (_, _) => SignOut("Signed out"), GlassButtonStyle.Secondary));
        links.Controls.Add(Link("Staff", OpenAdmin));
        links.Controls.Add(Link("Help", ShowHelp));
        links.Controls.Add(Link("Rewards", ShowRewards));
        links.Controls.Add(Link("Check-in", ShowCheckIn));
        links.Controls.Add(Link("Waitlist", ShowWaitlist));
        links.Controls.Add(Link("Manage booking", ShowBookingLookupMessage));
        // Each button sizes itself to its text (the sign-out one includes the staff name), so a fixed width would cut off the
        // leftmost link. Make the menu exactly as wide as its buttons.
        links.Width = links.Padding.Horizontal + links.Controls.Cast<Control>().Sum(c => c.Width + c.Margin.Horizontal);

        header.Controls.Add(links);
        header.Controls.Add(tag);
        header.Controls.Add(brand);
        return header;
    }

    private Control BuildHero()
    {
        var hero = new GlassCard { Dock = DockStyle.Top, Height = D(184), ContentPadding = 8 };

        var art = new HeroArt { Dock = DockStyle.Right, Width = D(420) };
        var text = new Panel { Dock = DockStyle.Fill, Padding = new Padding(D(24), D(14), D(8), D(10)), BackColor = Color.Transparent };

        var cta = _bookButton;
        cta.Text = "Book a flight  →";
        cta.Height = D(44);
        cta.Click += OpenBooking;

        // Each label starts at the previous one's bottom edge so transparent labels never overlap.
        var eyebrow = new Label
        {
            Text = "YOUR NEXT CHAPTER STARTS HERE", Font = new Font(Theme.Small, FontStyle.Bold), ForeColor = Theme.Accent,
            AutoSize = true, Location = new Point(D(24), D(4)), BackColor = Color.Transparent
        };
        var headline = new Label
        {
            Text = "A better way to fly.", Font = new Font("Segoe UI", 22f, FontStyle.Bold), ForeColor = Theme.TextPrimary,
            AutoSize = true, Location = new Point(D(24), Below(eyebrow) + D(2)), BackColor = Color.Transparent
        };
        var sub = new Label
        {
            Text = "More possibilities. A little more peace of mind.", Font = Theme.Body, ForeColor = Theme.TextSecondary,
            AutoSize = true, Location = new Point(D(26), Below(headline) + D(2)), BackColor = Color.Transparent
        };
        cta.Location = new Point(D(24), Below(sub) + D(10));

        text.Controls.Add(cta);
        text.Controls.Add(sub);
        text.Controls.Add(headline);
        text.Controls.Add(eyebrow);
        hero.Controls.Add(text);
        hero.Controls.Add(art);
        return hero;
    }

    private Control BuildSearch()
    {
        // One tidy row: a small caption above each field, and the button level with the fields.
        var card = new GlassCard { Dock = DockStyle.Top, Height = D(92), ContentPadding = 10 };
        _cabin.Items.AddRange(AppData.ClassNames.Cast<object>().ToArray());
        _cabin.SelectedIndex = 0;
        foreach (Control input in new Control[] { _to, _date, _cabin })
        {
            input.Font = Theme.Body;
            input.BackColor = Color.White;
            input.Dock = DockStyle.None;
            input.Anchor = AnchorStyles.Left | AnchorStyles.Right;   // full width, centred in its row
            input.Margin = new Padding(0, 0, D(14), 0);
        }
        _to.DropDownWidth = D(360);

        Label Caption(string text) => new()
        {
            Text = text, Font = new Font(Theme.Small, FontStyle.Bold), ForeColor = Theme.TextSecondary,
            AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.BottomLeft, BackColor = Color.Transparent
        };

        _bookSelected.Text = "Book selected  →";
        _bookSelected.Dock = DockStyle.Fill;
        _bookSelected.Margin = Padding.Empty;
        _bookSelected.Enabled = false;
        _bookSelected.Click += BookSelectedFlight;

        var row = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 2, BackColor = Color.Transparent };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 18));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, D(190)));
        row.RowStyles.Add(new RowStyle(SizeType.Absolute, D(24)));
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        row.Controls.Add(Caption("TO  (FROM MANILA)"), 0, 0);
        row.Controls.Add(Caption("DEPARTURE"), 1, 0);
        row.Controls.Add(Caption("CABIN"), 2, 0);
        row.Controls.Add(_to, 0, 1);
        row.Controls.Add(_date, 1, 1);
        row.Controls.Add(_cabin, 2, 1);
        row.Controls.Add(_bookSelected, 3, 1);
        card.Controls.Add(row);

        // Any change to the fields refreshes the flight list straight away.
        _to.SelectedIndexChanged += (_, _) => RefreshFlights();
        _date.ValueChanged += (_, _) => RefreshFlights();
        _cabin.SelectedIndexChanged += (_, _) => RefreshFlights();
        return card;
    }

    /// <summary>The live list of every flight that matches the fields above.</summary>
    private Control BuildFlightList()
    {
        var card = new GlassCard { Dock = DockStyle.Top, Height = D(300), ContentPadding = 10 };

        _flightSummary.Font = Theme.Small;
        _flightSummary.ForeColor = Theme.TextSecondary;
        _flightSummary.AutoSize = false;
        _flightSummary.Dock = DockStyle.Fill;
        _flightSummary.TextAlign = ContentAlignment.MiddleLeft;
        _flightSummary.BackColor = Color.Transparent;

        // Header: what is shown on the left, a live clock on the right.
        _live.Text = "● LIVE";
        _live.Font = new Font(Theme.Small, FontStyle.Bold);
        _live.ForeColor = Theme.Accent;
        _live.AutoSize = false;
        _live.Width = D(64);
        _live.Dock = DockStyle.Right;
        _live.TextAlign = ContentAlignment.MiddleRight;
        _live.BackColor = Color.Transparent;
        _clock.Font = Theme.Body;
        _clock.ForeColor = Theme.TextPrimary;
        _clock.AutoSize = false;
        _clock.Width = D(250);
        _clock.Dock = DockStyle.Right;
        _clock.TextAlign = ContentAlignment.MiddleRight;
        _clock.BackColor = Color.Transparent;
        var header = new Panel { Dock = DockStyle.Top, Height = D(30), BackColor = Color.Transparent };
        header.Controls.Add(_flightSummary);
        header.Controls.Add(_clock);
        header.Controls.Add(_live);

        GridStyle.Apply(_flights);
        _flights.Dock = DockStyle.Fill;
        _flights.AutoGenerateColumns = false;
        _flights.ColumnHeadersHeight = D(34);
        _flights.RowTemplate.Height = D(38);
        // Every flight on this board leaves Manila, so the destination alone is enough and leaves room for times and fares.
        AddFlightColumn("Flight", nameof(FlightRow.FlightNo), 0.75f);
        AddFlightColumn("To", nameof(FlightRow.DestinationText), 1.9f);
        AddFlightColumn("Scheduled", nameof(FlightRow.ScheduledText), 0.95f);
        AddFlightColumn("Estimated", nameof(FlightRow.EstimatedText), 0.95f);
        AddFlightColumn("Gate", nameof(FlightRow.GateText), 0.55f);
        AddFlightColumn("Status", nameof(FlightRow.StatusText), 1.3f);
        AddFlightColumn("Countdown", nameof(FlightRow.CountdownText), 1.4f);
        AddFlightColumn("Fare", nameof(FlightRow.Price), 1.0f, right: true);
        AddFlightColumn("Seats left", nameof(FlightRow.SeatsLeft), 0.8f, right: true);
        AddFlightColumn("Load", nameof(FlightRow.LoadPercent), 1.3f);
        _flights.SelectionChanged += (_, _) => UpdateBookButton();
        _flights.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) BookSelectedFlight(null, EventArgs.Empty); };
        _flights.CellFormatting += FormatBoardCell;
        _flights.CellPainting += PaintBoardCell;

        card.Controls.Add(_flights);
        card.Controls.Add(header);
        return card;
    }

    private static readonly Font StatusFont = new(Theme.Small, FontStyle.Bold);

    private static bool HasLeft(FlightRow row)
        => row.Status.Kind is FlightStatusKind.Departed or FlightStatusKind.InFlight or FlightStatusKind.Landed;

    private static Color StatusColor(FlightStatusKind kind) => kind switch
    {
        FlightStatusKind.CheckInOpen => Theme.Accent,
        FlightStatusKind.Boarding or FlightStatusKind.Delayed => Theme.Warning,
        FlightStatusKind.GateClosing => Theme.DangerRed,
        FlightStatusKind.Departed or FlightStatusKind.InFlight => Theme.Info,
        _ => Theme.TextSecondary
    };

    /// <summary>Green for a normal load, amber from 90% (nearly full), red from 98% (full).</summary>
    private static Color LoadColor(int percent) => percent >= 98 ? Theme.DangerRed : percent >= 90 ? Theme.Warning : Theme.Primary;

    /// <summary>Text colours for the plain cells; dims flights that have already left. Status and load are painted below.</summary>
    private void FormatBoardCell(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || _flights.Rows[e.RowIndex].DataBoundItem is not FlightRow row) return;
        string column = _flights.Columns[e.ColumnIndex].DataPropertyName;
        bool past = HasLeft(row);

        if (column == nameof(FlightRow.FlightNo))
        {
            e.CellStyle!.Font = StatusFont;
            e.CellStyle.ForeColor = past ? Theme.TextSecondary : Theme.AccentDark;
        }
        else if (column == nameof(FlightRow.EstimatedText) && row.Status.DelayMinutes > 0)
        {
            e.CellStyle!.ForeColor = Theme.Warning;
            e.CellStyle.Font = StatusFont;
        }
        else if (column == nameof(FlightRow.SeatsLeft) && (row.SoldOut || row.FewSeatsLeft))
        {
            e.CellStyle!.ForeColor = row.SoldOut ? Theme.DangerRed : Theme.Warning;   // sold out: red, under 20 left: amber
            e.CellStyle.Font = StatusFont;
        }
        else if (past)
            e.CellStyle!.ForeColor = Theme.TextSecondary;
    }

    /// <summary>Draws the status as a coloured pill and the load as a bar with its percentage.</summary>
    private void PaintBoardCell(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0 || e.Graphics is null || _flights.Rows[e.RowIndex].DataBoundItem is not FlightRow row) return;
        string column = _flights.Columns[e.ColumnIndex].DataPropertyName;
        if (column != nameof(FlightRow.StatusText) && column != nameof(FlightRow.LoadPercent)) return;

        e.PaintBackground(e.ClipBounds, true);   // row colour, selection highlight and grid line as usual
        Graphics g = e.Graphics;
        GraphicsState state = g.Save();
        try
        {
            Gfx.HighQuality(g);
            if (column == nameof(FlightRow.StatusText)) PaintStatusPill(g, e.CellBounds, row);
            else PaintLoadBar(g, e.CellBounds, row);
        }
        finally { g.Restore(state); }
        e.Handled = true;
    }

    private void PaintStatusPill(Graphics g, Rectangle cell, FlightRow row)
    {
        Color color = StatusColor(row.Status.Kind);
        Size text = TextRenderer.MeasureText(g, row.StatusText, StatusFont, Size.Empty, TextFormatFlags.NoPadding);
        int height = D(22), maxWidth = Math.Max(0, cell.Width - D(16));
        var pill = new Rectangle(cell.X + D(8), cell.Y + (cell.Height - height) / 2, Math.Min(text.Width + D(20), maxWidth), height);
        using (GraphicsPath path = Gfx.RoundedRect(pill, height / 2f))
        using (var fill = new SolidBrush(Gfx.Mix(color, Color.White, 0.86f)))
            g.FillPath(fill, path);
        TextRenderer.DrawText(g, row.StatusText, StatusFont, pill, color,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
    }

    private void PaintLoadBar(Graphics g, Rectangle cell, FlightRow row)
    {
        int percent = Math.Clamp(row.LoadPercent, 0, 100);
        bool past = HasLeft(row);
        Color color = LoadColor(percent);
        if (past) color = Gfx.Mix(color, Color.White, 0.45f);

        int textWidth = D(40), gap = D(8), barHeight = D(8);
        var label = new Rectangle(cell.Right - D(10) - textWidth, cell.Y, textWidth, cell.Height);
        var track = new RectangleF(cell.X + D(10), cell.Y + (cell.Height - barHeight) / 2f, Math.Max(0, label.X - gap - cell.X - D(10)), barHeight);
        using (GraphicsPath path = Gfx.RoundedRect(track, barHeight / 2f))
        using (var back = new SolidBrush(Theme.StepPending))
            g.FillPath(back, path);
        if (percent > 0 && track.Width > 0)
        {
            var filled = track with { Width = Math.Max(barHeight, track.Width * percent / 100f) };
            using GraphicsPath path = Gfx.RoundedRect(filled, barHeight / 2f);
            using var brush = new SolidBrush(color);
            g.FillPath(brush, path);
        }
        TextRenderer.DrawText(g, percent + "%", StatusFont, label, past ? Theme.TextSecondary : Theme.TextPrimary,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }

    private void AddFlightColumn(string header, string property, float weight, bool right = false)
    {
        var column = new DataGridViewTextBoxColumn { HeaderText = header, DataPropertyName = property, FillWeight = weight * 100 };
        if (right)
        {
            column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
        }
        _flights.Columns.Add(column);
    }

    private FlightRow? SelectedFlight() => _flights.CurrentRow?.DataBoundItem as FlightRow;

    /// <summary>Booking needs a date from tomorrow on; today's board is for tracking only.</summary>
    private bool CanBookDate => _date.Value.Date > DateTime.Today;

    /// <summary>"Book selected" normally; on a sold-out flight the same button becomes "Join waitlist".</summary>
    private void UpdateBookButton()
    {
        FlightRow? row = SelectedFlight();
        _bookSelected.Text = row is { SoldOut: true } ? "Join waitlist  →" : "Book selected  →";
        _bookSelected.Enabled = CanBookDate && row is not null;
    }

    /// <summary>Reloads the board from the database (seats included) for the current destination, date and cabin.</summary>
    private void RefreshFlights()
    {
        if (_loadingDashboard || _to.SelectedItem is not DestinationOption option) return;
        try
        {
            BindBoard(_controller.SearchFlights(option.Destination, _date.Value.Date, _cabin.Text));
            _lastReload = _lastRestamp = DateTime.Now;
        }
        catch (IOException ex)
        {
            AppLogger.Error("FlightSearch", ex);
            _boardRows = new List<FlightRow>();
            _flights.DataSource = null;
            _flightSummary.Text = "The flights could not be loaded: " + ex.Message;
        }
        UpdateBookButton();
    }

    private readonly SimulationService _simulation;
    private bool _reloading;

    /// <summary>
    /// The once-a-minute reload. The database work (seat counts for every flight, lapsed waitlist offers) runs on a
    /// worker thread so the window never freezes; only binding the finished rows happens on the UI thread.
    /// </summary>
    private async void ReloadBoardInBackground()
    {
        if (_reloading || _to.SelectedItem is not DestinationOption option) return;
        _reloading = true;
        string destination = option.Destination, cabin = _cabin.Text;
        DateTime date = _date.Value.Date;
        try
        {
            List<FlightRow> rows = await Task.Run(() =>
            {
                // Lapsed waitlist offers pass to the next traveller in line (this also emails them).
                // Demo activity (off unless staff switched it on): fills flights and takes one live step, in the background.
                try { _simulation.RunIfEnabled(DateTime.Now); }
                catch (Exception ex) { AppLogger.Error("Demo", ex); }
                try { _store.ExpireWaitlistOffers(DateTime.Now); }
                catch (Exception ex) { AppLogger.Error("Waitlist", ex); }
                return _controller.SearchFlights(destination, date, cabin);
            });
            // The search bar may have changed while loading; the next tick simply tries again.
            if (IsDisposed || _to.SelectedItem is not DestinationOption now || now.Destination != destination
                || _date.Value.Date != date || _cabin.Text != cabin) return;
            BindBoard(rows);
            _lastReload = _lastRestamp = DateTime.Now;
            UpdateBookButton();
        }
        catch (IOException ex)
        {
            AppLogger.Error("FlightSearch", ex);
            _lastReload = DateTime.Now;   // do not retry every second while the database is unavailable
        }
        finally { _reloading = false; }
    }

    /// <summary>Shows rows on the board, keeping the same flight selected and the list where it was scrolled.</summary>
    private void BindBoard(List<FlightRow> rows)
    {
        string? keep = SelectedFlight()?.FlightNo;
        int top = _flights.FirstDisplayedScrollingRowIndex;
        _boardRows = rows;
        _flights.DataSource = rows;
        if (rows.Count > 0)
        {
            int index = keep is null ? 0 : Math.Max(0, rows.FindIndex(r => r.FlightNo == keep));
            _flights.CurrentCell = _flights[0, index];
            if (top >= 0 && top < rows.Count) _flights.FirstDisplayedScrollingRowIndex = top;
        }
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        if (_to.SelectedItem is not DestinationOption option) return;
        string place = option.Destination.Length == 0 ? "All flights from Manila" : $"Flights to {option.Destination}";
        string tail = CanBookDate
            ? "Fares are one-way per traveller. Double-click a flight to book it."
            : "Booking opens from tomorrow. Pick a later date to book.";
        _flightSummary.Text = _boardRows.Count == 0
            ? $"{place}  ·  no flights match"
            : $"{place}  ·  {_boardRows.Count} flight(s)  ·  {_date.Value:ddd, MMM d}  ·  {_cabin.Text}  ·  {tail}";
    }

    // ------------------------------------------------------------------ live board

    /// <summary>Runs every second: moves the clock, re-works statuses every 15 s and reloads seats every minute.</summary>
    private void Tick()
    {
        DateTime now = DateTime.Now;
        _clock.Text = now.ToString("ddd, MMM d  ·  h:mm:ss tt");
        if (_loadingDashboard || _boardRows.Count == 0 && _to.SelectedItem is null) return;

        if (now - _lastReload >= TimeSpan.FromSeconds(60))
        {
            ReloadBoardInBackground();
            return;
        }
        if (now - _lastRestamp < TimeSpan.FromSeconds(15)) return;
        _lastRestamp = now;
        if (_boardRows.Count == 0) return;
        List<FlightRow> restamped = HomeController.Restamp(_boardRows, _date.Value.Date, now);
        if (!SameBoardText(_boardRows, restamped)) BindBoard(restamped);   // rebuilding the grid is costly; skip when nothing visible changed
    }

    /// <summary>True when both lists show exactly the same text on the board, in the same order.</summary>
    private static bool SameBoardText(List<FlightRow> a, List<FlightRow> b)
    {
        if (a.Count != b.Count) return false;
        for (int i = 0; i < a.Count; i++)
        {
            FlightRow x = a[i], y = b[i];
            if (x.FlightNo != y.FlightNo || x.SeatsLeft != y.SeatsLeft || x.ScheduledText != y.ScheduledText
                || x.EstimatedText != y.EstimatedText || x.GateText != y.GateText || x.StatusText != y.StatusText
                || x.CountdownText != y.CountdownText) return false;
        }
        return true;
    }

    private Control BuildSectionTitle()
    {
        var bar = new Panel { Dock = DockStyle.Top, Height = D(44), BackColor = Color.Transparent };
        var title = new Label
        {
            Text = "Where will you go next?", Font = new Font("Segoe UI", 15f, FontStyle.Bold), ForeColor = Theme.TextPrimary,
            AutoSize = true, Location = new Point(D(6), D(10)), BackColor = Color.Transparent
        };
        _routeCount.Font = Theme.Small;
        _routeCount.ForeColor = Theme.TextSecondary;
        _routeCount.AutoSize = true;
        _routeCount.BackColor = Color.Transparent;
        _routeCount.Location = new Point(D(330), D(18));
        bar.Controls.Add(_routeCount);
        bar.Controls.Add(title);
        return bar;
    }

    private Control BuildFooter()
    {
        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = D(40), Padding = new Padding(D(34), D(10), D(20), 0),
            WrapContents = false, BackColor = Color.Transparent
        };

        Label Plain(string text) => new()
        {
            Text = text, Font = Theme.Small, ForeColor = Theme.TextSecondary, AutoSize = true,
            Margin = new Padding(0, 0, D(10), 0), BackColor = Color.Transparent
        };
        LinkLabel Link(string title) => new()
        {
            Text = title, Font = Theme.Small, AutoSize = true, Margin = new Padding(0, 0, D(10), 0), BackColor = Color.Transparent,
            LinkColor = Theme.TextSecondary, ActiveLinkColor = Theme.Accent, VisitedLinkColor = Theme.TextSecondary,
            LinkBehavior = LinkBehavior.HoverUnderline, Cursor = Cursors.Hand,
            AccessibleName = title, AccessibleRole = AccessibleRole.Link
        };

        footer.Controls.Add(Plain("© 2026 SkyWings Airline"));
        foreach (string title in new[] { "Flight policies", "Baggage guide", "Fare rules" })
        {
            footer.Controls.Add(Plain("·"));
            LinkLabel link = Link(title);
            link.LinkClicked += (_, _) => ShowPolicyInfo(title);
            footer.Controls.Add(link);
        }
        return footer;
    }

    private void ShowPolicyInfo(string title)
    {
        using var form = new PolicyInfoForm(title);
        form.ShowDialog(this);
    }

    // ------------------------------------------------------------------ data

    private void LoadDashboard()
    {
        try
        {
            Dashboard dashboard = _controller.LoadDashboard();

            _destinations.SuspendLayout();
            foreach (Control old in _destinations.Controls.Cast<Control>().ToList())
            {
                _destinations.Controls.Remove(old);
                old.Dispose();   // Clear() alone leaks the cards' window handles
            }
            foreach (RouteGroup route in dashboard.Routes)
                _destinations.Controls.Add(CreateDestinationCard(
                    new RouteSummary(route.Title, route.Subtitle, AccentFor(route.Title), route.Flights, route.RouteType)));
            _destinations.ResumeLayout();

            string? selected = (_to.SelectedItem as DestinationOption)?.Destination;   // null on the first load
            _loadingDashboard = true;   // refill the list once at the end, not once per change
            try
            {
                _to.Items.Clear();
                foreach (DestinationOption option in dashboard.Destinations) _to.Items.Add(option);
                if (_to.Items.Count > 0) _to.SelectedIndex = dashboard.DefaultDestinationIndex(selected);
            }
            finally { _loadingDashboard = false; }
            _routeCount.Text = dashboard.Summary;
            RefreshFlights();
        }
        catch (Exception ex)
        {
            _routeCount.Text = "Unable to load routes: " + ex.Message;
            AppLogger.Error("Dashboard", ex);
        }
    }

    private Control CreateDestinationCard(RouteSummary summary)
    {
        string from = summary.Flights.Count > 0 ? $"{summary.Flights.Select(f => f.Destination).Distinct().Count()} places · {summary.Flights.Count} flights · from PHP {summary.Flights.Min(f => f.BasePrice):N0}" : "No flights available";
        var card = new DestinationCard
        {
            Width = D(268), Height = D(166), Margin = new Padding(0, 0, D(10), 0),
            Title = summary.Title, Subtitle = summary.Subtitle, Meta = from, Accent = summary.Accent,
            AccessibleName = $"{summary.Title}: {from}"
        };
        card.Click += (_, _) => ShowFlightDetails(summary);
        return card;
    }

    private void ShowFlightDetails(RouteSummary summary)
    {
        using var form = new FlightDetailsForm(_store, summary.Title, summary.RouteType, summary.Flights);
        form.ShowDialog(this);
        LoadDashboard();
    }

    // ------------------------------------------------------------------ actions

    /// <summary>Starts a booking for the flight chosen in the list, with the date and cabin from the search bar.</summary>
    private void BookSelectedFlight(object? sender, EventArgs e)
    {
        if (!CanBookDate || SelectedFlight() is not { } row) return;
        if (row.SoldOut)
        {
            using var join = new JoinWaitlistForm(_store, row.Flight, _date.Value.Date, _cabin.Text);
            join.ShowDialog(this);
            RefreshFlights();
            return;
        }
        using var wizard = new BookingWizardForm(_store);
        wizard.Prefill(row.Flight.FlightNo, null, _date.Value.Date, _cabin.Text);
        wizard.ShowDialog(this);
        LoadDashboard();
    }

    private void OpenBooking(object? sender, EventArgs e)
    {
        using var wizard = new BookingWizardForm(_store);
        wizard.ShowDialog(this);
        LoadDashboard();
    }

    private void OpenAdmin(object? sender, EventArgs e)
    {
        // Already signed in: the staff window shows only the sections this person's role allows.
        using var admin = new AdminForm(new AdminService(_store, _session));
        admin.ShowDialog(this);
        if (admin.SignOutRequested) { SignOut("Signed out"); return; }
        LoadDashboard();
    }

    private void ShowBookingLookupMessage(object? sender, EventArgs e)
    {
        using var manage = new ManageBookingForm(_store);
        manage.ShowDialog(this);
        LoadDashboard();
    }
    private void ShowCheckIn(object? sender, EventArgs e)
    {
        using var checkIn = new CheckInForm(_store);
        checkIn.ShowDialog(this);
    }
    /// <summary>Waitlist lookup. A live offer opens the booking wizard on that flight, date and cabin, tied to the offer.</summary>
    private void ShowWaitlist(object? sender, EventArgs e)
    {
        using var status = new WaitlistStatusForm(_store);
        if (status.ShowDialog(this) != DialogResult.OK || status.OfferToBook is not { } offer) return;
        using var wizard = new BookingWizardForm(_store);
        wizard.UseWaitlistCode(offer.BookingReference);
        wizard.Prefill(offer.FlightNo, null, offer.FlightDate, offer.ClassName);
        wizard.ShowDialog(this);
        LoadDashboard();
    }
    private void ShowRewards(object? sender, EventArgs e) => MessageBox.Show(this, "Rewards information is coming soon.", "Rewards", MessageBoxButtons.OK, MessageBoxIcon.Information);
    private void ShowHelp(object? sender, EventArgs e)
    {
        using var help = new HelpForm(_session);
        help.ShowDialog(this);
    }

    // ------------------------------------------------------------------ artwork

    /// <summary>
    /// The brand kit's sage hero background with the logo mark and a faint wing watermark. Rendered once per size
    /// into a bitmap, so repainting the home screen is a single blit.
    /// </summary>
    private sealed class HeroArt : Control, ISelfStyled
    {
        private readonly BackdropCache _backdrop = new();

        public HeroArt()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            AccessibleName = "SkyWings brand artwork";
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (Width < 40 || Height < 40) return;
            using (var surface = new SolidBrush(Gfx.Surface(this))) e.Graphics.FillRectangle(surface, e.ClipRectangle);
            e.Graphics.DrawImageUnscaled(_backdrop.Get(Size, DeviceDpi, PaintBackdrop), 0, 0);
        }

        private void PaintBackdrop(Graphics g)
        {
            var r = new Rectangle(0, 0, Width, Height);
            using var clip = Gfx.RoundedRect(new RectangleF(0, 0, Width - 1, Height - 1), Gfx.S(this, 16f));
            g.SetClip(clip);
            BrandArt.FillHero(g, r, DeviceDpi);

            Bitmap? mark = Assets.Watermark(DeviceDpi);
            if (mark is not null)
            {
                int size = (int)(Height * 1.15f);
                BrandArt.DrawScaled(g, mark, new Rectangle(r.Right - size + Gfx.S(this, 30), r.Bottom - size + Gfx.S(this, 20), size, size));
            }

            int logo = Math.Min(Gfx.S(this, 96), Height - Gfx.S(this, 32));
            BrandArt.DrawScaled(g, Assets.Logo(LogoVariant.Green, DeviceDpi),
                new Rectangle((Width - logo) / 2, (Height - logo) / 2, logo, logo));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) _backdrop.Dispose();
            base.Dispose(disposing);
        }
    }
}
