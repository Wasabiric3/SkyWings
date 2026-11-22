using System.Globalization;
using System.Runtime.InteropServices;
using SkyWings.UI;

namespace SkyWings;

/// <summary>
/// Staff view of one waitlist entry: who it is, where they stand and the whole queue for that cabin. The code can be copied
/// with one click, to paste into the Waitlist lookup on the home screen.
/// </summary>
public sealed class WaitlistEntryForm : GlassForm
{
    private readonly WaitlistEntry _entry;
    private readonly GlassButton _copy = new() { Text = "Copy code" };

    /// <param name="entry">The entry to show.</param>
    /// <param name="cabinQueue">Every live entry of the same flight, date and cabin, in queue order (including <paramref name="entry"/>).</param>
    public WaitlistEntryForm(WaitlistEntry entry, IReadOnlyList<WaitlistEntry> cabinQueue)
    {
        _entry = entry;
        Text = "Waitlist entry · SkyWings";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.None;
        int D(int px) => Gfx.S(this, px);
        var code = new TextBox { Text = entry.BookingReference, ReadOnly = true };
        var codeRow = new FieldRow("WAITLIST CODE  (SELECT IT, OR USE COPY CODE)", code);

        string status = entry.Status == BookingStore.WaitlistWaiting
            ? $"Waiting · number {entry.Position} in line"
            : entry.OfferExpiresAt is { } until
                ? "Offered · seats are held until " + until.ToString("ddd, d MMM h:mm tt", CultureInfo.InvariantCulture)
                : "Offered";
        var details = new Label
        {
            Text = $"Traveller:  {entry.PassengerName}\nEmail:  {entry.Email}\nSeats wanted:  {entry.SeatsWanted}\n" +
                   $"Joined:  {entry.AddedAt.ToString("ddd, d MMM h:mm tt", CultureInfo.InvariantCulture)}\nStatus:  {status}",
            AutoSize = false, Height = D(120), ForeColor = Theme.TextPrimary
        };

        var queueTitle = new Label
        {
            Text = "QUEUE FOR THIS CABIN  (▶ = THIS ENTRY)", Font = new Font(Theme.Small, FontStyle.Bold),
            ForeColor = Theme.TextSecondary, AutoSize = false, Height = D(30), TextAlign = ContentAlignment.BottomLeft
        };
        var queue = new TextBox
        {
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BorderStyle = BorderStyle.None,
            BackColor = Theme.BgCard, Font = Theme.Small,
            Text = string.Join(Environment.NewLine, cabinQueue.Select(e =>
                $"{(e.BookingReference == entry.BookingReference ? "▶" : "  ")} " +
                $"{(e.Status == BookingStore.WaitlistWaiting ? "#" + e.Position.ToString(CultureInfo.InvariantCulture) : "Offered"),-8}" +
                $"{e.PassengerName}  ×{e.SeatsWanted}   joined {e.AddedAt.ToString("h:mm tt", CultureInfo.InvariantCulture)}"))
        };

        var queueHost = new Panel { Height = D(190), Padding = new Padding(D(1)), BackColor = Theme.Border };   // a thin outline like the other fields
        queueHost.Controls.Add(queue);

        _copy.Click += (_, _) => CopyCode();
        var close = new GlassButton { Text = "Close", Style = GlassButtonStyle.Secondary };
        DialogFrame.Create(this, 620, $"{entry.FlightNo} · {entry.FlightDate:ddd, d MMM yyyy} · {entry.ClassName}", "",
            new Control[] { codeRow, details, queueTitle, queueHost }, _copy, close);
        Theme.Apply(this);
    }

    private void CopyCode()
    {
        try
        {
            Clipboard.SetText(_entry.BookingReference);
            _copy.Text = "Copied ✓";
        }
        catch (ExternalException ex)   // another program is holding the clipboard
        {
            AppLogger.Error("Waitlist", ex);
            MessageBox.Show(this, "The clipboard is busy. Try again, or select the code and press Ctrl+C.", "Copy code",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}

/// <summary>Adds a traveller to the waitlist of a sold-out cabin. The offer arrives by email when seats free up.</summary>
public sealed class JoinWaitlistForm : GlassForm
{
    private readonly BookingStore _store;
    private readonly Flight _flight;
    private readonly DateTime _date;
    private readonly string _cabin;
    private readonly TextBox _name = new() { MaxLength = 120 };
    private readonly TextBox _email = new() { MaxLength = 200 };
    private readonly ComboBox _party = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly FieldRow _nameRow, _emailRow, _partyRow;

    public JoinWaitlistForm(BookingStore store, Flight flight, DateTime date, string cabin)
    {
        _store = store; _flight = flight; _date = date; _cabin = cabin;
        Text = "Join waitlist · SkyWings";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.None;
        int D(int px) => Gfx.S(this, px);
        _party.Items.AddRange(Enumerable.Range(1, 9).Select(n => (object)n.ToString()).ToArray());
        _party.SelectedIndex = 0;

        _nameRow = new FieldRow("TRAVELLER NAME", _name);
        _emailRow = new FieldRow("EMAIL  (THE OFFER IS SENT HERE)", _email);
        _partyRow = new FieldRow("SEATS NEEDED", _party);
        foreach (var row in new[] { _nameRow, _emailRow })
            if (row.Input is TextBox box) { var r = row; box.TextChanged += (_, _) => r.ClearMessage(); }

        var save = new GlassButton { Text = "Join waitlist" };
        save.Click += (_, _) => Join();
        var cancel = new GlassButton { Text = "Cancel", Style = GlassButtonStyle.Secondary };
        DialogFrame.Create(this, 560, $"{flight.FlightNo} · {date:ddd, d MMM yyyy} · {cabin} is sold out",
            $"Join the waitlist and we will email you the moment seats free up. You then have up to {AppData.WaitlistOfferHours} hours to book them (less if the flight is close to departure; bookings close {AppData.BookingCutoffHours} hours before it leaves).",
            new Control[] { _nameRow, _emailRow, _partyRow }, save, cancel);
        Theme.Apply(this);
    }

    private void Join()
    {
        if (_name.Text.Trim().Length < 2) { _nameRow.SetMessage("Enter the traveller's name."); return; }
        if (!BookingRules.IsValidEmail(_email.Text)) { _emailRow.SetMessage("Enter a valid email address."); return; }
        try
        {
            WaitlistEntry? entry = _store.JoinWaitlist(_flight.FlightNo, _date, _cabin, _name.Text, _email.Text, _party.SelectedIndex + 1);
            if (entry is null)
            {
                MessageBox.Show(this,
                    "Seats are available for this party, or this email is already on the waitlist for this flight. Refresh the board to check.",
                    "Join waitlist", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            MessageBox.Show(this,
                $"You are number {entry.Position} on the waitlist.\n\nWaitlist code: {entry.BookingReference}\n\n" +
                "Keep this code. We will email you if seats become available.",
                "On the waitlist", MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (IOException ex)
        {
            AppLogger.Error("Waitlist", ex);
            MessageBox.Show(this, "The waitlist could not be updated: " + ex.Message, "Join waitlist", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}

/// <summary>Looks up a waitlist entry by its code. When seats are offered, "Book now" hands the entry back to start the booking.</summary>
public sealed class WaitlistStatusForm : GlassForm
{
    private readonly BookingStore _store;
    private readonly TextBox _code = new() { MaxLength = 20, CharacterCasing = CharacterCasing.Upper };
    private readonly FieldRow _codeRow;
    private readonly Label _result = new() { AutoSize = false, Height = 90 };
    private readonly GlassButton _book = new() { Text = "Book now  →", Visible = false };
    private readonly GlassButton _leave = new() { Text = "Leave waitlist", Style = GlassButtonStyle.Secondary, Visible = false };
    private WaitlistEntry? _entry;

    /// <summary>Set when the traveller chose "Book now" on a live offer.</summary>
    public WaitlistEntry? OfferToBook { get; private set; }

    public WaitlistStatusForm(BookingStore store)
    {
        _store = store;
        Text = "Waitlist · SkyWings";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false; ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.None;
        int D(int px) => Gfx.S(this, px);
        _result.Height = D(90);

        _codeRow = new FieldRow("WAITLIST CODE", _code);
        var find = new GlassButton { Text = "Look up" };
        find.Click += (_, _) => Lookup();
        _book.Click += (_, _) => { OfferToBook = _entry; DialogResult = DialogResult.OK; Close(); };
        _leave.Click += (_, _) => Leave();
        var close = new GlassButton { Text = "Close", Style = GlassButtonStyle.Secondary };
        DialogFrame frame = DialogFrame.Create(this, 720, "Check your waitlist spot",
            "Enter the waitlist code you were given when you joined.", new Control[] { _codeRow, _result }, find, close);
        frame.Buttons.Controls.Add(_book);    // shown only while there is a live offer
        frame.Buttons.Controls.Add(_leave);
        Theme.Apply(this);
    }

    private void Lookup()
    {
        _entry = null;
        _book.Visible = _leave.Visible = false;
        try { _entry = _store.FindWaitlistEntry(_code.Text); }
        catch (IOException ex) { AppLogger.Error("Waitlist", ex); _result.Text = "The waitlist could not be read: " + ex.Message; return; }

        if (_entry is null) { _result.Text = "No waitlist entry matches that code."; return; }

        string route = $"{_entry.FlightNo} · {_entry.FlightDate:ddd, d MMM yyyy} · {_entry.ClassName} · {_entry.SeatsWanted} seat(s)";
        switch (_entry.Status)
        {
            case BookingStore.WaitlistWaiting:
                _result.Text = $"{route}\nYou are number {_entry.Position} in line. We will email you when seats are available.";
                _leave.Visible = true;
                break;
            case BookingStore.WaitlistOffered when _entry.OfferExpiresAt > DateTime.Now:
                _result.Text = $"{route}\nSeats are held for you until {_entry.OfferExpiresAt:ddd, d MMM h:mm tt}. Book before then.";
                _book.Visible = _leave.Visible = true;
                break;
            case BookingStore.WaitlistOffered:
            case BookingStore.WaitlistExpired:
                _result.Text = $"{route}\nThe offer has expired and the seats went to the next traveller.";
                break;
            case BookingStore.WaitlistConverted:
                _result.Text = $"{route}\nThis offer was already used to book.";
                break;
            default:
                _result.Text = $"{route}\nThis waitlist entry was removed.";
                break;
        }
    }

    private void Leave()
    {
        if (_entry is null) return;
        if (MessageBox.Show(this, "Leave the waitlist? Your place in line is lost.", "Waitlist", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        try { _store.CancelWaitlistEntry(_entry.BookingReference); }
        catch (IOException ex) { AppLogger.Error("Waitlist", ex); }
        Lookup();
    }
}
