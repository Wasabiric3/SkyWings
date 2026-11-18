
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
            // Emails the offer to the next waitlisted traveller whenever a cancellation or expiry frees seats.
            SkyWings.Email.EmailServices.CreateWaitlist(store).Attach();

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
