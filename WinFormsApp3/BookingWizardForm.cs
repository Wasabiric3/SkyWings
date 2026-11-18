using System.Drawing.Printing;
using SkyWings.Controllers;
using SkyWings.UI;

namespace SkyWings;

/// <summary>
/// Six-step booking flow: flight, passengers, class and extras, seats, payment, receipt.
/// A step bar on top, one glass card for the current step, and a live price summary on the right.
/// </summary>
public sealed class BookingWizardForm : GlassForm
{
    private const int StepFlight = 0, StepPassengers = 1, StepOptions = 2, StepSeats = 3, StepPayment = 4, StepReceipt = 5;
    private static readonly string[] StepNames = { "Flight", "Passengers", "Class & extras", "Seats", "Payment", "Receipt" };
    private static readonly string[] StepTitles =
    {
        "Where are you flying?", "Who is travelling?", "Choose your cabin", "Pick your seats", "Review and pay", "You're all set"
    };
    private static readonly string[] StepHints =
    {
        "Choose a flight and your travel dates.",
        "Add every traveller. Senior citizen discount requires age 60 or older.",
        "Class sets your baggage and meal options.",
        "Select one seat per traveller. Seats are held for 10 minutes once you continue.",
        "This is a demo payment. Never enter real card details.",
        "Your booking is confirmed."
    };
    private static readonly string[] ClassDetails =
    {
        "Bags & meals extra", "1 bag · meal", "2 bags · meal", "3 bags · dining"
    };

    private readonly BookingWizardController _controller;
    private BookingSession _session => _controller.Session;

    // frame
    private readonly StepBar _stepBar = new(StepNames);
    private readonly GlassCard _card = new() { ContentPadding = 22 };
    private readonly PriceSummaryPanel _price = new() { Title = "Your trip" };
    private readonly Label _pageTitle = new();
    private readonly Label _pageHint = new();
    private readonly Panel _pageHost = new() { Dock = DockStyle.Fill };
    private readonly Panel[] _pages = new Panel[6];
    private readonly GlassButton _back = new() { Text = "Back", Style = GlassButtonStyle.Secondary };
    private readonly GlassButton _next = new() { Text = "Continue" };
    private readonly GlassButton _print = new() { Text = "Print receipt", Style = GlassButtonStyle.Secondary, Visible = false };

    // step 1: flight
    private readonly ComboBox _flight = new() { DropDownStyle = ComboBoxStyle.DropDownList, DrawMode = DrawMode.OwnerDrawFixed, DropDownWidth = 560 };
    private readonly DateTimePicker _flightDate = new() { Format = DateTimePickerFormat.Custom, CustomFormat = AppData.DateInputFormat, MinDate = DateTime.Today.AddDays(1) };
    private readonly CheckBox _roundTrip = new() { Text = "Add a return flight", AutoSize = false, Dock = DockStyle.Top };
    private readonly ComboBox _returnFlight = new() { DropDownStyle = ComboBoxStyle.DropDownList, DrawMode = DrawMode.OwnerDrawFixed, Enabled = false };
    private readonly DateTimePicker _returnDate = new() { Format = DateTimePickerFormat.Custom, CustomFormat = AppData.DateInputFormat, MinDate = DateTime.Today.AddDays(2), Enabled = false };
    private readonly Label _flightInfo = new() { AutoSize = false, Dock = DockStyle.Top };
    private FieldRow _flightRow = null!, _dateRow = null!, _returnFlightRow = null!, _returnDateRow = null!;

    // step 2: passengers
    private readonly TextBox _name = new() { MaxLength = BookingRules.MaxNameLength };
    private readonly TextBox _address = new() { MaxLength = BookingRules.MaxAddressLength };
    private readonly TextBox _contact = new() { MaxLength = BookingRules.MaxContactLength };
    private readonly TextBox _idNumber = new() { MaxLength = BookingRules.MaxIdNumberLength };
    private readonly TextBox _birthday = new() { PlaceholderText = "yyyy-MM-dd" };
    private readonly CheckBox _senior = new() { Text = "Senior citizen (60+)", AutoSize = true };
    private readonly CheckBox _pwd = new() { Text = "Person with disability", AutoSize = true };
    private readonly ListBox _passengerList = new() { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, IntegralHeight = false };
    private readonly TextBox _email = new() { MaxLength = BookingRules.MaxEmailLength, PlaceholderText = "name@example.com" };
    private FieldRow _nameRow = null!, _addressRow = null!, _contactRow = null!, _idRow = null!, _birthdayRow = null!, _emailRow = null!;
    private readonly GlassButton _addTraveller = new() { Text = "Add traveller" };

    // step 3: class and extras
    private readonly ChoiceCard[] _classCards = new ChoiceCard[4];
    private string _selectedClass = AppData.ClassNames[0];
    private readonly ComboBox _baggage = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _meal = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly List<CheckBox> _addonBoxes = new();
    private readonly TextBox _promoCode = new() { CharacterCasing = CharacterCasing.Upper };
    private FieldRow _baggageRow = null!, _mealRow = null!, _promoRow = null!;
    private string _appliedPromo = "";

    // step 4: seats
    private readonly SeatMap _outboundMap = new();
    private readonly SeatMap _returnMap = new() { Visible = false };
    private readonly Panel _mapHost = new() { Dock = DockStyle.Fill, AutoScroll = true };
    private readonly FlowLayoutPanel _seatTabs = new();
    private readonly GlassButton _seatTabOut = new() { Text = "Outbound", Style = GlassButtonStyle.Primary };
    private readonly GlassButton _seatTabBack = new() { Text = "Return", Style = GlassButtonStyle.Secondary };
    private readonly Label _seatInfo = new() { AutoSize = false, Dock = DockStyle.Top };
    private string _seatSignature = "";

    // step 5: payment
    private readonly ComboBox _paymentMethod = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    // Shown as dots, like a password, so card digits are not readable over someone's shoulder.
    private readonly TextBox _account = new() { UseSystemPasswordChar = true, MaxLength = 40 };
    private readonly Label _review = new() { AutoSize = false, Dock = DockStyle.Top };
    private FieldRow _methodRow = null!, _accountRow = null!;

    // step 6: receipt
    private readonly ReceiptPanel _receipt = new() { Dock = DockStyle.Top };
    private List<(string Label, string Value)> _receiptRows = new();
    private BookingRecord? _receiptBooking;   // the stored booking the receipt page and the printout are built from
    private readonly Label _emailStatus = new() { AutoSize = false, Height = 0, Dock = DockStyle.Top, Font = Theme.Small };

    private readonly Font _boldFont = new(Theme.Body, FontStyle.Bold);
    private int _stepIndex;
    private bool _bookingSubmitted => _controller.BookingSubmitted;
    private List<Flight> _availableFlights = new();

    public BookingWizardForm(BookingStore store)
    {
        _controller = new BookingWizardController(store);
        Text = "New booking · SkyWings";
        StartPosition = FormStartPosition.CenterParent;
        SizeForScreen(1120, 660, 960, 580);
        Padding = new Padding(D(24), D(10), D(24), D(16));
        ApplyMetrics();

        BuildFrame();
        _pages[StepFlight] = BuildFlightPage();
        _pages[StepPassengers] = BuildPassengerPage();
        _pages[StepOptions] = BuildOptionsPage();
        _pages[StepSeats] = BuildSeatPage();
        _pages[StepPayment] = BuildPaymentPage();
        _pages[StepReceipt] = BuildReceiptPage();
        foreach (var page in _pages) _pageHost.Controls.Add(page);

        Theme.Apply(this);
        StyleLabels();
        LoadFlights();
        PopulateCabinOptions();
        SelectClass(_selectedClass);
        GoTo(StepFlight);

        FormClosing += (_, e) =>
        {
            if (_bookingSubmitted || _session.Passengers.Count == 0) return;
            if (MessageBox.Show(this, "Cancel this booking and discard the current details?", "Cancel booking",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) e.Cancel = true;
            else _controller.ReleaseSeatHolds();
        };
        FormClosed += (_, _) => { _controller.ReleaseSeatHolds(); _boldFont.Dispose(); };
    }

    // ------------------------------------------------------------------ frame

    /// <summary>Scales a 96-dpi design value to this display.</summary>
    private int D(int px) => Gfx.S(this, px);

    /// <summary>Sizes that cannot be set in field initializers because they depend on the display scale.</summary>
    private void ApplyMetrics()
    {
        _roundTrip.Height = D(34);
        _flightInfo.Height = D(26);
        _seatInfo.Height = D(26);
        _review.Height = D(150);
        _receipt.Height = D(330);
        _flight.DropDownWidth = D(560);
        _returnFlight.DropDownWidth = D(560);
        _flight.ItemHeight = D(46);
        _returnFlight.ItemHeight = D(46);
        _passengerList.ItemHeight = D(28);
        _price.Width = D(300);
    }

    private void BuildFrame()
    {
        _stepBar.Dock = DockStyle.Top;
        _stepBar.StepClicked += (_, index) =>
        {
            if (_bookingSubmitted || index >= _stepIndex) return;
            GoTo(index);
        };

        _pageTitle.Font = Theme.Header;
        _pageTitle.ForeColor = Theme.TextPrimary;
        _pageTitle.AutoSize = false;
        _pageTitle.Height = D(38);
        _pageTitle.Dock = DockStyle.Top;
        _pageTitle.TextAlign = ContentAlignment.MiddleLeft;

        _pageHint.Font = Theme.Small;
        _pageHint.ForeColor = Theme.TextSecondary;
        _pageHint.AutoSize = false;
        _pageHint.Height = D(26);
        _pageHint.Dock = DockStyle.Top;
        _pageHint.TextAlign = ContentAlignment.TopLeft;

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = D(54), FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false, Padding = new Padding(0, D(10), 0, 0)
        };
        _next.Margin = new Padding(D(10), 0, 0, 0);
        _next.Click += (_, _) => Next();
        _back.Click += (_, _) => { if (_stepIndex > 0) GoTo(_stepIndex - 1); };
        _print.Click += (_, _) => PrintReceipt();
        footer.Controls.Add(_next);
        footer.Controls.Add(_back);
        footer.Controls.Add(_print);

        // Fill first, then edges: docking is applied from the last-added control backwards.
        _card.Controls.Add(_pageHost);
        _card.Controls.Add(footer);
        _card.Controls.Add(_pageHint);
        _card.Controls.Add(_pageTitle);

        _card.ContentPadding = 16;
        var gap = new Panel { Dock = DockStyle.Right, Width = D(4) };
        _price.Dock = DockStyle.Right;
        _card.Dock = DockStyle.Fill;

        var body = new Panel { Dock = DockStyle.Fill };
        body.Controls.Add(_card);
        body.Controls.Add(gap);
        body.Controls.Add(_price);

        Controls.Add(body);
        Controls.Add(_stepBar);
        AcceptButton = _next;
    }

    private void StyleLabels()
    {
        _flightInfo.Font = Theme.Small;
        _flightInfo.ForeColor = Theme.Accent;
        _seatInfo.Font = Theme.Small;
        _seatInfo.ForeColor = Theme.TextSecondary;
        _review.Font = Theme.Body;
        _review.ForeColor = Theme.TextPrimary;
    }

    private static Panel NewPage() => new() { Dock = DockStyle.Fill, AutoScroll = true, Visible = false };

    /// <summary>Stacks controls top to bottom in argument order (docking uses reverse add order).</summary>
    private static void AddStack(Panel page, params Control[] controls)
    {
        int total = 0;
        foreach (var c in controls) { c.Dock = DockStyle.Top; total += c.Height; }
        for (int i = controls.Length - 1; i >= 0; i--) page.Controls.Add(controls[i]);
        for (int i = 0; i < controls.Length; i++) controls[i].TabIndex = i;   // Tab goes top to bottom, not in add order
        page.AutoScrollMinSize = new Size(0, total + 8);
    }

    /// <summary>Puts controls side by side in equal columns.</summary>
    private TableLayoutPanel Row(params Control[] columns)
    {
        var table = new TableLayoutPanel
        {
            Dock = DockStyle.Top, ColumnCount = columns.Length, RowCount = 1,
            Height = columns.Max(c => c.Height), Margin = Padding.Empty, Padding = Padding.Empty
        };
        for (int i = 0; i < columns.Length; i++)
        {
            table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / columns.Length));
            columns[i].Dock = DockStyle.Fill;
            columns[i].Margin = new Padding(0, 0, i < columns.Length - 1 ? D(14) : 0, 0);
            table.Controls.Add(columns[i], i, 0);
        }
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        return table;
    }

    private static void ClearOnEdit(FieldRow row)
    {
        if (row.Input is TextBox box) box.TextChanged += (_, _) => row.ClearMessage();
    }

    /// <summary>Pre-fills the first step, e.g. from the home screen search or a flight list.</summary>
    /// <summary>Books against a live waitlist offer; the offer is marked used when the booking is paid.</summary>
    public void UseWaitlistCode(string code) => _controller.Session.WaitlistCode = code;

    public void Prefill(string? flightNo, string? destination, DateTime? date, string? className)
    {
        int index = -1;
        if (!string.IsNullOrEmpty(flightNo))
            index = _availableFlights.FindIndex(f => f.FlightNo == flightNo);
        if (index < 0 && !string.IsNullOrEmpty(destination))
            index = _availableFlights.FindIndex(f => f.Destination == destination && f.Origin.StartsWith("Manila", StringComparison.Ordinal));
        if (index >= 0 && index < _flight.Items.Count) _flight.SelectedIndex = index;
        if (date is { } day && day.Date >= _flightDate.MinDate) _flightDate.Value = day.Date;
        if (!string.IsNullOrEmpty(className) && AppData.ClassNames.Contains(className)) SelectClass(className);
    }

    // ------------------------------------------------------------------ step 1: flight

    private Panel BuildFlightPage()
    {
        var page = NewPage();
        _flight.DrawItem += DrawFlightItem;
        _returnFlight.DrawItem += DrawFlightItem;
        _flightRow = new FieldRow("FLIGHT", _flight);
        _dateRow = new FieldRow("DEPARTURE DATE", _flightDate);
        _returnFlightRow = new FieldRow("RETURN FLIGHT", _returnFlight);
        _returnDateRow = new FieldRow("RETURN DATE", _returnDate);

        _flight.SelectedIndexChanged += (_, _) => { UpdateFlightInfo(); UpdateReturnFlightChoice(); SyncSession(); };
        _flightDate.ValueChanged += (_, _) =>
        {
            _returnDate.MinDate = _flightDate.Value.Date.AddDays(1);
            SyncSession();
        };
        _roundTrip.CheckedChanged += (_, _) =>
        {
            _returnFlight.Enabled = _roundTrip.Checked;
            _returnDate.Enabled = _roundTrip.Checked;
            _returnFlightRow.Visible = _roundTrip.Checked;   // no washed-out duplicate block until it is needed
            UpdateReturnFlightChoice();
            SyncSession();
        };
        _returnFlight.SelectedIndexChanged += (_, _) => SyncSession();
        _returnDate.ValueChanged += (_, _) => SyncSession();

        _returnFlightRow.Visible = _roundTrip.Checked;
        AddStack(page, _flightRow, _flightInfo, Row(_dateRow, _returnDateRow), _roundTrip, _returnFlightRow);
        return page;
    }

    private void DrawFlightItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || sender is not ComboBox combo || combo.Items[e.Index] is not Flight flight) return;
        // Owner-drawn boxes don't grey themselves out when disabled, so do it here (the return flight box before "Add a return flight" is ticked).
        bool disabled = !combo.Enabled;
        Color strongText = disabled ? Theme.DisabledText : Theme.TextPrimary;
        Color softText = disabled ? Theme.DisabledTextSoft : Theme.TextSecondary;
        Color priceText = disabled ? Theme.DisabledText : Theme.Accent;
        bool highlighted = !disabled && (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
        using (var back = new SolidBrush(disabled ? Theme.DisabledBack : highlighted ? Theme.Selection : Color.White))
            e.Graphics.FillRectangle(back, e.Bounds);

        string time = AppData.FlightScheduledTimes.TryGetValue(flight.FlightNo, out TimeOnly t) ? t.ToString("h:mm tt") : "";
        string price = $"PHP {flight.BasePrice:N0}";
        int pad = 12;
        Size priceSize = TextRenderer.MeasureText(price, _boldFont);
        var priceRect = new Rectangle(e.Bounds.Right - priceSize.Width - pad, e.Bounds.Y, priceSize.Width, e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, price, _boldFont, priceRect, priceText,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.NoPadding);

        int textW = e.Bounds.Width - priceSize.Width - pad * 3;
        var line1 = new Rectangle(e.Bounds.X + pad, e.Bounds.Y + 6, textW, e.Bounds.Height / 2 - 4);
        var line2 = new Rectangle(e.Bounds.X + pad, e.Bounds.Y + e.Bounds.Height / 2, textW, e.Bounds.Height / 2 - 4);
        TextRenderer.DrawText(e.Graphics, $"{flight.Origin} → {flight.Destination}", _boldFont, line1, strongText,
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(e.Graphics, $"{flight.FlightNo}" + (time.Length > 0 ? $"  ·  departs {time}" : ""), Theme.Small, line2,
            softText, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);
    }

    private void LoadFlights()
    {
        try
        {
            _availableFlights = _controller.LoadFlights();
        }
        catch (IOException ex)
        {
            ShowError("Unable to load flights", ex);
        }
        _flight.Items.AddRange(_availableFlights.Cast<object>().ToArray());
        if (_flight.Items.Count > 0) _flight.SelectedIndex = 0;
    }

    private void UpdateFlightInfo()
    {
        if (_flight.SelectedItem is not Flight flight) { _flightInfo.Text = ""; return; }
        _flightInfo.Text = _controller.DescribeFlight(flight);
    }

    private void UpdateReturnFlightChoice()
    {
        _returnFlight.Items.Clear();
        if (_flight.SelectedItem is not Flight outbound) return;
        foreach (var flight in _availableFlights
            .Where(f => string.Equals(f.Origin, outbound.Destination, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(f.Destination, outbound.Origin, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f.FlightNo))
            _returnFlight.Items.Add(flight);
        if (_returnFlight.Items.Count > 0) _returnFlight.SelectedIndex = 0;
    }

    // ------------------------------------------------------------------ step 2: passengers

    private Panel BuildPassengerPage()
    {
        var page = NewPage();
        _nameRow = new FieldRow("FULL NAME", _name);
        _idRow = new FieldRow("ID NUMBER", _idNumber);
        _birthdayRow = new FieldRow("BIRTHDAY", _birthday);
        _addressRow = new FieldRow("ADDRESS", _address);
        _contactRow = new FieldRow("CONTACT NUMBER", _contact);
        _emailRow = new FieldRow("EMAIL (OPTIONAL, FOR YOUR RECEIPT)", _email);
        foreach (var row in new[] { _nameRow, _idRow, _birthdayRow, _addressRow, _contactRow, _emailRow }) ClearOnEdit(row);
        _senior.CheckedChanged += (_, _) => _birthdayRow.ClearMessage();

        var checks = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, Padding = new Padding(0, D(6), 0, 0) };
        _senior.Margin = new Padding(0, 0, D(18), 0);
        checks.Controls.Add(_senior);
        checks.Controls.Add(_pwd);

        _addTraveller.Dock = DockStyle.Bottom;
        _addTraveller.Width = D(150);
        _addTraveller.Click += (_, _) => AddPassenger();
        var addHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 0, D(2)) };
        addHost.Controls.Add(_addTraveller);

        var actionRow = new TableLayoutPanel { Height = D(52), ColumnCount = 2, RowCount = 1, Margin = Padding.Empty };
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
        actionRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        actionRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        actionRow.Controls.Add(checks, 0, 0);
        actionRow.Controls.Add(addHost, 1, 0);

        var form = new Panel { Dock = DockStyle.Fill };
        AddStack(form, Row(_nameRow, _idRow), Row(_birthdayRow, _contactRow), Row(_addressRow, _emailRow), actionRow);

        var listTitle = new Label
        {
            Text = "TRAVELLERS", Font = new Font(Theme.Small, FontStyle.Bold), ForeColor = Theme.TextSecondary,
            AutoSize = false, Height = D(24), Dock = DockStyle.Top, TextAlign = ContentAlignment.BottomLeft
        };
        var remove = new GlassButton { Text = "Remove selected", Style = GlassButtonStyle.Ghost, Dock = DockStyle.Bottom };
        remove.Click += (_, _) => RemoveSelectedPassenger();
        _passengerList.BackColor = Color.White;
        _passengerList.Font = Theme.Body;
        var listHost = new Panel { Dock = DockStyle.Right, Width = D(250), Padding = new Padding(D(20), 0, 0, 0) };
        listHost.Controls.Add(_passengerList);
        listHost.Controls.Add(remove);
        listHost.Controls.Add(listTitle);

        page.Controls.Add(form);
        page.Controls.Add(listHost);
        page.AutoScroll = false;
        return page;
    }

    private void AddPassenger()
    {
        string name = _name.Text.Trim();
        string address = _address.Text.Trim();
        string contact = _contact.Text.Trim();
        string idNumber = _idNumber.Text.Trim();
        string birthdayText = _birthday.Text.Trim();
        string email = _email.Text.Trim();

        var result = _controller.AddPassenger(
            new PassengerInput(name, address, contact, idNumber, birthdayText, email, _senior.Checked, _pwd.Checked),
            DateTime.Today);
        if (result.General is not null) { _nameRow.SetMessage(result.General); return; }
        if (result.Name is not null) _nameRow.SetMessage(result.Name);
        if (result.IdNumber is not null) _idRow.SetMessage(result.IdNumber);
        if (result.Address is not null) _addressRow.SetMessage(result.Address);
        if (result.Contact is not null) _contactRow.SetMessage(result.Contact);
        if (result.Email is not null) _emailRow.SetMessage(result.Email);
        if (result.Birthday is not null) _birthdayRow.SetMessage(result.Birthday);
        if (!result.IsValid) return;

        // The receipt address stays in the box for the next traveller.
        _name.Clear(); _address.Clear(); _contact.Clear(); _idNumber.Clear(); _birthday.Clear();
        _senior.Checked = false; _pwd.Checked = false;
        RefreshPassengerList();
        _name.Focus();
        SyncSession();
    }

    private void RemoveSelectedPassenger()
    {
        if (!_controller.RemovePassenger(_passengerList.SelectedIndex)) return;
        RefreshPassengerList();
        SyncSession();
    }

    private void RefreshPassengerList()
    {
        _passengerList.Items.Clear();
        foreach (var p in _session.Passengers)
        {
            string tag = p.Senior && p.Pwd ? " · Senior/PWD" : p.Senior ? " · Senior" : p.Pwd ? " · PWD" : "";
            _passengerList.Items.Add($"{p.Name}  ·  {p.Age}{tag}");
        }
    }

    // ------------------------------------------------------------------ step 3: class and extras

    private Panel BuildOptionsPage()
    {
        var page = NewPage();
        var cards = new TableLayoutPanel { Dock = DockStyle.Top, Height = D(100), ColumnCount = 4, RowCount = 1 };
        for (int i = 0; i < 4; i++)
        {
            cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            var card = new ChoiceCard { Dock = DockStyle.Fill, Margin = new Padding(0, 0, i < 3 ? D(10) : 0, D(8)) };
            card.Title = AppData.ClassNames[i];
            card.Detail = ClassDetails[i];
            card.AccessibleName = $"{AppData.ClassNames[i]} class";
            string className = AppData.ClassNames[i];
            card.Click += (_, _) => SelectClass(className);
            _classCards[i] = card;
            cards.Controls.Add(card, i, 0);
        }
        cards.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _baggageRow = new FieldRow("BAGGAGE", _baggage);
        _mealRow = new FieldRow("MEAL", _meal);
        _baggage.SelectedIndexChanged += (_, _) => SyncSession();
        _meal.SelectedIndexChanged += (_, _) => SyncSession();

        // Add-ons are plain checkboxes in one row: there are only a couple, so a list box wastes space.
        var addons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = D(36), WrapContents = false, Padding = new Padding(0, D(4), 0, 0) };
        var addonsTitle = new Label { Text = "ADD-ONS PER TRAVELLER", Font = new Font(Theme.Small, FontStyle.Bold),
            ForeColor = Theme.TextSecondary, AutoSize = true, Margin = new Padding(0, D(6), D(16), 0) };
        addons.Controls.Add(addonsTitle);
        foreach (string addon in AppData.OptionalAddonNames)
        {
            int index = AppData.GetAddonIndex(addon);
            string price = index >= 0 ? $" (+PHP {AppData.AddonPrices[index]:N0})" : "";
            var box = new CheckBox { Text = addon + price, Tag = addon, AutoSize = true, Margin = new Padding(0, 0, D(18), 0) };
            box.CheckedChanged += (_, _) => SyncSession();
            _addonBoxes.Add(box);
            addons.Controls.Add(box);
        }

        _promoRow = new FieldRow("PROMO CODE", _promoCode);
        var apply = new GlassButton { Text = "Apply", Style = GlassButtonStyle.Secondary, Dock = DockStyle.Bottom };
        apply.Click += (_, _) => ApplyPromoCode();
        var applyHost = new Panel { Padding = new Padding(0, 0, 0, D(24)) };
        applyHost.Controls.Add(apply);
        ClearOnEdit(_promoRow);

        var promo = new TableLayoutPanel { Dock = DockStyle.Top, Height = _promoRow.Height, ColumnCount = 2, RowCount = 1 };
        promo.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        promo.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, D(120)));
        _promoRow.Dock = DockStyle.Fill;
        _promoRow.Margin = new Padding(0, 0, D(12), 0);
        applyHost.Dock = DockStyle.Fill;
        applyHost.Margin = Padding.Empty;
        promo.Controls.Add(_promoRow, 0, 0);
        promo.Controls.Add(applyHost, 1, 0);
        promo.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        AddStack(page, cards, Row(_baggageRow, _mealRow), addons, promo);
        return page;
    }

    private void SelectClass(string className)
    {
        _selectedClass = className;
        for (int i = 0; i < _classCards.Length; i++) _classCards[i].Selected = AppData.ClassNames[i] == className;
        PopulateCabinOptions();
        SyncSession();
    }

    private void PopulateCabinOptions()
    {
        _baggage.Items.Clear();
        _baggage.Items.AddRange(AppData.BaggageOptionsForClass(_selectedClass).Cast<object>().ToArray());
        if (_baggage.Items.Count > 0) _baggage.SelectedIndex = 0;
        _meal.Items.Clear();
        _meal.Items.AddRange(AppData.MealOptionsForClass(_selectedClass).Cast<object>().ToArray());
        if (_meal.Items.Count > 0) _meal.SelectedIndex = 0;
    }

    private void UpdateClassCards()
    {
        // Only the cabins the chosen aircraft (and the return aircraft, for a round trip) actually has can be picked.
        IReadOnlyList<string> available = _session.SelectedFlight is { } flight
            ? _controller.AvailableClasses(flight, _session.ReturnFlight)
            : AppData.ClassNames;
        if (available.Count > 0 && !available.Contains(_selectedClass))
        {
            _selectedClass = available[0];
            _session.ClassName = _selectedClass;
            PopulateCabinOptions();   // refills baggage and meal for the new class (re-enters here once, now valid)
        }
        for (int i = 0; i < _classCards.Length; i++)
        {
            bool offered = available.Contains(AppData.ClassNames[i]);
            decimal perTraveller = (_session.SelectedFlight?.BasePrice ?? 0m) + (_session.ReturnFlight?.BasePrice ?? 0m);
            _classCards[i].Enabled = offered;
            _classCards[i].Selected = AppData.ClassNames[i] == _selectedClass;
            _classCards[i].Subtitle = !offered
                ? "Not offered on this aircraft"
                : perTraveller > 0
                    ? $"PHP {perTraveller * AppData.ClassMultipliers[i]:N0} per traveller"
                    : $"×{AppData.ClassMultipliers[i]:0.0} base fare";
            _classCards[i].Invalidate();
        }
    }

    private void ApplyPromoCode()
    {
        _appliedPromo = _promoCode.Text.Trim();
        SyncSession();
        if (_appliedPromo.Length == 0) { _promoRow.ClearMessage(); return; }
        FareQuote quote = _session.Quote;
        _promoRow.SetMessage(quote.PromoMessage, isError: quote.PromoDiscount <= 0);
    }

    // ------------------------------------------------------------------ step 4: seats

    private Panel BuildSeatPage()
    {
        var page = NewPage();
        page.AutoScroll = false;

        _outboundMap.SelectionChanged += (_, _) => UpdateSeatInfo();
        _returnMap.SelectionChanged += (_, _) => UpdateSeatInfo();
        _mapHost.Controls.Add(_outboundMap);
        _mapHost.Controls.Add(_returnMap);
        _mapHost.Resize += (_, _) => CenterMaps();

        _seatTabOut.Click += (_, _) => ShowSeatLeg(returnLeg: false);
        _seatTabBack.Click += (_, _) => ShowSeatLeg(returnLeg: true);
        _seatTabs.Dock = DockStyle.Top;
        _seatTabs.Height = D(46);
        _seatTabs.WrapContents = false;
        _seatTabs.Padding = new Padding(0, D(2), 0, 0);
        _seatTabs.Visible = false;
        _seatTabs.Controls.Add(_seatTabOut);
        _seatTabs.Controls.Add(_seatTabBack);
        _seatTabOut.Visible = true;
        _seatTabBack.Visible = true;

        var legend = new SeatLegend { Dock = DockStyle.Top };
        page.Controls.Add(_mapHost);
        page.Controls.Add(_seatInfo);
        page.Controls.Add(legend);
        page.Controls.Add(_seatTabs);
        return page;
    }

    private void CenterMaps()
    {
        foreach (var map in new[] { _outboundMap, _returnMap })
            map.Left = Math.Max(0, (_mapHost.ClientSize.Width - map.Width) / 2);
    }

    private void ShowSeatLeg(bool returnLeg)
    {
        _outboundMap.Visible = !returnLeg;
        _returnMap.Visible = returnLeg;
        _seatTabOut.Style = returnLeg ? GlassButtonStyle.Secondary : GlassButtonStyle.Primary;
        _seatTabBack.Style = returnLeg ? GlassButtonStyle.Primary : GlassButtonStyle.Secondary;
        _mapHost.AutoScrollPosition = Point.Empty;
        CenterMaps();
    }

    private bool PrepareSeatStep()
    {
        var s = _session;
        if (s.SelectedFlight is null) return false;
        string signature = string.Join("|", s.SelectedFlight.FlightNo, s.FlightDate.Date, s.ReturnFlight?.FlightNo,
            s.ReturnDate?.Date, s.ClassName, s.Passengers.Count);
        bool rebuild = signature != _seatSignature;
        if (rebuild) _controller.ReleaseOwnHolds();

        try
        {
            _controller.PurgeExpiredHolds();
            ConfigureMap(_outboundMap, s.SelectedFlight, s.FlightDate, rebuild);
            bool hasReturn = s.ReturnFlight is not null && s.ReturnDate is not null;
            if (hasReturn) ConfigureMap(_returnMap, s.ReturnFlight!, s.ReturnDate!.Value, rebuild);
            _seatTabs.Visible = hasReturn;
            ShowSeatLeg(returnLeg: false);
        }
        catch (IOException ex)
        {
            ShowError("Unable to load the seat map", ex);
            return false;
        }
        _seatSignature = signature;
        UpdateSeatInfo();
        return true;
    }

    private void ConfigureMap(SeatMap map, Flight flight, DateTime date, bool rebuild)
    {
        string[] keep = rebuild ? Array.Empty<string>() : map.Selected.ToArray();
        if (rebuild)
        {
            map.Configure(_controller.SeatCabin(flight) ?? Aircraft.Legacy(AppData.DefaultDomesticCapacity).Cabin(_session.ClassName)!);
        }
        map.MaxSelection = Math.Max(1, _session.Passengers.Count);
        map.SetSelected(keep);
        map.SetUnavailable(_controller.TakenSeats(flight, date), _controller.SeatsHeldByOthers(flight, date));
    }

    private void UpdateSeatInfo()
    {
        int need = _session.Passengers.Count;
        string text = $"Outbound: {_outboundMap.Selected.Count} of {need} selected" +
                      (_outboundMap.Selected.Count > 0 ? $" ({string.Join(", ", _outboundMap.Selected)})" : "");
        if (_session.ReturnFlight is not null)
            text += $"   ·   Return: {_returnMap.Selected.Count} of {need} selected" +
                    (_returnMap.Selected.Count > 0 ? $" ({string.Join(", ", _returnMap.Selected)})" : "");
        _seatInfo.Text = text;
    }

    private bool HoldSelectedSeats()
    {
        SeatHoldResult result = _controller.HoldSeats(_outboundMap.Selected, _returnMap.Selected);
        switch (result.Outcome)
        {
            case SeatHoldOutcome.Held:
                return true;
            case SeatHoldOutcome.WrongSeatCount:
                MessageBox.Show(this, result.Message, "Seats", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return false;
            case SeatHoldOutcome.SeatTaken:
                MessageBox.Show(this, result.Message, "Seat unavailable", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _seatSignature = "";   // reload the map so the taken seat shows as unavailable
                PrepareSeatStep();
                return false;
            case SeatHoldOutcome.TooManyHolds:
                MessageBox.Show(this, result.Message, "Seats", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            default:
                ShowError("Unable to hold the selected seats", result.Error!);
                return false;
        }
    }

    // ------------------------------------------------------------------ step 5: payment

    private Panel BuildPaymentPage()
    {
        var page = NewPage();
        _paymentMethod.Items.AddRange(new object[] { "Credit Card", "Debit Card", "E-Wallet" });
        _paymentMethod.SelectedIndex = 0;
        _methodRow = new FieldRow("PAYMENT METHOD", _paymentMethod);
        _accountRow = new FieldRow("ACCOUNT OR CARD (DEMO)", _account);
        ClearOnEdit(_accountRow);
        var note = new Label
        {
            Text = "Demo only: enter any 4 or more characters. Only the last four are kept, masked.",
            Font = Theme.Small, ForeColor = Theme.TextSecondary, AutoSize = false, Height = D(28)
        };
        AddStack(page, _review, Row(_methodRow, _accountRow), note);
        return page;
    }

    private void UpdateReview()
    {
        _review.Text = _controller.BuildReviewText();
    }

    private bool ConfirmBooking()
    {
        if (_bookingSubmitted) return true;
        if (!_controller.IsReadyForPayment())
        {
            MessageBox.Show(this, "Complete the flight, traveller and seat steps before payment.", "Booking incomplete",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }
        string account = _account.Text.Trim();
        if (account.Length < 4)
        {
            _accountRow.SetMessage("Enter at least 4 characters (demo account).");
            _account.Focus();
            return false;
        }
        SyncSession();
        if (MessageBox.Show(this, $"Review and confirm this demo payment?\n\nTotal: PHP {_session.Total:N2}\n\n{_review.Text}",
            "Confirm booking", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return false;

        SubmitResult submit = _controller.SubmitBooking(_paymentMethod.Text, account);
        if (!submit.Succeeded)
        {
            MessageBox.Show(this, "The booking could not be saved, so no payment was recorded. Please try again.\n\n" + submit.Error!.Message,
                "Booking failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }

        _account.Clear();   // the card or account text is not needed any more; only the masked last four were kept
        BuildReceipt(submit.Booking!);
        _ = EmailReceiptAsync(submit.Booking!);
        return true;
    }

    /// <summary>Sends the receipt email after the booking is stored and shows the outcome as a note on the receipt page.</summary>
    private async Task EmailReceiptAsync(BookingRecord booking)
    {
        string note = await _controller.EmailReceiptAsync(booking);
        if (note.Length == 0 || IsDisposed) return;
        _emailStatus.Text = note;
        _emailStatus.ForeColor = Theme.TextSecondary;
        _emailStatus.Height = Gfx.S(this, 40);
    }

    // ------------------------------------------------------------------ step 6: receipt

    private Panel BuildReceiptPage()
    {
        var page = NewPage();
        AddStack(page, _emailStatus, _receipt);
        return page;
    }

    private void BuildReceipt(BookingRecord booking)
    {
        _receiptBooking = booking;
        var rows = new List<(string, string)>
        {
            ("Passengers", string.Join(", ", booking.Passengers.Select(p => p.Name))),
            ("Class", booking.ClassName),
            ("Outbound", $"{booking.FlightNo} · {booking.Origin} → {booking.Destination}"),
            ("Date", booking.FlightDate),
            ("Seats", string.Join(", ", booking.Passengers.Select(p => p.SeatCode).Where(c => c.Length > 0))),
            ("Departure", booking.RealtimeEst)
        };
        if (booking.ReturnFlightNo is not null)
        {
            rows.Add(("Return", $"{booking.ReturnFlightNo} · {booking.ReturnOrigin} → {booking.ReturnDestination}"));
            rows.Add(("Return date", $"{booking.ReturnFlightDateValue:MMM dd, yyyy}"));
            rows.Add(("Return seats", string.Join(", ", booking.Passengers.Select(p => p.ReturnSeatCode).Where(c => c.Length > 0))));
        }
        rows.Add(("Baggage", booking.BaggageOption));
        rows.Add(("Meal", booking.MealOption));
        rows.Add(("Total paid", $"PHP {booking.TotalAmount:N2}"));
        rows.Add(("Payment", $"{booking.PaymentMethod} {booking.MaskedAccount}"));
        _receiptRows = rows;
        _receipt.Height = D(100) + ((rows.Count + 1) / 2) * D(58);
        _receipt.SetReceipt(booking.Reference, rows);
        _pages[StepReceipt].AutoScrollMinSize = new Size(0, _receipt.Height + D(52));   // room for the email note above
    }

    private void PrintReceipt()
    {
        if (_receiptBooking is null) return;
        ReceiptPrinter.ShowPreview(this, ReceiptData.From(_receiptBooking, _session));
    }

    // ------------------------------------------------------------------ navigation

    private void GoTo(int step)
    {
        _stepIndex = step;
        for (int i = 0; i < _pages.Length; i++) _pages[i].Visible = i == step;
        _stepBar.Current = step;
        _pageTitle.Text = StepTitles[step];
        _pageHint.Text = StepHints[step];
        _back.Visible = step > 0 && step < StepReceipt;
        _print.Visible = step == StepReceipt;
        _next.Text = step switch
        {
            StepOptions => "Continue to seats",
            StepSeats => "Continue to payment",
            StepPayment => "Confirm and pay",
            StepReceipt => "Done",
            _ => "Continue"
        };
        if (step == StepPayment) UpdateReview();
        _next.Style = GlassButtonStyle.Primary;
        // Enter adds the traveller while typing on the passenger step; everywhere else it continues.
        AcceptButton = step == StepPassengers ? _addTraveller : _next;
        if (step == StepSeats) CenterMaps();
    }

    private void Next()
    {
        if (!TryLeave(_stepIndex)) return;
        if (_stepIndex == StepReceipt) { Close(); return; }
        int target = _stepIndex + 1;
        if (target == StepSeats && !PrepareSeatStep()) return;
        GoTo(target);
    }

    private bool TryLeave(int step)
    {
        switch (step)
        {
            case StepFlight:
                SyncSession();
                if (_session.SelectedFlight is null)
                {
                    _flightRow.SetMessage("Select a flight to continue.");
                    return false;
                }
                if (_roundTrip.Checked)
                {
                    if (_returnFlight.SelectedItem is not Flight)
                    {
                        _returnFlightRow.SetMessage("No return flight is available for this route.");
                        return false;
                    }
                    if (_returnDate.Value.Date <= _flightDate.Value.Date)
                    {
                        _returnDateRow.SetMessage("Choose a date after the departure date.");
                        return false;
                    }
                }
                _flightRow.ClearMessage();
                _returnFlightRow.ClearMessage();
                _returnDateRow.ClearMessage();
                return true;
            case StepPassengers:
                if (_session.Passengers.Count == 0)
                {
                    _nameRow.SetMessage("Add at least one traveller to continue.");
                    return false;
                }
                return true;
            case StepOptions:
                if (_promoCode.Text.Trim() != _appliedPromo) ApplyPromoCode();
                SyncSession();
                if (_appliedPromo.Length > 0 && _session.Quote.PromoDiscount <= 0)
                {
                    _promoRow.SetMessage(_session.Quote.PromoMessage);
                    return false;
                }
                return true;
            case StepSeats:
                return HoldSelectedSeats();
            case StepPayment:
                return ConfirmBooking();
            default:
                return true;
        }
    }

    // ------------------------------------------------------------------ live summary

    private void SyncSession()
    {
        _session.SelectedFlight = _flight.SelectedItem as Flight;
        _session.FlightDate = _flightDate.Value.Date;
        _session.ReturnFlight = _roundTrip.Checked ? _returnFlight.SelectedItem as Flight : null;
        _session.ReturnDate = _session.ReturnFlight is null ? null : _returnDate.Value.Date;
        _session.ClassName = _selectedClass;
        _session.BaggageOption = _baggage.SelectedItem?.ToString() ?? "No checked baggage included";
        _session.MealOption = _meal.SelectedItem?.ToString() ?? "No meal";
        _session.Addons.Clear();
        foreach (var box in _addonBoxes.Where(b => b.Checked)) _session.Addons.Add((string)box.Tag!);
        _session.PromoCode = _appliedPromo;
        UpdateClassCards();
        RefreshPrice();
    }

    private static string Code(string place)
    {
        int open = place.IndexOf('(');
        int close = place.IndexOf(')');
        return open >= 0 && close > open ? place[(open + 1)..close] : place;
    }

    private static string Php(decimal value) => $"PHP {value:N2}";

    private void RefreshPrice()
    {
        var s = _session;
        var lines = new List<PriceLine>();
        if (s.SelectedFlight is { } flight)
        {
            lines.Add(new("Outbound", "", PriceLineKind.Heading));
            lines.Add(new(flight.FlightNo, $"{Code(flight.Origin)} → {Code(flight.Destination)}"));
            lines.Add(new("Date", s.FlightDate.ToString("MMM d, yyyy")));
            if (s.ReturnFlight is { } back)
            {
                lines.Add(new("Return", "", PriceLineKind.Heading));
                lines.Add(new(back.FlightNo, $"{Code(back.Origin)} → {Code(back.Destination)}"));
                lines.Add(new("Date", s.ReturnDate?.ToString("MMM d, yyyy") ?? "—"));
            }
        }

        lines.Add(new("Travellers", "", PriceLineKind.Heading));
        lines.Add(new("Class", s.ClassName));
        lines.Add(new("Travellers", s.Passengers.Count == 0 ? "Not added yet" : s.Passengers.Count.ToString()));

        lines.Add(new("Fare", "", PriceLineKind.Heading));
        if (s.Passengers.Count == 0)
        {
            lines.Add(new("Per traveller", Php(s.BaseFarePerPassenger)));
            lines.Add(new("Add travellers to see your total.", "", PriceLineKind.Note));
        }
        else
        {
            FareQuote q = s.Quote;
            lines.Add(new("Base fare", Php(q.BaseFare)));
            if (q.SeasonalSurcharge > 0) lines.Add(new("Peak season", Php(q.SeasonalSurcharge)));
            if (q.Addons > 0) lines.Add(new("Baggage, meals, add-ons", Php(q.Addons)));
            if (q.AdvancePurchaseDiscount > 0) lines.Add(new("Advance purchase", "-" + Php(q.AdvancePurchaseDiscount), PriceLineKind.Discount));
            if (q.PassengerDiscount > 0) lines.Add(new("Senior / PWD", "-" + Php(q.PassengerDiscount), PriceLineKind.Discount));
            if (q.PromoDiscount > 0) lines.Add(new($"Promo {s.PromoCode}", "-" + Php(q.PromoDiscount), PriceLineKind.Discount));
            lines.Add(new("VAT 12%", Php(q.Tax)));
            lines.Add(new("Total", Php(q.Total), PriceLineKind.Total));
        }
        _price.SetLines(lines);
    }

    private void ShowError(string message, Exception ex)
    {
        AppLogger.Error("Wizard", ex);
        MessageBox.Show(this, $"{message}.\n\n{ex.Message}", "SkyWings", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}
