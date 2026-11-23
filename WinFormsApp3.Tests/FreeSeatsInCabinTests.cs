using Microsoft.Data.Sqlite;

namespace SkyWings.Tests;

/// <summary>
/// "Free a seat" on the staff Waitlist tab must free enough seats for the traveller first in line, even when every demo booking in the
/// cabin is a single traveller (it once freed one seat, and a party of two was never offered anything).
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class FreeSeatsInCabinTests
{
    private static readonly DateTime Day = DateTime.Today.AddDays(3);
    private string _dbPath = "";
    private string _connection = "";
    private BookingStore _store = null!;

    [TestInitialize]
    public void Setup()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"skywings-freeseats-{Guid.NewGuid():N}.db");
        _connection = new SqliteConnectionStringBuilder { DataSource = _dbPath, ForeignKeys = true, Pooling = false }.ConnectionString;
        _store = new BookingStore(_connection);
        _store.SeedDefaultFlights();
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (File.Exists(_dbPath)) File.Delete(_dbPath);
    }

    private void Sql(string sql)
    {
        using var con = new SqliteConnection(_connection);
        con.Open();
        using var cmd = con.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    /// <summary>A confirmed economy booking on PR-101 for the given number of travellers, marked as a demo booking.</summary>
    private string SimulatedBooking(string reference, int travellers, bool simulated = true)
    {
        var people = Enumerable.Range(1, travellers).Select(i => new PassengerRecord($"Demo {reference} {i}", "1 St", "09171234567", 35, "1991-01-01",
            "P" + reference[^6..] + i, false, false, new SeatAssignment($"Demo {reference} {i}", $"ECO{(int)reference[^1] % 20 + 1}{(char)('A' + i - 1)}"),
            Array.Empty<string>(), 1000m)).ToArray();
        var booking = new BookingRecord(reference, DateTime.Now, "PR-101", "Manila (MNL)", "Cebu (CEB)", Day, true, "Economy", "Credit Card", "**** 4521",
            1000m * travellers, "Scheduled", people, AppData.StatusConfirmed, "PAID", "TXN-" + reference, DateTime.Now, 0m);
        _store.SaveConfirmedBooking(booking, false, booking.TotalAmount, "Credit Card", "**** 4521", "TXN-" + reference, DateTime.Now);
        if (simulated) Sql($"UPDATE bookings SET is_simulated = 1 WHERE reference = '{reference}'");
        return reference;
    }

    private int Confirmed(string reference) => _store.FindByReference(reference)!.Status == AppData.StatusConfirmed ? 1 : 0;

    [TestMethod]
    public void ACabinOfSingleTravellers_StillFreesEnoughSeatsForAPartyOfTwo()
    {
        string a = SimulatedBooking("SKY-DDDD00000001", 1);
        string b = SimulatedBooking("SKY-DDDD00000002", 1);
        string c = SimulatedBooking("SKY-DDDD00000003", 1);
        Assert.IsTrue(_store.AddSimulatedWaitlistEntry("PR-101", Day, "Economy", "Party Of Two", "demo@example.com", 2, DateTime.Now.AddHours(-1)));

        (string Reference, int Seats)? freed = new SimulationService(_store).FreeSeatsInCabin("PR-101", Day, "Economy");

        Assert.IsNotNull(freed);
        Assert.AreEqual(2, freed.Value.Seats, "enough seats for the party first in line");
        Assert.AreEqual(1, Confirmed(a) + Confirmed(b) + Confirmed(c), "two of the three single bookings were cancelled, not all of them");
    }

    [TestMethod]
    public void OneBookingThatIsBigEnough_IsStillTheOnlyOneCancelled()
    {
        string small = SimulatedBooking("SKY-DDDD00000011", 1);
        string big = SimulatedBooking("SKY-DDDD00000012", 3);
        Assert.IsTrue(_store.AddSimulatedWaitlistEntry("PR-101", Day, "Economy", "Party Of Three", "demo@example.com", 3, DateTime.Now.AddHours(-1)));

        (string Reference, int Seats)? freed = new SimulationService(_store).FreeSeatsInCabin("PR-101", Day, "Economy");

        Assert.IsNotNull(freed);
        Assert.AreEqual(big, freed.Value.Reference);
        Assert.AreEqual(3, freed.Value.Seats);
        Assert.AreEqual(1, Confirmed(small), "nothing more than needed is cancelled");
    }

    [TestMethod]
    public void WhenThereAreNotEnoughDemoBookings_ItFreesWhatThereIs_AndNeverTouchesARealOne()
    {
        string demo = SimulatedBooking("SKY-DDDD00000021", 1);
        string real = SimulatedBooking("SKY-DDDD00000022", 1, simulated: false);
        Assert.IsTrue(_store.AddSimulatedWaitlistEntry("PR-101", Day, "Economy", "Party Of Four", "demo@example.com", 4, DateTime.Now.AddHours(-1)));

        (string Reference, int Seats)? freed = new SimulationService(_store).FreeSeatsInCabin("PR-101", Day, "Economy");

        Assert.IsNotNull(freed);
        Assert.AreEqual(1, freed.Value.Seats, "all the demo bookings there are");
        Assert.AreEqual(0, Confirmed(demo));
        Assert.AreEqual(1, Confirmed(real), "a real customer's booking is never cancelled");
    }

    [TestMethod]
    public void NoDemoBooking_MeansNothingIsFreed()
    {
        SimulatedBooking("SKY-DDDD00000031", 1, simulated: false);
        Assert.IsNull(new SimulationService(_store).FreeSeatsInCabin("PR-101", Day, "Economy"));
    }
}
