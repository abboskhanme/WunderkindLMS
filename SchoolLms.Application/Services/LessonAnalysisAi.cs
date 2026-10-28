using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using SchoolLms.Domain;

namespace SchoolLms.Application.Services;

// ===========================================================================
//  Dars tahlili — AI summary (docs/modules/lesson-analysis.md §6).
//  A 1:1 port of the old app's backend/app/services/ai.py: same system prompt
//  (verbatim), same observation text, same Gemini REST call. Best-effort: every
//  failure comes back as an error string, never as an exception, so completing
//  an observation never depends on the provider being reachable.
// ===========================================================================

/// <summary>Exactly one of the two is set on a real outcome (the old <c>(summary, error)</c> tuple).</summary>
public sealed record LessonAiResult(string? Summary, string? Error);

/// <summary>One answered criterion as the prompt shows it.</summary>
public sealed record LessonAiDetail(string SectionTitle, string CriterionText, bool? IsDone, string? Comment);

/// <summary>Everything the prompt needs about one observation.</summary>
public sealed record LessonAiObservation(
    string ObservationId,
    string TeacherName,
    string? Subject,
    string? ClassName,
    DateOnly LessonDate,
    int ScoreDone,
    int ScoreTotal,
    decimal ScorePercent,
    IReadOnlyList<LessonAiDetail> Details,
    string? Strengths,
    string? GrowthAreas,
    string? NextLessonAdvice);

/// <summary>
/// The AI summariser. Replaceable in tests (a fake is registered there) — tests never
/// touch the network.
/// </summary>
public interface ILessonAnalysisAi
{
    /// <summary>Never throws: a failure is returned as <see cref="LessonAiResult.Error"/>.</summary>
    Task<LessonAiResult> SummarizeAsync(LessonAiObservation observation, CancellationToken ct = default);
}

/// <summary>Prompt texts — verbatim from ai.py.</summary>
public static class LessonAiPrompt
{
    public const string SystemPrompt = """
        Sen tajribali maktab metodisti va dars tahlilchisisan.
        Senga bitta dars kuzatuvi natijalari beriladi: har bir mezon "BAJARILDI" yoki
        "BAJARILMADI" deb belgilangan, ba'zilarida kuzatuvchining izohi bor.
        "BELGILANMAGAN" mezon darsda kuzatilmagan - uni yutuq ham, kamchilik ham deb hisoblama.

        Vazifang - shu tarqoq ma'lumotlardan yaxlit, xolis va foydali metodik xulosa yozish.

        Qoidalar:
        - Faqat o'zbek tilida (lotin alifbosida) yoz.
        - Faqat berilgan ma'lumotga tayan, hech narsa o'ylab topma.
        - Xushomad qilma, lekin hurmatli va rivojlantiruvchi ohangda yoz.
        - Tavsiyalar aniq va amaliy bo'lsin ("keyingi darsda ... qiling" ko'rinishida).
        - Javobni FAQAT quyidagi JSON formatida qaytar, boshqa hech narsa yozma:

        {
          "umumiy_xulosa": "3-5 gaplik umumiy baho",
          "kuchli_tomonlar": ["...", "..."],
          "rivojlantirish": ["...", "..."],
          "tavsiyalar": ["...", "..."]
        }
        """;

    /// <summary>Old <c>_build_observation_text</c>, line for line.</summary>
    public static string BuildObservationText(LessonAiObservation o)
    {
        var lines = new List<string>
        {
            $"O'qituvchi: {o.TeacherName}",
            $"Fan: {(string.IsNullOrEmpty(o.Subject) ? "-" : o.Subject)}",
            $"Sinf: {(string.IsNullOrEmpty(o.ClassName) ? "-" : o.ClassName)}",
            $"Sana: {o.LessonDate:yyyy-MM-dd}",
            $"Natija: {o.ScoreDone}/{o.ScoreTotal} ({LessonAnalysisScoring.FormatWhole((double)o.ScorePercent)}%)",
            "",
            "MEZONLAR:",
        };

        // Sections in order of first appearance (a dict in the old code).
        var bySection = new List<(string Title, List<string> Rows)>();
        foreach (var d in o.Details)
        {
            var mark = d.IsDone is null ? "BELGILANMAGAN" : d.IsDone.Value ? "BAJARILDI" : "BAJARILMADI";
            var row = $"  - [{mark}] {d.CriterionText}";
            if (!string.IsNullOrEmpty(d.Comment))
                row += $"\n      Kuzatuvchi izohi: {d.Comment}";
            var bucket = bySection.FindIndex(x => x.Title == d.SectionTitle);
            if (bucket < 0) bySection.Add((d.SectionTitle, [row]));
            else bySection[bucket].Rows.Add(row);
        }
        foreach (var (title, rows) in bySection)
        {
            lines.Add($"\n{title}");
            lines.AddRange(rows);
        }

        if (!string.IsNullOrEmpty(o.Strengths))
            lines.Add($"\nKUZATUVCHI QAYD ETGAN KUCHLI TOMONLAR:\n{o.Strengths}");
        if (!string.IsNullOrEmpty(o.GrowthAreas))
            lines.Add($"\nKUZATUVCHI QAYD ETGAN RIVOJLANISH SOHALARI:\n{o.GrowthAreas}");
        if (!string.IsNullOrEmpty(o.NextLessonAdvice))
            lines.Add($"\nKELGUSI DARS UCHUN TAVSIYA:\n{o.NextLessonAdvice}");

        return string.Join("\n", lines);
    }

    /// <summary>
    /// What gets stored as <c>ai_summary</c>: the JSON text the model returned (a fenced
    /// <c>```json</c> block is unwrapped). A reply that is not JSON is kept as raw prose rather
    /// than lost — the old app did the same.
    /// </summary>
    public static string NormalizeReply(string text)
    {
        var trimmed = text.Trim();
        var candidate = trimmed;
        if (candidate.StartsWith("```", StringComparison.Ordinal))
        {
            var firstNewLine = candidate.IndexOf('\n');
            var lastFence = candidate.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewLine > 0 && lastFence > firstNewLine)
                candidate = candidate[(firstNewLine + 1)..lastFence].Trim();
        }
        try
        {
            using var doc = JsonDocument.Parse(candidate);
            return doc.RootElement.ValueKind == JsonValueKind.Object ? candidate : trimmed;
        }
        catch (JsonException)
        {
            return trimmed;
        }
    }

    /// <summary>
    /// Old <c>_format_summary</c>: the stored JSON as readable text (PDF). Text that is not a
    /// JSON object (imported rows, or a model that ignored the schema) is returned as is.
    /// </summary>
    public static string FormatSummary(string stored)
    {
        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(stored);
            root = doc.RootElement.Clone();
        }
        catch (JsonException)
        {
            return stored;
        }
        if (root.ValueKind != JsonValueKind.Object) return stored;

        var parts = new List<string>();
        if (root.TryGetProperty("umumiy_xulosa", out var overall) && Truthy(overall))
            parts.Add("UMUMIY XULOSA\n" + AsText(overall).Trim());

        void Bullets(string title, string key)
        {
            if (!root.TryGetProperty(key, out var value) || !Truthy(value)) return;
            var items = value.ValueKind == JsonValueKind.Array
                ? value.EnumerateArray().Select(i => AsText(i).Trim()).Where(i => i.Length > 0).ToList()
                : [AsText(value).Trim()];
            items = [.. items.Where(i => i.Length > 0)];
            if (items.Count > 0)
                parts.Add(title + "\n" + string.Join("\n", items.Select(i => $"- {i}")));
        }

        Bullets("KUCHLI TOMONLAR", "kuchli_tomonlar");
        Bullets("RIVOJLANTIRISH KERAK", "rivojlantirish");
        Bullets("TAVSIYALAR", "tavsiyalar");
        var formatted = string.Join("\n\n", parts);
        return formatted.Length > 0 ? formatted : stored;
    }

    private static bool Truthy(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Null or JsonValueKind.Undefined or JsonValueKind.False => false,
        JsonValueKind.String => e.GetString()!.Length > 0,
        JsonValueKind.Array => e.GetArrayLength() > 0,
        _ => true,
    };

    private static string AsText(JsonElement e) =>
        e.ValueKind == JsonValueKind.String ? e.GetString()! : e.GetRawText();
}

/// <summary>
/// Gemini over REST (<c>generativelanguage.googleapis.com/v1beta/models/{model}:generateContent</c>).
/// Config: <c>Gemini:ApiKey</c> (env <c>Gemini__ApiKey</c>, never in the repo) and
/// <c>Gemini:Model</c> (env <c>Gemini__Model</c>, default <c>gemini-3.8-flash</c>). The key travels in the
/// <c>x-goog-api-key</c> header, never in the URL — a query string ends up in logs.
/// </summary>
public sealed class GeminiLessonAnalysisAi(HttpClient http, IConfiguration config) : ILessonAnalysisAi
{
    // gemini-2.5-flash answers 404 "no longer available to new users" to newly created keys (seen 2026-10-28).
    public const string DefaultModel = "gemini-3.8-flash";

    public const string MissingKeyError =
        "Gemini API kaliti sozlanmagan. Administrator serverdagi .env faylga "
        + "Gemini__ApiKey ni kiritishi kerak.";

    public async Task<LessonAiResult> SummarizeAsync(LessonAiObservation observation, CancellationToken ct = default)
    {
        var apiKey = config["Gemini:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
            return new LessonAiResult(null, MissingKeyError);

        var payload = new Dictionary<string, object>
        {
            ["system_instruction"] = new { parts = new[] { new { text = LessonAiPrompt.SystemPrompt } } },
            ["contents"] = new[]
            {
                new
                {
                    role = "user",
                    parts = new[] { new { text = LessonAiPrompt.BuildObservationText(observation) } },
                },
            },
            ["generationConfig"] = new { temperature = 0.4, responseMimeType = "application/json" },
        };

        string text;
        try
        {
            // retries an overloaded model, then asks the fallback model (GeminiRest)
            var response = await GeminiRest.GenerateAsync(http, apiKey, GeminiRest.Models(config), payload, ct);
            var body = response.Body;
            if (!response.IsSuccess)
                return new LessonAiResult(null,
                    $"Gemini API xatosi ({response.StatusCode}, {response.Model}): {(body.Length > 300 ? body[..300] : body)}");

            using var doc = JsonDocument.Parse(body);
            text = doc.RootElement.GetProperty("candidates")[0]
                .GetProperty("content").GetProperty("parts")[0]
                .GetProperty("text").GetString() ?? "";
        }
        catch (Exception ex)
        {
            // network, timeout, unexpected shape
            return new LessonAiResult(null, $"AI xulosa yaratishda xato: {ex.Message}");
        }

        return new LessonAiResult(LessonAiPrompt.NormalizeReply(text), null);
    }
}
