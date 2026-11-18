using SkyWings.Controllers;
using SkyWings.UI;

namespace SkyWings;

/// <summary>
/// Customer self-service: find a booking by reference and last name, view it, change seats or cancel with a
/// refund preview.
/// </summary>
public sealed class ManageBookingForm : GlassForm
{
    private readonly BookingStore _store;
    private readonly TextBox _reference = new() { CharacterCasing = CharacterCasing.Upper, MaxLength = 24 };
    private readonly TextBox _lastName = new() { MaxLength = BookingRules.MaxNameLength };
    private readonly FieldRow _referenceRow, _lastNameRow;
    private readonly Panel _lookupPage = new() { Dock = DockStyle.Fill };
    private readonly Panel _detailsPage = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly Label _title = new();
    private readonly Label _banner = new();
    private readonly Label _policy = new();
    private readonly ReceiptPanel _receipt = new() { Dock = DockStyle.Top };
    private readonly Panel _receiptHost = new() { Dock = DockStyle.Fill, AutoScroll = true };
    private readonly GlassButton _find = new() { Text = "Find booking" };
    private readonly GlassButton _changeSeats = new() { Text = "Change seats", Style = GlassButtonStyle.Secondary };
    private readonly GlassButton _cancel = new() { Text = "Cancel booking", Style = GlassButtonStyle.Danger };
    private readonly ManageBookingController _controller;
    private readonly Label _emailStatus = new() { AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Font = Theme.Small };
    private readonly GlassButton _toggleEmail = new() { Text = "Stop emails", Style = GlassButtonStyle.Secondary };
    private readonly GlassButton _sendEmail = new() { Text = "Email me this receipt", Style = GlassButtonStyle.Secondary };
    private readonly FlowLayoutPanel _emailBar = new() { WrapContents = false };

    public ManageBookingForm(BookingStore store)
    {
        _store = store;
        _controller = new ManageBookingController(store);
        Text = "Manage booking · SkyWings";
        StartPosition = FormStartPosition.CenterParent;
        SizeForScreen(860, 640, 720, 520);
        int D(int px) => Gfx.S(this, px);
        Padding = new Padding(D(20), D(12), D(20), D(16));

        _referenceRow = new FieldRow("BOOKING REFERENCE", _reference);
        _lastNameRow = new FieldRow("LAST NAME OF A TRAVELLER", _lastName);
        _reference.PlaceholderText = "e.g. SKY-1A2B3C4D";
        _reference.TextChanged += (_, _) => _referenceRow.ClearMessage();
        _lastName.TextChanged += (_, _) => _lastNameRow.ClearMessage();

        BuildLookupPage(D);
        BuildDetailsPage(D);

        var card = new GlassCard { Dock = DockStyle.Fill, ContentPadding = 18 };
        card.Controls.Add(_lookupPage);
        card.Controls.Add(_detailsPage);
        Controls.Add(card);

        Theme.Apply(this);
        StyleLabels();
    }

    private void BuildLookupPage(Func<int, int> D)
    {
        var heading = new Label { Text = "Find your booking", Font = Theme.Title, AutoSize = false, Height = D(52), TextAlign = ContentAlignment.MiddleLeft };
        var hint = new Label
        {
            Text = "Enter the reference from your receipt and the last name of any traveller on the booking.",
            Font = Theme.Small, ForeColor = Theme.TextSecondary, AutoSize = false, Height = D(34)
        };
        _find.Height = D(44);
        _find.Click += (_, _) => Find();
        var findHost = new Panel { Height = D(64), Padding = new Padding(0, D(12), 0, 0) };
        _find.Dock = DockStyle.Left;
        _find.Width = D(170);
        findHost.Controls.Add(_find);
        AcceptButton = _find;

        Layouts.Stack(_lookupPage, heading, hint, _referenceRow, _lastNameRow, findHost);
        _lookupPage.Padding = new Padding(0, 0, D(160), 0);   // keep the form narrower than the card
    }

    private void BuildDetailsPage(Func<int, int> D)
    {
        _title.Font = Theme.Header;
        _title.AutoSize = false;
        _title.Height = D(42);
        _title.Dock = DockStyle.Top;
        _title.TextAlign = ContentAlignment.MiddleLeft;

        _banner.Font = new Font(Theme.Small, FontStyle.Bold);
        _banner.AutoSize = false;
        _banner.Height = D(26);
        _banner.Dock = DockStyle.Top;

        _policy.Font = Theme.Small;
        _policy.ForeColor = Theme.TextSecondary;
        _policy.AutoSize = false;
        _policy.Height = D(48);
        _policy.Dock = DockStyle.Bottom;

        _changeSeats.Click += (_, _) => ChangeSeats();
        _cancel.Click += (_, _) => CancelBooking();
        var another = new GlassButton { Text = "Look up another", Style = GlassButtonStyle.Ghost };
        another.Click += (_, _) => ShowLookup();
        var close = new GlassButton { Text = "Close", Style = GlassButtonStyle.Secondary, DialogResult = DialogResult.Cancel };
        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = D(56), WrapContents = false, Padding = new Padding(0, D(10), 0, 0)
        };
        footer.Controls.Add(_changeSeats);
        footer.Controls.Add(_cancel);
        footer.Controls.Add(another);
        footer.Controls.Add(close);
        CancelButton = close;

        _receiptHost.Controls.Add(_receipt);
        _detailsPage.Controls.Add(_receiptHost);
        _emailBar.Dock = DockStyle.Bottom;
        _emailBar.Height = D(50);
        _emailBar.Padding = new Padding(0, D(6), 0, 0);
        _emailStatus.Width = D(300);
        _emailStatus.Height = D(40);
        _toggleEmail.Click += (_, _) => ToggleEmail();
        _sendEmail.Click += async (_, _) => await SendReceipt();
        _emailBar.Controls.Add(_emailStatus);
        _emailBar.Controls.Add(_toggleEmail);
        _emailBar.Controls.Add(_sendEmail);

        _detailsPage.Controls.Add(_policy);
        _detailsPage.Controls.Add(_emailBar);
        _detailsPage.Controls.Add(footer);
        _detailsPage.Controls.Add(_banner);
        _detailsPage.Controls.Add(_title);
    }

    private void StyleLabels()
    {
        _title.ForeColor = Theme.TextPrimary;
        _policy.ForeColor = Theme.TextSecondary;
    }

    // ------------------------------------------------------------------ lookup

    private void Find()
    {
        LookupResult result = _controller.Find(_reference.Text, _lastName.Text);
        if (result.NormalizedReference is not null) _reference.Text = result.NormalizedReference;
        if (!result.Found)
        {
            _lastNameRow.SetMessage(result.Error!);
            return;
        }
        ShowBooking(result.Booking!);
    }

    private void ShowLookup()
    {
        _controller.Clear();
        _detailsPage.Visible = false;
        _lookupPage.Visible = true;
        AcceptButton = _find;
        _reference.Focus();
    }

    // ------------------------------------------------------------------ booking view

    private void ShowBooking(BookingRecord booking)
    {
        int D(int px) => Gfx.S(this, px);
        var rows = BookingSummary.Rows(booking);
        _receipt.Height = D(104) + ((rows.Count + 1) / 2) * D(58);
        _receipt.SetReceipt(booking.Reference, rows);
        _receiptHost.AutoScrollMinSize = new Size(0, _receipt.Height + D(8));

        _title.Text = $"Booking {booking.Reference}";
        BookingBanner banner = ManageBookingController.DescribeBooking(booking);
        Color bannerColor = banner.Kind switch
        {
            BannerKind.Canceled => Theme.DangerRed,
            BannerKind.Departed => Theme.TextSecondary,
            _ => Theme.Accent
        };
        _banner.ForeColor = bannerColor;
        _banner.BackColor = Color.FromArgb(28, bannerColor);   // tinted strip so the state reads at a glance
        _banner.Padding = new Padding(D(10), 0, 0, 0);
        _banner.TextAlign = ContentAlignment.MiddleLeft;
        _banner.Height = D(34);
        _banner.Text = banner.Text;
        _changeSeats.Enabled = banner.CanModify;
        _cancel.Enabled = banner.CanModify;
        _policy.Text = banner.PolicyText;

        RefreshEmailBar();
        _lookupPage.Visible = false;
        _detailsPage.Visible = true;
        AcceptButton = null;
    }

    // ------------------------------------------------------------------ email preferences

    private void RefreshEmailBar()
    {
        if (_controller.Current is null) return;
        EmailBarState state = _controller.RefreshEmail();
        _toggleEmail.Visible = state.HasEmail;
        _sendEmail.Visible = state.HasEmail;
        _emailStatus.Text = state.StatusText;
        if (!state.HasEmail) return;
        _toggleEmail.Text = state.ToggleText;
        _sendEmail.Enabled = state.Subscribed;
    }

    private void ToggleEmail()
    {
        if (!_controller.ToggleEmail())
            MessageBox.Show(this, "The email setting could not be saved. Please try again.", "Manage booking",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        RefreshEmailBar();
    }

    private async Task SendReceipt()
    {
        if (_controller.Current is null || string.IsNullOrEmpty(_controller.ContactEmail)) return;
        _sendEmail.Enabled = false;
        SkyWings.Email.EmailSendResult? sent = await _controller.SendReceiptAsync();
        if (IsDisposed || sent is null) return;
        var result = sent;
        _sendEmail.Enabled = true;
        MessageBox.Show(this,
            result.Delivered
                ? (result.Channel == "SMTP" ? "The receipt was emailed." : "The receipt was saved as a file:\n" + result.Detail)
                : "No email was sent. " + result.Detail,
            "Email receipt", MessageBoxButtons.OK, result.Delivered ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
    }

    private void Reload()
    {
        if (_controller.Current is null) return;
        BookingRecord? fresh = _controller.Reload();
        if (fresh is null) { ShowLookup(); return; }
        ShowBooking(fresh);
    }

    private void ChangeSeats()
    {
        if (_controller.Current is null) return;
        using var form = new SeatChangeForm(_store, _controller.Current);
        form.ShowDialog(this);
        if (form.Changed) Reload();
    }

    private void CancelBooking()
    {
        CancelPreview? preview = _controller.PreviewCancel();
        if (preview is null) return;
        if (MessageBox.Show(this, preview.ConfirmationText, "Cancel booking", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;

        CancelResult result = _controller.Cancel();
        if (result.Outcome == CancelOutcome.StoreError)
        {
            MessageBox.Show(this, "The booking could not be canceled. Please try again.\n\n" + result.Error!.Message, "Manage booking",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        if (result.Outcome == CancelOutcome.AlreadyChanged)
            MessageBox.Show(this, "This booking was already canceled or changed. Showing its latest details.", "Manage booking",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        Reload();
    }
}
