namespace SkyWings;

public sealed record FareQuote(
    decimal BaseFare,
    decimal Addons,
    decimal SeasonalSurcharge,
    decimal AdvancePurchaseDiscount,
    decimal PassengerDiscount,
    decimal PromoDiscount,
    decimal TaxableAmount,
    decimal Tax,
    decimal Total,
    string PromoMessage);

public static class FarePricing
{
    private static readonly DateOnly PromoStart = new(2025, 1, 1);
    private static readonly DateOnly PromoEnd = new(2035, 12, 31);

    public static FareQuote Calculate(BookingSession session, DateTime bookedAt)
    {
        int passengerCount = session.Passengers.Count;
        decimal baseFare = session.BaseFarePerPassenger * passengerCount;
        decimal addons = session.AddonTotalPerPassenger * passengerCount;
        decimal seasonalFarePerPassenger =
            (session.SelectedFlight is not null && IsPeakSeason(session.FlightDate) ? session.SelectedFlight.BasePrice : 0m) +
            (session.ReturnFlight is not null && session.ReturnDate is DateTime returnDate && IsPeakSeason(returnDate)
                ? session.ReturnFlight.BasePrice
                : 0m);
        decimal seasonalSurcharge = AppData.Round2(seasonalFarePerPassenger * session.ClassMultiplier * passengerCount * AppData.PeakSeasonRate);
        bool advanceEligible = (session.FlightDate.Date - bookedAt.Date).TotalDays >= AppData.AdvancePurchaseDays;
        bool early500Selected = string.Equals(session.PromoCode, "EARLY500", StringComparison.OrdinalIgnoreCase);
        decimal advanceDiscount = advanceEligible && !early500Selected ? AppData.Round2(baseFare * AppData.AdvancePurchaseRate) : 0m;
        decimal passengerDiscount = AppData.Round2(session.Passengers.Count(p => p.Senior || p.Pwd) *
                                   session.BaseFarePerPassenger * AppData.DiscountRate);

        decimal prePromo = Math.Max(0m, baseFare + seasonalSurcharge + addons - advanceDiscount - passengerDiscount);
        decimal promoDiscount = 0m;
        string promoMessage = "";
        if (!string.IsNullOrWhiteSpace(session.PromoCode))
        {
            bool promoAccepted = TryCalculatePromo(session.PromoCode, session.ClassName, session.FlightDate,
                bookedAt, advanceEligible, prePromo, out promoDiscount, out promoMessage);
            if (!promoAccepted) promoDiscount = 0m;
        }

        decimal taxable = Math.Max(0m, prePromo - promoDiscount);
        bool taxExempt = passengerCount > 0 && session.Passengers.All(p => p.Senior || p.Pwd);
        decimal tax = taxExempt ? 0m : taxable * AppData.TaxRate;
        return new FareQuote(baseFare, addons, seasonalSurcharge, advanceDiscount, passengerDiscount,
            promoDiscount, taxable, AppData.Round2(tax), AppData.Round2(taxable + tax), promoMessage);
    }

    public static bool TryCalculatePromo(string code, string className, DateTime travelDate,
        DateTime bookedAt, bool advanceEligible, decimal eligibleAmount, out decimal discount, out string message)
    {
        discount = 0m;
        string normalized = code.Trim().ToUpperInvariant();
        DateOnly travel = DateOnly.FromDateTime(travelDate);
        if (travel < PromoStart || travel > PromoEnd)
        {
            message = "This promo is not valid for the selected travel date.";
            return false;
        }

        if (normalized == "SKY10")
        {
            if (className == "First Class")
            {
                message = "SKY10 is not valid for First Class.";
                return false;
            }
            if (advanceEligible)
            {
                message = "SKY10 cannot be combined with the advance-purchase discount.";
                return false;
            }
            discount = AppData.Round2(eligibleAmount * 0.10m);
            message = discount > 0 ? $"SKY10 applied: PHP {discount:N2} off." : "The booking total is not eligible for this promo.";
            return discount > 0;
        }

        if (normalized == "EARLY500")
        {
            if ((travelDate.Date - bookedAt.Date).TotalDays < 30)
            {
                message = "EARLY500 requires booking at least 30 days before departure.";
                return false;
            }
            if (eligibleAmount < 5_000m)
            {
                message = "EARLY500 requires an eligible fare of at least PHP 5,000.";
                return false;
            }
            discount = Math.Min(500m, eligibleAmount);
            message = $"EARLY500 applied: PHP {discount:N2} off; it replaces the advance-purchase discount.";
            return true;
        }

        message = "Promo code not recognized.";
        return false;
    }

    public static bool IsPeakSeason(DateTime travelDate)
    {
        int monthDay = travelDate.Month * 100 + travelDate.Day;
        return monthDay >= 615 && monthDay <= 831 || monthDay >= 1215 || monthDay <= 115;
    }

    public static decimal GetSeasonalRate(DateTime travelDate)
        => IsPeakSeason(travelDate) ? AppData.PeakSeasonRate : 0m;
}
