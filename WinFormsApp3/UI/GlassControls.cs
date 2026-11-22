using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace SkyWings.UI;

/// <summary>Flat rounded white card with a thin border. Child controls are laid out inside <see cref="DisplayRectangle"/>.</summary>
public class GlassCard : Control
{
    private int _contentPadding = 24;

    public GlassCard()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = Theme.BgCard;
    }

    [DefaultValue(16)] public int Radius { get; set; } = 16;

    /// <summary>Kept for layout compatibility: it still sets the margin around the card, but no shadow is drawn.</summary>
    [DefaultValue(10)] public int ShadowSize { get; set; } = 10;

    /// <summary>Ignored. Cards are opaque; kept so existing callers still compile.</summary>
    [DefaultValue(210)] public int Opacity { get; set; } = 210;

    /// <summary>Space between the card edge and its content, in 96-dpi pixels. Used instead of <see cref="Control.Padding"/>.</summary>
    [DefaultValue(24)]
    public int ContentPadding
    {
        get => _contentPadding;
        set { _contentPadding = value; PerformLayout(); Invalidate(); }
    }

    private RectangleF CardBounds()
    {
        float s = Gfx.S(this, ShadowSize);
        return new RectangleF(s, s, Width - 2 * s - 1, Height - 2 * s - 1);
    }

    public override Rectangle DisplayRectangle
    {
        get
        {
            int inset = Gfx.S(this, ShadowSize) + Gfx.S(this, _contentPadding);
            return new Rectangle(inset, inset, Math.Max(0, Width - 2 * inset), Math.Max(0, Height - 2 * inset));
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        // Paint the margin ourselves instead of being transparent: no repaint cascade through the parent chain.
        using (var surface = new SolidBrush(Gfx.Surface(this))) g.FillRectangle(surface, e.ClipRectangle);

        RectangleF card = CardBounds();
        if (card.Width < 4 || card.Height < 4) return;
        Gfx.HighQuality(g);
        using var path = Gfx.RoundedRect(card, Gfx.S(this, Radius));
        using (var fill = new SolidBrush(BackColor)) g.FillPath(fill, path);
        using var edge = new Pen(Theme.BorderLight, 1f);
        g.DrawPath(edge, path);
    }
}

/// <summary><see cref="Outline"/> (white with a border) is the Figma look's secondary button; it is appended so the older values keep their numbers.</summary>
public enum GlassButtonStyle { Primary, Secondary, Ghost, Danger, Outline }

/// <summary>Rounded button with hover/pressed/focus states. Behaves like a normal button (AcceptButton, DialogResult).</summary>
public sealed class GlassButton : Control, IButtonControl, ISelfStyled
{
    // One font shared by every button; creating a Font per button leaks GDI handles.
    private static readonly Font ButtonFont = new(Theme.Body, FontStyle.Bold);

    private bool _hover, _pressed, _isDefault;
    private GlassButtonStyle _style = GlassButtonStyle.Primary;

    public GlassButton()
    {
        SetStyle(ControlStyles.UserPaint |
                 ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.StandardClick, true);
        TabStop = true;
        Cursor = Cursors.Hand;
        Font = ButtonFont;
        Height = Gfx.S(this, 40);
        Margin = new Padding(0, 0, Gfx.S(this, 10), 0);
    }

    [DefaultValue(GlassButtonStyle.Primary)]
    public GlassButtonStyle Style
    {
        get => _style;
        set { _style = value; Invalidate(); }
    }

    private bool _pill;

    /// <summary>The Figma look: fully rounded ends, and a deep-green (<see cref="Theme.Brand"/>) primary button.</summary>
    [DefaultValue(false)]
    public bool Pill
    {
        get => _pill;
        set { _pill = value; Invalidate(); }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public DialogResult DialogResult { get; set; } = DialogResult.None;
    public void NotifyDefault(bool value) { _isDefault = value; Invalidate(); }
    public void PerformClick() { if (CanSelect && Enabled) OnClick(EventArgs.Empty); }

    // A plain Control is 0x0 by default (unlike Button), so give buttons a sensible starting size.
    protected override Size DefaultSize => new(120, 40);

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        // Free-standing buttons (flow panels, manual layout) fit their text; docked buttons keep their dock size.
        if (Dock == DockStyle.None) Width = GetPreferredSize(Size.Empty).Width;
        Parent?.PerformLayout(this, nameof(Text));
        Invalidate();
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        Size text = TextRenderer.MeasureText(Text, Font);
        return new Size(Math.Max(Gfx.S(this, 104), text.Width + Gfx.S(this, 40)), Math.Max(Height, Gfx.S(this, 40)));
    }

    protected override void OnClick(EventArgs e)
    {
        if (DialogResult != DialogResult.None && FindForm() is { } form) form.DialogResult = DialogResult;
        base.OnClick(e);
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left) { _pressed = true; Focus(); Invalidate(); }
        base.OnMouseDown(e);
    }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter) { PerformClick(); e.Handled = true; }
        base.OnKeyUp(e);
    }

    protected override bool IsInputKey(Keys keyData) => keyData == Keys.Space || base.IsInputKey(keyData);

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        using (var surface = new SolidBrush(Gfx.Surface(this))) g.FillRectangle(surface, ClientRectangle);
        Gfx.HighQuality(g);
        var bounds = new RectangleF(1, 1, Width - 3, Height - 3);
        float radius = _pill ? bounds.Height / 2 : Height * 0.27f;   // the brand kit's buttons round to ~27% of their height

        Color fill, border = Color.Transparent, text;
        switch (_style)
        {
            case GlassButtonStyle.Outline:
                fill = _pressed ? Theme.BrandSoft : Color.White;
                border = _hover || _pressed ? Theme.Brand : Theme.Border;
                text = Theme.Brand;
                break;
            case GlassButtonStyle.Secondary:
                fill = _pressed ? Gfx.Mix(Theme.SoftFill, Theme.Primary, 0.25f) : _hover ? Gfx.Mix(Theme.SoftFill, Theme.Primary, 0.12f) : Theme.SoftFill;
                text = Theme.Accent;
                break;
            case GlassButtonStyle.Ghost:
                fill = _pressed ? Gfx.Mix(Theme.SoftFill, Theme.Primary, 0.25f) : _hover ? Theme.SoftFill : Color.Transparent;
                text = Theme.Accent;
                break;
            case GlassButtonStyle.Danger:
                fill = _pressed ? Color.FromArgb(200, 48, 40) : _hover ? Color.FromArgb(235, 80, 70) : Theme.DangerRed;
                text = Color.White;
                break;
            default:
                if (_pill)
                    fill = _pressed ? Gfx.Mix(Theme.Brand, Color.Black, 0.3f) : _hover ? Gfx.Mix(Theme.Brand, Color.Black, 0.15f) : Theme.Brand;
                else
                    fill = _pressed ? Theme.AccentDark : _hover ? Theme.Accent : Theme.Primary;
                text = Color.White;
                break;
        }

        if (!Enabled)
        {
            fill = _style == GlassButtonStyle.Ghost ? Color.Transparent : Theme.DisabledBack;
            border = Color.Transparent;
            text = Theme.DisabledText;
        }

        using var path = Gfx.RoundedRect(bounds, radius);
        if (fill.A > 0)
        {
            using var brush = new SolidBrush(fill);
            g.FillPath(brush, path);
        }
        if (border.A > 0)
        {
            using var pen = new Pen(border, 1f);
            g.DrawPath(pen, path);
        }
        if (Focused && ShowFocusCues)
        {
            using var ring = new Pen(Color.FromArgb(190, Theme.Primary), 2f);
            RectangleF outer = RectangleF.Inflate(bounds, 1.5f, 1.5f);
            using var ringPath = Gfx.RoundedRect(outer, radius + 2);
            g.DrawPath(ring, ringPath);
        }

        using var textBrush = new SolidBrush(text);
        using var format = Gfx.Format(StringAlignment.Center);
        g.DrawString(Text, Font, textBrush, new RectangleF(0, 0, Width, Height - 1), format);
    }
}

/// <summary>Horizontal numbered progress indicator. Completed steps can be clicked to go back.</summary>
public sealed class StepBar : Control, ISelfStyled
{
    private readonly string[] _steps;
    private int _current;
    private int _furthest;

    public StepBar(string[] steps)
    {
        _steps = steps;
        SetStyle(ControlStyles.UserPaint |
                 ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);
        Height = Gfx.S(this, 70);
        Font = Theme.Small;
        AccessibleName = "Booking progress";
    }

    public event EventHandler<int>? StepClicked;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Current
    {
        get => _current;
        set { _current = value; _furthest = Math.Max(_furthest, value); Invalidate(); }
    }

    public int Furthest => _furthest;

    private float SlotWidth => _steps.Length == 0 ? 0 : Width / (float)_steps.Length;
    private float CenterX(int i) => SlotWidth * (i + 0.5f);
    private float Diameter => Gfx.S(this, 28f);
    private float CenterY => Gfx.S(this, 6f) + Diameter / 2;

    private int HitStep(Point p)
    {
        for (int i = 0; i < _steps.Length; i++)
        {
            var area = new RectangleF(CenterX(i) - SlotWidth / 2, 0, SlotWidth, Height);
            if (area.Contains(p)) return i;
        }
        return -1;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        int i = HitStep(e.Location);
        Cursor = i >= 0 && i <= _furthest && i != _current ? Cursors.Hand : Cursors.Default;
        base.OnMouseMove(e);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        int i = HitStep(e.Location);
        if (i >= 0 && i <= _furthest && i != _current) StepClicked?.Invoke(this, i);
        base.OnMouseClick(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        using (var surface = new SolidBrush(Gfx.Surface(this))) g.FillRectangle(surface, ClientRectangle);
        Gfx.HighQuality(g);
        float d = Diameter, cy = CenterY, gap = Gfx.S(this, 8f);

        for (int i = 0; i < _steps.Length - 1; i++)
        {
            using var pen = new Pen(i < _current ? Theme.Primary : Theme.Border, Gfx.S(this, 2f));
            g.DrawLine(pen, CenterX(i) + d / 2 + gap, cy, CenterX(i + 1) - d / 2 - gap, cy);
        }

        _numberFont ??= new Font(Theme.Small, FontStyle.Bold);
        _boldFont ??= new Font(Font, FontStyle.Bold);
        _regularFont ??= new Font(Font, FontStyle.Regular);
        using var labelFormat = Gfx.Format(StringAlignment.Center, StringAlignment.Near);
        using var centerFormat = Gfx.Format(StringAlignment.Center);
        for (int i = 0; i < _steps.Length; i++)
        {
            var circle = new RectangleF(CenterX(i) - d / 2, cy - d / 2, d, d);
            bool done = i < _current, current = i == _current;
            // Flat circles: active = Primary, done = Accent, pending = StepPending.
            using (var brush = new SolidBrush(current ? Theme.Primary : done ? Theme.Accent : Theme.StepPending))
                g.FillEllipse(brush, circle);

            Color numberColor = current || done ? Color.White : Theme.TextSecondary;
            using var numberBrush = new SolidBrush(numberColor);
            g.DrawString(done ? "✓" : (i + 1).ToString(), _numberFont, numberBrush, circle, centerFormat);

            Color labelColor = current ? Theme.TextPrimary : done ? Theme.Accent : Theme.TextSecondary;
            using var labelBrush = new SolidBrush(labelColor);
            var labelRect = new RectangleF(CenterX(i) - SlotWidth / 2 + 2, cy + d / 2 + Gfx.S(this, 6f), SlotWidth - 4, Gfx.S(this, 22f));
            g.DrawString(_steps[i], current ? _boldFont : _regularFont, labelBrush, labelRect, labelFormat);
        }
    }

    // Fonts are created once and reused; creating them on every paint is what made this control allocate heavily.
    private Font? _numberFont, _boldFont, _regularFont;

    private void DisposeFonts()
    {
        _numberFont?.Dispose(); _boldFont?.Dispose(); _regularFont?.Dispose();
        _numberFont = _boldFont = _regularFont = null;
    }

    protected override void OnFontChanged(EventArgs e) { DisposeFonts(); base.OnFontChanged(e); }

    protected override void Dispose(bool disposing)
    {
        if (disposing) DisposeFonts();
        base.Dispose(disposing);
    }
}

/// <summary>Caption, input and inline message stacked in one row, so inputs always get the full column width.</summary>
public sealed class FieldRow : Control, ISelfStyled
{
    private static readonly Font CaptionFont = new(Theme.Small, FontStyle.Bold);   // shared: one Font per row would leak GDI handles

    private readonly Label _caption = new();
    private readonly Label _message = new();

    private readonly RoundedInput? _host;

    /// <param name="rounded">The Figma look: the input sits in a rounded box that draws the focus and error outline.</param>
    public FieldRow(string caption, Control input, bool rounded = false)
    {
        // No transparency: the row simply inherits its parent's colour (ambient BackColor), so nothing repaints through it.
        Input = input;
        TabStop = false;   // the row is only a container; Tab goes straight to its input
        if (rounded) _host = new RoundedInput(input);

        _caption.Text = caption;
        _caption.Font = CaptionFont;
        _caption.ForeColor = Theme.TextSecondary;
        _caption.AutoSize = false;
        _caption.TextAlign = ContentAlignment.BottomLeft;
        _caption.Dock = DockStyle.Top;

        input.Font = Theme.Body;
        if (input is TextBox or ComboBox or DateTimePicker or NumericUpDown) input.BackColor = Color.White;
        input.Margin = Padding.Empty;
        // The box that holds the input is what docks in the row, so it takes the input's place in the stack.
        Control body = _host ?? input;
        if (_host is not null) _host.Dock = DockStyle.Top; else input.Dock = DockStyle.Top;

        _message.Font = Theme.Small;
        _message.AutoSize = false;
        _message.Dock = DockStyle.Top;
        _message.TextAlign = ContentAlignment.TopLeft;

        // Owner-drawn combo boxes only reach their final height once their handle exists.
        body.SizeChanged += (_, _) => ApplySizes();

        // Room for the focus / error outline painted around the input (native boxes can't draw their own).
        Padding = new Padding(2, 0, 2, 0);
        input.Enter += (_, _) => Invalidate();
        input.Leave += (_, _) => Invalidate();
        input.EnabledChanged += (_, _) => Invalidate();

        Controls.Add(_message);
        Controls.Add(body);
        Controls.Add(_caption);
        Dock = DockStyle.Top;
        ApplySizes();
    }

    public Control Input { get; }

    /// <summary>The text currently shown under the input (empty when there is none).</summary>
    public string Message => _message.Text;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Caption
    {
        get => _caption.Text;
        set => _caption.Text = value;
    }

    private void ApplySizes()
    {
        _caption.Height = Gfx.S(this, 24);
        _message.Height = Gfx.S(this, 22);
        Height = _caption.Height + (_host ?? Input).Height + _message.Height + Gfx.S(this, 2);
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        ApplySizes();
    }

    private bool _hasError;

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_host is not null || !Input.Visible || !Input.Enabled) return;   // the rounded box draws its own outline
        bool focused = Input.ContainsFocus;
        if (!focused && !_hasError) return;
        Gfx.HighQuality(e.Graphics);
        var ring = Rectangle.Inflate(Input.Bounds, 2, 2);
        ring.Width -= 1; ring.Height -= 1;
        using var pen = new Pen(_hasError ? Theme.DangerRed : Theme.Primary, 2f);
        e.Graphics.DrawRectangle(pen, ring);
    }

    public void SetMessage(string? text, bool isError = true)
    {
        _message.Text = text ?? "";
        _message.ForeColor = isError ? Theme.DangerRed : Theme.Accent;
        _hasError = isError && !string.IsNullOrEmpty(text);
        if (_host is not null) _host.HasError = _hasError;
        Invalidate();
    }

    public void ClearMessage() => SetMessage(null);
}

/// <summary>Selectable card used for cabin classes.</summary>
public sealed class ChoiceCard : Control, ISelfStyled
{
    private static readonly Font TitleFont = new(Theme.Body, FontStyle.Bold);

    private bool _hover;
    private bool _selected;

    public ChoiceCard()
    {
        SetStyle(ControlStyles.UserPaint |
                 ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.StandardClick, true);
        TabStop = true;
        Cursor = Cursors.Hand;
        Height = Gfx.S(this, 92);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Title { get; set; } = "";

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Subtitle { get; set; } = "";

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Detail { get; set; } = "";

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Selected
    {
        get => _selected;
        set { _selected = value; Invalidate(); }
    }

    protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnMouseDown(MouseEventArgs e) { Focus(); base.OnMouseDown(e); }
    protected override void OnKeyUp(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Space or Keys.Enter) { OnClick(EventArgs.Empty); e.Handled = true; }
        base.OnKeyUp(e);
    }
    protected override bool IsInputKey(Keys keyData) => keyData == Keys.Space || base.IsInputKey(keyData);

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        using (var surface = new SolidBrush(Gfx.Surface(this))) g.FillRectangle(surface, ClientRectangle);
        Gfx.HighQuality(g);
        var bounds = new RectangleF(2, 2, Width - 5, Height - 5);
        using var path = Gfx.RoundedRect(bounds, Gfx.S(this, 14));
        Color fill = !Enabled ? Theme.DisabledBack : _selected ? Theme.SoftFill : _hover ? Theme.BgMuted : Theme.BgCard;
        using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);
        using (var pen = new Pen(_selected && Enabled ? Theme.Primary : Theme.Border, _selected && Enabled ? 2f : 1f)) g.DrawPath(pen, path);
        if (Focused && ShowFocusCues)
        {
            using var ring = new Pen(Color.FromArgb(150, Theme.Primary), 1f) { DashStyle = DashStyle.Dot };
            g.DrawPath(ring, Gfx.RoundedRect(RectangleF.Inflate(bounds, -4, -4), Gfx.S(this, 10)));
        }

        // Selection is shown by the border, tint and accent title, so the full width stays available for text.
        float pad = Gfx.S(this, 12f);
        var titleFont = TitleFont;
        using var titleBrush = new SolidBrush(!Enabled ? Theme.DisabledText : _selected ? Theme.Accent : Theme.TextPrimary);
        using var accentBrush = new SolidBrush(Enabled ? Theme.Accent : Theme.DisabledText);
        using var mutedBrush = new SolidBrush(Enabled ? Theme.TextSecondary : Theme.DisabledText);
        using var near = Gfx.Format(StringAlignment.Near, StringAlignment.Near);
        float lineH = Gfx.S(this, 24f);
        float y = bounds.Top + Gfx.S(this, 10f);
        float width = bounds.Width - 2 * pad;
        g.DrawString(Title, titleFont, titleBrush, new RectangleF(bounds.Left + pad, y, width, lineH), near);
        g.DrawString(Subtitle, Theme.Small, accentBrush, new RectangleF(bounds.Left + pad, y + lineH, width, lineH), near);
        g.DrawString(Detail, Theme.Small, mutedBrush, new RectangleF(bounds.Left + pad, y + lineH * 2, width, lineH), near);
    }
}
