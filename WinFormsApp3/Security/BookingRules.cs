namespace SkyWings;

/// <summary>
/// Limits that protect the system from oversized or abusive input. They are checked in the booking form (so people get a
/// friendly message) and again in the store (so no other code path can bypass them).
/// </summary>
public static class BookingRules
{
    public const int MaxPassengersPerBooking = 9;
    /// <summary>A booking of 9 travellers on a round trip holds 18 seats; more than that at once is abuse.</summary>
    public const int MaxActiveSeatHoldsPerOwner = MaxPassengersPerBooking * 2;
    public const int MaxNameLength = 120;
    public const int MaxAddressLength = 200;
    public const int MaxContactLength = 30;
    public const int MaxIdNumberLength = 40;
    public const int MaxEmailLength = 254;

    public static bool HasControlCharacters(string? text) => text is not null && text.Any(char.IsControl);

    /// <summary>
    /// A plain address such as <c>name@example.com</c>. Display names ("Name &lt;a@b.c&gt;"), spaces, control characters
    /// and addresses without a dotted domain are rejected, so the value is safe to put in a mail header.
    /// </summary>
    public static bool IsValidEmail(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        string value = text.Trim();
        if (value.Length > MaxEmailLength || HasControlCharacters(value) || value.Any(char.IsWhiteSpace)) return false;
        if (!System.Net.Mail.MailAddress.TryCreate(value, out var address)) return false;
        return string.Equals(address.Address, value, StringComparison.Ordinal) && address.Host.Contains('.');
    }

    /// <summary>Throws <see cref="ArgumentException"/> with a readable message when a booking breaks a limit.</summary>
    public static void Validate(BookingRecord booking)
    {
        if (booking.Passengers.Count == 0) throw new ArgumentException("A booking needs at least one traveller.");
        if (booking.Passengers.Count > MaxPassengersPerBooking)
            throw new ArgumentException($"A booking can have at most {MaxPassengersPerBooking} travellers.");

        foreach (PassengerRecord p in booking.Passengers)
        {
            CheckText(p.Name, MaxNameLength, "name");
            CheckText(p.Address, MaxAddressLength, "address");
            CheckText(p.Contact, MaxContactLength, "contact number");
            CheckText(p.IdNumber, MaxIdNumberLength, "ID number");
        }
    }

    private static void CheckText(string value, int max, string field)
    {
        if (value.Length > max) throw new ArgumentException($"The {field} is too long (at most {max} characters).");
        if (HasControlCharacters(value)) throw new ArgumentException($"The {field} contains characters that aren't allowed.");
    }
}
