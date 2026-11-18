using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace SkyWings.UI;

/// <summary>
/// Boarding-pass style confirmation: details on the left, reference stub on the right. The reference is always shown in
/// full (the text shrinks to fit) and has a Copy button; double-clicking the reference copies it too.
/// </summary>
public sealed class ReceiptPanel : Control, ISelfStyled
{
    private const string CopyIdle = "Copy", CopyDone = "Copied ✓", CopyFailed = "Try again";

    // Shared fonts: creating them on every paint (one was never disposed) allocated GDI handles on each repaint.
    private static readonly Font BandFont = new(Theme.Body, FontStyle.Bold);
    private static readonly Font PlaneFont = new(Theme.Header, FontStyle.Regular);
    private static readonly Font ValueFont = new(Theme.Body, FontStyle.Bold);
    private static readonly Font PillFont = new(Theme.Small, FontStyle.Bold);

    private string _reference = "";
    private List<(string Label, string Value)> _rows = new();
    private RectangleF _copyRect, _referenceRect;
    private readonly BackdropCache _logoCache = new();
    private bool _copyHover;
    private string _copyLabel = CopyIdle;
    private readonly System.Windows.Forms.Timer _copyTimer = new() { Interval = 2000 };

    public ReceiptPanel()
    {
        SetStyle(ControlStyles.UserPaint |
                 ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);
        AccessibleName = _title;
        _copyTimer.Tick += (_, _) => { _copyTimer.Stop(); _copyLabel = CopyIdle; Invalidate(); };
    }

    private string _title = "Booking receipt";

    /// <summary>The heading in the green band. "Booking receipt" for receipts; the boarding pass screen sets "Boarding pass".</summary>
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string Title
    {
        get => _title;
        set { _title = value; AccessibleName = value; Invalidate(); }
    }

    public void SetReceipt(string reference, IEnumerable<(string Label, string Value)> rows)
    {
        _reference = reference;
        _rows = rows.ToList();
        _copyLabel = CopyIdle;
        AccessibleDescription = $"Booking {reference}. " + string.Join(". ", _rows.Select(r => $"{r.Label}: {r.Value}"));
        Invalidate();
    }

    /// <summary>Puts the booking reference on the clipboard and shows "Copied" for a moment.</summary>
    public void CopyReference()
    {
        if (_reference.Length == 0) return;
        try
        {
            Clipboard.SetDataObject(_reference, true, 3, 100);   // retries when another program holds the clipboard
            _copyLabel = CopyDone;
        }
        catch (ExternalException)
        {
            _copyLabel = CopyFailed;
        }
        _copyTimer.Stop();
        _copyTimer.Start();
        Invalidate();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        bool overCopy = _copyRect.Contains(e.Location);
        if (overCopy != _copyHover) { _copyHover = overCopy; Invalidate(); }
        Cursor = overCopy || _referenceRect.Contains(e.Location) ? Cursors.Hand : Cursors.Default;
        base.OnMouseMove(e);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        if (_copyHover) { _copyHover = false; Invalidate(); }
        base.OnMouseLeave(e);
    }

    protected override void OnMouseClick(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && _copyRect.Contains(e.Location)) CopyReference();
        base.OnMouseClick(e);
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        if (e.Button == MouseButtons.Left && _referenceRect.Contains(e.Location)) CopyReference();
        base.OnMouseDoubleClick(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _copyTimer.Dispose(); _logoCache.Dispose(); }
        base.Dispose(disposing);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        using (var surface = new SolidBrush(Gfx.Surface(this))) g.FillRectangle(surface, ClientRectangle);
        Gfx.HighQuality(g);
        var bounds = new RectangleF(2, 2, Width - 5, Height - 5);
        if (bounds.Width < 200 || bounds.Height < 120) return;
        float radius = Gfx.S(this, 18f);

        using var path = Gfx.RoundedRect(bounds, radius);
        using (var fill = new SolidBrush(Color.White)) g.FillPath(fill, path);
        using (var border = new Pen(Theme.Border, 1f)) g.DrawPath(border, path);

        // Header band: flat brand green (no gradient).
        float bandH = Gfx.S(this, 50f);
        var oldClip = g.Clip;
        g.SetClip(path, CombineMode.Intersect);
        using (var band = new SolidBrush(Theme.Primary))
            g.FillRectangle(band, bounds.X, bounds.Y, bounds.Width, bandH);
        g.Clip = oldClip;

        using var white = new SolidBrush(Color.White);
        var bandFont = BandFont;
        using var left = Gfx.Format(StringAlignment.Near);
        using var right = Gfx.Format(StringAlignment.Far);
        float pad = Gfx.S(this, 20f);
        g.DrawString($"SkyWings  ·  {Title}", bandFont, white,
            new RectangleF(bounds.X + pad, bounds.Y, bounds.Width * 0.6f, bandH), left);
        Bitmap? logo = Assets.Logo(LogoVariant.White, DeviceDpi);
        if (logo is not null)
        {
            int mark = (int)(bandH * 0.72f);
            // Scaled once and cached (rescaling a 160px logo on every repaint, e.g. each Copy-button hover, is wasted work).
            Bitmap scaled = _logoCache.Get(new Size(mark, mark), DeviceDpi, lg => BrandArt.DrawScaled(lg, logo, new Rectangle(0, 0, mark, mark)));
            g.DrawImageUnscaled(scaled, (int)(bounds.Right - pad - mark), (int)(bounds.Y + (bandH - mark) / 2));
        }
        else
            g.DrawString("✈", PlaneFont, white, new RectangleF(bounds.Right - pad - 80, bounds.Y, 80, bandH), right);

        // Stub (right) and details (left) are split by a dashed line.
        float stubW = Math.Max(Gfx.S(this, 250f), bounds.Width * 0.30f);
        float splitX = bounds.Right - stubW;
        using (var dash = new Pen(Theme.Border, 1.5f) { DashStyle = DashStyle.Dash })
            g.DrawLine(dash, splitX, bounds.Y + bandH + Gfx.S(this, 10f), splitX, bounds.Bottom - Gfx.S(this, 10f));

        using var mutedBrush = new SolidBrush(Theme.TextSecondary);
        using var textBrush = new SolidBrush(Theme.TextPrimary);
        var valueFont = ValueFont;
        using var topLeft = Gfx.Format(StringAlignment.Near, StringAlignment.Near);

        float colW = (splitX - bounds.X - pad * 2) / 2;
        float rowH = Gfx.S(this, 58f);
        float startY = bounds.Y + bandH + Gfx.S(this, 18f);
        for (int i = 0; i < _rows.Count; i++)
        {
            int col = i % 2, row = i / 2;
            float x = bounds.X + pad + col * colW;
            float y = startY + row * rowH;
            if (y + rowH > bounds.Bottom) break;
            g.DrawString(_rows[i].Label.ToUpperInvariant(), Theme.Small, mutedBrush,
                new RectangleF(x, y, colW - pad / 2, Gfx.S(this, 20f)), topLeft);
            g.DrawString(_rows[i].Value, valueFont, textBrush,
                new RectangleF(x, y + Gfx.S(this, 20f), colW - pad / 2, Gfx.S(this, 30f)), topLeft);
        }

        // Stub: reference + decorative bars.
        float sx = splitX + pad;
        float sw = stubW - pad * 2;
        g.DrawString("REFERENCE", Theme.Small, mutedBrush, new RectangleF(sx, startY, sw, Gfx.S(this, 20f)), topLeft);
        using Font refFont = FitReferenceFont(g, topLeft, sw);
        _referenceRect = new RectangleF(sx, startY + Gfx.S(this, 22f), sw, Gfx.S(this, 34f));
        g.DrawString(_reference, refFont, textBrush, _referenceRect, topLeft);

        // Copy button under the reference.
        _copyRect = new RectangleF(sx, startY + Gfx.S(this, 62f), Gfx.S(this, 104f), Gfx.S(this, 32f));
        if (_reference.Length > 0)
        {
            bool done = _copyLabel == CopyDone;
            using var pill = Gfx.RoundedRect(_copyRect, Gfx.S(this, 9f));
            using (var pillFill = new SolidBrush(done ? Theme.SoftFill : _copyHover ? Theme.BgMuted : Color.White))
                g.FillPath(pillFill, pill);
            using (var pillBorder = new Pen(_copyHover || done ? Theme.Primary : Theme.Border, 1f))
                g.DrawPath(pillBorder, pill);
            using var pillText = new SolidBrush(Theme.Accent);
            using var centered = Gfx.Format(StringAlignment.Center);
            g.DrawString(_copyLabel, PillFont, pillText, _copyRect, centered);
        }

        float barsY = startY + Gfx.S(this, 112f);
        DrawBars(g, new RectangleF(sx, barsY, sw, Math.Min(Gfx.S(this, 70f), bounds.Bottom - barsY - Gfx.S(this, 16f))));
    }

    /// <summary>The largest bold font (13.5pt down to 9pt) in which the whole reference fits on one line.</summary>
    private Font FitReferenceFont(Graphics g, StringFormat format, float maxWidth)
    {
        for (float size = Theme.Subtitle.Size; size > 9f; size -= 0.5f)
        {
            var font = new Font(Theme.Subtitle.FontFamily, size, FontStyle.Bold);
            if (g.MeasureString(_reference, font, new SizeF(10000f, 100f), format).Width <= maxWidth - 2f) return font;
            font.Dispose();
        }
        return new Font(Theme.Subtitle.FontFamily, 9f, FontStyle.Bold);
    }

    /// <summary>Decorative bar pattern derived from the reference (not a scannable barcode).</summary>
    private void DrawBars(Graphics g, RectangleF area)
    {
        if (area.Height < 10 || _reference.Length == 0) return;
        using var brush = new SolidBrush(Theme.TextPrimary);
        float x = area.X;
        int seed = 17;
        foreach (char ch in _reference + _reference)
        {
            seed = seed * 31 + ch;
            float barW = 1 + Math.Abs(seed % 3) * Gfx.S(this, 1f);
            if (x + barW > area.Right) break;
            if (Math.Abs(seed % 2) == 0) g.FillRectangle(brush, x, area.Y, barW, area.Height);
            x += barW + Gfx.S(this, 2f);
        }
    }
}
