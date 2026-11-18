using SkyWings.Controllers;
using SkyWings.UI;

namespace SkyWings;

/// <summary>Flights in one region: a table, a detail panel with fares per class, and a Book button.</summary>
public sealed class FlightDetailsForm : GlassForm
{
    private readonly BookingStore _store;
    private readonly string _routeType;
    private readonly IReadOnlyList<Flight> _flights;
    private readonly DataGridView _grid = new();
    private readonly Label _details = new() { AutoSize = false, Dock = DockStyle.Fill };
    private readonly GlassButton _bookButton = new() { Text = "Book selected flight" };
    private readonly Label _status = new();

    private sealed record FlightRow(string FlightNo, string Route, string Departure,
        string Duration, decimal BaseFare, string Status, Flight Flight);

    public FlightDetailsForm(BookingStore store, string title, string routeType, IReadOnlyList<Flight> flights)
    {
        _store = store;
        _routeType = routeType;
        _flights = flights;
        Text = $"{title} · SkyWings";
        StartPosition = FormStartPosition.CenterParent;
        SizeForScreen(1000, 620, 800, 500);
        int D(int px) => Gfx.S(this, px);
        Padding = new Padding(D(24), D(12), D(24), D(16));

        var heading = new Label
        {
            Text = title, Font = Theme.Title, ForeColor = Theme.TextPrimary, AutoSize = false,
            Height = D(48), Dock = DockStyle.Top, TextAlign = ContentAlignment.MiddleLeft
        };

        GridStyle.Apply(_grid);
        _grid.Dock = DockStyle.Fill;
        _grid.AutoGenerateColumns = false;
        _grid.SelectionChanged += (_, _) => ShowSelectedFlight();
        _grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) BookSelectedFlight(); };
        AddColumn("Flight", "FlightNo", 0.8f);
        AddColumn("Route", "Route", 2.2f);
        AddColumn("Departure", "Departure", 1f);
        AddColumn("Duration", "Duration", 0.9f);
        AddColumn("Base fare (PHP)", "BaseFare", 1.2f, "N2", right: true);
        AddColumn("Status", "Status", 0.8f);

        var tableCard = new GlassCard { Dock = DockStyle.Fill, ContentPadding = 10 };
        tableCard.Controls.Add(_grid);

        _details.Font = Theme.Mono;
        _details.ForeColor = Theme.TextPrimary;
        var detailCard = new GlassCard { Dock = DockStyle.Bottom, Height = D(220), ContentPadding = 14 };
        detailCard.Controls.Add(_details);

        var close = new GlassButton { Text = "Close", Style = GlassButtonStyle.Secondary, DialogResult = DialogResult.Cancel };
        _bookButton.Click += (_, _) => BookSelectedFlight();
        _status.AutoSize = true;
        _status.ForeColor = Theme.TextSecondary;
        _status.Font = Theme.Small;
        _status.Margin = new Padding(D(10), D(12), 0, 0);
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = D(56), FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false, Padding = new Padding(0, D(10), 0, 0)
        };
        actions.Controls.Add(_bookButton);
        actions.Controls.Add(close);
        actions.Controls.Add(_status);

        Controls.Add(tableCard);
        Controls.Add(detailCard);
        Controls.Add(actions);
        Controls.Add(heading);
        Theme.Apply(this);
        _details.Font = Theme.Mono;
        _status.ForeColor = Theme.TextSecondary;
        AcceptButton = _bookButton;
        CancelButton = close;
        LoadRows();
    }

    private void AddColumn(string header, string property, float weight, string? format = null, bool right = false)
    {
        var column = new DataGridViewTextBoxColumn
        {
            HeaderText = header, DataPropertyName = property, FillWeight = weight * 100,
            DefaultCellStyle = new DataGridViewCellStyle { Format = format }
        };
        if (right)
        {
            column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
        }
        _grid.Columns.Add(column);
    }

    private void LoadRows()
    {
        var rows = _flights.Select(f =>
        {
            DateTime departure = ScheduledDateTime(f);
            int minutes = AppData.FlightEstDurationMin.GetValueOrDefault(f.FlightNo, 120);
            // Flights run daily and customers pick a future date, so an active flight is always bookable.
            // (Comparing today's departure time to the clock made every flight look CLOSED in the evening.)
            return new FlightRow(f.FlightNo, $"{f.Origin} → {f.Destination}", $"{departure:h:mm tt}",
                $"{minutes / 60}h {minutes % 60:00}m", f.BasePrice, "OPEN", f);
        }).ToList();
        _grid.DataSource = rows;
        if (rows.Count > 0) _grid.CurrentCell = _grid[0, 0];
        _status.Text = rows.Count == 0 ? "No flights available." : $"{rows.Count} flight(s) · double-click a row to book";
        if (rows.Count == 0) _bookButton.Enabled = false;
    }

    private static DateTime ScheduledDateTime(Flight flight)
        => DateTime.Today.Add(AppData.FlightScheduledTimes.GetValueOrDefault(flight.FlightNo, new TimeOnly(8, 0)).ToTimeSpan());

    private void ShowSelectedFlight()
    {
        if (_grid.CurrentRow?.DataBoundItem is not FlightRow row) return;
        DateTime departure = ScheduledDateTime(row.Flight);
        DateTime arrival = departure.AddMinutes(AppData.FlightEstDurationMin.GetValueOrDefault(row.FlightNo, 120));
        _details.Text = string.Join(Environment.NewLine,
            FlightLoad.Details(_store, row.Flight, _routeType, row.BaseFare, departure, arrival, DateTime.Today.AddDays(1)));
        _bookButton.Enabled = row.Status == "OPEN";
    }

    private void BookSelectedFlight()
    {
        if (_grid.CurrentRow?.DataBoundItem is not FlightRow row || row.Status != "OPEN")
        {
            MessageBox.Show(this, "Select an open flight before booking.", "Flight unavailable", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var wizard = new BookingWizardForm(_store);
        wizard.Prefill(row.Flight.FlightNo, null, null, null);
        wizard.ShowDialog(this);
    }
}
