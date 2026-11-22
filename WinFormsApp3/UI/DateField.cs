using System.ComponentModel;
using System.Globalization;

namespace SkyWings.UI;

/// <summary>
/// A date shown as plain text that opens a month calendar when clicked. It is what the rounded look uses instead of the native
/// <see cref="DateTimePicker"/>, which cannot be styled: it can be as tall as other fields and has no square drop-down button.
/// Space, F4 or Alt+Down open the calendar from the keyboard.
/// </summary>
public sealed class DateField : Control, ISelfStyled
{
    private static readonly Font TextFont = new(Theme.Body, FontStyle.Bold);

    private DateTime _value = DateTime.Today;
    private DateTime _minDate = new(1900, 1, 1);
    private string _format = "MMM d, yyyy";
    private ToolStripDropDown? _popup;

    public DateField()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.StandardClick, true);
        TabStop = true;
        Cursor = Cursors.Hand;
        Font = Theme.Body;
        BackColor = Color.White;
        AccessibleRole = AccessibleRole.ComboBox;
        Height = TextRenderer.MeasureText("Ag", TextFont).Height + 6;
        Text = DisplayText;   // a screen reader reads the date as the field's text
    }

    public event EventHandler? ValueChanged;

    /// <summary>The chosen day (no time of day). A value before <see cref="MinDate"/> becomes <see cref="MinDate"/>.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public DateTime Value
    {
        get => _value;
        set
        {
            DateTime day = value.Date < _minDate ? _minDate : value.Date;
            if (day == _value) return;
            _value = day;
            Text = DisplayText;
            Invalidate();
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>The earliest day that can be chosen. Raising it past the current value moves the value up to it.</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public DateTime MinDate
    {
        get => _minDate;
        set
        {
            _minDate = value.Date;
            if (_value < _minDate) Value = _minDate;
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string CustomFormat
    {
        get => _format;
        set { _format = value; Text = DisplayText; Invalidate(); }
    }

    internal string DisplayText => _value.ToString(_format, CultureInfo.InvariantCulture);

    internal bool IsOpen => _popup is not null;

    /// <summary>A key pressed inside the open calendar. Enter commits <paramref name="highlighted"/> and closes it; Escape closes it. True when handled.</summary>
    internal bool HandleCalendarKey(Keys key, DateTime highlighted)
    {
        switch (key)
        {
            case Keys.Enter:
                Pick(highlighted);
                _popup?.Close();
                return true;
            case Keys.Escape:
                _popup?.Close();
                return true;
            default:
                return false;
        }
    }

    /// <summary>Chooses a day from the calendar (or directly).</summary>
    internal void Pick(DateTime day) => Value = day;

    public void OpenDropDown()
    {
        if (!Enabled || _popup is not null) return;
        var calendar = new MonthCalendar { MinDate = _minDate, MaxSelectionCount = 1 };
        calendar.SetDate(_value);
        var popup = new ToolStripDropDown { Padding = Padding.Empty };
        calendar.DateSelected += (_, e) => { Pick(e.Start); popup.Close(); };
        // The arrow keys move the highlighted day inside the calendar; Enter then commits it and Escape cancels.
        calendar.KeyDown += (_, k) => { if (HandleCalendarKey(k.KeyCode, calendar.SelectionStart)) { k.Handled = true; k.SuppressKeyPress = true; } };
        popup.Items.Add(new ToolStripControlHost(calendar) { Margin = Padding.Empty, Padding = Padding.Empty });
        popup.Closed += (_, _) =>
        {
            _popup = null;
            // The calendar may still be inside its own DateSelected/KeyDown handler, so the popup is disposed once that has returned.
            if (IsHandleCreated && !IsDisposed) BeginInvoke(new Action(popup.Dispose)); else popup.Dispose();
            Invalidate();
        };
        _popup = popup;
        popup.Show(this, new Point(0, Height));
        calendar.Select();   // so the keyboard works at once
    }

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
        if (e.KeyCode is Keys.Space or Keys.F4 || (e.Alt && e.KeyCode == Keys.Down))
        {
            OpenDropDown();
            e.Handled = true;
        }
        else if (Enabled && e.KeyCode is Keys.Up or Keys.Down or Keys.PageUp or Keys.PageDown)
        {
            // Without the mouse: Up/Down move a day, Page Up/Down a month (never before the minimum date).
            Value = e.KeyCode switch
            {
                Keys.Up => _value.AddDays(1),
                Keys.Down => _value.AddDays(-1),
                Keys.PageUp => _value.AddMonths(1),
                _ => _value.AddMonths(-1)
            };
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
        base.OnKeyDown(e);
    }

    protected override bool IsInputKey(Keys keyData)
        => keyData is Keys.Space or Keys.Up or Keys.Down or Keys.PageUp or Keys.PageDown || base.IsInputKey(keyData);

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
        using (var back = new SolidBrush(BackColor)) e.Graphics.FillRectangle(back, ClientRectangle);
        TextRenderer.DrawText(e.Graphics, DisplayText, TextFont, ClientRectangle, Enabled ? Theme.TextPrimary : Theme.DisabledText,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
    }
}
