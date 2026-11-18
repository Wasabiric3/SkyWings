namespace SkyWings.Controllers;

/// <summary>Outcome of looking a booking up. Exactly one of <see cref="Booking"/> and <see cref="Error"/> is set.</summary>
/// <param name="NormalizedReference">The reference after cleaning (e.g. taken out of a scanned QR code), to show back in the box.</param>
public sealed record LookupResult(BookingRecord? Booking, string? Error, string? NormalizedReference = null)
{
    public bool Found => Booking is not null;
}

/// <summary>
/// The customer lookup shared by Manage booking and Check-in: honours the brute-force lock, accepts a scanned QR code,
/// and counts wrong details towards the lock (see <see cref="LookupGuard"/>).
/// </summary>
internal static class CustomerLookup
{
    public static LookupResult Find(BookingStore store, string screen, string logSource, string reference, string lastName)
    {
        if (LookupGuard.LockedMessage() is { } locked) return new LookupResult(null, locked, reference);

        BookingRecord? booking;
        string normalized = reference;
        try
        {
            normalized = SkyWings.Email.QrPayload.Normalize(reference);   // accepts a scanned QR code too
            booking = store.FindForCustomer(normalized, lastName);
        }
        catch (Exception ex)
        {
            AppLogger.Error(logSource, ex);
            return new LookupResult(null, "Something went wrong. Please try again.", normalized);
        }

        if (booking is null)
            return new LookupResult(null, LookupGuard.RecordFailure(store, screen), normalized);

        LookupGuard.RecordSuccess();
        return new LookupResult(booking, null, normalized);
    }
}
