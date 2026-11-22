using System.Drawing.Drawing2D;

namespace SkyWings.UI;

/// <summary>The Figma look's check box: a rounded square that fills with the brand green and shows a white tick when checked.</summary>
public sealed class GlassCheckBox : CheckBox, ISelfStyled
{
    public GlassCheckBox()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);
        Font = Theme.Body;
        Cursor = Cursors.Hand;
        AutoSize = true;
    }

    private int BoxSize => Gfx.S(this, 18);
    private int BoxLeft => Gfx.S(this, 2);

    public override Size GetPreferredSize(Size proposedSize)
    {
        Size text = TextRenderer.MeasureText(Text, Font);
        return new Size(BoxLeft + BoxSize + Gfx.S(this, 8) + text.Width + Gfx.S(this, 2), Math.Max(Gfx.S(this, 24), text.Height + 4));
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        using (var surface = new SolidBrush(Gfx.Surface(this))) g.FillRectangle(surface, ClientRectangle);
        Gfx.HighQuality(g);

        var box = new RectangleF(BoxLeft, (Height - BoxSize) / 2f, BoxSize, BoxSize);
        using (var path = Gfx.RoundedRect(box, Gfx.S(this, 5)))
        {
            Color fill = !Enabled ? Theme.DisabledBack : Checked ? Theme.Brand : Color.White;
            using (var brush = new SolidBrush(fill)) g.FillPath(brush, path);
            using var pen = new Pen(Checked && Enabled ? Theme.Brand : Theme.Border, 1.5f);
            g.DrawPath(pen, path);
            if (Focused && ShowFocusCues)
            {
                using var ring = new Pen(Color.FromArgb(190, Theme.Brand), 2f);
                using var outer = Gfx.RoundedRect(RectangleF.Inflate(box, 2.5f, 2.5f), Gfx.S(this, 7));
                g.DrawPath(ring, outer);
            }
        }

        if (Checked)
        {
            using var tick = new Pen(Enabled ? Color.White : Theme.DisabledText, Gfx.S(this, 2f))
            { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            float s = BoxSize / 18f;
            g.DrawLines(tick, new[]
            {
                new PointF(box.X + 5 * s, box.Y + 9.5f * s), new PointF(box.X + 8 * s, box.Y + 12.5f * s), new PointF(box.X + 13.5f * s, box.Y + 5.5f * s)
            });
        }

        var textBounds = new Rectangle(BoxLeft + BoxSize + Gfx.S(this, 8), 0, Math.Max(0, Width - BoxLeft - BoxSize - Gfx.S(this, 8)), Height);
        TextRenderer.DrawText(g, Text, Font, textBounds, Enabled ? Theme.TextPrimary : Theme.DisabledText,
            TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
    }
}
