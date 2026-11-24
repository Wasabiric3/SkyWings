using SkyWings.Controllers;
using SkyWings.UI;

namespace SkyWings;

/// <summary>
/// Moves the open booking's outbound leg to another flight or date on the same route and cabin. Shows each flight that day with
/// its seats left and what the change would cost, and asks for confirmation before anything is charged.
/// </summary>
public sealed class ChangeFlightForm : GlassForm
{
    private sealed record OptionRow(string Flight, string Departs, string Seats, string Cost, string Note);

    private readonly ManageBookingController _controller;
    private readonly DateTimePicker _date = new() { Format = DateTimePickerFormat.Long };
    private readonly DataGridView _grid = new() { Dock = DockStyle.Fill, AutoGenerateColumns = true };
    private readonly Label _summary = new() { AutoSize = false, Dock = DockStyle.Bottom, Font = Theme.Small };
    private readonly GlassButton _change = new() { Text = "Change flight", Enabled = false };
    private List<FlightChangeOption> _options = new();

    /// <summary>True when the flight was changed, so the caller refreshes its view.</summary>
    public bool Changed { get; private set; }

    public ChangeFlightForm(ManageBookingController controller)
    {
        _controller = controller;
        BookingRecord booking = controller.Current ?? throw new ArgumentException("No booking is open.", nameof(controller));
        Text = $"Change flight · {booking.Reference}";
        StartPosition = FormStartPosition.CenterParent;
        SizeForScreen(860, 600, 720, 480);
        int D(int px) => Gfx.S(this, px);
        Padding = new Padding(D(20), D(12), D(20), D(16));

        var header = new PageHeader { Title = "Change flight", Hint = "Move this booking to another flight on the same route." };
        var current = new Label
        {
            Text = $"Now: {booking.FlightNo} · {booking.Origin} → {booking.Destination} · {booking.FlightDateValue:ddd, MMM d, yyyy} · {booking.ClassName} · " +
                   $"{booking.Passengers.Count} traveller(s)",
            AutoSize = false, Height = D(26), Font = Theme.Small
        };
        var policy = new Label
        {
            Text = $"Same route and cabin. Charged: any fare increase plus tax, and PHP {FlightChangeRules.FeePerPassenger:N0} per traveller. " +
                   "A cheaper flight is not refunded. Seats are assigned automatically; use Change seats afterwards to pick others.",
            AutoSize = false, Height = D(44), Font = Theme.Small
        };

        _date.MinDate = DateTime.Today;
        _date.Value = booking.FlightDateValue < DateTime.Today ? DateTime.Today : booking.FlightDateValue;
        _date.Width = D(240);
        _date.ValueChanged += (_, _) => LoadOptions();
        var dateRow = new FieldRow("NEW DATE", _date);

        GridStyle.Apply(_grid);
        _grid.DefaultCellStyle.Font = Theme.Small;
        _grid.DefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
        _grid.SelectionChanged += (_, _) => UpdateSelection();
        _grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0 && _change.Enabled) Confirm(); };
        _grid.DataBindingComplete += (_, _) =>
        {
            var weights = new Dictionary<string, float> { ["Flight"] = 0.8f, ["Departs"] = 1f, ["Seats"] = 0.8f, ["Cost"] = 2.4f, ["Note"] = 1.6f };
            foreach (DataGridViewColumn column in _grid.Columns)
                if (weights.TryGetValue(column.DataPropertyName, out float w)) column.FillWeight = w * 100;
        };

        _summary.Height = D(44);
        _change.Click += (_, _) => Confirm();
        var close = new GlassButton { Text = "Close", Style = GlassButtonStyle.Secondary, DialogResult = DialogResult.Cancel };
        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = D(54), FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false, Padding = new Padding(0, D(8), 0, 0)
        };
        footer.Controls.Add(_change);
        footer.Controls.Add(close);

        var card = new GlassCard { Dock = DockStyle.Fill, ContentPadding = 16 };
        card.Controls.Add(_grid);
        card.Controls.Add(_summary);
        card.Controls.Add(footer);
        var top = new Panel { Dock = DockStyle.Top };
        top.Height = Layouts.Stack(top, current, policy, Layouts.Row(D(14), dateRow));
        card.Controls.Add(top);
        Controls.Add(card);
        Controls.Add(header);   // added last, so it docks above the card

        Theme.Apply(this);
        CancelButton = close;
        current.ForeColor = Theme.TextPrimary;
        policy.ForeColor = Theme.TextSecondary;
        _summary.ForeColor = Theme.TextSecondary;
        Shown += (_, _) => LoadOptions();
    }

    private void LoadOptions()
    {
        try
        {
            _options = _controller.ChangeOptions(_date.Value.Date, DateTime.Now);
        }
        catch (IOException ex)
        {
            AppLogger.Error("ChangeFlight", ex);
            _options = new List<FlightChangeOption>();
            _summary.Text = "The flights could not be loaded. Please try again.";
        }
        _grid.DataSource = _options.Select(o => new OptionRow(o.Flight.FlightNo, o.Departure.ToString("h:mm tt"),
            o.SeatsLeft.ToString(), o.Possible ? o.Quote.Summary : "", o.Problem ?? "")).ToList();
        if (_options.Count == 0) _summary.Text = "No other flight runs on this route.";
        UpdateSelection();
    }

    private FlightChangeOption? SelectedOption()
        => _grid.CurrentRow is { Index: >= 0 } row && row.Index < _options.Count ? _options[row.Index] : null;

    private void UpdateSelection()
    {
        FlightChangeOption? option = SelectedOption();
        _change.Enabled = option is { Possible: true };
        if (option is null) return;
        _summary.Text = option.Possible ? option.Quote.Summary : option.Problem ?? "";
    }

    private void Confirm()
    {
        if (SelectedOption() is not { Possible: true } option) return;
        DateTime date = _date.Value.Date;
        string message = $"Move {_controller.Current!.Reference} to {option.Flight.FlightNo} on {date:ddd, MMM d, yyyy} at {option.Departure:h:mm tt}?\n\n" +
                         option.Quote.Summary + "\n\nThe amount is charged to the card or account used for the booking.";
        if (MessageBox.Show(this, message, "Change flight", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

        FlightChangeResult result = _controller.ChangeFlight(option, date);
        if (result.Outcome == FlightChangeOutcome.Changed)
        {
            Changed = true;
            MessageBox.Show(this, $"{result.Message}\n\nNew seats: {string.Join(", ", result.NewSeats)}.", "Change flight",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            return;
        }
        MessageBox.Show(this, result.Message, "Change flight", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        LoadOptions();
    }
}
