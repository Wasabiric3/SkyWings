namespace SkyWings.UI;

/// <summary>
/// Seat colours from the Figma: light green free seats, deep green for the traveller's own seat, amber held, grey taken.
/// Labels use dark text on the light fills so they stay readable (&gt;= 3:1).
/// </summary>
internal static class SeatPalette
{
    public static (Color Fill, Color Border, Color Text) For(SeatState state, bool hover)
    {
        switch (state)
        {
            case SeatState.Selected:   // "my seat"
                return (Theme.Brand, Theme.Brand, Color.White);
            case SeatState.Taken:
                return (Theme.SeatTaken, Gfx.Mix(Theme.SeatTaken, Color.Black, 0.12f), Color.FromArgb(58, 66, 62));
            case SeatState.Held:
                return (Theme.SeatHeld, Gfx.Mix(Theme.SeatHeld, Color.Black, 0.12f), Color.FromArgb(84, 56, 18));
            default:
                Color fill = hover ? Gfx.Mix(Theme.BrandSoft, Theme.Primary, 0.25f) : Theme.BrandSoft;
                return (fill, hover ? Theme.Brand : Gfx.Mix(Theme.BrandSoft, Color.Black, 0.12f), Theme.Brand);
        }
    }
}
