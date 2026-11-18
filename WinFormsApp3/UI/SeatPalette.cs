namespace SkyWings.UI;

/// <summary>Seat colours from the brand kit (Seat_*.svg). Labels use dark text on the light fills so they stay readable (&gt;= 3:1).</summary>
internal static class SeatPalette
{
    private static readonly Color Available = Color.FromArgb(0x9E, 0xC8, 0xAC);
    private static readonly Color Occupied = Color.FromArgb(0xA7, 0xAD, 0xA9);
    private static readonly Color OnHold = Color.FromArgb(0xCD, 0xA8, 0x75);

    public static (Color Fill, Color Border, Color Text) For(SeatState state, bool hover)
    {
        switch (state)
        {
            case SeatState.Selected:   // "my seat"
                return (Theme.Primary, Theme.Accent, Color.White);
            case SeatState.Taken:
                return (Occupied, Gfx.Mix(Occupied, Color.Black, 0.12f), Color.FromArgb(58, 66, 62));
            case SeatState.Held:
                return (OnHold, Gfx.Mix(OnHold, Color.Black, 0.12f), Color.FromArgb(84, 56, 18));
            default:
                Color fill = hover ? Gfx.Mix(Available, Theme.Primary, 0.25f) : Available;
                return (fill, hover ? Theme.Primary : Gfx.Mix(Available, Color.Black, 0.12f), Theme.AccentDark);
        }
    }
}
