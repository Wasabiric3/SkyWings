using SkyWings.UI;

namespace SkyWings;

/// <summary>
/// Staff sign-in. <see cref="SignIn"/> runs the whole flow: first-run setup or migration of the old shared admin,
/// the sign-in itself, and the forced password change when one is required.
/// </summary>
public sealed class StaffLoginForm : GlassForm
{
    private readonly BookingStore _store;
    private readonly TextBox _username = new();
    private readonly TextBox _password = new() { UseSystemPasswordChar = true };
    private readonly FieldRow _usernameRow, _passwordRow;
    private readonly GlassButton _signInButton = new() { Text = "Sign in" };

    private StaffLoginForm(BookingStore store, string? prefillUsername, bool standalone)
    {
        _store = store;
        Text = "SkyWings · Sign in";
        // Standalone = the very first screen of the program, with no window behind it.
        StartPosition = standalone ? FormStartPosition.CenterScreen : FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = standalone;
        ShowInTaskbar = standalone;
        AutoScaleMode = AutoScaleMode.None;
        int D(int px) => Gfx.S(this, px);
        ClientSize = new Size(D(760), D(460));

        _usernameRow = new FieldRow("USERNAME", _username);
        _passwordRow = new FieldRow("PASSWORD", _password);
        _username.Text = prefillUsername ?? "";
        _username.MaxLength = 40;
        _password.MaxLength = PasswordPolicy.MaxLength;
        _username.TextChanged += (_, _) => _passwordRow.ClearMessage();
        _password.TextChanged += (_, _) => _passwordRow.ClearMessage();

        // Show/hide toggle sits in the caption row of the password field, so it is reachable by keyboard and mouse.
        var showToggle = new GlassButton
        {
            Text = "Show", Style = GlassButtonStyle.Ghost, Font = new Font(Theme.Small, FontStyle.Bold),
            Size = new Size(D(56), D(24)), AccessibleName = "Show or hide password", Margin = Padding.Empty
        };
        showToggle.Click += (_, _) =>
        {
            _password.UseSystemPasswordChar = !_password.UseSystemPasswordChar;
            showToggle.Text = _password.UseSystemPasswordChar ? "Show" : "Hide";
        };
        _passwordRow.Controls.Add(showToggle);
        showToggle.BringToFront();
        _passwordRow.Resize += (_, _) => showToggle.Location = new Point(_passwordRow.Width - showToggle.Width, 0);

        _password.KeyUp += (_, _) => UpdateCapsWarning();
        _password.GotFocus += (_, _) => UpdateCapsWarning();

        var brand = new LoginBrandPanel { Dock = DockStyle.Left, Width = D(300) };

        var title = new Label { Text = "Welcome back", Font = new Font("Segoe UI", 20f, FontStyle.Bold), AutoSize = false, Height = D(46), TextAlign = ContentAlignment.MiddleLeft };
        var subtitle = new Label
        {
            Text = "Sign in with your staff account.", Font = Theme.Body, ForeColor = Theme.TextSecondary,
            AutoSize = false, Height = D(30), TextAlign = ContentAlignment.TopLeft
        };
        var hint = new Label
        {
            Text = "Everything you do is recorded.", Font = Theme.Small, ForeColor = Theme.TextSecondary,
            AutoSize = false, Height = D(24), Dock = DockStyle.Bottom, TextAlign = ContentAlignment.BottomLeft
        };

        _signInButton.Height = D(44);
        _signInButton.Margin = Padding.Empty;
        _signInButton.Click += async (_, _) => await Authenticate();
        var cancel = new GlassButton
        {
            Text = standalone ? "Exit" : "Cancel", Style = GlassButtonStyle.Ghost, DialogResult = DialogResult.Cancel,
            Height = D(36), Margin = Padding.Empty
        };
        Control Spacer(int px) => new Panel { Height = D(px), BackColor = Color.Transparent };

        var form = new Panel { Dock = DockStyle.Fill, Padding = new Padding(D(44), D(34), D(44), D(24)) };
        form.Controls.Add(hint);
        Layouts.Stack(form, title, subtitle, Spacer(10), _usernameRow, _passwordRow, Spacer(6), _signInButton, Spacer(6), cancel);

        Controls.Add(form);
        Controls.Add(brand);

        Theme.Apply(this);
        AcceptButton = _signInButton;
        CancelButton = cancel;
        Shown += (_, _) => (_username.Text.Length == 0 ? _username : _password).Focus();
    }

    private const string CapsMessage = "Caps Lock is on.";

    /// <summary>Warns under the password box while Caps Lock is on, without hiding a real error message.</summary>
    private void UpdateCapsWarning()
    {
        bool caps = Control.IsKeyLocked(Keys.CapsLock);
        if (caps && _passwordRow.Message.Length == 0) _passwordRow.SetMessage(CapsMessage, isError: false);
        else if (!caps && _passwordRow.Message == CapsMessage) _passwordRow.ClearMessage();
    }

    /// <summary>The account that signed in when the dialog returns OK.</summary>
    public StaffSession? Session { get; private set; }

    /// <summary>Runs the full staff sign-in. Returns the session, or null when the person cancelled or could not sign in.</summary>
    public static StaffSession? SignIn(IWin32Window? owner, BookingStore store)
    {
        string? prefill = null;
        try
        {
            if (store.CountStaff() == 0)
            {
                if (AppConfig.ImportLegacyAdmin(store))
                {
                    MessageBox.Show(owner,
                        "The admin account from your settings file has moved into the new staff accounts.\n\n" +
                        "Sign in with your current admin password. You will then be asked to choose a new one.",
                        "Staff accounts", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    prefill = store.LoadStaff().FirstOrDefault()?.Username;
                }
                else
                {
                    using var first = new FirstRunAdminForm(store);
                    if (first.ShowDialog(owner) != DialogResult.OK) return null;
                    prefill = first.CreatedUsername;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException)
        {
            AppLogger.Error("StaffSetup", ex);
            MessageBox.Show(owner, "Staff accounts could not be set up.\n\n" + ex.Message, "Staff accounts",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
            return null;
        }

        using var login = new StaffLoginForm(store, prefill, standalone: owner is null);
        if (login.ShowDialog(owner) != DialogResult.OK || login.Session is null) return null;

        StaffSession session = login.Session;
        if (session.MustChangePassword)
        {
            using var change = new ChangePasswordForm(new AdminService(store, session), forced: true);
            if (change.ShowDialog(owner) != DialogResult.OK)
            {
                store.WriteAudit(session.Username, RolePermissions.ToDb(session.Role), "SIGN_OUT", "", "Did not set a new password");
                return null;
            }
        }
        return session;
    }

    private async Task Authenticate()
    {
        // Password hashing is deliberately slow; run it off the UI thread so the dialog doesn't freeze.
        string username = _username.Text, password = _password.Text;
        AuthResult result;
        _signInButton.Enabled = false;
        _signInButton.Text = "Signing in…";
        UseWaitCursor = true;
        try
        {
            result = await Task.Run(() => _store.Authenticate(username, password));
        }
        catch (IOException ex)
        {
            AppLogger.Error("StaffSignIn", ex);
            _passwordRow.SetMessage("Sign-in is unavailable right now.");
            return;
        }
        finally
        {
            UseWaitCursor = false;
            _signInButton.Text = "Sign in";
            _signInButton.Enabled = true;
        }

        if (!result.Success)
        {
            // Clear first: emptying the box fires TextChanged, which would erase the message if it were set before.
            _password.Clear();
            _passwordRow.SetMessage(result.Message);
            _password.Focus();
            return;
        }

        Session = result.Session;
        DialogResult = DialogResult.OK;
        Close();
    }
}
