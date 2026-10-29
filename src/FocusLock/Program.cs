namespace FocusLock;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Only one FocusLock at a time
        using var mutex = new Mutex(true, "FocusLock_SingleInstance", out bool isFirst);
        if (!isFirst) return;

        ApplicationConfiguration.Initialize();

        var settings = Storage.LoadSettings();
        var session = Storage.LoadSession();

        // A session left over from a crash, restart or Task Manager kill:
        if (session != null && session.EndUtc <= DateTime.UtcNow)
        {
            // Time ran out while the app was closed. Clean up, but don't count it as completed.
            Blocker.UnblockSites();
            Storage.ClearSession();
            session = null;
        }

        bool isNewSession = session == null;
        if (session == null)
        {
            // Make sure no old block is hanging around before showing the start screen
            Blocker.UnblockSites();

            var setup = new SetupForm(settings);
            Application.Run(setup);
            session = setup.StartedSession;
            if (session == null) return; // closed without starting
        }

        // Start (or resume) the lock
        string? warning = null;
        if (settings.BlockedSites.Count > 0 && !Blocker.BlockSites(settings.BlockedSites, out var error))
            warning = "Couldn't block websites (run as administrator). Apps are still blocked.";

        // Warm-up first: quiz on your last session before you learn anything new.
        // (Skipped when resuming a session that was already running.)
        if (isNewSession)
        {
            var last = Storage.LastCompleted();
            if (last != null)
            {
                using var review = new QuizForm(last, settings, isReview: true);
                Application.Run(review);
            }
        }

        Application.Run(new SessionForm(session, settings, warning));
    }
}
