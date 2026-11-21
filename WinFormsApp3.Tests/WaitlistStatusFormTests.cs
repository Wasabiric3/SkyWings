using Microsoft.Data.Sqlite;
using SkyWings.UI;

namespace SkyWings.Tests;

/// <summary>The traveller's waitlist lookup: with every button showing (an offer is live), they all fit on one line.</summary>
[TestClass]
[DoNotParallelize]
public sealed class WaitlistStatusFormTests
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
    public void AllFourButtons_FitOnOneLine_InsideTheWindow()
        => OnStaThread(() =>
        {
            string db = Path.Combine(Path.GetTempPath(), $"skywings-wlstatus-{Guid.NewGuid():N}.db");
            try
            {
                var store = new BookingStore(new SqliteConnectionStringBuilder { DataSource = db, ForeignKeys = true }.ConnectionString);
                using var form = new WaitlistStatusForm(store)
                {
                    StartPosition = FormStartPosition.Manual, Location = new Point(-4000, 0), ShowInTaskbar = false
                };
                form.Show();
                List<GlassButton> buttons = All(form).OfType<GlassButton>().ToList();
                Assert.HasCount(4, buttons, "Look up, Leave waitlist, Book now and Close");
                foreach (GlassButton b in buttons) b.Visible = true;   // "Book now" and "Leave" show once a live offer is found
                Application.DoEvents();

                Assert.AreEqual(1, buttons.Select(b => b.Top).Distinct().Count(), "no button wrapped onto a second line");
                foreach (GlassButton b in buttons)
                {
                    Rectangle area = form.RectangleToClient(b.RectangleToScreen(b.ClientRectangle));
                    Assert.IsTrue(form.ClientRectangle.Contains(area), $"'{b.Text}' is inside the window");
                }
            }
            finally { SqliteConnection.ClearAllPools(); if (File.Exists(db)) File.Delete(db); }
        });
}
