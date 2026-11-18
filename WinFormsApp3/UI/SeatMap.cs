using System.Drawing.Drawing2D;

namespace SkyWings.UI;

public enum SeatState { Free, Taken, Held, Selected }

/// <summary>
/// Custom-painted cabin seat map. One control paints every seat, so a 300-seat cabin stays fast.
/// Click or use the arrow keys and Space to pick seats; selection order maps to passenger order.
/// </summary>
public sealed class SeatMap : Control, ISelfStyled
{
    private char[] _letters = Array.Empty<char>();
    private int _aisleAfter;
    private int _rows;
    private string _className = AppData.ClassNames[0];
    private readonly HashSet<string> _taken = new();
    private readonly HashSet<string> _held = new();
    private readonly List<string> _selected = new();
    private readonly ToolTip _tip = new();
    private static readonly Font LabelFont = new(Theme.Small, FontStyle.Bold);   // shared; was recreated on every paint
    private (int Row, int Col) _hover = (-1, -1);
    private (int Row, int Col) _cursor = (0, 0);

    public SeatMap()
    {
        SetStyle(ControlStyles.UserPaint |
                 ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        TabStop = true;
        Font = Theme.Small;
        AccessibleName = "Seat map";
    }

    public event EventHandler? SelectionChanged;

    /// <summary>How many seats may be selected at once (one per passenger).</summary>
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public int MaxSelection { get; set; } = 1;

    public IReadOnlyList<string> Selected => _selected;

    public void Configure(string className, int rows)
    {
        _className = className;
        int index = SeatLayout.ClassIndex(className);
        _letters = AppData.ClassSeatLetters[index];
        _aisleAfter = AppData.ClassAisleAfter[index];
        _rows = Math.Max(1, rows);
        _selected.Clear();
        _cursor = (0, 0);
        UpdateSize();
        Invalidate();
    }

    public void SetUnavailable(IEnumerable<string> taken, IEnumerable<string> heldByOthers)
    {
        _taken.Clear();
        _held.Clear();
        foreach (string code in taken) _taken.Add(SeatLayout.Normalize(code));
        foreach (string code in heldByOthers) _held.Add(SeatLayout.Normalize(code));
        int removed = _selected.RemoveAll(code => _taken.Contains(SeatLayout.Normalize(code)) || _held.Contains(SeatLayout.Normalize(code)));
        Invalidate();
        if (removed > 0) SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SetSelected(IEnumerable<string> seats)
    {
        _selected.Clear();
        foreach (string seat in seats.Take(Math.Max(1, MaxSelection))) _selected.Add(seat);
        Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ClearSelection()
    {
        if (_selected.Count == 0) return;
        _selected.Clear();
        Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    // ---- geometry ----
    private int SeatW => Gfx.S(this, 38);
    private int SeatH => Gfx.S(this, 34);
    private int Gap => Gfx.S(this, 6);
    private int Aisle => Gfx.S(this, 30);
    private int LabelW => Gfx.S(this, 34);
    private int HeaderH => Gfx.S(this, 32);
    private int Pad => Gfx.S(this, 8);
    private bool HasAisle => _aisleAfter < _letters.Length - 1;

    private int ColX(int col) => Pad + LabelW + col * (SeatW + Gap) + (HasAisle && col > _aisleAfter ? Aisle : 0);
    private int RowY(int row) => HeaderH + Pad + row * (SeatH + Gap);

    private void UpdateSize()
    {
        if (_letters.Length == 0) return;
        int width = Pad * 2 + LabelW * 2 + _letters.Length * SeatW + (_letters.Length - 1) * Gap + (HasAisle ? Aisle : 0);
        int height = HeaderH + Pad * 2 + _rows * (SeatH + Gap);
        Size = new Size(width, height);
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        UpdateSize();
    }

    private Rectangle SeatRect(int row, int col) => new(ColX(col), RowY(row), SeatW, SeatH);

    private (int Row, int Col) HitTest(Point p)
    {
        for (int r = 0; r < _rows; r++)
        {
            if (p.Y < RowY(r) || p.Y > RowY(r) + SeatH) continue;
            for (int c = 0; c < _letters.Length; c++)
                if (SeatRect(r, c).Contains(p)) return (r, c);
        }
        return (-1, -1);
    }

    private string CodeAt(int row, int col) => SeatLayout.Code(_className, row + 1, _letters[col]);

    public SeatState StateOf(string code)
    {
        if (_selected.Contains(code)) return SeatState.Selected;
        string normalized = SeatLayout.Normalize(code);
        if (_taken.Contains(normalized)) return SeatState.Taken;
        if (_held.Contains(normalized)) return SeatState.Held;
        return SeatState.Free;
    }

    // ---- interaction ----
    private void Toggle(int row, int col)
    {
        string code = CodeAt(row, col);
        switch (StateOf(code))
        {
            case SeatState.Taken:
            case SeatState.Held:
                System.Media.SystemSounds.Beep.Play();
                return;
            case SeatState.Selected:
                _selected.Remove(code);
                break;
            default:
                if (_selected.Count >= MaxSelection)
                {
                    if (MaxSelection == 1) _selected.Clear();
                    else { System.Media.SystemSounds.Beep.Play(); return; }
                }
                _selected.Add(code);
                break;
        }
        Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        var hit = HitTest(e.Location);
        if (hit != _hover)
        {
            var previous = _hover;
            _hover = hit;
            if (hit.Row >= 0)
            {
                string code = CodeAt(hit.Row, hit.Col);
                string state = StateOf(code) switch
                {
                    SeatState.Taken => "Taken",
                    SeatState.Held => "Held by another traveller",
                    SeatState.Selected => "Your seat",
                    _ => "Available"
                };
                _tip.SetToolTip(this, $"{code} · {SeatLayout.Position(_className, hit.Col)} · {state}");
            }
            else _tip.SetToolTip(this, "");
            Cursor = hit.Row >= 0 && StateOf(CodeAt(hit.Row, hit.Col)) is SeatState.Free or SeatState.Selected ? Cursors.Hand : Cursors.Default;
            InvalidateSeat(previous);
            InvalidateSeat(hit);
        }
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        var previous = _hover;
        _hover = (-1, -1);
        InvalidateSeat(previous);
        base.OnMouseLeave(e);
    }

    /// <summary>Repaints one seat (plus room for its focus ring) instead of the whole map.</summary>
    private void InvalidateSeat((int Row, int Col) seat)
    {
        if (seat.Row < 0 || seat.Row >= _rows || seat.Col < 0 || seat.Col >= _letters.Length) return;
        Invalidate(Rectangle.Inflate(SeatRect(seat.Row, seat.Col), Gfx.S(this, 4), Gfx.S(this, 4)));
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        Focus();
        var hit = HitTest(e.Location);
        if (e.Button == MouseButtons.Left && hit.Row >= 0)
        {
            _cursor = hit;
            Toggle(hit.Row, hit.Col);
        }
        base.OnMouseDown(e);
    }

    protected override bool IsInputKey(Keys keyData)
        => keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down or Keys.Space || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_letters.Length == 0) { base.OnKeyDown(e); return; }
        switch (e.KeyCode)
        {
            case Keys.Left: _cursor.Col = Math.Max(0, _cursor.Col - 1); break;
            case Keys.Right: _cursor.Col = Math.Min(_letters.Length - 1, _cursor.Col + 1); break;
            case Keys.Up: _cursor.Row = Math.Max(0, _cursor.Row - 1); break;
            case Keys.Down: _cursor.Row = Math.Min(_rows - 1, _cursor.Row + 1); break;
            case Keys.Space: Toggle(_cursor.Row, _cursor.Col); break;
            default: base.OnKeyDown(e); return;
        }
        e.Handled = true;
        Invalidate();
    }

    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    // ---- painting ----
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_letters.Length == 0) return;
        var g = e.Graphics;
        using (var surface = new SolidBrush(Gfx.Surface(this))) g.FillRectangle(surface, e.ClipRectangle);
        Gfx.HighQuality(g);

        using var headerFormat = Gfx.Format(StringAlignment.Center);
        using var mutedBrush = new SolidBrush(Theme.TextSecondary);
        var numberFont = LabelFont;
        for (int c = 0; c < _letters.Length; c++)
            g.DrawString(_letters[c].ToString(), numberFont, mutedBrush,
                new RectangleF(ColX(c), Pad, SeatW, HeaderH - Pad), headerFormat);

        float radius = Gfx.S(this, 9f);
        for (int r = 0; r < _rows; r++)
        {
            // Partial repaints (hover) only need the rows that touch the dirty rectangle.
            if (RowY(r) + SeatH + Gap < e.ClipRectangle.Top || RowY(r) - Gap > e.ClipRectangle.Bottom) continue;
            var leftLabel = new RectangleF(Pad, RowY(r), LabelW - Gap, SeatH);
            var rightLabel = new RectangleF(ColX(_letters.Length - 1) + SeatW + Gap, RowY(r), LabelW - Gap, SeatH);
            g.DrawString((r + 1).ToString(), Font, mutedBrush, leftLabel, headerFormat);
            g.DrawString((r + 1).ToString(), Font, mutedBrush, rightLabel, headerFormat);

            for (int c = 0; c < _letters.Length; c++)
                DrawSeat(g, r, c, radius, numberFont, headerFormat);
        }
    }

    private void DrawSeat(Graphics g, int row, int col, float radius, Font font, StringFormat center)
    {
        string code = CodeAt(row, col);
        SeatState state = StateOf(code);
        bool hover = _hover == (row, col);
        Rectangle rect = SeatRect(row, col);
        var bounds = new RectangleF(rect.X + 0.5f, rect.Y + 0.5f, rect.Width - 1, rect.Height - 1);

        var (fill, border, text) = SeatPalette.For(state, hover);
        string label = state switch
        {
            SeatState.Taken => "✕",
            SeatState.Held => "•••",
            SeatState.Selected => (_selected.IndexOf(code) + 1).ToString(),
            _ => code   // free seats show their code (e.g. 12A), as in the brand kit
        };

        using var path = Gfx.RoundedRect(bounds, radius);
        using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);
        using (var pen = new Pen(border, 1.2f)) g.DrawPath(pen, path);

        using (var textBrush = new SolidBrush(text))
            g.DrawString(label, font, textBrush, bounds, center);

        if (Focused && _cursor == (row, col) && ShowFocusCues)
        {
            using var ring = new Pen(Color.FromArgb(200, Theme.Primary), 2f);
            using var ringPath = Gfx.RoundedRect(RectangleF.Inflate(bounds, 2.5f, 2.5f), radius + 2);
            g.DrawPath(ring, ringPath);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _tip.Dispose();
        base.Dispose(disposing);
    }
}

/// <summary>Colour key for the seat map.</summary>
public sealed class SeatLegend : Control, ISelfStyled
{
    public SeatLegend()
    {
        SetStyle(ControlStyles.UserPaint |
                 ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Height = Gfx.S(this, 30);
        Font = Theme.Small;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        using (var surface = new SolidBrush(Gfx.Surface(this))) g.FillRectangle(surface, ClientRectangle);
        Gfx.HighQuality(g);
        var items = new (string Label, Color Fill, Color Border)[]
        {
            ("Available", SeatPalette.For(SeatState.Free, false).Fill, SeatPalette.For(SeatState.Free, false).Border),
            ("Selected", SeatPalette.For(SeatState.Selected, false).Fill, SeatPalette.For(SeatState.Selected, false).Border),
            ("Held by someone", SeatPalette.For(SeatState.Held, false).Fill, SeatPalette.For(SeatState.Held, false).Border),
            ("Taken", SeatPalette.For(SeatState.Taken, false).Fill, SeatPalette.For(SeatState.Taken, false).Border)
        };
        float x = 0, size = Gfx.S(this, 16f);
        using var brush = new SolidBrush(Theme.TextSecondary);
        using var format = Gfx.Format(StringAlignment.Near);
        foreach (var (label, fill, border) in items)
        {
            var swatch = new RectangleF(x, (Height - size) / 2, size, size);
            using var path = Gfx.RoundedRect(swatch, Gfx.S(this, 4f));
            using (var fillBrush = new SolidBrush(fill)) g.FillPath(fillBrush, path);
            using (var pen = new Pen(border, 1f)) g.DrawPath(pen, path);
            float textW = TextRenderer.MeasureText(label, Font).Width + Gfx.S(this, 6f);
            g.DrawString(label, Font, brush, new RectangleF(x + size + Gfx.S(this, 6f), 0, textW, Height), format);
            x += size + Gfx.S(this, 6f) + textW + Gfx.S(this, 14f);
        }
    }
}
