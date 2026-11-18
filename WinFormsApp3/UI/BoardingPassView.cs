using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace SkyWings.UI;

/// <summary>
/// Shows one boarding pass on screen exactly as it prints: the same ticket <see cref="BoardingPassPrinter.DrawTicket"/> draws on
/// paper (tinted backdrop, header band, big route, flight details, tear line and QR panel), scaled to the width of the control.
/// The height always follows the ticket's proportions. The picture is drawn once per size and reused while repainting, because the
/// QR code takes noticeably longer to draw than a repaint should.
/// </summary>
public sealed class BoardingPassView : Control, ISelfStyled
{
    private BoardingPassData? _data;
    private Bitmap? _cache;

    public BoardingPassView()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);
    }

    /// <summary>The pass to show.</summary>
    public void SetPass(BoardingPassData data)
    {
        _data = data;
        AccessibleName = $"Boarding pass for {data.PassengerName}, {data.OriginCode} to {data.DestinationCode}, seat {data.Seat}";
        DropCache();
        Invalidate();
    }

    /// <summary>The height a ticket needs at a given width, keeping the printed ticket's proportions.</summary>
    public static int HeightForWidth(int width)
        => (int)Math.Round(width * (double)BoardingPassPrinter.TicketHeight / BoardingPassPrinter.TicketWidth);

    // The height is not free: whatever the layout asks for, the ticket keeps its shape (so it is never squashed or cropped).
    protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
        => base.SetBoundsCore(x, y, width, HeightForWidth(width), specified);

    protected override void OnSizeChanged(EventArgs e)
    {
        DropCache();
        base.OnSizeChanged(e);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        g.Clear(Parent?.BackColor ?? Color.White);   // the ticket has rounded corners; what shows behind them is the card
        if (_data is null || Width < 40 || Height < 16) return;

        _cache ??= Render(_data);
        g.DrawImageUnscaled(_cache, 0, 0);
    }

    private Bitmap Render(BoardingPassData data)
    {
        var bitmap = new Bitmap(Width, Height);
        using Graphics g = Graphics.FromImage(bitmap);
        g.Clear(Parent?.BackColor ?? Color.White);
        float scale = Width / (float)BoardingPassPrinter.TicketWidth;
        g.ScaleTransform(scale, scale);
        BoardingPassPrinter.DrawTicket(g, data);
        return bitmap;
    }

    private void DropCache()
    {
        _cache?.Dispose();
        _cache = null;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) DropCache();
        base.Dispose(disposing);
    }
}
