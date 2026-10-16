using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// The audit screen (docs/modules/audit-log.md, 2026-10-15): its own key `audit` (read gated),
/// finance and salary rows only with the finance right, sections that file the old misfiled rows
/// correctly, keyset pages with the total, and the per-record endpoint's new default limit.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class AuditLogTests(ApiFixture fixture)
{
    private const string Log = "/api/admin/audit/log";
    private const string Actors = "/api/admin/audit/actors";
    private const string Export = "/api/admin/audit/export";
    private const string PerRecord = "/api/admin/audit";

    // =====================================================================
    //  RBAC
    // =====================================================================

    [Fact]
    public async Task Ekran_audit_kaliti_bilan_ochiladi()
    {
        using var anonymous = fixture.Api.AnonymousClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Log)).StatusCode);

        using var teacher = await fixture.Api.ClientAsAsync(Roles.Teacher);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync(Log)).StatusCode);

        // Any other admin key is not enough: the whole school's history is gated.
        using var otherStaff = await fixture.Api.ClientAsAsync(Roles.Staff, "students", "finance");
        foreach (var url in new[] { Log, Actors, Export, "/api/admin/audit/sections" })
            Assert.Equal(HttpStatusCode.Forbidden, (await otherStaff.GetAsync(url)).StatusCode);

        using var auditor = await fixture.Api.ClientAsAsync(Roles.Staff, "audit");
        using var viewer = await fixture.Api.ClientAsAsync(Roles.Staff, "audit" + Roles.ViewSuffix);
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        foreach (var client in new[] { auditor, viewer, admin })
            foreach (var url in new[] { Log, Actors, Export, "/api/admin/audit/sections" })
                Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(url)).StatusCode);
    }

    // =====================================================================
    //  Money rule and sections
    // =====================================================================

    [Fact]
    public async Task Moliya_huquqisiz_pul_yozuvlari_yoq_summa_kesiladi_eski_arxiv_qatori_korinadi()
    {
        var tag = Tag();
        await SeedAsync(
            Row(AuditService.EntityPayment, "create", $"To'lov qabul qilindi {tag}", after: """{"Amount":500000}"""),
            Row(AuditService.EntityTeacherSalary, "update", $"Oylik sozlamasi {tag}"),
            // Old misfiled pupil archive (written as a finance type before 2026-10-15).
            Row(AuditService.EntityStudentDiscount, "update", $"O'quvchi arxivga ko'chirildi (Ali {tag}): \"ketdi\""),
            Row(AuditService.EntityStudent, "update", $"O'quvchi ma'lumoti o'zgardi {tag}", after: """{"Telefon":"901","amount":5}"""));

        using var auditor = await fixture.Api.ClientAsAsync(Roles.Staff, "audit");
        var staffRows = await ItemsAsync(auditor, $"{Log}?q={tag}");
        Assert.Equal(2, staffRows.Count);
        Assert.DoesNotContain(staffRows, r => r.GetProperty("entityType").GetString() is "Payment" or "TeacherSalary");
        var archive = staffRows.Single(r => r.GetProperty("entityType").GetString() == AuditService.EntityStudentDiscount);
        Assert.Equal(AuditCatalog.Students, archive.GetProperty("section").GetString());
        Assert.Equal("O'quvchilar", archive.GetProperty("sectionLabel").GetString());
        var card = staffRows.Single(r => r.GetProperty("entityType").GetString() == AuditService.EntityStudent);
        Assert.DoesNotContain("amount", card.GetProperty("after").GetString());
        Assert.Contains("Telefon", card.GetProperty("after").GetString());

        // The finance view key opens the money rows (FinanceViewer reads every finance screen).
        using var financeViewer = await fixture.Api.ClientAsAsync(Roles.Staff, "audit", "finance" + Roles.ViewSuffix);
        Assert.Equal(4, (await ItemsAsync(financeViewer, $"{Log}?q={tag}")).Count);

        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var all = await ItemsAsync(admin, $"{Log}?q={tag}");
        Assert.Equal(4, all.Count);
        Assert.Equal(AuditCatalog.Finance, all.Single(r => r.GetProperty("entityType").GetString() == "Payment").GetProperty("section").GetString());
        Assert.Equal(AuditCatalog.Salary, all.Single(r => r.GetProperty("entityType").GetString() == "TeacherSalary").GetProperty("section").GetString());

        // Section filter: the misfiled row is a pupil row, not a finance one.
        var students = await ItemsAsync(admin, $"{Log}?q={tag}&section=students");
        Assert.Equal(2, students.Count);
        var finance = await ItemsAsync(admin, $"{Log}?q={tag}&section=finance");
        Assert.Equal("Payment", Assert.Single(finance).GetProperty("entityType").GetString());

        // The per-record endpoint follows the same rule.
        using var students2 = await fixture.Api.ClientAsAsync(Roles.Staff, "students");
        var perRecord = await ArrayAsync(students2, $"{PerRecord}?q={tag}");
        Assert.Equal(2, perRecord.Count);
    }

    [Fact]
    public async Task Bolim_amal_turi_va_osti_yorligi()
    {
        Assert.Equal(AuditCatalog.Students, AuditCatalog.SectionOf("StudentDiscount", "O'quvchi ommaviy arxivlandi (X): \"y\""));
        Assert.Equal(AuditCatalog.Finance, AuditCatalog.SectionOf("StudentDiscount", "Katalogdan chegirma biriktirildi"));
        Assert.Equal(AuditCatalog.Students, AuditCatalog.SectionOf("ClassFee", "Sinf arxivlandi (5-A) — 3 ta o'quvchi bilan"));
        Assert.Equal(AuditCatalog.Staff, AuditCatalog.SectionOf("TeacherSalary", "O'qituvchi arxivdan qaytarildi (X)"));
        Assert.Equal(AuditCatalog.Salary, AuditCatalog.SectionOf("TeacherSalary", "Toifa belgilandi: oliy"));
        Assert.Equal(AuditCatalog.Schedule, AuditCatalog.SectionOf(AuditService.EntityScheduleTemplate, ""));
        Assert.Equal(AuditCatalog.Other, AuditCatalog.SectionOf("News", ""));

        Assert.Equal(AuditCatalog.KindCreate, AuditCatalog.KindOf("enrol"));
        Assert.Equal(AuditCatalog.KindDelete, AuditCatalog.KindOf("remove"));
        Assert.Equal(AuditCatalog.KindUpdate, AuditCatalog.KindOf("reverse"));
        Assert.Equal("Storno", AuditCatalog.ActionLabel("reverse"));
        Assert.Null(AuditCatalog.ActionLabel("update"));

        var tag = Tag();
        await SeedAsync(
            Row(AuditService.EntityStudent, "create", $"Yaratildi {tag}"),
            Row(AuditService.EntityPayment, "reverse", $"Storno {tag}"),
            Row(AuditService.EntityStudentDelete, "delete", $"O'chirildi {tag}"));
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var deleted = Assert.Single(await ItemsAsync(admin, $"{Log}?q={tag}&kind=delete"));
        Assert.Equal("delete", deleted.GetProperty("kind").GetString());
        var changed = Assert.Single(await ItemsAsync(admin, $"{Log}?q={tag}&kind=update"));
        Assert.Equal("Storno", changed.GetProperty("actionLabel").GetString());
        Assert.Single(await ItemsAsync(admin, $"{Log}?q={tag}&kind=create"));
    }

    // =====================================================================
    //  Paging, people, dates, export
    // =====================================================================

    [Fact]
    public async Task Sahifalar_eng_yangisi_birinchi_jami_bilan()
    {
        var tag = Tag();
        var rows = Enumerable.Range(1, 7)
            .Select(i => Row(AuditService.EntityStudent, "update", $"Qator {i} {tag}", timestamp: $"2026-10-0{i}T10:00:00"))
            .ToArray();
        await SeedAsync(rows);
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var seen = new List<string>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var url = $"{Log}?q={tag}&limit=3" + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
            using var page = JsonDocument.Parse(await admin.GetStringAsync(url));
            Assert.Equal(7, page.RootElement.GetProperty("total").GetInt32());
            seen.AddRange(page.RootElement.GetProperty("items").EnumerateArray().Select(r => r.GetProperty("timestamp").GetString()!));
            cursor = page.RootElement.GetProperty("nextCursor").GetString();
            pages++;
        } while (cursor is not null && pages < 10);

        Assert.Equal(3, pages);
        Assert.Equal(seen.OrderByDescending(t => t, StringComparer.Ordinal), seen);
        Assert.Equal(7, seen.Distinct().Count());

        // A bare `to` day includes the whole day; `from` is inclusive.
        var window = await ItemsAsync(admin, $"{Log}?q={tag}&from=2026-10-03&to=2026-10-05");
        Assert.Equal(["2026-10-05T10:00:00", "2026-10-04T10:00:00", "2026-10-03T10:00:00"],
            window.Select(r => r.GetProperty("timestamp").GetString()));
    }

    [Fact]
    public async Task Xodim_filtri_va_royxati()
    {
        var tag = Tag();
        var (actor, _) = await fixture.Api.SeedUserAsync(Roles.Staff, fullName: $"Kotiba {tag}");
        await SeedAsync(
            Row(AuditService.EntityStudent, "update", $"Uning ishi {tag}", actorId: actor.Id, actorName: actor.FullName),
            Row(AuditService.EntityStudent, "update", $"Tizim ishi {tag}", actorId: null, actorName: "Tizim"));
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var mine = Assert.Single(await ItemsAsync(admin, $"{Log}?q={tag}&actorId={actor.Id}"));
        Assert.Equal(actor.FullName, mine.GetProperty("actorName").GetString());
        Assert.Contains("Tizim ishi", Assert.Single(await ItemsAsync(admin, $"{Log}?q={tag}&actorId=system")).GetProperty("summary").GetString());

        var people = await ArrayAsync(admin, Actors);
        Assert.Contains(people, p => p.GetProperty("id").GetString() == actor.Id && p.GetProperty("name").GetString() == actor.FullName);
        Assert.Contains(people, p => p.GetProperty("id").GetString() == "system");
    }

    [Fact]
    public async Task Excel_va_eski_endpointning_sukut_chegarasi()
    {
        var tag = Tag();
        var type = "T" + tag;
        await SeedAsync([.. Enumerable.Range(0, 105).Select(i => Row(type, "update", $"Ko'p {i} {tag}"))]);
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        // The old unbounded call now stops at 100 rows; an explicit limit still works.
        Assert.Equal(100, (await ArrayAsync(admin, $"{PerRecord}?entityType={type}")).Count);
        Assert.Equal(105, (await ArrayAsync(admin, $"{PerRecord}?entityType={type}&limit=500")).Count);

        var file = await admin.GetAsync($"{Export}?q={tag}");
        Assert.Equal(HttpStatusCode.OK, file.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", file.Content.Headers.ContentType?.MediaType);
        Assert.True((await file.Content.ReadAsByteArrayAsync()).Length > 1000);
    }

    // =====================================================================
    //  Helpers
    // =====================================================================

    private static string Tag() => "AU" + Guid.NewGuid().ToString("N")[..8];

    private static AuditLog Row(
        string type, string action, string summary, string? after = null, string? timestamp = null,
        string? actorId = null, string? actorName = "Test") => new()
        {
            EntityType = type,
            EntityId = Guid.NewGuid().ToString(),
            Action = action,
            Timestamp = timestamp ?? AppClock.Iso(),
            ActorId = actorId,
            ActorName = actorName,
            Summary = summary,
            After = after,
        };

    private Task SeedAsync(params AuditLog[] rows) => fixture.Api.WithDbAsync(async db =>
    {
        db.AuditLogs.AddRange(rows);
        await db.SaveChangesAsync();
    });

    private static async Task<List<JsonElement>> ItemsAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return [.. doc.RootElement.GetProperty("items").EnumerateArray().Select(e => e.Clone())];
    }

    private static async Task<List<JsonElement>> ArrayAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return [.. doc.RootElement.EnumerateArray().Select(e => e.Clone())];
    }
}
