
namespace SkyWings;

/// <summary>
/// Slows down guessing on the customer lookup screens (Manage booking and Check-in share one counter, so switching
/// screens does not reset it). After 5 wrong tries the screens lock; each further lock lasts twice as long, up to
/// 5 minutes. Counters live for the running session; every failure is also written to the audit log (without any
/// personal data) so repeated guessing can be noticed.
/// </summary>
internal static class LookupGuard
{
    private const int MaxFailures = 5;
    private static readonly TimeSpan BaseLockout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaxLockout = TimeSpan.FromMinutes(5);

    /// <summary>Replaceable clock so tests can move time forward.</summary>
    internal static Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

    private static int s_failures;
    private static int s_lockouts;
    private static DateTime s_lockedUntilUtc = DateTime.MinValue;

    /// <summary>Returns a message while lookups are locked, otherwise null.</summary>
    public static string? LockedMessage()
    {
        DateTime now = Clock();
        if (now >= s_lockedUntilUtc) return null;
        int seconds = (int)Math.Ceiling((s_lockedUntilUtc - now).TotalSeconds);
        return $"Too many attempts. Try again in {Format(seconds)}.";
    }

    /// <summary>Records a failed lookup and returns the message to show.</summary>
    public static string RecordFailure(BookingStore? store = null, string screen = "")
    {
        if (++s_failures >= MaxFailures)
        {
            TimeSpan length = TimeSpan.FromSeconds(Math.Min(MaxLockout.TotalSeconds, BaseLockout.TotalSeconds * Math.Pow(2, s_lockouts)));
            s_lockouts++;
            s_failures = 0;
            s_lockedUntilUtc = Clock() + length;
            store?.WriteAudit("customer", "", "LOOKUP_LOCKED", screen, $"Locked for {(int)length.TotalSeconds}s after repeated wrong details");
            return $"Too many attempts. Locked for {Format((int)length.TotalSeconds)}.";
        }
        store?.WriteAudit("customer", "", "LOOKUP_FAILED", screen);
        return "We couldn't find a booking with that reference.";
    }

    public static void RecordSuccess()
    {
        s_failures = 0;
        s_lockouts = 0;
    }

    internal static void Reset()
    {
        s_failures = 0;
        s_lockouts = 0;
        s_lockedUntilUtc = DateTime.MinValue;
        Clock = () => DateTime.UtcNow;
    }

    private static string Format(int seconds)
        => seconds >= 90 ? $"{(int)Math.Ceiling(seconds / 60.0)} minutes" : $"{seconds}s";
}
