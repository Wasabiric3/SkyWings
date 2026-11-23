namespace SkyWings.UI;

/// <summary>
/// A birthday typed as digits: only 0-9 is accepted and the dashes are added as you go (<c>19900423</c> becomes
/// <c>1990-04-23</c>), so there are no format mistakes and no dashes to type. Pasted text is cleaned the same way. The chevron
/// at the right of its rounded box (and F4 or Alt+Down) opens a calendar for picking the day with the mouse; click the calendar's
/// title to jump by month, then by year. The text stays in <see cref="AppData.BirthdayFormat"/>, which is what the booking rules read.
/// </summary>
public sealed class BirthdayBox : TextBox, IDropDownField
{
    private const int MaxDigits = 8;   // yyyyMMdd
    private ToolStripDropDown? _popup;
    private bool _formatting;

    public BirthdayBox()
    {
        PlaceholderText = AppData.BirthdayFormat;
        MaxLength = MaxDigits + 2;   // two dashes
        AccessibleDescription = "Type the digits, for example 19900423, or open the calendar";
    }

    /// <summary>"19900423" (or "1990-04-23", or "19900423999") becomes "1990-04-23"; the dash after a part appears with the next digit.</summary>
    internal static string FormatDigits(string? text)
    {
        string digits = new((text ?? "").Where(char.IsAsciiDigit).Take(MaxDigits).ToArray());
        if (digits.Length <= 4) return digits;
        if (digits.Length <= 6) return $"{digits[..4]}-{digits[4..]}";
        return $"{digits[..4]}-{digits[4..6]}-{digits[6..]}";
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        // Letters and symbols never get in; control keys (backspace, Ctrl+V and so on) still work.
        if (!char.IsControl(e.KeyChar) && !char.IsAsciiDigit(e.KeyChar)) e.Handled = true;
        base.OnKeyPress(e);
    }

    protected override void OnTextChanged(EventArgs e)
    {
        if (!_formatting)
        {
            string formatted = FormatDigits(Text);
            if (formatted != Text)
            {
                _formatting = true;
                try { Text = formatted; SelectionStart = formatted.Length; }
                finally { _formatting = false; }
            }
        }
        base.OnTextChanged(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.F4 || (e.Alt && e.KeyCode == Keys.Down))
        {
            OpenDropDown();
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        base.OnKeyDown(e);
    }

    /// <summary>Puts a day from the calendar in the box.</summary>
    internal void Pick(DateTime day) => Text = day.ToString(AppData.BirthdayFormat, System.Globalization.CultureInfo.InvariantCulture);

    public void OpenDropDown()
    {
        if (!Enabled || _popup is not null) return;
        DateTime today = DateTime.Today;
        // Start on the typed day when it is a real date, otherwise on a typical adult's birthday so the year is close.
        DateTime start = AppData.TryParseBirthday(Text, out DateTime typed) && typed <= today ? typed : today.AddYears(-30);
        var calendar = new MonthCalendar { MinDate = new DateTime(1900, 1, 1), MaxDate = today, MaxSelectionCount = 1 };
        calendar.SetDate(start);
        var popup = new ToolStripDropDown { Padding = Padding.Empty };
        calendar.DateSelected += (_, args) => { Pick(args.Start); popup.Close(); };
        calendar.KeyDown += (_, key) =>
        {
            if (key.KeyCode == Keys.Enter) { Pick(calendar.SelectionStart); popup.Close(); key.Handled = key.SuppressKeyPress = true; }
            else if (key.KeyCode == Keys.Escape) { popup.Close(); key.Handled = key.SuppressKeyPress = true; }
        };
        popup.Items.Add(new ToolStripControlHost(calendar) { Margin = Padding.Empty, Padding = Padding.Empty });
        popup.Closed += (_, _) =>
        {
            _popup = null;
            // The calendar may still be inside its own handler, so the popup is disposed once that has returned.
            if (IsHandleCreated && !IsDisposed) BeginInvoke(new Action(popup.Dispose)); else popup.Dispose();
            if (IsHandleCreated && !IsDisposed) { Focus(); SelectionStart = TextLength; }
        };
        _popup = popup;
        popup.Show(this, new Point(0, Height + Gfx.S(this, 8)));
        calendar.Select();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _popup?.Dispose();
        base.Dispose(disposing);
    }
}
