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
/// Base window for the flat brand look: a solid page colour (<see cref="Theme.BgMain"/>) and a Mica title bar.
/// Cards and controls paint themselves opaquely over it, so nothing needs to be composed through transparency.
/// </summary>
public class GlassForm : Form
{
    public GlassForm()
    {
        DoubleBuffered = true;
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
}
