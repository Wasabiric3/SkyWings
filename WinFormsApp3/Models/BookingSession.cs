namespace SkyWings;

/// <summary>State shared by the customer booking workflow screens.</summary>
public sealed class BookingSession
{
    public Flight? SelectedFlight { get; set; }
    public DateTime FlightDate { get; set; } = DateTime.Today.AddDays(1);
    public Flight? ReturnFlight { get; set; }
    public DateTime? ReturnDate { get; set; }
    public string ClassName { get; set; } = AppData.ClassNames[0];
    public List<PassengerDraft> Passengers { get; } = new();
    public HashSet<string> Addons { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> SeatAssignments { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> ReturnSeatAssignments { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string BaggageOption { get; set; } = "Standard";
    public string MealOption { get; set; } = "Standard meal";
    public string PromoCode { get; set; } = "";
    public string ProfileReference { get; set; } = "";
    public bool IsWaitlisted { get; set; }
    /// <summary>The live waitlist offer this booking uses (empty for an ordinary booking). Marked converted on payment.</summary>
    public string WaitlistCode { get; set; } = "";
    public DateTime? CheckedInAt { get; set; }
    public string PaymentMethod { get; set; } = "Credit Card";
    public string MaskedAccount { get; set; } = "";
    public string BookingReference { get; set; } = "";
    /// <summary>Where the receipt email goes. Optional; empty means no email.</summary>
    public string ContactEmail { get; set; } = "";
    public MockPaymentResult? PaymentResult { get; set; }

    public decimal ClassMultiplier
    {
        get
        {
            int index = Array.IndexOf(AppData.ClassNames, ClassName);
            return index >= 0 ? AppData.ClassMultipliers[index] : 1.0m;
        }
    }

    public decimal AddonTotalPerPassenger
        => Addons.Sum(name =>
        {
            int index = AppData.GetAddonIndex(name);
            return index >= 0 ? AppData.AddonPrices[index] : 0m;
        }) + AppData.BaggagePrice(BaggageOption) + AppData.MealPrice(MealOption);

    public decimal BaseFarePerPassenger => ((SelectedFlight?.BasePrice ?? 0m) + (ReturnFlight?.BasePrice ?? 0m)) * ClassMultiplier;

    public FareQuote Quote => FarePricing.Calculate(this, DateTime.Today);
    public decimal Subtotal => Quote.BaseFare + Quote.Addons + Quote.SeasonalSurcharge;
    public decimal DiscountTotal => Quote.AdvancePurchaseDiscount + Quote.PassengerDiscount + Quote.PromoDiscount;
    public decimal TaxableAmount => Quote.TaxableAmount;
    public decimal TaxTotal => Quote.Tax;
    public decimal Total => Quote.Total;

    public void Reset()
    {
        SelectedFlight = null;
        FlightDate = DateTime.Today.AddDays(1);
        ReturnFlight = null;
        ReturnDate = null;
        ClassName = AppData.ClassNames[0];
        Passengers.Clear();
        Addons.Clear();
        SeatAssignments.Clear();
        ReturnSeatAssignments.Clear();
        BaggageOption = "Standard";
        MealOption = "Standard meal";
        PromoCode = "";
        ProfileReference = "";
        IsWaitlisted = false;
        WaitlistCode = "";
        CheckedInAt = null;
        PaymentMethod = "Credit Card";
        MaskedAccount = "";
        BookingReference = "";
        ContactEmail = "";
        PaymentResult = null;
    }
}

public sealed class PassengerDraft
{
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public string Contact { get; set; } = "";
    public int Age { get; set; }
    public string Birthday { get; set; } = "";
    public string IdNumber { get; set; } = "";
    public string ProfileReference { get; set; } = "";
    public bool Senior { get; set; }
    public bool Pwd { get; set; }

    public PassengerRecord ToRecord(string seatCode, IEnumerable<string> addons, decimal totalAmount, string returnSeatCode = "")
        => new(Name, Address, Contact, Age, Birthday, IdNumber, Senior, Pwd,
            string.IsNullOrWhiteSpace(seatCode) ? null : new SeatAssignment(Name, seatCode),
            addons, totalAmount, returnSeatCode);
}
