using SkyWings.UI;

namespace SkyWings;

/// <summary>Lets a staff member choose a new password. When <c>forced</c>, it cannot be skipped.</summary>
public sealed class ChangePasswordForm : GlassForm
{
    private readonly AdminService _service;
    private readonly TextBox _current = new() { UseSystemPasswordChar = true, MaxLength = PasswordPolicy.MaxLength };
    private readonly TextBox _new = new() { UseSystemPasswordChar = true, MaxLength = PasswordPolicy.MaxLength };
    private readonly TextBox _repeat = new() { UseSystemPasswordChar = true, MaxLength = PasswordPolicy.MaxLength };
    private readonly FieldRow _currentRow, _newRow, _repeatRow;

    public ChangePasswordForm(AdminService service, bool forced)
    {
        _service = service;
        Text = forced ? "Choose a new password · SkyWings" : "Change password · SkyWings";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        ControlBox = !forced;
        AutoScaleMode = AutoScaleMode.None;

        _currentRow = new FieldRow(forced ? "CURRENT (TEMPORARY) PASSWORD" : "CURRENT PASSWORD", _current);
        _newRow = new FieldRow($"NEW PASSWORD  (AT LEAST {PasswordPolicy.MinLength} CHARACTERS)", _new);
        _repeatRow = new FieldRow("REPEAT NEW PASSWORD", _repeat);
        foreach (var row in new[] { _currentRow, _newRow, _repeatRow })
            if (row.Input is TextBox box) { var r = row; box.TextChanged += (_, _) => r.ClearMessage(); }

        var save = new GlassButton { Text = "Save password" };
        save.Click += (_, _) => Save();
        var cancel = new GlassButton { Text = forced ? "Sign out" : "Cancel", Style = GlassButtonStyle.Secondary };
        DialogFrame.Create(this, 500, forced ? "Choose a new password" : "Change password",
            forced ? "Your password was set by someone else, so you need to replace it before continuing."
                   : "Pick something you don't use anywhere else.",
            new Control[] { _currentRow, _newRow, _repeatRow }, save, cancel);
        Theme.Apply(this);
    }

    private void Save()
    {
        if (PasswordPolicy.Check(_new.Text, _service.Session.Username) is { } problem) { _newRow.SetMessage(problem); return; }
        if (!string.Equals(_new.Text, _repeat.Text, StringComparison.Ordinal)) { _repeatRow.SetMessage("The passwords don't match."); return; }

        try
        {
            _service.ChangeOwnPassword(_current.Text, _new.Text);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (UnauthorizedAccessException ex) { _current.Clear(); _currentRow.SetMessage(ex.Message); }   // clear first, see StaffLoginForm
        catch (ArgumentException ex) { _newRow.SetMessage(ex.Message); }
        catch (IOException ex)
        {
            AppLogger.Error("ChangePassword", ex);
            MessageBox.Show(this, "The password could not be changed.\n\n" + ex.Message, "Change password", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
