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
