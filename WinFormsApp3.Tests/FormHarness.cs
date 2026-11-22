using Microsoft.Data.Sqlite;
using SkyWings.Controllers;

namespace SkyWings.Tests;

/// <summary>
/// Opens forms on a temporary seeded database so tests can render them to images and check that nothing is clipped.
/// Forms that need an existing booking are added by the task that restyles them.
/// </summary>
internal sealed class FormHarness : IDisposable
{
    public static string Folder => Path.Combine(Path.GetTempPath(), "skywings-snapshots");

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"skywings-forms-{Guid.NewGuid():N}.db");

    public FormHarness()
    {
        Store = new BookingStore(new SqliteConnectionStringBuilder { DataSource = _path, ForeignKeys = true }.ConnectionString);
        Store.SeedFlightsIfEmpty(AppData.DomesticFlights, AppData.InternationalFlights, AppData.DefaultDomesticCapacity, AppData.DefaultInternationalCapacity);
        Session = new StaffSession(1, "admin", "Admin", StaffRole.SuperAdmin, false);
        Admin = new AdminService(Store, Session);
    }

    public BookingStore Store { get; }
    public StaffSession Session { get; }
    public AdminService Admin { get; }

    /// <summary>A paid booking Manila to Cebu with two travellers, saved in the store (the reference is returned in the record).</summary>
    public BookingRecord SampleBooking(string reference = "SKY-FORM0001")
    {
        DateTime day = DateTime.Today.AddDays(30);
        PassengerRecord Person(string name, string seat) => new(name, "1 Example St", "09171234567", 35, "1991-01-01", "P1234567A", false, false,
            new SeatAssignment(name, seat), Array.Empty<string>(), 3500m);
        var booking = new BookingRecord(reference, DateTime.Now, "PR-101", "Manila (MNL)", "Cebu (CEB)", day, true, "Economy", "Credit Card",
            "**** 4521", 7000m, "Scheduled", new[] { Person("Ana Example", "ECO1A"), Person("Ben Example", "ECO1B") }, AppData.StatusConfirmed, "PAID",
            "TXN-" + reference, DateTime.Now, 0m);
        Store.SaveConfirmedBooking(booking, false, 7000m, "Credit Card", "**** 4521", "TXN-" + reference, DateTime.Now);
        return booking;
    }

    /// <summary>One entry per form that can be created from the store alone.</summary>
    public IEnumerable<(string Name, Func<Form> Create)> Forms()
    {
        Flight flight = AppData.DomesticFlights[0];
        var account = new StaffAccount(2, "amy.agent", "Amy Agent", StaffRole.Agent, true, false, null, DateTime.UtcNow, null);
        yield return ("home", () => new HomeForm(Store, Session));
        yield return ("checkin", () => new CheckInForm(Store));
        yield return ("manage-booking", () => new ManageBookingForm(Store));
        yield return ("flight-results", () => new FlightResultsForm(Store, null, DateTime.Today.AddDays(10), "Economy"));
        yield return ("flight-details", () => new FlightDetailsForm(Store, "Domestic", "Domestic", AppData.DomesticFlights.Take(6).ToList()));
        yield return ("help", () => new HelpForm(Session));
        yield return ("policy", () => new PolicyInfoForm("Baggage policy"));
        yield return ("prompt", () => new PromptForm("Reason", "Tell us why.", "REASON", "e.g. schedule change"));
        yield return ("admin", () => new AdminForm(Admin));
        yield return ("waitlist-status", () => new WaitlistStatusForm(Store));
        yield return ("join-waitlist", () => new JoinWaitlistForm(Store, flight, DateTime.Today.AddDays(10), "Economy"));
        yield return ("first-run-admin", () => new FirstRunAdminForm(Store));
        yield return ("change-password", () => new ChangePasswordForm(Admin, false));
        yield return ("add-staff", () => new AddStaffForm(Admin));
        yield return ("reset-password", () => new ResetPasswordForm(Admin, account));
        yield return ("backup-password", () => new BackupPasswordForm());
        yield return ("restore-credentials", () => new RestoreCredentialsForm("skywings-backup.zip"));
        yield return ("staff-login", () => (Form)Activator.CreateInstance(typeof(StaffLoginForm),
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null, new object?[] { Store, "admin", true }, null)!);
    }

    /// <summary>The forms that need a saved booking, a trip status or a waitlist entry.</summary>
    public IEnumerable<(string Name, Func<Form> Create)> BookingForms()
    {
        BookingRecord booking = SampleBooking();
        DateTime day = booking.FlightDateValue;
        var trip = new TripStatusInfo(TripPhase.Upcoming, "Upcoming", false, null, "Cebu (CEB)");
        var checkIns = booking.Passengers.Select(p => new CheckInRecord(booking.Reference, p.Name, "PR-101", day, p.SeatCode, DateTime.Now)).ToList();
        WaitlistEntry entry = Store.AddToWaitlist("WL-FORM0001", "PR-101", day);
        yield return ("booking-detail", () => new BookingDetailForm(booking, trip));
        yield return ("change-flight", () =>
        {
            var controller = new ManageBookingController(Store, "desk");
            controller.Find(booking.Reference);
            return new ChangeFlightForm(controller);
        });
        yield return ("change-seats", () => new SeatChangeForm(Store, booking));
        yield return ("boarding-pass", () => new BoardingPassForm(booking, false, checkIns));
        yield return ("waitlist-entry", () => new WaitlistEntryForm(entry, new[] { entry }));
    }

    public static void OnStaThread(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new AssertFailedException(error.Message, error);
    }

    public static Bitmap Snapshot(Form form)
    {
        if (!form.Visible) form.Show();
        Application.DoEvents();
        var bmp = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
        return bmp;
    }

    public static string SaveSnapshot(Form form, string name)
    {
        Directory.CreateDirectory(Folder);
        string file = Path.Combine(Folder, name + ".png");
        using Bitmap bmp = Snapshot(form);
        bmp.Save(file);
        return file;
    }

    /// <summary>More than a few distinct colours were drawn, so the image is not a blank window.</summary>
    public static bool IsDrawn(Bitmap bmp)
    {
        var colours = new HashSet<int>();
        for (int y = 0; y < bmp.Height; y += 7)
            for (int x = 0; x < bmp.Width; x += 7) colours.Add(bmp.GetPixel(x, y).ToArgb());
        return colours.Count > 5;
    }

    /// <summary>Names every visible label, button, field row or tick box that sticks out of the form's client area (scrolling panels excluded).</summary>
    public static List<string> Clipped(Form form)
    {
        var found = new List<string>();
        Rectangle client = form.ClientRectangle;
        void Walk(Control parent)
        {
            foreach (Control c in parent.Controls)
            {
                if (!c.Visible) continue;
                if (c is UI.GlassButton or UI.FieldRow or Label or CheckBox)
                {
                    Rectangle r = form.RectangleToClient(c.Parent!.RectangleToScreen(c.Bounds));
                    if (r.Right > client.Right + 1 || r.Bottom > client.Bottom + 1 || r.Left < client.Left - 1)
                        found.Add($"{c.GetType().Name} \"{c.Text}\" {r} outside {client.Size}");
                }
                if (c is ScrollableControl { AutoScroll: true }) continue;
                Walk(c);
            }
        }
        Walk(form);
        return found;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (File.Exists(_path)) File.Delete(_path); } catch (IOException) { /* a temp file */ }
    }
}
