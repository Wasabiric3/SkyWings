
namespace SkyWings
{
    internal static class Program
    {
        /// <summary>
        ///  The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // To customize application configuration such as set high DPI settings or default font,
            // see https://aka.ms/applicationconfiguration.
            ApplicationConfiguration.Initialize();
            Application.ThreadException += (_, e) =>
            {
                AppLogger.Error("UI", e.Exception);
                MessageBox.Show("An unexpected error occurred. The details were written to the application log.",
                    "Unexpected error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                if (e.ExceptionObject is Exception exception) AppLogger.Error("Unhandled", exception);
            };

            var store = new BookingStore();
            // A maintenance switch (e.g. "--reset-simulation") does its job, says so, and exits without opening the app.
            if (CommandLine.Run(Environment.GetCommandLineArgs().Skip(1).ToArray(), store, DateTime.Now) is { } done)
            {
                MessageBox.Show(done, "SkyWings", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            // Emails the offer to the next waitlisted traveller whenever a cancellation or expiry frees seats.
            SkyWings.Email.EmailServices.CreateWaitlist(store).Attach();
            // Emails the customer a cancellation notice (with the refund reference) whenever a booking is cancelled.
            SkyWings.Email.EmailServices.Create(store).Attach();
            // Reads refund request emails from the desk mailbox every minute (read-only) and writes them into the Refunds tab.
            if (AppConfig.Imap.IsConfigured)
            {
                var mailPoller = new SkyWings.Email.RefundMailPoller(store, new SkyWings.Email.ImapRefundMailSource(AppConfig.Imap), TimeSpan.FromSeconds(60));
                SkyWings.Email.EmailServices.RefundMail = mailPoller;
                mailPoller.Start();
            }

            // The dashboard is only reachable after a staff sign-in. "Sign out" (or the idle timeout) brings the
            // sign-in screen back; closing the sign-in screen or the dashboard window quits the program.
            while (true)
            {
                StaffSession? session = StaffLoginForm.SignIn(null, store);
                if (session is null) return;

                using var home = new HomeForm(store, session);
                Application.Run(home);
                if (!home.SignedOut) return;
            }
        }
    }
}
