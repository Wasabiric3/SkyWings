using System.Globalization;

namespace SkyWings;

/// <summary>One line of the fare summary. <see cref="Amount"/> is always positive; discounts are flagged and shown with a minus.</summary>
public sealed record ReceiptLine(string Label, decimal Amount, bool Discount = false)
{
    public static string Money(decimal value) => "PHP " + value.ToString("N2", CultureInfo.InvariantCulture);

    public string Text => Discount ? "-" + Money(Amount) : Money(Amount);
}

/// <summary>The return flight of a round trip, shown as one extra row on the receipt.</summary>
public sealed record ReceiptReturn(string FlightNo, string Route, DateTime Departure, string Seats);

/// <summary>Everything the printed booking receipt shows, already worked out as display text. Drawn by <c>ReceiptPrinter</c>.</summary>
public sealed record ReceiptData(
    string Reference, string PaymentStatus, DateTime IssuedOn,
    string OriginCode, string OriginCity, string DestinationCode, string DestinationCity,
    string Passengers, string FlightNo, string ClassAndSeat, DateTime Departure, DateTime Arrival, string TripType,
    ReceiptReturn? Return, IReadOnlyList<ReceiptLine> FareLines, string TotalPaid, string PaymentMethod)
{
    /// <summary>"Manila (MNL)" becomes ("MNL", "Manila"). A place without a bracketed code uses its first three letters.</summary>
    public static (string Code, string City) SplitPlace(string place)
    {
        int open = place.IndexOf('('), close = place.IndexOf(')');
        if (open > 0 && close > open) return (place[(open + 1)..close].Trim(), place[..open].Trim());
        string city = place.Trim();
        return ((city.Length >= 3 ? city[..3] : city).ToUpperInvariant(), city);
    }

    /// <summary>Builds the receipt for a stored booking. <paramref name="session"/> supplies the fare breakdown (add-ons, discounts).</summary>
    public static ReceiptData From(BookingRecord booking, BookingSession session)
        => Build(booking, BuildFareLines(session, booking.BookedAtValue));

    /// <summary>
    /// Builds the receipt for a booking that is only stored (no live wizard session), e.g. when printing from Manage booking.
    /// The fare breakdown is worked out again from the stored booking and the current flight list. It is shown only if it adds up
    /// to what was actually paid; if a flight is gone or prices have changed, one honest total line is printed instead.
    /// </summary>
    public static ReceiptData FromStored(BookingRecord booking, IEnumerable<Flight> flights)
    {
        IReadOnlyList<ReceiptLine> lines = TotalOnly(booking);
        if (RebuildSession(booking, flights.ToList()) is { } session)
        {
            List<ReceiptLine> rebuilt = BuildFareLines(session, booking.BookedAtValue);
            decimal sum = rebuilt.Sum(l => l.Discount ? -l.Amount : l.Amount);
            if (Math.Abs(sum - booking.TotalAmount) <= 0.05m) lines = rebuilt;
        }
        return Build(booking, lines);
    }

    private static List<ReceiptLine> TotalOnly(BookingRecord booking) => new() { new("Fare, add-ons and taxes", booking.TotalAmount) };

    private static BookingSession? RebuildSession(BookingRecord booking, List<Flight> flights)
    {
        Flight? outbound = flights.FirstOrDefault(f => f.FlightNo == booking.FlightNo);
        Flight? back = booking.ReturnFlightNo is null ? null : flights.FirstOrDefault(f => f.FlightNo == booking.ReturnFlightNo);
        if (outbound is null || (booking.ReturnFlightNo is not null && back is null) || booking.Passengers.Count == 0) return null;

        var session = new BookingSession
        {
            SelectedFlight = outbound, FlightDate = booking.FlightDateValue, ClassName = booking.ClassName,
            ReturnFlight = back, ReturnDate = booking.ReturnFlightDateValue,
            BaggageOption = booking.BaggageOption, MealOption = booking.MealOption, PromoCode = booking.PromoCode
        };
        foreach (PassengerRecord p in booking.Passengers)
            session.Passengers.Add(new PassengerDraft { Name = p.Name, Senior = p.Senior, Pwd = p.Pwd });
        foreach (string addon in booking.Passengers[0].Addons) session.Addons.Add(addon);
        return session;
    }

    private static ReceiptData Build(BookingRecord booking, IReadOnlyList<ReceiptLine> fareLines)
    {
        var (originCode, originCity) = SplitPlace(booking.Origin);
        var (destinationCode, destinationCity) = SplitPlace(booking.Destination);

        DateTime departure = AppData.DepartureOf(booking.FlightNo, booking.FlightDateValue);
        DateTime arrival = departure.AddMinutes(AppData.FlightEstDurationMin.GetValueOrDefault(booking.FlightNo, 120));

        string seats = string.Join(", ", booking.Passengers.Select(p => p.SeatCode).Where(c => c.Length > 0));
        string classAndSeat = seats.Length > 0 ? $"{booking.ClassName} · {seats}" : booking.ClassName;

        ReceiptReturn? back = null;
        if (booking.ReturnFlightNo is { } returnNo)
        {
            DateTime returnDate = booking.ReturnFlightDateValue ?? booking.FlightDateValue;
            string route = $"{SplitPlace(booking.ReturnOrigin ?? "").Code} → {SplitPlace(booking.ReturnDestination ?? "").Code}";
            string returnSeats = string.Join(", ", booking.Passengers.Select(p => p.ReturnSeatCode).Where(c => c.Length > 0));
            back = new ReceiptReturn(returnNo, route, AppData.DepartureOf(returnNo, returnDate), returnSeats);
        }

        return new ReceiptData(
            booking.Reference,
            // Payments in this app are simulated (the confirm dialog says "demo payment"), so the receipt says so too.
            booking.PaymentStatus.ToUpperInvariant() + " · DEMO TRANSACTION",
            booking.PaymentConfirmedAtValue ?? booking.BookedAtValue,
            originCode, originCity, destinationCode, destinationCity,
            string.Join(", ", booking.Passengers.Select(p => p.Name)),
            booking.FlightNo, classAndSeat, departure, arrival,
            back is null ? "One-way" : "Round trip", back,
            fareLines,
            ReceiptLine.Money(booking.TotalAmount),
            $"{booking.PaymentMethod} {booking.MaskedAccount}".Trim());
    }

    private static List<ReceiptLine> BuildFareLines(BookingSession session, DateTime bookedAt)
    {
        FareQuote q = FarePricing.Calculate(session, bookedAt);
        int travellers = session.Passengers.Count;
        var lines = new List<ReceiptLine> { new("Base fare", q.BaseFare) };
        if (q.SeasonalSurcharge > 0) lines.Add(new("Peak season surcharge", q.SeasonalSurcharge));

        decimal shown = 0m;
        void AddOn(string label, decimal pricePerTraveller)
        {
            decimal amount = pricePerTraveller * travellers;
            if (amount <= 0) return;
            lines.Add(new(label, amount));
            shown += amount;
        }
        AddOn("Extra baggage", AppData.BaggagePrice(session.BaggageOption));
        AddOn("In-flight meal", AppData.MealPrice(session.MealOption));
        foreach (string name in session.Addons.OrderBy(AppData.GetAddonIndex))
            if (AppData.GetAddonIndex(name) is >= 0 and int index) AddOn(name, AppData.AddonPrices[index]);

        decimal other = q.Addons - shown;   // keeps the lines adding up to the total if a price rule is added later
        if (other >= 0.01m) lines.Add(new("Other add-ons", other));

        if (q.AdvancePurchaseDiscount > 0) lines.Add(new("Advance purchase discount", q.AdvancePurchaseDiscount, true));
        if (q.PassengerDiscount > 0) lines.Add(new("Senior / PWD discount", q.PassengerDiscount, true));
        if (q.PromoDiscount > 0) lines.Add(new($"Promo {session.PromoCode}", q.PromoDiscount, true));
        lines.Add(new("Taxes and fees (VAT 12%)", q.Tax));
        return lines;
    }
}
