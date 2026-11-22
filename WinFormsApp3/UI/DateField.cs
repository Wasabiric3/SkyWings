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
        set { _format = value; Invalidate(); }
    }

    internal string DisplayText => _value.ToString(_format, CultureInfo.InvariantCulture);

    internal bool IsOpen => _popup is not null;

    /// <summary>Chooses a day from the calendar (or directly).</summary>
    internal void Pick(DateTime day) => Value = day;

    public void OpenDropDown()
    {
        if (!Enabled || _popup is not null) return;
        var calendar = new MonthCalendar { MinDate = _minDate, MaxSelectionCount = 1 };
        calendar.SetDate(_value);
        var popup = new ToolStripDropDown { Padding = Padding.Empty };
        calendar.DateSelected += (_, e) => { Pick(e.Start); popup.Close(); };
        popup.Items.Add(new ToolStripControlHost(calendar) { Margin = Padding.Empty, Padding = Padding.Empty });
        popup.Closed += (_, _) => { _popup = null; popup.Dispose(); Invalidate(); };
        _popup = popup;
        popup.Show(this, new Point(0, Height));
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
        base.OnKeyDown(e);
    }

    protected override bool IsInputKey(Keys keyData) => keyData == Keys.Space || base.IsInputKey(keyData);

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
