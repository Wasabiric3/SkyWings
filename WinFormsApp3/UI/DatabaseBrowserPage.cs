using System.Data;

namespace SkyWings.UI;

/// <summary>
/// Admin "Database" page: pick any table, see its rows, and Insert / Search / Update / Delete / Clear from auto-built fields.
/// All work goes through <see cref="AdminService"/>, which checks the role and writes the audit entry.
/// </summary>
internal sealed class DatabaseBrowserPage : Panel
{
    private const int WideTableColumns = 7;   // beyond this the grid scrolls sideways instead of squeezing every column

    private readonly AdminService _service;
    private readonly ComboBox _tables = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230 };
    private readonly TextBox _searchBox = new() { PlaceholderText = "Search this table", Width = 280 };
    private readonly System.Windows.Forms.Timer _searchTimer = new() { Interval = 300 };
    private readonly Label _count = new() { AutoSize = false, Dock = DockStyle.Right, Width = 220, TextAlign = ContentAlignment.MiddleRight };
    private readonly Label _heading = new() { AutoSize = false, Dock = DockStyle.Top };
    private readonly Label _hint = new() { AutoSize = false, Dock = DockStyle.Top };
    private readonly Panel _fieldsHost = new() { Dock = DockStyle.Fill, AutoScroll = true };
    private readonly DataGridView _grid = new() { Dock = DockStyle.Fill, AutoGenerateColumns = true };
    private readonly Dictionary<string, FieldRow> _rows = new();
    private readonly List<Label> _captions = new();
    private readonly GlassButton _insert = new() { Text = "INSERT", Style = GlassButtonStyle.Primary };
    private readonly GlassButton _search = new() { Text = "SEARCH", Style = GlassButtonStyle.Secondary };
    private readonly GlassButton _delete = new() { Text = "DELETE", Style = GlassButtonStyle.Secondary };
    private readonly GlassButton _update = new() { Text = "UPDATE", Style = GlassButtonStyle.Secondary };
    private readonly GlassButton _clear = new() { Text = "CLEAR", Style = GlassButtonStyle.Ghost };
    private DataTableInfo? _info;
    private bool _loading;

    public DatabaseBrowserPage(AdminService service)
    {
        _service = service;
        Dock = DockStyle.Fill;
        Visible = false;
        int D(int px) => Gfx.S(this, px);

        // ---- top bar: table picker, search, row count (all the same height and centred on one line)
        int h = D(44);   // the height of a rounded box
        Label Caption(string text)
        {
            var label = new Label
            {
                Text = text, AutoSize = false, Width = D(52), Height = h, TextAlign = ContentAlignment.MiddleLeft,
                Font = Theme.Small, ForeColor = Theme.TextSecondary, Margin = new Padding(0, 0, D(6), 0)
            };
            _captions.Add(label);
            return label;
        }
        // The wizard's rounded boxes around the picker and the search (they size the inputs, so the widths are set on the boxes).
        var tablesBox = new RoundedInput(_tables) { Width = D(200), Margin = new Padding(0, 0, D(24), 0) };
        var searchHost = new RoundedInput(_searchBox) { Width = D(200) };
        _count.Font = Theme.Small;
        _count.ForeColor = Theme.TextSecondary;
        var pickers = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, Padding = new Padding(0, D(6), 0, 0) };
        pickers.Controls.Add(Caption("Table"));
        pickers.Controls.Add(tablesBox);
        pickers.Controls.Add(Caption("Search"));
        pickers.Controls.Add(searchHost);
        var top = new Panel { Dock = DockStyle.Top, Height = D(60) };
        top.Controls.Add(pickers);
        top.Controls.Add(_count);

        // ---- left: heading, hint, action buttons, then the scrolling fields (buttons stay in reach on wide tables)
        _heading.Font = Theme.Subtitle;
        _heading.ForeColor = Theme.TextPrimary;
        _heading.Height = D(34);
        _heading.TextAlign = ContentAlignment.MiddleLeft;
        _hint.Font = Theme.Small;
        _hint.ForeColor = Theme.TextSecondary;
        _hint.Height = D(44);

        var buttons = new TableLayoutPanel { Dock = DockStyle.Top, Height = D(150), ColumnCount = 2, RowCount = 3, Padding = new Padding(0, D(4), 0, D(8)) };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        for (int r = 0; r < 3; r++) buttons.RowStyles.Add(new RowStyle(SizeType.Percent, 33.3f));
        foreach (var b in new[] { _insert, _search, _delete, _update, _clear }) { b.Dock = DockStyle.Fill; b.Margin = new Padding(D(3)); }
        buttons.Controls.Add(_insert, 0, 0); buttons.Controls.Add(_search, 1, 0);
        buttons.Controls.Add(_delete, 0, 1); buttons.Controls.Add(_update, 1, 1);
        buttons.Controls.Add(_clear, 0, 2); buttons.SetColumnSpan(_clear, 2);

        var left = new Panel { Dock = DockStyle.Left, Width = D(330), Padding = new Padding(0, 0, D(8), 0) };
        left.Controls.Add(_fieldsHost);
        left.Controls.Add(buttons);
        left.Controls.Add(_hint);
        left.Controls.Add(_heading);

        var divider = new Panel { Dock = DockStyle.Left, Width = 1, BackColor = Theme.BorderLight };
        var gap = new Panel { Dock = DockStyle.Left, Width = D(14) };

        GridStyle.Apply(_grid);
        var card = new GlassCard { Dock = DockStyle.Fill, ContentPadding = 12 };
        card.Controls.Add(_grid);
        card.Controls.Add(gap);
        card.Controls.Add(divider);
        card.Controls.Add(left);
        card.Controls.Add(top);
        Controls.Add(card);

        _tables.SelectedIndexChanged += (_, _) => { if (!_loading) { _searchBox.Clear(); LoadTable(); } };
        _searchBox.TextChanged += (_, _) => { _searchTimer.Stop(); _searchTimer.Start(); };
        _searchBox.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; _searchTimer.Stop(); Run("Unable to search", () => Reload(_searchBox.Text)); } };
        _searchTimer.Tick += (_, _) => { _searchTimer.Stop(); Run("Unable to search", () => Reload(_searchBox.Text)); };
        Disposed += (_, _) => _searchTimer.Dispose();

        _grid.SelectionChanged += (_, _) => FillFieldsFromSelection();
        _insert.Click += (_, _) => Run("Unable to insert", Insert);
        _search.Click += (_, _) => { _searchTimer.Stop(); Run("Unable to search", () => Reload(_searchBox.Text)); };
        _update.Click += (_, _) => Run("Unable to update", UpdateSelected);
        _delete.Click += (_, _) => Run("Unable to delete", DeleteSelected);
        _clear.Click += (_, _) =>
        {
            _searchTimer.Stop();
            _searchBox.Clear();
            Run("Unable to refresh", () => Reload(null));
            ClearFields();
        };
        UpdateState();
    }

    /// <summary>Called each time the section is shown.</summary>
    public void OnShow()
    {
        // AdminForm runs Theme.Apply over every page after building it, which resets label colours; set the muted ones again.
        _count.ForeColor = _hint.ForeColor = Theme.TextSecondary;
        foreach (Label caption in _captions) caption.ForeColor = Theme.TextSecondary;
        Run("Unable to list the tables", () =>
        {
            string? keep = _tables.SelectedItem as string;
            _loading = true;
            _tables.Items.Clear();
            foreach (string t in _service.ListTables()) _tables.Items.Add(t);
            _loading = false;
            _tables.SelectedItem = keep is not null && _tables.Items.Contains(keep) ? keep : _tables.Items.Count > 0 ? _tables.Items[0] : null;
        });
    }

    private void Run(string title, Action action)
    {
        try { action(); }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(FindForm(), ex.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void LoadTable()
    {
        if (_tables.SelectedItem is not string name) return;
        Run("Unable to open the table", () =>
        {
            _info = _service.DescribeTable(name);
            BuildFields();
            Reload(null);
        });
    }

    private void BuildFields()
    {
        _fieldsHost.SuspendLayout();
        foreach (Control old in _fieldsHost.Controls.Cast<Control>().ToList()) { _fieldsHost.Controls.Remove(old); old.Dispose(); }
        _rows.Clear();
        // Dock.Top stacks the last control added at the top, so add in reverse to keep the column order.
        foreach (var c in _info!.Columns.Reverse())
        {
            var row = new FieldRow(c.Name, new TextBox { PlaceholderText = c.Masked ? "hidden" : "" });
            var notes = new List<string>();
            if (c.IsKey) notes.Add("key");
            if (c.NotNull && !c.HasDefault && !c.IsKey) notes.Add("required");
            notes.Add(string.IsNullOrWhiteSpace(c.Type) ? "any" : c.Type.ToLowerInvariant());
            row.SetMessage(string.Join(" · ", notes), isError: false);
            _rows[c.Name] = row;
            _fieldsHost.Controls.Add(row);
        }
        _fieldsHost.ResumeLayout();
        ClearFields();
    }

    private void Reload(string? search)
    {
        if (_info is null) return;
        DataTable data = _service.QueryTable(_info.Name, search);
        _grid.DataSource = data;
        // Wide tables scroll sideways with readable columns; narrow ones fill the width.
        _grid.AutoSizeColumnsMode = data.Columns.Count > WideTableColumns ? DataGridViewAutoSizeColumnsMode.DisplayedCells : DataGridViewAutoSizeColumnsMode.Fill;
        _grid.ClearSelection();
        string shown = data.Rows.Count == 1 ? "1 row" : $"{data.Rows.Count} rows";
        _count.Text = data.Rows.Count >= 1000 ? shown + " (first 1000)" : shown;
        UpdateState();
    }

    private bool HasSelection => _grid.CurrentRow is { Selected: true };

    private void FillFieldsFromSelection()
    {
        if (_info is not null && _grid.CurrentRow is { Selected: true } row)
            foreach (var (name, field) in _rows)
                field.Input.Text = Convert.ToString(row.Cells[name].Value) ?? "";
        UpdateState();
    }

    private void ClearFields()
    {
        foreach (var field in _rows.Values) field.Input.Text = "";
        _grid.ClearSelection();
        UpdateState();
    }

    /// <summary>Heading, hint, which buttons work and which fields can be typed in, for the current table and selection.</summary>
    private void UpdateState()
    {
        bool viewOnly = _info?.ReadOnly ?? true;
        bool selected = HasSelection;
        _insert.Enabled = !viewOnly;
        _update.Enabled = _delete.Enabled = !viewOnly && selected;
        _search.Enabled = _info is not null;
        _heading.Text = _info is null ? "Row details" : selected ? "Edit row" : viewOnly ? "Row details" : "New row";
        _hint.Text = _info is null ? "Choose a table to begin."
            : viewOnly ? "This table can be viewed here but not changed; use its own screen."
            : selected ? "Change the fields, then UPDATE. Key fields are locked."
            : "Click a row to edit it, or fill in the fields and press INSERT.";
        if (_info is null) return;
        foreach (var c in _info.Columns)
            if (_rows.TryGetValue(c.Name, out var field) && field.Input is TextBox box)
                box.ReadOnly = viewOnly || c.Masked || (selected && c.IsKey);
    }

    private Dictionary<string, string?> FieldValues()
        => _rows.ToDictionary(kv => kv.Key, kv => (string?)kv.Value.Input.Text);

    /// <summary>The selected row's key, read from the grid (not the edited fields) so a changed field cannot redirect an update.</summary>
    private Dictionary<string, string?> SelectedKey()
    {
        if (_info is null || _grid.CurrentRow is not { Selected: true } row)
            throw new InvalidOperationException("Select a row in the grid first.");
        var names = _info.UsesRowId ? new[] { "rowid" } : _info.Columns.Where(c => c.IsKey).Select(c => c.Name).ToArray();
        return names.ToDictionary(n => n, n => (string?)Convert.ToString(row.Cells[n].Value));
    }

    private void Insert()
    {
        _service.InsertRow(_info!.Name, FieldValues());
        Reload(_searchBox.Text);
        ClearFields();
    }

    private void UpdateSelected()
    {
        var key = SelectedKey();
        _service.UpdateRow(_info!.Name, key, FieldValues());
        Reload(_searchBox.Text);
        ClearFields();
    }

    private void DeleteSelected()
    {
        var key = SelectedKey();
        string which = string.Join(", ", key.Select(k => $"{k.Key} = {k.Value}"));
        if (MessageBox.Show(FindForm(), $"Delete the row where {which} from {_info!.Name}?\nThis cannot be undone.", "Delete row",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        _service.DeleteRow(_info.Name, key);
        Reload(_searchBox.Text);
        ClearFields();
    }
}
