using SkyWings.Controllers;
using SkyWings.UI;

namespace SkyWings;

/// <summary>Read-only booking details for staff, shown as a boarding-pass style card.</summary>
public sealed class BookingDetailForm : GlassForm
{
    /// <param name="trip">Where the trip is right now (Upcoming … Completed, No-show, Canceled), shown as the status pill.</param>
    /// <param name="request">
    /// A refund request for this booking. Its details and the customer's note are shown above the receipt, and while it is still open
    /// the dialog closes with <see cref="DialogResult.Yes"/> (Approve) or <see cref="DialogResult.No"/> (Decline).
    /// </param>
    public BookingDetailForm(BookingRecord booking, TripStatusInfo trip, RefundRequestRow? request = null)
    {
        Text = $"Booking {booking.Reference}";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.None;
        int D(int px) => Gfx.S(this, px);

        var rows = BookingSummary.Rows(booking);
        var receipt = new ReceiptPanel { Dock = DockStyle.Fill };
        receipt.SetReceipt(booking.Reference, rows);

        var close = new GlassButton { Text = "Close", Style = GlassButtonStyle.Secondary, DialogResult = DialogResult.Cancel, Dock = DockStyle.Right };
        var footer = new Panel { Dock = DockStyle.Bottom, Height = D(54), Padding = new Padding(0, D(10), 0, 0) };
        bool open = request is { Status: BookingStore.RequestOpen };
        if (open)
        {
            // Left to right: Decline, Approve, Close (docked right, so the last one added sits furthest right).
            var decline = new GlassButton { Text = "Decline", Style = GlassButtonStyle.Danger, DialogResult = DialogResult.No, Dock = DockStyle.Right };
            var approve = new GlassButton { Text = "Approve refund", Style = GlassButtonStyle.Primary, DialogResult = DialogResult.Yes, Dock = DockStyle.Right, Width = D(150) };
            footer.Controls.Add(decline);
            footer.Controls.Add(new Panel { Dock = DockStyle.Right, Width = D(10) });
            footer.Controls.Add(approve);
            footer.Controls.Add(new Panel { Dock = DockStyle.Right, Width = D(10) });
        }
        footer.Controls.Add(close);

        // Header: reference, route and a status pill so the state is visible before reading the details.
        Color statusColor = TripColors.For(trip.Phase);
        var header = new Panel { Dock = DockStyle.Top, Height = D(58) };
        var status = new StatusPill
        {
            // Wide enough for the longest label, "RETURN · CHECK-IN OPEN".
            Text = trip.Label.ToUpperInvariant(), Width = D(260), Dock = DockStyle.Right,
            TextColor = statusColor, FillColor = Gfx.Mix(statusColor, Color.White, 0.86f)
        };
        var statusGap = new Panel { Dock = DockStyle.Right, Width = D(10) };
        var heading = new Label
        {
            Text = $"{booking.Reference}   {booking.Origin} → {booking.Destination}", Dock = DockStyle.Fill,
            Font = Theme.Title, ForeColor = Theme.Brand, TextAlign = ContentAlignment.MiddleLeft
        };
        header.Controls.Add(heading);
        header.Controls.Add(statusGap);
        header.Controls.Add(status);

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(D(20), D(16), D(20), D(6)) };
        body.Controls.Add(receipt);
        body.Controls.Add(footer);
        int requestHeight = 0;
        if (request is not null)
        {
            requestHeight = D(176);
            body.Controls.Add(BuildRequestPanel(booking, request, requestHeight));   // added before the header, so it sits just under it
        }
        body.Controls.Add(header);
        Controls.Add(body);

        ClientSize = new Size(D(860), D(104) + ((rows.Count + 1) / 2) * D(58) + D(90) + D(58) + requestHeight);
        Theme.Apply(this);
        CancelButton = close;
    }

    /// <summary>The refund request: where it came from, what the refund would be, and the customer's own words.</summary>
    private Panel BuildRequestPanel(BookingRecord booking, RefundRequestRow request, int height)
    {
        int D(int px) => Gfx.S(this, px);
        bool open = request.Status == BookingStore.RequestOpen;

        var panel = new Panel { Dock = DockStyle.Top, Height = height, Padding = new Padding(D(14), D(10), D(14), D(10)), BackColor = Theme.PageBg };

        var note = new TextBox
        {
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, Dock = DockStyle.Fill, BorderStyle = BorderStyle.None,
            BackColor = Theme.BgCard, ForeColor = Theme.TextPrimary, Font = Theme.Body, TabStop = false,
            Text = string.IsNullOrWhiteSpace(request.CustomerMessage)
                ? "The customer did not leave a note."
                : request.CustomerMessage.Replace("\r\n", "\n").Replace("\n", Environment.NewLine)
        };
        var noteHost = new Panel { Dock = DockStyle.Fill, Padding = new Padding(D(1)), BackColor = Theme.Border };
        noteHost.Controls.Add(note);

        string outcome;
        if (open)
        {
            decimal refund = AppData.ComputeRefundAmount(booking.TotalAmount, booking.FlightDateValue);
            outcome = $"Refund if approved: PHP {refund:N2} ({AppData.RefundRate(booking.FlightDateValue):P0}) to {booking.PaymentMethod} {booking.MaskedAccount}";
        }
        else if (request.Status == BookingStore.RequestDone) outcome = $"Refunded by {request.ClosedBy}";
        else if (request.Status == BookingStore.RequestSettled) outcome = $"Settled · {request.Note}";
        else outcome = $"Declined by {request.ClosedBy}: {request.Note}";

        string source = request.Source == "EMAIL" ? "Email" : "Desk";
        var meta = new Label
        {
            Text = $"{source} request · {request.LoggedAt:MMM d, yyyy h:mm tt}\n{outcome}", Dock = DockStyle.Top, Height = D(44),
            Font = Theme.Small, ForeColor = Theme.TextSecondary, TextAlign = ContentAlignment.MiddleLeft
        };
        var title = new Label
        {
            Text = "CUSTOMER'S REQUEST", Dock = DockStyle.Top, Height = D(22),
            Font = new Font(Theme.Small, FontStyle.Bold), ForeColor = Theme.TextSecondary, TextAlign = ContentAlignment.MiddleLeft
        };

        panel.Controls.Add(noteHost);
        bool settled = request.Status == BookingStore.RequestSettled;
        if ((open || settled) && request.Source == "EMAIL" && !request.SenderMatches)
        {
            panel.Controls.Add(new Label
            {
                Text = settled
                    ? "⚠ Sent from an address that is not the booking's. Already settled; nothing to approve."
                    : "⚠ This email came from an address that is not the one on the booking. Check it before approving.",
                Dock = DockStyle.Top, Height = D(26), Font = new Font(Theme.Small, FontStyle.Bold), ForeColor = Theme.Warning,
                TextAlign = ContentAlignment.MiddleLeft
            });
        }
        panel.Controls.Add(meta);
        panel.Controls.Add(title);
        return panel;
    }
}
