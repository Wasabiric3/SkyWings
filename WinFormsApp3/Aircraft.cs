namespace SkyWings;

/// <summary>
/// One cabin of an aircraft: how many rows and how the seats in a row are grouped. <c>{3,4,3}</c> means three seats, an aisle,
/// four seats, an aisle, three seats (letters A to J). Generic, typical layouts: not any airline's own seat map.
/// </summary>
public sealed record CabinLayout(string ClassName, int Rows, IReadOnlyList<int> Groups)
{
    public int SeatsPerRow => Groups.Sum();
    public int Seats => Rows * SeatsPerRow;
    public char[] Letters => Enumerable.Range(0, SeatsPerRow).Select(i => (char)('A' + i)).ToArray();

    /// <summary>The seat columns (0-based) that have an aisle right after them.</summary>
    public int[] AislesAfter
    {
        get
        {
            var aisles = new List<int>();
            int column = -1;
            for (int g = 0; g < Groups.Count - 1; g++)
            {
                column += Groups[g];
                aisles.Add(column);
            }
            return aisles.ToArray();
        }
    }

    /// <summary>"3-4-3".</summary>
    public string LayoutText => string.Join("-", Groups);

    /// <summary>"Window", "Aisle" or "Middle" for a seat column.</summary>
    public string Position(int column)
    {
        if (column == 0 || column == SeatsPerRow - 1) return "Window";
        foreach (int after in AislesAfter)
            if (column == after || column == after + 1) return "Aisle";
        return "Middle";
    }

    /// <summary>Seat code as the wizard writes it, e.g. ECO3B.</summary>
    public string Code(int row, char letter) => SeatLayout.Code(ClassName, row, letter);
}

/// <summary>An aircraft type with its cabins. Seat counts and seat maps come from here, not from a single capacity number.</summary>
public sealed record Aircraft(string Id, string Model, IReadOnlyList<CabinLayout> Cabins)
{
    public int TotalSeats => Cabins.Sum(c => c.Seats);

    /// <summary>The cabin for a class, or null when this aircraft has none (for example First Class on a narrow-body).</summary>
    public CabinLayout? Cabin(string className) => Cabins.FirstOrDefault(c => c.ClassName == className);

    /// <summary>Id of the layout used by flights that have no aircraft set (the original ratio-based layout).</summary>
    public const string LegacyId = "LEGACY";

    public static readonly Aircraft A320 = new("A320", "Airbus A320", new[]
    {
        new CabinLayout("Business", 3, new[] { 2, 2 }),
        new CabinLayout("Economy", 28, new[] { 3, 3 })
    });

    public static readonly Aircraft A330 = new("A330", "Airbus A330-300", new[]
    {
        new CabinLayout("Business", 5, new[] { 1, 2, 1 }),
        new CabinLayout("Premium Economy", 4, new[] { 2, 3, 2 }),
        new CabinLayout("Economy", 22, new[] { 2, 4, 2 })
    });

    public static readonly Aircraft B777 = new("B777", "Boeing 777-300ER", new[]
    {
        new CabinLayout("First Class", 2, new[] { 1, 2, 1 }),
        new CabinLayout("Business", 6, new[] { 2, 2, 2 }),
        new CabinLayout("Premium Economy", 5, new[] { 2, 4, 2 }),
        new CabinLayout("Economy", 23, new[] { 3, 4, 3 })
    });

    public static readonly IReadOnlyList<Aircraft> All = new[] { A320, A330, B777 };

    public static Aircraft? Find(string? id)
        => string.IsNullOrWhiteSpace(id) ? null : All.FirstOrDefault(a => string.Equals(a.Id, id.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The aircraft a flight gets automatically, by route group: domestic (PR-1xx and its returns PR-5xx) A320, Southeast Asia
    /// (PR-2xx / PR-6xx) A330, Europe and North America (PR-3xx, 4xx / 7xx, 8xx) 777. Unknown numbers follow the domestic flag.
    /// </summary>
    public static Aircraft DefaultFor(string flightNo, bool domestic)
    {
        string digits = new(flightNo.SkipWhile(c => !char.IsDigit(c)).TakeWhile(char.IsDigit).ToArray());
        if (flightNo.StartsWith("PR-", StringComparison.OrdinalIgnoreCase) && int.TryParse(digits, out int number))
            switch (number / 100)
            {
                case 1: case 5: return A320;
                case 2: case 6: return A330;
                case 3: case 4: case 7: case 8: return B777;
            }
        return domestic ? A320 : A330;
    }

    /// <summary>
    /// The original layout for flights without an aircraft: every cabin's rows come from the flight's capacity and the cabin's
    /// share of seats, with the fixed seat grouping per class. Keeps existing databases looking exactly as before.
    /// </summary>
    public static Aircraft Legacy(int capacity)
    {
        int[][] groups = { new[] { 3, 3 }, new[] { 2, 2 }, new[] { 2, 1 }, new[] { 1, 1 } };   // Economy, Premium, Business, First
        var cabins = AppData.ClassNames
            .Select((name, i) => new CabinLayout(name, SeatLayout.RowCount(name, capacity), groups[i]))
            .ToArray();
        return new Aircraft(LegacyId, "Standard layout", cabins);
    }
}
