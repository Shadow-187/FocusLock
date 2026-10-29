namespace FocusLock;

/// <summary>
/// Quiz about one session. Two modes:
///  - Claude mode: multiple-choice questions generated from the topic and your notes
///  - Recall mode (no API key or no internet): write what you remember, then compare with your notes
/// The window cannot be closed until the quiz is finished.
/// </summary>
public class QuizForm : Form
{
    private readonly SessionLog _session;
    private readonly AppSettings _settings;
    private readonly bool _isReview;

    private readonly Label _header = new();
    private readonly Label _question = new();
    private readonly RadioButton[] _options = new RadioButton[4];
    private readonly TextBox _recall = new();
    private readonly Label _feedback = new();
    private readonly Button _main = new();
    private readonly Button[] _selfGrade = new Button[3];

    private List<QuizQuestion> _questions = new();
    private int _index;
    private int _score;
    private bool _answered;
    private bool _done;

    public QuizForm(SessionLog session, AppSettings settings, bool isReview)
    {
        _session = session;
        _settings = settings;
        _isReview = isReview;

        Text = isReview ? "Warm-up: last session" : "Quiz: today's session";
        Font = new Font("Segoe UI", 10.5f);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        TopMost = true;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(640, 520);
        BackColor = Color.FromArgb(24, 26, 32);
        ForeColor = Color.Gainsboro;

        _header.Bounds = new Rectangle(20, 14, 600, 26);
        _header.ForeColor = Color.FromArgb(162, 230, 53);
        _header.Font = new Font("Segoe UI Semibold", 11f);
        Controls.Add(_header);

        _question.Bounds = new Rectangle(20, 46, 600, 84);
        _question.Font = new Font("Segoe UI Semibold", 11.5f);
        Controls.Add(_question);

        for (int i = 0; i < 4; i++)
        {
            _options[i] = new RadioButton
            {
                Bounds = new Rectangle(24, 136 + i * 52, 596, 48),
                AutoSize = false,
                ForeColor = Color.White,
                Visible = false
            };
            Controls.Add(_options[i]);
        }

        _recall.Bounds = new Rectangle(20, 136, 600, 160);
        _recall.Multiline = true;
        _recall.AcceptsReturn = true;
        _recall.ScrollBars = ScrollBars.Vertical;
        _recall.BackColor = Color.FromArgb(40, 43, 52);
        _recall.ForeColor = Color.White;
        _recall.BorderStyle = BorderStyle.FixedSingle;
        _recall.Visible = false;
        Controls.Add(_recall);

        _feedback.Bounds = new Rectangle(20, 346, 600, 110);
        Controls.Add(_feedback);

        _main.Bounds = new Rectangle(470, 466, 150, 38);
        _main.FlatStyle = FlatStyle.Flat;
        _main.BackColor = Color.FromArgb(162, 230, 53);
        _main.ForeColor = Color.Black;
        _main.FlatAppearance.BorderSize = 0;
        _main.Click += (_, _) => MainClicked();
        Controls.Add(_main);

        string[] grades = { "Remembered it", "Partly", "Forgot it" };
        for (int i = 0; i < 3; i++)
        {
            int points = 2 - i;
            _selfGrade[i] = new Button
            {
                Text = grades[i],
                Bounds = new Rectangle(20 + i * 150, 466, 140, 38),
                FlatStyle = FlatStyle.Flat,
                ForeColor = Color.White,
                Visible = false
            };
            _selfGrade[i].Click += (_, _) => FinishRecall(points);
            Controls.Add(_selfGrade[i]);
        }

        Shown += async (_, _) => await LoadQuizAsync();
    }

    private string Which => _isReview ? "last session" : "today";

    private async Task LoadQuizAsync()
    {
        _header.Text = $"{_session.Topic} ({Which})";
        _main.Enabled = false;

        if (QuizGenerator.GetApiKey(_settings) == null)
        {
            StartRecall("No API key set, so this is a recall quiz.");
            return;
        }

        _question.Text = "Claude is writing your quiz...";
        try
        {
            _questions = await QuizGenerator.GenerateAsync(_session, _settings);
            ShowQuestion();
        }
        catch (Exception ex)
        {
            StartRecall($"Couldn't reach Claude ({Short(ex.Message)}), so this is a recall quiz.");
        }
    }

    // ---------- Multiple-choice mode ----------

    private void ShowQuestion()
    {
        var q = _questions[_index];
        _answered = false;
        _header.Text = $"{_session.Topic} ({Which})  ·  Question {_index + 1} of {_questions.Count}";
        _question.Text = q.Question;
        _feedback.Text = "";

        for (int i = 0; i < 4; i++)
        {
            _options[i].Visible = i < q.Options.Count;
            _options[i].Checked = false;
            _options[i].Enabled = true;
            _options[i].ForeColor = Color.White;
            if (i < q.Options.Count) _options[i].Text = q.Options[i];
        }

        _main.Text = "Check";
        _main.Enabled = true;
    }

    private void MainClicked()
    {
        if (_done) { Close(); return; }
        if (_questions.Count == 0) { RevealNotes(); return; }   // recall mode

        if (!_answered)
        {
            int picked = Array.FindIndex(_options, o => o.Checked);
            if (picked < 0) { _feedback.Text = "Pick an answer first."; return; }

            var q = _questions[_index];
            bool right = picked == q.Answer;
            if (right) _score++;

            foreach (var o in _options) o.Enabled = false;
            _options[q.Answer].ForeColor = Color.FromArgb(162, 230, 53);
            if (!right) _options[picked].ForeColor = Color.IndianRed;

            _feedback.ForeColor = right ? Color.FromArgb(162, 230, 53) : Color.IndianRed;
            _feedback.Text = (right ? "Correct! " : "Not quite. ") + q.Explanation;

            _answered = true;
            _main.Text = _index + 1 < _questions.Count ? "Next" : "See score";
            return;
        }

        _index++;
        if (_index < _questions.Count) ShowQuestion();
        else ShowResult(_score, _questions.Count);
    }

    // ---------- Recall mode ----------

    private void StartRecall(string reason)
    {
        _header.Text = $"{_session.Topic} ({Which})  ·  Recall";
        _question.Text = $"Without looking: what do you remember about \"{_session.Topic}\"? " +
                         "Write the key ideas, commands and why they work.";
        _feedback.ForeColor = Color.DarkGray;
        _feedback.Text = reason;
        _recall.Visible = true;
        _recall.Focus();
        _main.Text = "Compare with notes";
        _main.Enabled = true;
    }

    private void RevealNotes()
    {
        if (_recall.Text.Trim().Length < 10)
        {
            _feedback.ForeColor = Color.IndianRed;
            _feedback.Text = "Write a bit more first. Even a few keywords count.";
            return;
        }

        _recall.ReadOnly = true;
        _feedback.ForeColor = Color.Gainsboro;
        _feedback.Text = "Your notes from that session:\n" +
            (string.IsNullOrWhiteSpace(_session.Learned)
                ? "(You didn't write notes. Open the TryHackMe room and skim the tasks to check.)"
                : _session.Learned) +
            "\n\nHow did you do?";
        _main.Visible = false;
        foreach (var b in _selfGrade) b.Visible = true;
    }

    private void FinishRecall(int points)
    {
        foreach (var b in _selfGrade) b.Visible = false;
        _main.Visible = true;
        ShowResult(points, 2);
    }

    // ---------- Result ----------

    private void ShowResult(int score, int total)
    {
        Storage.SetQuizScore(_session.StartUtc, score, total, _isReview);

        foreach (var o in _options) o.Visible = false;
        _recall.Visible = false;

        double pct = total == 0 ? 0 : (double)score / total;
        _header.Text = $"{_session.Topic} ({Which})  ·  Done";
        _question.Text = $"Score: {score} / {total}";
        _feedback.ForeColor = Color.Gainsboro;
        _feedback.Text = pct switch
        {
            >= 0.8 => "Strong. This one is sticking.",
            >= 0.5 => "Decent. Reread the parts you missed in your notes.",
            _ => "Worth redoing the room's tasks once more before moving on."
        };

        _done = true;
        _main.Text = _isReview ? "Start studying" : "Close";
        _main.Enabled = true;
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_done && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            _feedback.ForeColor = Color.IndianRed;
            _feedback.Text = "Finish the quiz first. It only takes a few minutes.";
            return;
        }
        base.OnFormClosing(e);
    }

    private static string Short(string s) => s.Length <= 60 ? s : s[..60] + "...";
}
