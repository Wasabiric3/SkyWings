using System.ComponentModel;

namespace SkyWings.UI;

/// <summary>A field that opens a popup (the date field and the flight picker); <see cref="RoundedInput"/> draws its chevron and opens it on a click.</summary>
internal interface IDropDownField
{
    void OpenDropDown();
}

/// <summary>
/// Chooses one flight. The closed field shows the chosen flight (route, flight number, price); a click opens a popup under it, as wide as
/// the field, with a search box and a short list (about six rows, then it scrolls) grouped by region. Typing narrows the list by city or
/// flight number; the arrow keys move, Enter chooses and Escape closes. It replaces the native drop-down, which opened one very tall,
/// unsorted list that ran off the screen.
/// </summary>
public sealed class FlightPicker : Control, ISelfStyled, IDropDownField
{
    private const int VisibleRows = 6;

    private static readonly Font BoldFont = new(Theme.Body, FontStyle.Bold);

    private List<Flight> _flights = new();
    private int _selected = -1;
    private ToolStripDropDown? _popup;

    public FlightPicker()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.StandardClick, true);
        TabStop = true;
        Cursor = Cursors.Hand;
        Font = Theme.Body;
        BackColor = Color.White;
        AccessibleRole = AccessibleRole.ComboBox;
        Height = TextRenderer.MeasureText("Ag", BoldFont).Height + 6;
        Text = DisplayText;
    }

    public event EventHandler? SelectedIndexChanged;

    /// <summary>The flights that can be chosen, in the order they were given.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public IReadOnlyList<Flight> Flights => _flights;

    /// <summary>Replaces the flights. Nothing is chosen afterwards.</summary>
    public void SetFlights(IEnumerable<Flight> flights)
    {
        _flights = flights.ToList();
        SelectedIndex = -1;
        Invalidate();
    }

    /// <summary>Index into <see cref="Flights"/> of the chosen flight, or -1 for none.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex
    {
        get => _selected;
        set
        {
            int index = value >= 0 && value < _flights.Count ? value : -1;
            if (index == _selected) return;
            _selected = index;
            Text = DisplayText;
            Invalidate();
            SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>The chosen flight, or null.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Flight? Selected
    {
        get => _selected < 0 ? null : _flights[_selected];
        set => SelectedIndex = value is null ? -1 : _flights.IndexOf(value);
    }

    internal bool IsOpen => _popup is not null;
    internal ToolStripDropDown? Popup => _popup;

    internal string DisplayText => Selected is { } f ? $"{f.Origin} → {f.Destination}   {f.FlightNo}" : "Choose a flight";

    /// <summary>Chooses <paramref name="flight"/> and closes the popup.</summary>
    internal void Pick(Flight flight)
    {
        Selected = flight;
        _popup?.Close();
    }

    // ------------------------------------------------------------------ the list: grouping, search, keys

    /// <summary>"DOMESTIC", "SOUTHEAST ASIA", ... from the flight number: PR-1xx/5xx, 2xx/6xx, 3xx/7xx, 4xx/8xx (the second set are the returns).</summary>
    internal static string GroupOf(Flight flight)
    {
        string digits = new(flight.FlightNo.SkipWhile(c => !char.IsDigit(c)).TakeWhile(char.IsDigit).ToArray());
        return int.TryParse(digits, out int number) && flight.FlightNo.StartsWith("PR-", StringComparison.OrdinalIgnoreCase)
            ? (number / 100) switch
            {
                1 or 5 => "DOMESTIC",
                2 or 6 => "SOUTHEAST ASIA",
                3 or 7 => "EUROPE",
                4 or 8 => "NORTH AMERICA",
                _ => "OTHER FLIGHTS"
            }
            : "OTHER FLIGHTS";
    }

    private static readonly string[] GroupOrder = { "DOMESTIC", "SOUTHEAST ASIA", "EUROPE", "NORTH AMERICA", "OTHER FLIGHTS" };

    /// <summary>
    /// The popup's rows: a heading (a string) followed by its flights, for every region that still has a match. Flights leaving Manila come
    /// before the returns, then by city and flight number. <paramref name="term"/> matches either city or the flight number, ignoring case.
    /// </summary>
    internal static IReadOnlyList<object> BuildItems(IEnumerable<Flight> flights, string term)
    {
        string needle = (term ?? "").Trim();
        var items = new List<object>();
        IEnumerable<Flight> matches = flights.Where(f => needle.Length == 0
            || f.Origin.Contains(needle, StringComparison.CurrentCultureIgnoreCase)
            || f.Destination.Contains(needle, StringComparison.CurrentCultureIgnoreCase)
            || f.FlightNo.Contains(needle, StringComparison.CurrentCultureIgnoreCase));
        foreach (IGrouping<string, Flight> group in matches.GroupBy(GroupOf).OrderBy(g => Array.IndexOf(GroupOrder, g.Key)))
        {
            items.Add(group.Key);
            items.AddRange(group
                .OrderBy(f => f.Origin.StartsWith("Manila", StringComparison.Ordinal) ? 0 : 1)
                .ThenBy(f => f.Origin.StartsWith("Manila", StringComparison.Ordinal) ? f.Destination : f.Origin, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(f => f.FlightNo, StringComparer.Ordinal));
        }
        return items;
    }

    /// <summary>The index of the next flight (never a heading) from <paramref name="from"/> in the direction of <paramref name="step"/> (+1 or -1); null when there is none.</summary>
    internal static int? NextSelectable(IReadOnlyList<object> items, int from, int step)
    {
        for (int i = from + step; i >= 0 && i < items.Count; i += step)
            if (items[i] is Flight) return i;
        return null;
    }

    // ------------------------------------------------------------------ the popup

    public void OpenDropDown()
    {
        if (!Enabled || _popup is not null || _flights.Count == 0) return;
        int D(int px) => Gfx.S(this, px);
        int rowHeight = D(46), headingHeight = D(30);

        var panel = new Panel { BackColor = Color.White, Width = Math.Max(Width, D(360)) };
        var search = new TextBox
        {
            BorderStyle = BorderStyle.None, Font = Theme.Body, PlaceholderText = "Type a city or flight number", Dock = DockStyle.Top,
            AccessibleName = "Search flights"
        };
        var searchHost = new Panel { Dock = DockStyle.Top, Height = D(44), Padding = new Padding(D(16), D(12), D(16), 0), BackColor = Color.White };
        searchHost.Controls.Add(search);
        var divider = new Panel { Dock = DockStyle.Top, Height = 1, BackColor = Theme.Border };
        var list = new ListBox
        {
            Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, IntegralHeight = false, DrawMode = DrawMode.OwnerDrawVariable,
            BackColor = Color.White, Font = Theme.Body, TabStop = false
        };
        panel.Controls.Add(list);
        panel.Controls.Add(divider);
        panel.Controls.Add(searchHost);

        ToolStripControlHost? host = null;
        IReadOnlyList<object> items = Array.Empty<object>();
        int ContentHeight() => items.Sum(i => i is Flight ? rowHeight : headingHeight);
        void Fill(string term, Flight? keep)
        {
            items = BuildItems(_flights, term);
            list.BeginUpdate();
            list.Items.Clear();
            list.Items.AddRange(items.ToArray());
            list.EndUpdate();
            int chosen = keep is null ? -1 : items.ToList().IndexOf(keep);
            if (chosen < 0) chosen = NextSelectable(items, -1, +1) ?? -1;
            if (chosen >= 0) { list.SelectedIndex = chosen; list.TopIndex = Math.Max(0, chosen - 2); }
            int rows = Math.Min(ContentHeight(), VisibleRows * rowHeight);
            panel.Height = searchHost.Height + divider.Height + Math.Max(rows, rowHeight);
            if (host is not null) { host.Size = panel.Size; _popup!.Size = panel.Size; }
        }

        list.MeasureItem += (_, e) => e.ItemHeight = e.Index >= 0 && e.Index < items.Count && items[e.Index] is Flight ? rowHeight : headingHeight;
        list.DrawItem += (_, e) => DrawRow(e, items);
        list.MouseMove += (_, e) =>
        {
            int index = list.IndexFromPoint(e.Location);
            if (index >= 0 && index < items.Count && items[index] is Flight && list.SelectedIndex != index) list.SelectedIndex = index;
        };
        list.MouseUp += (_, e) =>
        {
            int index = list.IndexFromPoint(e.Location);
            if (e.Button == MouseButtons.Left && index >= 0 && index < items.Count && items[index] is Flight flight) Pick(flight);
        };
        list.SelectedIndexChanged += (_, _) =>
        {
            // A heading cannot be chosen: step on to the flight below it.
            if (list.SelectedIndex >= 0 && list.SelectedIndex < items.Count && items[list.SelectedIndex] is string && NextSelectable(items, list.SelectedIndex, +1) is { } next)
                list.SelectedIndex = next;
        };

        void Move(int step)
        {
            int? next = NextSelectable(items, list.SelectedIndex, step);
            if (next is { } n) list.SelectedIndex = n;
        }
        search.TextChanged += (_, _) => Fill(search.Text, null);
        search.KeyDown += (_, k) =>
        {
            switch (k.KeyCode)
            {
                case Keys.Down: Move(+1); break;
                case Keys.Up: Move(-1); break;
                case Keys.PageDown: for (int i = 0; i < VisibleRows; i++) Move(+1); break;
                case Keys.PageUp: for (int i = 0; i < VisibleRows; i++) Move(-1); break;
                case Keys.Enter:
                    if (list.SelectedIndex >= 0 && items[list.SelectedIndex] is Flight chosen) Pick(chosen);
                    break;
                case Keys.Escape: _popup?.Close(); break;
                default: return;
            }
            k.Handled = true;
            k.SuppressKeyPress = true;
        };

        var popup = new ToolStripDropDown { Padding = Padding.Empty, DropShadowEnabled = true };
        host = new ToolStripControlHost(panel) { Margin = Padding.Empty, Padding = Padding.Empty, AutoSize = false };
        popup.AutoSize = false;
        popup.Items.Add(host);
        popup.Closed += (_, _) =>
        {
            _popup = null;
            if (IsHandleCreated && !IsDisposed) BeginInvoke(new Action(popup.Dispose)); else popup.Dispose();
            Invalidate();
        };
        _popup = popup;
        Fill("", Selected);
        popup.Show(this, new Point(0, Height + D(4)));
        search.Select();
    }

    private void DrawRow(DrawItemEventArgs e, IReadOnlyList<object> items)
    {
        if (e.Index < 0 || e.Index >= items.Count) return;
        bool highlighted = (e.State & DrawItemState.Selected) != 0;
        if (items[e.Index] is string heading)
        {
            using (var back = new SolidBrush(Color.White)) e.Graphics.FillRectangle(back, e.Bounds);
            TextRenderer.DrawText(e.Graphics, heading, HeadingFont, new Rectangle(e.Bounds.X + Gfx.S(this, 16), e.Bounds.Y + Gfx.S(this, 6), e.Bounds.Width, e.Bounds.Height - Gfx.S(this, 6)),
                Theme.TextSecondary, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            return;
        }
        var flight = (Flight)items[e.Index];
        using (var back = new SolidBrush(highlighted ? Theme.BrandSoft : Color.White)) e.Graphics.FillRectangle(back, e.Bounds);

        string time = AppData.FlightScheduledTimes.TryGetValue(flight.FlightNo, out TimeOnly t) ? t.ToString("h:mm tt") : "";
        string price = $"PHP {flight.BasePrice:N0}";
        int pad = Gfx.S(this, 16);
        Size priceSize = TextRenderer.MeasureText(price, BoldFont);
        TextRenderer.DrawText(e.Graphics, price, BoldFont, new Rectangle(e.Bounds.Right - priceSize.Width - pad, e.Bounds.Y, priceSize.Width, e.Bounds.Height),
            Theme.Brand, TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.NoPadding);
        int textWidth = e.Bounds.Width - priceSize.Width - pad * 3;
        var line1 = new Rectangle(e.Bounds.X + pad, e.Bounds.Y + Gfx.S(this, 5), textWidth, e.Bounds.Height / 2 - Gfx.S(this, 3));
        var line2 = new Rectangle(e.Bounds.X + pad, e.Bounds.Y + e.Bounds.Height / 2, textWidth, e.Bounds.Height / 2 - Gfx.S(this, 3));
        TextRenderer.DrawText(e.Graphics, $"{flight.Origin} → {flight.Destination}", BoldFont, line1, Theme.TextPrimary,
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(e.Graphics, flight.FlightNo + (time.Length > 0 ? $"  ·  departs {time}" : ""), Theme.Small, line2, Theme.TextSecondary,
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.VerticalCenter);
    }

    private static readonly Font HeadingFont = new(Theme.Small, FontStyle.Bold);

    // ------------------------------------------------------------------ keyboard, mouse, painting

    protected override void OnClick(EventArgs e)
    {
        OpenDropDown();
        base.OnClick(e);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) Focus();
        base.OnMouseDown(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter or Keys.F4 || (e.Alt && e.KeyCode == Keys.Down))
        {
            OpenDropDown();
            e.Handled = true;
        }
        else if (Enabled && e.KeyCode is Keys.Up or Keys.Down && _flights.Count > 0)
        {
            SelectedIndex = Math.Clamp(_selected + (e.KeyCode == Keys.Down ? 1 : -1), 0, _flights.Count - 1);
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        base.OnKeyDown(e);
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Space or Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnEnter(EventArgs e) { Invalidate(); base.OnEnter(e); }
    protected override void OnLeave(EventArgs e) { Invalidate(); base.OnLeave(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _popup?.Dispose();
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        Graphics g = e.Graphics;
        using (var back = new SolidBrush(BackColor)) g.FillRectangle(back, ClientRectangle);
        const TextFormatFlags flags = TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
        Color strong = Enabled ? Theme.TextPrimary : Theme.DisabledText, soft = Enabled ? Theme.TextSecondary : Theme.DisabledTextSoft;
        if (Selected is not { } flight)
        {
            TextRenderer.DrawText(g, "Choose a flight", Font, ClientRectangle, soft, flags);
            return;
        }
        string price = $"PHP {flight.BasePrice:N0}";
        int gap = Gfx.S(this, 12);
        Size priceSize = TextRenderer.MeasureText(price, BoldFont);
        bool priceFits = Width > priceSize.Width + Gfx.S(this, 200);
        int right = priceFits ? Width - priceSize.Width - gap : Width;
        if (priceFits)
            TextRenderer.DrawText(g, price, BoldFont, new Rectangle(Width - priceSize.Width, 0, priceSize.Width, Height), Enabled ? Theme.Brand : Theme.DisabledText,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.NoPadding);
        string route = $"{flight.Origin} → {flight.Destination}";
        int routeWidth = Math.Min(TextRenderer.MeasureText(route, BoldFont).Width, right);
        TextRenderer.DrawText(g, route, BoldFont, new Rectangle(0, 0, routeWidth, Height), strong, flags);
        if (right - routeWidth > Gfx.S(this, 60))
            TextRenderer.DrawText(g, flight.FlightNo, Font, new Rectangle(routeWidth + gap, 0, right - routeWidth - gap, Height), soft, flags);
    }
}
