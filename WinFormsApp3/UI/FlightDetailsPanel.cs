using System.ComponentModel;
using SkyWings.Controllers;

namespace SkyWings.UI;

/// <summary>
/// The panel beside the home board for the selected flight: its number and route, the status and countdown, a timeline of the flight's stages,
/// the aircraft, one row per cabin (fare, seats left, how full) and the one booking button with the reason under it when it is off.
/// It draws itself; it only repaints when what it shows actually changes (the board refreshes its status every second).
/// </summary>
public sealed class FlightDetailsPanel : GlassCard
{
    private static readonly Font NumberFont = new(Theme.Subtitle, FontStyle.Bold);
    private static readonly Font BoldFont = new(Theme.Body, FontStyle.Bold);
    private static readonly Font SmallBold = new(Theme.Small, FontStyle.Bold);
    private static readonly Font CapsFont = new(Theme.Small, FontStyle.Bold);
    private static readonly Font TimelineFont = new("Segoe UI", 8.25f, FontStyle.Regular);   // the timeline labels sit side by side: a little smaller
    private static readonly Font TimelineBold = new("Segoe UI", 8.25f, FontStyle.Bold);

    private const TextFormatFlags Line = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;

    private readonly StatusPill _pill = new();
    private FlightDetails? _details;
    private int _selected;
    private BookingAction _action = new(BookAction.None, "Book →", "Choose a flight.");
    private string _key = "";

    public FlightDetailsPanel()
    {
        ContentPadding = 6;
        TabStop = true;
        SetStyle(ControlStyles.Selectable | ControlStyles.StandardClick, true);
        ActionButton.Click += (_, _) => ActionClicked?.Invoke(this, EventArgs.Empty);
        _pill.Visible = false;
        Controls.Add(ActionButton);
        Controls.Add(_pill);
        AccessibleRole = AccessibleRole.Pane;
        AccessibleName = "Flight details";
        AccessibleDescription = "No flight selected";
    }

    /// <summary>Raised with the index of the cabin row that was clicked or chosen with the arrow keys.</summary>
    public event EventHandler<int>? CabinChosen;

    /// <summary>Raised when the booking button is pressed.</summary>
    public event EventHandler? ActionClicked;

    internal GlassButton ActionButton { get; } = new() { Text = "Book →", Enabled = false };   // created first: setting ContentPadding below lays the panel out

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedCabin => _selected;

    /// <summary>The line under the button: why it is off, or empty.</summary>
    internal string Reason => _action.Reason;

    /// <summary>"Sold out" or "12 left" for a cabin row.</summary>
    internal string CabinSeatsText(int index)
        => _details is { } d && index >= 0 && index < d.Cabins.Count ? (d.Cabins[index].SoldOut ? "Sold out" : $"{d.Cabins[index].SeatsLeft:N0} left") : "";

    /// <summary>Shows a flight (or "No flight selected" for null), the chosen cabin and what the button does.</summary>
    public void ShowFlight(FlightDetails? details, int selectedCabin, BookingAction action)
    {
        string key = KeyOf(details, selectedCabin, action);
        if (key == _key) return;   // nothing visible changed: no repaint, no flicker
        _key = key;
        _details = details;
        _selected = details is null ? 0 : Math.Clamp(selectedCabin, 0, Math.Max(0, details.Cabins.Count - 1));
        _action = action;
        ActionButton.Text = action.Label;
        ActionButton.Enabled = action.Kind != BookAction.None;
        AccessibleDescription = details is null ? "No flight selected" : $"{details.Flight.FlightNo}, {details.Flight.Origin} to {details.Flight.Destination}, {details.Status.Label}";
        if (details is null) _pill.Visible = false;
        else
        {
            _pill.Text = details.Status.Label;
            (Color fill, Color text) = PillColours(details.Status.Kind);
            _pill.FillColor = fill;
            _pill.TextColor = text;
            _pill.Visible = true;
        }
        PlaceChildren();
        Invalidate();
    }

    private static string KeyOf(FlightDetails? d, int selected, BookingAction a)
        => d is null
            ? $"none|{a.Kind}|{a.Label}|{a.Reason}"
            : string.Join("|", d.Flight.FlightNo, d.Status.Label, d.Status.Countdown, d.DelayText, d.Aircraft, d.Duration, selected, a.Kind, a.Label, a.Reason,
                string.Join(";", d.Timeline.Select(t => $"{t.Label}/{t.Time}/{t.State}")),
                string.Join(";", d.Cabins.Select(c => $"{c.ClassName}/{c.Fare}/{c.SeatsLeft}/{c.LoadPercent}")));

    private static (Color Fill, Color Text) PillColours(FlightStatusKind kind) => kind switch
    {
        FlightStatusKind.Boarding or FlightStatusKind.GateClosing or FlightStatusKind.Delayed => (Gfx.Mix(Theme.Warning, Color.White, 0.86f), Theme.Warning),
        FlightStatusKind.Departed or FlightStatusKind.InFlight => (Gfx.Mix(Theme.Info, Color.White, 0.86f), Theme.Info),
        FlightStatusKind.CheckInOpen => (Theme.BrandSoft, Theme.Brand),
        FlightStatusKind.Landed => (Color.FromArgb(0xEC, 0xEF, 0xED), Theme.TextSecondary),
        _ => (Color.FromArgb(0xEC, 0xEF, 0xED), Theme.TextSecondary)
    };

    // ------------------------------------------------------------------ geometry

    private int S(int px) => Gfx.S(this, px);

    /// <summary>The y of each block, from the top of the content area; the same numbers draw and hit-test.</summary>
    private (int Header, int Timeline, int Aircraft, int CabinsTitle, int Rows, int RowHeight) Blocks()
    {
        int top = DisplayRectangle.Top;
        int header = top, timeline = header + S(76) + S(10), aircraft = timeline + S(74) + S(8);
        int cabinsTitle = aircraft + S(24), rows = cabinsTitle + S(22);
        return (header, timeline, aircraft, cabinsTitle, rows, S(54));
    }

    /// <summary>The bounds of a cabin row, in the panel's own coordinates (empty when there is no such row).</summary>
    internal Rectangle CabinRowBounds(int index)
    {
        if (_details is null || index < 0 || index >= _details.Cabins.Count) return Rectangle.Empty;
        var b = Blocks();
        Rectangle rc = DisplayRectangle;
        return new Rectangle(rc.Left, b.Rows + index * b.RowHeight, rc.Width, b.RowHeight - S(6));
    }

    private void PlaceChildren()
    {
        Rectangle rc = DisplayRectangle;
        ActionButton.Height = S(44);
        ActionButton.SetBounds(rc.Left, rc.Bottom - ActionButton.Height, rc.Width, ActionButton.Height);
        if (_pill.Visible)
        {
            int width = Math.Min(rc.Width / 2, Math.Max(S(96), TextRenderer.MeasureText(_pill.Text, SmallBold).Width + S(40)));
            _pill.SetBounds(rc.Right - width, rc.Top, width, S(32));
        }
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        PlaceChildren();
    }

    // ------------------------------------------------------------------ keyboard and mouse

    internal void ClickAt(Point point)
    {
        if (_details is null) return;
        for (int i = 0; i < _details.Cabins.Count; i++)
            if (CabinRowBounds(i).Contains(point))
            {
                _selected = i;
                Invalidate();
                CabinChosen?.Invoke(this, i);
                return;
            }
    }

    internal void PressKey(Keys key)
    {
        if (_details is null || _details.Cabins.Count == 0) return;
        int next = key switch { Keys.Down => _selected + 1, Keys.Up => _selected - 1, _ => _selected };
        if (next == _selected || next < 0 || next >= _details.Cabins.Count) return;
        _selected = next;
        Invalidate();
        CabinChosen?.Invoke(this, next);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        if (e.Button == MouseButtons.Left) ClickAt(e.Location);
        base.OnMouseDown(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        bool overRow = _details is not null && Enumerable.Range(0, _details.Cabins.Count).Any(i => CabinRowBounds(i).Contains(e.Location));
        Cursor = overRow ? Cursors.Hand : Cursors.Default;
        base.OnMouseMove(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Up or Keys.Down) { PressKey(e.KeyCode); e.Handled = true; }
        base.OnKeyDown(e);
    }

    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    // ------------------------------------------------------------------ painting

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);   // the white card
        Graphics g = e.Graphics;
        Gfx.HighQuality(g);
        Rectangle rc = DisplayRectangle;

        if (_details is not { } d)
        {
            TextRenderer.DrawText(g, "No flight selected", BoldFont, new Rectangle(rc.Left, rc.Top, rc.Width, S(30)), Theme.TextSecondary, Line);
            TextRenderer.DrawText(g, "Choose a flight on the board to see its status, timeline and cabins.", Theme.Small,
                new Rectangle(rc.Left, rc.Top + S(34), rc.Width, S(44)), Theme.TextSecondary, TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            DrawReason(g, rc);
            return;
        }

        var b = Blocks();
        // header: the flight number (the status pill sits at the right), the route, the countdown and the delay
        int numberWidth = Math.Max(S(40), rc.Width - _pill.Width - S(8));
        TextRenderer.DrawText(g, d.Flight.FlightNo, NumberFont, new Rectangle(rc.Left, b.Header, numberWidth, S(32)), Theme.Brand, Line);
        TextRenderer.DrawText(g, $"{d.Flight.Origin} → {d.Flight.Destination}", Theme.Body, new Rectangle(rc.Left, b.Header + S(34), rc.Width, S(22)), Theme.TextPrimary, Line);
        string gate = d.Status.Gate.Length > 0 ? $"Gate {d.Status.Gate} · " : "";
        string countdown = gate + d.Status.Countdown;
        TextRenderer.DrawText(g, countdown, Theme.Small, new Rectangle(rc.Left, b.Header + S(56), rc.Width, S(20)), Theme.TextSecondary, Line);
        if (d.DelayText.Length > 0)
        {
            int countdownWidth = TextRenderer.MeasureText(countdown, Theme.Small).Width;
            TextRenderer.DrawText(g, "· " + d.DelayText, SmallBold, new Rectangle(rc.Left + countdownWidth + S(6), b.Header + S(56), Math.Max(0, rc.Width - countdownWidth - S(6)), S(20)), Theme.Warning, Line);
        }

        DrawTimeline(g, rc, b.Timeline, d);

        string aircraft = d.Duration.Length > 0 ? $"{d.Aircraft} · {d.Duration}" : d.Aircraft;
        TextRenderer.DrawText(g, aircraft, Theme.Small, new Rectangle(rc.Left, b.Aircraft, rc.Width, S(22)), Theme.TextSecondary, Line);
        TextRenderer.DrawText(g, "CABINS", CapsFont, new Rectangle(rc.Left, b.CabinsTitle, rc.Width, S(20)), Theme.TextSecondary, Line);

        for (int i = 0; i < d.Cabins.Count; i++) DrawCabin(g, d.Cabins[i], CabinRowBounds(i), i == _selected);
        DrawReason(g, rc);
    }

    private void DrawTimeline(Graphics g, Rectangle rc, int top, FlightDetails d)
    {
        int count = d.Timeline.Count;
        if (count < 2) return;
        int inset = S(30), radius = S(6);   // half a label, so the first and last labels stay inside the card
        float step = (rc.Width - 2 * inset) / (float)(count - 1);
        int lineY = top + S(8);
        for (int i = 0; i < count - 1; i++)
        {
            bool done = d.Timeline[i].State == StageState.Done;
            using var pen = new Pen(done ? Theme.Brand : Theme.Border, S(3));
            g.DrawLine(pen, rc.Left + inset + i * step, lineY, rc.Left + inset + (i + 1) * step, lineY);
        }
        for (int i = 0; i < count; i++)
        {
            TimelineStage stage = d.Timeline[i];
            float cx = rc.Left + inset + i * step;
            switch (stage.State)
            {
                case StageState.Done:
                    using (var fill = new SolidBrush(Theme.Brand)) g.FillEllipse(fill, cx - radius, lineY - radius, 2 * radius, 2 * radius);
                    break;
                case StageState.Current:
                    using (var ring = new SolidBrush(Theme.BrandSoft)) g.FillEllipse(ring, cx - radius - S(4), lineY - radius - S(4), 2 * (radius + S(4)), 2 * (radius + S(4)));
                    using (var fill = new SolidBrush(Theme.Brand)) g.FillEllipse(fill, cx - radius, lineY - radius, 2 * radius, 2 * radius);
                    using (var core = new SolidBrush(Color.White)) g.FillEllipse(core, cx - S(2), lineY - S(2), S(4), S(4));
                    break;
                default:
                    using (var fill = new SolidBrush(Theme.Border)) g.FillEllipse(fill, cx - radius, lineY - radius, 2 * radius, 2 * radius);
                    break;
            }
            int width = (int)step;
            int left = Math.Clamp((int)cx - width / 2, rc.Left, Math.Max(rc.Left, rc.Right - width));   // the end labels stay inside the card
            string label = stage.Label == "Check-in open" ? "Check-in" : stage.Label;
            Color ink = stage.State == StageState.Upcoming ? Theme.TextSecondary : Theme.TextPrimary;
            TextRenderer.DrawText(g, label, stage.State == StageState.Current ? TimelineBold : TimelineFont, new Rectangle(left, top + S(20), width, S(18)), ink,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, stage.Time.Replace(", ", "\n"), Theme.Small, new Rectangle(left, top + S(38), width, S(34)), Theme.TextSecondary,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }
    }

    private void DrawCabin(Graphics g, CabinOption cabin, Rectangle row, bool selected)
    {
        using (GraphicsPath_ path = new(Gfx.RoundedRect(row, S(12))))
        {
            using (var fill = new SolidBrush(selected ? Theme.BrandSoft : Color.White)) g.FillPath(fill, path.Path);
            using var pen = new Pen(selected ? Theme.Brand : Theme.Border, selected ? 2f : 1f);
            g.DrawPath(pen, path.Path);
        }
        int pad = S(12);
        string fare = $"PHP {cabin.Fare:N0}";
        int fareWidth = TextRenderer.MeasureText(fare, BoldFont).Width;
        TextRenderer.DrawText(g, fare, BoldFont, new Rectangle(row.Right - pad - fareWidth, row.Top + S(6), fareWidth, S(22)), Theme.Brand,
            TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(g, cabin.ClassName, BoldFont, new Rectangle(row.Left + pad, row.Top + S(6), row.Width - 2 * pad - fareWidth - S(6), S(22)), Theme.TextPrimary, Line);

        string seats = cabin.SoldOut ? "Sold out" : $"{cabin.SeatsLeft:N0} left";
        Color seatsInk = cabin.SoldOut ? Theme.DangerRed : cabin.SeatsLeft < FlightRow.FewSeatsThreshold ? Theme.Warning : Theme.TextSecondary;
        TextRenderer.DrawText(g, seats, SmallBold, new Rectangle(row.Left + pad, row.Top + S(28), S(90), S(18)), seatsInk, Line);

        int barWidth = Math.Min(S(110), row.Width - S(90) - 2 * pad - S(10));
        if (barWidth > S(30))
        {
            var track = new RectangleF(row.Right - pad - barWidth, row.Top + S(34), barWidth, S(6));
            using (var path = Gfx.RoundedRect(track, S(3))) using (var back = new SolidBrush(Theme.StepPending)) g.FillPath(back, path);
            int percent = Math.Clamp(cabin.LoadPercent, 0, 100);
            if (percent > 0)
            {
                var filled = track with { Width = Math.Max(S(6), track.Width * percent / 100f) };
                Color colour = percent >= 98 ? Theme.DangerRed : percent >= 90 ? Theme.Warning : Theme.Brand;
                using var path = Gfx.RoundedRect(filled, S(3));
                using var brush = new SolidBrush(colour);
                g.FillPath(brush, path);
            }
        }
    }

    private void DrawReason(Graphics g, Rectangle rc)
    {
        if (_action.Reason.Length == 0) return;
        var area = new Rectangle(rc.Left, rc.Bottom - S(44) - S(34), rc.Width, S(30));
        TextRenderer.DrawText(g, _action.Reason, Theme.Small, area, Theme.TextSecondary,
            TextFormatFlags.Left | TextFormatFlags.Bottom | TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
    }

    /// <summary>Disposes a rounded-rectangle path when the block ends.</summary>
    private readonly struct GraphicsPath_ : IDisposable
    {
        public GraphicsPath_(System.Drawing.Drawing2D.GraphicsPath path) => Path = path;
        public System.Drawing.Drawing2D.GraphicsPath Path { get; }
        public void Dispose() => Path.Dispose();
    }
}
