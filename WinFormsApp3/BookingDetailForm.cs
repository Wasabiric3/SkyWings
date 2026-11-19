using SkyWings.Controllers;
using SkyWings.UI;

namespace SkyWings;

/// <summary>Read-only booking details for staff, shown as a boarding-pass style card.</summary>
public sealed class BookingDetailForm : GlassForm
{
    /// <param name="trip">Where the trip is right now (Upcoming … Completed, No-show, Canceled), shown as the status pill.</param>
    public BookingDetailForm(BookingRecord booking, TripStatusInfo trip)
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
        footer.Controls.Add(close);

        // Header: reference, route and a status pill so the state is visible before reading the details.
        Color statusColor = TripColors.For(trip.Phase);
        var header = new Panel { Dock = DockStyle.Top, Height = D(58) };
        var status = new Label
        {
            // Wide enough for the longest label, "RETURN · CHECK-IN OPEN".
            Text = trip.Label.ToUpperInvariant(), AutoSize = false, Width = D(190), Dock = DockStyle.Right,
            TextAlign = ContentAlignment.MiddleCenter, Font = new Font(Theme.Small, FontStyle.Bold),
            ForeColor = statusColor, BackColor = Color.FromArgb(32, statusColor)
        };
        var statusGap = new Panel { Dock = DockStyle.Right, Width = D(10) };
        var heading = new Label
        {
            Text = $"{booking.Reference}   {booking.Origin} → {booking.Destination}", Dock = DockStyle.Fill,
            Font = Theme.Header, ForeColor = Theme.TextPrimary, TextAlign = ContentAlignment.MiddleLeft
        };
        header.Controls.Add(heading);
        header.Controls.Add(statusGap);
        header.Controls.Add(status);

        var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(D(20), D(16), D(20), D(6)) };
        body.Controls.Add(receipt);
        body.Controls.Add(footer);
        body.Controls.Add(header);
        Controls.Add(body);

        ClientSize = new Size(D(860), D(104) + ((rows.Count + 1) / 2) * D(58) + D(90) + D(58));
        Theme.Apply(this);
        CancelButton = close;
    }
}
