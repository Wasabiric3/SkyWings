using System.Security.Cryptography;

namespace SkyWings;

/// <summary>
/// PBKDF2-SHA256 password hashing for the staff login.
/// Stored format: PBKDF2-SHA256$&lt;iterations&gt;$&lt;salt base64&gt;$&lt;hash base64&gt;
/// </summary>
public static class PasswordHasher
{
    private const string Prefix = "PBKDF2-SHA256";
    private const int Iterations = 210_000;
    private const int SaltSize = 16;
    private const int KeySize = 32;

    public static string Hash(string password)
    {
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] key = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);
        return $"{Prefix}${Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(key)}";
    }

    /// <summary>True when a stored hash uses fewer iterations than today's setting and should be recomputed at next sign-in.</summary>
    public static bool NeedsRehash(string stored)
    {
        string[] parts = stored.Split('$');
        return parts.Length != 4 || parts[0] != Prefix || !int.TryParse(parts[1], out int iterations) || iterations < Iterations;
    }

    public static bool Verify(string password, string stored)
    {
        string[] parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != Prefix || !int.TryParse(parts[1], out int iterations) || iterations < 1)
            return false;

        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        byte[] actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
