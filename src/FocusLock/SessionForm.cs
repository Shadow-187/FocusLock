namespace FocusLock;

/// <summary>
/// Small always-on-top countdown in the corner of the screen. While it runs it keeps closing
/// blocked apps. It refuses to close until the time is up (or the emergency phrase is typed).
/// </summary>
public class SessionForm : Form
{
    private const string EmergencyPhrase = "I am choosing to quit my study session early";

    private readonly ActiveSession _session;
    private readonly AppSettings _settings;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    private readonly Label _countdown = new();
    private readonly Label _status = new();
    private readonly ProgressBar _progress = new();
    private int _ticks;
    private bool _allowClose;

    public SessionForm(ActiveSession session, AppSettings settings, string? startupWarning)
    {
        _session = session;
        _settings = settings;

        Text = "FocusLock – studying";
        Font = new Font("Segoe UI", 10f);
        FormBorderStyle = FormBorderStyle.FixedToolWindow;
        TopMost = true;
        ShowInTaskbar = true;
        StartPosition = FormStartPosition.Manual;
        ClientSize = new Size(340, 190);
        BackColor = Color.FromArgb(24, 26, 32);
        ForeColor = Color.Gainsboro;

        var area = Screen.PrimaryScreen!.WorkingArea;
        Location = new Point(area.Right - Width - 16, area.Bottom - Height - 16);

        Controls.Add(new Label
        {
            Text = session.Topic,
            AutoEllipsis = true,
            Bounds = new Rectangle(14, 10, 312, 24),
            ForeColor = Color.DarkGray
        });

        _countdown.Font = new Font("Consolas", 34f, FontStyle.Bold);
        _countdown.ForeColor = Color.FromArgb(162, 230, 53);
        _countdown.Bounds = new Rectangle(10, 34, 320, 60);
        _countdown.TextAlign = ContentAlignment.MiddleCenter;
        Controls.Add(_countdown);

        _progress.Bounds = new Rectangle(14, 100, 312, 10);
        _progress.Maximum = 1000;
        Controls.Add(_progress);

        _status.Bounds = new Rectangle(14, 116, 312, 40);
        _status.ForeColor = Color.DarkGray;
        _status.Text = startupWarning ?? "Locked in. Apps and sites are blocked.";
        Controls.Add(_status);

        var emergency = new LinkLabel
        {
            Text = "Emergency stop",
            AutoSize = true,
            Location = new Point(14, 160),
            LinkColor = Color.IndianRed
        };
        emergency.LinkClicked += (_, _) => EmergencyStop();
        Controls.Add(emergency);

        _timer.Tick += (_, _) => Tick();
        Load += (_, _) => { Tick(); _timer.Start(); };
    }

    private void Tick()
    {
        var left = _session.EndUtc - DateTime.UtcNow;
        if (left <= TimeSpan.Zero)
        {
            Finish();
            return;
        }

        _countdown.Text = left.TotalHours >= 1
            ? left.ToString(@"h\:mm\:ss")
            : left.ToString(@"mm\:ss");

        var total = (_session.EndUtc - _session.StartUtc).TotalSeconds;
        var done = (DateTime.UtcNow - _session.StartUtc).TotalSeconds;
        _progress.Value = (int)Math.Clamp(done / total * 1000, 0, 1000);

        // Sweep for blocked apps every 2 seconds
        if (_ticks++ % 2 == 0)
        {
            var closed = Blocker.CloseBlockedApps(_settings.BlockedApps);
            if (closed.Count > 0)
                _status.Text = $"Closed {string.Join(", ", closed.Distinct())} at {DateTime.Now:HH:mm}. Back to work!";
        }
    }

    private void Finish()
    {
        _timer.Stop();
        Blocker.UnblockSites();
        Storage.ClearSession();

        var minutes = (int)Math.Round((_session.EndUtc - _session.StartUtc).TotalMinutes);
        var learned = Prompt.Ask(this, "Session complete!",
            $"Nice work, {minutes} minutes done.\n\nWhat did you learn? Write the key ideas and commands. " +
            "Your quiz now, and the warm-up next time, are built from this.", multiline: true);

        var log = new SessionLog
        {
            StartUtc = _session.StartUtc,
            Minutes = minutes,
            Topic = _session.Topic,
            Learned = learned ?? "",
            Completed = true
        };
        Storage.AddToHistory(log);

        // Quiz on what you just studied
        Hide();
        using (var quiz = new QuizForm(log, _settings, isReview: false))
            quiz.ShowDialog();

        _allowClose = true;
        Close();
    }

    private void EmergencyStop()
    {
        var typed = Prompt.Ask(this, "Emergency stop",
            $"To stop early, type exactly:\n\n{EmergencyPhrase}");
        if (typed == null) return;

        if (!string.Equals(typed.Trim(), EmergencyPhrase, StringComparison.Ordinal))
        {
            MessageBox.Show(this, "That doesn't match. Keep going, you've got this.",
                "FocusLock", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _timer.Stop();
        Blocker.UnblockSites();
        Storage.ClearSession();
        Storage.AddToHistory(new SessionLog
        {
            StartUtc = _session.StartUtc,
            Minutes = (int)(DateTime.UtcNow - _session.StartUtc).TotalMinutes,
            Topic = _session.Topic,
            Completed = false
        });

        _allowClose = true;
        Close();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // Let Windows shut down / log off; the session is saved and resumes on next start.
        if (!_allowClose && e.CloseReason != CloseReason.WindowsShutDown
                         && e.CloseReason != CloseReason.TaskManagerClosing)
        {
            e.Cancel = true;
            var left = _session.EndUtc - DateTime.UtcNow;
            _status.Text = $"Nope. {Math.Ceiling(left.TotalMinutes)} min left. Stay with it.";
            return;
        }
        base.OnFormClosing(e);
    }
}

/// <summary>Tiny text-input dialog (WinForms has none built in).</summary>
public static class Prompt
{
    public static string? Ask(IWin32Window owner, string title, string message, bool multiline = false)
    {
        using var form = new Form
        {
            Text = title,
            Font = new Font("Segoe UI", 10f),
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterScreen,
            MinimizeBox = false,
            MaximizeBox = false,
            TopMost = true,
            ClientSize = new Size(440, multiline ? 330 : 200)
        };
        int inputHeight = multiline ? 158 : 28;
        var label = new Label { Text = message, Bounds = new Rectangle(14, 12, 412, 100) };
        var input = new TextBox
        {
            Bounds = new Rectangle(14, 116, 412, inputHeight),
            Multiline = multiline,
            AcceptsReturn = multiline,
            ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None
        };
        int buttonsY = 116 + inputHeight + 12;
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Bounds = new Rectangle(250, buttonsY, 85, 32) };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Bounds = new Rectangle(341, buttonsY, 85, 32) };
        form.Controls.AddRange(new Control[] { label, input, ok, cancel });
        if (!multiline) form.AcceptButton = ok;   // Enter adds a new line in multiline mode
        form.CancelButton = cancel;

        return form.ShowDialog(owner) == DialogResult.OK ? input.Text : null;
    }
}
