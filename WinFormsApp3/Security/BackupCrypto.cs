using System.Security.Cryptography;
using System.Text;

namespace SkyWings;

/// <summary>
/// Password-based encryption for backup files (<c>.skybak</c>). AES-256-GCM, with the key derived from the password by
/// PBKDF2-SHA256 and a random salt per file. GCM also detects any change to the file, so a wrong password and a
/// tampered file both fail to open.
/// Layout: magic(8) | salt(16) | nonce(12) | tag(16) | ciphertext.
/// </summary>
public static class BackupCrypto
{
    public const string Extension = ".skybak";
    public const long MaxPlainBytes = 200L * 1024 * 1024;

    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("SKYBAK1\0");
    private const int SaltSize = 16, NonceSize = 12, TagSize = 16, KeySize = 32, Iterations = 210_000;
    private const int HeaderSize = 8 + SaltSize + NonceSize + TagSize;

    private static byte[] DeriveKey(string password, byte[] salt)
        => Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);

    public static void EncryptFile(string plainPath, string destination, string password)
    {
        if (string.IsNullOrEmpty(password)) throw new ArgumentException("A backup password is required.");
        var info = new FileInfo(plainPath);
        if (!info.Exists) throw new FileNotFoundException("The file to back up was not found.", plainPath);
        if (info.Length > MaxPlainBytes) throw new InvalidDataException("The database is too large to back up with this tool.");

        byte[] plain = File.ReadAllBytes(plainPath);
        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
        byte[] cipher = new byte[plain.Length];
        byte[] tag = new byte[TagSize];
        byte[] key = DeriveKey(password, salt);
        try
        {
            using var aes = new AesGcm(key, TagSize);
            // The header is authenticated too, so it cannot be swapped without detection.
            aes.Encrypt(nonce, plain, cipher, tag, AssociatedData(salt, nonce));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plain);
        }

        string temporary = destination + ".part";
        using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            output.Write(Magic);
            output.Write(salt);
            output.Write(nonce);
            output.Write(tag);
            output.Write(cipher);
        }
        File.Move(temporary, destination, overwrite: true);
    }

    /// <summary>Decrypts into <paramref name="plainPath"/>. Throws <see cref="CryptographicException"/> for a wrong password or damaged file.</summary>
    public static void DecryptFile(string source, string plainPath, string password)
    {
        var info = new FileInfo(source);
        if (!info.Exists) throw new FileNotFoundException("The backup file was not found.", source);
        if (info.Length < HeaderSize || info.Length > MaxPlainBytes + HeaderSize)
            throw new InvalidDataException("This is not a SkyWings backup file.");

        byte[] file = File.ReadAllBytes(source);
        if (!file.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            throw new InvalidDataException("This is not a SkyWings backup file.");

        byte[] salt = file[8..(8 + SaltSize)];
        byte[] nonce = file[(8 + SaltSize)..(8 + SaltSize + NonceSize)];
        byte[] tag = file[(8 + SaltSize + NonceSize)..HeaderSize];
        byte[] cipher = file[HeaderSize..];
        byte[] plain = new byte[cipher.Length];
        byte[] key = DeriveKey(password, salt);
        try
        {
            using var aes = new AesGcm(key, TagSize);
            try
            {
                aes.Decrypt(nonce, cipher, tag, plain, AssociatedData(salt, nonce));
            }
            catch (CryptographicException)
            {
                throw new CryptographicException("The backup password is wrong, or the file has been changed or damaged.");
            }
            File.WriteAllBytes(plainPath, plain);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
            CryptographicOperations.ZeroMemory(plain);
        }
    }

    private static byte[] AssociatedData(byte[] salt, byte[] nonce)
    {
        var data = new byte[Magic.Length + salt.Length + nonce.Length];
        Magic.CopyTo(data, 0);
        salt.CopyTo(data, Magic.Length);
        nonce.CopyTo(data, Magic.Length + salt.Length);
        return data;
    }
}
