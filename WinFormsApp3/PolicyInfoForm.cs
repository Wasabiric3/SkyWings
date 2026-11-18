using SkyWings.Controllers;
using SkyWings.UI;

namespace SkyWings;

/// <summary>
/// Customer information window opened from the footer links on the home screen: flight policies, baggage guide and
/// fare rules. One tab per page; the link that was clicked decides which tab opens first.
/// </summary>
public sealed class PolicyInfoForm : GlassForm
{
    private readonly IReadOnlyList<InfoPage> _pages = PolicyInfo.Pages();
    private readonly Dictionary<GlassButton, InfoPage> _tabs = new();
    private readonly RichTextBox _text = new() { ReadOnly = true, BorderStyle = BorderStyle.None, BackColor = Color.White, DetectUrls = false };

    /// <param name="title">Title of the page to show first, e.g. "Baggage guide". Unknown titles show the first page.</param>
    public PolicyInfoForm(string title)
    {
        Text = title + " · SkyWings";
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        SizeForScreen(820, 620, 640, 480);
        Padding = new Padding(D(24), D(12), D(24), D(16));

        var close = new GlassButton { Text = "Close", Style = GlassButtonStyle.Secondary, Height = D(38), DialogResult = DialogResult.Cancel };
        var footer = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = D(54), FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, D(10), 0, 0) };
        footer.Controls.Add(close);

        var nav = new FlowLayoutPanel { Dock = DockStyle.Top, Height = D(54), WrapContents = false, Padding = new Padding(0, D(6), 0, 0) };
        foreach (InfoPage page in _pages)
        {
            var tab = new GlassButton { Text = page.Title, Style = GlassButtonStyle.Ghost, Height = D(38) };
            _tabs[tab] = page;
            tab.Click += (_, _) => Show(tab);
            nav.Controls.Add(tab);
        }

        _text.Dock = DockStyle.Fill;
        _text.Font = Theme.Body;
        _text.TabStop = false;
        var card = new GlassCard { Dock = DockStyle.Fill, ContentPadding = 16 };
        card.Controls.Add(_text);
        var host = new Panel { Dock = DockStyle.Fill };
        host.Controls.Add(card);

        Controls.Add(host);
        Controls.Add(footer);
        Controls.Add(nav);
        Theme.Apply(this);
        CancelButton = close;

        Show(_tabs.FirstOrDefault(t => t.Value.Title == title).Key ?? _tabs.Keys.First());
    }

    private int D(int px) => Gfx.S(this, px);

    private void Show(GlassButton active)
    {
        foreach (var (button, _) in _tabs) button.Style = button == active ? GlassButtonStyle.Primary : GlassButtonStyle.Ghost;
        Render(_tabs[active]);
    }

    /// <summary>Draws a page: heading, intro, then each section with its rule on the left and value on the right.</summary>
    private void Render(InfoPage page)
    {
        _text.Clear();
        _text.SelectionTabs = new[] { D(300) };
        _text.SelectionIndent = 0;

        _text.SelectionFont = Theme.Title;
        _text.SelectionColor = Theme.TextPrimary;
        _text.AppendText(page.Title + Environment.NewLine);
        _text.SelectionFont = Theme.Body;
        _text.SelectionColor = Theme.TextSecondary;
        _text.AppendText(page.Intro + Environment.NewLine + Environment.NewLine);

        var bold = new Font(Theme.Body, FontStyle.Bold);
        foreach (PolicySection section in page.Sections)
        {
            _text.SelectionFont = Theme.Subtitle;
            _text.SelectionColor = Theme.Accent;
            _text.AppendText(section.Heading + Environment.NewLine);
            foreach (var (rule, value) in section.Rows)
            {
                // Hanging indent keeps a long value tidy under the value column instead of running back to the left edge.
                _text.SelectionIndent = 0;
                _text.SelectionHangingIndent = D(300);
                _text.SelectionFont = Theme.Body;
                _text.SelectionColor = Theme.TextSecondary;
                _text.AppendText(rule + "\t");
                _text.SelectionFont = bold;
                _text.SelectionColor = Theme.TextPrimary;
                _text.AppendText(value + Environment.NewLine);
            }
            _text.SelectionHangingIndent = 0;
            _text.AppendText(Environment.NewLine);
        }
        _text.Select(0, 0);
    }
}
