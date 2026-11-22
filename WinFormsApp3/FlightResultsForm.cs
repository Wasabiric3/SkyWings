using SkyWings.Controllers;
using SkyWings.UI;

namespace SkyWings;

/// <summary>
/// Search results: the flights that match the chosen destination, date and cabin, with the price for that cabin and
/// how many seats are left on that day. Choosing one continues to the booking wizard.
/// </summary>
public sealed class FlightResultsForm : GlassForm
{
    private readonly DataGridView _grid = new();
    private readonly GlassButton _choose = new() { Text = "Choose this flight" };
    private readonly Label _note = new();

    /// <summary>The flight the person chose, when the dialog returns OK.</summary>
    public Flight? Selected { get; private set; }

    /// <param name="destination">A destination such as "Cebu (CEB)", or null/empty for every flight from Manila.</param>
    public FlightResultsForm(BookingStore store, string? destination, DateTime date, string className)
    {
        Text = "Flight results · SkyWings";
        StartPosition = FormStartPosition.CenterParent;
        SizeForScreen(1040, 560, 820, 440);
        int D(int px) => Gfx.S(this, px);
        Padding = new Padding(D(24), D(12), D(24), D(16));

        bool anywhere = string.IsNullOrEmpty(destination);
        var rows = new List<FlightRow>();
        string? problem = null;
        try
        {
            rows = FlightSearch.Search(store, destination, date, className);
        }
        catch (IOException ex)
        {
            AppLogger.Error("FlightSearch", ex);
            problem = "The flights could not be loaded: " + ex.Message;
        }

        var header = new PageHeader
        {
            Title = anywhere ? "All flights from Manila" : $"Flights to {destination}",
            Hint = $"{date:dddd, MMMM d, yyyy}   ·   {className}   ·   {rows.Count} flight(s)"
        };

        GridStyle.Apply(_grid);
        _grid.Dock = DockStyle.Fill;
        _grid.AutoGenerateColumns = false;
        AddColumn("Flight", "FlightNo", 0.8f);
        AddColumn("Route", "Route", 2.4f);
        AddColumn("Departs", "Departs", 0.9f);
        AddColumn("Arrives", "Arrives", 0.9f);
        AddColumn("Duration", "Duration", 0.9f);
        AddColumn("Price per traveller", "Price", 1.3f, right: true);
        AddColumn("Seats left", "SeatsLeft", 0.9f, right: true);
        _grid.SelectionChanged += (_, _) => UpdateChoose();
        _grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) Choose(); };
        _grid.CellFormatting += (_, e) =>
        {
            if (e.RowIndex >= 0 && _grid.Columns[e.ColumnIndex].DataPropertyName == "SeatsLeft" && e.Value is "Sold out")
                e.CellStyle!.ForeColor = Theme.DangerRed;
        };

        var tableCard = new GlassCard { Dock = DockStyle.Fill, ContentPadding = 10 };
        tableCard.Controls.Add(_grid);

        _note.Text = problem ?? (rows.Count == 0
            ? "No flights match. Try another destination."
            : "One-way price in your cabin, before taxes, extras and any seasonal or promo changes. Seats left are for this date.");
        _note.Font = Theme.Small;
        _note.AutoSize = false;
        _note.Height = D(34);
        _note.Dock = DockStyle.Fill;
        _note.TextAlign = ContentAlignment.MiddleLeft;

        _choose.Click += (_, _) => Choose();
        var close = new GlassButton { Text = "Back to search", Style = GlassButtonStyle.Secondary, DialogResult = DialogResult.Cancel };
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Right, Width = D(400), FlowDirection = FlowDirection.RightToLeft, WrapContents = false,
            Padding = new Padding(0, D(10), 0, 0)
        };
        actions.Controls.Add(_choose);
        actions.Controls.Add(close);
        var footer = new Panel { Dock = DockStyle.Bottom, Height = D(56) };
        footer.Controls.Add(_note);
        footer.Controls.Add(actions);

        Controls.Add(tableCard);
        Controls.Add(footer);
        Controls.Add(header);
        Theme.Apply(this);
        _note.ForeColor = problem is null ? Theme.TextSecondary : Theme.DangerRed;
        AcceptButton = _choose;
        CancelButton = close;
        // Bind after the grid is inside the window, otherwise its rows don't exist yet.
        _grid.DataSource = rows;
        if (rows.Count > 0) _grid.CurrentCell = _grid[0, 0];
        UpdateChoose();
    }

    private void AddColumn(string header, string property, float weight, bool right = false)
    {
        var column = new DataGridViewTextBoxColumn { HeaderText = header, DataPropertyName = property, FillWeight = weight * 100 };
        if (right)
        {
            column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
            column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
        }
        _grid.Columns.Add(column);
    }

    private FlightRow? Current() => _grid.CurrentRow?.DataBoundItem as FlightRow;

    private void UpdateChoose()
    {
        FlightRow? row = Current();
        _choose.Enabled = row is not null && row.Seats > 0;
    }

    private void Choose()
    {
        FlightRow? row = Current();
        if (row is null || row.Seats == 0) return;
        Selected = row.Flight;
        DialogResult = DialogResult.OK;
        Close();
    }
}
