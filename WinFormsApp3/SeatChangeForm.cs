using SkyWings.UI;

namespace SkyWings;

/// <summary>Lets a customer move one traveller to another free seat on either leg of a booking.</summary>
public sealed class SeatChangeForm : GlassForm
{
    private readonly BookingStore _store;
    private BookingRecord _booking;
    private readonly ComboBox _passenger = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _leg = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly SeatMap _map = new() { MaxSelection = 1 };
    private readonly Panel _mapHost = new() { Dock = DockStyle.Fill, AutoScroll = true };
    private readonly Label _info = new() { AutoSize = false, Dock = DockStyle.Top };
    private readonly GlassButton _save = new() { Text = "Move to this seat" };
    private bool _loading;
    private bool _locked;

    /// <summary>True when at least one seat was changed, so the caller refreshes its view.</summary>
    public bool Changed { get; private set; }

    public SeatChangeForm(BookingStore store, BookingRecord booking)
    {
        _store = store;
        _booking = booking;
        Text = $"Change seats · {booking.Reference}";
        StartPosition = FormStartPosition.CenterParent;
        SizeForScreen(900, 660, 760, 520);
        int D(int px) => Gfx.S(this, px);
        Padding = new Padding(D(20), D(12), D(20), D(16));

        var passengerRow = new FieldRow("TRAVELLER", _passenger);
        var legRow = new FieldRow("FLIGHT", _leg);
        _info.Height = D(28);
        _info.Font = Theme.Small;

        var header = new PageHeader { Title = "Change seats", Hint = "Pick a traveller and a flight, then choose a seat on the map." };
        var legend = new SeatLegend();

        _save.Click += (_, _) => Save();
        var done = new GlassButton { Text = "Done", Style = GlassButtonStyle.Secondary, DialogResult = DialogResult.Cancel };
        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = D(54), FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false, Padding = new Padding(0, D(8), 0, 0)
        };
        footer.Controls.Add(_save);
        footer.Controls.Add(done);

        _mapHost.Controls.Add(_map);
        _mapHost.Resize += (_, _) => CenterMap();
        _map.SelectionChanged += (_, _) => UpdateSaveState();

        var card = new GlassCard { Dock = DockStyle.Fill, ContentPadding = 16 };
        card.Controls.Add(_mapHost);
        card.Controls.Add(footer);
        var top = new Panel { Dock = DockStyle.Top };
        int topHeight = Layouts.Stack(top, Layouts.Row(D(14), passengerRow, legRow), _info, legend);
        top.Height = topHeight;
        card.Controls.Add(top);
        Controls.Add(card);
        Controls.Add(header);   // added last, so it docks above the card

        FillChoices();
        Theme.Apply(this);
        CancelButton = done;
        _info.ForeColor = Theme.TextSecondary;
        Shown += (_, _) => RefreshMap();
    }

    private void FillChoices()
    {
        _loading = true;
        int selectedPassenger = Math.Max(0, _passenger.SelectedIndex);
        _passenger.Items.Clear();
        foreach (var p in _booking.Passengers) _passenger.Items.Add(p.Name);
        _passenger.SelectedIndex = Math.Min(selectedPassenger, _passenger.Items.Count - 1);

        int selectedLeg = Math.Max(0, _leg.SelectedIndex);
        _leg.Items.Clear();
        _leg.Items.Add($"Outbound · {_booking.FlightNo} · {_booking.FlightDateValue:MMM d}");
        if (_booking.ReturnFlightNo is not null)
            _leg.Items.Add($"Return · {_booking.ReturnFlightNo} · {_booking.ReturnFlightDateValue:MMM d}");
        _leg.SelectedIndex = Math.Min(selectedLeg, _leg.Items.Count - 1);
        _passenger.SelectedIndexChanged -= OnChoiceChanged;
        _leg.SelectedIndexChanged -= OnChoiceChanged;
        _passenger.SelectedIndexChanged += OnChoiceChanged;
        _leg.SelectedIndexChanged += OnChoiceChanged;
        _loading = false;
    }

    private void OnChoiceChanged(object? sender, EventArgs e)
    {
        if (!_loading) RefreshMap();
    }

    private bool IsReturnLeg => _leg.SelectedIndex == 1;

    private (string FlightNo, DateTime Date, string Current) CurrentLeg()
    {
        var passenger = _booking.Passengers[Math.Max(0, _passenger.SelectedIndex)];
        return IsReturnLeg
            ? (_booking.ReturnFlightNo!, _booking.ReturnFlightDateValue!.Value, passenger.ReturnSeatCode)
            : (_booking.FlightNo, _booking.FlightDateValue, passenger.SeatCode);
    }

    private void RefreshMap()
    {
        var (flightNo, date, current) = CurrentLeg();
        string own = SeatLayout.Normalize(current);
        try
        {
            Aircraft aircraft = _store.LoadAircraft(flightNo);
            _map.Configure(aircraft.Cabin(_booking.ClassName) ?? Aircraft.Legacy(AppData.DefaultDomesticCapacity).Cabin(_booking.ClassName)!);
            _map.MaxSelection = 1;
            if (own.Length > 0) _map.SetSelected(new[] { own });

            // The traveller's own seat is not "taken" for them.
            var taken = _store.LoadTakenSeats(flightNo, date);
            taken.Remove(own);
            var held = _store.GetActiveHoldsForFlightDate(flightNo, date).Keys;
            _map.SetUnavailable(taken, held);
        }
        catch (IOException ex)
        {
            AppLogger.Error("SeatChange", ex);
            MessageBox.Show(this, "The seat map could not be loaded.\n\n" + ex.Message, "Change seats",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            Close();
            return;
        }
        _info.ForeColor = Theme.TextSecondary;
        _info.Text = own.Length > 0 ? $"Current seat: {current}. Pick a free seat, then press Move." : "No seat assigned yet. Pick a free seat.";

        // A checked-in traveller holds a boarding pass for this seat, so it can no longer be moved.
        _locked = _store.IsCheckedIn(_booking.Reference, _booking.Passengers[Math.Max(0, _passenger.SelectedIndex)].Name, flightNo, date);
        _map.Enabled = !_locked;
        if (_locked)
        {
            _info.ForeColor = Theme.DangerRed;
            _info.Text = $"{_booking.Passengers[_passenger.SelectedIndex].Name} has already checked in for this flight, so the seat can't be changed.";
        }
        CenterMap();
        UpdateSaveState();
    }

    private void CenterMap() => _map.Left = Math.Max(0, (_mapHost.ClientSize.Width - _map.Width) / 2);

    private void UpdateSaveState()
    {
        string own = SeatLayout.Normalize(CurrentLeg().Current);
        _save.Enabled = !_locked && _map.Selected.Count == 1 && SeatLayout.Normalize(_map.Selected[0]) != own;
    }

    private void Save()
    {
        if (_map.Selected.Count != 1) return;
        string passenger = _booking.Passengers[_passenger.SelectedIndex].Name;
        string seat = _map.Selected[0];
        try
        {
            if (_store.ChangeSeat(_booking.Reference, passenger, IsReturnLeg, seat))
            {
                Changed = true;
                BookingRecord? fresh = _store.FindByReference(_booking.Reference);
                if (fresh is not null) _booking = fresh;
                FillChoices();
                RefreshMap();
                _info.ForeColor = Theme.Accent;
                _info.Text = $"{passenger} is now in seat {seat}.";
            }
            else
            {
                MessageBox.Show(this, "That seat is no longer available. The map has been refreshed.", "Change seats",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                RefreshMap();
            }
        }
        catch (IOException ex)
        {
            AppLogger.Error("SeatChange", ex);
            MessageBox.Show(this, "The seat could not be changed.\n\n" + ex.Message, "Change seats", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
