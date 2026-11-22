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
    private readonly FieldRow _referenceRow;
    private readonly Panel _lookupPage = new() { Dock = DockStyle.Fill };
    private readonly Panel _detailsPage = new() { Dock = DockStyle.Fill, Visible = false };
    private const string LookupHint = "Enter the booking reference from your receipt.";
    private readonly PageHeader _header = new() { Title = "Manage booking", Hint = LookupHint };
    private readonly Label _title = new();
    private readonly Label _banner = new();
    private readonly Label _policy = new();
    private readonly ReceiptPanel _receipt = new() { Dock = DockStyle.Top };
    private readonly Panel _receiptHost = new() { Dock = DockStyle.Fill, AutoScroll = true };
    private readonly GlassButton _find = new() { Text = "Find booking" };
    private readonly GlassButton _logRequest = new() { Text = "Log refund request", Style = GlassButtonStyle.Secondary };
    private readonly GlassButton _changeFlight = new() { Text = "Change flight", Style = GlassButtonStyle.Secondary };
    private readonly GlassButton _changeSeats = new() { Text = "Change seats", Style = GlassButtonStyle.Secondary };
    private readonly GlassButton _cancel = new() { Text = "Cancel booking", Style = GlassButtonStyle.Danger };
    private readonly ManageBookingController _controller;
    private readonly Label _emailStatus = new() { AutoSize = false, TextAlign = ContentAlignment.MiddleLeft, Font = Theme.Small };
    private readonly GlassButton _sendEmail = new() { Text = "Send this receipt", Style = GlassButtonStyle.Secondary };
    private readonly GlassButton _print = new() { Text = "Print receipt", Style = GlassButtonStyle.Secondary };
    private readonly FlowLayoutPanel _emailBar = new() { WrapContents = false };

    /// <param name="operatorName">Who is at the desk (the signed-in staff member); recorded when a refund request is logged.</param>
    public ManageBookingForm(BookingStore store, string operatorName = "desk")
    {
        _store = store;
        _controller = new ManageBookingController(store, operatorName);
        Text = "Manage booking · SkyWings";
        StartPosition = FormStartPosition.CenterParent;
        SizeForScreen(860, 640, 720, 520);
        int D(int px) => Gfx.S(this, px);
        Padding = new Padding(D(20), D(12), D(20), D(16));

        _referenceRow = new FieldRow("BOOKING REFERENCE", _reference);
        _reference.PlaceholderText = "e.g. SKY-1A2B3C4D";
        _reference.TextChanged += (_, _) => _referenceRow.ClearMessage();

        BuildLookupPage(D);
        BuildDetailsPage(D);

        var card = new GlassCard { Dock = DockStyle.Fill, ContentPadding = 18 };
        card.Controls.Add(_lookupPage);
        card.Controls.Add(_detailsPage);
        Controls.Add(card);
        Controls.Add(_header);   // added last, so it docks above the card

        Theme.Apply(this);
        StyleLabels();
    }

    private void BuildLookupPage(Func<int, int> D)
    {
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
        column.Height = Layouts.Stack(column, _referenceRow, findHost);
        Layouts.CenterColumn(_lookupPage, column, D(480), D(30));
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

        _changeFlight.Click += (_, _) => ChangeFlight();
        _logRequest.Click += (_, _) => LogRefundRequest();
        _changeSeats.Click += (_, _) => ChangeSeats();
        _cancel.Click += (_, _) => CancelBooking();
        var another = new GlassButton { Text = "Look up another", Style = GlassButtonStyle.Ghost };
        another.Click += (_, _) => ShowLookup();
        var close = new GlassButton { Text = "Close", Style = GlassButtonStyle.Secondary, DialogResult = DialogResult.Cancel };
        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = D(108), WrapContents = true, Padding = new Padding(0, D(10), 0, 0)   // six buttons: two rows
        };
        footer.Controls.Add(_changeFlight);
        footer.Controls.Add(_changeSeats);
        footer.Controls.Add(_logRequest);
        footer.Controls.Add(_cancel);
        footer.Controls.Add(another);
        footer.Controls.Add(close);
        CancelButton = close;

        _receiptHost.Controls.Add(_receipt);
        _detailsPage.Controls.Add(_receiptHost);
        _emailBar.Dock = DockStyle.Bottom;
        _emailBar.Height = D(50);
        _emailBar.Padding = new Padding(0, D(6), 0, 0);
        _emailStatus.Width = D(220);
        _emailStatus.Height = D(40);
        _sendEmail.Click += async (_, _) => await SendReceipt();
        _print.Click += (_, _) => PrintReceipt();
        _emailBar.Controls.Add(_print);
        _emailBar.Controls.Add(_emailStatus);
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
        LookupResult result = _controller.Find(_reference.Text);
        if (result.NormalizedReference is not null) _reference.Text = result.NormalizedReference;
        if (!result.Found)
        {
            _referenceRow.SetMessage(result.Error!);
            return;
        }
        ShowBooking(result.Booking!);
    }

    private void ShowLookup()
    {
        _controller.Clear();
        _detailsPage.Visible = false;
        _header.Hint = LookupHint;
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
        TripStatusInfo? trip = null;
        try { trip = _controller.CurrentTrip(DateTime.Now); }
        catch (IOException ex) { AppLogger.Error("ManageBooking", ex); }   // without check-ins the banner falls back to "departed"
        BookingBanner banner = ManageBookingController.DescribeBooking(booking, trip, _controller.CurrentRefund());
        Color bannerColor = banner.Kind switch
        {
            BannerKind.Canceled => Theme.DangerRed,
            BannerKind.Departed => Theme.TextSecondary,
            BannerKind.NoShow => Theme.Warning,
            _ => Theme.Brand
        };
        _banner.ForeColor = bannerColor;
        _banner.BackColor = Color.FromArgb(28, bannerColor);   // tinted strip so the state reads at a glance
        _banner.Padding = new Padding(D(10), 0, 0, 0);
        _banner.TextAlign = ContentAlignment.MiddleLeft;
        _banner.Height = D(34);
        _banner.Text = banner.Text;
        _changeSeats.Enabled = banner.CanModify;
        _changeFlight.Enabled = banner.CanModify && _controller.ChangeFlightRefusal(DateTime.Now) is null;   // not after check-in
        bool requestOpen = banner.CanModify && _controller.HasOpenRefundRequest();
        _logRequest.Text = requestOpen ? "Refund requested" : "Log refund request";
        _logRequest.Enabled = banner.CanModify && !requestOpen;
        _cancel.Enabled = banner.CanModify;
        _policy.Text = banner.PolicyText;

        RefreshEmailBar();
        _lookupPage.Visible = false;
        _detailsPage.Visible = true;
        _header.Hint = "View the receipt, change seats or the flight, or cancel the booking.";
        AcceptButton = null;
    }

    // ------------------------------------------------------------------ email preferences

    private void RefreshEmailBar()
    {
        if (_controller.Current is null) return;
        EmailBarState state = _controller.RefreshEmail();
        _sendEmail.Visible = state.HasEmail;
        _emailStatus.Text = state.StatusText;
        if (!state.HasEmail) return;
        _sendEmail.Enabled = state.Subscribed;
    }

    /// <summary>Opens the A4 print preview of this booking's receipt (works for any booking you can see here, not just a new one).</summary>
    private void PrintReceipt()
    {
        try
        {
            if (_controller.BuildReceipt() is { } receipt) ReceiptPrinter.ShowPreview(this, receipt);
        }
        catch (Exception ex) when (ex is IOException or System.Drawing.Printing.InvalidPrinterException)
        {
            AppLogger.Error("PrintReceipt", ex);
            MessageBox.Show(this, "The receipt could not be prepared for printing. Please try again.", "Print receipt",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
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
                ? (result.Channel == "SMTP" ? "The receipt was sent to " + ManageBookingController.MaskEmail(_controller.ContactEmail!) + "." : "The receipt was saved as a file:\n" + result.Detail)
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

    private void LogRefundRequest()
    {
        RefundRequestResult result = _controller.LogRefundRequest();
        MessageBox.Show(this, result.Message + (result.Ok ? "\n\nTo give the refund, cancel the booking here." : ""), "Refund request", MessageBoxButtons.OK,
            result.Ok ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        Reload();
    }

    private void ChangeFlight()
    {
        if (_controller.Current is null) return;
        if (_controller.ChangeFlightRefusal(DateTime.Now) is { } refusal)
        {
            MessageBox.Show(this, refusal, "Change flight", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var form = new ChangeFlightForm(_controller);
        form.ShowDialog(this);
        if (form.Changed) Reload();
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
