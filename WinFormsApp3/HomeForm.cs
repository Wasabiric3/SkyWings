using System.Drawing.Drawing2D;
using SkyWings.Controllers;
using SkyWings.UI;

namespace SkyWings;

/// <summary>Home screen: hero, a working flight search, and region cards.</summary>
public partial class HomeForm : GlassForm
{
    private readonly BookingStore _store;
    private readonly FlowLayoutPanel _destinations = new();
    // The filter row above the board: region chips, a day selector and a search box (city or flight number).
    private readonly Dictionary<string, GlassButton> _regionChips = new();
    private string _region = "";   // "" is every region
    private readonly DateField _day = new() { CustomFormat = "ddd, MMM d", MinDate = DateTime.Today };
    private readonly GlassButton _dayPrev = new() { Text = "‹", Style = GlassButtonStyle.Outline };
    private readonly GlassButton _dayNext = new() { Text = "›", Style = GlassButtonStyle.Outline };
    private readonly GlassButton _dayToday = new() { Text = "Today", Style = GlassButtonStyle.Ghost };
    private readonly TextBox _boardSearch = new() { PlaceholderText = "Search flights" };
    private readonly Label _routeCount = new();
    private readonly FlightDetailsPanel _detailsPanel = new();                 // beside the board: the selected flight's status, timeline, cabins and the one booking button
    private readonly Dictionary<string, int> _chosenCabin = new();             // the cabin picked for each flight and day, kept while the board refreshes
    private FlightDetails? _shownDetails;
    private BookingAction? _shownAction;
    private bool _binding;                                                     // true while the board is being filled: selection events are ignored
    private Panel? _content, _boardArea;

    /// <summary>For tests: when set, the booking button reports (what, flight, day, cabin) here instead of opening a window.</summary>
    internal Action<BookAction, string, DateTime, string>? StartHook;
    private readonly GlassButton _bookButton = new();
    private readonly DataGridView _flights = new();
    private readonly Label _flightSummary = new();
    private readonly Label _clock = new();
    private readonly Label _masterTime = new(), _masterDate = new();
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
        SizeForScreen(1320, 760, 1060, 560);
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
        var content = _content = new Panel
        {
            Dock = DockStyle.Fill, AutoScroll = true,
            Padding = new Padding(D(28), D(2), D(28), D(10)), BackColor = Color.Transparent
        };

        Control hero = BuildHero();
        Control filters = BuildFilterRow();
        Control flightList = BuildBoardArea();
        Control sectionTitle = BuildSectionTitle();
        _destinations.Dock = DockStyle.Top;
        _destinations.Height = D(218);
        _destinations.WrapContents = false;
        _destinations.AutoScroll = false;
        _destinations.BackColor = Color.Transparent;

        // Docked controls stack in reverse add order.
        content.Controls.Add(_destinations);
        content.Controls.Add(sectionTitle);
        content.Controls.Add(flightList);
        content.Controls.Add(Spacer(D(10)));
        content.Controls.Add(filters);
        content.Controls.Add(hero);
        // The filter row grows a second line on a narrow window, so the scrollable height follows what is really there.
        void FitScroll() => content.AutoScrollMinSize = new Size(0,
            hero.Height + filters.Height + flightList.Height + D(10) + sectionTitle.Height + _destinations.Height + D(16));
        FitScroll();
        filters.SizeChanged += (_, _) => FitScroll();

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
        links.Controls.Add(Link("Check-in", ShowCheckIn));
        links.Controls.Add(Link("Waitlist", ShowWaitlist));
        links.Controls.Add(Link("Manage booking", ShowBookingLookupMessage));
        // Each button sizes itself to its text (the sign-out one includes the staff name), so a fixed width would cut off the
        // leftmost link. Make the menu exactly as wide as its buttons.
        links.Width = links.Padding.Horizontal + links.Controls.Cast<Control>().Sum(c => c.Width + c.Margin.Horizontal);

        // Master clock in the empty middle of the header. It fills the space left of the menu; the left padding keeps it clear of the
        // brand and tagline, which sit on top of this panel.
        _masterTime.Font = Theme.Title;
        _masterTime.ForeColor = Theme.Brand;
        _masterDate.Font = Theme.Small;
        _masterDate.ForeColor = Theme.TextSecondary;
        foreach (var label in new[] { _masterTime, _masterDate })
        {
            label.AutoSize = false;
            label.Dock = DockStyle.Top;
            label.TextAlign = ContentAlignment.MiddleCenter;
            label.BackColor = Color.Transparent;
        }
        _masterTime.Height = D(34);
        _masterDate.Height = D(20);
        var masterClock = new Panel
        {
            Dock = DockStyle.Fill, Padding = new Padding(D(300), D(2), 0, 0), BackColor = Color.Transparent,
            AccessibleName = "Master clock", AccessibleRole = AccessibleRole.Clock
        };
        masterClock.Controls.Add(_masterDate);   // docked in reverse order: the time goes on top
        masterClock.Controls.Add(_masterTime);
        UpdateMasterClock(DateTime.Now);

        header.Controls.Add(masterClock);
        header.Controls.Add(links);
        header.Controls.Add(tag);
        header.Controls.Add(brand);
        // The clock panel fills the header (docking needs it first), so it sits in front of these two: bring them back on top.
        // They are not docked, so this does not change the layout.
        tag.BringToFront();
        brand.BringToFront();
        return header;
    }

    private Control BuildHero()
    {
        var hero = new GlassCard { Dock = DockStyle.Top, Height = D(216), ContentPadding = 8 };

        var art = new HeroArt { Dock = DockStyle.Right, Width = D(420) };
        var text = new Panel { Dock = DockStyle.Fill, Padding = new Padding(D(24), D(14), D(8), D(10)), BackColor = Color.Transparent };

        var cta = _bookButton;
        cta.Text = "Book a flight  →";
        cta.Height = D(44);
        cta.Click += OpenBooking;

        // Each label starts at the previous one's bottom edge so transparent labels never overlap.
        var eyebrow = new Label
        {
            Text = "YOUR NEXT CHAPTER STARTS HERE", Font = new Font(Theme.Small, FontStyle.Bold), ForeColor = Theme.Brand,
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

    /// <summary>
    /// The row above the board: region chips, a day selector (previous day, the day, next day, Today) and a search box. Any change refreshes the
    /// board straight away. On a narrow window the controls wrap onto a second line.
    /// </summary>
    private Control BuildFilterRow()
    {
        var card = new GlassCard { Dock = DockStyle.Top, Height = D(76), ContentPadding = 10 };
        var flow = new FlowLayoutPanel { WrapContents = true, AutoScroll = false, BackColor = Color.Transparent };   // sized by FitHeight below, not docked

        foreach (string region in new[] { "" }.Concat(FlightRegions.Names))
        {
            string name = region;
            var chip = new GlassButton { Text = name.Length == 0 ? "All" : name, Style = GlassButtonStyle.Outline, Height = D(40), Margin = new Padding(0, D(2), D(6), D(2)) };
            chip.Width = Math.Max(D(64), TextRenderer.MeasureText(chip.Text, chip.Font).Width + D(30));
            chip.Click += (_, _) => SetRegion(name);
            _regionChips[name] = chip;
            flow.Controls.Add(chip);
        }

        foreach (GlassButton arrow in new[] { _dayPrev, _dayNext })
        {
            arrow.Width = D(40); arrow.Height = D(40); arrow.Margin = new Padding(D(8), D(2), D(2), D(2));
        }
        _dayNext.Margin = new Padding(D(2), D(2), D(4), D(2));
        _dayToday.Width = D(70); _dayToday.Height = D(40); _dayToday.Margin = new Padding(0, D(2), D(14), D(2));
        _day.Width = D(158);
        var dayBox = new RoundedInput(_day) { Width = D(158), Margin = new Padding(0, D(2), 0, D(2)) };
        _boardSearch.Width = D(160);
        var searchBox = new RoundedInput(_boardSearch) { Width = D(160), Margin = new Padding(0, D(2), 0, D(2)) };
        flow.Controls.Add(_dayPrev);
        flow.Controls.Add(dayBox);
        flow.Controls.Add(_dayNext);
        flow.Controls.Add(_dayToday);   // shown only when the day is not today, so the row stays on one line
        flow.Controls.Add(searchBox);
        card.Controls.Add(flow);

        _dayPrev.AccessibleName = "Previous day"; _dayNext.AccessibleName = "Next day"; _dayToday.AccessibleName = "Today";
        _dayPrev.Click += (_, _) => _day.Value = _day.Value.Date.AddDays(-1);
        _dayNext.Click += (_, _) => _day.Value = _day.Value.Date.AddDays(1);
        _dayToday.Click += (_, _) => _day.Value = DateTime.Today;   // read now: the window may have been open since yesterday
        _day.ValueChanged += (_, _) =>
        {
            _dayPrev.Enabled = _day.Value.Date > DateTime.Today;
            _dayToday.Visible = _day.Value.Date != DateTime.Today;
            RefreshFlights();
        };
        _dayPrev.Enabled = false;
        _dayToday.Visible = false;
        _boardSearch.TextChanged += (_, _) => RefreshFlights();
        SetChipStyles();

        // Taller when the controls wrap: the card is as high as its rows need, and the flow panel is given exactly that room.
        void FitHeight()
        {
            Rectangle rc = card.DisplayRectangle;
            int inner = Math.Max(D(60), rc.Width);
            int rows = flow.GetPreferredSize(new Size(inner, 0)).Height;
            flow.SetBounds(rc.Left, rc.Top, inner, rows);
            int wanted = rows + 2 * (D(card.ShadowSize) + D(card.ContentPadding));
            if (card.Height != wanted) card.Height = wanted;
        }
        card.SizeChanged += (_, _) => FitHeight();
        Shown += (_, _) => FitHeight();
        return card;
    }

    /// <summary>Chooses a region ("" for all): marks its chip and refreshes the board.</summary>
    private void SetRegion(string region)
    {
        _region = region;
        SetChipStyles();
        RefreshFlights();
    }

    private void SetChipStyles()
    {
        foreach ((string name, GlassButton chip) in _regionChips)
            chip.Style = name == _region ? GlassButtonStyle.Primary : GlassButtonStyle.Outline;
    }

    /// <summary>The live list of every flight that matches the fields above.</summary>
    /// <summary>The board on the left and the selected flight's details on the right.</summary>
    private Control BuildBoardArea()
    {
        // Tall enough for the panel with four cabins, a reason line and the button (a wide-body flight), so nothing in it ever overlaps.
        var area = _boardArea = new Panel { Dock = DockStyle.Top, Height = Math.Max(D(476), _detailsPanel.RequiredHeight(4)), BackColor = Color.Transparent };
        Control board = BuildFlightList();
        var gap = new Panel { Dock = DockStyle.Right, Width = D(14), BackColor = Color.Transparent };
        _detailsPanel.Dock = DockStyle.Right;
        _detailsPanel.Width = D(340);
        _detailsPanel.CabinChosen += (_, index) =>
        {
            if (SelectedFlight() is { } row) _chosenCabin[CabinKey(row)] = index;
            UpdateDetails();
        };
        _detailsPanel.ActionClicked += (_, _) => StartBooking();
        // Docked controls stack in reverse add order: the panel takes the right edge, then the gap, and the board fills what is left.
        area.Controls.Add(board);
        area.Controls.Add(gap);
        area.Controls.Add(_detailsPanel);
        return area;
    }

    private string CabinKey(FlightRow row) => $"{row.FlightNo}|{_day.Value.Date:yyyyMMdd}";

    private Control BuildFlightList()
    {
        var card = new GlassCard { Dock = DockStyle.Fill, ContentPadding = 10 };

        _flightSummary.Font = Theme.Small;
        _flightSummary.ForeColor = Theme.TextSecondary;
        _flightSummary.AutoSize = false;
        _flightSummary.Dock = DockStyle.Fill;
        _flightSummary.TextAlign = ContentAlignment.MiddleLeft;
        _flightSummary.BackColor = Color.Transparent;

        // Header: what is shown on the left, a live clock on the right.
        _live.Text = "● LIVE";
        _live.Font = new Font(Theme.Small, FontStyle.Bold);
        _live.ForeColor = Theme.Brand;
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
        _flights.RowTemplate.Height = D(36);
        _flights.DefaultCellStyle.Font = Theme.Small;                         // the board shares the row with the details panel: a little smaller and tighter
        _flights.DefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
        _flights.ColumnHeadersDefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
        // Every flight on this board leaves Manila, so the destination alone is enough and leaves room for times and fares.
        AddFlightColumn("Flight", nameof(FlightRow.FlightNo), 0.95f);
        AddFlightColumn("To", nameof(FlightRow.DestinationText), 1.9f);
        AddFlightColumn("Departs", nameof(FlightRow.DepartsText), 1.9f);   // the scheduled time, with the estimate when the flight is late
        AddFlightColumn("Status", nameof(FlightRow.StatusText), 1.3f);
        AddFlightColumn("Countdown", nameof(FlightRow.CountdownText), 1.5f);
        AddFlightColumn("Fare", nameof(FlightRow.Price), 1.0f, right: true);
        AddFlightColumn("Seats left", nameof(FlightRow.SeatsLeft), 0.8f, right: true);
        _flights.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) StartBooking(); };
        _flights.CurrentCellChanged += (_, _) => { if (!_binding) UpdateDetails(); };   // not SelectionChanged: that fires before the current row has moved
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

    /// <summary>Text colours for the plain cells; dims flights that have left or can no longer be booked. The status is painted below.</summary>
    private void FormatBoardCell(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0 || _flights.Rows[e.RowIndex].DataBoundItem is not FlightRow row) return;
        string column = _flights.Columns[e.ColumnIndex].DataPropertyName;
        bool past = HasLeft(row) || !CanBook(row);   // also greyed: inside the 2-hour booking cutoff

        if (column == nameof(FlightRow.FlightNo))
        {
            e.CellStyle!.Font = StatusFont;
            e.CellStyle.ForeColor = past ? Theme.TextSecondary : Theme.Brand;
        }
        else if (column == nameof(FlightRow.DepartsText) && row.Status.DelayMinutes > 0)
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

    /// <summary>Draws the status as a coloured pill.</summary>
    private void PaintBoardCell(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0 || e.Graphics is null || _flights.Rows[e.RowIndex].DataBoundItem is not FlightRow row) return;
        string column = _flights.Columns[e.ColumnIndex].DataPropertyName;
        if (column != nameof(FlightRow.StatusText)) return;

        e.PaintBackground(e.ClipBounds, true);   // row colour, selection highlight and grid line as usual
        Graphics g = e.Graphics;
        GraphicsState state = g.Save();
        try
        {
            Gfx.HighQuality(g);
            PaintStatusPill(g, e.CellBounds, row);
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

    /// <summary>Whether a board row can still be booked: same day included, until the cutoff 2 hours before departure.</summary>
    private bool CanBook(FlightRow row) => HomeController.CanBook(row.FlightNo, _day.Value.Date, DateTime.Now);

    /// <summary>Reloads the board from the database (seats included) for the chosen region, search text and day.</summary>
    private void RefreshFlights()
    {
        if (_loadingDashboard) return;
        try
        {
            BindBoard(_controller.LoadBoard(_region, _boardSearch.Text, _day.Value.Date));
            _lastReload = _lastRestamp = DateTime.Now;
        }
        catch (IOException ex)
        {
            AppLogger.Error("FlightSearch", ex);
            _boardRows = new List<FlightRow>();
            _flights.DataSource = null;
            _flightSummary.Text = "The flights could not be loaded: " + ex.Message;
            UpdateDetails();
        }
    }

    private readonly SimulationService _simulation;
    private bool _reloading;

    /// <summary>
    /// The once-a-minute reload. The database work (seat counts for every flight, lapsed waitlist offers) runs on a
    /// worker thread so the window never freezes; only binding the finished rows happens on the UI thread.
    /// </summary>
    private async void ReloadBoardInBackground()
    {
        if (_reloading || _loadingDashboard) return;
        _reloading = true;
        string region = _region, text = _boardSearch.Text;
        DateTime date = _day.Value.Date;
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
                return _controller.LoadBoard(region, text, date);
            });
            // The filter row may have changed while loading; the next tick simply tries again.
            if (IsDisposed || _region != region || _boardSearch.Text != text || _day.Value.Date != date) return;
            BindBoard(rows);
            _lastReload = _lastRestamp = DateTime.Now;
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
        _binding = true;   // filling the grid fires selection events for rows that are about to change: update the panel once, at the end
        try
        {
            _flights.DataSource = rows;
            if (rows.Count > 0)
            {
                int index = keep is null ? 0 : Math.Max(0, rows.FindIndex(r => r.FlightNo == keep));
                _flights.CurrentCell = _flights[0, index];
                if (top >= 0 && top < rows.Count) _flights.FirstDisplayedScrollingRowIndex = top;
            }
        }
        finally { _binding = false; }
        UpdateSummary();
        UpdateDetails();
    }

    private void UpdateSummary()
    {
        string place = _region.Length == 0 ? "All flights from Manila" : $"{_region} flights from Manila";
        if (_boardSearch.Text.Trim().Length > 0) place += $"  ·  \"{_boardSearch.Text.Trim()}\"";
        string tail = _day.Value.Date == DateTime.Today
            ? $"Fares shown are Economy, one-way per traveller. Flights can be booked until {AppData.BookingCutoffHours} hours before departure."
            : "Fares shown are Economy, one-way per traveller. Pick a flight to see every cabin.";
        _flightSummary.Text = _boardRows.Count == 0
            ? $"{place}  ·  no flights match"
            : $"{place}  ·  {_boardRows.Count} flight(s)  ·  {_day.Value:ddd, MMM d}  ·  {tail}";
    }

    // ------------------------------------------------------------------ live board

    /// <summary>Runs every second: moves the clock, re-works statuses every 15 s and reloads seats every minute.</summary>
    private void Tick()
    {
        DateTime now = DateTime.Now;
        _clock.Text = now.ToString("ddd, MMM d  ·  h:mm:ss tt");
        UpdateMasterClock(now);
        if (_loadingDashboard) return;

        if (now - _lastReload >= TimeSpan.FromSeconds(60))
        {
            ReloadBoardInBackground();
            return;
        }
        if (now - _lastRestamp < TimeSpan.FromSeconds(15)) return;
        _lastRestamp = now;
        if (_boardRows.Count == 0) return;
        List<FlightRow> restamped = HomeController.Restamp(_boardRows, _day.Value.Date, now);
        if (!SameBoardText(_boardRows, restamped)) BindBoard(restamped);   // rebuilding the grid is costly; skip when nothing visible changed
    }

    /// <summary>Shows the same clock the board counts down from (this computer's local time).</summary>
    private void UpdateMasterClock(DateTime now)
    {
        _masterTime.Text = now.ToString("h:mm:ss tt");
        _masterDate.Text = $"{now:dddd, MMMM d, yyyy}  ·  Local time";
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
            LinkColor = Theme.TextSecondary, ActiveLinkColor = Theme.Brand, VisitedLinkColor = Theme.TextSecondary,
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
            Width = D(268), Height = D(212), Margin = new Padding(0, 0, D(10), 0),
            Title = summary.Title, Subtitle = summary.Subtitle, Meta = from, Accent = summary.Accent,
            AccessibleName = $"{summary.Title}: {from}"
        };
        card.Click += (_, _) =>
        {
            SetRegion(summary.Title);   // the board shows this region; its first flight is selected
            if (_content is not null && _boardArea is not null) _content.ScrollControlIntoView(_boardArea);
        };
        return card;
    }

    // ------------------------------------------------------------------ actions

    /// <summary>Shows the selected flight in the details panel (or "No flight selected"): its status, timeline, cabins and what the one booking button does.</summary>
    private void UpdateDetails()
    {
        FlightRow? row = SelectedFlight();
        if (row is null)
        {
            _shownDetails = null;
            _shownAction = new BookingAction(BookAction.None, "Book →", "Choose a flight.");
            _detailsPanel.ShowFlight(null, 0, _shownAction);
            return;
        }
        try
        {
            DateTime day = _day.Value.Date;
            FlightDetails details = _controller.Describe(row.Flight, day);
            int cabin = _chosenCabin.TryGetValue(CabinKey(row), out int chosen) && chosen >= 0 && chosen < details.Cabins.Count ? chosen : details.DefaultCabin;
            CabinOption? option = details.Cabins.Count > 0 ? details.Cabins[cabin] : null;
            _shownDetails = details;
            _shownAction = _controller.ActionFor(row.Flight, day, option);
            _detailsPanel.ShowFlight(details, cabin, _shownAction);
        }
        catch (IOException ex)
        {
            AppLogger.Error("FlightDetails", ex);
            _shownDetails = null;
            _shownAction = new BookingAction(BookAction.None, "Book →", "The flight could not be loaded.");
            _detailsPanel.ShowFlight(null, 0, _shownAction);
        }
    }

    /// <summary>The one booking button (and a double-click on a board row): books the selected flight in the chosen cabin, or joins that cabin's waitlist when it is sold out.</summary>
    private void StartBooking()
    {
        if (SelectedFlight() is not { } row || _shownDetails is not { } details || _shownAction is not { } action || action.Kind == BookAction.None) return;
        string cabin = details.Cabins[Math.Clamp(_detailsPanel.SelectedCabin, 0, details.Cabins.Count - 1)].ClassName;
        DateTime day = _day.Value.Date;
        if (StartHook is not null) { StartHook(action.Kind, row.FlightNo, day, cabin); return; }
        if (action.Kind == BookAction.JoinWaitlist)
        {
            using var join = new JoinWaitlistForm(_store, row.Flight, day, cabin);
            join.ShowDialog(this);
            RefreshFlights();
            return;
        }
        using var wizard = new BookingWizardForm(_store);
        wizard.Prefill(row.FlightNo, null, day, cabin);
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
        using var manage = new ManageBookingForm(_store, _session.Username);
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
            // The SkyWings airliner photograph, cropped to the slot with the plane kept in view.
            if (Assets.Photo("hero") is { } photo)
            {
                BrandArt.DrawCover(g, photo, new RectangleF(0, 0, Width, Height), Assets.PhotoFocus("hero"));
                return;
            }

            // No photograph (the file is missing): the brand art, as before.
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
