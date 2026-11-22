using SkyWings.UI;

namespace SkyWings;

public sealed class FlightEditorForm : GlassForm
{
    private readonly TextBox _flightNo = new();
    private readonly TextBox _origin = new();
    private readonly TextBox _destination = new();
    private readonly NumericUpDown _price = new() { DecimalPlaces = 2, Maximum = 1_000_000, Minimum = 0, Increment = 100, ThousandsSeparator = true };
    private readonly NumericUpDown _capacity = new() { Maximum = 100_000, Minimum = 1 };
    private readonly ComboBox _type = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly DateTimePicker _departure = new() { Format = DateTimePickerFormat.Custom, CustomFormat = "h:mm tt", ShowUpDown = true };
    private readonly CheckBox _active = new() { Text = "Available for new bookings", Checked = true, AutoSize = true };
    private readonly ComboBox _aircraft = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly FieldRow _flightNoRow, _originRow, _destinationRow, _priceRow, _capacityRow, _aircraftRow;
    private readonly string? _originalAircraftId;

    private sealed record AircraftChoice(string? Id, string Text, int Seats) { public override string ToString() => Text; }
    private readonly AdminService _service;
    private readonly bool _isNew;
    private readonly int _confirmedBookings;

    private FlightEditorForm(AdminService service, FlightAdminRow? flight, int confirmedBookings)
    {
        _service = service;
        _isNew = flight is null;
        _confirmedBookings = confirmedBookings;
        Text = flight is null ? "Add flight" : $"Edit {flight.FlightNo}";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.None;
        int D(int px) => Gfx.S(this, px);

        _flightNo.Text = flight?.FlightNo ?? "";
        _flightNo.ReadOnly = flight is not null;
        _origin.Text = flight?.Origin ?? "Manila (MNL)";
        _destination.Text = flight?.Destination ?? "";
        _price.Value = flight is null ? 0 : flight.BasePrice;
        _capacity.Value = flight is null ? 180 : Math.Max(1, flight.Capacity);
        _type.Items.AddRange(new object[] { "Domestic", "International" });
        _type.SelectedItem = flight?.Type ?? "Domestic";
        _departure.Value = DateTime.Today.AddHours(8);   // default for new flights
        if (flight is not null && DateTime.TryParse(flight.DepartureDisplay, out var departure)) _departure.Value = departure;
        _active.Checked = flight?.Status != "Inactive";

        // The aircraft decides the cabins and seat maps; with one chosen, the capacity is its seat count and cannot be typed.
        _aircraft.Items.Add(new AircraftChoice(null, "Standard layout (uses the capacity)", 0));
        foreach (Aircraft a in Aircraft.All) _aircraft.Items.Add(new AircraftChoice(a.Id, $"{a.Model} · {a.TotalSeats} seats", a.TotalSeats));
        _originalAircraftId = flight?.AircraftId;
        _aircraft.SelectedIndex = Math.Max(0, _aircraft.Items.Cast<AircraftChoice>().ToList().FindIndex(c => c.Id == _originalAircraftId));
        _aircraft.SelectedIndexChanged += (_, _) => { _aircraftRow.ClearMessage(); ApplyAircraftChoice(); };
        ApplyAircraftChoice();

        _flightNoRow = new FieldRow("FLIGHT NUMBER", _flightNo);
        _originRow = new FieldRow("ORIGIN", _origin);
        _destinationRow = new FieldRow("DESTINATION", _destination);
        _priceRow = new FieldRow("BASE FARE (PHP)", _price);
        _capacityRow = new FieldRow("CAPACITY (SEATS)", _capacity);
        _aircraftRow = new FieldRow("AIRCRAFT (SETS THE SEAT MAPS)", _aircraft);
        var typeRow = new FieldRow("TYPE", _type);
        var departureRow = new FieldRow("DEPARTURE TIME", _departure);
        foreach (var row in new[] { _flightNoRow, _originRow, _destinationRow })
            if (row.Input is TextBox box) box.TextChanged += (_, _) => row.ClearMessage();

        var activeHost = new Panel { Height = D(40), Padding = new Padding(0, D(8), 0, 0) };
        _active.Dock = DockStyle.Left;
        activeHost.Controls.Add(_active);

        var save = new GlassButton { Text = "Save flight" };
        save.Click += (_, _) => { if (ValidateInput()) { DialogResult = DialogResult.OK; Close(); } };
        var cancel = new GlassButton { Text = "Cancel", Style = GlassButtonStyle.Secondary };
        DialogFrame.Create(this, 560, flight is null ? "Add a flight" : $"Edit {flight.FlightNo}",
            flight is null ? "Enter the route, base fare, aircraft and departure time." : "Change the fare, aircraft, capacity, departure time or availability.",
            new Control[]
            {
                Layouts.Row(D(14), _flightNoRow, typeRow), Layouts.Row(D(14), _originRow, _destinationRow),
                Layouts.Row(D(14), _priceRow, _capacityRow), _aircraftRow, departureRow, activeHost
            }, save, cancel);
        Theme.Apply(this);
    }

    private AircraftChoice SelectedAircraft => (AircraftChoice)_aircraft.SelectedItem!;

    private void ApplyAircraftChoice()
    {
        AircraftChoice choice = SelectedAircraft;
        _capacity.Enabled = choice.Id is null;
        if (choice.Id is not null) _capacity.Value = Math.Min(_capacity.Maximum, Math.Max(_capacity.Minimum, choice.Seats));
    }

    public static bool ShowEditor(IWin32Window owner, AdminService service, FlightAdminRow? flight)
    {
        int confirmed = flight is null ? 0 : service.CountActiveBookingsForFlight(flight.FlightNo);
        using var editor = new FlightEditorForm(service, flight, confirmed);
        if (editor.ShowDialog(owner) != DialogResult.OK) return false;
        try
        {
            service.SaveFlight(editor._flightNo.Text.Trim(), editor._origin.Text.Trim(), editor._destination.Text.Trim(),
                editor._price.Value, editor._type.SelectedItem?.ToString() == "Domestic",
                (int)editor._capacity.Value, editor._active.Checked, TimeOnly.FromDateTime(editor._departure.Value));
            string? chosen = editor.SelectedAircraft.Id;
            if (chosen != editor._originalAircraftId)
            {
                AircraftChangeResult change = service.ChangeAircraft(editor._flightNo.Text.Trim(), chosen);
                if (!change.Ok)
                {
                    MessageBox.Show(owner, change.Message, "Change aircraft", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
            }
            return true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner, ex.Message, "Unable to save flight", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return false;
        }
    }

    private bool ValidateInput()
    {
        bool ok = true;
        string number = _flightNo.Text.Trim();
        if (number.Length == 0) { _flightNoRow.SetMessage("Enter a flight number."); ok = false; }
        else if (_isNew && _service.FlightExists(number)) { _flightNoRow.SetMessage($"{number} already exists. Edit it instead."); ok = false; }
        if (_origin.Text.Trim().Length == 0) { _originRow.SetMessage("Enter the origin."); ok = false; }
        if (_destination.Text.Trim().Length == 0) { _destinationRow.SetMessage("Enter the destination."); ok = false; }
        if (_price.Value <= 0) { _priceRow.SetMessage("Enter a fare above zero."); ok = false; }
        if (_capacity.Value < _confirmedBookings)
        {
            _capacityRow.SetMessage($"Needs at least {_confirmedBookings} for confirmed bookings.");
            ok = false;
        }
        if (!_isNew && SelectedAircraft.Id != _originalAircraftId)
        {
            AircraftChangeResult check = _service.CheckAircraftChange(number, SelectedAircraft.Id);
            if (!check.Ok)
            {
                _aircraftRow.SetMessage("Some booked seats would not exist on this aircraft.");
                MessageBox.Show(this, check.Message, "Change aircraft", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                ok = false;
            }
        }
        return ok;
    }
}
