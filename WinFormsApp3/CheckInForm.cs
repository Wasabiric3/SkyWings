using SkyWings.Controllers;
using SkyWings.UI;

namespace SkyWings;

/// <summary>
/// Online check-in: find the booking, pick the flight, tick the travellers and check them in.
/// Check-in opens 24 hours before departure and closes at departure.
/// </summary>
public sealed class CheckInForm : GlassForm
{
    private readonly TextBox _reference = new() { CharacterCasing = CharacterCasing.Upper, MaxLength = 24 };
    private readonly FieldRow _referenceRow;
    private readonly GlassButton _find = new() { Text = "Find booking" };
    private readonly Panel _lookupPage = new() { Dock = DockStyle.Fill };
    private readonly Panel _checkInPage = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly Label _title = new();
    private readonly Label _banner = new();
    private readonly ComboBox _leg = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Panel _travellers = new() { Dock = DockStyle.Fill, AutoScroll = true };
    private readonly GlassButton _checkIn = new() { Text = "Check in selected" };
    private readonly GlassButton _passes = new() { Text = "Boarding passes", Style = GlassButtonStyle.Secondary };
    private readonly List<CheckBox> _boxes = new();
    private readonly CheckInController _controller;
    private bool _loading;

    public CheckInForm(BookingStore store)
    {
        _controller = new CheckInController(store);
        Text = "Online check-in · SkyWings";
        StartPosition = FormStartPosition.CenterParent;
        SizeForScreen(860, 640, 720, 520);
        int D(int px) => Gfx.S(this, px);
        Padding = new Padding(D(20), D(12), D(20), D(16));

        _referenceRow = new FieldRow("BOOKING REFERENCE", _reference);
        _reference.PlaceholderText = "e.g. SKY-1A2B3C4D";
        _reference.TextChanged += (_, _) => _referenceRow.ClearMessage();

        BuildLookupPage(D);
        BuildCheckInPage(D);

        var card = new GlassCard { Dock = DockStyle.Fill, ContentPadding = 18 };
        card.Controls.Add(_lookupPage);
        card.Controls.Add(_checkInPage);
        Controls.Add(card);

        Theme.Apply(this);
        _title.ForeColor = Theme.TextPrimary;
    }

    private void BuildLookupPage(Func<int, int> D)
    {
        var heading = new Label { Text = "Online check-in", Font = Theme.Title, AutoSize = false, Height = D(52), TextAlign = ContentAlignment.MiddleCenter };
        var hint = new Label
        {
            Text = $"Check-in opens {AppData.CheckInWindowHours} hours before departure. Enter your booking reference.",
            Font = Theme.Small, ForeColor = Theme.TextSecondary, AutoSize = false, Height = D(44), TextAlign = ContentAlignment.TopCenter
        };
        _find.Height = D(44);
        _find.Click += (_, _) => Find();
        var findHost = new Panel { Height = D(64), Padding = new Padding(0, D(12), 0, 0) };
        _find.Dock = DockStyle.None;
        _find.Width = D(170);
        findHost.Controls.Add(_find);
        findHost.Resize += (_, _) => _find.Left = (findHost.ClientSize.Width - _find.Width) / 2;   // centred under the field
        AcceptButton = _find;

        // The lookup is one field, so it sits in the middle of the card instead of the left.
        var column = new Panel();
        column.Height = Layouts.Stack(column, heading, hint, _referenceRow, findHost);
        Layouts.CenterColumn(_lookupPage, column, D(480), D(30));
    }

    private void BuildCheckInPage(Func<int, int> D)
    {
        _title.Font = Theme.Header;
        _title.AutoSize = false;
        _title.Height = D(42);
        _title.Dock = DockStyle.Top;
        _title.TextAlign = ContentAlignment.MiddleLeft;

        _banner.Font = new Font(Theme.Small, FontStyle.Bold);
        _banner.AutoSize = false;
        _banner.Height = D(28);
        _banner.Dock = DockStyle.Top;

        var legRow = new FieldRow("FLIGHT", _leg) { Dock = DockStyle.Top };
        _leg.SelectedIndexChanged += (_, _) => { if (!_loading) RebuildTravellers(); };

        _checkIn.Click += (_, _) => CheckInSelected();
        _passes.Click += (_, _) => ShowPasses(null);
        var another = new GlassButton { Text = "Look up another", Style = GlassButtonStyle.Ghost };
        another.Click += (_, _) => ShowLookup();
        var close = new GlassButton { Text = "Close", Style = GlassButtonStyle.Secondary, DialogResult = DialogResult.Cancel };
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = D(56), WrapContents = false, Padding = new Padding(0, D(10), 0, 0) };
        footer.Controls.Add(_checkIn);
        footer.Controls.Add(_passes);
        footer.Controls.Add(another);
        footer.Controls.Add(close);
        CancelButton = close;

        _checkInPage.Controls.Add(_travellers);
        _checkInPage.Controls.Add(footer);
        _checkInPage.Controls.Add(legRow);
        _checkInPage.Controls.Add(_banner);
        _checkInPage.Controls.Add(_title);
    }

    // ------------------------------------------------------------------ lookup

    private void Find()
    {
        LookupResult result = _controller.Find(_reference.Text);
        if (result.NormalizedReference is not null) _reference.Text = result.NormalizedReference;
        if (!result.Found) { _referenceRow.SetMessage(result.Error!); return; }

        _loading = true;
        _leg.Items.Clear();
        foreach (string label in _controller.LegLabels()) _leg.Items.Add(label);
        _leg.SelectedIndex = 0;
        _loading = false;

        _title.Text = $"Check-in · {result.Booking!.Reference}";
        _lookupPage.Visible = false;
        _checkInPage.Visible = true;
        AcceptButton = null;
        RebuildTravellers();
    }

    private void ShowLookup()
    {
        _controller.Clear();
        _checkInPage.Visible = false;
        _lookupPage.Visible = true;
        AcceptButton = _find;
        _reference.Focus();
    }

    // ------------------------------------------------------------------ check-in

    private bool ReturnLeg => _leg.SelectedIndex == 1;

    private void RebuildTravellers()
    {
        if (_controller.Current is null) return;
        int D(int px) => Gfx.S(this, px);
        CheckInView view;
        try
        {
            view = _controller.Load(ReturnLeg, DateTime.Now);
        }
        catch (Exception ex)
        {
            AppLogger.Error("CheckIn", ex);
            MessageBox.Show(this, "Check-in could not be loaded. Please try again.\n\n" + ex.Message, "Check-in",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        bool open = view.Open;

        _banner.ForeColor = view.Banner switch
        {
            CheckInBannerKind.NotYetOpen => Theme.TextSecondary,
            CheckInBannerKind.Open => Theme.Accent,
            _ => Theme.DangerRed
        };
        _banner.Text = view.BannerText;

        _travellers.Controls.Clear();
        _boxes.Clear();
        var rows = new List<Control>();
        bool anyCheckedIn = view.AnyCheckedIn;
        foreach (TravellerCheckIn traveller in view.Travellers)
        {
            if (traveller.Record is { } record)
            {
                rows.Add(new Label
                {
                    Text = $"✓  {traveller.Name}   ·   seat {record.SeatCode}   ·   checked in {record.CheckedInAt:MMM d, h:mm tt}",
                    Font = Theme.Body, ForeColor = Theme.Accent, AutoSize = false, Height = D(38), TextAlign = ContentAlignment.MiddleLeft
                });
            }
            else
            {
                var box = new CheckBox
                {
                    Text = $"{traveller.Name}   ·   seat {(traveller.HasSeat ? traveller.Seat : "not assigned")}",
                    Tag = traveller.Name, AutoSize = false, Height = D(38), Font = Theme.Body,
                    Enabled = open && traveller.HasSeat, Checked = open && traveller.HasSeat
                };
                box.CheckedChanged += (_, _) => UpdateButtons(open);
                _boxes.Add(box);
                rows.Add(box);
            }
        }
        int total = Layouts.Stack(_travellers, rows.ToArray());
        _travellers.AutoScrollMinSize = new Size(0, total);
        Theme.Apply(_travellers);
        foreach (var label in rows.OfType<Label>()) { label.ForeColor = Theme.Accent; label.Font = Theme.Body; }

        _passes.Enabled = anyCheckedIn;
        UpdateButtons(open);
    }

    private void UpdateButtons(bool open) => _checkIn.Enabled = open && _boxes.Any(b => b.Checked);

    private async void CheckInSelected()
    {
        if (_controller.Current is null) return;
        bool returnLeg = ReturnLeg;
        CheckInSummary summary = _controller.CheckIn(
            _boxes.Where(b => b.Checked).Select(b => (string)b.Tag!).ToList(), returnLeg, DateTime.Now);

        RebuildTravellers();
        if (summary.Problems.Count > 0)
            MessageBox.Show(this, string.Join(Environment.NewLine, summary.Problems), "Check-in", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        if (summary.Done.Count == 0) return;

        // The check-in is saved. Send the email in the background so the passes appear at once; a failure only shows a note.
        Task<SkyWings.Email.EmailSendResult?> sending = SendPassEmailAsync(summary.Done.ToList(), returnLeg);
        ShowPasses(summary.Done.ToList());
        SkyWings.Email.EmailSendResult? sent = await sending;
        if (IsDisposed || sent is null) return;
        MessageBox.Show(this,
            sent.Delivered
                ? (sent.Channel == "SMTP" ? "The boarding pass was emailed to the address on the booking." : "The boarding pass email was saved as a file:\n" + sent.Detail)
                : "No boarding pass email was sent. " + sent.Detail,
            "Email boarding pass", MessageBoxButtons.OK, sent.Delivered ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    private async Task<SkyWings.Email.EmailSendResult?> SendPassEmailAsync(IReadOnlyCollection<string> names, bool returnLeg)
    {
        try { return await _controller.SendBoardingPassAsync(names, returnLeg); }
        catch (Exception ex) { AppLogger.Error("CheckIn", ex); return null; }
    }

    /// <summary>Shows boarding passes for the checked-in travellers on this leg (all, or only <paramref name="only"/>).</summary>
    private void ShowPasses(IReadOnlyCollection<string>? only)
    {
        if (_controller.Current is null) return;
        var records = _controller.PassRecords(ReturnLeg, only);
        if (records.Count == 0) return;
        using var form = new BoardingPassForm(_controller.Current, ReturnLeg, records);
        form.ShowDialog(this);
    }
}
