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

    /// <summary>
    /// A Philippine phone number, typed the way people write it: <c>+63 917 123 4567</c>, <c>0917 123 4567</c> or <c>63917…</c>
    /// (spaces, dashes and brackets are ignored). A mobile number has 10 digits after the country code and starts with 9;
    /// a landline has an area code starting 2-8 and 8 or 9 digits in all. Returns the number as <c>+63 …</c>.
    /// </summary>
    public static bool TryNormalizePhContact(string? text, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(text)) return false;
        string value = new(text.Where(c => c is not ' ' and not '-' and not '(' and not ')').ToArray());
        bool plus = value.StartsWith('+');
        string digits = plus ? value[1..] : value;
        if (digits.Length == 0 || !digits.All(char.IsAsciiDigit)) return false;

        string national;
        if (digits.StartsWith("63")) national = digits[2..];
        else if (!plus && digits.StartsWith('0')) national = digits[1..];
        else return false;

        bool mobile = national.Length == 10 && national[0] == '9';
        bool landline = national.Length is >= 8 and <= 9 && national[0] is >= '2' and <= '8';
        if (!mobile && !landline) return false;

        normalized = mobile
            ? $"+63 {national[..3]} {national[3..6]} {national[6..]}"
            : $"+63 {national}";
        return true;
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
