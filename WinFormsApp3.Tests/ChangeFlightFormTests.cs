using Microsoft.Data.Sqlite;
using SkyWings.Controllers;

namespace SkyWings.Tests;

/// <summary>The Change flight dialog opens for a real booking and lists the route's flights without errors.</summary>
[TestClass]
[DoNotParallelize]
public sealed class ChangeFlightFormTests
{
    private static void OnStaThread(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new AssertFailedException(error.Message, error);
    }

    [TestMethod]
    public void TheDialog_Opens_AndNeedsAnOpenBooking()
        => OnStaThread(() =>
        {
            string path = Path.Combine(Path.GetTempPath(), $"skywings-changeform-{Guid.NewGuid():N}.db");
            try
            {
                var store = new BookingStore(new SqliteConnectionStringBuilder { DataSource = path, ForeignKeys = true }.ConnectionString);
                store.SeedFlightsIfEmpty(AppData.DomesticFlights, AppData.InternationalFlights,
                    AppData.DefaultDomesticCapacity, AppData.DefaultInternationalCapacity);
                var controller = new ManageBookingController(store);
                Assert.ThrowsExactly<ArgumentException>(() => new ChangeFlightForm(controller), "no booking open");

                var person = new PassengerRecord("Ana Example", "1 St", "09171234567", 35, "1991-01-01", "P1234567A", false, false,
                    new SeatAssignment("Ana Example", "ECO1A"), Array.Empty<string>(), 4000m);
                var booking = new BookingRecord("SKY-FORM00000001", DateTime.Now, "PR-101", "Manila (MNL)", "Cebu (CEB)", new DateTime(2030, 3, 5), true,
                    "Economy", "Credit Card", "**** 4521", 4000m, "Scheduled", new[] { person }, AppData.StatusConfirmed, "PAID", "TXN-1", DateTime.Now, 0m);
                store.SaveConfirmedBooking(booking, false, 4000m, "Credit Card", "**** 4521", "TXN-1", DateTime.Now);
                Assert.IsTrue(controller.Find(booking.Reference).Found);

                using var form = new ChangeFlightForm(controller);
                form.CreateControl();
                form.Show();
                Application.DoEvents();
                Assert.IsFalse(form.Changed);
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                if (File.Exists(path)) File.Delete(path);
            }
        });
}
