namespace SkyWings.UI;

/// <summary>
/// The Figma look's input field: a white rounded box around a native <see cref="TextBox"/>, <see cref="ComboBox"/> or
/// <see cref="DateTimePicker"/>. Native controls cannot have rounded corners, so a text box is made borderless and sits inside the
/// box, while a combo box or date picker sits inside a square clipping panel (inset from the corners) that hides the native
/// border. The box draws the outline: grey normally, green while the input has focus, red while <see cref="HasError"/>.
/// </summary>
internal sealed class RoundedInput : Control, ISelfStyled
{
    private readonly Panel? _clip;
    private bool _hasError;

    public RoundedInput(Control input)
    {
        Input = input;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);
        TabStop = false;

        input.Font = Theme.Body;
        input.BackColor = Color.White;
        input.Dock = DockStyle.None;
        input.Anchor = AnchorStyles.None;
        if (input is TextBox box) box.BorderStyle = BorderStyle.None;
        if (input is ComboBox combo && combo.DrawMode == DrawMode.Normal)
        {
            combo.DrawMode = DrawMode.OwnerDrawFixed;
            combo.ItemHeight = Gfx.S(this, 38);
            combo.DrawItem += DrawComboItem;
        }

        if (input is ComboBox)
        {
            _clip = new Panel { BackColor = Color.White, TabStop = false };
            _clip.Controls.Add(input);
            Controls.Add(_clip);
        }
        else
        {
            Controls.Add(input);   // a text box or a date field sits straight in the box
        }

        input.Enter += (_, _) => Invalidate();
        input.Leave += (_, _) => Invalidate();
        input.EnabledChanged += (_, _) => ApplyEnabled();
        input.SizeChanged += (_, _) => UpdateHeight();
        ApplyEnabled();   // the input may already be disabled (the return date before "Add a return flight" is ticked)
        Height = HostHeight();
    }

    private void ApplyEnabled()
    {
        Input.BackColor = Input.Enabled ? Color.White : Theme.DisabledBack;
        if (_clip is not null) _clip.BackColor = Input.BackColor;
        Invalidate();
    }

    public Control Input { get; }

    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool HasError
    {
        get => _hasError;
        set { if (_hasError == value) return; _hasError = value; Invalidate(); }
    }

    public bool FocusVisible => Input.ContainsFocus;

    /// <summary>Combo boxes and date fields show a chevron, painted by the box, in a zone at the right.</summary>
    private bool Chevron => Input is ComboBox or DateField;

    private int RightPad => Gfx.S(this, Chevron ? 40 : 14);

    private int HostHeight() => _clip is null ? Math.Max(Gfx.S(this, 44), Input.Height + 2 * Gfx.S(this, 8)) : Input.Height;

    private void UpdateHeight()
    {
        int wanted = HostHeight();
        if (Height != wanted) Height = wanted;
        else PositionInput();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UpdateHeight();
    }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        PositionInput();
    }

    private void PositionInput()
    {
        if (_clip is null)
        {
            int pad = Gfx.S(this, 14);
            Input.SetBounds(pad, (Height - Input.Height) / 2, Math.Max(0, Width - pad - RightPad), Input.Height);
            return;
        }
        int side = Gfx.S(this, 12), edge = Gfx.S(this, 2);
        _clip.SetBounds(side, edge, Math.Max(0, Width - side - RightPad), Math.Max(0, Height - 2 * edge));
        // The native control is a little larger than the panel: the panel clips its border, and its arrow button (it sticks out
        // to the right of the panel by just its own width, so an owner-drawn item still ends at the visible edge) is cut away,
        // because the box paints its own chevron.
        int button = SystemInformation.GetVerticalScrollBarWidthForDpi(DeviceDpi);
        Input.SetBounds(-edge, -edge, _clip.Width + edge + button + 3, _clip.Height + 2 * edge);
    }

    protected override void OnClick(EventArgs e)
    {
        if (Input is ComboBox { Enabled: true } combo) { combo.Focus(); combo.DroppedDown = true; }
        else if (Input is DateField field) { field.Focus(); field.OpenDropDown(); }
        else Input.Focus();
        base.OnClick(e);
    }

    private void DrawComboItem(object? sender, DrawItemEventArgs e)
    {
        if (e.Index < 0 || sender is not ComboBox combo) return;
        bool edit = (e.State & DrawItemState.ComboBoxEdit) != 0;
        bool highlighted = !edit && (e.State & DrawItemState.Selected) != 0;
        Color back = !combo.Enabled ? Theme.DisabledBack : highlighted ? Theme.BrandSoft : Color.White;
        using (var brush = new SolidBrush(back)) e.Graphics.FillRectangle(brush, e.Bounds);
        var bounds = new Rectangle(e.Bounds.X + Gfx.S(combo, 2), e.Bounds.Y, e.Bounds.Width - Gfx.S(combo, 2), e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, combo.GetItemText(combo.Items[e.Index]), edit ? BoldFont : Theme.Body, bounds,
            combo.Enabled ? Theme.TextPrimary : Theme.DisabledText,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
    }

    private static readonly Font BoldFont = new(Theme.Body, FontStyle.Bold);

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        using (var surface = new SolidBrush(Gfx.Surface(this))) g.FillRectangle(surface, ClientRectangle);
        Gfx.HighQuality(g);
        var bounds = new RectangleF(1, 1, Width - 3, Height - 3);
        using var path = Gfx.RoundedRect(bounds, Gfx.S(this, 12));
        using (var fill = new SolidBrush(Input.Enabled ? Color.White : Theme.DisabledBack)) g.FillPath(fill, path);
        Color line = _hasError ? Theme.DangerRed : FocusVisible ? Theme.Brand : Theme.Border;
        using var pen = new Pen(line, _hasError || FocusVisible ? 2f : 1f);
        g.DrawPath(pen, path);

        if (Chevron)
        {
            float cx = Width - Gfx.S(this, 22f), cy = Height / 2f, d = Gfx.S(this, 4f);
            using var chevron = new Pen(Input.Enabled ? Theme.TextSecondary : Theme.DisabledTextSoft, Gfx.S(this, 1.75f))
            { StartCap = System.Drawing.Drawing2D.LineCap.Round, EndCap = System.Drawing.Drawing2D.LineCap.Round, LineJoin = System.Drawing.Drawing2D.LineJoin.Round };
            g.DrawLines(chevron, new[] { new PointF(cx - d, cy - d / 2), new PointF(cx, cy + d / 2), new PointF(cx + d, cy - d / 2) });
        }
    }
}
