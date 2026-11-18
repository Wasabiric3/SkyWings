using System.Globalization;

namespace SkyWings;

/// <summary>
/// Static flight/pricing/seat data and rules ported from the top of AirplaneTicketingSystem.java.
/// All money values use <see cref="decimal"/> to avoid floating-point rounding errors.
/// </summary>
public static class AppData
{
    // ========================= FLIGHT DATA =========================
    public static readonly string[] FlightColumns = { "Flight No.", "Origin", "Destination", "Base Price (PHP)" };
    public static readonly string[] FlightColumnsWithEst =
        { "Flight No.", "Departure", "Arrival", "Realtime EST", "Base Fare (PHP)", "Status" };

    public static readonly Flight[] DomesticFlights =
    {
        new("PR-101", "Manila (MNL)", "Cebu (CEB)", 3500.00m),
        new("PR-102", "Manila (MNL)", "Davao (DVO)", 4200.00m),
        new("PR-103", "Manila (MNL)", "Boracay (MPH)", 3800.00m),
        new("PR-104", "Manila (MNL)", "Palawan (PPS)", 4500.00m),
        new("PR-105", "Manila (MNL)", "Bohol (TAG)", 3200.00m),
    };

    public static readonly Flight[] InternationalFlights =
    {
        new("PR-201", "Manila (MNL)", "Singapore (SIN)", 8500.00m),
        new("PR-202", "Manila (MNL)", "Bangkok (BKK)", 7800.00m),
        new("PR-203", "Manila (MNL)", "Kuala Lumpur (KUL)", 7200.00m),
        new("PR-204", "Manila (MNL)", "Ho Chi Minh (SGN)", 6800.00m),
        new("PR-205", "Manila (MNL)", "Jakarta (CGK)", 9200.00m),
    };

    public static readonly Flight[] EuropeFlights =
    {
        new("PR-301", "Manila (MNL)", "London (LHR)", 45000.00m),
        new("PR-302", "Manila (MNL)", "Paris (CDG)", 43000.00m),
        new("PR-303", "Manila (MNL)", "Rome (FCO)", 41000.00m),
        new("PR-304", "Manila (MNL)", "Amsterdam (AMS)", 42000.00m),
        new("PR-305", "Manila (MNL)", "Frankfurt (FRA)", 44000.00m),
    };

    public static readonly Flight[] NorthAmericaFlights =
    {
        new("PR-401", "Manila (MNL)", "Los Angeles (LAX)", 52000.00m),
        new("PR-402", "Manila (MNL)", "San Francisco (SFO)", 54000.00m),
        new("PR-403", "Manila (MNL)", "New York (JFK)", 58000.00m),
        new("PR-404", "Manila (MNL)", "Vancouver (YVR)", 49000.00m),
        new("PR-405", "Manila (MNL)", "Toronto (YYZ)", 51000.00m),
    };

    // ========================= CLASS & PRICING =========================
    public static readonly string[] ClassNames = { "Economy", "Premium Economy", "Business", "First Class" };
    public static readonly decimal[] ClassMultipliers = { 1.0m, 1.5m, 2.5m, 4.0m };

    // ========================= ADD-ONS =========================
    public static readonly string[] AddonNames = { "Extra Baggage", "Pet", "Meal", "Insurance", "Tax" };
    public static readonly decimal[] AddonPrices = { 1500.00m, 3000.00m, 500.00m, 800.00m, 600.00m };
    public static readonly string[] OptionalAddonNames = { "Pet", "Insurance" };
    public const string AddonTax = "Tax";

    // ========================= RATES & LIMITS =========================
    public const decimal DiscountRate = 0.20m;            // 20% for Senior/PWD
    public const decimal TaxRate = 0.12m;                 // 12% VAT (exempt for Senior/PWD)
    public const decimal OverbookingBufferRate = 0.10m;   // 10% allowable overbooking
    public const decimal PeakSeasonRate = 0.15m;
    public const decimal AdvancePurchaseRate = 0.10m;
    public const int AdvancePurchaseDays = 60;
    public const int DefaultDomesticCapacity = 180;
    public const int DefaultInternationalCapacity = 220;
    public const int DefaultEuropeCapacity = 300;
    public const int DefaultNorthAmericaCapacity = 320;
    public const string PolicyOverbooking = "Overbooking limits";
    public const string PolicyCheckin = "Check-in window (e.g., 24hrs before departure)";
    public const string PolicyRefund = "Refund/cancellation policies";
    public const int BookingCutoffHours = 2;
    /// <summary>Online check-in opens this many hours before departure and closes at departure.</summary>
    public const int CheckInWindowHours = 24;
    public const int SeatHoldMinutes = 10;

    /// <summary>How long a waitlisted traveller has to book once seats are offered to them.</summary>
    public const int WaitlistOfferHours = 24;

    /// <summary>
    /// When a waitlist offer made at <paramref name="now"/> for a flight leaving at <paramref name="departure"/> lapses: after
    /// <see cref="WaitlistOfferHours"/>, but never later than the booking cutoff (nobody can book after it, so holding the
    /// seats longer would only waste them). Null when the cutoff has already passed: no offer can be made.
    /// </summary>
    public static DateTime? WaitlistOfferDeadline(DateTime now, DateTime departure)
    {
        DateTime cutoff = departure.AddHours(-BookingCutoffHours);
        if (now >= cutoff) return null;
        DateTime full = now.AddHours(WaitlistOfferHours);
        return full < cutoff ? full : cutoff;
    }

    // ========================= PASSENGER RULES =========================
    /// <summary>Minimum age for the senior citizen discount (Philippine law: 60 and above).</summary>
    public const int SeniorMinimumAge = 60;
    public const int MaxPassengerAge = 120;

    // ========================= STATUS =========================
    public const string StatusConfirmed = "CONFIRMED";
    public const string StatusCanceled = "CANCELED";
    public const string PaymentPaid = "PAID";
    public const string PaymentUnpaid = "UNPAID";
    public const string PaymentRefunded = "REFUNDED";
    public const string DateInputFormat = "MMMM d, yyyy";

    // ========================= FORMATS =========================
    // Java: "h:mma" / "MMM dd, yyyy hh:mm a" / "MMM dd, yyyy" / strict "uuuu-MM-dd"
    public const string EstTimeFormat = "h:mmtt";
    public const string EstDateTimeFormat = "MMM dd, yyyy hh:mm tt";
    public const string FlightDateDisplayFormat = "MMM dd, yyyy";
    public const string BirthdayFormat = "yyyy-MM-dd";

    public static bool TryParseBirthday(string text, out DateTime date)
        => DateTime.TryParseExact(text, BirthdayFormat, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out date);

    /// <summary>Whole years between the birthday and <paramref name="today"/>.</summary>
    public static int AgeOn(DateTime birthday, DateTime today)
    {
        int age = today.Year - birthday.Year;
        if (birthday.Date > today.AddYears(-age)) age--;
        return age;
    }

    // ========================= SEAT LAYOUT =========================
    public static readonly char[][] ClassSeatLetters =
    {
        new[] { 'A', 'B', 'C', 'D', 'E', 'F' }, // Economy        3+3
        new[] { 'A', 'B', 'C', 'D' },           // Premium Eco    2+2
        new[] { 'A', 'B', 'C' },                // Business       2+1
        new[] { 'A', 'B' },                     // First Class    1+1
    };
    public static readonly int[] ClassAisleAfter = { 2, 1, 1, 0 };
    public static readonly double[] ClassSeatRatios = { 0.60, 0.25, 0.10, 0.05 };
    public static readonly int[] ClassBtnWidth = { 44, 52, 64, 80 };
    public static readonly int[] ClassBtnHeight = { 40, 44, 52, 60 };

    /// <summary>Seat-code prefix per class index (Java: ECO / PRE / BIZ / FST).</summary>
    public static string SeatPrefixForClass(string className) => className switch
    {
        "Economy" => "ECO",
        "Premium Economy" => "PRE",
        "Business" => "BIZ",
        _ => "FST"
    };

    // ========================= WIZARD STEPS =========================
    public static readonly string[] StepIds =
        { "HOME", "FLIGHTS", "PASSENGERS", "CLASS", "ADDONS", "BILLING", "SEATS", "PAYMENT", "RECEIPT" };
    public static readonly string[] StepTitles =
        { "Home", "Flights", "Passengers", "Class", "Add-ons", "Seats", "Billing", "Payment", "Receipt" };
    public const int StepHome = 0, StepFlights = 1, StepPassengers = 2, StepClass = 3, StepAddons = 4,
        StepSeats = 5, StepBilling = 6, StepPayment = 7, StepReceipt = 8;

    // ========================= PER-FLIGHT TABLES =========================
    public static readonly Dictionary<string, int> FlightEstDepartureOffsetMin = new()
    {
        ["PR-101"] = 50, ["PR-102"] = 150, ["PR-103"] = 240, ["PR-104"] = 360, ["PR-105"] = 480,
        ["PR-201"] = 75, ["PR-202"] = 180, ["PR-203"] = 300, ["PR-204"] = 420, ["PR-205"] = 540,
        ["PR-301"] = 180, ["PR-302"] = 210, ["PR-303"] = 240, ["PR-304"] = 270, ["PR-305"] = 300,
        ["PR-401"] = 360, ["PR-402"] = 420, ["PR-403"] = 480, ["PR-404"] = 540, ["PR-405"] = 600,
    };

    public static readonly Dictionary<string, int> FlightEstDurationMin = new()
    {
        ["PR-101"] = 90, ["PR-102"] = 115, ["PR-103"] = 75, ["PR-104"] = 95, ["PR-105"] = 80,
        ["PR-201"] = 220, ["PR-202"] = 205, ["PR-203"] = 235, ["PR-204"] = 165, ["PR-205"] = 250,
        ["PR-301"] = 840, ["PR-302"] = 855, ["PR-303"] = 870, ["PR-304"] = 830, ["PR-305"] = 845,
        ["PR-401"] = 980, ["PR-402"] = 990, ["PR-403"] = 1020, ["PR-404"] = 960, ["PR-405"] = 970,
        ["PR-701"] = 840, ["PR-702"] = 855, ["PR-703"] = 870, ["PR-704"] = 830, ["PR-705"] = 845,
        ["PR-801"] = 980, ["PR-802"] = 990, ["PR-803"] = 1020, ["PR-804"] = 960, ["PR-805"] = 970,
    };

    public static readonly Dictionary<string, TimeOnly> FlightScheduledTimes = new()
    {
        ["PR-101"] = new(6, 0),  ["PR-102"] = new(10, 0), ["PR-103"] = new(13, 0),
        ["PR-104"] = new(15, 0), ["PR-105"] = new(18, 0),
        ["PR-201"] = new(7, 0),  ["PR-202"] = new(10, 30), ["PR-203"] = new(13, 30),
        ["PR-204"] = new(16, 0), ["PR-205"] = new(20, 0),
        ["PR-301"] = new(18, 0), ["PR-302"] = new(19, 0), ["PR-303"] = new(20, 0),
        ["PR-304"] = new(21, 0), ["PR-305"] = new(22, 0),
        ["PR-401"] = new(23, 0), ["PR-402"] = new(0, 30), ["PR-403"] = new(1, 0),
        ["PR-404"] = new(2, 0),  ["PR-405"] = new(3, 0),
        ["PR-501"] = new(7, 30), ["PR-502"] = new(10, 30), ["PR-503"] = new(13, 30),
        ["PR-504"] = new(16, 30), ["PR-505"] = new(19, 30),
        ["PR-601"] = new(8, 0), ["PR-602"] = new(11, 0), ["PR-603"] = new(14, 0),
        ["PR-604"] = new(17, 0), ["PR-605"] = new(20, 0),
        // Return legs of the Europe (PR-3xx) and North America (PR-4xx) routes.
        ["PR-701"] = new(9, 0), ["PR-702"] = new(10, 0), ["PR-703"] = new(11, 0),
        ["PR-704"] = new(12, 0), ["PR-705"] = new(13, 0),
        ["PR-801"] = new(9, 30), ["PR-802"] = new(11, 30), ["PR-803"] = new(13, 30),
        ["PR-804"] = new(15, 30), ["PR-805"] = new(17, 30),
    };

    // ========================= EXTRA DAILY DEPARTURES =========================
    // Domestic and Southeast Asia routes run three times a day: the original flight, a second one 10 numbers up (PR-111)
    // and a third 20 numbers up (PR-121). The second is a little cheaper, the third a little dearer, rounded to PHP 50.
    // Long-haul routes keep a single daily flight.
    private static readonly string[] SecondDomesticTimes = { "09:30", "13:30", "16:30", "08:00", "11:00" };
    private static readonly string[] ThirdDomesticTimes = { "14:00", "17:00", "19:30", "18:30", "21:00" };
    private static readonly string[] SecondInternationalTimes = { "12:00", "15:00", "08:30", "09:00", "06:30" };
    private static readonly string[] ThirdInternationalTimes = { "17:30", "19:00", "18:00", "21:00", "11:00" };

    public static readonly Flight[] ExtraDomesticFlights =
        ExtraDepartures(DomesticFlights, 10, 0.92m).Concat(ExtraDepartures(DomesticFlights, 20, 1.08m)).ToArray();

    public static readonly Flight[] ExtraInternationalFlights =
        ExtraDepartures(InternationalFlights, 10, 0.92m).Concat(ExtraDepartures(InternationalFlights, 20, 1.08m)).ToArray();

    private static Flight[] ExtraDepartures(Flight[] baseFlights, int step, decimal priceFactor)
        => baseFlights.Select(f =>
        {
            int number = int.Parse(f.FlightNo[3..], CultureInfo.InvariantCulture) + step;
            decimal price = Math.Round(f.BasePrice * priceFactor / 50m, MidpointRounding.AwayFromZero) * 50m;
            return new Flight($"PR-{number}", f.Origin, f.Destination, price) { Domestic = f.Domestic };
        }).ToArray();

    static AppData()
    {
        // Runs after every static field above is ready. Fills in departure times for the extra flights, and a duration
        // and (when missing) a departure time for every flight and its return leg.
        AssignTimes(ExtraDomesticFlights, SecondDomesticTimes.Concat(ThirdDomesticTimes).ToArray());
        AssignTimes(ExtraInternationalFlights, SecondInternationalTimes.Concat(ThirdInternationalTimes).ToArray());

        var all = new List<(Flight Flight, bool Domestic)>();
        all.AddRange(DomesticFlights.Concat(ExtraDomesticFlights).Select(f => (f, true)));
        all.AddRange(InternationalFlights.Concat(ExtraInternationalFlights).Concat(EuropeFlights).Concat(NorthAmericaFlights).Select(f => (f, false)));

        // A route takes the same time in both directions, whatever the flight number.
        var minutesByRoute = new Dictionary<string, int>();
        foreach (var (flight, _) in all)
            if (FlightEstDurationMin.TryGetValue(flight.FlightNo, out int minutes)) minutesByRoute[flight.Destination] = minutes;

        foreach (var (flight, domestic) in all)
        {
            int minutes = minutesByRoute.GetValueOrDefault(flight.Destination, 120);
            FlightEstDurationMin.TryAdd(flight.FlightNo, minutes);
            Flight back = CreateReverseFlight(flight, domestic);
            FlightEstDurationMin.TryAdd(back.FlightNo, minutes);
            if (FlightScheduledTimes.TryGetValue(flight.FlightNo, out TimeOnly depart))
                FlightScheduledTimes.TryAdd(back.FlightNo, depart.AddHours(3));
        }
    }

    private static void AssignTimes(Flight[] flights, string[] times)
    {
        for (int i = 0; i < flights.Length; i++)
            FlightScheduledTimes[flights[i].FlightNo] = TimeOnly.Parse(times[i], CultureInfo.InvariantCulture);
    }

    public static Flight CreateReverseFlight(Flight outbound, bool domestic)
    {
        int separator = outbound.FlightNo.LastIndexOf('-');
        int number = int.Parse(outbound.FlightNo[(separator + 1)..], CultureInfo.InvariantCulture);
        string returnFlightNo = outbound.FlightNo[..(separator + 1)] + (number + 400).ToString("D3", CultureInfo.InvariantCulture);
        return new Flight(returnFlightNo, outbound.Destination, outbound.Origin, outbound.BasePrice) { Domestic = domestic };
    }

    /// <summary>Default capacity by flight (built from the four flight arrays).</summary>
    public static Dictionary<string, int> CreateDefaultFlightCapacityMap()
    {
        var map = new Dictionary<string, int>();
        foreach (var f in DomesticFlights) map[f.FlightNo] = DefaultDomesticCapacity;
        foreach (var f in InternationalFlights) map[f.FlightNo] = DefaultInternationalCapacity;
        foreach (var f in EuropeFlights) map[f.FlightNo] = DefaultEuropeCapacity;
        foreach (var f in NorthAmericaFlights) map[f.FlightNo] = DefaultNorthAmericaCapacity;
        return map;
    }

    // ========================= SMALL BUSINESS HELPERS =========================
    /// <summary>Port of computeRefundAmount(): 80% if 7+ days out, 50% if 1+ day, else 20%.</summary>
    public static decimal ComputeRefundAmount(decimal totalAmount, DateTime flightDate)
        => Round2(totalAmount * RefundRate(flightDate));

    /// <summary>Share of the fare that is refunded when canceling for a flight on <paramref name="flightDate"/>.</summary>
    public static decimal RefundRate(DateTime flightDate)
    {
        long days = (long)(flightDate.Date - DateTime.Today).TotalDays;
        if (days >= 7) return 0.80m;
        if (days >= 1) return 0.50m;
        return 0.20m;
    }

    /// <summary>Scheduled departure of a flight on a given date (08:00 when the flight has no scheduled time).</summary>
    public static DateTime DepartureOf(string flightNo, DateTime flightDate)
        => flightDate.Date.Add(FlightScheduledTimes.GetValueOrDefault(flightNo, new TimeOnly(8, 0)).ToTimeSpan());

    public static decimal Round2(decimal v) => Math.Round(v, 2, MidpointRounding.AwayFromZero);

    public static int GetAddonIndex(string addonName)
    {
        for (int i = 0; i < AddonNames.Length; i++)
            if (string.Equals(AddonNames[i], addonName, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    public static string[] BaggageOptionsForClass(string className) => className switch
    {
        "Business" => new[] { "Business allowance · 2 bags included", "Extra baggage (+PHP 1,500)" },
        "First Class" => new[] { "First Class allowance · 3 bags included", "Extra baggage (+PHP 1,500)" },
        "Premium Economy" => new[] { "Premium Economy allowance · 1 bag included", "Extra baggage (+PHP 1,500)" },
        _ => new[] { "No checked baggage included", "Add checked baggage (+PHP 1,500)" }
    };

    public static string[] MealOptionsForClass(string className) => className switch
    {
        "Business" => new[] { "Business meal · included", "Vegetarian meal · included" },
        "First Class" => new[] { "First Class dining · included", "Vegetarian meal · included" },
        "Premium Economy" => new[] { "Standard meal · included", "Vegetarian meal · included", "Premium meal (+PHP 300)" },
        _ => new[] { "No meal", "Standard meal (+PHP 500)", "Vegetarian meal (+PHP 500)" }
    };

    public static decimal BaggagePrice(string option)
        => option.StartsWith("Extra baggage", StringComparison.OrdinalIgnoreCase) ||
           option.StartsWith("Add checked baggage", StringComparison.OrdinalIgnoreCase) ? 1_500m : 0m;

    public static decimal MealPrice(string option) => option switch
    {
        "Standard meal (+PHP 500)" => 500m,
        "Vegetarian meal (+PHP 500)" => 500m,
        "Vegetarian meal (+PHP 250)" => 250m,
        "Premium meal (+PHP 300)" => 300m,
        "Premium meal (+PHP 500)" => 500m,
        _ => 0m
    };

    public static string GetDiscountTypeLabel(bool isSenior, bool isPwd)
        => isSenior && isPwd ? "Senior/PWD" : (isSenior ? "Senior" : "PWD");
}
