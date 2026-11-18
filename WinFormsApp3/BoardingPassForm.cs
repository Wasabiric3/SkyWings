using System.Drawing.Printing;
using SkyWings.UI;

namespace SkyWings;

/// <summary>Boarding passes for the travellers who have checked in on one leg, with print preview.</summary>
public sealed class BoardingPassForm : GlassForm
{
    private readonly List<(string Reference, List<(string Label, string Value)> Rows)> _passes = new();

    public BoardingPassForm(BookingRecord booking, bool returnLeg, IReadOnlyList<CheckInRecord> records)
    {
        Text = $"Boarding passes · {booking.Reference}";
        StartPosition = FormStartPosition.CenterParent;
        SizeForScreen(860, 640, 700, 480);
        int D(int px) => Gfx.S(this, px);
        Padding = new Padding(D(20), D(12), D(20), D(16));

        string origin = returnLeg ? booking.ReturnOrigin ?? "" : booking.Origin;
        string destination = returnLeg ? booking.ReturnDestination ?? "" : booking.Destination;
        var host = new Panel { Dock = DockStyle.Fill, AutoScroll = true };

        var items = new List<Control>();
        foreach (CheckInRecord record in records)
        {
            DateTime departure = AppData.DepartureOf(record.FlightNo, record.FlightDate);
            var rows = new List<(string, string)>
            {
                ("Passenger", record.PassengerName),
                ("Class", booking.ClassName),
                ("Flight", record.FlightNo),
                ("Seat", record.SeatCode),
                ("Route", $"{Code(origin)} → {Code(destination)}"),
                ("Date", record.FlightDate.ToString("MMM dd, yyyy")),
                ("Departs", departure.ToString("h:mm tt")),
                ("Checked in", record.CheckedInAt.ToString("MMM d, h:mm tt"))
            };
            _passes.Add((booking.Reference, rows));

            var pass = new ReceiptPanel { Title = "Boarding pass", Height = D(100) + ((rows.Count + 1) / 2) * D(58) };
            pass.SetReceipt(booking.Reference, rows);
            items.Add(pass);
            items.Add(new Panel { Height = D(14) });
        }
        int total = Layouts.Stack(host, items.ToArray());
        host.AutoScrollMinSize = new Size(0, total);

        var print = new GlassButton { Text = "Print", Style = GlassButtonStyle.Secondary };
        print.Click += (_, _) => PrintPasses(booking.Reference);
        var close = new GlassButton { Text = "Close", DialogResult = DialogResult.Cancel };
        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = D(56), FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false, Padding = new Padding(0, D(10), 0, 0)
        };
        footer.Controls.Add(close);
        footer.Controls.Add(print);

        var card = new GlassCard { Dock = DockStyle.Fill, ContentPadding = 14 };
        card.Controls.Add(host);
        card.Controls.Add(footer);
        Controls.Add(card);
        Theme.Apply(this);
        CancelButton = close;
    }

    private static string Code(string place)
    {
        int open = place.IndexOf('(');
        int close = place.IndexOf(')');
        return open >= 0 && close > open ? place[(open + 1)..close] : place;
    }

    private void PrintPasses(string reference)
    {
        int index = 0;
        using var document = new PrintDocument { DocumentName = $"SkyWings boarding passes {reference}" };
        document.PrintPage += (_, e) =>
        {
            Graphics g = e.Graphics!;
            using var title = new Font("Segoe UI", 20, FontStyle.Bold);
            using var heading = new Font("Segoe UI", 12, FontStyle.Bold);
            using var body = new Font("Segoe UI", 12);
            float x = e.MarginBounds.Left, y = e.MarginBounds.Top;
            g.DrawString("SkyWings · Boarding pass", title, Brushes.Black, x, y);
            y += 56;
            g.DrawString($"Reference: {_passes[index].Reference}", heading, Brushes.Black, x, y);
            y += 40;
            foreach (var (label, value) in _passes[index].Rows)
            {
                g.DrawString($"{label}:", heading, Brushes.Black, x, y);
                g.DrawString(value, body, Brushes.Black, x + 160, y);
                y += 32;
            }
            index++;
            e.HasMorePages = index < _passes.Count;
        };
        using var preview = new PrintPreviewDialog { Document = document, Width = 900, Height = 720 };
        preview.ShowDialog(this);
    }
}
