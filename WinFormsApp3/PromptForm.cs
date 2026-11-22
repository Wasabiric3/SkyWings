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

        _input.MaxLength = maxLength;
        _input.PlaceholderText = placeholder;
        _row = new FieldRow(caption, _input);
        _input.TextChanged += (_, _) => _row.ClearMessage();

        var ok = new GlassButton { Text = okText };
        ok.Click += (_, _) => Accept();
        var cancel = new GlassButton { Text = "Cancel", Style = GlassButtonStyle.Secondary };

        var rows = new List<Control> { _row };
        if (optionText is not null)
        {
            _option = new CheckBox { Text = optionText, Checked = optionDefault, AutoSize = false, Height = D(34), Width = D(480) };
            rows.Add(_option);
        }
        DialogFrame.Create(this, 540, title, hint, rows, ok, cancel);
        Theme.Apply(this);
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
