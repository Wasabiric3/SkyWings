using SkyWings.UI;

namespace SkyWings.Tests;

/// <summary>The staff dialog for one waitlist entry: it opens, and its buttons are fully visible.</summary>
[TestClass]
public sealed class WaitlistEntryFormTests
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

    private static WaitlistEntry Entry(long position, string name, string status = BookingStore.WaitlistWaiting) =>
        new(position, "WL-TEST000" + position, "PR-611", new DateTime(2026, 11, 21), new DateTime(2026, 11, 21, 8, 30, 0), status)
        {
            PassengerName = name, Email = "x@example.com", ClassName = "Business", SeatsWanted = 2,
            OfferExpiresAt = status == BookingStore.WaitlistOffered ? new DateTime(2026, 11, 22, 8, 0, 0) : null
        };

    [TestMethod]
    [DataRow(BookingStore.WaitlistWaiting)]
    [DataRow(BookingStore.WaitlistOffered)]
    public void TheDialog_ShowsTheCodeAndKeepsItsButtonsOnScreen(string status)
        => OnStaThread(() =>
        {
            WaitlistEntry entry = Entry(2, "Ana Garcia", status);
            var queue = new List<WaitlistEntry> { Entry(1, "Alma Torres"), entry, Entry(3, "Eric Go"), Entry(4, "Rosa Reyes") };
            using var form = new WaitlistEntryForm(entry, queue)
            {
                StartPosition = FormStartPosition.Manual, Location = new Point(-4000, 0), ShowInTaskbar = false
            };
            form.Show();
            Application.DoEvents();

            Assert.IsTrue(All(form).OfType<TextBox>().Any(t => t.Text == entry.BookingReference), "the code is shown");
            Assert.IsTrue(All(form).OfType<TextBox>().Any(t => t.Text.Contains("Eric Go")), "the cabin's queue is listed");
            GlassButton copy = All(form).OfType<GlassButton>().Single(b => b.Text == "Copy code");
            Point bottomRight = form.PointToClient(copy.PointToScreen(new Point(copy.Width, copy.Height)));
            Assert.IsLessThanOrEqualTo(form.ClientSize.Height, bottomRight.Y, "Copy code is inside the window");
        });
}
