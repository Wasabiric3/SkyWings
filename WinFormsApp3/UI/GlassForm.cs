using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace SkyWings.UI;

/// <summary>Applies the Windows 11 Mica system backdrop to a window's title bar (no-op on older Windows).</summary>
internal static class WindowBackdrop
{
    private const int DwmUseImmersiveDarkMode = 20;
    private const int DwmSystemBackdropType = 38;
    private const int BackdropMica = 2;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    public static void Apply(Form form)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22621)) return;
        try
        {
            int light = 0;
            DwmSetWindowAttribute(form.Handle, DwmUseImmersiveDarkMode, ref light, sizeof(int));
            int mica = BackdropMica;
            DwmSetWindowAttribute(form.Handle, DwmSystemBackdropType, ref mica, sizeof(int));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Backdrop is cosmetic; ignore when the API is unavailable.
        }
    }
}

/// <summary>
/// Base window for the light "frosted glass" look: a pale gradient with soft glows (cached as a bitmap)
/// and a Mica title bar. Translucent child controls paint over this background.
/// </summary>
public class GlassForm : Form
{
    private Bitmap? _background;

    public GlassForm()
    {
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Theme.BgMain;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        WindowBackdrop.Apply(this);
    }

    /// <summary>
    /// Sizes the window from 96-dpi design values, scaled to the display and capped to the screen's working area.
    /// Layout in glass forms is scaled explicitly (<c>Gfx.S</c>), so automatic form scaling is switched off.
    /// </summary>
    protected void SizeForScreen(int width, int height, int minWidth, int minHeight)
    {
        AutoScaleMode = AutoScaleMode.None;
        Rectangle area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
        int maxWidth = area.Width - 16, maxHeight = area.Height - 16;
        MinimumSize = new Size(Math.Min(Gfx.S(this, minWidth), maxWidth), Math.Min(Gfx.S(this, minHeight), maxHeight));
        Size = new Size(Math.Min(Gfx.S(this, width), maxWidth), Math.Min(Gfx.S(this, height), maxHeight));
    }

    private System.Windows.Forms.Timer? _rebuildTimer;

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        // Rebuilding the gradient + glows on every size event makes dragging the edge janky. Keep drawing the
        // old bitmap stretched while resizing and rebuild it once the size has settled.
        if (_background is null) return;
        _rebuildTimer ??= CreateRebuildTimer();
        _rebuildTimer.Stop();
        _rebuildTimer.Start();
    }

    private System.Windows.Forms.Timer CreateRebuildTimer()
    {
        var timer = new System.Windows.Forms.Timer { Interval = 120 };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _background?.Dispose();
            _background = null;
            Invalidate();
        };
        return timer;
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
        _background ??= BuildBackground(ClientSize);
        if (_background.Size == ClientSize) e.Graphics.DrawImageUnscaled(_background, 0, 0);
        else e.Graphics.DrawImage(_background, 0, 0, ClientSize.Width, ClientSize.Height);
    }

    private static Bitmap BuildBackground(Size size)
    {
        var bitmap = new Bitmap(size.Width, size.Height);
        using var g = Graphics.FromImage(bitmap);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var gradient = new LinearGradientBrush(new Rectangle(0, 0, size.Width, size.Height),
                   Color.FromArgb(246, 250, 248), Color.FromArgb(229, 240, 243), 60f))
            g.FillRectangle(gradient, 0, 0, size.Width, size.Height);

        float reach = Math.Max(size.Width, size.Height);
        Gfx.Glow(g, size.Width * 0.88f, size.Height * -0.04f, reach * 0.55f, Color.FromArgb(78, 150, 208, 190));
        Gfx.Glow(g, size.Width * 0.04f, size.Height * 1.02f, reach * 0.50f, Color.FromArgb(64, 168, 200, 232));
        Gfx.Glow(g, size.Width * 0.55f, size.Height * 0.45f, reach * 0.35f, Color.FromArgb(34, 255, 255, 255));
        return bitmap;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { _background?.Dispose(); _rebuildTimer?.Dispose(); }
        base.Dispose(disposing);
    }
}
