using Microsoft.Data.Sqlite;
using SkyWings.UI;

namespace SkyWings.Tests;

/// <summary>The home screen's top menu: every link is fully visible, whatever the signed-in name.</summary>
[TestClass]
[DoNotParallelize]
public sealed class HomeHeaderTests
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
    [DataRow("Al")]
    [DataRow("Maximiliana")]
    public void EveryMenuLink_FitsInsideTheMenu(string firstName)
        => OnStaThread(() =>
        {
            string db = Path.Combine(Path.GetTempPath(), $"skywings-header-{Guid.NewGuid():N}.db");
            try
            {
                var store = new BookingStore(new SqliteConnectionStringBuilder { DataSource = db, ForeignKeys = true }.ConnectionString);
                store.SeedDefaultFlights();
                store.SetSetting(BookingStore.SettingDemoActivity, "0");   // no background load generation during the test
                StaffAccount staff = store.CreateStaff("headertest", firstName + " Tester", "Header-Test-Pass1!", StaffRole.Admin, false);
                using var form = new HomeForm(store, new StaffSession(staff.Id, staff.Username, staff.DisplayName, staff.Role, false))
                {
                    StartPosition = FormStartPosition.Manual, Location = new Point(-4000, 0), Size = new Size(1500, 900), ShowInTaskbar = false
                };
                form.Show();
                Application.DoEvents();

                GlassButton manage = Find(form).Single(b => b.Text == "Manage booking");
                Control menu = manage.Parent!;
                foreach (GlassButton link in menu.Controls.OfType<GlassButton>())
                {
                    Assert.IsGreaterThanOrEqualTo(0, link.Left, $"'{link.Text}' starts left of the menu and is cut off");
                    Assert.IsLessThanOrEqualTo(menu.ClientSize.Width, link.Right, $"'{link.Text}' runs past the menu's right edge");
                }
                form.Close();
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                if (File.Exists(db)) File.Delete(db);
            }
        });

    private static IEnumerable<GlassButton> Find(Control root)
    {
        foreach (Control c in root.Controls)
        {
            if (c is GlassButton b) yield return b;
            foreach (GlassButton inner in Find(c)) yield return inner;
        }
    }
}
