using SkyWings.UI;

namespace SkyWings;

/// <summary>Shown once, when no staff accounts exist yet: creates the first administrator.</summary>
public sealed class FirstRunAdminForm : GlassForm
{
    private readonly BookingStore _store;
    private readonly TextBox _name = new() { MaxLength = 80 };
    private readonly TextBox _username = new() { Text = "admin", MaxLength = 40 };
    private readonly TextBox _password = new() { UseSystemPasswordChar = true, MaxLength = PasswordPolicy.MaxLength };
    private readonly TextBox _confirm = new() { UseSystemPasswordChar = true, MaxLength = PasswordPolicy.MaxLength };
    private readonly FieldRow _nameRow, _usernameRow, _passwordRow, _confirmRow;

    public FirstRunAdminForm(BookingStore store)
    {
        _store = store;
        Text = "Set up staff access · SkyWings";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.None;

        _nameRow = new FieldRow("YOUR NAME", _name);
        _usernameRow = new FieldRow("USERNAME", _username);
        _passwordRow = new FieldRow($"PASSWORD  (AT LEAST {PasswordPolicy.MinLength} CHARACTERS)", _password);
        _confirmRow = new FieldRow("REPEAT PASSWORD", _confirm);
        foreach (var row in new[] { _nameRow, _usernameRow, _passwordRow, _confirmRow })
            if (row.Input is TextBox box) { var r = row; box.TextChanged += (_, _) => r.ClearMessage(); }

        var create = new GlassButton { Text = "Create account" };
        create.Click += (_, _) => Create();
        var cancel = new GlassButton { Text = "Cancel", Style = GlassButtonStyle.Secondary };
        DialogFrame.Create(this, 540, "Create the first administrator",
            "No staff accounts exist yet. This account can manage flights, other staff, and backups. " +
            "A long phrase works better than a short complicated password.",
            new Control[] { _nameRow, _usernameRow, _passwordRow, _confirmRow }, create, cancel);
        Theme.Apply(this);
    }

    public string CreatedUsername { get; private set; } = "";

    private void Create()
    {
        if (_name.Text.Trim().Length == 0) { _nameRow.SetMessage("Enter your name."); return; }
        if (PasswordPolicy.Check(_password.Text, _username.Text) is { } problem) { _passwordRow.SetMessage(problem); return; }
        if (!string.Equals(_password.Text, _confirm.Text, StringComparison.Ordinal)) { _confirmRow.SetMessage("The passwords don't match."); return; }

        try
        {
            StaffAccount account = _store.CreateStaff(_username.Text, _name.Text, _password.Text, StaffRole.Admin, mustChangePassword: false);
            _store.WriteAudit("system", "", "FIRST_ADMIN_CREATED", account.Username);
            CreatedUsername = account.Username;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (ArgumentException ex) { _usernameRow.SetMessage(ex.Message); }
        catch (InvalidOperationException ex) { _usernameRow.SetMessage(ex.Message); }
        catch (IOException ex)
        {
            AppLogger.Error("StaffSetup", ex);
            MessageBox.Show(this, "The account could not be created.\n\n" + ex.Message, "Staff accounts", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void InitializeComponent()
    {

    }
}
