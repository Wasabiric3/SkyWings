namespace SkyWings.UI;

/// <summary>Small layout helpers shared by the dialogs.</summary>
internal static class Layouts
{
    /// <summary>
    /// Stacks controls top to bottom in argument order. Docked controls are laid out from the last-added backwards,
    /// so they are added in reverse. Returns the total height of the stack.
    /// </summary>
    public static int Stack(Control host, params Control[] controls)
    {
        int total = 0;
        foreach (var c in controls) { c.Dock = DockStyle.Top; total += c.Height; }
        for (int i = controls.Length - 1; i >= 0; i--) host.Controls.Add(controls[i]);
        // Adding in reverse also reversed the keyboard (Tab) order, so restore it to top-to-bottom.
        for (int i = 0; i < controls.Length; i++) controls[i].TabIndex = i;
        return total;
    }

    /// <summary>
    /// Keeps <paramref name="column"/> a fixed width and centred in <paramref name="page"/>, horizontally and a little above the middle
    /// vertically, and re-centres it whenever the page is resized. Used by the lookup screens so the form sits in the middle of the card.
    /// </summary>
    public static void CenterColumn(Control page, Control column, int width, int liftPx = 0)
    {
        column.Dock = DockStyle.None;
        column.Width = width;
        void Place()
        {
            column.Left = Math.Max(0, (page.ClientSize.Width - width) / 2);
            column.Top = Math.Max(0, (page.ClientSize.Height - column.Height) / 2 - liftPx);
        }
        page.Controls.Add(column);
        page.Resize += (_, _) => Place();
        page.VisibleChanged += (_, _) => Place();
        Place();
    }

    /// <summary>Puts controls side by side in equal columns, separated by <paramref name="gap"/> pixels.</summary>
    public static TableLayoutPanel Row(int gap, params Control[] columns)
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
            columns[i].Margin = new Padding(0, 0, i < columns.Length - 1 ? gap : 0, 0);
            table.Controls.Add(columns[i], i, 0);
        }
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        return table;
    }
}
