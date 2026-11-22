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
        "Where are you flying?", "Who is travelling?", "Choose your cabin", "Pick your seats", "Review and pay", "You're all set!"
    };
    private static readonly string[] StepHints =
    {
        "Choose your destination, travel date, and preferred flight.",
        "Enter the traveller details exactly as shown on a valid identification document.",
        "Select the level of comfort and travel add-ons for your flight. Prices are per traveller.",
        // These two state rules (the seat hold, the demo payment), so they keep their wording.
        "Select one seat per traveller. Seats are held for 10 minutes once you continue.",
        "This is a demo payment. Never enter real card details.",
        "Your booking is confirmed. Keep the receipt for your records."
    };
    /// <summary>Steps whose page already draws its own section cards; every other step still sits on one white card. Grows as the steps are restyled.</summary>
    private static readonly HashSet<int> OwnCards = new() { StepFlight, StepPassengers, StepOptions, StepSeats, StepPayment, StepReceipt };
    private static readonly string[] ClassDetails =
    {
        "Bags & meals extra", "1 bag · meal", "2 bags · meal", "3 bags · dining"
    };

    private readonly BookingWizardController _controller;
    private BookingSession _session => _controller.Session;

    // frame
    private readonly StepBar _stepBar = new(StepNames) { ShowLogo = true };
    private readonly PriceSummaryPanel _price = new() { Title = "Your trip" };
    private readonly Label _pageTitle = new();
    private readonly Label _pageHint = new();
    private readonly Panel _pageHost = new() { Dock = DockStyle.Fill };
    private readonly Panel[] _pages = new Panel[6];
    private readonly Control[] _pageRoots = new Control[6];   // the page itself, or the white card it sits on (see OwnCards)
    private readonly GlassButton _back = new() { Text = "← Back", Style = GlassButtonStyle.Outline, Pill = true };
    private readonly GlassButton _next = new() { Text = "Continue →", Pill = true };
    private readonly GlassButton _print = new() { Text = "Print receipt", Style = GlassButtonStyle.Outline, Pill = true, Visible = false };

    // step 1: flight
    private readonly FlightPicker _flight = new();
    // Same-day bookings are fine until the cutoff (AppData.IsBookable); the flight step checks the chosen flight.
    private readonly DateField _flightDate = new() { CustomFormat = AppData.DateInputFormat, MinDate = DateTime.Today };
    private readonly GlassCheckBox _roundTrip = new() { Text = "Add a return flight", AutoSize = false, Dock = DockStyle.Top };
    private readonly FlightPicker _returnFlight = new() { Enabled = false };
    private readonly DateField _returnDate = new() { CustomFormat = AppData.DateInputFormat, MinDate = DateTime.Today.AddDays(1), Enabled = false };
    private readonly InfoStrip _flightInfo = new() { Dock = DockStyle.Top };
    private FieldRow _flightRow = null!, _dateRow = null!, _returnFlightRow = null!, _returnDateRow = null!;

    // step 2: passengers
    private readonly TextBox _name = new() { MaxLength = BookingRules.MaxNameLength };
    private readonly TextBox _address = new() { MaxLength = BookingRules.MaxAddressLength };
    private readonly TextBox _contact = new() { MaxLength = BookingRules.MaxContactLength };
    private readonly TextBox _idNumber = new() { MaxLength = BookingRules.MaxIdNumberLength };
    private readonly TextBox _birthday = new() { PlaceholderText = "yyyy-MM-dd" };
    private readonly GlassCheckBox _senior = new() { Text = "Senior citizen (60+)", AutoSize = true };
    private readonly GlassCheckBox _pwd = new() { Text = "Person with disability (PWD)", AutoSize = true };
    private readonly ListBox _passengerList = new() { Dock = DockStyle.Top, BorderStyle = BorderStyle.None, IntegralHeight = false, DrawMode = DrawMode.OwnerDrawFixed };
    private readonly Label _travellerHeading = new() { Text = "TRAVELLERS (0)", AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly TextBox _email = new() { MaxLength = BookingRules.MaxEmailLength, PlaceholderText = "name@example.com" };
    private FieldRow _nameRow = null!, _addressRow = null!, _contactRow = null!, _idRow = null!, _birthdayRow = null!, _emailRow = null!;
    private readonly GlassButton _addTraveller = new() { Text = "+ Add traveller", Pill = true };

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
    private readonly GlassButton _seatTabOut = new() { Text = "Outbound", Style = GlassButtonStyle.Primary, Pill = true };
    private readonly GlassButton _seatTabBack = new() { Text = "Return", Style = GlassButtonStyle.Outline, Pill = true };
    private readonly GlassCard _seatCard = new() { ContentPadding = 16, Dock = DockStyle.Fill };   // its heading is the aircraft summary
    private readonly Label _seatInfo = new() { AutoSize = false, Dock = DockStyle.Top };
    private readonly Label _seatAircraft = new() { AutoSize = false, Dock = DockStyle.Top };   // "Airbus A320 · Economy cabin 3-3 · 28 rows"
    private string _seatSignature = "";

    // step 5: payment
    private readonly ComboBox _paymentMethod = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    // Shown as dots, like a password, so card digits are not readable over someone's shoulder.
    private readonly TextBox _account = new() { UseSystemPasswordChar = true, MaxLength = 40 };
    private readonly Label _review = new() { AutoSize = false, Dock = DockStyle.Top };   // not shown; its text feeds the confirmation dialog
    private readonly SummaryList _reviewList = new() { GrowToFit = true };
    private FieldRow _methodRow = null!, _accountRow = null!;

    // step 6: receipt
    private readonly ReceiptPanel _receipt = new() { Dock = DockStyle.Top, BandColor = Theme.Brand };
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
        SizeForScreen(1280, 800, 960, 580);
        Padding = new Padding(D(24), 0, D(24), 0);
        ApplyMetrics();

        BuildFrame();
        _pages[StepFlight] = BuildFlightPage();
        _pages[StepPassengers] = BuildPassengerPage();
        _pages[StepOptions] = BuildOptionsPage();
        _pages[StepSeats] = BuildSeatPage();
        _pages[StepPayment] = BuildPaymentPage();
        _pages[StepReceipt] = BuildReceiptPage();
        for (int i = 0; i < _pages.Length; i++)
        {
            _pageRoots[i] = PageRoot(i);
            _pageHost.Controls.Add(_pageRoots[i]);
        }

        Theme.Apply(this);
        BackColor = Theme.PageBg;
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
        _seatInfo.Height = D(26);
        _seatAircraft.Height = D(24);
        _review.Height = D(150);
        _receipt.Height = D(330);
        _passengerList.ItemHeight = D(46);
        _passengerList.Height = D(60);   // its minimum: one row; it grows to fill the page
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

        _pageTitle.Font = Theme.Title;
        _pageTitle.ForeColor = Theme.Brand;
        _pageTitle.AutoSize = false;
        _pageTitle.Height = D(46);
        _pageTitle.Dock = DockStyle.Top;
        _pageTitle.TextAlign = ContentAlignment.MiddleLeft;

        _pageHint.Font = Theme.Small;
        _pageHint.ForeColor = Theme.TextSecondary;
        _pageHint.AutoSize = false;
        _pageHint.Height = D(30);
        _pageHint.Dock = DockStyle.Top;
        _pageHint.TextAlign = ContentAlignment.TopLeft;

        // Footer: a hairline, then Back (and Print receipt) at the left and the main action at the right.
        var footer = new Panel { Dock = DockStyle.Bottom, Height = D(78) };
        var buttons = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, D(18), 0, D(12)) };
        _next.Dock = DockStyle.Right;
        _back.Dock = DockStyle.Left;
        _print.Dock = DockStyle.Left;
        _next.Click += (_, _) => Next();
        _back.Click += (_, _) => { if (_stepIndex > 0) GoTo(_stepIndex - 1); else Close(); };
        _print.Click += (_, _) => PrintReceipt();
        // Docking is applied from the last-added control backwards, so Back ends up leftmost.
        buttons.Controls.Add(_next);
        buttons.Controls.Add(_print);
        buttons.Controls.Add(_back);
        footer.Controls.Add(buttons);
        footer.Controls.Add(new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Theme.Border });

        // The left column: title, hint and the page. The summary card sits to its right.
        var left = new Panel { Dock = DockStyle.Fill };
        left.Controls.Add(_pageHost);
        left.Controls.Add(_pageHint);
        left.Controls.Add(_pageTitle);

        var gap = new Panel { Dock = DockStyle.Right, Width = D(20) };
        _price.Dock = DockStyle.Right;
        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, D(14), 0, D(8)) };
        body.Controls.Add(left);
        body.Controls.Add(gap);
        body.Controls.Add(_price);

        Controls.Add(body);
        Controls.Add(footer);
        Controls.Add(_stepBar);
        AcceptButton = _next;
    }

    /// <summary>The control that goes into the page host for a step: the page itself, or a white card around it.</summary>
    private Control PageRoot(int step)
    {
        if (OwnCards.Contains(step)) return _pages[step];
        var card = new GlassCard { Dock = DockStyle.Fill, ContentPadding = 16, Visible = false };
        _pages[step].Visible = true;
        card.Controls.Add(_pages[step]);
        return card;
    }

    /// <summary>A step page that draws its own section card: the scrolling <paramref name="content"/> sits inside one white card (listed in <see cref="OwnCards"/>).</summary>
    private static Panel CardPage(Panel content, string heading = "")
    {
        content.Dock = DockStyle.Fill;
        content.Visible = true;
        var card = new GlassCard { Dock = DockStyle.Fill, ContentPadding = 16, Heading = heading };
        card.Controls.Add(content);
        var page = new Panel { Dock = DockStyle.Fill, Visible = false };
        page.Controls.Add(card);
        return page;
    }

    /// <summary>Sizes a footer button to its text (docked buttons keep the width they are given).</summary>
    private void FitButton(GlassButton button)
        => button.Width = Math.Max(D(150), TextRenderer.MeasureText(button.Text, button.Font).Width + D(60));

    private void StyleLabels()
    {
        _pageTitle.ForeColor = Theme.Brand;   // Theme.Apply sets every label to the body colour, so this is set after it
        _travellerHeading.Font = new Font(Theme.Small, FontStyle.Bold);
        _travellerHeading.ForeColor = Theme.Brand;
        _seatInfo.Font = new Font(Theme.Body, FontStyle.Bold);
        _seatInfo.ForeColor = Theme.TextPrimary;
        _seatAircraft.Font = Theme.Small;
        _seatAircraft.ForeColor = Theme.Accent;
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
        if (index >= 0 && index < _flight.Flights.Count) _flight.SelectedIndex = index;
        if (date is { } day && day.Date >= _flightDate.MinDate) _flightDate.Value = day.Date;
        if (!string.IsNullOrEmpty(className) && AppData.ClassNames.Contains(className)) SelectClass(className);
    }

    // ------------------------------------------------------------------ step 1: flight

    private Panel BuildFlightPage()
    {
        var page = NewPage();
        _flightRow = new FieldRow("FLIGHT", _flight);
        _dateRow = new FieldRow("DEPARTURE DATE", _flightDate);
        _returnFlightRow = new FieldRow("RETURN FLIGHT", _returnFlight);
        _returnDateRow = new FieldRow("RETURN DATE", _returnDate);

        _flight.SelectedIndexChanged += (_, _) => { UpdateFlightInfo(); UpdateReturnFlightChoice(); SyncSession(); };
        _flightDate.ValueChanged += (_, _) =>
        {
            _returnDate.MinDate = _flightDate.Value.Date.AddDays(1);
            UpdateFlightInfo();   // seats open (and "bookings closed") depend on the date
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
        return CardPage(page);
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
        _flight.SetFlights(_availableFlights);
        if (_flight.Flights.Count > 0) _flight.SelectedIndex = 0;
    }

    private void UpdateFlightInfo()
    {
        if (_flight.Selected is not { } flight) { _flightInfo.Text = ""; return; }
        _flightInfo.Text = _controller.DescribeFlight(flight, _flightDate.Value.Date, _selectedClass, DateTime.Now);
    }

    private void UpdateReturnFlightChoice()
    {
        _returnFlight.SetFlights(Array.Empty<Flight>());
        if (_flight.Selected is not { } outbound) return;
        _returnFlight.SetFlights(_availableFlights
            .Where(f => string.Equals(f.Origin, outbound.Destination, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(f.Destination, outbound.Origin, StringComparison.OrdinalIgnoreCase))
            .OrderBy(f => f.FlightNo).ToList());
        if (_returnFlight.Flights.Count > 0) _returnFlight.SelectedIndex = 0;
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

        var checks = new FlowLayoutPanel { Height = D(44), WrapContents = false, Padding = new Padding(0, D(8), 0, 0) };
        _senior.Margin = new Padding(0, 0, D(40), 0);
        _pwd.Margin = Padding.Empty;
        checks.Controls.Add(_senior);
        checks.Controls.Add(_pwd);

        // "TRAVELLERS (n)" at the left; Remove selected and + Add traveller at the right (the last one added docks furthest right).
        _addTraveller.Dock = DockStyle.Right;
        _addTraveller.Width = D(170);
        _addTraveller.Click += (_, _) => AddPassenger();
        var remove = new GlassButton { Text = "Remove selected", Style = GlassButtonStyle.Ghost, Pill = true, Dock = DockStyle.Right, Width = D(160) };
        remove.Click += (_, _) => RemoveSelectedPassenger();
        var travellerHeader = new Panel { Height = D(56), Padding = new Padding(0, D(8), 0, D(8)) };
        travellerHeader.Controls.Add(_travellerHeading);
        travellerHeader.Controls.Add(remove);
        travellerHeader.Controls.Add(_addTraveller);

        _passengerList.BackColor = Color.White;
        _passengerList.Font = Theme.Body;
        _passengerList.DrawItem += DrawTravellerItem;

        var divider = new Panel { Height = D(1), BackColor = Theme.Border };
        var form = NewPage();
        AddStack(form, Row(_nameRow, _idRow), Row(_birthdayRow, _contactRow), Row(_addressRow, _emailRow), checks,
            new Panel { Height = D(8) }, divider, travellerHeader, _passengerList);
        // The list takes whatever height is left (it was stacked first, so it docks last); the page only scrolls below its minimum.
        _passengerList.Dock = DockStyle.Fill;
        return CardPage(form);
    }

    private sealed record TravellerRow(int Number, string Name, string Detail)
    {
        public override string ToString() => $"{Name}  ·  {Detail}";
    }

    /// <summary>One traveller as a soft-green strip: their number, their name in bold and their age and discount tag muted.</summary>
    private void DrawTravellerItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || sender is not ListBox list || list.Items[e.Index] is not TravellerRow row) return;
        bool selected = (e.State & DrawItemState.Selected) != 0;
        using (var back = new SolidBrush(Color.White)) e.Graphics.FillRectangle(back, e.Bounds);
        Gfx.HighQuality(e.Graphics);
        var strip = new RectangleF(e.Bounds.X, e.Bounds.Y + D(3), e.Bounds.Width - 1, e.Bounds.Height - D(7));
        using (var path = Gfx.RoundedRect(strip, D(10)))
        {
            using (var fill = new SolidBrush(selected ? Gfx.Mix(Theme.BrandSoft, Theme.Brand, 0.12f) : Theme.BrandSoft)) e.Graphics.FillPath(fill, path);
            if (selected)
            {
                using var outline = new Pen(Theme.Brand, 1.5f);
                e.Graphics.DrawPath(outline, path);
            }
        }
        const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
        int x = (int)strip.X + D(16);
        int h = (int)strip.Height, y = (int)strip.Y;
        TextRenderer.DrawText(e.Graphics, row.Number.ToString("00"), Theme.Small, new Rectangle(x, y, D(30), h), Theme.TextSecondary, flags);
        x += D(38);
        int nameWidth = Math.Min(TextRenderer.MeasureText(row.Name, _boldFont, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width, (int)(strip.Width * 0.5f));
        TextRenderer.DrawText(e.Graphics, row.Name, _boldFont, new Rectangle(x, y, nameWidth, h), Theme.TextPrimary, flags);
        x += nameWidth + D(18);
        TextRenderer.DrawText(e.Graphics, row.Detail, Theme.Small, new Rectangle(x, y, Math.Max(0, (int)strip.Right - x - D(12)), h), Theme.TextSecondary, flags);
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
        int number = 0;
        foreach (var p in _session.Passengers)
        {
            string tag = p.Senior && p.Pwd ? " · Senior/PWD" : p.Senior ? " · Senior" : p.Pwd ? " · PWD" : "";
            _passengerList.Items.Add(new TravellerRow(++number, p.Name, $"Age {p.Age}{tag}"));
        }
        _travellerHeading.Text = $"TRAVELLERS ({number})";
    }

    // ------------------------------------------------------------------ step 3: class and extras

    private Panel BuildOptionsPage()
    {
        var page = NewPage();
        // Padded so the cards line up with the white card below (which draws its border a few pixels inside its bounds).
        var cards = new TableLayoutPanel { Dock = DockStyle.Top, Height = D(134), ColumnCount = 4, RowCount = 1, Padding = new Padding(D(8), 0, D(8), 0) };
        for (int i = 0; i < 4; i++)
        {
            cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            var card = new ChoiceCard { Dock = DockStyle.Fill, Margin = new Padding(0, 0, i < 3 ? D(14) : 0, D(14)) };
            card.Title = AppData.ClassNames[i];
            card.Detail = ClassDetails[i];
            card.AccessibleName = $"{AppData.ClassNames[i]} class";
            string className = AppData.ClassNames[i];
            card.Click += (_, _) => SelectClass(className);
            _classCards[i] = card;
            cards.Controls.Add(card, i, 0);
        }
        cards.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        _baggageRow = new FieldRow("BAGGAGE ALLOWANCE", _baggage);
        _mealRow = new FieldRow("MEAL PREFERENCE", _meal);
        _baggage.SelectedIndexChanged += (_, _) => SyncSession();
        _meal.SelectedIndexChanged += (_, _) => SyncSession();

        // Add-ons are plain checkboxes in one row: there are only a couple, so a list box wastes space.
        var addons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = D(48), WrapContents = false, Padding = new Padding(0, D(8), 0, 0) };
        var addonsTitle = new Label { Text = "ADD-ONS PER TRAVELLER", Font = new Font(Theme.Small, FontStyle.Bold),
            ForeColor = Theme.TextSecondary, AutoSize = true, Margin = new Padding(0, D(4), D(24), 0) };
        addons.Controls.Add(addonsTitle);
        foreach (string addon in AppData.OptionalAddonNames)
        {
            int index = AppData.GetAddonIndex(addon);
            string price = index >= 0 ? $"  + PHP {AppData.AddonPrices[index]:N0}" : "";
            var box = new GlassCheckBox { Text = addon + price, Tag = addon, AutoSize = true, Margin = new Padding(0, 0, D(32), 0) };
            box.CheckedChanged += (_, _) => SyncSession();
            _addonBoxes.Add(box);
            addons.Controls.Add(box);
        }

        _promoRow = new FieldRow("PROMO CODE", _promoCode);
        var apply = new GlassButton { Text = "Apply", Pill = true, Dock = DockStyle.Bottom, Height = D(44) };
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

        // The four cabin cards sit on the page; the rest of the options are in one white card below them.
        var stack = NewPage();
        AddStack(stack, Row(_baggageRow, _mealRow), addons, promo);
        stack.Dock = DockStyle.Fill;
        stack.Visible = true;
        var optionsCard = new GlassCard { Dock = DockStyle.Fill, ContentPadding = 16 };
        optionsCard.Controls.Add(stack);
        page.AutoScroll = false;
        page.Controls.Add(optionsCard);
        page.Controls.Add(cards);
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
                    ? $"PHP {perTraveller * AppData.ClassMultipliers[i]:N0}"
                    : $"×{AppData.ClassMultipliers[i]:0.0} base fare";
            _classCards[i].Detail = offered ? ClassDetails[i] : "";
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
        var page = new Panel { Dock = DockStyle.Fill, Visible = false };

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

        // Left: one card, titled with the aircraft summary, holding the Outbound/Return tabs (round trips) and the seat map.
        _seatCard.Controls.Add(_mapHost);
        _seatCard.Controls.Add(_seatTabs);

        // Right: the seats picked so far, and the colour key.
        _seatInfo.Dock = DockStyle.Fill;
        var selectedCard = new GlassCard { Dock = DockStyle.Fill, ContentPadding = 16, Heading = "Selected seats" };
        selectedCard.Controls.Add(_seatInfo);
        var legendCard = new GlassCard { Dock = DockStyle.Bottom, Height = D(140), ContentPadding = 16 };
        legendCard.Controls.Add(new SeatLegend { Grid = true, Dock = DockStyle.Fill });
        var side = new Panel { Dock = DockStyle.Right, Width = D(300), Padding = new Padding(D(8), 0, 0, 0) };
        side.Controls.Add(selectedCard);
        side.Controls.Add(legendCard);

        page.Controls.Add(_seatCard);
        page.Controls.Add(side);
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
        _seatTabOut.Style = returnLeg ? GlassButtonStyle.Outline : GlassButtonStyle.Primary;
        _seatTabBack.Style = returnLeg ? GlassButtonStyle.Primary : GlassButtonStyle.Outline;
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
        _seatAircraft.Text = _controller.AircraftSummary(s.SelectedFlight!, s.ReturnFlight);
        _seatCard.Heading = _seatAircraft.Text;
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
            text += Environment.NewLine + $"Return: {_returnMap.Selected.Count} of {need} selected" +
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
        var page = new Panel { Dock = DockStyle.Fill, Visible = false };
        _paymentMethod.Items.AddRange(new object[] { "Credit Card", "Debit Card", "E-Wallet" });
        _paymentMethod.SelectedIndex = 0;
        _methodRow = new FieldRow("PAYMENT METHOD", _paymentMethod);
        _accountRow = new FieldRow("CARD / ACCOUNT NUMBER (DEMO)", _account);
        ClearOnEdit(_accountRow);
        var note = new Label
        {
            Text = "Demo only: enter any 4 or more characters. Only the last four are kept, masked.",
            Font = Theme.Small, ForeColor = Theme.TextSecondary, AutoSize = false, Height = D(44)
        };
        var payFields = NewPage();
        AddStack(payFields, _methodRow, _accountRow, note);
        payFields.Dock = DockStyle.Fill;
        payFields.Visible = true;

        // An amber notice across the top, then two cards side by side: what is being booked, and how it is paid.
        var summaryCard = new GlassCard { Dock = DockStyle.Fill, ContentPadding = 16, Heading = "Booking summary", Margin = Padding.Empty };
        // The summary list is as tall as its rows and sits in a scrolling panel, so no row is ever dropped when the card is short.
        var summaryScroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        _reviewList.Dock = DockStyle.None;
        _reviewList.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
        _reviewList.Location = Point.Empty;
        summaryScroll.Controls.Add(_reviewList);
        summaryScroll.Resize += (_, _) => _reviewList.Width = summaryScroll.ClientSize.Width;
        summaryCard.Controls.Add(summaryScroll);
        var payCard = new GlassCard { Dock = DockStyle.Fill, ContentPadding = 16, Heading = "Payment details", Margin = Padding.Empty };
        payCard.Controls.Add(payFields);
        var cards = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = Padding.Empty };
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        cards.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        cards.Controls.Add(summaryCard, 0, 0);
        cards.Controls.Add(payCard, 1, 0);

        // Padded so the notice lines up with the cards, which draw their border a few pixels inside their bounds.
        var noticeHost = new Panel { Dock = DockStyle.Top, Height = D(54), Padding = new Padding(D(8), D(2), D(8), 0) };
        noticeHost.Controls.Add(new InfoStrip
        {
            Dock = DockStyle.Fill, FillColor = Theme.AmberSoft, TextColor = Theme.AmberText,
            Text = "DEMO ONLY — No real payment will be processed."
        });
        page.Controls.Add(cards);
        page.Controls.Add(noticeHost);
        return page;
    }

    private void UpdateReview()
    {
        _reviewList.SetRows(_controller.ReviewRows());
        _review.Text = _controller.BuildReviewText();   // the confirmation dialog still shows this text
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
        _pages[StepReceipt].AutoScrollMinSize = new Size(0, _receipt.Height + _emailStatus.Height);
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
        rows.Add(("Payment", $"{booking.PaymentMethod} {booking.MaskedAccount}"));
        _receiptRows = rows;
        // The total is shown in its own band under the details, so the card is a little taller than the rows need.
        _receipt.Height = D(100) + ((rows.Count + 1) / 2) * D(58) + D(84);
        _receipt.SetReceipt(booking.Reference, rows, $"PHP {booking.TotalAmount:N2}");
        _pages[StepReceipt].AutoScrollMinSize = new Size(0, _receipt.Height + _emailStatus.Height);   // room for the email note above, once it has arrived
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
        for (int i = 0; i < _pageRoots.Length; i++) _pageRoots[i].Visible = i == step;
        _stepBar.Current = step;
        _pageTitle.Text = StepTitles[step];
        _pageHint.Text = StepHints[step];
        _back.Visible = step < StepReceipt;
        _back.Text = step == 0 ? "← Back" : $"← Back to {StepNames[step - 1]}";
        _print.Visible = step == StepReceipt;
        _next.Text = step switch
        {
            StepPayment => "Confirm and pay →",
            StepReceipt => "Done →",
            _ => $"Continue to {StepNames[step + 1]} →"
        };
        foreach (GlassButton button in new[] { _back, _print, _next }) FitButton(button);
        if (step == StepPayment) UpdateReview();
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
                if (_controller.FlightDateProblem(_session.SelectedFlight, _session.FlightDate, DateTime.Now) is { } closed)
                {
                    _dateRow.SetMessage(closed);
                    return false;
                }
                _dateRow.ClearMessage();
                if (_roundTrip.Checked)
                {
                    if (_returnFlight.Selected is null)
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
        _session.SelectedFlight = _flight.Selected;
        _session.FlightDate = _flightDate.Value.Date;
        _session.ReturnFlight = _roundTrip.Checked ? _returnFlight.Selected : null;
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

    private static string Php(decimal value) => $"PHP {value:N2}";

    private void RefreshPrice()
    {
        var s = _session;
        var lines = new List<PriceLine>();
        if (s.SelectedFlight is { } flight)
        {
            lines.Add(new(flight.FlightNo, $"{flight.Origin} → {flight.Destination}", PriceLineKind.Hero));
            lines.Add(new("Departure", s.FlightDate.ToString("MMM d, yyyy"), PriceLineKind.Stat));
            if (s.ReturnFlight is { } back)
            {
                lines.Add(new(back.FlightNo, $"{back.Origin} → {back.Destination}", PriceLineKind.Hero));
                lines.Add(new("Return", s.ReturnDate?.ToString("MMM d, yyyy") ?? "—", PriceLineKind.Stat));
            }
        }

        lines.Add(new("Travellers", s.Passengers.Count switch { 0 => "Not added yet", 1 => "1 traveller", var n => $"{n} travellers" }, PriceLineKind.Stat));
        lines.Add(new("Cabin", s.ClassName, PriceLineKind.Stat));

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
