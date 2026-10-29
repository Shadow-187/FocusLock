using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace FocusLock;

public class QuizQuestion
{
    public string Question { get; set; } = "";
    public List<string> Options { get; set; } = new();
    public int Answer { get; set; }            // index into Options
    public string Explanation { get; set; } = "";
}

/// <summary>
/// Asks Claude to write multiple-choice questions about one study session, based on
/// the topic and the notes you wrote when it ended.
/// </summary>
public static class QuizGenerator
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    public static string? GetApiKey(AppSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.ApiKey)) return settings.ApiKey.Trim();
        var env = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        return string.IsNullOrWhiteSpace(env) ? null : env.Trim();
    }

    /// <summary>Returns questions, or throws if there is no key / no internet / a bad reply.</summary>
    public static async Task<List<QuizQuestion>> GenerateAsync(SessionLog session, AppSettings settings)
    {
        var key = GetApiKey(settings) ?? throw new InvalidOperationException("No API key set.");

        var prompt =
            $"You are a cybersecurity tutor. A student just studied this TryHackMe topic:\n\n" +
            $"Topic: {session.Topic}\n" +
            $"Student's own notes: {(string.IsNullOrWhiteSpace(session.Learned) ? "(none)" : session.Learned)}\n\n" +
            $"Write {settings.QuizQuestions} multiple-choice questions that test real understanding of this topic " +
            "(concepts, what a command or flag does, which technique fits a situation). Mix easy and harder ones. " +
            "Base them on the topic as taught in that TryHackMe room, and use the notes to focus on what the student covered. " +
            "Each question has exactly 4 options and one correct answer. Vary the position of the correct answer.\n\n" +
            "Reply with ONLY a JSON array, no other text, in this shape:\n" +
            "[{\"question\":\"...\",\"options\":[\"...\",\"...\",\"...\",\"...\"],\"answer\":0,\"explanation\":\"one or two sentences\"}]";

        var body = JsonSerializer.Serialize(new
        {
            model = settings.QuizModel,
            max_tokens = 3000,
            messages = new[] { new { role = "user", content = prompt } }
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.anthropic.com/v1/messages")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        request.Headers.Add("x-api-key", key);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using var response = await Http.SendAsync(request);
        var raw = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Claude API error {(int)response.StatusCode}: {raw}");

        // The reply text is in content[0].text
        using var doc = JsonDocument.Parse(raw);
        var text = doc.RootElement.GetProperty("content")[0].GetProperty("text").GetString() ?? "";

        return Parse(text);
    }

    /// <summary>Pulls the JSON array out of the reply and checks every question is usable.</summary>
    public static List<QuizQuestion> Parse(string text)
    {
        int start = text.IndexOf('['), end = text.LastIndexOf(']');
        if (start < 0 || end <= start) throw new InvalidOperationException("No quiz found in the reply.");

        var questions = JsonSerializer.Deserialize<List<QuizQuestion>>(
            text[start..(end + 1)],
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();

        var valid = questions.Where(q => !string.IsNullOrWhiteSpace(q.Question)
                                         && q.Options.Count >= 2
                                         && q.Answer >= 0 && q.Answer < q.Options.Count).ToList();
        if (valid.Count == 0) throw new InvalidOperationException("The quiz came back empty.");
        return valid;
    }
}
