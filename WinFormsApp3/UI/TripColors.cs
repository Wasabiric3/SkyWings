using SkyWings.Controllers;

namespace SkyWings.UI;

/// <summary>One colour per trip phase, shared by the staff Bookings tab and the booking detail.</summary>
internal static class TripColors
{
    internal static Color For(TripPhase phase) => phase switch
    {
        TripPhase.Completed => Theme.Accent,
        TripPhase.Boarding or TripPhase.InFlight => Theme.Info,
        TripPhase.CheckInOpen or TripPhase.CheckedIn => Theme.Primary,
        TripPhase.NoShow => Theme.Warning,
        TripPhase.Canceled => Theme.DangerRed,
        _ => Theme.TextSecondary
    };
}
