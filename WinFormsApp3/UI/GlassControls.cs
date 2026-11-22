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

    private string _heading = "";

    /// <summary>Optional small title at the top of the card, drawn in capitals in the brand green; the content starts below it.</summary>
    [DefaultValue("")]
    public string Heading
    {
        get => _heading;
        set { _heading = value ?? ""; PerformLayout(); Invalidate(); }
    }

    private int HeadingHeight => _heading.Length == 0 ? 0 : Gfx.S(this, 30);

    public override Rectangle DisplayRectangle
    {
        get
        {
            int inset = Gfx.S(this, ShadowSize) + Gfx.S(this, _contentPadding);
            int head = HeadingHeight;
            return new Rectangle(inset, inset + head, Math.Max(0, Width - 2 * inset), Math.Max(0, Height - 2 * inset - head));
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

        if (_heading.Length > 0)
        {
            Rectangle area = DisplayRectangle;
            var line = new RectangleF(area.X, area.Y - HeadingHeight, area.Width, HeadingHeight);
            using var brush = new SolidBrush(Theme.Brand);
            using var left = Gfx.Format(StringAlignment.Near);
            g.DrawString(_heading.ToUpperInvariant(), HeadingFont, brush, line, left);
        }
    }

    private static readonly Font HeadingFont = new(Theme.Body, FontStyle.Bold);
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

    private bool _hover;
    private bool _selected;

    public ChoiceCard()
    {
        SetStyle(ControlStyles.UserPaint |
                 ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.StandardClick, true);
        TabStop = true;
        Cursor = Cursors.Hand;
        Height = Gfx.S(this, 118);
    }

    /// <summary>The circle that holds the tick, at the top right of the card.</summary>
    internal Rectangle TickBounds
    {
        get
        {
            int d = Gfx.S(this, 22);
            return new Rectangle(Width - 3 - Gfx.S(this, 16) - d, 2 + Gfx.S(this, 14), d, d);
        }
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
        Color fill = !Enabled ? Theme.DisabledBack : _selected ? Theme.BrandSoft : _hover ? Theme.BgMuted : Theme.BgCard;
        using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);
        using (var pen = new Pen(_selected && Enabled ? Theme.Brand : Theme.Border, _selected && Enabled ? 2f : 1f)) g.DrawPath(pen, path);
        if (Focused && ShowFocusCues)
        {
            using var ring = new Pen(Color.FromArgb(150, Theme.Brand), 1f) { DashStyle = DashStyle.Dot };
            g.DrawPath(ring, Gfx.RoundedRect(RectangleF.Inflate(bounds, -4, -4), Gfx.S(this, 10)));
        }

        // The class name in capitals, the price large, the detail small; a tick circle marks the chosen card.
        const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
        int pad = Gfx.S(this, 16);
        int x = (int)bounds.Left + pad;
        int width = (int)bounds.Width - 2 * pad;
        int y = (int)bounds.Top + Gfx.S(this, 14);
        Color strong = Enabled ? Theme.Brand : Theme.DisabledText;
        Color muted = Enabled ? Theme.TextSecondary : Theme.DisabledText;
        TextRenderer.DrawText(g, Title.ToUpperInvariant(), CapsFont, new Rectangle(x, y, width - Gfx.S(this, 30), Gfx.S(this, 22)), strong, flags);
        // A cabin that is not offered says so in small muted text instead of a price.
        TextRenderer.DrawText(g, Subtitle, Enabled ? PriceFont : Theme.Small, new Rectangle(x, y + Gfx.S(this, 28), width, Gfx.S(this, 32)),
            Enabled ? strong : muted, flags);
        TextRenderer.DrawText(g, Detail, Theme.Small, new Rectangle(x, y + Gfx.S(this, 64), width, Gfx.S(this, 22)), muted, flags);

        if (_selected && Enabled)
        {
            Rectangle tick = TickBounds;
            using (var circle = new SolidBrush(Theme.Brand)) g.FillEllipse(circle, tick);
            using var mark = new Pen(Color.White, Gfx.S(this, 2f)) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            float s = tick.Width / 22f;
            g.DrawLines(mark, new[] { new PointF(tick.X + 6 * s, tick.Y + 11.5f * s), new PointF(tick.X + 9.5f * s, tick.Y + 15 * s), new PointF(tick.X + 16 * s, tick.Y + 7.5f * s) });
        }
    }

    private static readonly Font CapsFont = new(Theme.Small, FontStyle.Bold);
    private static readonly Font PriceFont = new(Theme.Subtitle, FontStyle.Bold);
}
