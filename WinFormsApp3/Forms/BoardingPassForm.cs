using SkyWings.UI;

namespace SkyWings;

/// <summary>
/// Boarding passes for the travellers who have checked in on one leg. Each pass is shown on screen with the same ticket design that
/// prints (see <see cref="BoardingPassView"/>), one under the other, with print preview.
/// </summary>
public sealed class BoardingPassForm : GlassForm
{
    private readonly List<BoardingPassData> _printData = new();   // what the tickets show, one per traveller (on screen and on paper)

    public BoardingPassForm(BookingRecord booking, bool returnLeg, IReadOnlyList<CheckInRecord> records)
    {
        Text = $"Boarding passes · {booking.Reference}";
        StartPosition = FormStartPosition.CenterParent;
        SizeForScreen(1100, 720, 700, 480);   // wide enough that the ticket shows near its printed size
        int D(int px) => Gfx.S(this, px);
        Padding = new Padding(D(20), D(12), D(20), D(16));

        var host = new Panel { Dock = DockStyle.Fill, AutoScroll = true };

        var passes = new List<BoardingPassView>();
        var items = new List<Control>();
        foreach (CheckInRecord record in records)
        {
            BoardingPassData data = BoardingPassData.From(booking, returnLeg, record);
            _printData.Add(data);

            var pass = new BoardingPassView();
            pass.SetPass(data);
            passes.Add(pass);
            items.Add(pass);
            items.Add(new Panel { Height = D(14) });
        }
        Layouts.Stack(host, items.ToArray());

        // The tickets keep their proportions, so their height follows the window's width; keep the scroll area in step with that.
        void FitTickets()
        {
            int width = host.ClientSize.Width;
            int total = 0;
            foreach (BoardingPassView pass in passes)
            {
                pass.Height = BoardingPassView.HeightForWidth(width);
                total += pass.Height + D(14);
            }
            host.AutoScrollMinSize = new Size(0, total);
        }
        host.Resize += (_, _) => FitTickets();
        host.HandleCreated += (_, _) => FitTickets();
        FitTickets();

        var print = new GlassButton { Text = "Print", Style = GlassButtonStyle.Secondary };
        print.Click += (_, _) => PrintPasses();
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

    private void PrintPasses() => BoardingPassPrinter.ShowPreview(this, _printData);
}
