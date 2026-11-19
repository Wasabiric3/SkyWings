using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using SkyWings.UI;

namespace SkyWings;

/// <summary>One question-and-answer in the Help window.</summary>
internal sealed record HelpTopic(string Title, string Body);

/// <summary>One row of the fare and policy table.</summary>
internal sealed record PolicyRow(string Area, string Rule, string Value);

/// <summary>
/// The text shown in the Help window. Numbers are read from <see cref="AppData"/> and friends, so the Help text
/// cannot drift away from the rules the app actually enforces.
/// </summary>
internal static class HelpContent
{
    public static IReadOnlyList<HelpTopic> Topics() => new HelpTopic[]
    {
        new("Book a flight",
            "1. On the home screen, search for a flight or press \"Book a flight\".\n" +
            "2. Work through the steps: Flights, Passengers, Class, Add-ons, Seats, Billing, Payment.\n" +
            "3. The Receipt shows the booking reference. Give it to the traveller; they need it to manage the booking and to check in.\n\n" +
            $"A booking can have up to {BookingRules.MaxPassengersPerBooking} travellers, and round trips are supported."),

        new("Seat holds",
            $"A seat you pick in the seat map is held for {AppData.SeatHoldMinutes} minutes while the booking is completed. " +
            "If the time runs out, the seat is released and someone else can take it, so pick the seats again before paying."),

        new("Change a seat or cancel",
            "Choose \"Manage booking\" on the home screen. Enter the booking reference from the receipt.\n\n" +
            "A confirmed booking can have its seats changed or be cancelled. Once the flight has departed, the booking can no longer be changed.\n\n" +
            "Refunds depend on how far away the flight is. See the \"Fares & policies\" tab."),

        new("Waitlist for a sold-out flight",
            "When a cabin is sold out, select the flight on the home screen and choose \"Join waitlist\". Enter the traveller's name, email and seats needed. " +
            "Keep the waitlist code that is shown.\n\n" +
            $"When a booking is cancelled, the first traveller in line is emailed an offer and the seats are held for up to {AppData.WaitlistOfferHours} hours, or until bookings close for the flight if that comes first. " +
            "To use it, choose \"Waitlist\" on the home screen, enter the waitlist code, then \"Book now\". " +
            "If the offer is not used in time, the seats go to the next traveller.\n\n" +
            "Staff can see and remove waitlist entries on the Waitlist tab of the staff window."),

        new("Aircraft and seat maps",
            "Each flight has an aircraft that decides its cabins and seat map: " +
            "a narrow-body (Airbus A320, 3-3 seats) on domestic routes, a mid-size wide-body (Airbus A330-300, 2-4-2 seats) to Southeast Asia, " +
            "and a large wide-body (Boeing 777-300ER, 3-4-3 seats) to Europe and North America.\n\n" +
            "A cabin an aircraft does not have (for example First Class on an A320) is not offered for that flight. " +
            "Staff can change a flight's aircraft in the flight editor; the change is refused if a booked seat would no longer exist."),

        new("Check in and boarding pass",
            "Online check-in opens as soon as the booking is made and closes at departure.\n\n" +
            "Choose \"Check-in\", enter the booking reference, then print or save the boarding pass.\n\n" +
            "If an email address was given for the booking, the boarding pass is also emailed as a PDF with a QR code. " +
            "Use \"Email boarding pass\" to send it again, for example for a traveller who checked in earlier. " +
            "Without mail settings it is saved as a file in the outbox folder instead."),

        new("Trip status",
            "Every booking shows where its trip is right now, following the flight's own times (delays included):\n" +
            "Upcoming → Check-in open → Checked in → Boarding → In flight → Completed.\n\n" +
            "A booking becomes a No-show when the flight leaves and nobody on it checked in. A cancelled booking shows Canceled. " +
            "On a return trip the outbound flight decides until it lands; then the return flight does (\"Return · In flight\"), " +
            "and the trip is Completed once the return lands.\n\n" +
            "Staff can filter the Bookings tab by Upcoming, In progress, Completed, No-show or Canceled."),

        new("Senior and PWD discounts",
            $"Senior citizens (age {AppData.SeniorMinimumAge} and above) and persons with disability get {AppData.DiscountRate:P0} off, " +
            $"and the {AppData.TaxRate:P0} VAT does not apply to them. Tick the discount for the traveller on the Passengers step and enter their ID number."),

        new("Promo codes",
            "Enter a code on the payment step.\n\n" +
            "SKY10: 10% off. Not valid for First Class, and it cannot be combined with the advance-purchase discount.\n\n" +
            "EARLY500: PHP 500 off. Needs a fare of at least PHP 5,000 and a booking made at least 30 days before departure. " +
            "It replaces the advance-purchase discount."),

        new("Signing in and passwords",
            $"Every staff member has their own account. Passwords need at least {PasswordPolicy.MinLength} characters.\n\n" +
            $"After {BookingStore.MaxFailedSignIns} wrong passwords the account is locked for {(int)BookingStore.SignInLockout.TotalMinutes} minutes.\n\n" +
            $"The staff window signs you out after {(int)IdleMonitor.DefaultLimit.TotalMinutes} minutes without activity.\n\n" +
            "Change your own password from the Staff window (\"Password\" button)."),

        new("Staff roles",
            "Agent: can view and cancel bookings and view reports.\n\n" +
            "Admin: everything an agent can do, plus managing flights and staff accounts, the audit log, and backup and restore.\n\n" +
            "Everything staff do is recorded in the audit log."),

        new("Backup and restore (admins)",
            "Open the Staff window, go to Flights, and use Backup or Restore.\n\n" +
            "Backups are encrypted with a password you choose. The password cannot be recovered, so keep it safe.\n\n" +
            "Restoring replaces the current bookings and flights. A safety copy of the current data is saved first."),
    };

    public static IReadOnlyList<PolicyRow> PolicyRows()
    {
        var rows = new List<PolicyRow>
        {
            new("Refunds", "Cancelled 7 or more days before the flight", $"{AppData.RefundRate(DateTime.Today.AddDays(7)):P0} of the total"),
            new("Refunds", "Cancelled 1 to 6 days before the flight", $"{AppData.RefundRate(DateTime.Today.AddDays(1)):P0} of the total"),
            new("Refunds", "Cancelled on the day of the flight", $"{AppData.RefundRate(DateTime.Today):P0} of the total"),
            new("Taxes and discounts", "VAT", $"{AppData.TaxRate:P0} (not applied to Senior/PWD)"),
            new("Taxes and discounts", "Senior citizen / PWD discount", $"{AppData.DiscountRate:P0} (seniors are {AppData.SeniorMinimumAge}+)"),
            new("Taxes and discounts", "Advance purchase discount", $"{AppData.AdvancePurchaseRate:P0} when booked {AppData.AdvancePurchaseDays}+ days ahead"),
            new("Taxes and discounts", "Peak season surcharge", $"{AppData.PeakSeasonRate:P0}"),
            new("Timing", "Seat hold while booking", $"{AppData.SeatHoldMinutes} minutes"),
            new("Timing", "Bookings close", $"{AppData.BookingCutoffHours} hours before departure (the same day is fine until then)"),
            new("Timing", "Online check-in", "Opens as soon as the booking is made, closes at departure"),
            new("Limits", "Travellers per booking", $"{BookingRules.MaxPassengersPerBooking}"),
        };

        for (int i = 0; i < AppData.AddonNames.Length; i++)
        {
            if (AppData.AddonNames[i] == AppData.AddonTax) continue;
            rows.Add(new PolicyRow("Add-ons", AppData.AddonNames[i], $"PHP {AppData.AddonPrices[i]:N2}"));
        }

        foreach (string cabin in new[] { "Economy", "Premium Economy", "Business", "First Class" })
            rows.Add(new PolicyRow("Baggage", cabin, AppData.BaggageOptionsForClass(cabin)[0]));

        return rows;
    }

    /// <summary>Technical details a staff member can paste into a message. Contains no passwords or booking data.</summary>
    public static string SupportInfo(StaffSession session)
    {
        return string.Join(Environment.NewLine,
            $"SkyWings {VersionText()}",
            $"Role: {session.Role}",
            $"Windows: {RuntimeInformation.OSDescription}",
            $".NET: {RuntimeInformation.FrameworkDescription}",
            $"Data folder: {AppPaths.DataDirectory}",
            $"Log folder: {AppPaths.LogDirectory}");
    }

    public static string VersionText()
        => (Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly()).GetName().Version?.ToString(3) ?? "unknown";
}

/// <summary>Help for staff: how-to answers, the fare and policy rules, and details to quote when reporting a problem.</summary>
public sealed class HelpForm : GlassForm
{
    private readonly StaffSession _session;

    private readonly Dictionary<GlassButton, Panel> _sections = new();
    private readonly Panel _host = new() { Dock = DockStyle.Fill };

    private readonly TextBox _search = new() { PlaceholderText = "Search help" };
    private readonly ListBox _topics = new() { BorderStyle = BorderStyle.None, IntegralHeight = false };
    private readonly RichTextBox _answer = new() { ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Color.White, DetectUrls = false };

    private readonly IReadOnlyList<HelpTopic> _allTopics = HelpContent.Topics();

    public HelpForm(StaffSession session)
    {
        _session = session;
        Text = "Help · SkyWings";
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        SizeForScreen(900, 620, 720, 480);
        Padding = new Padding(D(24), D(12), D(24), D(16));

        var close = new GlassButton { Text = "Close", Style = GlassButtonStyle.Secondary, Height = D(38), DialogResult = DialogResult.Cancel };
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = D(54), FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, D(10), 0, 0) };
        footer.Controls.Add(close);

        var nav = new FlowLayoutPanel { Dock = DockStyle.Top, Height = D(54), WrapContents = false, Padding = new Padding(0, D(6), 0, 0) };
        AddSection(nav, "How-to", BuildFaqPage());
        AddSection(nav, "Fares & policies", BuildPolicyPage());
        AddSection(nav, "About", BuildAboutPage());

        Controls.Add(_host);
        Controls.Add(footer);
        Controls.Add(nav);

        Theme.Apply(this);
        CancelButton = close;

        _search.TextChanged += (_, _) => FilterTopics();
        _topics.SelectedIndexChanged += (_, _) => ShowTopic(_topics.SelectedItem as HelpTopic);
        FilterTopics();
        ShowSection(_sections.Keys.First());
    }

    private int D(int px) => Gfx.S(this, px);

    private void AddSection(FlowLayoutPanel nav, string title, Panel page)
    {
        var button = new GlassButton { Text = title, Style = GlassButtonStyle.Ghost, Height = D(38) };
        page.Visible = false;
        _sections[button] = page;
        _host.Controls.Add(page);
        button.Click += (_, _) => ShowSection(button);
        nav.Controls.Add(button);
    }

    private void ShowSection(GlassButton active)
    {
        foreach (var (button, page) in _sections)
        {
            page.Visible = button == active;
            button.Style = button == active ? GlassButtonStyle.Primary : GlassButtonStyle.Ghost;
        }
    }

    // ------------------------------------------------------------------ how-to

    private Panel BuildFaqPage()
    {
        _search.Dock = DockStyle.Top;
        _topics.Dock = DockStyle.Fill;
        _topics.Font = Theme.Body;
        _answer.Dock = DockStyle.Fill;
        _answer.Font = Theme.Body;

        var left = new Panel { Dock = DockStyle.Left, Width = D(260), Padding = new Padding(0, 0, D(12), 0) };
        left.Controls.Add(_topics);
        left.Controls.Add(_search);

        var card = new GlassCard { Dock = DockStyle.Fill, ContentPadding = 12 };
        card.Controls.Add(_answer);
        card.Controls.Add(left);
        return Wrap(card);
    }

    private void FilterTopics()
    {
        string term = _search.Text.Trim();
        var matches = _allTopics
            .Where(t => term.Length == 0
                || t.Title.Contains(term, StringComparison.CurrentCultureIgnoreCase)
                || t.Body.Contains(term, StringComparison.CurrentCultureIgnoreCase))
            .ToList();

        _topics.DisplayMember = nameof(HelpTopic.Title);
        _topics.DataSource = matches;
        if (matches.Count == 0) ShowTopic(null);
        else _topics.SelectedIndex = 0;
    }

    private void ShowTopic(HelpTopic? topic)
    {
        _answer.Clear();
        if (topic is null)
        {
            _answer.Text = "No help topics match your search.";
            return;
        }
        _answer.SelectionFont = Theme.Subtitle;
        _answer.AppendText(topic.Title + Environment.NewLine + Environment.NewLine);
        _answer.SelectionFont = Theme.Body;
        _answer.AppendText(topic.Body);
        _answer.Select(0, 0);
    }

    // ------------------------------------------------------------------ fares and policies

    private Panel BuildPolicyPage()
    {
        var grid = new DataGridView { Dock = DockStyle.Fill, AutoGenerateColumns = true };
        GridStyle.Apply(grid);
        grid.DataSource = new BindingList<PolicyRow>(HelpContent.PolicyRows().ToList());
        grid.SelectionChanged += (_, _) => grid.ClearSelection();

        var note = new Label
        {
            Text = "These values come straight from the booking rules, so they always match what the app charges and refunds.",
            Font = Theme.Small, ForeColor = Theme.TextSecondary, AutoSize = false, Height = D(30), Dock = DockStyle.Top
        };

        var card = new GlassCard { Dock = DockStyle.Fill, ContentPadding = 12 };
        card.Controls.Add(grid);
        card.Controls.Add(note);
        return Wrap(card);
    }

    // ------------------------------------------------------------------ about

    private Panel BuildAboutPage()
    {
        var info = new TextBox
        {
            Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Color.White, Font = Theme.Mono,
            Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, TabStop = false,
            Text = string.Join(Environment.NewLine + Environment.NewLine,
                $"SkyWings {HelpContent.VersionText()}",
                $"Signed in as: {_session.DisplayName} ({_session.Role})",
                HelpContent.SupportInfo(_session))
        };

        var logs = new GlassButton { Text = "Open logs folder", Style = GlassButtonStyle.Secondary, Height = D(38) };
        logs.Click += (_, _) => OpenFolder(AppPaths.LogDirectory);
        var data = new GlassButton { Text = "Open data folder", Style = GlassButtonStyle.Secondary, Height = D(38) };
        data.Click += (_, _) => OpenFolder(AppPaths.DataDirectory);
        var copy = new GlassButton { Text = "Copy support info", Height = D(38) };
        copy.Click += (_, _) => CopySupportInfo();

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = D(54), WrapContents = false, Padding = new Padding(0, D(6), 0, 0) };
        toolbar.Controls.Add(copy);
        toolbar.Controls.Add(logs);
        toolbar.Controls.Add(data);

        var hint = new Label
        {
            Text = "When you report a problem, copy the support info and mention what you were doing. It never contains passwords or booking details.",
            Font = Theme.Small, ForeColor = Theme.TextSecondary, AutoSize = false, Height = D(34), Dock = DockStyle.Top
        };

        var card = new GlassCard { Dock = DockStyle.Fill, ContentPadding = 12 };
        card.Controls.Add(info);
        card.Controls.Add(hint);
        card.Controls.Add(toolbar);
        return Wrap(card);
    }

    private void OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
        {
            MessageBox.Show(this, "That folder could not be opened.\n\n" + ex.Message, "Help",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void CopySupportInfo()
    {
        try
        {
            Clipboard.SetText(HelpContent.SupportInfo(_session));
            MessageBox.Show(this, "Support info copied. You can paste it into a message.", "Help",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (ExternalException)
        {
            // Another program has the clipboard locked.
            MessageBox.Show(this, "The clipboard is busy. Please try again.", "Help", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private static Panel Wrap(Control card)
    {
        var page = new Panel { Dock = DockStyle.Fill };
        page.Controls.Add(card);
        return page;
    }
}
