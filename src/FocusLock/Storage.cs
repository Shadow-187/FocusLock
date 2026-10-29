using System.Text.Json;
using System.Text.Json.Serialization;

namespace FocusLock;

/// <summary>What the user configured: what to block and how long a session lasts.</summary>
public class AppSettings
{
    public int SessionMinutes { get; set; } = 60;

    // Optional Claude API key for generated quizzes. If empty, the ANTHROPIC_API_KEY
    // environment variable is used; if that is empty too, you get a recall quiz instead.
    // Only the DPAPI-encrypted version is written to settings.json.
    public string ApiKeyProtected { get; set; } = "";

    /// <summary>The decrypted key, in memory only. Setting it encrypts it right away.</summary>
    [JsonIgnore]
    public string ApiKey
    {
        get => Secrets.Unprotect(ApiKeyProtected);
        set => ApiKeyProtected = Secrets.Protect(value);
    }

    /// <summary>
    /// Old versions saved the key in plain text as "ApiKey". This reads it once, encrypts it,
    /// and is never written back (the getter returns null), so the plain text disappears
    /// the next time settings are saved.
    /// </summary>
    [JsonPropertyName("ApiKey")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyPlainApiKey
    {
        get => null;
        set
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            ApiKey = value;
            NeedsResave = true;
        }
    }

    [JsonIgnore] public bool NeedsResave { get; set; }
    public string QuizModel { get; set; } = "claude-haiku-5-5";
    public int QuizQuestions { get; set; } = 6;

    // Process names WITHOUT ".exe". Matching is case-insensitive.
    public List<string> BlockedApps { get; set; } = new()
    {
        "Discord", "steam", "steamwebhelper", "EpicGamesLauncher", "Battle.net",
        "RiotClientServices", "LeagueClient", "WhatsApp", "Telegram"
    };

    // Domains to block through the hosts file. "www." is added automatically.
    public List<string> BlockedSites { get; set; } = new()
    {
        "youtube.com", "tiktok.com", "instagram.com", "facebook.com", "x.com",
        "twitter.com", "reddit.com", "netflix.com", "twitch.tv", "9gag.com"
    };
}

/// <summary>A running session, saved to disk so closing the app does not end it.</summary>
public class ActiveSession
{
    public DateTime StartUtc { get; set; }
    public DateTime EndUtc { get; set; }
    public string Topic { get; set; } = "";
}

/// <summary>One finished (or abandoned) session, for the history and streak.</summary>
public class SessionLog
{
    public DateTime StartUtc { get; set; }
    public int Minutes { get; set; }
    public string Topic { get; set; } = "";
    public string Learned { get; set; } = "";
    public bool Completed { get; set; }
    public int QuizScore { get; set; }       // quiz right after the session
    public int QuizTotal { get; set; }
    public int ReviewScore { get; set; }     // warm-up quiz at the start of the NEXT session
    public int ReviewTotal { get; set; }
}

public static class Storage
{
    private static readonly string Dir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FocusLock");

    private static string SettingsPath => Path.Combine(Dir, "settings.json");
    private static string SessionPath => Path.Combine(Dir, "active-session.json");
    private static string LogPath => Path.Combine(Dir, "history.json");

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static AppSettings LoadSettings()
    {
        var s = Read<AppSettings>(SettingsPath) ?? new AppSettings();
        if (s.NeedsResave)
        {
            // Rewrite right away so the old plain-text key is gone from disk.
            Write(SettingsPath, s);
            s.NeedsResave = false;
        }
        return s;
    }
    public static void SaveSettings(AppSettings s) => Write(SettingsPath, s);

    public static ActiveSession? LoadSession() => Read<ActiveSession>(SessionPath);
    public static void SaveSession(ActiveSession s) => Write(SessionPath, s);
    public static void ClearSession() { try { File.Delete(SessionPath); } catch { } }

    public static List<SessionLog> LoadHistory() => Read<List<SessionLog>>(LogPath) ?? new();

    public static void AddToHistory(SessionLog entry)
    {
        var all = LoadHistory();
        all.Add(entry);
        Write(LogPath, all);
    }

    /// <summary>The most recent completed session (the one before the session that just ended).</summary>
    public static SessionLog? LastCompleted() =>
        LoadHistory().Where(h => h.Completed).OrderBy(h => h.StartUtc).LastOrDefault();

    /// <summary>Saves a quiz result on the session it belongs to.</summary>
    public static void SetQuizScore(DateTime sessionStartUtc, int score, int total, bool isReview)
    {
        var all = LoadHistory();
        var entry = all.FirstOrDefault(h => h.StartUtc == sessionStartUtc);
        if (entry == null) return;
        if (isReview) { entry.ReviewScore = score; entry.ReviewTotal = total; }
        else { entry.QuizScore = score; entry.QuizTotal = total; }
        Write(LogPath, all);
    }

    /// <summary>Minutes of completed study today (local time).</summary>
    public static int MinutesToday() =>
        LoadHistory().Where(h => h.StartUtc.ToLocalTime().Date == DateTime.Today)
                     .Sum(h => h.Minutes);

    /// <summary>Days in a row (ending today or yesterday) with at least one completed session.</summary>
    public static int Streak()
    {
        var days = LoadHistory().Where(h => h.Completed)
                                .Select(h => h.StartUtc.ToLocalTime().Date)
                                .ToHashSet();
        var day = days.Contains(DateTime.Today) ? DateTime.Today : DateTime.Today.AddDays(-1);
        int streak = 0;
        while (days.Contains(day)) { streak++; day = day.AddDays(-1); }
        return streak;
    }

    private static T? Read<T>(string path)
    {
        try { return File.Exists(path) ? JsonSerializer.Deserialize<T>(File.ReadAllText(path)) : default; }
        catch { return default; }
    }

    private static void Write<T>(string path, T value)
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(path, JsonSerializer.Serialize(value, Json));
    }
}
