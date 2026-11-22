using System.Reflection;
using Microsoft.Data.Sqlite;
using SkyWings.Controllers;

namespace SkyWings.Tests;

/// <summary>Opens the booking wizard on a temporary seeded database and gives tests the private pieces they need.</summary>
internal sealed class WizardHarness : IDisposable
{
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"skywings-wizard-{Guid.NewGuid():N}.db");

    public WizardHarness(Size? size = null)
    {
        Store = new BookingStore(new SqliteConnectionStringBuilder { DataSource = _path, ForeignKeys = true, Pooling = false }.ConnectionString);
        Store.SeedFlightsIfEmpty(AppData.DomesticFlights, AppData.InternationalFlights,
            AppData.DefaultDomesticCapacity, AppData.DefaultInternationalCapacity);
        Form = new BookingWizardForm(Store);
        if (size is { } s) Form.Size = s;
        Form.Show();
        Application.DoEvents();
    }

    public BookingStore Store { get; }
    public BookingWizardForm Form { get; }

    public static void OnStaThread(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { action(); } catch (Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (error is not null) throw new AssertFailedException(error.Message, error);
    }

    public T Field<T>(string name) => (T)typeof(BookingWizardForm).GetField(name, Any)!.GetValue(Form)!;

    public BookingWizardController Controller => Field<BookingWizardController>("_controller");

    public void GoTo(int step)
    {
        typeof(BookingWizardForm).GetMethod("GoTo", Any)!.Invoke(Form, new object[] { step });
        Application.DoEvents();
    }

    public void Call(string method) => typeof(BookingWizardForm).GetMethod(method, Any)!.Invoke(Form, null);

    /// <summary>Adds one valid traveller through the controller (as the Add traveller button does) and refreshes the summary.</summary>
    public void AddTraveller(string name = "Ana Example", string idNumber = "P1234567A")
    {
        var result = Controller.AddPassenger(new PassengerInput(name, "1 Test St, Manila", "09171234567", idNumber, "1991-01-01", "", false, false), DateTime.Today);
        Assert.IsTrue(result.IsValid, "the sample traveller must be valid");
        Call("RefreshPassengerList");
        Call("SyncSession");
    }

    public Bitmap Snapshot()
    {
        // DrawToBitmap draws the whole window, title bar included, so the bitmap is the window's size.
        var bmp = new Bitmap(Form.Width, Form.Height);
        Form.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
        return bmp;
    }

    public static IEnumerable<Control> All(Control root)
    {
        foreach (Control c in root.Controls)
        {
            yield return c;
            foreach (Control inner in All(c)) yield return inner;
        }
    }

    /// <summary>The control's rectangle in the form's client coordinates.</summary>
    public Rectangle InForm(Control control) => Form.RectangleToClient(control.RectangleToScreen(control.ClientRectangle));

    public void Dispose()
    {
        if (!Form.IsDisposed) Form.Dispose();
        try { if (File.Exists(_path)) File.Delete(_path); } catch (IOException) { /* a temp file; the OS cleans it up */ }
    }
}
