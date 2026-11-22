using System.Reflection;
using Microsoft.Data.Sqlite;
using SkyWings.UI;

namespace SkyWings.Tests;

/// <summary>Manage booking with a booking open: every action button is fully visible and the right ones are enabled.</summary>
[TestClass]
[DoNotParallelize]
public sealed class ManageBookingFormLayoutTests
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

    private static IEnumerable<Control> All(Control root)
    {
        foreach (Control c in root.Controls)
        {
            yield return c;
            foreach (Control inner in All(c)) yield return inner;
        }
    }

    [TestMethod]
    public void TheActionButtons_AreAllInsideTheWindow_AndLogRequestTurnsIntoRefundRequested()
        => OnStaThread(() =>
        {
            string path = Path.Combine(Path.GetTempPath(), $"skywings-managelayout-{Guid.NewGuid():N}.db");
            try
            {
                var store = new BookingStore(new SqliteConnectionStringBuilder { DataSource = path, ForeignKeys = true, Pooling = false }.ConnectionString);
                store.SeedFlightsIfEmpty(AppData.DomesticFlights, AppData.InternationalFlights,
                    AppData.DefaultDomesticCapacity, AppData.DefaultInternationalCapacity);
                var person = new PassengerRecord("Ana Example", "1 St", "09171234567", 35, "1991-01-01", "P1234567A", false, false,
                    new SeatAssignment("Ana Example", "ECO1A"), Array.Empty<string>(), 4000m);
                var booking = new BookingRecord("SKY-LAYOUT000001", DateTime.Now, "PR-101", "Manila (MNL)", "Cebu (CEB)", new DateTime(2030, 3, 5), true,
                    "Economy", "Credit Card", "**** 4521", 4000m, "Scheduled", new[] { person }, AppData.StatusConfirmed, "PAID", "TXN-1", DateTime.Now, 0m);
                store.SaveConfirmedBooking(booking, false, 4000m, "Credit Card", "**** 4521", "TXN-1", DateTime.Now);

                using var form = new ManageBookingForm(store, "agent1");
                form.CreateControl();
                form.Show();
                Application.DoEvents();

                BindingFlags any = BindingFlags.Instance | BindingFlags.NonPublic;
                ((TextBox)typeof(ManageBookingForm).GetField("_reference", any)!.GetValue(form)!).Text = "SKY-LAYOUT000001";
                typeof(ManageBookingForm).GetMethod("Find", any)!.Invoke(form, null);
                Application.DoEvents();

                string[] expected = { "Change flight", "Change seats", "Log refund request", "Cancel booking", "Look up another", "Close" };
                foreach (string text in expected)
                {
                    GlassButton button = All(form).OfType<GlassButton>().First(b => b.Text == text);
                    Assert.IsGreaterThanOrEqualTo(0, button.Left, text + " starts inside the window");
                    Assert.IsLessThanOrEqualTo(button.Parent!.ClientSize.Width, button.Right, text + " ends inside the window");
                    Assert.IsLessThanOrEqualTo(button.Parent.ClientSize.Height, button.Bottom, text + " is not cut off at the bottom");
                }
                Assert.IsTrue(All(form).OfType<GlassButton>().First(b => b.Text == "Log refund request").Enabled);

                var controller = (SkyWings.Controllers.ManageBookingController)typeof(ManageBookingForm).GetField("_controller", any)!.GetValue(form)!;
                Assert.IsTrue(controller.LogRefundRequest().Ok);
                Assert.AreEqual("agent1", store.LoadRefundRequests().Single().LoggedBy, "the signed-in staff member is recorded");
                typeof(ManageBookingForm).GetMethod("Reload", any)!.Invoke(form, null);
                Application.DoEvents();
                GlassButton requested = All(form).OfType<GlassButton>().First(b => b.Text == "Refund requested");
                Assert.IsFalse(requested.Enabled);
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        });
}
