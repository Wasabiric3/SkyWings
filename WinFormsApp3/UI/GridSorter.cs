namespace SkyWings.UI;

/// <summary>How to sort by a column: the value to compare, and whether the first click on it goes largest/newest first.</summary>
internal sealed record SortKey<T>(Func<T, IComparable?> Of, bool FirstDescending = false);

/// <summary>
/// Click-a-header sorting for a grid that shows a plain list. The owner keeps the rows, asks <see cref="Sort"/> for them in order and
/// rebinds when <see cref="Changed"/> is raised. Columns are named by their data property; a column without a key cannot be sorted.
/// The sort is a short setting ("Booked:desc") so a screen can remember it. Rows with equal keys keep their order; rows without a
/// value come first when ascending and last when descending.
/// </summary>
internal sealed class GridSorter<T>
{
    private readonly IReadOnlyDictionary<string, SortKey<T>> _keys;
    private DataGridView? _grid;

    /// <param name="column">The column sorted by at first, or null to keep the screen's own order until a header is clicked.</param>
    public GridSorter(IReadOnlyDictionary<string, SortKey<T>> keys, string? column = null, bool descending = false)
    {
        _keys = keys;
        Column = column is not null && keys.ContainsKey(column) ? column : null;
        Descending = descending;
    }

    public string? Column { get; private set; }
    public bool Descending { get; private set; }

    /// <summary>Raised after a header click changed the sort (the glyph is already updated).</summary>
    public event EventHandler? Changed;

    public IReadOnlyList<T> Sort(IEnumerable<T> rows)
    {
        if (Column is null) return rows.ToList();   // no sort chosen: the screen's own order
        Func<T, IComparable?> key = _keys[Column].Of;
        IOrderedEnumerable<T> ordered = Descending
            ? rows.OrderByDescending(key, ValueComparer.Instance)
            : rows.OrderBy(key, ValueComparer.Instance);
        return ordered.ToList();
    }

    /// <summary>A click on a column: a new column starts in its own first direction, the same column flips. False for a column that cannot be sorted.</summary>
    public bool Toggle(string column)
    {
        if (!_keys.TryGetValue(column, out SortKey<T>? key)) return false;
        if (column == Column) Descending = !Descending;
        else { Column = column; Descending = key.FirstDescending; }
        return true;
    }

    /// <summary>Back to the screen's own order, and the arrow off the grid.</summary>
    public void Clear()
    {
        Column = null;
        Descending = false;
        ApplyGlyphs();
    }

    /// <summary>"Column:asc" or "Column:desc"; empty when no sort was chosen.</summary>
    public string ToSetting() => Column is null ? "" : $"{Column}:{(Descending ? "desc" : "asc")}";

    /// <summary>Takes a setting written by <see cref="ToSetting"/>; anything else (empty, unknown column, bad direction) is ignored.</summary>
    public void LoadSetting(string? setting)
    {
        string[] parts = (setting ?? "").Trim().Split(':');
        if (parts.Length != 2 || !_keys.ContainsKey(parts[0])) return;
        if (parts[1] is not ("asc" or "desc")) return;
        Column = parts[0];
        Descending = parts[1] == "desc";
    }

    /// <summary>Makes the grid's columns sortable from a header click and keeps the arrow on the sorted column.</summary>
    public void Attach(DataGridView grid)
    {
        _grid = grid;
        grid.DataBindingComplete += (_, _) => ApplyGlyphs();
        grid.ColumnHeaderMouseClick += (_, e) =>
        {
            if (e.Button != MouseButtons.Left && e.Button != MouseButtons.None) return;
            if (e.ColumnIndex < 0 || e.ColumnIndex >= grid.Columns.Count) return;
            if (!Toggle(grid.Columns[e.ColumnIndex].DataPropertyName)) return;
            ApplyGlyphs();
            Changed?.Invoke(this, EventArgs.Empty);
        };
    }

    private void ApplyGlyphs()
    {
        if (_grid is null) return;
        foreach (DataGridViewColumn column in _grid.Columns)
        {
            if (!_keys.ContainsKey(column.DataPropertyName)) continue;
            column.SortMode = DataGridViewColumnSortMode.Programmatic;   // the owner sorts the list; the grid only draws the arrow
            column.HeaderCell.SortGlyphDirection = column.DataPropertyName == Column
                ? (Descending ? SortOrder.Descending : SortOrder.Ascending)
                : SortOrder.None;
        }
    }

    private sealed class ValueComparer : IComparer<IComparable?>
    {
        public static readonly ValueComparer Instance = new();

        public int Compare(IComparable? a, IComparable? b)
        {
            if (a is null) return b is null ? 0 : -1;
            if (b is null) return 1;
            return a is string x && b is string y
                ? string.Compare(x, y, StringComparison.CurrentCultureIgnoreCase)
                : a.CompareTo(b);
        }
    }
}
