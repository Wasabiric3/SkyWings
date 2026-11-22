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

    internal const TextFormatFlags TextFlags =
        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;

    private static readonly Font WordmarkFont = new("Segoe UI", 17f, FontStyle.Bold);
    private static readonly Font TaglineFont = new("Segoe UI", 6.75f, FontStyle.Bold);
    private const string Tagline = "AIRLINE  •  RESERVATIONS";

    private int WordmarkWidth()
        => Math.Max(TextRenderer.MeasureText("SkyWings", WordmarkFont, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width,
                    TextRenderer.MeasureText(Tagline, TaglineFont, Size.Empty, TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width);

    /// <summary>
    /// Arranges the pills left to right after the logo (and, when asked, the wordmark). When the window is narrow the padding inside
    /// each pill shrinks, down to <paramref name="minPad"/>; <c>Fits</c> says whether they all fit then.
    /// </summary>
    private (List<Rectangle> Pills, bool Fits, int MarkRight) Arrange(bool wordmark, int minPad)
    {
        int left = Gfx.S(this, 4);
        Size logo = LogoSize();
        int markRight = logo.IsEmpty ? left : left + logo.Width;
        int start = logo.IsEmpty ? left : markRight + Gfx.S(this, 40);
        if (wordmark) start = markRight + Gfx.S(this, 10) + WordmarkWidth() + Gfx.S(this, 36);
        int available = Math.Max(0, Width - start - Gfx.S(this, 4));

        int[] textWidths = _steps.Select((name, i) => TextRenderer.MeasureText(PillText(i, name), BoldFont, Size.Empty,
            TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width).ToArray();
        int pad = Gfx.S(this, 18);
        while (pad > minPad && textWidths.Sum() + _steps.Length * 2 * pad + (_steps.Length - 1) * PillGap > available) pad--;
        bool fits = textWidths.Sum() + _steps.Length * 2 * pad + (_steps.Length - 1) * PillGap <= available;

        int y = (Height - PillHeight) / 2 - Gfx.S(this, 1);
        var result = new List<Rectangle>(_steps.Length);
        int x = start;
        for (int i = 0; i < _steps.Length; i++)
        {
            int w = textWidths[i] + 2 * pad;
            result.Add(new Rectangle(x, y, w, PillHeight));
            x += w + PillGap;
        }
        return (result, fits, markRight);
    }

    /// <summary>The wordmark is shown only when the pills still fit comfortably beside it; otherwise the pills win and only the mark stays.</summary>
    private (List<Rectangle> Pills, bool Wordmark, int MarkRight) Compute()
    {
        if (_showLogo && !LogoSize().IsEmpty)
        {
            var withWordmark = Arrange(wordmark: true, minPad: Gfx.S(this, 12));
            if (withWordmark.Fits) return (withWordmark.Pills, true, withWordmark.MarkRight);
        }
        var plain = Arrange(wordmark: false, minPad: Gfx.S(this, 8));
        return (plain.Pills, false, plain.MarkRight);
    }

    private List<Rectangle> Layout() => Compute().Pills;

    internal bool WordmarkShown => Compute().Wordmark;

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

        var (pills, wordmark, markRight) = Compute();
        if (wordmark)
        {
            int x = markRight + Gfx.S(this, 10), mid = Height / 2 - Gfx.S(this, 1);
            TextRenderer.DrawText(g, "SkyWings", WordmarkFont, new Rectangle(x, mid - Gfx.S(this, 26), WordmarkWidth() + Gfx.S(this, 4), Gfx.S(this, 30)),
                Theme.Brand, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
            TextRenderer.DrawText(g, Tagline, TaglineFont, new Rectangle(x + Gfx.S(this, 2), mid + Gfx.S(this, 3), WordmarkWidth() + Gfx.S(this, 4), Gfx.S(this, 14)),
                Theme.TextSecondary, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
        }
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
            TextRenderer.DrawText(g, PillText(i, _steps[i]), current ? BoldFont : Font, pills[i], text, TextFlags);
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
