using System.Text;

namespace SkyWings.UI;

/// <summary>Consistent light styling for the admin tables: friendly headers, currency formatting, status colours.</summary>
internal static class GridStyle
{
    /// <summary>Shared by every status cell; creating a Font per formatted cell would leak GDI handles.</summary>
    private static readonly Font StatusFont = new(Theme.Small, FontStyle.Bold);

    private static readonly Dictionary<string, string> Headers = new(StringComparer.Ordinal)
    {
        ["FlightNo"] = "Flight",
        ["DepartureDisplay"] = "Departure",
        ["BookedAt"] = "Booked",
        ["FlightDate"] = "Flight date",
        ["PaymentStatus"] = "Payment",
        ["PaxCount"] = "Passengers",
        ["BasePrice"] = "Fare",
        ["TotalAmount"] = "Total",
        ["RefundAmount"] = "Refund"
    };

    private static readonly System.Reflection.PropertyInfo? DoubleBufferedProperty =
        typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

    public static void Apply(DataGridView grid)
    {
        DoubleBufferedProperty?.SetValue(grid, true);   // DataGridView hides this property; it removes flicker while scrolling/rebinding
        grid.BorderStyle = BorderStyle.None;
        grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        grid.BackgroundColor = Color.White;
        grid.GridColor = Theme.Border;
        grid.EnableHeadersVisualStyles = false;
        grid.RowHeadersVisible = false;
        grid.AllowUserToAddRows = false;
        grid.AllowUserToDeleteRows = false;
        grid.AllowUserToResizeRows = false;
        grid.ReadOnly = true;
        grid.MultiSelect = false;
        grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        grid.ColumnHeadersHeight = Gfx.S(grid, 48);   // room for two-line headers such as "Total (PHP)"
        grid.RowTemplate.Height = Gfx.S(grid, 36);
        grid.Font = Theme.Body;

        grid.ColumnHeadersDefaultCellStyle.BackColor = Theme.PageBg;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = Theme.TextSecondary;
        grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Theme.PageBg;
        grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Theme.TextSecondary;
        grid.ColumnHeadersDefaultCellStyle.Font = StatusFont;
        grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(10, 0, 10, 0);

        grid.DefaultCellStyle.BackColor = Color.White;
        grid.DefaultCellStyle.ForeColor = Theme.TextPrimary;
        grid.DefaultCellStyle.SelectionBackColor = Theme.BrandSoft;
        grid.DefaultCellStyle.SelectionForeColor = Theme.TextPrimary;
        grid.DefaultCellStyle.Padding = new Padding(10, 0, 10, 0);
        grid.AlternatingRowsDefaultCellStyle.BackColor = Theme.RowAlt;

        grid.DataBindingComplete -= OnDataBindingComplete;
        grid.DataBindingComplete += OnDataBindingComplete;
        grid.CellFormatting -= OnCellFormatting;
        grid.CellFormatting += OnCellFormatting;
        grid.CellPainting -= OnCellPainting;
        grid.CellPainting += OnCellPainting;
    }

    private static readonly (Color Back, Color Fore) PillGood = (Color.FromArgb(0xDA, 0xEC, 0xE1), Color.FromArgb(0x31, 0x5B, 0x49));
    private static readonly (Color Back, Color Fore) PillBad = (Color.FromArgb(0xF5, 0xDD, 0xDA), Color.FromArgb(0x9A, 0x4E, 0x42));
    private static readonly (Color Back, Color Fore) PillDelayed = (Color.FromArgb(0xFC, 0xE4, 0xD8), Color.FromArgb(0xA8, 0x5A, 0x37));
    private static readonly (Color Back, Color Fore) PillPending = (Color.FromArgb(0xFA, 0xEB, 0xD0), Color.FromArgb(0x8E, 0x63, 0x38));

    private static readonly (Color Back, Color Fore) PillUnderway = (Color.FromArgb(0xDC, 0xE7, 0xF3), Color.FromArgb(0x3C, 0x6E, 0xAA));
    private static readonly (Color Back, Color Fore) PillNeutral = (Color.FromArgb(0xEC, 0xEF, 0xED), Color.FromArgb(0x5F, 0x6B, 0x65));

    /// <summary>
    /// Pill colours from the brand kit (Status_*.svg) for a status value, or null when the value is not a pill status.
    /// Trip statuses (Upcoming … Completed, No-show) are included; a return-leg label ("Return · In flight") looks like its phase.
    /// </summary>
    internal static (Color Back, Color Fore)? StatusStyle(string text)
    {
        string key = text.ToUpperInvariant();
        if (key.StartsWith("RETURN · ", StringComparison.Ordinal)) key = key["RETURN · ".Length..];
        return key switch
        {
            "CONFIRMED" or "ACTIVE" or "PAID" or "OPEN" or "SUCCESS" or "COMPLETED" or "DONE" => PillGood,
            "CANCELED" or "CANCELLED" or "INACTIVE" or "CLOSED" => PillBad,
            "DELAYED" or "NO-SHOW" => PillDelayed,
            "REFUNDED" or "PENDING" => PillPending,
            "CHECK-IN OPEN" or "CHECKED IN" or "BOARDING" or "IN FLIGHT" => PillUnderway,
            "UPCOMING" or "PAID · NOT USED" or "DECLINED" => PillNeutral,   // paid, but the traveller never flew
            _ => null
        };
    }

    /// <summary>Draws status values as small tinted pills instead of bare coloured text.</summary>
    private static void OnCellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
    {
        if (sender is not DataGridView grid || e.RowIndex < 0 || e.ColumnIndex < 0 || e.Graphics is null) return;
        if (e.Value is not string text || text.Length == 0) return;
        string name = grid.Columns[e.ColumnIndex].DataPropertyName;
        if (name is not ("Status" or "PaymentStatus") || StatusStyle(text) is not { } style) return;

        e.Paint(e.ClipBounds, DataGridViewPaintParts.Background | DataGridViewPaintParts.Border);
        Size size = TextRenderer.MeasureText(e.Graphics, text, StatusFont, Size.Empty, TextFormatFlags.NoPadding);
        int padX = Gfx.S(grid, 10), h = size.Height + Gfx.S(grid, 6);
        var pill = new Rectangle(e.CellBounds.Left + Gfx.S(grid, 10), e.CellBounds.Top + (e.CellBounds.Height - h) / 2, size.Width + padX * 2, h);
        Gfx.HighQuality(e.Graphics);
        using var path = Gfx.RoundedRect(pill, pill.Height / 2);
        using var back = new SolidBrush(style.Back);
        e.Graphics.FillPath(back, path);
        TextRenderer.DrawText(e.Graphics, text, StatusFont, pill, style.Fore,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        e.Handled = true;
    }

    private static void OnDataBindingComplete(object? sender, DataGridViewBindingCompleteEventArgs e)
    {
        // Grids with hand-made columns keep their own headers and formats.
        if (sender is not DataGridView { AutoGenerateColumns: true } grid) return;
        foreach (DataGridViewColumn column in grid.Columns)
        {
            string key = column.DataPropertyName.Length > 0 ? column.DataPropertyName : column.Name;
            column.HeaderText = Headers.TryGetValue(key, out string? friendly) ? friendly : Humanize(key);
            column.SortMode = DataGridViewColumnSortMode.Automatic;
            // Text columns (routes, names) get more room than numeric ones.
            column.FillWeight = column.ValueType == typeof(string) ? 130 : 90;
            if (column.ValueType == typeof(decimal))
            {
                column.DefaultCellStyle.Format = "N2";
                column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
                column.HeaderText += " (PHP)";
            }
            else if (column.ValueType == typeof(int) || column.ValueType == typeof(long))
            {
                column.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
                column.HeaderCell.Style.Alignment = DataGridViewContentAlignment.MiddleRight;
            }
        }
    }

    private static void OnCellFormatting(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (sender is not DataGridView grid || e.RowIndex < 0 || e.Value is not string text) return;
        string name = grid.Columns[e.ColumnIndex].DataPropertyName;
        if (name is not ("Status" or "PaymentStatus")) return;
        if (StatusStyle(text) is { } style)
        {
            e.CellStyle!.ForeColor = style.Fore;
            e.CellStyle.Font = StatusFont;
        }
    }

    private static string Humanize(string name)
    {
        var builder = new StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1])) builder.Append(' ');
            builder.Append(i == 0 ? name[i] : char.ToLowerInvariant(name[i]));
        }
        return builder.ToString();
    }
}
