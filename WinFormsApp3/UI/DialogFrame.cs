namespace SkyWings.UI;

/// <summary>
/// The standard frame of a small dialog: a <see cref="PageHeader"/> at the top, the content in the middle, and a row of buttons
/// at the bottom right. Add the primary button to <see cref="Buttons"/> first (it ends up rightmost), then the others.
/// </summary>
public sealed class DialogFrame : Panel
{
    public DialogFrame()
    {
        Dock = DockStyle.Fill;
        TabStop = false;
        Header = new PageHeader();
        Content = new Panel { Dock = DockStyle.Fill, TabIndex = 0 };
        Buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, WrapContents = false, AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(0, Gfx.S(this, 12), 0, 0), TabIndex = 1
        };
        // Docking is applied from the last-added control backwards: content first, then the bottom row, then the top header.
        Controls.Add(Content);
        Controls.Add(Buttons);
        Controls.Add(Header);
    }

    public PageHeader Header { get; }
    public Panel Content { get; }
    public FlowLayoutPanel Buttons { get; }

    /// <summary>
    /// Builds a whole small dialog: the header (title and hint), a white card holding <paramref name="rows"/> stacked top to bottom, and the buttons
    /// (<paramref name="accept"/> rightmost). The window is as wide as <paramref name="width"/> (96-dpi pixels) and as tall as its content needs once
    /// the fields have their real heights.
    /// </summary>
    internal static DialogFrame Create(Form form, int width, string title, string hint, IReadOnlyList<Control> rows, GlassButton accept, GlassButton? cancel)
    {
        var frame = new DialogFrame();
        frame.Header.Title = title;
        frame.Header.Hint = hint;
        var card = new GlassCard { Dock = DockStyle.Fill, ContentPadding = 14 };
        Layouts.Stack(card, rows.ToArray());
        frame.Content.Controls.Add(card);
        frame.Buttons.Controls.Add(accept);
        if (cancel is not null) frame.Buttons.Controls.Add(cancel);
        frame.Attach(form, accept, cancel);
        form.Controls.Add(frame);
        form.ClientSize = new Size(Gfx.S(form, width), Gfx.S(form, 420));
        form.Load += (_, _) => frame.FitHeight(form, card, rows);
        return frame;
    }

    /// <summary>Sets the window height from what the header, the card's rows and the button row actually measure once laid out.</summary>
    private void FitHeight(Form form, GlassCard card, IReadOnlyList<Control> rows)
    {
        form.PerformLayout();
        int chrome = 2 * (Gfx.S(card, card.ShadowSize) + Gfx.S(card, card.ContentPadding));
        int needed = form.Padding.Vertical + Header.Height + chrome + rows.Sum(r => r.Height) + Buttons.Height;
        form.ClientSize = new Size(form.ClientSize.Width, needed);
    }

    /// <summary>Makes <paramref name="accept"/> the form's Enter button and <paramref name="cancel"/> its Esc button (it closes the dialog with Cancel).</summary>
    public void Attach(Form form, GlassButton accept, GlassButton? cancel)
    {
        form.AcceptButton = accept;
        if (cancel is not null)
        {
            cancel.DialogResult = DialogResult.Cancel;
            form.CancelButton = cancel;
        }
        form.Padding = new Padding(Gfx.S(form, Theme.Sp20), Gfx.S(form, Theme.Sp12), Gfx.S(form, Theme.Sp20), Gfx.S(form, Theme.Sp16));
    }
}
