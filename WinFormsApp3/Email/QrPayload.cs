using System.Text.RegularExpressions;

namespace SkyWings.Email;

/// <summary>
/// What a booking's QR code holds: just the booking reference (for example <c>SKY-1A2B3C4D5E6F</c>). A phone shows it as
/// text; a USB scanner types it into the reference box. Parsing also accepts extra text around the reference.
/// </summary>
public static class QrPayload
{
    private static readonly Regex ReferencePattern = new(@"SKY-[0-9A-Z]{6,16}", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string Build(string reference) => reference.Trim().ToUpperInvariant();

    public static bool TryParse(string? scanned, out string reference)
    {
        reference = "";
        if (string.IsNullOrWhiteSpace(scanned)) return false;
        Match match = ReferencePattern.Match(scanned.ToUpperInvariant());
        if (!match.Success) return false;
        reference = match.Value;
        return true;
    }

    /// <summary>The reference found in scanned text, or the trimmed original when there is none.</summary>
    public static string Normalize(string? text)
        => TryParse(text, out string reference) ? reference : (text ?? "").Trim();
}
