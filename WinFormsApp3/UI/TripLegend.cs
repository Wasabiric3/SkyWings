using System.Drawing.Drawing2D;
using SkyWings.Controllers;

namespace SkyWings.UI;

/// <summary>
/// "What the statuses mean": each trip status drawn as the same pill the Bookings grid uses (<see cref="GridStyle.StatusStyle"/>),
/// with a short meaning beside it. Four columns on a wide window, two on a narrow one; the height follows.
/// </summary>
internal sealed class TripLegend : Control
{
    private static readonly Font PillFont = new(Theme.Small, FontStyle.Bold);
    private static readonly Font TitleFont = new(Theme.Small, FontStyle.Bold);

    public TripLegend()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        BackColor = Color.White;
        Font = Theme.Small;
        AutoSize = true;   // docked: the layout asks GetPreferredSize for the height at the width it gives
    }

    private int S(int px) => Gfx.S(this, px);
    private int ColumnsFor(int width) => width >= S(980) ? 4 : 2;
    private int RowHeight => S(40);
    private int TitleHeight => S(30);

    /// <summary>The height the legend needs at a given width.</summary>
    internal int NeededHeight(int width)
    {
        int columns = ColumnsFor(width);
        return TitleHeight + (TripStatus.Legend.Count + columns - 1) / columns * RowHeight + S(22);
    }

    public override Size GetPreferredSize(Size proposedSize)
    {
        int width = proposedSize.Width is > 0 and < int.MaxValue ? proposedSize.Width : Width;
        return new Size(width, NeededHeight(width));
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        Parent?.PerformLayout();   // a new width can change the number of columns, and so the height
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        Graphics g = e.Graphics;
        using (var line = new Pen(Theme.BorderLight))
            g.DrawLine(line, 0, S(6), Width, S(6));
        TextRenderer.DrawText(g, "What the statuses mean", TitleFont, new Rectangle(S(10), S(10), Width, S(20)), Theme.TextSecondary,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        Gfx.HighQuality(g);
        int columns = ColumnsFor(Width), pillWidth = S(116), gap = S(8);
        int columnWidth = (Width - S(10)) / columns;
        for (int i = 0; i < TripStatus.Legend.Count; i++)
        {
            var (label, meaning) = TripStatus.Legend[i];
            int x = S(10) + i % columns * columnWidth, y = TitleHeight + i / columns * RowHeight;
            var pill = new Rectangle(x, y + (RowHeight - S(24)) / 2, pillWidth, S(24));
            if (GridStyle.StatusStyle(label) is { } style)
            {
                using GraphicsPath path = Gfx.RoundedRect(pill, pill.Height / 2f);
                using var back = new SolidBrush(style.Back);
                g.FillPath(back, path);
                TextRenderer.DrawText(g, label, PillFont, pill, style.Fore,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
            var text = new Rectangle(pill.Right + gap, y, columnWidth - pillWidth - gap - S(12), RowHeight);
            TextRenderer.DrawText(g, meaning, Font, text, Theme.TextPrimary,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.EndEllipsis);
        }
        int footY = TitleHeight + (TripStatus.Legend.Count + columns - 1) / columns * RowHeight;
        TextRenderer.DrawText(g, "\"Return · …\" means the outbound flight has landed and the status is now for the return flight.",
            Font, new Rectangle(S(10), footY, Width - S(20), S(20)), Theme.TextSecondary,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}
