namespace SkyWings;

/// <summary>Rules for staff passwords. Length matters more than symbols, so there is no "must contain" list.</summary>
public static class PasswordPolicy
{
    public const int MinLength = 12;
    public const int MaxLength = 128;

    // A short list of passwords that satisfy the length rule but are among the first ones guessed.
    private static readonly HashSet<string> Common = new(StringComparer.OrdinalIgnoreCase)
    {
        "password1234", "passw0rd1234", "123456789012", "1234567890123", "qwertyuiop12", "qwertyuiopas",
        "administrator", "adminadmin12", "welcome12345", "letmein12345", "iloveyou1234", "skywings1234",
        "skywingsadmin", "airlineadmin", "changeme1234", "000000000000", "111111111111", "abcdefghijkl"
    };

    /// <summary>Returns null when the password is acceptable, otherwise a message explaining what to fix.</summary>
    public static string? Check(string? password, string? username = null)
    {
        if (string.IsNullOrEmpty(password)) return "Enter a password.";
        if (password.Length < MinLength) return $"Use at least {MinLength} characters.";
        if (password.Length > MaxLength) return $"Use at most {MaxLength} characters.";
        if (password.Trim().Length == 0) return "The password cannot be only spaces.";
        if (password.Distinct().Count() < 5) return "Use a more varied password.";
        if (Common.Contains(password)) return "That password is too common. Choose something less guessable.";
        if (!string.IsNullOrWhiteSpace(username) && username.Trim().Length >= 3 &&
            password.Contains(username.Trim(), StringComparison.OrdinalIgnoreCase))
            return "The password cannot contain the username.";
        return null;
    }
}
