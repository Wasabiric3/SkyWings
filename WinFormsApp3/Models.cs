using System.Globalization;

namespace SkyWings;

/// <summary>A flight service in the schedule.</summary>
public sealed record Flight(string FlightNo, string Origin, string Destination, decimal BasePrice)
{
    public bool Domestic { get; init; } = FlightNo.StartsWith("PR-1", StringComparison.Ordinal);

    public override string ToString() => $"{FlightNo} · {Origin} → {Destination} · PHP {BasePrice:N0}";
}

/// <summary>Row shown in the admin "Flight Management" table.</summary>
public sealed record FlightAdminRow(
    string FlightNo, string Origin, string Destination, decimal BasePrice,
    string Type, int Capacity, string Status, string DepartureDisplay)
{
    /// <summary>Id of the flight's aircraft (see <see cref="Aircraft"/>), or null for the original capacity-based layout.</summary>
    public string? AircraftId { get; init; }
}

public sealed record FlightInventory(int Capacity, int ConfirmedBookings, int HeldSeats)
{
    public int AvailableSeats => Math.Max(0, Capacity - ConfirmedBookings - HeldSeats);
}

public sealed record PassengerProfile(
    string Reference, string Name, string Address, string Contact, int Age,
    string Birthday, string IdNumber, bool Senior, bool Pwd, string LoyaltyNumber, long Miles);

public sealed record WaitlistEntry(
    long Position, string BookingReference, string FlightNo, DateTime FlightDate,
    DateTime AddedAt, string Status)
{
    public long Id { get; init; }
    public string PassengerName { get; init; } = "";
    public string Email { get; init; } = "";
    public string ClassName { get; init; } = "Economy";
    public int SeatsWanted { get; init; } = 1;
    public DateTime? OfferExpiresAt { get; init; }
    /// <summary>Made by the demo simulator: never audited or emailed.</summary>
    public bool IsSimulated { get; init; }
    /// <summary>When the current offer was made (null before an offer).</summary>
    public DateTime? OfferedAt { get; init; }
}

/// <summary>The result of freeing seats for a waitlist: how many were freed and who (if anyone) was offered them.</summary>
public sealed record WaitlistSeatRelease(int SeatsFreed, WaitlistEntry? OfferedTo);

public sealed record CheckInRecord(
    string BookingReference, string PassengerName, string FlightNo, DateTime FlightDate,
    string SeatCode, DateTime CheckedInAt);

/// <summary>A demo traveller who has not checked in yet for their flight (used by the simulator).</summary>
public sealed record SimCheckInCandidate(string Reference, string FlightNo, DateTime FlightDate, string PassengerName, string SeatCode);

public sealed class ValidationResult
{
    public bool Valid { get; }
    public string Message { get; }
    public Control? Field { get; }

    private ValidationResult(bool valid, string message, Control? field)
    {
        Valid = valid; Message = message; Field = field;
    }

    public static ValidationResult Ok() => new(true, "", null);
    public static ValidationResult Error(string message, Control? field) => new(false, message, field);
}

public sealed class MockPaymentResult
{
    public bool Approved { get; }
    public string Status { get; }
    public string TransactionReference { get; }
    public DateTime ProcessedAt { get; }
    public string DetailMessage { get; }

    private MockPaymentResult(bool approved, string status, string txnRef, DateTime processedAt, string detail)
    {
        Approved = approved; Status = status; TransactionReference = txnRef;
        ProcessedAt = processedAt; DetailMessage = detail;
    }

    public static MockPaymentResult CreateApproved(string txnRef, DateTime at, string detail)
        => new(true, "SUCCESS", txnRef, at, detail);

    public static MockPaymentResult CreateDeclined(string txnRef, DateTime at, string detail)
        => new(false, "FAILED", txnRef, at, detail);
}

public sealed class SeatAssignment
{
    public string PassengerName { get; }
    public string SeatCode { get; }

    public SeatAssignment(string passengerName, string? seatCode)
    {
        PassengerName = passengerName;
        SeatCode = seatCode ?? "";
    }
}

public sealed class PassengerRecord
{
    public string Name { get; }
    public string Address { get; }
    public string Contact { get; }
    public int Age { get; }
    public string Birthday { get; }
    public string IdNumber { get; }
    public bool Senior { get; }
    public bool Pwd { get; }
    public SeatAssignment? SeatAssignment { get; }
    public List<string> Addons { get; }
    public decimal TotalAmount { get; }
    public string SeatCode { get; }
    public string ReturnSeatCode { get; }

    public PassengerRecord(string name, string address, string contact, int age, string birthday,
        string idNumber, bool senior, bool pwd, SeatAssignment? seatAssignment,
        IEnumerable<string> addons, decimal totalAmount, string returnSeatCode = "")
    {
        Name = name; Address = address; Contact = contact; Age = age; Birthday = birthday;
        IdNumber = idNumber; Senior = senior; Pwd = pwd; SeatAssignment = seatAssignment;
        Addons = new List<string>(addons);
        TotalAmount = totalAmount;
        SeatCode = seatAssignment?.SeatCode ?? "";
        ReturnSeatCode = returnSeatCode;
    }

    public string DiscountLabel
    {
        get
        {
            if (Senior && Pwd) return "Senior/PWD";
            if (Senior) return "Senior";
            if (Pwd) return "PWD";
            return "None";
        }
    }

    public string AddonSummary => Addons.Count == 0 ? "None" : string.Join(", ", Addons);
}

public sealed class BookingRecord
{
    public string Reference { get; }
    public DateTime BookedAtValue { get; }
    public string BookedAt { get; }
    public string FlightNo { get; }
    public string Origin { get; }
    public string Destination { get; }
    public DateTime FlightDateValue { get; }   // date part only
    public string FlightDate { get; }
    public bool Domestic { get; }
    public string ClassName { get; }
    public string PaymentMethod { get; }
    public string MaskedAccount { get; }
    public decimal TotalAmount { get; }
    public string RealtimeEst { get; }
    public List<PassengerRecord> Passengers { get; }
    public string Status { get; }
    public string PaymentStatus { get; }
    public string PaymentTransactionRef { get; }
    public DateTime? PaymentConfirmedAtValue { get; }
    public string PaymentConfirmedAt { get; }
    public decimal RefundAmount { get; }
    public string? ReturnFlightNo { get; init; }
    public string? ReturnOrigin { get; init; }
    public string? ReturnDestination { get; init; }
    public bool ReturnDomestic { get; init; }
    public DateTime? ReturnFlightDateValue { get; init; }
    public string BaggageOption { get; init; } = "Standard";
    public string MealOption { get; init; } = "Standard meal";
    public string PromoCode { get; init; } = "";
    public string ProfileReference { get; init; } = "";
    public bool IsWaitlisted { get; init; }

    public BookingRecord(string reference, DateTime bookedAtValue, string flightNo, string origin,
        string destination, DateTime flightDateValue, bool domestic, string className,
        string paymentMethod, string maskedAccount, decimal totalAmount, string realtimeEst,
        IEnumerable<PassengerRecord> passengers, string status, string? paymentStatus,
        string? paymentTransactionRef, DateTime? paymentConfirmedAtValue, decimal refundAmount)
    {
        Reference = reference;
        BookedAtValue = bookedAtValue;
        BookedAt = bookedAtValue.ToString(AppData.EstDateTimeFormat, CultureInfo.InvariantCulture);
        FlightNo = flightNo;
        Origin = origin;
        Destination = destination;
        FlightDateValue = flightDateValue.Date;
        FlightDate = FlightDateValue.ToString(AppData.FlightDateDisplayFormat, CultureInfo.InvariantCulture);
        Domestic = domestic;
        ClassName = className;
        PaymentMethod = paymentMethod;
        MaskedAccount = maskedAccount;
        TotalAmount = totalAmount;
        RealtimeEst = realtimeEst;
        Passengers = new List<PassengerRecord>(passengers);
        Status = status;
        PaymentStatus = string.IsNullOrWhiteSpace(paymentStatus) ? AppData.PaymentUnpaid : paymentStatus;
        PaymentTransactionRef = paymentTransactionRef ?? "";
        PaymentConfirmedAtValue = paymentConfirmedAtValue;
        PaymentConfirmedAt = paymentConfirmedAtValue == null
            ? "N/A"
            : paymentConfirmedAtValue.Value.ToString(AppData.EstDateTimeFormat, CultureInfo.InvariantCulture);
        RefundAmount = refundAmount;
    }

    public BookingRecord WithStatus(string newStatus)
        => new BookingRecord(Reference, BookedAtValue, FlightNo, Origin, Destination, FlightDateValue, Domestic,
            ClassName, PaymentMethod, MaskedAccount, TotalAmount, RealtimeEst, Passengers, newStatus,
            PaymentStatus, PaymentTransactionRef, PaymentConfirmedAtValue, RefundAmount)
        {
            ReturnFlightNo = ReturnFlightNo,
            ReturnOrigin = ReturnOrigin,
            ReturnDestination = ReturnDestination,
            ReturnDomestic = ReturnDomestic,
            ReturnFlightDateValue = ReturnFlightDateValue,
            BaggageOption = BaggageOption,
            MealOption = MealOption,
            PromoCode = PromoCode,
            ProfileReference = ProfileReference,
            IsWaitlisted = IsWaitlisted
        };
}

public sealed class SeatHoldInfo
{
    public string Owner { get; }
    public DateTime ExpiresAt { get; }
    public SeatHoldInfo(string owner, DateTime expiresAt) { Owner = owner; ExpiresAt = expiresAt; }
}

// ---- Report row types (Java used Object[] / Map<String,Object>) ----
public sealed record RouteStat(string FlightNo, string Origin, string Destination, long Count);
public sealed record ClassStat(string ClassName, long Count, decimal Revenue);

public sealed class ReportStats
{
    public long TotalBookings { get; set; }
    public long ConfirmedCount { get; set; }
    public long CancelledCount { get; set; }
    public decimal TotalRevenue { get; set; }
    public decimal TotalRefunded { get; set; }
    public List<RouteStat> TopRoutes { get; } = new();
    public List<ClassStat> ByClass { get; } = new();
}

public sealed record CompletedBookingRow(
    string Reference, string FlightNo, string Route, string FlightDate,
    string ClassName, long PaxCount, decimal TotalAmount, string PaymentStatus);

public sealed record CancelledBookingRow(
    string Reference, string FlightNo, string Route, string FlightDate,
    string ClassName, long PaxCount, decimal TotalAmount, string PaymentStatus, decimal RefundAmount);
