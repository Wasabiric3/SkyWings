using SkyWings.UI;

namespace SkyWings;

/// <summary>A small dialog that asks for one line of text (a booking reference, a reason). <see cref="Value"/> holds the answer after OK.</summary>
public sealed class PromptForm : GlassForm
{
    private readonly TextBox _input = new();
    private readonly FieldRow _row;
    private readonly Func<string, string?>? _validate;

    private readonly CheckBox? _option;

    public string Value { get; private set; } = "";

    /// <summary>The state of the optional checkbox when OK was pressed (false when the dialog has none).</summary>
    public bool OptionChecked { get; private set; }

    /// <param name="validate">Returns a message to show under the box when the text is not acceptable, or null when it is.</param>
    public PromptForm(string title, string hint, string caption, string placeholder = "", int maxLength = 200,
        string okText = "OK", Func<string, string?>? validate = null, string? optionText = null, bool optionDefault = true)
    {
        _validate = validate;
        Text = title + " · SkyWings";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.None;
        int D(int px) => Gfx.S(this, px);
        ClientSize = new Size(D(540), D(optionText is null ? 320 : 372));

        _input.MaxLength = maxLength;
        _input.PlaceholderText = placeholder;
        var heading = new Label { Text = title, Font = Theme.Header, AutoSize = false, Height = D(44), TextAlign = ContentAlignment.MiddleLeft };
        var hintLabel = new Label { Text = hint, Font = Theme.Small, ForeColor = Theme.TextSecondary, AutoSize = false, Height = D(60) };
        _row = new FieldRow(caption, _input);
        _input.TextChanged += (_, _) => _row.ClearMessage();

        var ok = new GlassButton { Text = okText };
        ok.Click += (_, _) => Accept();
        var cancel = new GlassButton { Text = "Cancel", Style = GlassButtonStyle.Secondary, DialogResult = DialogResult.Cancel };
        var buttons = new FlowLayoutPanel { Height = D(56), FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, D(10), 0, 0) };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);

        var card = new GlassCard { Dock = DockStyle.Fill, ContentPadding = 14 };
        if (optionText is not null)
        {
            _option = new CheckBox { Text = optionText, Checked = optionDefault, AutoSize = false, Height = D(34), Width = D(480) };
            Layouts.Stack(card, heading, hintLabel, _row, _option, buttons);
        }
        else Layouts.Stack(card, heading, hintLabel, _row, buttons);
        Controls.Add(card);
        Padding = new Padding(D(10));
        Theme.Apply(this);
        AcceptButton = ok;
        CancelButton = cancel;
        Shown += (_, _) => _input.Focus();
    }

    private void Accept()
    {
        string text = _input.Text.Trim();
        if (_validate?.Invoke(text) is { } problem) { _row.SetMessage(problem); return; }
        Value = text;
        OptionChecked = _option?.Checked ?? false;
        DialogResult = DialogResult.OK;
        Close();
    }
}
