namespace SkyWings.Controllers;

/// <summary>A group of rule and value lines under a heading.</summary>
public sealed record PolicySection(string Heading, IReadOnlyList<(string Rule, string Value)> Rows);

/// <summary>One page of the customer information window: a title, a short intro and its sections.</summary>
public sealed record InfoPage(string Title, string Intro, IReadOnlyList<PolicySection> Sections);

/// <summary>
/// The customer-facing text behind the footer links on the home screen. Every number is read from <see cref="AppData"/>
/// and <see cref="BookingRules"/>, so what is shown always matches what the app charges, refunds and allows.
/// </summary>
public static class PolicyInfo
{
    public static IReadOnlyList<InfoPage> Pages() => new[] { FlightPolicies(), BaggageGuide(), FareRules() };

    public static InfoPage FlightPolicies() => new("Flight policies",
        "How cancellations, check-in and changes work.",
        new[]
        {
            new PolicySection("Cancellations and refunds", new[]
            {
                ("7 or more days before the flight", Percent(AppData.RefundRate(DateTime.Today.AddDays(7))) + " of the total"),
                ("1 to 6 days before the flight", Percent(AppData.RefundRate(DateTime.Today.AddDays(1))) + " of the total"),
                ("On the day of the flight", Percent(AppData.RefundRate(DateTime.Today)) + " of the total"),
                ("After departure", "No changes or cancellations"),
            }),
            new PolicySection("Check-in", new[]
            {
                ("Online check-in opens", $"{AppData.CheckInWindowHours} hours before departure"),
                ("Online check-in closes", "At departure"),
                ("Boarding pass", "Print or save it after you check in"),
            }),
            new PolicySection("Booking", new[]
            {
                ("Seats are held while you book", $"{AppData.SeatHoldMinutes} minutes"),
                ("Travellers per booking", BookingRules.MaxPassengersPerBooking.ToString()),
                ("Change seats or cancel", "Manage booking, with your booking reference"),
            }),
        });

    public static InfoPage BaggageGuide()
    {
        var sections = new List<PolicySection>();
        foreach (string cabin in AppData.ClassNames)
        {
            var rows = new List<(string, string)>();
            string[] bags = AppData.BaggageOptionsForClass(cabin);
            rows.Add(("Checked baggage", Clean(bags[0])));
            for (int i = 1; i < bags.Length; i++) rows.Add(("Extra baggage", Clean(bags[i])));
            string[] meals = AppData.MealOptionsForClass(cabin);
            rows.Add(("Meals", string.Join(", ", meals.Select(Clean))));
            sections.Add(new PolicySection(cabin, rows));
        }
        sections.Add(new PolicySection("Extras you can add", AppData.OptionalAddonNames
            .Select(name => (name, $"PHP {AppData.AddonPrices[AppData.GetAddonIndex(name)]:N0}")).ToList()));
        return new InfoPage("Baggage guide", "What each cabin includes, and what extras cost.", sections);
    }

    public static InfoPage FareRules()
    {
        var cabins = AppData.ClassNames
            .Select((name, i) => (name, $"x{AppData.ClassMultipliers[i]:0.0} of the base fare")).ToList();
        return new InfoPage("Fare rules", "How the price of a booking is worked out.", new[]
        {
            new PolicySection("Cabin fares", cabins),
            new PolicySection("Taxes and surcharges", new[]
            {
                ("VAT", $"{Percent(AppData.TaxRate)} (not applied to Senior and PWD travellers)"),
                ("Peak season surcharge", Percent(AppData.PeakSeasonRate)),
            }),
            new PolicySection("Discounts", new[]
            {
                ("Senior citizen (age " + AppData.SeniorMinimumAge + "+) and PWD", Percent(AppData.DiscountRate) + " off"),
                ("Advance purchase", $"{Percent(AppData.AdvancePurchaseRate)} off when booked {AppData.AdvancePurchaseDays}+ days ahead"),
                ("Promo code SKY10", "10% off. Not for First Class, and not combined with the advance-purchase discount"),
                ("Promo code EARLY500", "PHP 500 off a fare of PHP 5,000 or more, booked 30+ days ahead"),
            }),
            new PolicySection("Good to know", new[]
            {
                ("Fares shown on the home screen", "One-way, per traveller, before taxes and extras"),
                ("Payment", "This is a demo. Never enter real card details"),
            }),
        });
    }

    private static string Percent(decimal rate) => $"{rate:P0}";

    /// <summary>Swaps the middle dot used in the booking options for a comma, which reads better in a sentence.</summary>
    private static string Clean(string option) => option.Replace(" · ", ", ");
}
