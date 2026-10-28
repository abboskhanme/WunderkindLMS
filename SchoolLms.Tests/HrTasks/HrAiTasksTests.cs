using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SchoolLms.Application.HrTasks;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests.HrTasks;

/// <summary>
/// HR tasks from a director's bot message through AI (docs/modules/hr-ai-tasks.md, phase 1). The bot is
/// driven update by update; the AI is a fake that answers from the refs it was given; every Telegram call
/// lands in a recording handler — nothing leaves the machine.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class HrAiTasksTests(ApiFixture fixture)
{
    private readonly HrTasksKit _kit = new(fixture);
    private static readonly TimeZoneInfo Tz = HrTasksKit.Tz;

    // =====================================================================
    //  Who is heard
    // =====================================================================

    [Fact]
    public async Task Direktor_bolmagan_xodim_odatdagi_javobni_oladi_AI_chaqirilmaydi()
    {
        var staff = await _kit.PersonAsync(Roles.Staff, HrRoles.Employee, telegram: true);
        var (bot, tg, ai) = Bot(_ => Ok(""));

        await bot.HandleUpdateAsync(TextUpdate(staff.Chat!.Value, "Alisherga hisobot topshiriq ber"), default);

        Assert.Empty(ai.Requests);
        Assert.DoesNotContain(tg.Calls, c => c.Method == "sendMessage" && c.Text.Contains("Topshiriq loyihasi"));
        Assert.NotEmpty(tg.Calls);
    }

    [Fact]
    public async Task Kalit_yoq_bolsa_direktorga_AI_yoqilmagan_deyiladi()
    {
        var director = await DirectorAsync();
        var (bot, tg, ai) = Bot(_ => Ok(""), configured: false);

        await bot.HandleUpdateAsync(TextUpdate(director.Chat!.Value, "Alisherga hisobot"), default);

        Assert.Empty(ai.Requests);
        Assert.Contains("hali yoqilmagan", Assert.Single(tg.Sent).Text);
        Assert.Equal(0, await DraftCountAsync(director.User.Id));
    }

    [Fact]
    public async Task Buyruq_AI_ga_ketmaydi()
    {
        var director = await DirectorAsync();
        var (bot, _, ai) = Bot(_ => Ok(""));
        await bot.HandleUpdateAsync(TextUpdate(director.Chat!.Value, "/start"), default);
        Assert.Empty(ai.Requests);
    }

    // =====================================================================
    //  Text → card → approve
    // =====================================================================

    [Fact]
    public async Task Matn_karta_tasdiqlash_topshiriq_yaratadi_va_xodimga_yuboradi()
    {
        var director = await DirectorAsync();
        var worker = await _kit.PersonAsync(Roles.Staff, HrRoles.Employee, telegram: true, name: Unique("Alisher Karimov"));
        var tomorrow = Today().AddDays(1);
        var (bot, tg, ai) = Bot(r => Ok(Task1(Ref(r, worker), "Kassa hisobotini topshirish", $"{tomorrow:yyyy-MM-dd}T10:00")));

        await bot.HandleUpdateAsync(TextUpdate(director.Chat!.Value, "Alisherga ertaga 10 gacha kassa hisobotini topshirsin"), default);

        var request = Assert.Single(ai.Requests);
        Assert.Equal("Alisherga ertaga 10 gacha kassa hisobotini topshirsin", request.Text);
        Assert.Contains(request.Employees, e => e.Name == worker.User.FullName);
        Assert.DoesNotContain(request.Employees, e => e.Ref == worker.Employee!.Id); // GUIDs never go to the model

        var card = tg.Sent.Single(c => c.Text.Contains("Topshiriq loyihasi"));
        Assert.Contains("Kassa hisobotini topshirish", card.Text);
        Assert.Contains($"{tomorrow:dd.MM.yyyy}, 10:00", card.Text);
        var draft = await LastDraftAsync(director.User.Id);
        Assert.Equal(HrAiDraftStatuses.Pending, draft.Status);
        Assert.Contains($"hai:ok:{draft.Id}", card.Body.GetProperty("reply_markup").GetRawText());
        Assert.Equal(0, await TaskCountAsync(worker.Employee!.Id));

        await bot.HandleUpdateAsync(CallbackUpdate(director.Chat.Value, $"hai:ok:{draft.Id}", card.MessageId), default);

        HrTask task = null!;
        await fixture.Api.WithDbAsync(async db => task = await db.HrTasks.AsNoTracking().SingleAsync(t => t.EmployeeId == worker.Employee!.Id));
        Assert.Equal(("Kassa hisobotini topshirish", HrTaskSources.Director, director.User.Id), (task.Title, task.Source, task.CreatedBy));
        Assert.Equal(HrValues.At(tomorrow, new TimeOnly(10, 0), Tz), task.DueAt);
        Assert.Contains(tg.Sent, m => m.Chat == worker.Chat && m.Text.Contains("Kassa hisobotini topshirish"));
        var edited = tg.Calls.Last(c => c.Method == "editMessageText");
        Assert.Contains("Tasdiqlandi", edited.Text);
        Assert.False(edited.Body.TryGetProperty("reply_markup", out _));
        Assert.Equal(HrAiDraftStatuses.Approved, (await LastDraftAsync(director.User.Id)).Status);

        // A second press creates nothing again.
        await bot.HandleUpdateAsync(CallbackUpdate(director.Chat.Value, $"hai:ok:{draft.Id}", card.MessageId), default);
        Assert.Equal(1, await TaskCountAsync(worker.Employee!.Id));
        Assert.Contains("allaqachon", tg.Calls.Last(c => c.Method == "answerCallbackQuery").Text);
    }

    [Fact]
    public async Task Bir_nechta_xodimga_va_takroriy_topshiriq_bitta_kartada()
    {
        var director = await DirectorAsync();
        var a = await _kit.PersonAsync(Roles.Staff, HrRoles.Employee, name: Unique("Aziza Buxgalter"));
        var b = await _kit.PersonAsync(Roles.Staff, HrRoles.Employee, name: Unique("Bobur Kassir"));
        var due = Today().AddDays(2);
        var (bot, tg, _) = Bot(r => Ok(JsonSerializer.Serialize(new
        {
            status = "ok",
            tasks = new object[]
            {
                Item([Ref(r, a), Ref(r, b)], "Inventarizatsiya qilish", false, due: $"{due:yyyy-MM-dd}T18:00"),
                Item([Ref(r, a)], "Moliya bo'limini tekshirish", true, recurrence: "weekly", weekdays: [5], dueTime: "18:00",
                    kind: "numeric", target: 10, unit: "hujjat"),
            },
        })));

        await bot.HandleUpdateAsync(TextUpdate(director.Chat!.Value, "ikkalasiga inventarizatsiya, Aziza har juma moliyani tekshirsin"), default);
        var card = tg.Sent.Single(c => c.Text.Contains("Topshiriq loyihasi"));
        Assert.Contains("Har hafta: juma, soat 18:00", card.Text);
        Assert.Contains("Maqsad: 10 hujjat", card.Text);

        var draft = await LastDraftAsync(director.User.Id);
        await bot.HandleUpdateAsync(CallbackUpdate(director.Chat.Value, $"hai:ok:{draft.Id}", card.MessageId), default);

        Assert.Equal(1, await TaskCountAsync(b.Employee!.Id));
        await fixture.Api.WithDbAsync(async db =>
        {
            var template = await db.HrTaskTemplates.AsNoTracking().SingleAsync(t => t.TargetEmployeeId == a.Employee!.Id);
            Assert.Equal(("Moliya bo'limini tekshirish", HrRecurrences.Weekly, new TimeOnly(18, 0)),
                (template.Title, template.Recurrence, template.DueTime!.Value));
            Assert.Equal(new short[] { 5 }, template.Weekdays);
            Assert.Equal((HrTaskKinds.Numeric, 10m, director.User.Id), (template.Kind, template.TargetAmount, template.CreatedBy));
            Assert.True(await db.HrTasks.AnyAsync(t => t.EmployeeId == a.Employee!.Id && t.Title == "Inventarizatsiya qilish"));
        });
        Assert.Contains("2 ta topshiriq, 1 ta takroriy topshiriq", tg.Calls.Last(c => c.Method == "editMessageText").Text);
    }

    [Fact]
    public async Task Boshqa_odam_tugmani_bossa_hech_narsa_yaratilmaydi()
    {
        var director = await DirectorAsync();
        var other = await DirectorAsync();
        var worker = await _kit.PersonAsync(Roles.Staff, HrRoles.Employee, name: Unique("Begona Sinov"));
        var (bot, tg, _) = Bot(r => Ok(Task1(Ref(r, worker), "Hisobot", $"{Today().AddDays(1):yyyy-MM-dd}T10:00")));
        await bot.HandleUpdateAsync(TextUpdate(director.Chat!.Value, "hisobot"), default);
        var draft = await LastDraftAsync(director.User.Id);

        await bot.HandleUpdateAsync(CallbackUpdate(other.Chat!.Value, $"hai:ok:{draft.Id}", 1), default);

        Assert.Contains("siz uchun emas", tg.Calls.Last(c => c.Method == "answerCallbackQuery").Text);
        Assert.Equal(0, await TaskCountAsync(worker.Employee!.Id));
        Assert.Equal(HrAiDraftStatuses.Pending, (await LastDraftAsync(director.User.Id)).Status);
    }

    [Fact]
    public async Task Bekor_qilinsa_hech_narsa_yaratilmaydi()
    {
        var director = await DirectorAsync();
        var worker = await _kit.PersonAsync(Roles.Staff, HrRoles.Employee, name: Unique("Bekor Sinov"));
        var (bot, tg, _) = Bot(r => Ok(Task1(Ref(r, worker), "Hisobot", $"{Today().AddDays(1):yyyy-MM-dd}T10:00")));
        await bot.HandleUpdateAsync(TextUpdate(director.Chat!.Value, "hisobot"), default);
        var draft = await LastDraftAsync(director.User.Id);

        await bot.HandleUpdateAsync(CallbackUpdate(director.Chat.Value, $"hai:no:{draft.Id}", 1), default);
        await bot.HandleUpdateAsync(CallbackUpdate(director.Chat.Value, $"hai:ok:{draft.Id}", 1), default);

        Assert.Equal(HrAiDraftStatuses.Cancelled, (await LastDraftAsync(director.User.Id)).Status);
        Assert.Equal(0, await TaskCountAsync(worker.Employee!.Id));
        Assert.Contains("Bekor qilindi", tg.Calls.First(c => c.Method == "editMessageText").Text);
    }

    [Fact]
    public async Task Ozgartirish_keyingi_xabar_joriy_loyiha_bilan_AI_ga_boradi()
    {
        var director = await DirectorAsync();
        var worker = await _kit.PersonAsync(Roles.Staff, HrRoles.Employee, name: Unique("Tuzatish Sinov"));
        var day = Today().AddDays(1);
        var calls = 0;
        var (bot, tg, ai) = Bot(r => Ok(Task1(Ref(r, worker), ++calls == 1 ? "Hisobot" : "Yillik hisobot", $"{day:yyyy-MM-dd}T{(calls == 1 ? "10" : "15")}:00")));

        await bot.HandleUpdateAsync(TextUpdate(director.Chat!.Value, "hisobot"), default);
        var first = await LastDraftAsync(director.User.Id);
        await bot.HandleUpdateAsync(CallbackUpdate(director.Chat.Value, $"hai:ed:{first.Id}", 1), default);
        Assert.Contains(tg.Sent, m => m.Text.Contains("Nimani o'zgartiramiz"));
        // The card keeps its buttons while the correction is awaited.
        var editingCard = tg.Calls.Last(c => c.Method == "editMessageText");
        Assert.Contains("O'zgartirilmoqda", editingCard.Text);
        Assert.Contains($"hai:ok:{first.Id}", editingCard.Body.GetProperty("reply_markup").GetRawText());

        await bot.HandleUpdateAsync(TextUpdate(director.Chat.Value, "yillik bo'lsin, soat 15 da"), default);

        var revision = ai.Requests[1];
        Assert.NotNull(revision.CurrentProposal);
        Assert.Contains(Ref(revision, worker), revision.CurrentProposal);
        Assert.Contains("\"Hisobot\"", revision.CurrentProposal);
        Assert.Equal("yillik bo'lsin, soat 15 da", revision.Text);

        await fixture.Api.WithDbAsync(async db =>
        {
            var drafts = await db.HrAiDrafts.AsNoTracking().Where(d => d.UserId == director.User.Id).OrderBy(d => d.CreatedAt).ToListAsync();
            Assert.Equal([HrAiDraftStatuses.Replaced, HrAiDraftStatuses.Pending], drafts.Select(d => d.Status));
            Assert.Contains("Yillik hisobot", drafts[1].Proposal);
        });
        Assert.Contains(tg.Calls, c => c.Method == "editMessageText" && c.Text.Contains("O'zgartirildi"));
    }

    // =====================================================================
    //  The model is not trusted
    // =====================================================================

    [Fact]
    public async Task Notanish_xodim_otgan_muddat_va_savol_loyiha_yaratmaydi()
    {
        var director = await DirectorAsync();
        var worker = await _kit.PersonAsync(Roles.Staff, HrRoles.Employee, name: Unique("Ishonch Sinov"));
        var answers = new Queue<Func<HrAiRequest, string>>(
        [
            _ => Task1("e999999", "Hisobot", $"{Today().AddDays(1):yyyy-MM-dd}T10:00"),
            r => Task1(Ref(r, worker), "Hisobot", $"{Today().AddDays(-1):yyyy-MM-dd}T10:00"),
            _ => """{"status":"unclear","question":"Qaysi Alisher: Karimov yoki Tursunov?","tasks":[]}""",
            _ => """{"status":"not_task","tasks":[]}""",
            _ => "bu json emas",
            r => JsonSerializer.Serialize(new { status = "ok", tasks = new[] { Item([Ref(r, worker)], "Hisobot", true, recurrence: "weekly") } }),
        ]);
        var (bot, tg, _) = Bot(r => Ok(answers.Dequeue()(r)));

        for (var i = 0; i < 6; i++) await bot.HandleUpdateAsync(TextUpdate(director.Chat!.Value, $"xabar {i}"), default);

        var texts = tg.Sent.Select(s => s.Text).ToList();
        Assert.Contains(texts, t => t.Contains("Xodim ro'yxatda topilmadi"));
        Assert.Contains(texts, t => t.Contains("Muddat o'tib ketgan"));
        Assert.Contains(texts, t => t.Contains("Qaysi Alisher: Karimov yoki Tursunov?"));
        Assert.Contains(texts, t => t.Contains("topshiriq berishga yordam beraman"));
        Assert.Contains(texts, t => t.Contains("tushunib bo'lmadi"));
        Assert.Contains(texts, t => t.Contains("Qachon takrorlanishini"));
        Assert.DoesNotContain(texts, t => t.Contains("Topshiriq loyihasi"));
        Assert.Equal(0, await DraftCountAsync(director.User.Id));
    }

    [Fact]
    public async Task AI_xatosi_direktorga_qayta_urinib_koring_deydi()
    {
        var director = await DirectorAsync();
        var (bot, tg, _) = Bot(_ => new HrAiReply(null, "Gemini API xatosi (500)"));
        await bot.HandleUpdateAsync(TextUpdate(director.Chat!.Value, "hisobot"), default);
        Assert.Contains("qayta urinib", Assert.Single(tg.Sent).Text);
    }

    [Fact]
    public async Task Muddati_otgan_loyiha_tasdiqlanmaydi()
    {
        var director = await DirectorAsync();
        var worker = await _kit.PersonAsync(Roles.Staff, HrRoles.Employee, name: Unique("Eski Loyiha"));
        var (bot, tg, _) = Bot(r => Ok(Task1(Ref(r, worker), "Hisobot", $"{Today().AddDays(3):yyyy-MM-dd}T10:00")));
        await bot.HandleUpdateAsync(TextUpdate(director.Chat!.Value, "hisobot"), default);
        var draft = await LastDraftAsync(director.User.Id);
        await fixture.Api.WithDbAsync(async db =>
        {
            await db.HrAiDrafts.Where(d => d.Id == draft.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(d => d.CreatedAt, DateTimeOffset.UtcNow.AddHours(-25)));
        });

        await bot.HandleUpdateAsync(CallbackUpdate(director.Chat.Value, $"hai:ok:{draft.Id}", 1), default);

        Assert.Equal(HrAiDraftStatuses.Expired, (await LastDraftAsync(director.User.Id)).Status);
        Assert.Equal(0, await TaskCountAsync(worker.Employee!.Id));
        Assert.Contains("Muddati o'tgan", tg.Calls.Last(c => c.Method == "answerCallbackQuery").Text);
    }

    [Fact]
    public async Task Soatiga_30_tadan_ortiq_sorov_toxtatiladi()
    {
        var director = await DirectorAsync();
        var (bot, tg, ai) = Bot(_ => Ok("""{"status":"not_task","tasks":[]}"""));
        for (var i = 0; i <= HrAiTaskService.MaxRequestsPerHour; i++)
            await bot.HandleUpdateAsync(TextUpdate(director.Chat!.Value, $"salom {i}"), default);
        Assert.Equal(HrAiTaskService.MaxRequestsPerHour, ai.Requests.Count);
        Assert.Contains("Juda ko'p so'rov", tg.Sent.Last().Text);
    }

    // =====================================================================
    //  Voice
    // =====================================================================

    [Fact]
    public async Task Ovozli_xabar_yuklab_olinadi_va_aytilgani_kartada()
    {
        var director = await DirectorAsync();
        var worker = await _kit.PersonAsync(Roles.Staff, HrRoles.Employee, name: Unique("Ovoz Sinov"));
        var (bot, tg, ai) = Bot(r => Ok(JsonSerializer.Serialize(new
        {
            transcript = "Ovozga ertaga hisobot topshirsin",
            status = "ok",
            tasks = new[] { Item([Ref(r, worker)], "Hisobot topshirish", false, due: $"{Today().AddDays(1):yyyy-MM-dd}T23:59") },
        })));

        await bot.HandleUpdateAsync(VoiceUpdate(director.Chat!.Value, "voice-file-1", seconds: 7), default);

        var request = Assert.Single(ai.Requests);
        Assert.Null(request.Text);
        Assert.Equal(TelegramRecorder.VoiceBytes, request.Audio!.Bytes);
        Assert.Equal("audio/ogg", request.Audio.MimeType);
        Assert.Contains(tg.Calls, c => c.Method == "getFile" && c.Body.GetProperty("file_id").GetString() == "voice-file-1");
        var card = tg.Sent.Single(c => c.Text.Contains("Topshiriq loyihasi"));
        Assert.Contains("Siz aytdingiz", card.Text);
        Assert.Contains("Ovozga ertaga hisobot topshirsin", card.Text);
        var draft = await LastDraftAsync(director.User.Id);
        Assert.Equal((HrAiSources.Voice, "Ovozga ertaga hisobot topshirsin"), (draft.Source, draft.InputText));
    }

    [Fact]
    public async Task Besh_daqiqadan_uzun_ovoz_qabul_qilinmaydi()
    {
        var director = await DirectorAsync();
        var (bot, tg, ai) = Bot(_ => Ok(""));
        await bot.HandleUpdateAsync(VoiceUpdate(director.Chat!.Value, "long", seconds: HrLimits.MaxVoiceSeconds + 1), default);
        Assert.Empty(ai.Requests);
        Assert.DoesNotContain(tg.Calls, c => c.Method == "getFile");
        Assert.Contains("5 daqiqadan oshmasin", Assert.Single(tg.Sent).Text);
    }

    [Fact]
    public async Task HR_rolidagi_direktor_ham_eshitiladi()
    {
        var director = await _kit.PersonAsync(Roles.Staff, HrRoles.Director, telegram: true);
        var (bot, _, ai) = Bot(_ => Ok("""{"status":"not_task","tasks":[]}"""));
        await bot.HandleUpdateAsync(TextUpdate(director.Chat!.Value, "salom"), default);
        Assert.Single(ai.Requests);
    }

    [Fact]
    public void Katta_loyiha_kartasi_Telegram_chegarasidan_oshmaydi()
    {
        var people = Enumerable.Range(1, HrLimits.MaxBatchEmployees)
            .Select(i => new HrAiPerson($"id{i}", $"Juda Uzun Familiya Ismli Xodim {i}", "Boshlang'ich sinf o'qituvchisi")).ToList();
        var items = Enumerable.Range(1, HrAiProposals.MaxItems).Select(i => new HrAiProposalItem(
            new string('T', HrLimits.TitleMax), new string('D', 1000), false, people, null, "2026-10-28T10:00",
            null, [], [], null, HrTaskKinds.Check, null, null)).ToList();
        var proposal = new HrAiProposal(new string('s', 5000), items);

        var card = HrAiTexts.Card(proposal, HrAiSources.Voice);
        var decided = HrAiTexts.Decided(proposal, HrAiSources.Voice,
            HrAiTexts.ApprovedLine(3, 0, [.. Enumerable.Range(1, 10).Select(i => new string('x', 400))]));

        Assert.True(card.Length < 4096, $"card {card.Length}");
        Assert.True(decided.Length < 4096, $"decided {decided.Length}");
        Assert.Contains("va yana", card);
    }

    // =====================================================================
    //  Gemini client (no network: a recording handler answers like the API)
    // =====================================================================

    [Fact]
    public async Task Gemini_kalitsiz_tarmoqqa_chiqmaydi()
    {
        var handler = new GeminiRecorder("{}");
        var ai = new GeminiHrTaskAi(new HttpClient(handler), new ConfigurationBuilder().Build());
        Assert.False(ai.IsConfigured);
        var reply = await ai.ProposeAsync(Request(null));
        Assert.Null(reply.Json);
        Assert.NotNull(reply.Error);
        Assert.Null(handler.Body);
    }

    [Fact]
    public async Task Gemini_sorovi_ovozni_ilova_qiladi_va_kalit_sarlavhada()
    {
        var answer = """{"status":"not_task","tasks":[]}""";
        var handler = new GeminiRecorder(JsonSerializer.Serialize(new
        {
            candidates = new[] { new { content = new { parts = new[] { new { text = "```json\n" + answer + "\n```" } } } } },
        }));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Gemini:ApiKey"] = "k-123" }).Build();
        var ai = new GeminiHrTaskAi(new HttpClient(handler), config);

        var reply = await ai.ProposeAsync(Request(new HrAiAudio([9, 8, 7], "audio/ogg")));

        Assert.Equal(answer, reply.Json);
        Assert.Equal("k-123", handler.Key);
        Assert.DoesNotContain("k-123", handler.Url);
        Assert.EndsWith($"/models/{GeminiLessonAnalysisAi.DefaultModel}:generateContent", handler.Url);
        using var body = JsonDocument.Parse(handler.Body!);
        var parts = body.RootElement.GetProperty("contents")[0].GetProperty("parts");
        Assert.Contains("e1 — Aliyev Ali — Buxgalter", parts[0].GetProperty("text").GetString());
        Assert.Contains("OVOZLI XABAR", parts[0].GetProperty("text").GetString());
        Assert.Equal(Convert.ToBase64String([9, 8, 7]), parts[1].GetProperty("inline_data").GetProperty("data").GetString());
        Assert.Equal("audio/ogg", parts[1].GetProperty("inline_data").GetProperty("mime_type").GetString());
        Assert.Contains("FAQAT", body.RootElement.GetProperty("system_instruction").GetProperty("parts")[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task Gemini_band_bolsa_qayta_urinadi_keyin_zaxira_modelga_otadi()
    {
        var ok = JsonSerializer.Serialize(new
        {
            candidates = new[] { new { content = new { parts = new[] { new { text = """{"status":"not_task","tasks":[]}""" } } } } },
        });
        // primary: 503 three times (1 try + 2 retries), then the fallback answers
        var handler = new GeminiScript((503, "{\"error\":{\"code\":503}}"), (503, "{}"), (503, "{}"), (200, ok));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Gemini:ApiKey"] = "k" }).Build();
        var ai = new GeminiHrTaskAi(new HttpClient(handler), config) { RetryDelays = [TimeSpan.Zero, TimeSpan.Zero] };

        var reply = await ai.ProposeAsync(Request(null));

        Assert.Null(reply.Error);
        Assert.Equal("""{"status":"not_task","tasks":[]}""", reply.Json);
        Assert.Equal(4, handler.Urls.Count);
        Assert.All(handler.Urls.Take(3), u => Assert.Contains($"/models/{GeminiLessonAnalysisAi.DefaultModel}:", u));
        Assert.Contains($"/models/{GeminiRest.DefaultFallbackModel}:", handler.Urls[3]);
    }

    [Fact]
    public async Task Gemini_yopilgan_model_404_zaxiraga_otadi_400_esa_darhol_qaytadi()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Gemini:ApiKey"] = "k", ["Gemini:Model"] = "m-old", ["Gemini:FallbackModel"] = "m-new",
        }).Build();

        // 404 is not retried on the same model — it goes straight to the fallback
        var gone = new GeminiScript((404, "{}"), (200, "{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"{}\"}]}}]}"));
        var reply = await new GeminiHrTaskAi(new HttpClient(gone), config) { RetryDelays = [TimeSpan.Zero] }
            .ProposeAsync(Request(null));
        Assert.Null(reply.Error);
        Assert.Equal(["m-old", "m-new"], gone.Urls.Select(u => u.Split("/models/")[1].Split(':')[0]));

        // a bad request would fail on any model — one call, error names status and model
        var bad = new GeminiScript((400, "{\"error\":\"bad\"}"));
        var failed = await new GeminiHrTaskAi(new HttpClient(bad), config) { RetryDelays = [TimeSpan.Zero] }
            .ProposeAsync(Request(null));
        Assert.Single(bad.Urls);
        Assert.Contains("(400, m-old)", failed.Error);
    }

    [Fact]
    public void Gemini_zaxira_asosiy_bilan_bir_xil_bolsa_bir_marta()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Gemini:Model"] = " gemini-3.6-flash ", ["Gemini:FallbackModel"] = "",
        }).Build();
        Assert.Equal(["gemini-3.6-flash"], GeminiRest.Models(config));
        Assert.Equal([GeminiLessonAnalysisAi.DefaultModel, GeminiRest.DefaultFallbackModel],
            GeminiRest.Models(new ConfigurationBuilder().Build()));
    }

    /// <summary>Answers each call with the next scripted (status, body); records the URLs.</summary>
    private sealed class GeminiScript(params (int Status, string Body)[] answers) : HttpMessageHandler
    {
        public List<string> Urls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Urls.Add(request.RequestUri!.ToString());
            var (status, body) = answers[Urls.Count - 1];
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            });
        }
    }

    private static HrAiRequest Request(HrAiAudio? audio) => new(
        audio is null ? "salom" : null, audio, new DateTime(2026, 10, 27, 14, 30, 0), "Asia/Tashkent",
        [new HrAiEmployeeRef("e1", "Aliyev Ali", "Buxgalter")], [new HrAiPositionRef("p1", "Buxgalter")]);

    private sealed class GeminiRecorder(string response) : HttpMessageHandler
    {
        public string? Body { get; private set; }
        public string? Key { get; private set; }
        public string Url { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Url = request.RequestUri!.ToString();
            Key = request.Headers.TryGetValues("x-goog-api-key", out var v) ? v.Single() : null;
            Body = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response, Encoding.UTF8, "application/json") };
        }
    }

    // =====================================================================
    //  Helpers
    // =====================================================================

    private Task<HrTasksKit.Person> DirectorAsync() => _kit.PersonAsync(Roles.Admin, hrRole: null, telegram: true);

    private static string Unique(string name) => $"{name} {Guid.NewGuid().ToString("N")[..6]}";

    private static DateOnly Today() => HrValues.LocalDate(DateTimeOffset.UtcNow, Tz);

    private static string Ref(HrAiRequest r, HrTasksKit.Person p) => r.Employees.Single(e => e.Name == p.User.FullName).Ref;

    private static HrAiReply Ok(string json) => new(json, null);

    private static string Task1(string employeeRef, string title, string due) =>
        JsonSerializer.Serialize(new { status = "ok", tasks = new[] { Item([employeeRef], title, false, due: due) } });

    private static object Item(
        string[] employees, string title, bool recurring, string? due = null, string? recurrence = null, int[]? weekdays = null,
        string? dueTime = null, string kind = "check", decimal? target = null, string? unit = null) => new
    {
        employees, position = (string?)null, title, description = (string?)null, recurring, due, recurrence,
        weekdays = weekdays ?? [], days_of_month = Array.Empty<int>(), due_time = dueTime, kind, target_amount = target, unit,
    };

    private async Task<HrAiDraft> LastDraftAsync(string userId)
    {
        HrAiDraft draft = null!;
        await fixture.Api.WithDbAsync(async db =>
            draft = await db.HrAiDrafts.AsNoTracking().Where(d => d.UserId == userId).OrderByDescending(d => d.CreatedAt).FirstAsync());
        return draft;
    }

    private async Task<int> DraftCountAsync(string userId)
    {
        var n = 0;
        await fixture.Api.WithDbAsync(async db => n = await db.HrAiDrafts.CountAsync(d => d.UserId == userId));
        return n;
    }

    private async Task<int> TaskCountAsync(string employeeId)
    {
        var n = 0;
        await fixture.Api.WithDbAsync(async db => n = await db.HrTasks.CountAsync(t => t.EmployeeId == employeeId));
        return n;
    }

    private (TelegramBotService Bot, TelegramRecorder Telegram, FakeAi Ai) Bot(Func<HrAiRequest, HrAiReply> answer, bool configured = true)
    {
        var recorder = new TelegramRecorder();
        var telegram = new TelegramService(
            fixture.Api.Services.GetRequiredService<IConfiguration>(), new Factory(recorder),
            fixture.Api.Services.GetRequiredService<ILogger<TelegramService>>());
        telegram.Set("test-token", "test_bot");
        var ai = new FakeAi(answer, configured);
        var bot = new TelegramBotService(fixture.Api.Services, telegram, fixture.Api.Services.GetRequiredService<ILogger<TelegramBotService>>())
        {
            HrTaskAi = ai,
        };
        return (bot, recorder, ai);
    }

    private static JsonElement Parse(object value)
    {
        using var doc = JsonDocument.Parse(JsonSerializer.Serialize(value));
        return doc.RootElement.Clone();
    }

    private static JsonElement TextUpdate(long chat, string text) => Parse(new
    {
        update_id = 1,
        message = new { message_id = 1, chat = new { id = chat, type = "private" }, from = new { id = chat, first_name = "D" }, text },
    });

    private static JsonElement VoiceUpdate(long chat, string fileId, int seconds) => Parse(new
    {
        update_id = 1,
        message = new
        {
            message_id = 1, chat = new { id = chat, type = "private" }, from = new { id = chat, first_name = "D" },
            voice = new { file_id = fileId, duration = seconds, mime_type = "audio/ogg", file_size = 3 },
        },
    });

    private static JsonElement CallbackUpdate(long from, string data, long messageId) => Parse(new
    {
        update_id = 1,
        callback_query = new
        {
            id = Guid.NewGuid().ToString("N"), from = new { id = from, first_name = "D" }, data,
            message = new { message_id = messageId, chat = new { id = from, type = "private" } },
        },
    });

    private sealed class FakeAi(Func<HrAiRequest, HrAiReply> answer, bool configured) : IHrTaskAi
    {
        public List<HrAiRequest> Requests { get; } = [];
        public bool IsConfigured => configured;

        public Task<HrAiReply> ProposeAsync(HrAiRequest request, CancellationToken ct = default)
        {
            Requests.Add(request);
            return Task.FromResult(answer(request));
        }
    }

    public sealed record Call(string Method, JsonElement Body)
    {
        public string Text => Body.ValueKind == JsonValueKind.Object && Body.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String
            ? t.GetString()! : "";
        public long? Chat => Body.ValueKind == JsonValueKind.Object && Body.TryGetProperty("chat_id", out var c) && c.TryGetInt64(out var id) ? id : null;
        public long MessageId { get; init; }
    }

    /// <summary>Answers like Telegram (message ids, a file for getFile) and keeps every call.</summary>
    private sealed class TelegramRecorder : HttpMessageHandler
    {
        public static readonly byte[] VoiceBytes = [1, 2, 3];
        private readonly List<Call> _calls = [];
        private long _messageId = 100;

        public IReadOnlyList<Call> Calls { get { lock (_calls) return [.. _calls]; } }
        public IReadOnlyList<Call> Sent => [.. Calls.Where(c => c.Method == "sendMessage")];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (path.StartsWith("/file/", StringComparison.Ordinal))
            {
                lock (_calls) _calls.Add(new Call("download", Parse(new { path })));
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(VoiceBytes) };
            }
            var method = path.Split('/').Last();
            var body = request.Content is null ? "{}" : await request.Content.ReadAsStringAsync(ct);
            JsonElement json;
            try { using var doc = JsonDocument.Parse(body); json = doc.RootElement.Clone(); }
            catch (JsonException) { json = Parse(new { }); }
            var id = Interlocked.Increment(ref _messageId);
            lock (_calls) _calls.Add(new Call(method, json) { MessageId = id });
            var result = method switch
            {
                "sendMessage" => $"{{\"message_id\":{id}}}",
                "getFile" => "{\"file_id\":\"x\",\"file_path\":\"voice/file_1.oga\",\"file_size\":3}",
                _ => "true",
            };
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($"{{\"ok\":true,\"result\":{result}}}", Encoding.UTF8, "application/json"),
            };
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
