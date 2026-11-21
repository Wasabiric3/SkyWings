using SkyWings;
using Microsoft.Data.Sqlite;

namespace SkyWings.Tests;

/// <summary>Integrity of the built-in flight schedule: numbers, times, durations and prices.</summary>
[TestClass]
[DoNotParallelize]
public sealed class FlightScheduleTests
{
    private static IEnumerable<(Flight Flight, bool Domestic)> Outbound()
        => AppData.DomesticFlights.Concat(AppData.ExtraDomesticFlights).Select(f => (f, true))
            .Concat(AppData.InternationalFlights.Concat(AppData.ExtraInternationalFlights)
                .Concat(AppData.EuropeFlights).Concat(AppData.NorthAmericaFlights).Select(f => (f, false)));

    private static IEnumerable<Flight> EveryFlight()
        => Outbound().SelectMany(x => new[] { x.Flight, AppData.CreateReverseFlight(x.Flight, x.Domestic) });

    [TestMethod]
    public void FlightNumbers_AreUniqueAcrossOutboundAndReturnFlights()
    {
        var numbers = EveryFlight().Select(f => f.FlightNo).ToList();
        CollectionAssert.AreEquivalent(numbers.Distinct().ToList(), numbers, "A flight number appears twice.");
        Assert.HasCount(80, numbers);   // 40 flights out of Manila (15 + 15 short-haul, 5 + 5 long-haul) plus their 40 return legs
    }

    [TestMethod]
    public void EveryFlight_HasADepartureTimeAndADuration()
    {
        foreach (Flight f in EveryFlight())
        {
            Assert.IsTrue(AppData.FlightScheduledTimes.ContainsKey(f.FlightNo), $"{f.FlightNo} has no departure time.");
            Assert.IsTrue(AppData.FlightEstDurationMin.TryGetValue(f.FlightNo, out int minutes) && minutes > 0, $"{f.FlightNo} has no duration.");
        }
    }

    [TestMethod]
    public void ReturnLeg_TakesAsLongAsTheOutboundFlight()
    {
        Flight cebu = AppData.DomesticFlights[0];
        Flight back = AppData.CreateReverseFlight(cebu, true);
        Assert.AreEqual(AppData.FlightEstDurationMin[cebu.FlightNo], AppData.FlightEstDurationMin[back.FlightNo]);
    }

    [TestMethod]
    public void ShortHaulDestinations_HaveThreeDistinctDailyDepartures()
    {
        var groups = AppData.DomesticFlights.Concat(AppData.ExtraDomesticFlights)
            .Concat(AppData.InternationalFlights).Concat(AppData.ExtraInternationalFlights).GroupBy(f => f.Destination).ToList();

        Assert.HasCount(10, groups);
        foreach (var group in groups)
        {
            Assert.HasCount(3, group.ToList(), group.Key);
            var times = group.Select(f => AppData.FlightScheduledTimes[f.FlightNo]).Distinct().ToList();
            Assert.HasCount(3, times, $"{group.Key} has two flights at the same time.");
        }
    }

    [TestMethod]
    public void LongHaulDestinations_KeepASingleDailyFlight()
    {
        foreach (var group in AppData.EuropeFlights.Concat(AppData.NorthAmericaFlights).GroupBy(f => f.Destination))
            Assert.HasCount(1, group.ToList(), group.Key);
    }

    [TestMethod]
    public void ExtraDepartures_ArePricedAroundTheOriginal_InWholeFiftyPesos()
    {
        for (int i = 0; i < AppData.DomesticFlights.Length; i++)
        {
            Flight original = AppData.DomesticFlights[i];
            Flight second = AppData.ExtraDomesticFlights[i];
            Flight third = AppData.ExtraDomesticFlights[AppData.DomesticFlights.Length + i];
            Assert.AreEqual(original.Destination, second.Destination);
            Assert.IsLessThan(original.BasePrice, second.BasePrice, original.FlightNo);
            Assert.IsGreaterThan(original.BasePrice, third.BasePrice, original.FlightNo);
            Assert.AreEqual(0m, second.BasePrice % 50m);
            Assert.AreEqual(0m, third.BasePrice % 50m);
            Assert.IsTrue(second.Domestic && third.Domestic);
        }
    }

    [TestMethod]
    public void SeededDatabase_GainsTheExtraFlights_AndKeepsExistingOnesUnchanged()
    {
        string path = Path.Combine(Path.GetTempPath(), $"skywings-sched-{Guid.NewGuid():N}.db");
        try
        {
            var store = new BookingStore(new SqliteConnectionStringBuilder { DataSource = path, ForeignKeys = true }.ConnectionString);
            // A database made by an older version: the flights table holds only the original flight, with a customised price.
            using (var con = new SqliteConnection(store.ConnectionString))
            {
                con.Open();
                using var cmd = con.CreateCommand();
                cmd.CommandText =
                    "CREATE TABLE flights (flight_no VARCHAR(10) PRIMARY KEY, origin VARCHAR(60) NOT NULL, destination VARCHAR(60) NOT NULL, " +
                    "base_price DECIMAL(10,2) NOT NULL, domestic INTEGER NOT NULL, capacity INT NOT NULL, active INTEGER NOT NULL DEFAULT 1, " +
                    "departure_time TIME NOT NULL DEFAULT '08:00:00');" +
                    "INSERT INTO flights VALUES ('PR-101', 'Manila (MNL)', 'Cebu (CEB)', 4000, 1, 150, 1, '06:00:00');";
                cmd.ExecuteNonQuery();
            }

            store.SeedFlightsIfEmpty(AppData.DomesticFlights, AppData.InternationalFlights, 180, 220);

            var domestic = store.LoadActiveFlights(true);
            Assert.HasCount(30, domestic);                                     // 15 outbound + 15 return
            Assert.AreEqual(4_000m, domestic.Single(f => f.FlightNo == "PR-101").BasePrice, "Existing flights are left alone.");
            Assert.AreEqual(3, domestic.Count(f => f.Destination == "Cebu (CEB)"));
            Assert.AreEqual(3, domestic.Count(f => f.Origin == "Cebu (CEB)"));
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
