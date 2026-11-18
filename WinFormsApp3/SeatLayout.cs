namespace SkyWings;

/// <summary>Pure seat-layout rules shared by the seat map, the wizard and the store (no UI dependencies).</summary>
public static class SeatLayout
{
    public static int ClassIndex(string className)
    {
        int index = Array.IndexOf(AppData.ClassNames, className);
        return index < 0 ? 0 : index;
    }

    /// <summary>Number of rows a cabin needs, from the flight capacity and the cabin's share of seats.</summary>
    public static int RowCount(string className, int capacity)
    {
        int index = ClassIndex(className);
        int lettersPerRow = AppData.ClassSeatLetters[index].Length;
        double seats = Math.Max(0, capacity) * AppData.ClassSeatRatios[index];
        return Math.Max(1, (int)Math.Ceiling(seats / lettersPerRow));
    }

    /// <summary>Seat code as produced by the booking wizard, e.g. ECO3B.</summary>
    public static string Code(string className, int row, char letter)
        => $"{AppData.SeatPrefixForClass(className)}{row}{letter}";

    /// <summary>
    /// Brings seat codes to one canonical form so "ECO-01A" (seed data) and "ECO1A" (wizard) compare equal.
    /// </summary>
    public static string Normalize(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "";
        string s = code.Trim().ToUpperInvariant().Replace("-", "");
        int i = 0;
        while (i < s.Length && char.IsLetter(s[i])) i++;
        int j = i;
        while (j < s.Length && char.IsDigit(s[j])) j++;
        string digits = s[i..j].TrimStart('0');
        if (digits.Length == 0 && j > i) digits = "0";
        return s[..i] + digits + s[j..];
    }

    /// <summary>"Window", "Aisle" or "Middle" for a column in the given cabin.</summary>
    public static string Position(string className, int column)
    {
        int index = ClassIndex(className);
        int letters = AppData.ClassSeatLetters[index].Length;
        int aisleAfter = AppData.ClassAisleAfter[index];
        if (column == 0 || column == letters - 1) return "Window";
        if (column == aisleAfter || column == aisleAfter + 1) return "Aisle";
        return "Middle";
    }
}
