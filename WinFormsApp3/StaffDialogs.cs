using SkyWings.UI;

namespace SkyWings;

/// <summary>Creates a staff account with a generated temporary password that the person must replace at first sign-in.</summary>
public sealed class AddStaffForm : GlassForm
{
    private readonly AdminService _service;
    private readonly TextBox _username = new() { MaxLength = 40 };
    private readonly TextBox _name = new() { MaxLength = 80 };
    private readonly ComboBox _role = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _password = new() { MaxLength = PasswordPolicy.MaxLength, PlaceholderText = $"Type a password, at least {PasswordPolicy.MinLength} characters" };
    private readonly FieldRow _usernameRow, _nameRow, _roleRow, _passwordRow;

    public AddStaffForm(AdminService service)
    {
        _service = service;
        Text = "Add staff · SkyWings";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.None;
        int D(int px) => Gfx.S(this, px);

        _role.Items.Add("Agent  ·  view and cancel bookings, see reports");
        // Only a Super Admin may create administrators; the list position (1 = Admin) is read in Create().
        if (service.Session.Has(Permission.ManageAdmins)) _role.Items.Add("Admin  ·  everything except administrators and the database");
        _role.SelectedIndex = 0;
        _usernameRow = new FieldRow("USERNAME", _username);
        _nameRow = new FieldRow("FULL NAME", _name);
        _roleRow = new FieldRow("ROLE", _role);
        _passwordRow = new FieldRow("TEMPORARY PASSWORD  (GIVE IT TO THEM; THEY MUST CHANGE IT)", _password);
        foreach (var row in new[] { _usernameRow, _nameRow, _passwordRow })
            if (row.Input is TextBox box) { var r = row; box.TextChanged += (_, _) => r.ClearMessage(); }

        var save = new GlassButton { Text = "Create account" };
        save.Click += (_, _) => Create();
        var cancel = new GlassButton { Text = "Cancel", Style = GlassButtonStyle.Secondary };
        DialogFrame.Create(this, 560, "Add a staff member", "They sign in with the temporary password and must choose their own straight away.",
            new Control[] { _usernameRow, _nameRow, _roleRow, _passwordRow }, save, cancel);
        Theme.Apply(this);
    }

    /// <summary>Picks the field a validation message is about.</summary>
    private FieldRow FieldFor(string message)
    {
        if (message.Contains("assword", StringComparison.Ordinal) || message.StartsWith("Use a", StringComparison.Ordinal)) return _passwordRow;
        if (message.StartsWith("Enter the person", StringComparison.Ordinal)) return _nameRow;
        return _usernameRow;
    }

    private void Create()
    {
        if (PasswordPolicy.Check(_password.Text, _username.Text) is { } problem) { _passwordRow.SetMessage(problem); return; }
        try
        {
            _service.CreateStaff(_username.Text, _name.Text, _password.Text, _role.SelectedIndex == 1 ? StaffRole.Admin : StaffRole.Agent);
            MessageBox.Show(this,
                $"Account created.\n\nUsername: {_username.Text.Trim()}\nTemporary password: {_password.Text}\n\n" +
                "Give these to the person now. The password is not shown again, and they must change it when they sign in.",
                "Staff account created", MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (ArgumentException ex) { FieldFor(ex.Message).SetMessage(ex.Message); }
        catch (InvalidOperationException ex) { _usernameRow.SetMessage(ex.Message); }
        catch (UnauthorizedAccessException ex) { MessageBox.Show(this, ex.Message, "Add staff", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
        catch (IOException ex)
        {
            AppLogger.Error("AddStaff", ex);
            MessageBox.Show(this, "The account could not be created.\n\n" + ex.Message, "Add staff", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}

/// <summary>Sets a new temporary password for someone who is locked out or forgot theirs.</summary>
public sealed class ResetPasswordForm : GlassForm
{
    private readonly AdminService _service;
    private readonly StaffAccount _account;
    private readonly TextBox _password = new() { MaxLength = PasswordPolicy.MaxLength, PlaceholderText = $"Type a password, at least {PasswordPolicy.MinLength} characters" };
    private FieldRow _row = null!;

    public ResetPasswordForm(AdminService service, StaffAccount account)
    {
        _service = service;
        _account = account;
        Text = "Reset password · SkyWings";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.None;
        int D(int px) => Gfx.S(this, px);
        _row = new FieldRow("TEMPORARY PASSWORD", _password);
        _password.TextChanged += (_, _) => _row.ClearMessage();

        var reset = new GlassButton { Text = "Reset password" };
        reset.Click += (_, _) => Reset();
        var cancel = new GlassButton { Text = "Cancel", Style = GlassButtonStyle.Secondary };
        DialogFrame.Create(this, 540, $"Reset password for {account.DisplayName}",
            "They will use this temporary password once and must choose their own. Any lock on the account is cleared.",
            new Control[] { _row }, reset, cancel);
        Theme.Apply(this);
    }

    private void Reset()
    {
        if (PasswordPolicy.Check(_password.Text, _account.Username) is { } problem) { _row.SetMessage(problem); return; }
        try
        {
            _service.ResetPassword(_account.Id, _password.Text);
            MessageBox.Show(this, $"Password reset.\n\nUsername: {_account.Username}\nTemporary password: {_password.Text}\n\nGive it to them now; it is not shown again.",
                "Password reset", MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or UnauthorizedAccessException or IOException)
        {
            AppLogger.Error("ResetPassword", ex);
            MessageBox.Show(this, ex.Message, "Reset password", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}

/// <summary>Asks for the password that will protect a new backup file.</summary>
public sealed class BackupPasswordForm : GlassForm
{
    private readonly TextBox _password = new() { UseSystemPasswordChar = true, MaxLength = PasswordPolicy.MaxLength };
    private readonly TextBox _repeat = new() { UseSystemPasswordChar = true, MaxLength = PasswordPolicy.MaxLength };
    private readonly FieldRow _passwordRow, _repeatRow;

    public BackupPasswordForm()
    {
        Text = "Protect the backup · SkyWings";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.None;
        int D(int px) => Gfx.S(this, px);
        _passwordRow = new FieldRow($"BACKUP PASSWORD  (AT LEAST {PasswordPolicy.MinLength} CHARACTERS)", _password);
        _repeatRow = new FieldRow("REPEAT PASSWORD", _repeat);
        _password.TextChanged += (_, _) => _passwordRow.ClearMessage();
        _repeat.TextChanged += (_, _) => _repeatRow.ClearMessage();

        var ok = new GlassButton { Text = "Create backup" };
        ok.Click += (_, _) => Accept();
        var cancel = new GlassButton { Text = "Cancel", Style = GlassButtonStyle.Secondary };
        DialogFrame.Create(this, 540, "Protect the backup",
            "The backup contains customer names, addresses and ID numbers, so it is encrypted. " +
            "Without this password it cannot be restored, and it cannot be recovered if you forget it.",
            new Control[] { _passwordRow, _repeatRow }, ok, cancel);
        Theme.Apply(this);
    }

    public string BackupPassword { get; private set; } = "";

    private void Accept()
    {
        if (PasswordPolicy.Check(_password.Text) is { } problem) { _passwordRow.SetMessage(problem); return; }
        if (!string.Equals(_password.Text, _repeat.Text, StringComparison.Ordinal)) { _repeatRow.SetMessage("The passwords don't match."); return; }
        BackupPassword = _password.Text;
        DialogResult = DialogResult.OK;
        Close();
    }
}

/// <summary>Asks for the backup's password and the signed-in person's own password before a restore.</summary>
public sealed class RestoreCredentialsForm : GlassForm
{
    private readonly TextBox _backupPassword = new() { UseSystemPasswordChar = true, MaxLength = PasswordPolicy.MaxLength };
    private readonly TextBox _staffPassword = new() { UseSystemPasswordChar = true, MaxLength = PasswordPolicy.MaxLength };
    private readonly FieldRow _backupRow, _staffRow;

    public RestoreCredentialsForm(string fileName)
    {
        Text = "Restore backup · SkyWings";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.None;
        int D(int px) => Gfx.S(this, px);
        _backupRow = new FieldRow("BACKUP PASSWORD", _backupPassword);
        _staffRow = new FieldRow("YOUR OWN PASSWORD (TO CONFIRM IT'S YOU)", _staffPassword);
        _backupPassword.TextChanged += (_, _) => _backupRow.ClearMessage();
        _staffPassword.TextChanged += (_, _) => _staffRow.ClearMessage();

        var ok = new GlassButton { Text = "Restore", Style = GlassButtonStyle.Danger };
        ok.Click += (_, _) => Accept();
        var cancel = new GlassButton { Text = "Cancel", Style = GlassButtonStyle.Secondary };
        DialogFrame.Create(this, 560, "Restore from backup",
            $"File: {fileName}\nBookings, flights and seats are replaced by the backup's. Staff accounts and the audit log stay as they are. " +
            "A safety copy of the current data is saved first.",
            new Control[] { _backupRow, _staffRow }, ok, cancel);
        Theme.Apply(this);
    }

    public string BackupPassword => _backupPassword.Text;
    public string StaffPassword => _staffPassword.Text;

    private void Accept()
    {
        if (_backupPassword.Text.Length == 0) { _backupRow.SetMessage("Enter the backup password."); return; }
        if (_staffPassword.Text.Length == 0) { _staffRow.SetMessage("Enter your own password."); return; }
        DialogResult = DialogResult.OK;
        Close();
    }
}
