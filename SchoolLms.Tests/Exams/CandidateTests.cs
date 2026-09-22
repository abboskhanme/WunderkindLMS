using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests.Exams;

// ===========================================================================
//  Nomzodlar (unit B5) — docs/modules/admission-and-testing.md §2.2, §6.1,
//  §8.5: the candidate register, the candidate card and the human decision,
//  under /api/admin/leads but gated on `admission` (LeadCandidatesController).
//
//  The online test is deferred (2026-09-22), so candidates reach `invited`
//  and `tested` the paper way: added to an admission exam, then scored in the
//  entry grid — exactly the ExamService path B2 built.
//
//  Shared database: every test works under a random tag and filters the
//  register with `search=<tag>`, so another test's candidates never count.
// ===========================================================================

[Collection(SchoolLmsCollection.Name)]
public class CandidateTests(ApiFixture fixture)
{
    private const string Leads = "/api/admin/leads";
    private const string Candidates = "/api/admin/leads/candidates";
    private const string Exams = "/api/admin/exams";

    private AppDbContext NewDb() => PostgresFixture.NewContext(fixture.Database.OwnerConnectionString);

    /// <summary>
    /// Letters only: the register's search also matches phone digits, so a hex
    /// tag such as "…901…" would pull in every candidate with a "+99890…" phone.
    /// </summary>
    private static string Tag() =>
        string.Concat(Guid.NewGuid().ToString("N")[..10].Select(c => (char)('a' + Convert.ToInt32(c.ToString(), 16))));

    private static string Today => AppClock.Today.ToString("yyyy-MM-dd");

    // =====================================================================
    //  1. The register: who is listed, the filters, the row
    // =====================================================================

    /// <summary>
    /// Only leads with <c>admission_status &lt;&gt; 'none'</c> are listed, by name.
    /// <c>admissionStatus</c>, <c>grade</c> and <c>examId</c> filter; the row
    /// summarises the newest participation, or the filtered exam's one.
    /// </summary>
    [Fact]
    public async Task Nomzodlar_royxati_filtrlar_va_qator()
    {
        var tag = Tag();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var subject = await SeedSubjectAsync($"Mantiq {tag}");
        var examA = await CreateAdmissionPaperAsync(admin, $"Qabul A {tag}", subject, grade: 5);
        var examB = await CreateAdmissionPaperAsync(admin, $"Qabul B {tag}", subject, grade: 5);

        var aziza = await SeedLeadAsync($"A Aziza {tag}", grade: 5);
        var bobur = await SeedLeadAsync($"B Bobur {tag}", grade: 6);
        var dilnoza = await SeedLeadAsync($"D Dilnoza {tag}", grade: 5);
        await SeedLeadAsync($"C Ordinary {tag}", grade: 5); // never on an exam → not a candidate

        await AddLeadsAsync(admin, examA, aziza, bobur);
        await AddLeadsAsync(admin, examB, aziza, dilnoza);
        await BackdateAsync(examA, aziza, days: 2);   // Aziza: A is older, B is newest
        await ScoreAsync(admin, examA, bobur, 64.5m);  // Bobur → tested
        await OkAsync(await admin.PatchAsJsonAsync($"{Leads}/{dilnoza}/admission-status", new { status = "rejected" }));

        // Everyone in the pipeline, by name; the ordinary lead is not a candidate.
        var all = await OkJsonAsync(await admin.GetAsync($"{Candidates}?search={tag}"));
        Assert.Equal(3, all.GetProperty("total").GetInt32());
        Assert.Equal(1, all.GetProperty("page").GetInt32());
        Assert.Equal(50, all.GetProperty("limit").GetInt32());
        Assert.Equal(new[] { aziza, bobur, dilnoza }, Ids(all));

        var azizaRow = Row(all, aziza);
        Assert.Equal($"A Aziza {tag}", azizaRow.GetProperty("fullName").GetString());
        Assert.Equal(5, azizaRow.GetProperty("targetGrade").GetInt32());
        Assert.Equal("invited", azizaRow.GetProperty("admissionStatus").GetString());
        Assert.Equal(examB, azizaRow.GetProperty("examId").GetString()); // newest participation
        Assert.Equal($"Qabul B {tag}", azizaRow.GetProperty("examTitle").GetString());
        Assert.Equal("assigned", azizaRow.GetProperty("participantStatus").GetString());
        Assert.Equal("none", azizaRow.GetProperty("invitationState").GetString());
        Assert.Equal(JsonValueKind.Null, azizaRow.GetProperty("totalPoints").ValueKind);
        Assert.Equal(JsonValueKind.Null, azizaRow.GetProperty("studentId").ValueKind);

        var boburRow = Row(all, bobur);
        Assert.Equal("tested", boburRow.GetProperty("admissionStatus").GetString());
        Assert.Equal("finished", boburRow.GetProperty("participantStatus").GetString());
        Assert.Equal(64.5m, boburRow.GetProperty("totalPoints").GetDecimal());
        Assert.Equal(100m, boburRow.GetProperty("maxPoints").GetDecimal());
        Assert.Equal(64.5m, boburRow.GetProperty("percent").GetDecimal());

        // Status, grade, exam.
        Assert.Equal(new[] { bobur }, Ids(await OkJsonAsync(await admin.GetAsync($"{Candidates}?search={tag}&admissionStatus=tested"))));
        Assert.Equal(new[] { dilnoza }, Ids(await OkJsonAsync(await admin.GetAsync($"{Candidates}?search={tag}&admissionStatus=rejected"))));
        Assert.Equal(new[] { bobur }, Ids(await OkJsonAsync(await admin.GetAsync($"{Candidates}?search={tag}&grade=6"))));
        var onA = await OkJsonAsync(await admin.GetAsync($"{Candidates}?search={tag}&examId={examA}"));
        Assert.Equal(new[] { aziza, bobur }, Ids(onA));
        Assert.Equal(examA, Row(onA, aziza).GetProperty("examId").GetString()); // the filtered exam, not the newest

        // Paging.
        var second = await OkJsonAsync(await admin.GetAsync($"{Candidates}?search={tag}&page=2&limit=2"));
        Assert.Equal((3, 2, 2), (second.GetProperty("total").GetInt32(), second.GetProperty("page").GetInt32(), second.GetProperty("limit").GetInt32()));
        Assert.Equal(new[] { dilnoza }, Ids(second));

        // `none` is not a candidate status; an unknown value is refused, not ignored.
        await ErrorAsync(await admin.GetAsync($"{Candidates}?search={tag}&admissionStatus=none"), HttpStatusCode.BadRequest, "validation");
        await ErrorAsync(await admin.GetAsync($"{Candidates}?search={tag}&admissionStatus=enrolled"), HttpStatusCode.BadRequest, "validation");
    }

    /// <summary>The phone is found with or without its separators.</summary>
    [Fact]
    public async Task Nomzod_telefon_boyicha_topiladi()
    {
        var tag = Tag();
        var local = Random.Shared.Next(1_000_000, 9_999_999).ToString();
        var phone = $"+998 (93) {local[..3]}-{local[3..5]}-{local[5..]}";
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var subject = await SeedSubjectAsync($"Ingliz {tag}");
        var exam = await CreateAdmissionPaperAsync(admin, $"Qabul tel {tag}", subject, grade: 4);
        var lead = await SeedLeadAsync($"Telefonli {tag}", grade: 4, phone: phone);
        await AddLeadsAsync(admin, exam, lead);

        Assert.Equal(new[] { lead }, Ids(await OkJsonAsync(await admin.GetAsync($"{Candidates}?search=93{local}"))));
        Assert.Equal(new[] { lead }, Ids(await OkJsonAsync(await admin.GetAsync(
            $"{Candidates}?search={Uri.EscapeDataString($"{local[..3]}-{local[3..5]}")}"))));
    }

    // =====================================================================
    //  2. The card
    // =====================================================================

    /// <summary>
    /// The row fields, the read-only lead fields, and every participation
    /// newest first with its exam; the row part describes <c>participations[0]</c>.
    /// A lead that is not a candidate still opens (no participations); an
    /// unknown id is 404.
    /// </summary>
    [Fact]
    public async Task Nomzod_kartochkasi_shakli()
    {
        var tag = Tag();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var subject = await SeedSubjectAsync($"Kimyo {tag}");
        var older = await CreateAdmissionPaperAsync(admin, $"Qabul eski {tag}", subject, grade: 7);
        var newer = await CreateAdmissionPaperAsync(admin, $"Qabul yangi {tag}", subject, grade: 7);
        var stageTitle = $"Sinovda {tag}";
        var lead = await SeedLeadAsync($"Kartochka {tag}", grade: 7, stageTitle: stageTitle, note: "Aka-ukasi 9-sinfda");

        await AddLeadsAsync(admin, older, lead);
        await ScoreAsync(admin, older, lead, 81m);
        await AddLeadsAsync(admin, newer, lead);
        await BackdateAsync(older, lead, days: 3);

        var card = await OkJsonAsync(await admin.GetAsync($"{Leads}/{lead}/admission"));
        Assert.Equal(lead, card.GetProperty("leadId").GetString());
        Assert.Equal($"Kartochka {tag}", card.GetProperty("fullName").GetString());
        Assert.Equal("+998901234567", card.GetProperty("parentPhone").GetString());
        Assert.Equal(7, card.GetProperty("targetGrade").GetInt32());
        Assert.Equal("tested", card.GetProperty("admissionStatus").GetString());
        Assert.Equal("female", card.GetProperty("gender").GetString());
        Assert.Equal("2016-03-14", card.GetProperty("birthDate").GetString());
        Assert.Equal("Karimov Aziz", card.GetProperty("parentFullName").GetString());
        Assert.Equal("Aka-ukasi 9-sinfda", card.GetProperty("note").GetString());
        Assert.Equal(stageTitle, card.GetProperty("stageName").GetString());
        Assert.False(string.IsNullOrEmpty(card.GetProperty("stage").GetString()));
        Assert.Equal("none", card.GetProperty("invitationState").GetString());
        Assert.Equal(JsonValueKind.Null, card.GetProperty("studentId").ValueKind);

        var participations = card.GetProperty("participations").EnumerateArray().ToList();
        Assert.Equal(new[] { newer, older }, participations.Select(p => p.GetProperty("examId").GetString()));

        // The row part is participations[0].
        Assert.Equal(newer, card.GetProperty("examId").GetString());
        Assert.Equal("assigned", card.GetProperty("participantStatus").GetString());
        Assert.Equal(JsonValueKind.Null, card.GetProperty("totalPoints").ValueKind);

        var scored = participations[1];
        Assert.False(string.IsNullOrEmpty(scored.GetProperty("participantId").GetString()));
        Assert.Equal($"Qabul eski {tag}", scored.GetProperty("examTitle").GetString());
        Assert.Equal("draft", scored.GetProperty("examStatus").GetString());
        Assert.Equal("manual", scored.GetProperty("delivery").GetString());
        Assert.Equal(7, scored.GetProperty("grade").GetInt32());
        Assert.Equal(Today, scored.GetProperty("examDate").GetString());
        Assert.Equal(JsonValueKind.Null, scored.GetProperty("opensAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, scored.GetProperty("timeLimitMin").ValueKind);
        Assert.Equal("finished", scored.GetProperty("status").GetString());
        Assert.Equal(81m, scored.GetProperty("totalPoints").GetDecimal());
        Assert.Equal(100m, scored.GetProperty("maxPoints").GetDecimal());
        Assert.Equal(81m, scored.GetProperty("percent").GetDecimal());
        Assert.NotEqual(JsonValueKind.Null, scored.GetProperty("scoredAt").ValueKind);
        Assert.NotEqual(JsonValueKind.Null, scored.GetProperty("createdAt").ValueKind);
        Assert.Equal(JsonValueKind.Null, scored.GetProperty("invitation").ValueKind);

        // An ordinary lead opens too — the card offers no decision for it.
        var plain = await SeedLeadAsync($"Oddiy {tag}", grade: 7);
        var plainCard = await OkJsonAsync(await admin.GetAsync($"{Leads}/{plain}/admission"));
        Assert.Equal("none", plainCard.GetProperty("admissionStatus").GetString());
        Assert.Empty(plainCard.GetProperty("participations").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, plainCard.GetProperty("examId").ValueKind);

        await ErrorAsync(await admin.GetAsync($"{Leads}/{Guid.NewGuid()}/admission"), HttpStatusCode.NotFound, "lead_not_found");
    }

    // =====================================================================
    //  3. The decision (§8.5)
    // =====================================================================

    /// <summary>
    /// <c>accepted</c> and <c>rejected</c> are set and audited, both ways;
    /// repeating one is a no-op. <c>enrolled</c>, the engine's statuses and
    /// unknown values are 400; a lead that is not a candidate is 409; an
    /// unknown lead is 404.
    /// </summary>
    [Fact]
    public async Task Qaror_faqat_qabul_yoki_rad()
    {
        var tag = Tag();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var subject = await SeedSubjectAsync($"Biologiya {tag}");
        var exam = await CreateAdmissionPaperAsync(admin, $"Qabul qaror {tag}", subject, grade: 5);
        var lead = await SeedLeadAsync($"Qaror {tag}", grade: 5);
        await AddLeadsAsync(admin, exam, lead);

        await OkAsync(await admin.PatchAsJsonAsync($"{Leads}/{lead}/admission-status", new { status = "accepted" }));
        Assert.Equal(LeadAdmissionStatus.Accepted, await StatusOfAsync(lead));
        await OkAsync(await admin.PatchAsJsonAsync($"{Leads}/{lead}/admission-status", new { status = "rejected" }));
        Assert.Equal(LeadAdmissionStatus.Rejected, await StatusOfAsync(lead));
        await OkAsync(await admin.PatchAsJsonAsync($"{Leads}/{lead}/admission-status", new { status = "rejected" }));

        await using (var db = NewDb())
        {
            var audit = await db.AuditLogs.AsNoTracking()
                .Where(a => a.EntityType == AuditService.EntityLead && a.EntityId == lead && a.Action == "update")
                .ToListAsync();
            Assert.Equal(2, audit.Count); // the repeat wrote nothing
        }

        await ErrorAsync(await admin.PatchAsJsonAsync($"{Leads}/{lead}/admission-status", new { status = "enrolled" }),
            HttpStatusCode.BadRequest, "enrol_via_endpoint");
        await ErrorAsync(await admin.PatchAsJsonAsync($"{Leads}/{lead}/admission-status", new { status = "tested" }),
            HttpStatusCode.BadRequest, "validation");
        await ErrorAsync(await admin.PatchAsJsonAsync($"{Leads}/{lead}/admission-status", new { status = "none" }),
            HttpStatusCode.BadRequest, "validation");
        await ErrorAsync(await admin.PatchAsJsonAsync($"{Leads}/{lead}/admission-status", new { status = "maybe" }),
            HttpStatusCode.BadRequest, "validation");
        await ErrorAsync(await admin.PatchAsJsonAsync($"{Leads}/{lead}/admission-status", new { }),
            HttpStatusCode.BadRequest, "validation");
        Assert.Equal(LeadAdmissionStatus.Rejected, await StatusOfAsync(lead)); // nothing refused changed it

        var plain = await SeedLeadAsync($"Nomzod emas {tag}", grade: 5);
        await ErrorAsync(await admin.PatchAsJsonAsync($"{Leads}/{plain}/admission-status", new { status = "accepted" }),
            HttpStatusCode.Conflict, "not_candidate");
        Assert.Equal(LeadAdmissionStatus.None, await StatusOfAsync(plain));

        await ErrorAsync(await admin.PatchAsJsonAsync($"{Leads}/{Guid.NewGuid()}/admission-status", new { status = "accepted" }),
            HttpStatusCode.NotFound, "lead_not_found");
    }

    // =====================================================================
    //  4. Permissions (§4.4)
    // =====================================================================

    /// <summary>
    /// Gated on <c>admission</c>, not <c>leads</c>: staff read freely, need
    /// <c>admission</c> to decide, and <c>leads</c> alone does not grant it.
    /// Other roles are refused; anonymous is 401. The board's own
    /// <c>GET /api/admin/leads</c> is unaffected by the shared route prefix.
    /// </summary>
    [Fact]
    public async Task Ruxsat_admission_boyicha_oqish_ochiq()
    {
        var tag = Tag();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        using var bare = await fixture.Api.ClientAsAsync(Roles.Staff);
        using var leadsOnly = await fixture.Api.ClientAsAsync(Roles.Staff, "leads");
        using var admissionOnly = await fixture.Api.ClientAsAsync(Roles.Staff, "admission");
        using var teacher = await fixture.Api.ClientAsAsync(Roles.Teacher);
        using var anonymous = fixture.Api.AnonymousClient();

        var subject = await SeedSubjectAsync($"Geografiya {tag}");
        var exam = await CreateAdmissionPaperAsync(admin, $"Qabul ruxsat {tag}", subject, grade: 5);
        var lead = await SeedLeadAsync($"Ruxsat {tag}", grade: 5);
        await AddLeadsAsync(admin, exam, lead);

        Assert.Equal(HttpStatusCode.OK, (await bare.GetAsync($"{Candidates}?search={tag}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await bare.GetAsync($"{Leads}/{lead}/admission")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await bare.PatchAsJsonAsync($"{Leads}/{lead}/admission-status", new { status = "accepted" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await leadsOnly.PatchAsJsonAsync($"{Leads}/{lead}/admission-status", new { status = "accepted" })).StatusCode);
        Assert.Equal(LeadAdmissionStatus.Invited, await StatusOfAsync(lead));

        await OkAsync(await admissionOnly.PatchAsJsonAsync($"{Leads}/{lead}/admission-status", new { status = "accepted" }));
        Assert.Equal(LeadAdmissionStatus.Accepted, await StatusOfAsync(lead));

        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync($"{Candidates}?search={tag}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync($"{Leads}/{lead}/admission")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync($"{Candidates}?search={tag}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PatchAsJsonAsync($"{Leads}/{lead}/admission-status", new { status = "rejected" })).StatusCode);

        var board = await OkJsonAsync(await admin.GetAsync(Leads));
        Assert.Equal(JsonValueKind.Array, board.ValueKind);
        Assert.Contains(board.EnumerateArray(), l => l.GetProperty("id").GetString() == lead);
    }

    // =====================================================================
    //  5. Enrolment deletes the lead — the candidate is gone (§2.2)
    // =====================================================================

    [Fact]
    public async Task Oquvchi_qilingan_nomzod_royxatda_yoq()
    {
        var tag = Tag();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var subject = await SeedSubjectAsync($"Tarix {tag}");
        var exam = await CreateAdmissionPaperAsync(admin, $"Qabul enrol {tag}", subject, grade: 5);
        var name = $"Enrol {tag}";
        var lead = await SeedLeadAsync(name, grade: 5);
        await AddLeadsAsync(admin, exam, lead);
        await ScoreAsync(admin, exam, lead, 90m);
        await OkAsync(await admin.PatchAsJsonAsync($"{Leads}/{lead}/admission-status", new { status = "accepted" }));
        Assert.Equal(new[] { lead }, Ids(await OkJsonAsync(await admin.GetAsync($"{Candidates}?search={tag}"))));

        var enrol = await admin.PostAsJsonAsync($"{Leads}/{lead}/enrol", new
        {
            student = new
            {
                fullName = name, birthDate = "2016-03-14", address = "", gender = "female",
                parentFullName = "Karimov Aziz", parentPhone = "+998901234567", className = "5-A",
                enrollmentDate = (string?)null,
            },
        });
        Assert.True(enrol.IsSuccessStatusCode, $"{(int)enrol.StatusCode}: {await enrol.Content.ReadAsStringAsync()}");

        var after = await OkJsonAsync(await admin.GetAsync($"{Candidates}?search={tag}"));
        Assert.Equal(0, after.GetProperty("total").GetInt32());
        Assert.Empty(after.GetProperty("items").EnumerateArray());
        Assert.Empty(Ids(await OkJsonAsync(await admin.GetAsync($"{Candidates}?examId={exam}"))));
        await ErrorAsync(await admin.GetAsync($"{Leads}/{lead}/admission"), HttpStatusCode.NotFound, "lead_not_found");
        await ErrorAsync(await admin.PatchAsJsonAsync($"{Leads}/{lead}/admission-status", new { status = "rejected" }),
            HttpStatusCode.NotFound, "lead_not_found");
    }

    // =====================================================================
    //  Helpers
    // =====================================================================

    /// <summary>A paper admission exam with one 100-point section — what B2 can score without the online engine.</summary>
    private async Task<string> CreateAdmissionPaperAsync(HttpClient admin, string title, string subjectId, int grade)
    {
        var body = new
        {
            title, kind = "admission", delivery = "manual", grade, examDate = Today,
            sections = new[] { new { subjectId, maxScore = 100m, order = 0 } },
        };
        return (await OkJsonAsync(await admin.PostAsJsonAsync(Exams, body))).GetProperty("id").GetString()!;
    }

    private static async Task AddLeadsAsync(HttpClient admin, string examId, params string[] leadIds)
    {
        var added = await OkJsonAsync(await admin.PostAsJsonAsync($"{Exams}/{examId}/participants", new { leadIds }));
        Assert.Equal(leadIds.Length, added.GetProperty("added").GetInt32());
    }

    /// <summary>Types one score into the entry grid — moves the lead to <c>tested</c> (ExamService).</summary>
    private async Task ScoreAsync(HttpClient admin, string examId, string leadId, decimal points)
    {
        var table = await OkJsonAsync(await admin.GetAsync($"{Exams}/{examId}/entry-table"));
        var sectionId = table.GetProperty("columns")[0].GetProperty("sectionId").GetString()!;
        string participantId;
        await using (var db = NewDb())
            participantId = (await db.ExamParticipants.AsNoTracking().SingleAsync(p => p.ExamId == examId && p.LeadId == leadId)).Id;
        await OkJsonAsync(await admin.PostAsJsonAsync($"{Exams}/{examId}/entry-table", new
        {
            rows = new object[] { new { participantId, scores = new[] { new { sectionId, points } }, absent = false } },
        }));
    }

    /// <summary>Moves one participation into the past, so "newest" does not hang on sub-millisecond timing.</summary>
    private async Task BackdateAsync(string examId, string leadId, int days)
    {
        await using var db = NewDb();
        var p = await db.ExamParticipants.SingleAsync(x => x.ExamId == examId && x.LeadId == leadId);
        p.CreatedAt = AppClock.Now.AddDays(-days);
        await db.SaveChangesAsync();
    }

    private async Task<string> SeedSubjectAsync(string name)
    {
        await using var db = NewDb();
        var subject = new Subject { Name = name };
        db.Subjects.Add(subject);
        await db.SaveChangesAsync();
        return subject.Id;
    }

    private async Task<string> SeedLeadAsync(
        string name, int grade, string phone = "+998901234567", string? stageTitle = null, string? note = null)
    {
        await using var db = NewDb();
        var stage = new LeadStage { Title = stageTitle ?? $"Nomzod {Tag()}", Color = "blue", Order = 960 };
        db.LeadStages.Add(stage);
        var lead = new Lead
        {
            FullName = name, Gender = "female", BirthDate = "2016-03-14", ParentFullName = "Karimov Aziz",
            ParentPhone = phone, TargetGrade = grade, Note = note, Stage = stage.Id,
        };
        db.Leads.Add(lead);
        await db.SaveChangesAsync();
        return lead.Id;
    }

    private async Task<string> StatusOfAsync(string leadId)
    {
        await using var db = NewDb();
        return (await db.Leads.AsNoTracking().SingleAsync(l => l.Id == leadId)).AdmissionStatus;
    }

    private static string?[] Ids(JsonElement page) =>
        page.GetProperty("items").EnumerateArray().Select(r => r.GetProperty("leadId").GetString()).ToArray();

    private static JsonElement Row(JsonElement page, string leadId) =>
        page.GetProperty("items").EnumerateArray().Single(r => r.GetProperty("leadId").GetString() == leadId);

    private static async Task OkAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.NoContent, $"expected 204, got {(int)response.StatusCode}: {text}");
    }

    private static async Task<JsonElement> OkJsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {text}");
        using var doc = JsonDocument.Parse(text);
        return doc.RootElement.Clone();
    }

    private static async Task<JsonElement> ErrorAsync(HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == status, $"expected {(int)status}, got {(int)response.StatusCode}: {text}");
        using var doc = JsonDocument.Parse(text);
        var root = doc.RootElement.Clone();
        Assert.Equal(code, root.GetProperty("code").GetString());
        return root;
    }
}
