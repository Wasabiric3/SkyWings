using System.Globalization;

namespace SkyWings;

/// <summary>Builds CSV text for the admin lists. Every cell goes through <see cref="CsvSafe.Cell"/>, so a name that starts
/// with = + - or @ cannot run as a spreadsheet formula.</summary>
public static class CsvExport
{
    public static List<string> Build(string[] header, IEnumerable<string?[]> rows)
    {
        var lines = new List<string> { string.Join(',', header) };
        lines.AddRange(rows.Select(r => string.Join(',', r.Select(CsvSafe.Cell))));
        return lines;
    }

    public static string Money(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);
}
