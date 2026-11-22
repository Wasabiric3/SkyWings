using System.ComponentModel;

namespace SkyWings.UI;

/// <summary>
/// The wizard's header band: the logo and one rounded pill per step ("1. Flight" ...). The current pill is filled with the brand
/// green. Pills of steps already visited can be clicked to go back; later ones cannot.
/// </summary>
public sealed class StepBar : Control, ISelfStyled
{
    private readonly string[] _steps;
    private readonly BackdropCache _logoCache = new();
    private int _current;
    private int _furthest;
    private bool _showLogo;

    public StepBar(string[] steps)
    {
        _steps = steps;
        SetStyle(ControlStyles.UserPaint |
                 ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);
        Height = Gfx.S(this, 76);
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

    /// <summary>Shows the SkyWings logo at the left of the pills.</summary>
    [DefaultValue(false)]
    public bool ShowLogo
    {
        get => _showLogo;
        set { _showLogo = value; Invalidate(); }
    }

    private int PillHeight => Gfx.S(this, 36);
    private int PillGap => Gfx.S(this, 10);
    private int LogoHeight => Gfx.S(this, 40);

    private Size LogoSize()
    {
        Bitmap? logo = _showLogo ? Assets.Logo(LogoVariant.Green, DeviceDpi) : null;
        if (logo is null) return Size.Empty;
        return new Size((int)Math.Round(LogoHeight * logo.Width / (double)logo.Height), LogoHeight);
    }

    private static string PillText(int index, string name) => $"{index + 1}. {name}";

    /// <summary>The pill rectangles, left to right. When the window is narrow the padding inside each pill shrinks so they all fit.</summary>
    private List<Rectangle> Layout()
    {
        int left = Gfx.S(this, 4);
        Size logo = LogoSize();
        int start = logo.IsEmpty ? left : left + logo.Width + Gfx.S(this, 40);
        int available = Math.Max(0, Width - start - Gfx.S(this, 4));

        int[] textWidths = _steps.Select((name, i) => TextRenderer.MeasureText(PillText(i, name), BoldFont, Size.Empty,
            TextFormatFlags.NoPadding).Width).ToArray();
        int pad = Gfx.S(this, 18);
        int minPad = Gfx.S(this, 8);
        while (pad > minPad && textWidths.Sum() + _steps.Length * 2 * pad + (_steps.Length - 1) * PillGap > available) pad--;

        int y = (Height - PillHeight) / 2 - Gfx.S(this, 1);
        var result = new List<Rectangle>(_steps.Length);
        int x = start;
        for (int i = 0; i < _steps.Length; i++)
        {
            int w = textWidths[i] + 2 * pad;
            result.Add(new Rectangle(x, y, w, PillHeight));
            x += w + PillGap;
        }
        return result;
    }

    internal Rectangle PillBounds(int index) => Layout()[index];

    /// <summary>The index of the pill under <paramref name="p"/>, or -1 (gaps, the logo, empty space).</summary>
    internal int HitStep(Point p)
    {
        List<Rectangle> pills = Layout();
        for (int i = 0; i < pills.Count; i++)
            if (pills[i].Contains(p)) return i;
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

        using (var divider = new Pen(Theme.Border, 1f)) g.DrawLine(divider, 0, Height - 1, Width, Height - 1);

        Size logoSize = LogoSize();
        if (!logoSize.IsEmpty && Assets.Logo(LogoVariant.Green, DeviceDpi) is { } logo)
        {
            // Scaled once and cached; rescaling the logo on every repaint is wasted work.
            Bitmap scaled = _logoCache.Get(logoSize, DeviceDpi, lg => BrandArt.DrawScaled(lg, logo, new Rectangle(0, 0, logoSize.Width, logoSize.Height)));
            g.DrawImageUnscaled(scaled, Gfx.S(this, 4), (Height - logoSize.Height) / 2 - Gfx.S(this, 1));
        }

        List<Rectangle> pills = Layout();
        for (int i = 0; i < pills.Count; i++)
        {
            bool current = i == _current, done = i < _current;
            using var path = Gfx.RoundedRect(new RectangleF(pills[i].X + 0.5f, pills[i].Y + 0.5f, pills[i].Width - 1, pills[i].Height - 1), pills[i].Height / 2f);
            using (var fill = new SolidBrush(current ? Theme.Brand : Color.White)) g.FillPath(fill, path);
            if (!current)
            {
                using var outline = new Pen(Theme.Border, 1f);
                g.DrawPath(outline, path);
            }
            Color text = current ? Color.White : done ? Theme.Brand : Theme.TextPrimary;
            TextRenderer.DrawText(g, PillText(i, _steps[i]), current ? BoldFont : Font, pills[i], text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    // One shared bold font for measuring and for the current pill (creating fonts per paint leaks GDI handles).
    private static readonly Font BoldFont = new(Theme.Small, FontStyle.Bold);

    protected override void Dispose(bool disposing)
    {
        if (disposing) _logoCache.Dispose();
        base.Dispose(disposing);
    }
}
