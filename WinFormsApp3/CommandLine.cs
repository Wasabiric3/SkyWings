namespace SkyWings;

/// <summary>
/// Maintenance switches given when starting SkyWings from a terminal. Not shown anywhere in the app:
/// <c>SkyWings.exe --reset-simulation</c> deletes the simulated traffic and generates it again (real bookings are never touched).
/// </summary>
internal static class CommandLine
{
    public const string ResetSimulation = "--reset-simulation";

    /// <summary><c>SkyWings.exe --make-superadmin &lt;username&gt;</c> makes an existing account a Super Admin; its password is not touched.</summary>
    public const string MakeSuperAdmin = "--make-superadmin";

    /// <summary><c>SkyWings.exe --rename-staff &lt;old&gt; &lt;new&gt;</c> changes an account's sign-in name; its password and role are not touched.</summary>
    public const string RenameStaff = "--rename-staff";

    /// <summary><c>SkyWings.exe --protect-smtp-password</c> replaces the plain <c>smtp.password</c> in db.properties with a
    /// version only this Windows user can read.</summary>
    public const string ProtectSmtpPassword = "--protect-smtp-password";

    /// <summary><c>SkyWings.exe --check-refund-mail</c> logs in to the desk mailbox and says how many refund emails it can see. Read-only: nothing is saved.</summary>
    public const string CheckRefundMail = "--check-refund-mail";

    /// <summary>Runs the switch in <paramref name="args"/> and returns what to tell the user, or null when there is none (start normally).</summary>
    public static string? Run(string[] args, BookingStore store, DateTime now)
    {
        if (args.Any(a => string.Equals(a.Trim(), ProtectSmtpPassword, StringComparison.OrdinalIgnoreCase)))
            return ProtectSmtpPasswordIn(AppConfig.PropertiesPath);
        if (args.Any(a => string.Equals(a.Trim(), CheckRefundMail, StringComparison.OrdinalIgnoreCase)))
            return CheckRefundMailbox(AppConfig.Imap);

        int rename = Array.FindIndex(args, a => string.Equals(a.Trim(), RenameStaff, StringComparison.OrdinalIgnoreCase));
        if (rename >= 0)
        {
            string oldName = rename + 1 < args.Length ? args[rename + 1] : "";
            string newName = rename + 2 < args.Length ? args[rename + 2] : "";
            try
            {
                var account = store.RenameStaff(oldName, newName);
                return $"The account is now called '{account.Username}'. Its password and role were not changed.";
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or ArgumentException)
            {
                AppLogger.Error("CommandLine", ex);
                return "Could not rename the account: " + ex.Message;
            }
        }
        int promote = Array.FindIndex(args, a => string.Equals(a.Trim(), MakeSuperAdmin, StringComparison.OrdinalIgnoreCase));
        if (promote >= 0)
        {
            string name = promote + 1 < args.Length ? args[promote + 1] : "";
            try
            {
                var account = store.PromoteToSuperAdmin(name);
                return $"'{account.Username}' is now a Super Admin. The password was not changed.";
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException)
            {
                AppLogger.Error("CommandLine", ex);
                return "Could not make a Super Admin: " + ex.Message;
            }
        }
        if (!args.Any(a => string.Equals(a.Trim(), ResetSimulation, StringComparison.OrdinalIgnoreCase))) return null;
        try
        {
            store.SeedDefaultFlights();   // a fresh database has no flights yet (the app normally seeds them on first use)
            var simulation = new SimulationService(store);
            simulation.MigrateIfNeeded();   // an older database: settings brought up to date, so the reset is not undone later
            int generated = simulation.Reset(now);
            // A neutral name: the staff Audit tab shows it.
            store.WriteAudit("command-line", "system", "MAINTENANCE_RESET", detail: $"{generated} flight dates regenerated");
            return $"Simulated bookings regenerated for {generated} flight dates.";
        }
        catch (IOException ex)   // e.g. the database is locked or unreadable
        {
            AppLogger.Error("CommandLine", ex);
            return "The reset could not be completed: " + ex.Message;
        }
    }

    /// <summary>Protects the SMTP password in the given settings file. Internal so the tests can use a temp file.</summary>
    internal static string ProtectSmtpPasswordIn(string? path)
    {
        if (path is null) return "There is no db.properties file, so there is nothing to protect.";
        try
        {
            var (lines, changed) = SecretProtector.ProtectSmtpPassword(File.ReadAllLines(path));
            if (!changed) return "No plain-text smtp.password was found in db.properties. Nothing was changed.";
            File.WriteAllLines(path, lines);
            AppLogger.Info("Security", "smtp.password in db.properties was encrypted for the current Windows user.");
            return "The SMTP password in db.properties is now encrypted for this Windows user. It will not work for other users or on another PC.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            AppLogger.Error("CommandLine", ex);
            return "Could not protect the password: " + ex.Message;
        }
    }

    /// <summary>Connects to the desk mailbox and reports how many refund request emails it can see (the last 14 days). Saves nothing.</summary>
    internal static string CheckRefundMailbox(SkyWings.Email.ImapSettings imap)
    {
        if (!imap.IsConfigured)
            return "Reading refund emails is not set up. It needs the mail settings (smtp.host, smtp.user, smtp.password) in db.properties, and imap.enabled must not be false.";
        try
        {
            SkyWings.Email.RefundMailBatch batch = new SkyWings.Email.ImapRefundMailSource(imap)
                .FetchAsync(0, DateTime.Now.AddDays(-14), 50).GetAwaiter().GetResult();
            var references = batch.Mails.Select(m => SkyWings.Email.RefundMailProcessor.ReferenceIn(m.Subject)).Where(r => r is not null).ToList();
            AppLogger.Info("RefundMail", $"Mailbox check: connected, {batch.Mails.Count} refund email(s) in the last 14 days.");
            return $"Connected to {imap.Host}. {batch.Mails.Count} refund email(s) in the last 14 days" +
                   (references.Count > 0 ? ": " + string.Join(", ", references) : "") + ". Nothing was saved or changed.";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppLogger.Error("RefundMail", ex);
            return "Could not read the mailbox: " + ex.Message;
        }
    }}
