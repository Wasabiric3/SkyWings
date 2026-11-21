using System.Security.Cryptography;
using System.Text;

namespace SkyWings;

/// <summary>
/// Protects a setting (the SMTP password) with Windows DPAPI for the current Windows user, so db.properties no longer
/// holds it in plain text. A protected value looks like <c>dpapi:BASE64</c>; anything else is treated as plain text.
/// Only the same Windows user on the same machine can read it back.
/// </summary>
public static class SecretProtector
{
    public const string Prefix = "dpapi:";
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SkyWings.db.properties.v1");

    public static bool IsProtected(string value) => value.StartsWith(Prefix, StringComparison.Ordinal);

    public static string Protect(string plain)
    {
        if (IsProtected(plain)) return plain;
        byte[] data = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser);
        return Prefix + Convert.ToBase64String(data);
    }

    /// <summary>Returns the plain value. Plain input is returned unchanged. Throws <see cref="CryptographicException"/> or
    /// <see cref="FormatException"/> when a protected value cannot be read (another user, another machine, damaged).</summary>
    public static string Unprotect(string value)
    {
        if (!IsProtected(value)) return value;
        byte[] data = ProtectedData.Unprotect(Convert.FromBase64String(value[Prefix.Length..]), Entropy, DataProtectionScope.CurrentUser);
        return Encoding.UTF8.GetString(data);
    }

    /// <summary>Rewrites <paramref name="lines"/> so that a plain <c>smtp.password</c> line becomes protected.
    /// Returns the new lines and whether anything changed. Other lines are kept as they are.</summary>
    public static (string[] Lines, bool Changed) ProtectSmtpPassword(string[] lines)
    {
        bool changed = false;
        var result = new string[lines.Length];
        for (int i = 0; i < lines.Length; i++)
        {
            result[i] = lines[i];
            string line = lines[i].Trim();
            if (line.StartsWith('#') || line.StartsWith('!')) continue;
            int eq = line.IndexOf('=');
            if (eq <= 0 || line[..eq].Trim() != "smtp.password") continue;
            string value = line[(eq + 1)..].Trim();
            if (value.Length == 0 || IsProtected(value)) continue;
            result[i] = "smtp.password=" + Protect(value);
            changed = true;
        }
        return (result, changed);
    }
}
