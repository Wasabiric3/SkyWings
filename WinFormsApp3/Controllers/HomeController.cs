namespace SkyWings.Controllers;

/// <summary>A region of the network shown as a card on the home screen. Flights are the ones leaving Manila.</summary>
public sealed record RouteGroup(string Title, string Subtitle, string RouteType, IReadOnlyList<Flight> Flights);

/// <summary>One entry of the "To" list. An empty <see cref="Destination"/> means "show every flight".</summary>
public sealed record DestinationOption(string Destination, decimal From, int Flights)
{
    public override string ToString() => Destination.Length == 0
        ? $"Anywhere   ·   show all {Flights} flights"
        : $"{Destination}   ·   from PHP {From:N0}";
}

/// <summary>Everything the home screen shows about the flight network.</summary>
public sealed record Dashboard(
    IReadOnlyList<RouteGroup> Routes, IReadOnlyList<DestinationOption> Destinations, int DestinationCount, int FlightCount)
{
    public string Summary => $"{DestinationCount} destinations · {FlightCount} daily flights from Manila";

    /// <summary>The entry to preselect: the one the user had, else Cebu, else the first.</summary>
    public int DefaultDestinationIndex(string? previouslySelected)
    {
        int index = previouslySelected is null ? -1 : IndexOf(c => c.Destination == previouslySelected);
        if (index < 0) index = IndexOf(c => c.Destination.StartsWith("Cebu", StringComparison.Ordinal));
        return Math.Max(0, index);
    }

    private int IndexOf(Func<DestinationOption, bool> match)
    {
        for (int i = 0; i < Destinations.Count; i++)
            if (match(Destinations[i])) return i;
        return -1;
    }
}

/// <summary>
/// Controller for <c>HomeForm</c>: loads the route cards and destination list for the signed-in staff member and records
/// their sign-out. Has no WinForms dependency.
/// </summary>
public sealed class HomeController
{
    private readonly BookingStore _store;

    public HomeController(BookingStore store, StaffSession session)
    {
        _store = store;
        Session = session;
    }

    public StaffSession Session { get; }

    /// <summary>The first word of the staff member's display name, or their username.</summary>
    public string FirstName
        => Session.DisplayName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? Session.Username;

    /// <summary>Loads the flights leaving Manila, grouped by region and by destination.</summary>
    /// <exception cref="IOException">The database could not be read.</exception>
    public Dashboard LoadDashboard()
    {
        _store.SeedFlightsIfEmpty(AppData.DomesticFlights, AppData.InternationalFlights,
            AppData.DefaultDomesticCapacity, AppData.DefaultInternationalCapacity);
        var all = _store.LoadActiveFlights(true).Concat(_store.LoadActiveFlights(false))
            .Where(f => f.Origin.StartsWith("Manila", StringComparison.Ordinal)).ToList();

        IReadOnlyList<Flight> Region(string prefix) => all.Where(f => f.FlightNo.StartsWith(prefix, StringComparison.Ordinal)).ToList();
        var routes = new[]
        {
            new RouteGroup("Domestic", "Island time, closer to home.", "Domestic (Philippines)", Region("PR-1")),
            new RouteGroup("Southeast Asia", "New flavors. New perspectives.", "International (Southeast Asia)", Region("PR-2")),
            new RouteGroup("Europe", "A little art. A lot of discovery.", "International (Europe)", Region("PR-3")),
            new RouteGroup("North America", "Big cities. Bigger possibilities.", "International (North America)", Region("PR-4"))
        };

        var destinations = new List<DestinationOption>();
        if (all.Count > 0) destinations.Add(new DestinationOption("", all.Min(f => f.BasePrice), all.Count));
        foreach (var group in all.GroupBy(f => f.Destination).OrderBy(g => g.Key))
            destinations.Add(new DestinationOption(group.Key, group.Min(f => f.BasePrice), group.Count()));

        return new Dashboard(routes, destinations, all.Select(f => f.Destination).Distinct().Count(), all.Count);
    }

    /// <summary>The flights matching a destination (empty for all), date and cabin, for the live list.</summary>
    /// <exception cref="IOException">The database could not be read.</exception>
    public List<FlightRow> SearchFlights(string? destination, DateTime date, string className, DateTime? now = null)
        => FlightSearch.Search(_store, destination, date, className, now);

    /// <summary>Re-works the live status of already loaded rows for a new moment, without touching the database.</summary>
    public static List<FlightRow> Restamp(IEnumerable<FlightRow> rows, DateTime date, DateTime now)
        => rows.Select(r => r with { Status = FlightStatusEngine.Evaluate(r.FlightNo, date, now) })
            .OrderBy(r => r.Status.Estimated).ThenBy(r => r.FlightNo).ToList();

    /// <summary>Writes the sign-out to the audit log.</summary>
    public void RecordSignOut(string reason)
        => _store.WriteAudit(Session.Username, RolePermissions.ToDb(Session.Role), "SIGN_OUT", "", reason);
}
