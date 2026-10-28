using System.Globalization;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace SchoolLms.Application.HrTasks;

// ===========================================================================
//  HR topshiriqlari — AI orqali yaratish (docs/modules/hr-ai-tasks.md §4.2–§4.3, phase 1).
//  The model only reads the director's words and returns a proposal as JSON; it never writes.
//  Employees and positions go to it as short refs (e1, p1) — GUIDs never leave, and an answer
//  that names a ref we did not send is refused by HrAiProposals.
// ===========================================================================

/// <summary>An employee as the model sees it.</summary>
public sealed record HrAiEmployeeRef(string Ref, string Name, string? Position);

/// <summary>A position as the model sees it.</summary>
public sealed record HrAiPositionRef(string Ref, string Name);

/// <summary>A voice note (Telegram sends OGG/OPUS); Gemini transcribes it itself.</summary>
public sealed record HrAiAudio(byte[] Bytes, string MimeType);

/// <summary>Everything one AI call needs.</summary>
/// <param name="Text">The director's message; null for a voice note.</param>
/// <param name="LocalNow">School wall-clock time now.</param>
/// <param name="CurrentProposal">Revision ("O'zgartirish"): the proposal on the card, as the answer JSON with refs.</param>
public sealed record HrAiRequest(
    string? Text, HrAiAudio? Audio, DateTime LocalNow, string TimeZone,
    IReadOnlyList<HrAiEmployeeRef> Employees, IReadOnlyList<HrAiPositionRef> Positions,
    string? CurrentProposal = null);

/// <summary>Exactly one is set: the model's JSON text, or an Uzbek error.</summary>
public sealed record HrAiReply(string? Json, string? Error);

/// <summary>The task-proposal model. Replaceable in tests — they never touch the network.</summary>
public interface IHrTaskAi
{
    /// <summary>false — no key: nothing is called and the director is told AI is off.</summary>
    bool IsConfigured { get; }

    /// <summary>Never throws: a failure comes back as <see cref="HrAiReply.Error"/>.</summary>
    Task<HrAiReply> ProposeAsync(HrAiRequest request, CancellationToken ct = default);
}

public static class HrAiPrompt
{
    public static readonly string[] WeekdayNames =
        ["dushanba", "seshanba", "chorshanba", "payshanba", "juma", "shanba", "yakshanba"];

    public const string SystemPrompt = """
        Sen maktab direktorining yordamchisisan. Direktor xodimlarga topshiriq berish uchun yozadi
        yoki ovozli xabar yuboradi. Vazifang — uning so'zlaridan topshiriq loyihasini tuzish.
        Sen hech narsani o'zing yaratmaysan: loyihani direktor ko'rib tasdiqlaydi.

        Qoidalar:
        - Faqat berilgan XODIMLAR va LAVOZIMLAR ro'yxatidagi qisqa kodlardan foydalan (e1, p1 ...).
          Ro'yxatda yo'q odamni o'ylab topma.
        - Direktor kimni nazarda tutganini aniq bilmasang (ism bir nechta xodimga mos keladi yoki umuman
          mos kelmaydi) — status "unclear" va bitta qisqa savol yoz.
        - Xabar topshiriq haqida bo'lmasa — status "not_task".
        - Bir martalik topshiriq: "due" — mahalliy vaqt, "YYYY-MM-DDTHH:mm". Sana aytilmasa — bugun;
          vaqt aytilmasa — 23:59. "ertaga", "indinga", "juma kuni" ni bugungi sanaga qarab hisobla.
        - Takroriy topshiriq ("har kuni", "har hafta", "har oy", "har juma"...): "recurring": true,
          "recurrence": "daily" | "weekly" | "monthly". Hafta kunlari: 1 = dushanba ... 7 = yakshanba;
          "daily" uchun 1..7 hammasi yoki ish kunlari aytilsa 1..6. "monthly" uchun "days_of_month".
          "due_time" — "HH:mm" yoki null (null = kun oxiri).
        - Butun lavozimga takroriy topshiriq aytilsa ("barcha o'qituvchilarga har juma") — "position"
          ga lavozim kodi, "employees" bo'sh. Bir martalik topshiriqda "position" bo'lmaydi.
        - Vaqt so'zlari: "ertalab" = 09:00, "tushda" = 13:00, "kechki payt" / "kechqurun" = 18:00,
          "ish oxirigacha" = 18:00.
        - "kind": "numeric" faqat erishiladigan son aytilsa ("50 ta qo'ng'iroq") — "target_amount" son,
          "unit" qisqa birlik ("ta", "qo'ng'iroq"). Aks holda "check", target_amount va unit null.
        - "title" — qisqa, buyruq shaklida (2–120 belgi), o'zbek tilida, lotin alifbosida.
          Tafsilot bo'lsa "description" ga yoz, bo'lmasa null.
        - Ovozli xabar bo'lsa, "transcript" ga aytilganini so'zma-so'z yoz.
        - JORIY LOYIHA berilgan bo'lsa — direktor uni tuzatmoqda: loyihani tuzatish bilan qaytar
          (o'zgartirilmagan qismlari o'z holicha qolsin).
        - Ko'pi bilan 10 ta topshiriq.

        Javobni FAQAT shu JSON ko'rinishida qaytar, boshqa hech narsa yozma:
        {
          "transcript": null,
          "status": "ok",
          "question": null,
          "tasks": [
            {
              "employees": ["e1"],
              "position": null,
              "title": "...",
              "description": null,
              "recurring": false,
              "due": "2026-10-28T10:00",
              "recurrence": null,
              "weekdays": [],
              "days_of_month": [],
              "due_time": null,
              "kind": "check",
              "target_amount": null,
              "unit": null
            }
          ]
        }
        """;

    /// <summary>The user turn: date, people, positions, the current proposal, the message.</summary>
    public static string BuildContext(HrAiRequest r)
    {
        var sb = new StringBuilder();
        var weekday = WeekdayNames[(r.LocalNow.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)r.LocalNow.DayOfWeek) - 1];
        sb.Append(CultureInfo.InvariantCulture,
            $"Bugun: {r.LocalNow:dd.MM.yyyy} ({r.LocalNow:yyyy-MM-dd}), {weekday}. Hozir: {r.LocalNow:HH:mm} ({r.TimeZone}).\n\n");
        sb.Append("XODIMLAR:\n");
        foreach (var e in r.Employees)
            sb.Append(e.Ref).Append(" — ").Append(e.Name)
              .Append(string.IsNullOrWhiteSpace(e.Position) ? "" : " — " + e.Position).Append('\n');
        sb.Append("\nLAVOZIMLAR:\n");
        foreach (var p in r.Positions) sb.Append(p.Ref).Append(" — ").Append(p.Name).Append('\n');
        if (!string.IsNullOrWhiteSpace(r.CurrentProposal))
            sb.Append("\nJORIY LOYIHA (direktor tuzatmoqda):\n").Append(r.CurrentProposal).Append('\n');
        sb.Append('\n');
        sb.Append(r.Audio is not null
            ? (r.CurrentProposal is null ? "DIREKTOR OVOZLI XABAR YUBORDI (ilova qilingan)." : "DIREKTOR TUZATISHI — OVOZLI XABAR (ilova qilingan).")
            : (r.CurrentProposal is null ? "DIREKTOR XABARI:\n" : "DIREKTOR TUZATISHI:\n") + r.Text);
        return sb.ToString();
    }
}

/// <summary>
/// Gemini over REST — the same call and config as <see cref="Services.GeminiLessonAnalysisAi"/>:
/// <c>Gemini:ApiKey</c> (env <c>Gemini__ApiKey</c>, never in the repo; must be a paid-tier key — staff
/// names go to it), <c>Gemini:Model</c> (default <see cref="Services.GeminiLessonAnalysisAi.DefaultModel"/>), key in the header.
/// </summary>
public sealed class GeminiHrTaskAi(HttpClient http, IConfiguration config) : IHrTaskAi
{
    public bool IsConfigured => !string.IsNullOrWhiteSpace(config["Gemini:ApiKey"]);

    public async Task<HrAiReply> ProposeAsync(HrAiRequest request, CancellationToken ct = default)
    {
        var apiKey = config["Gemini:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey)) return new HrAiReply(null, Services.GeminiLessonAnalysisAi.MissingKeyError);
        var model = config["Gemini:Model"];
        if (string.IsNullOrWhiteSpace(model)) model = Services.GeminiLessonAnalysisAi.DefaultModel;

        var parts = new List<object> { new { text = HrAiPrompt.BuildContext(request) } };
        if (request.Audio is { } audio)
            parts.Add(new { inline_data = new { mime_type = audio.MimeType, data = Convert.ToBase64String(audio.Bytes) } });
        var payload = new Dictionary<string, object>
        {
            ["system_instruction"] = new { parts = new[] { new { text = HrAiPrompt.SystemPrompt } } },
            ["contents"] = new[] { new { role = "user", parts } },
            ["generationConfig"] = new { temperature = 0.2, responseMimeType = "application/json" },
        };

        var url = $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model.Trim())}:generateContent";
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(payload) };
            message.Headers.Add("x-goog-api-key", apiKey.Trim());
            using var response = await http.SendAsync(message, ct);
            var body = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                return new HrAiReply(null,
                    $"Gemini API xatosi ({(int)response.StatusCode}): {(body.Length > 300 ? body[..300] : body)}");
            using var doc = JsonDocument.Parse(body);
            var text = doc.RootElement.GetProperty("candidates")[0]
                .GetProperty("content").GetProperty("parts")[0]
                .GetProperty("text").GetString() ?? "";
            return new HrAiReply(Services.LessonAiPrompt.NormalizeReply(text), null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
        {
            // network, timeout, unexpected shape — the director is told to try again
            return new HrAiReply(null, $"AI javobida xato: {ex.GetType().Name}");
        }
    }
}
