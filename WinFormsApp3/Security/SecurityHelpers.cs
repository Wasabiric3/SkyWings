using System.Security.Cryptography;
using System.Text;

namespace SkyWings;

/// <summary>Creates temporary passwords for new staff accounts and resets.</summary>
public static class PasswordGenerator
{
    // No look-alike characters (0/O, 1/l/I) so the password can be read out or typed without mistakes.
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";

    public static string Generate(string? username = null, int length = 16)
    {
        while (true)
        {
            var builder = new StringBuilder(length);
            for (int i = 0; i < length; i++) builder.Append(Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)]);
            string password = builder.ToString();
            if (PasswordPolicy.Check(password, username) is null) return password;
        }
    }
}

/// <summary>Makes text safe to put into a CSV cell that a spreadsheet will open.</summary>
public static class CsvSafe
{
    /// <summary>
    /// Quotes the value and neutralises spreadsheet formulas: a cell that starts with = + - @ (or a tab or carriage
    /// return) would be run as a formula when the file is opened, so it gets a leading apostrophe.
    /// </summary>
    public static string Cell(string? value)
    {
        string text = value ?? "";
        if (text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r') text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
}

/// <summary>
/// Signals when the user has not touched the keyboard or mouse for a while. Used to sign staff out of the admin window.
/// Set <c>SKYWINGS_IDLE_SECONDS</c> to shorten the limit for testing.
/// </summary>
public sealed class IdleMonitor : IMessageFilter, IDisposable
{
    public static readonly TimeSpan DefaultLimit = TimeSpan.FromMinutes(10);

    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 2000 };
    private DateTime _lastInputUtc = DateTime.UtcNow;
    private bool _fired;

    public IdleMonitor(TimeSpan? limit = null)
    {
        Limit = limit ?? LimitFromEnvironment();
        Application.AddMessageFilter(this);
        _timer.Tick += (_, _) =>
        {
            if (_fired || DateTime.UtcNow - _lastInputUtc < Limit) return;
            _fired = true;
            _timer.Stop();
            TimedOut?.Invoke(this, EventArgs.Empty);
        };
        _timer.Start();
    }

    public TimeSpan Limit { get; }

    public event EventHandler? TimedOut;

    private static TimeSpan LimitFromEnvironment()
    {
        string? text = Environment.GetEnvironmentVariable("SKYWINGS_IDLE_SECONDS");
        return int.TryParse(text, out int seconds) && seconds >= 5 ? TimeSpan.FromSeconds(seconds) : DefaultLimit;
    }

    // Key presses, mouse movement, clicks and wheel all count as activity.
    public bool PreFilterMessage(ref Message m)
    {
        if (m.Msg is 0x100 or 0x101 or 0x200 or 0x201 or 0x203 or 0x204 or 0x206 or 0x207 or 0x20A)
            _lastInputUtc = DateTime.UtcNow;
        return false;
    }

    public void Dispose()
    {
        Application.RemoveMessageFilter(this);
        _timer.Dispose();
    }
}
