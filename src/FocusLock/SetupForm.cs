namespace FocusLock;

/// <summary>Start screen: pick a topic and a duration, edit the block lists, press Start.</summary>
public class SetupForm : Form
{
    private readonly AppSettings _settings;
    private readonly TextBox _topic = new();
    private readonly NumericUpDown _minutes = new();
    private readonly TextBox _apps = new();
    private readonly TextBox _sites = new();
    private readonly TextBox _apiKey = new();

    /// <summary>Set when the user starts a session; Program then opens the session window.</summary>
    public ActiveSession? StartedSession { get; private set; }

    public SetupForm(AppSettings settings)
    {
        _settings = settings;

        Text = "FocusLock";
        Font = new Font("Segoe UI", 10f);
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(560, 700);
        BackColor = Color.FromArgb(24, 26, 32);
        ForeColor = Color.Gainsboro;

        var y = 18;

        Add(new Label
        {
            Text = "Study session",
            Font = new Font("Segoe UI Semibold", 18f),
            ForeColor = Color.FromArgb(162, 230, 53),
            AutoSize = true,
            Location = new Point(20, y)
        });
        y += 48;

        Add(new Label
        {
            Text = $"Today: {Storage.MinutesToday()} min studied   ·   Streak: {Storage.Streak()} day(s)",
            AutoSize = true,
            ForeColor = Color.DarkGray,
            Location = new Point(22, y)
        });
        y += 38;

        Add(Caption("What are you studying? (e.g. \"Nmap Basic Port Scans\")", y));
        y += 24;
        _topic.SetBounds(22, y, 516, 28);
        Style(_topic);
        Add(_topic);
        y += 44;

        Add(Caption("Minutes:", y + 4));
        _minutes.SetBounds(100, y, 80, 28);
        _minutes.Minimum = 15;
        _minutes.Maximum = 240;
        _minutes.Increment = 15;
        _minutes.Value = Math.Clamp(settings.SessionMinutes, 15, 240);
        _minutes.BackColor = Color.FromArgb(40, 43, 52);
        _minutes.ForeColor = Color.White;
        Add(_minutes);
        y += 48;

        Add(Caption("Claude API key for quizzes (optional, starts with sk-ant-):", y));
        y += 24;
        _apiKey.SetBounds(22, y, 516, 28);
        _apiKey.UseSystemPasswordChar = true;
        _apiKey.Text = settings.ApiKey;
        Style(_apiKey);
        Add(_apiKey);
        y += 30;
        Add(new Label
        {
            Text = "Without a key you still get a recall quiz built from your own notes.",
            AutoSize = true,
            ForeColor = Color.DarkGray,
            Location = new Point(22, y)
        });
        y += 34;

        Add(Caption("Apps to close (process name, one per line):", y));
        Add(Caption("Websites to block (one per line):", y, 290));
        y += 24;

        _apps.SetBounds(22, y, 250, 300);
        _apps.Multiline = true;
        _apps.AcceptsReturn = true;
        _apps.ScrollBars = ScrollBars.Vertical;
        _apps.Text = string.Join(Environment.NewLine, settings.BlockedApps);
        Style(_apps);
        Add(_apps);

        _sites.SetBounds(288, y, 250, 300);
        _sites.Multiline = true;
        _sites.AcceptsReturn = true;
        _sites.ScrollBars = ScrollBars.Vertical;
        _sites.Text = string.Join(Environment.NewLine, settings.BlockedSites);
        Style(_sites);
        Add(_sites);
        y += 316;

        var start = new Button
        {
            Text = "Start and lock",
            Font = new Font("Segoe UI Semibold", 11f),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(162, 230, 53),
            ForeColor = Color.Black,
            Bounds = new Rectangle(22, y, 516, 44)
        };
        start.FlatAppearance.BorderSize = 0;
        start.Click += (_, _) => Start();
        Add(start);

        AcceptButton = start;
    }

    private void Start()
    {
        if (string.IsNullOrWhiteSpace(_topic.Text))
        {
            MessageBox.Show(this, "Write what you are going to study first.\nA clear goal makes the hour count.",
                "FocusLock", MessageBoxButtons.OK, MessageBoxIcon.Information);
            _topic.Focus();
            return;
        }

        _settings.SessionMinutes = (int)_minutes.Value;
        var apps = Lines(_apps.Text);
        var sites = Lines(_sites.Text);
        var removed = apps.Concat(sites).Where(Blocker.IsProtected).ToList();
        _settings.BlockedApps = apps.Where(a => !Blocker.IsProtected(a)).ToList();
        _settings.BlockedSites = sites.Where(s => !Blocker.IsProtected(s)).ToList();
        _settings.ApiKey = _apiKey.Text.Trim();
        Storage.SaveSettings(_settings);

        if (removed.Count > 0)
        {
            MessageBox.Show(this,
                "These are never blocked, so they were removed from your lists:\n\n" +
                string.Join("\n", removed) +
                "\n\nClaude, TryHackMe and your lab tools always stay available.",
                "FocusLock", MessageBoxButtons.OK, MessageBoxIcon.Information);
            _apps.Text = string.Join(Environment.NewLine, _settings.BlockedApps);
            _sites.Text = string.Join(Environment.NewLine, _settings.BlockedSites);
        }

        var answer = MessageBox.Show(this,
            $"Lock for {_minutes.Value} minutes?\n\n" +
            "Blocked apps will be closed and blocked sites won't load.\n" +
            "You can't stop early without the emergency phrase.",
            "Ready?", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer != DialogResult.Yes) return;

        var now = DateTime.UtcNow;
        StartedSession = new ActiveSession
        {
            StartUtc = now,
            EndUtc = now.AddMinutes((double)_minutes.Value),
            Topic = _topic.Text.Trim()
        };
        Storage.SaveSession(StartedSession);
        Close();
    }

    private static List<string> Lines(string text) =>
        text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).Distinct().ToList();

    private void Add(Control c) => Controls.Add(c);

    private static Label Caption(string text, int y, int x = 22) =>
        new() { Text = text, AutoSize = true, Location = new Point(x, y) };

    private static void Style(TextBox t)
    {
        t.BackColor = Color.FromArgb(40, 43, 52);
        t.ForeColor = Color.White;
        t.BorderStyle = BorderStyle.FixedSingle;
    }
}
