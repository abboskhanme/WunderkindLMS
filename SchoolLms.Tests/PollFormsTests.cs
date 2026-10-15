using System.Net;
using System.Net.Http.Json;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// So'rovnoma Google Forms kabi (client, 2026-10-15: "So'rovnoma olish — so'rovnoma Google form holatda
/// bo'lishi"; docs/modules/surveys-forms.md): every question kind and its checks, the public link
/// `/sorovnoma/{token}` (anonymous and with name/phone), the "Javoblar" table and its Excel export,
/// "Nusxa olish", and that old polls and the KPI read exactly as before.
///
/// <para>
/// Public requests carry their own <c>X-Forwarded-For</c> from 192.0.2.0/24 (TEST-NET-1): without it
/// every anonymous request of the suite shares one rate-limit bucket (PublicSurveyAbuseTests uses
/// 203.0.113.0/24 for the same reason).
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class PollFormsTests(ApiFixture fixture)
{
    private const string Api = "/api/admin/polls";
    private const string Public = "/api/public/polls";
    private const string XlsxType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    // =================================================================
    //  Question kinds — create, answer in the Mini App, summaries, table, Excel
    // =================================================================

    [Fact]
    public async Task Har_turdagi_savol_bilan_forma_tuziladi_ota_ona_ilovada_javob_beradi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var poll = await CreateOpenAsync(admin, Form("parents", [
            Q("section", "Umumiy", description: "Maktab haqida"),
            Q("nps", "Tavsiya qilasizmi?"),
            Q("stars", "Ovqat", required: false),
            Q("choice", "Ta'lim sifati", options: ["Yaxshi", "O'rtacha", "Yomon"], allowOther: true),
            Q("multi", "Qaysi to'garaklar?", options: ["Sport", "Musiqa", "Rasm"], allowOther: true, minChoices: 1, maxChoices: 2),
            Q("dropdown", "Farzandingiz sinfi", options: ["5-sinf", "6-sinf", "7-sinf"], allowOther: true),
            Q("section", "Qo'shimcha"),
            Q("short", "Bir so'z bilan"),
            Q("text", "Takliflar", required: false),
            Q("scale", "Maktab muhiti", minValue: 0, maxValue: 4, minLabel: "Yomon", maxLabel: "A'lo"),
            Q("number", "Uydan maktabgacha necha km?", minValue: 0, maxValue: 100),
            Q("date", "Qachon kelgansiz?"),
        ]));
        var q = poll.Questions;
        Assert.Equal(12, q.Count);
        Assert.False(q[0].Required);                                      // a section is never required
        Assert.Equal("Maktab haqida", q[0].Description);
        Assert.True(q[3].AllowOther);
        Assert.Equal((1, 2), (q[4].MinChoices, q[4].MaxChoices));
        Assert.False(q[5].AllowOther);                                    // a dropdown has no "Boshqa"
        Assert.Equal((0m, 4m, "Yomon", "A'lo"), (q[9].MinValue, q[9].MaxValue, q[9].MinLabel, q[9].MaxLabel));
        Assert.Equal((0m, 100m), (q[10].MinValue, q[10].MaxValue));
        Assert.Null(poll.PublicToken);                                    // a parents poll is not on the link by default

        var (parent, studentId, className) = await SeedParentAsync();
        using (parent)
        {
            var open = Assert.Single((await parent.GetFromJsonAsync<List<PortalPollDto>>($"/api/student/polls?studentId={studentId}"))!, p => p.Id == poll.Id);
            Assert.Equal(12, open.Questions.Count);

            var ok = await parent.PostAsJsonAsync($"/api/student/polls/{poll.Id}?studentId={studentId}", new PollSubmitRequest([
                new(q[1].Id, null, 9, null, null),
                new(q[3].Id, null, null, null, "Juda yaxshi"),
                new(q[4].Id, null, null, null, "Shaxmat", Choices: ["Musiqa"]),
                new(q[5].Id, null, null, "6-sinf", null),
                new(q[7].Id, null, null, null, "Bir qator\nikkinchi"),
                new(q[9].Id, null, 3, null, null),
                new(q[10].Id, null, null, null, null, Number: 12.5m),
                new(q[11].Id, null, null, null, null, Date: "2026-10-01"),
            ]));
            Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);
        }

        var results = (await admin.GetFromJsonAsync<PollResultsDto>($"{Api}/{poll.Id}/results"))!;
        Assert.Equal(1, results.Responses);
        Assert.Equal("section", results.Questions[0].Kind);
        Assert.Equal(1, Assert.Single(results.Questions[3].Distribution, d => d.Label == PollService.OtherLabel).Count);
        Assert.Contains(results.Questions[3].Texts, t => t.Text == "Juda yaxshi");   // the "Boshqa" text is readable
        var multi = results.Questions[4].Distribution;
        Assert.Equal([("Sport", 0), ("Musiqa", 1), ("Rasm", 0), ("Boshqa", 1)], multi.Select(d => (d.Label, d.Count)));
        Assert.Equal(1, Assert.Single(results.Questions[5].Distribution, d => d.Label == "6-sinf").Count);
        Assert.Equal("Bir qator ikkinchi", Assert.Single(results.Questions[7].Texts).Text);   // short is one line
        Assert.Equal(["0", "1", "2", "3", "4"], results.Questions[9].Distribution.Select(d => d.Label));
        Assert.Equal(3.0, results.Questions[9].Average);
        Assert.Equal((12.5, 12.5, 12.5), (results.Questions[10].Min, results.Questions[10].Average, results.Questions[10].Max));
        Assert.Equal(("2026-10-01", 1), (results.Questions[11].Distribution[0].Label, results.Questions[11].Distribution[0].Count));

        var table = (await admin.GetFromJsonAsync<PollResponsesDto>($"{Api}/{poll.Id}/responses"))!;
        Assert.Equal(1, table.Total);
        Assert.False(table.ShowRespondent);                               // anonymous: no "Kim" column
        Assert.Equal(10, table.Columns.Count);                            // sections are not columns
        var row = Assert.Single(table.Rows);
        Assert.Null(row.Respondent);
        Assert.Equal(className, row.ClassName);
        Assert.False(row.ViaLink);
        Assert.Equal(
            ["9", "", "Boshqa: Juda yaxshi", "Musiqa, Boshqa: Shaxmat", "6-sinf", "Bir qator ikkinchi", "", "3", "12.5", "01.10.2026"],
            row.Cells);
        Assert.Equal(0, (await admin.GetFromJsonAsync<PollResponsesDto>($"{Api}/{poll.Id}/responses?className=Boshqa"))!.Total);

        var export = await admin.GetAsync($"{Api}/{poll.Id}/responses/export");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.Equal(XlsxType, export.Content.Headers.ContentType?.MediaType);
        var sheet = ReadSheet(await export.Content.ReadAsByteArrayAsync());
        Assert.Equal(poll.Title, sheet[0][0]);                            // title line, blank row, header, data
        Assert.Contains("Qaysi to'garaklar?", sheet[2]);
        Assert.Contains("Sinf", sheet[2]);
        Assert.Contains("Musiqa, Boshqa: Shaxmat", sheet[3]);
    }

    [Fact]
    public async Task Har_tur_uchun_javob_serverda_tekshiriladi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var poll = await CreateOpenAsync(admin, Form("public", [
            Q("scale", "Baho", minValue: 1, maxValue: 5),
            Q("multi", "Ikkitasini tanlang", required: false, options: ["A", "B", "C"], minChoices: 2, maxChoices: 2),
            Q("number", "Son", required: false, minValue: 1, maxValue: 10),
            Q("choice", "Ha yoki yo'q", required: false, options: ["Ha", "Yo'q"]),
            Q("dropdown", "Ro'yxat", required: false, options: ["X", "Y"]),
            Q("date", "Sana", required: false),
            Q("section", "Oxiri"),
            Q("short", "Qisqa", required: false),
        ]));
        var q = poll.Questions;
        using var anon = fixture.Api.AnonymousClient();
        var ip = NextIp();
        var scale = new PollAnswerInput(q[0].Id, null, 3, null, null);

        async Task<HttpResponseMessage> Send(params PollAnswerInput[] answers) =>
            await PublicPostAsync(anon, poll.PublicToken!, new PublicPollSubmitRequest([.. answers], NewBrowser(), null, null), ip);

        var missing = await Send();
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
        var body = (await missing.Content.ReadFromJsonAsync<ErrorBody>())!;
        Assert.Equal(PollService.CodeValidation, body.Code);
        Assert.Contains("javob bering", body.Message, StringComparison.Ordinal);

        PollAnswerInput[][] invalid =
        [
            [new(q[0].Id, null, 6, null, null)],                                         // scale above its end
            [new(q[0].Id, null, 0, null, null)],                                         // scale starts at 1
            [scale, new(q[1].Id, null, null, null, null, Choices: ["A"])],               // fewer than the minimum
            [scale, new(q[1].Id, null, null, null, null, Choices: ["A", "B", "C"])],     // more than the maximum
            [scale, new(q[1].Id, null, null, null, null, Choices: ["A", "Z"])],          // unknown option
            [scale, new(q[2].Id, null, null, null, null, Number: 11)],                   // number above max
            [scale, new(q[2].Id, null, null, null, null, Number: 0.5m)],                 // number below min
            [scale, new(q[3].Id, null, null, "Balki", null)],                            // unknown choice
            [scale, new(q[4].Id, null, null, "Z", null)],                                // unknown dropdown option
            [scale, new(q[5].Id, null, null, null, null, Date: "2026-13-01")],           // not a date
            [scale, new(q[6].Id, null, null, null, "bo'lim")],                           // a section takes no answer
            [scale, new(Guid.NewGuid(), null, 1, null, null)],                           // another poll's question
            [scale, scale],                                                              // one answer per question
        ];
        foreach (var answers in invalid)
        {
            var response = await Send(answers);
            Assert.True(response.StatusCode == HttpStatusCode.BadRequest,
                $"{System.Text.Json.JsonSerializer.Serialize(answers)} → {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        }

        var valid = await Send(scale,
            new(q[1].Id, null, null, null, null, Choices: ["C", "A"]),
            new(q[2].Id, null, null, null, null, Number: 10),
            new(q[3].Id, null, null, "Ha", null),
            new(q[4].Id, null, null, "Y", null),
            new(q[5].Id, null, null, null, null, Date: "2026-10-15"),
            new(q[7].Id, null, null, null, "ok"));
        Assert.Equal(HttpStatusCode.NoContent, valid.StatusCode);
        await fixture.Api.WithDbAsync(async db =>
        {
            var answers = await db.PollAnswers.Where(a => a.QuestionId == q[1].Id).ToListAsync();
            Assert.Equal(["A", "C"], Assert.Single(answers).Choices!);   // stored in the question's order
            Assert.Equal(1, await db.PollResponses.CountAsync(r => r.PollId == poll.Id));   // the rejected ones left nothing
        });
    }

    [Fact]
    public async Task Savol_sozlamalari_saqlashda_tekshiriladi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        object[][] invalid =
        [
            [Q("scale", "?", minValue: 2, maxValue: 5)],                                 // scale starts at 0 or 1
            [Q("scale", "?", minValue: 1, maxValue: 11)],                                // ends at 2..10
            [Q("multi", "?", options: ["A", "B", "C"], minChoices: 3, maxChoices: 2)],
            [Q("multi", "?", options: ["A", "B"], maxChoices: 3)],                       // more than there are options
            [Q("number", "?", minValue: 10, maxValue: 1)],
            [Q("dropdown", "?", options: [.. Enumerable.Range(1, 51).Select(i => $"V{i}")])],
            [Q("choice", "?", options: ["Ha", "Boshqa"], allowOther: true)],             // "Boshqa" comes from the switch
            [Q("multi", "?", options: ["Bitta"])],
            [Q("section", "Faqat bo'lim")],                                              // no question at all
            [Q("section", "")],
            [Q("nps", "Ustoz?", target: "teachers")],                                    // nobody to rate on the link
            [.. Enumerable.Range(1, 51).Select(i => Q("stars", $"Savol {i}"))],
        ];
        foreach (var questions in invalid)
        {
            var response = await admin.PostAsJsonAsync(Api, Form("public", questions));
            Assert.True(response.StatusCode == HttpStatusCode.BadRequest,
                $"{System.Text.Json.JsonSerializer.Serialize(questions)} → {(int)response.StatusCode}");
        }
        var contactButAnonymous = await admin.PostAsJsonAsync(Api, Form("public", [Q("stars", "?")], anonymous: true, collectContact: true));
        Assert.Equal(HttpStatusCode.BadRequest, contactButAnonymous.StatusCode);

        // 50 blocks, sections included, is the limit.
        var fifty = await admin.PostAsJsonAsync(Api, Form("parents",
            [Q("section", "Bo'lim"), .. Enumerable.Range(1, 49).Select(i => Q("stars", $"Savol {i}"))]));
        Assert.Equal(HttpStatusCode.OK, fifty.StatusCode);
        await admin.DeleteAsync($"{Api}/{(await fifty.Content.ReadFromJsonAsync<PollDto>())!.Id}");
    }

    // =================================================================
    //  The public link
    // =================================================================

    [Fact]
    public async Task Havola_orqali_anonim_javob_bir_brauzerdan_bir_marta()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var created = await admin.PostAsJsonAsync(Api, Form("public", [
            Q("section", "Kirish", description: "Ikki daqiqa"), Q("stars", "Tadbir qanday o'tdi?"), Q("text", "Fikringiz", required: false)]));
        var poll = (await created.Content.ReadFromJsonAsync<PollDto>())!;
        Assert.Equal(("public", true), (poll.Audience, poll.PublicLink));
        Assert.Equal(22, poll.PublicToken!.Length);
        using var anon = fixture.Api.AnonymousClient();
        var ip = NextIp();
        var token = poll.PublicToken;

        // A draft: the page says it is closed and shows nothing else.
        var draft = (await (await PublicGetAsync(anon, token, ip)).Content.ReadFromJsonAsync<PublicPollDto>())!;
        Assert.False(draft.Accepting);
        Assert.Empty(draft.Questions);
        var early = await PublicPostAsync(anon, token, Sheet(poll, NewBrowser()), ip);
        Assert.Equal(HttpStatusCode.BadRequest, early.StatusCode);
        Assert.Equal(PollService.CodeClosed, (await early.Content.ReadFromJsonAsync<ErrorBody>())!.Code);

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"{Api}/{poll.Id}/open", null)).StatusCode);
        var page = (await (await PublicGetAsync(anon, token, ip)).Content.ReadFromJsonAsync<PublicPollDto>())!;
        Assert.True(page.Accepting);
        Assert.True(page.Anonymous);
        Assert.False(page.CollectContact);
        Assert.Equal(["section", "stars", "text"], page.Questions.Select(x => x.Kind));

        var browserA = NewBrowser();
        Assert.Equal(HttpStatusCode.NoContent, (await PublicPostAsync(anon, token, Sheet(poll, browserA), ip)).StatusCode);
        var again = await PublicPostAsync(anon, token, Sheet(poll, browserA), ip);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal(PollService.CodeAnswered, (await again.Content.ReadFromJsonAsync<ErrorBody>())!.Code);
        Assert.Equal(HttpStatusCode.NoContent, (await PublicPostAsync(anon, token, Sheet(poll, NewBrowser()), ip)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PublicPostAsync(anon, token, Sheet(poll, "short"), ip)).StatusCode);

        await fixture.Api.WithDbAsync(async db =>
        {
            var rows = await db.PollResponses.Where(r => r.PollId == poll.Id).ToListAsync();
            Assert.Equal(2, rows.Count);
            Assert.All(rows, r => Assert.True(r.ViaLink && r.ContactName is null && r.StudentId is null));
            Assert.DoesNotContain(rows, r => r.RespondentKey.Contains(browserA, StringComparison.Ordinal));
        });
        Assert.Equal(2, (await admin.GetFromJsonAsync<PollResultsDto>($"{Api}/{poll.Id}/results"))!.Responses);

        // The bot has nobody to announce a link-only poll to.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync($"{Api}/{poll.Id}/notify", null)).StatusCode);
        Assert.Equal(0, (await admin.GetFromJsonAsync<RecipientsBody>($"{Api}/{poll.Id}/recipients"))!.Count);

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"{Api}/{poll.Id}/close", null)).StatusCode);
        Assert.False((await (await PublicGetAsync(anon, token, ip)).Content.ReadFromJsonAsync<PublicPollDto>())!.Accepting);
        var late = await PublicPostAsync(anon, token, Sheet(poll, NewBrowser()), ip);
        Assert.Equal(PollService.CodeClosed, (await late.Content.ReadFromJsonAsync<ErrorBody>())!.Code);

        // An unknown or malformed token: 404, on both endpoints.
        foreach (var wrong in new[] { PollService.NewToken(), "abc", token[..^1] + (token[^1] == 'A' ? 'B' : 'A') })
        {
            var get = await PublicGetAsync(anon, wrong, ip);
            Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
            Assert.Equal(PollService.CodeNotFound, (await get.Content.ReadFromJsonAsync<ErrorBody>())!.Code);
            Assert.Equal(HttpStatusCode.NotFound, (await PublicPostAsync(anon, wrong, Sheet(poll, NewBrowser()), ip)).StatusCode);
        }
    }

    [Fact]
    public async Task Havola_orqali_ism_va_telefon_soraladi_bir_raqamdan_bir_javob()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var poll = await CreateOpenAsync(admin, Form("public", [Q("stars", "Xizmat sifati")], anonymous: false, collectContact: true));
        Assert.True(poll.CollectContact);
        using var anon = fixture.Api.AnonymousClient();
        var ip = NextIp();
        var token = poll.PublicToken!;
        Assert.True((await (await PublicGetAsync(anon, token, ip)).Content.ReadFromJsonAsync<PublicPollDto>())!.CollectContact);

        var answers = new List<PollAnswerInput> { new(poll.Questions[0].Id, null, 5, null, null) };
        var digits = Random.Shared.Next(10_000_000, 99_999_999).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var phone = $"+998 9{digits[0]} {digits[1..4]} {digits[4..6]} {digits[6..8]}";

        Assert.Equal(HttpStatusCode.BadRequest, (await PublicPostAsync(anon, token, new PublicPollSubmitRequest(answers, NewBrowser(), null, phone), ip)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PublicPostAsync(anon, token, new PublicPollSubmitRequest(answers, NewBrowser(), "Aziz", "123"), ip)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await PublicPostAsync(anon, token, new PublicPollSubmitRequest(answers, NewBrowser(), " Aziz Karimov ", phone), ip)).StatusCode);
        // The same number from another browser is the same person.
        var twice = await PublicPostAsync(anon, token, new PublicPollSubmitRequest(answers, NewBrowser(), "Aziz", phone.Replace(" ", "", StringComparison.Ordinal)), ip);
        Assert.Equal(HttpStatusCode.Conflict, twice.StatusCode);

        var table = (await admin.GetFromJsonAsync<PollResponsesDto>($"{Api}/{poll.Id}/responses"))!;
        Assert.True(table.ShowRespondent && table.ShowPhone);
        Assert.False(table.ShowSource);                                   // everyone on a public poll came by the link
        var row = Assert.Single(table.Rows);
        Assert.Equal(("Aziz Karimov", phone), (row.Respondent, row.Phone));
        Assert.Equal(["5"], row.Cells);

        var sheet = ReadSheet(await (await admin.GetAsync($"{Api}/{poll.Id}/responses/export")).Content.ReadAsByteArrayAsync());
        Assert.Equal(["№", "Vaqt", "Kim", "Telefon", "Xizmat sifati"], sheet[2]);
        Assert.Equal("Aziz Karimov", sheet[3][2]);
    }

    [Fact]
    public async Task Ota_ona_sorovnomasi_havola_orqali_ham_ochiladi_va_yopiladi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        object[] questions = [Q("nps", "Ustozni tavsiya qilasizmi?", target: "teachers"), Q("stars", "Maktab")];
        var poll = await CreateOpenAsync(admin, Form("parents", questions, publicLink: true));
        Assert.True(poll.PublicLink);
        Assert.NotNull(poll.PublicToken);
        using var anon = fixture.Api.AnonymousClient();
        var ip = NextIp();

        // Per-person questions stay in the Mini App: the link shows the school question only.
        var page = (await (await PublicGetAsync(anon, poll.PublicToken!, ip)).Content.ReadFromJsonAsync<PublicPollDto>())!;
        Assert.Equal("stars", Assert.Single(page.Questions).Kind);
        var sent = await PublicPostAsync(anon, poll.PublicToken!,
            new PublicPollSubmitRequest([new(poll.Questions[1].Id, null, 4, null, null)], NewBrowser(), null, null), ip);
        Assert.Equal(HttpStatusCode.NoContent, sent.StatusCode);

        var table = (await admin.GetFromJsonAsync<PollResponsesDto>($"{Api}/{poll.Id}/responses"))!;
        Assert.True(table.ShowSource);
        Assert.True(Assert.Single(table.Rows).ViaLink);

        // Answered: the link may still be switched off; the questions may not change.
        var off = await admin.PutAsJsonAsync($"{Api}/{poll.Id}", Form("parents", questions, publicLink: false));
        Assert.Equal(HttpStatusCode.OK, off.StatusCode);
        Assert.Null((await off.Content.ReadFromJsonAsync<PollDto>())!.PublicToken);
        Assert.Equal(HttpStatusCode.NotFound, (await PublicGetAsync(anon, poll.PublicToken!, ip)).StatusCode);
        var changed = await admin.PutAsJsonAsync($"{Api}/{poll.Id}", Form("parents", [Q("stars", "Maktab")]));
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
    }

    [Fact]
    public async Task Havola_chastota_chegarasi_30_tadan_keyin_429()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var poll = await CreateOpenAsync(admin, Form("public", [Q("stars", "?")]));
        using var anon = fixture.Api.AnonymousClient();
        var ip = NextIp();
        var empty = new PublicPollSubmitRequest([], null, null, null);
        for (var i = 1; i <= 30; i++)
        {
            var response = await PublicPostAsync(anon, poll.PublicToken!, empty, ip);
            Assert.True(response.StatusCode == HttpStatusCode.BadRequest, $"{i}-so'rov: {(int)response.StatusCode}");
        }
        Assert.Equal(HttpStatusCode.TooManyRequests, (await PublicPostAsync(anon, poll.PublicToken!, empty, ip)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await PublicPostAsync(anon, poll.PublicToken!, empty, NextIp())).StatusCode);
    }

    // =================================================================
    //  Old polls and the KPI
    // =================================================================

    [Fact]
    public async Task Eski_sorovnoma_javoblari_oqiladi_va_nomi_ozgaradi()
    {
        // Rows as they were before 2026-10-15: no token, no new settings, answers in score/choice/text.
        var poll = new Poll { Title = "Eski", Audience = PollAudience.Parents, Status = PollStatus.Closed, CreatedBy = "test", CreatedAt = AppClock.NowInstant };
        var choice = new PollQuestion { PollId = poll.Id, Position = 1, Kind = PollQuestionKind.Choice, Text = "Ovqat", Options = "Ha\nYo'q" };
        var stars = new PollQuestion { PollId = poll.Id, Position = 2, Kind = PollQuestionKind.Stars, Text = "Baho" };
        var text = new PollQuestion { PollId = poll.Id, Position = 3, Kind = PollQuestionKind.Text, Text = "Fikr", Required = false };
        poll.Questions = [choice, stars, text];
        var response = new PollResponse { PollId = poll.Id, RespondentKey = Guid.NewGuid().ToString("N"), ClassName = "5-A", SubmittedAt = AppClock.NowInstant };
        response.Answers.AddRange([
            new PollAnswer { ResponseId = response.Id, QuestionId = choice.Id, Choice = "Ha" },
            new PollAnswer { ResponseId = response.Id, QuestionId = stars.Id, Score = 4 },
            new PollAnswer { ResponseId = response.Id, QuestionId = text.Id, Text = "Eski fikr" }]);
        await fixture.Api.WithDbAsync(async db =>
        {
            db.Polls.Add(poll);
            db.PollResponses.Add(response);
            await db.SaveChangesAsync();
        });

        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var results = (await admin.GetFromJsonAsync<PollResultsDto>($"{Api}/{poll.Id}/results"))!;
        Assert.Equal([("Ha", 1), ("Yo'q", 0)], results.Questions[0].Distribution.Select(d => (d.Label, d.Count)));
        Assert.Equal(1, Assert.Single(results.Questions[1].Distribution, d => d.Label == "4").Count);
        Assert.Equal("Eski fikr", Assert.Single(results.Questions[2].Texts).Text);
        Assert.Equal(["Ha", "4", "Eski fikr"], Assert.Single((await admin.GetFromJsonAsync<PollResponsesDto>($"{Api}/{poll.Id}/responses"))!.Rows).Cells);

        // Answered: the same questions sent back count as unchanged, so the title may change.
        object[] same = [Q("choice", "Ovqat", options: ["Ha", "Yo'q"]), Q("stars", "Baho"), Q("text", "Fikr", required: false)];
        var renamed = await admin.PutAsJsonAsync($"{Api}/{poll.Id}", Form("parents", same, title: "Eski (yangi nom)"));
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Null((await renamed.Content.ReadFromJsonAsync<PollDto>())!.PublicToken);   // has one now, but the link is off
        var linked = await admin.PutAsJsonAsync($"{Api}/{poll.Id}", Form("parents", same, title: "Eski", publicLink: true));
        Assert.NotNull((await linked.Content.ReadFromJsonAsync<PollDto>())!.PublicToken);
        var anonymity = await admin.PutAsJsonAsync($"{Api}/{poll.Id}", Form("parents", same, anonymous: false));
        Assert.Equal(HttpStatusCode.Conflict, anonymity.StatusCode);
    }

    [Fact]
    public async Task Kpi_faqat_nps_ni_oladi_shkala_kirmaydi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var manager = await SeedManagerAsync();
        var (teacher, _) = await fixture.Api.SeedUserAsync(Roles.Teacher);
        var poll = await CreateOpenAsync(admin, Form("teachers", [
            Q("nps", "Rahbaringizni tavsiya qilasizmi?", target: "management"),
            Q("scale", "Rahbar bilan ishlash", target: "management", minValue: 1, maxValue: 10),
            Q("multi", "Nima yoqadi?", required: false, options: ["Ochiqlik", "Talabchanlik"]),
        ]));
        using var client = fixture.Api.ClientWithToken(fixture.Api.TokenFor(Roles.Teacher, teacher.Id, teacher.FullName, teacher.Email));
        var q = poll.Questions;
        var ok = await client.PostAsJsonAsync($"/api/teacher/polls/{poll.Id}", new PollSubmitRequest([
            new(q[0].Id, manager.Id, 10, null, null),
            new(q[1].Id, manager.Id, 2, null, null),
            new(q[2].Id, null, null, null, null, Choices: ["Ochiqlik", "Talabchanlik"])]));
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);

        var row = Assert.Single((await admin.GetFromJsonAsync<KpiReportDto>("/api/admin/kpi"))!.Rows, r => r.EmployeeId == manager.Id);
        Assert.Equal(1, row.StaffAnswers);                                // the scale answer is not an NPS
        Assert.Equal(100, row.StaffNpsRaw);
        var cells = Assert.Single((await admin.GetFromJsonAsync<PollResponsesDto>($"{Api}/{poll.Id}/responses"))!.Rows).Cells;
        Assert.Equal($"{manager.FullName}: 10", cells[0]);                // per-person cells name the person
        Assert.Equal("Ochiqlik, Talabchanlik", cells[2]);
    }

    // =================================================================
    //  Nusxa olish, RBAC
    // =================================================================

    [Fact]
    public async Task Nusxa_olish_yangi_qoralama_yaratadi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var title = new string('S', 200);
        var source = await CreateOpenAsync(admin, Form("public", [
            Q("section", "Bo'lim"), Q("multi", "Tanlang", options: ["A", "B", "C"], allowOther: true, minChoices: 1, maxChoices: 2),
            Q("scale", "Shkala", minValue: 0, maxValue: 10, minLabel: "Past", maxLabel: "Yuqori")],
            anonymous: false, collectContact: true, title: title, closesOn: AppClock.Today.AddDays(5).ToString("yyyy-MM-dd")));

        var response = await admin.PostAsync($"{Api}/{source.Id}/duplicate", null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var copy = (await response.Content.ReadFromJsonAsync<PollDto>())!;
        Assert.NotEqual(source.Id, copy.Id);
        Assert.Equal(("draft", 0, (string?)null), (copy.Status, copy.Responses, copy.ClosesOn));
        Assert.Equal(200, copy.Title.Length);
        Assert.EndsWith(" (nusxa)", copy.Title, StringComparison.Ordinal);
        Assert.True(copy.CollectContact);
        Assert.NotNull(copy.PublicToken);
        Assert.NotEqual(source.PublicToken, copy.PublicToken);
        Assert.Equal(
            source.Questions.Select(x => (x.Kind, x.Text, string.Join('|', x.Options), x.AllowOther, x.MinValue, x.MaxValue, x.MinLabel, x.MaxChoices)),
            copy.Questions.Select(x => (x.Kind, x.Text, string.Join('|', x.Options), x.AllowOther, x.MinValue, x.MaxValue, x.MinLabel, x.MaxChoices)));
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync($"{Api}/{Guid.NewGuid()}/duplicate", null)).StatusCode);
    }

    [Theory]
    [InlineData(Roles.Staff)]
    [InlineData(Roles.Teacher)]
    [InlineData(Roles.Cashier)]
    public async Task Ruxsatsiz_rol_javoblar_eksport_va_nusxani_ochmaydi(string role)
    {
        using var client = await fixture.Api.ClientAsAsync(role);
        var id = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"{Api}/{id}/responses")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"{Api}/{id}/responses/export")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsync($"{Api}/{id}/duplicate", null)).StatusCode);
    }

    [Fact]
    public async Task Tizimga_kirmagan_admin_endpointlarini_ochmaydi_ruxsatli_xodim_ochadi()
    {
        using var anon = fixture.Api.AnonymousClient();
        var id = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync(Api)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync($"{Api}/{id}/responses")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.GetAsync($"{Api}/{id}/responses/export")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsync($"{Api}/{id}/duplicate", null)).StatusCode);

        using var staff = await fixture.Api.ClientAsAsync(Roles.Staff, PollService.Permission);
        var poll = await CreateOpenAsync(staff, Form("public", [Q("stars", "?")]));
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync($"{Api}/{poll.Id}/responses")).StatusCode);
        var export = await staff.GetAsync($"{Api}/{poll.Id}/responses/export");
        Assert.Equal(HttpStatusCode.OK, export.StatusCode);
        Assert.Equal(XlsxType, export.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.OK, (await staff.PostAsync($"{Api}/{poll.Id}/duplicate", null)).StatusCode);
    }

    // =================================================================

    private sealed record ErrorBody(string? Code, string? Message);

    private sealed record RecipientsBody(int Count, bool BotConfigured);

    private static int _lastOctet;

    /// <summary>A fresh 192.0.2.x per test — its own rate-limit bucket.</summary>
    private static string NextIp()
    {
        var n = Interlocked.Increment(ref _lastOctet);
        Assert.True(n <= 254, "192.0.2.0/24 manzillari tugadi");
        return $"192.0.2.{n}";
    }

    private static string NewBrowser() => Guid.NewGuid().ToString();

    private static object Q(
        string kind, string text, bool required = true, string target = "school", string[]? options = null, bool allowOther = false,
        decimal? minValue = null, decimal? maxValue = null, string? minLabel = null, string? maxLabel = null,
        int? minChoices = null, int? maxChoices = null, string? description = null) =>
        new { kind, text, options, required, target, description, allowOther, minValue, maxValue, minLabel, maxLabel, minChoices, maxChoices };

    private static object Form(
        string audience, object[] questions, bool anonymous = true, bool publicLink = false, bool collectContact = false,
        string title = "Forma", string? closesOn = null) =>
        new { title, description = "Izoh", audience, anonymous, closesOn, questions, publicLink, collectContact };

    /// <summary>The anonymous-poll answer sheet of the link test: five stars on the stars question.</summary>
    private static PublicPollSubmitRequest Sheet(PollDto poll, string browser) =>
        new([new(poll.Questions.Single(x => x.Kind == "stars").Id, null, 5, null, null)], browser, null, null);

    private static async Task<PollDto> CreateOpenAsync(HttpClient admin, object input)
    {
        var created = await admin.PostAsJsonAsync(Api, input);
        Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());
        var poll = (await created.Content.ReadFromJsonAsync<PollDto>())!;
        var opened = await admin.PostAsync($"{Api}/{poll.Id}/open", null);
        Assert.True(opened.IsSuccessStatusCode, await opened.Content.ReadAsStringAsync());
        return (await opened.Content.ReadFromJsonAsync<PollDto>())!;
    }

    private static async Task<HttpResponseMessage> PublicGetAsync(HttpClient client, string token, string ip)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{Public}/{token}");
        request.Headers.Add("X-Forwarded-For", ip);
        return await client.SendAsync(request);
    }

    private static async Task<HttpResponseMessage> PublicPostAsync(HttpClient client, string token, object body, string ip)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{Public}/{token}") { Content = JsonContent.Create(body) };
        request.Headers.Add("X-Forwarded-For", ip);
        return await client.SendAsync(request);
    }

    /// <summary>A parent signed in with the phone their child's card carries (no timetable — school questions only).</summary>
    private async Task<(HttpClient Client, string StudentId, string ClassName)> SeedParentAsync()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var phone = "99893" + Random.Shared.Next(1_000_000, 9_999_999);
        var (parentUser, _) = await fixture.Api.SeedUserAsync("parent", email: phone);
        var student = GeneralSettingsFlagsTests.NewStudent($"Bola {tag}", $"PF-{tag}", phone);
        await fixture.Api.WithDbAsync(async db =>
        {
            db.Students.Add(student);
            await db.SaveChangesAsync();
        });
        return (fixture.Api.ClientWithToken(fixture.Api.TokenFor("parent", parentUser.Id, "Ota-ona", phone)), student.Id, student.ClassName);
    }

    private async Task<HrEmployee> SeedManagerAsync()
    {
        var (user, _) = await fixture.Api.SeedUserAsync(Roles.Staff);
        HrEmployee employee = null!;
        await fixture.Api.WithDbAsync(async db =>
        {
            employee = await db.HrEmployees.FirstOrDefaultAsync(e => e.UserId == user.Id)
                       ?? db.HrEmployees.Add(new HrEmployee { UserId = user.Id, FullName = "Rahbar " + user.Id[..6] }).Entity;
            employee.HrRole = HrRoles.Manager;
            employee.IsActive = true;
            await db.SaveChangesAsync();
        });
        return employee;
    }

    /// <summary>Every row of the first sheet as text (inline strings), empty rows included.</summary>
    private static List<List<string>> ReadSheet(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var doc = SpreadsheetDocument.Open(stream, isEditable: false);
        var wb = doc.WorkbookPart!;
        var ws = (WorksheetPart)wb.GetPartById(wb.Workbook.Descendants<Sheet>().First().Id!.Value!);
        return [.. ws.Worksheet.Descendants<Row>().Select(r => r.Elements<Cell>().Select(c => c.InlineString?.Text?.Text ?? c.CellValue?.Text ?? "").ToList())];
    }
}
