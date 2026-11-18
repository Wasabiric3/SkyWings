namespace SkyWings;

/// <summary>
/// Plain-text application log in the protected data folder. It rotates at 5 MB (keeping 5 older files) and removes
/// files older than 30 days. Callers must not pass passwords, ID numbers or card numbers.
/// </summary>
public static class AppLogger
{
    private const long MaxBytes = 5 * 1024 * 1024;
    private const int KeepFiles = 5;
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(30);
    private static readonly object Sync = new();
    private static bool _pruned;

    public static void Error(string area, Exception exception)
        => Write($"[{DateTime.Now:O}] [{area}] {exception}\r\n");

    public static void Info(string area, string message)
        => Write($"[{DateTime.Now:O}] [{area}] {message}\r\n");

    private static void Write(string entry)
    {
        try
        {
            lock (Sync)
            {
                string directory = AppPaths.LogDirectory;
                Directory.CreateDirectory(directory);
                string path = Path.Combine(directory, "skywings.log");
                Rotate(path);
                File.AppendAllText(path, entry);
                if (!_pruned) { _pruned = true; Prune(directory); }
            }
        }
        catch
        {
            // Logging must never break the app.
        }
    }

    private static void Rotate(string path)
    {
        if (!File.Exists(path) || new FileInfo(path).Length < MaxBytes) return;
        string Numbered(int n) => Path.Combine(Path.GetDirectoryName(path)!, $"skywings.{n}.log");
        if (File.Exists(Numbered(KeepFiles))) File.Delete(Numbered(KeepFiles));
        for (int i = KeepFiles - 1; i >= 1; i--)
            if (File.Exists(Numbered(i))) File.Move(Numbered(i), Numbered(i + 1));
        File.Move(path, Numbered(1));
    }

    private static void Prune(string directory)
    {
        foreach (string file in Directory.EnumerateFiles(directory, "*.log"))
            if (DateTime.UtcNow - File.GetLastWriteTimeUtc(file) > MaxAge) File.Delete(file);
    }
}
