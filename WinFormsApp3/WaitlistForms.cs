using SkyWings.UI;

namespace SkyWings;

/// <summary>Adds a traveller to the waitlist of a sold-out cabin. The offer arrives by email when seats free up.</summary>
public sealed class JoinWaitlistForm : GlassForm
{
    private readonly BookingStore _store;
    private readonly Flight _flight;
    private readonly DateTime _date;
    private readonly string _cabin;
    private readonly TextBox _name = new() { MaxLength = 120 };
    private readonly TextBox _email = new() { MaxLength = 200 };
    private readonly NumericUpDown _party = new() { Minimum = 1, Maximum = 9, Value = 1 };
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
        ClientSize = new Size(D(560), D(470));

        _nameRow = new FieldRow("TRAVELLER NAME", _name);
        _emailRow = new FieldRow("EMAIL  (THE OFFER IS SENT HERE)", _email);
        _partyRow = new FieldRow("SEATS NEEDED", _party);
        foreach (var row in new[] { _nameRow, _emailRow })
            if (row.Input is TextBox box) { var r = row; box.TextChanged += (_, _) => r.ClearMessage(); }

        var title = new Label
        {
            Text = $"{flight.FlightNo} · {date:ddd, d MMM yyyy} · {cabin} is sold out", Font = Theme.Header,
            AutoSize = false, Height = D(44), TextAlign = ContentAlignment.MiddleLeft
        };
        var note = new Label
        {
            Text = $"Join the waitlist and we will email you the moment seats free up. You then have {AppData.WaitlistOfferHours} hours to book them.",
            AutoSize = false, Height = D(48), ForeColor = Theme.TextSecondary
        };
        var save = new GlassButton { Text = "Join waitlist" };
        save.Click += (_, _) => Join();
        var cancel = new GlassButton { Text = "Cancel", Style = GlassButtonStyle.Secondary, DialogResult = DialogResult.Cancel };
        var buttons = new FlowLayoutPanel { Height = D(56), FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, D(10), 0, 0) };
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);

        var card = new GlassCard { Dock = DockStyle.Fill, ContentPadding = 14 };
        Layouts.Stack(card, title, note, _nameRow, _emailRow, _partyRow, buttons);
        Controls.Add(card);
        Padding = new Padding(D(10));
        Theme.Apply(this);
        AcceptButton = save;
        CancelButton = cancel;
    }

    private void Join()
    {
        if (_name.Text.Trim().Length < 2) { _nameRow.SetMessage("Enter the traveller's name."); return; }
        if (!BookingRules.IsValidEmail(_email.Text)) { _emailRow.SetMessage("Enter a valid email address."); return; }
        try
        {
            WaitlistEntry? entry = _store.JoinWaitlist(_flight.FlightNo, _date, _cabin, _name.Text, _email.Text, (int)_party.Value);
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

/// <summary>Looks up a waitlist entry by code and email. When seats are offered, "Book now" hands the entry back to start the booking.</summary>
public sealed class WaitlistStatusForm : GlassForm
{
    private readonly BookingStore _store;
    private readonly TextBox _code = new() { MaxLength = 20, CharacterCasing = CharacterCasing.Upper };
    private readonly TextBox _email = new() { MaxLength = 200 };
    private readonly FieldRow _codeRow, _emailRow;
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
        ClientSize = new Size(D(560), D(520));
        _result.Height = D(90);

        _codeRow = new FieldRow("WAITLIST CODE", _code);
        _emailRow = new FieldRow("EMAIL USED TO JOIN", _email);
        var title = new Label { Text = "Check your waitlist spot", Font = Theme.Header, AutoSize = false, Height = D(44), TextAlign = ContentAlignment.MiddleLeft };
        var find = new GlassButton { Text = "Look up" };
        find.Click += (_, _) => Lookup();
        _book.Click += (_, _) => { OfferToBook = _entry; DialogResult = DialogResult.OK; Close(); };
        _leave.Click += (_, _) => Leave();
        var close = new GlassButton { Text = "Close", Style = GlassButtonStyle.Secondary, DialogResult = DialogResult.Cancel };
        var buttons = new FlowLayoutPanel { Height = D(56), FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, D(10), 0, 0) };
        buttons.Controls.Add(close);
        buttons.Controls.Add(_book);
        buttons.Controls.Add(_leave);
        buttons.Controls.Add(find);

        var card = new GlassCard { Dock = DockStyle.Fill, ContentPadding = 14 };
        Layouts.Stack(card, title, _codeRow, _emailRow, _result, buttons);
        Controls.Add(card);
        Padding = new Padding(D(10));
        Theme.Apply(this);
        AcceptButton = find;
        CancelButton = close;
    }

    private void Lookup()
    {
        _entry = null;
        _book.Visible = _leave.Visible = false;
        try { _entry = _store.FindWaitlistEntry(_code.Text, _email.Text); }
        catch (IOException ex) { AppLogger.Error("Waitlist", ex); _result.Text = "The waitlist could not be read: " + ex.Message; return; }

        // One message for a wrong code and a wrong email, so codes cannot be guessed.
        if (_entry is null) { _result.Text = "No waitlist entry matches that code and email."; return; }

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
