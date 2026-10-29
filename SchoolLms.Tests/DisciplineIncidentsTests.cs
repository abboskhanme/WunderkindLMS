using System.IO.Compression;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

// ===========================================================================
//  Xulq-atvor 1:1 with EduSchool — the office side (docs/modules/behaviour.md, client 2026-10-15):
//  typed reasons, editing an incident (audited), the audited delete, the Harakatlar Excel and the
//  "belgilanmagan sinflar" coverage. Every test works in its own uniquely named classes.
// ===========================================================================

[Collection(SchoolLmsCollection.Name)]
public class DisciplineIncidentsTests(ApiFixture fixture)
{
    private const string Base = "/api/admin/discipline";

    // =====================================================================
    //  1. Reasons: Ijobiy / Salbiy
    // =====================================================================

    [Fact]
    public async Task Typed_reason_takes_its_sign_from_the_type_and_refuses_zero()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var tag = Tag();

        var negative = await admin.PostAsJsonAsync($"{Base}/reasons", new { name = $"So'kindi {tag}", points = 5, type = "negative" });
        Assert.Equal(HttpStatusCode.OK, negative.StatusCode);
        var id = (await JsonAsync(negative)).GetProperty("id").GetString();
        Assert.Equal(-5, (await JsonAsync(negative)).GetProperty("points").GetInt32());

        // Typing a minus by habit changes nothing: the type decides.
        var positive = await admin.PutAsJsonAsync($"{Base}/reasons/{id}", new { name = $"Kitob o'qidi {tag}", points = -4, type = "positive" });
        Assert.Equal(4, (await JsonAsync(positive)).GetProperty("points").GetInt32());

        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsJsonAsync($"{Base}/reasons", new { name = $"Nol {tag}", points = 0, type = "negative" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PostAsJsonAsync($"{Base}/reasons", new { name = $"Xato {tag}", points = 2, type = "neutral" })).StatusCode);

        // An older client without a type: stored as sent, as before.
        var legacy = await admin.PostAsJsonAsync($"{Base}/reasons", new { name = $"Eski mijoz {tag}", points = -7 });
        Assert.Equal(-7, (await JsonAsync(legacy)).GetProperty("points").GetInt32());
    }

    // =====================================================================
    //  2. Editing an incident
    // =====================================================================

    [Fact]
    public async Task Edit_with_a_new_reason_resnapshots_points_and_is_audited()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var pointId = await AddAsync(admin, w.StudentId, w.NegativeId, "Urushdi");

        var put = await admin.PutAsJsonAsync($"{Base}/points/{pointId}", new { reasonId = w.PositiveId, note = "Xato kiritilgan edi" });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        var dto = await JsonAsync(put);
        Assert.Equal(3, dto.GetProperty("points").GetInt32());
        Assert.Equal(w.PositiveId, dto.GetProperty("reasonId").GetString());
        Assert.False(string.IsNullOrEmpty(dto.GetProperty("updatedAt").GetString()));

        await fixture.Api.WithDbAsync(async db =>
        {
            var p = await db.DisciplinePoints.AsNoTracking().SingleAsync(x => x.Id == pointId);
            Assert.Equal((w.PositiveId, w.PositiveName, 3, "Xato kiritilgan edi"), (p.ReasonId, p.ReasonName, p.Points, p.Note));
            Assert.NotNull(p.UpdatedBy);
            var log = await db.AuditLogs.AsNoTracking().SingleAsync(a =>
                a.EntityType == AuditService.EntityDisciplinePoint && a.EntityId == pointId && a.Action == "update");
            Assert.Equal(w.StudentId, log.StudentId);
            Assert.Contains("-10", log.Before);
        });
    }

    [Fact]
    public async Task Edit_with_the_same_reason_keeps_the_snapshot_and_changes_the_note()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var pointId = await AddAsync(admin, w.StudentId, w.NegativeId, "birinchi");
        // The reason is re-priced afterwards — history must not move with it.
        await fixture.Api.WithDbAsync(async db =>
        {
            var r = await db.DisciplineReasons.SingleAsync(x => x.Id == w.NegativeId);
            r.Points = -20;
            await db.SaveChangesAsync();
        });

        var put = await admin.PutAsJsonAsync($"{Base}/points/{pointId}", new { reasonId = w.NegativeId, note = "ikkinchi" });
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);
        Assert.Equal(-10, (await JsonAsync(put)).GetProperty("points").GetInt32());
        Assert.Equal("ikkinchi", (await JsonAsync(put)).GetProperty("note").GetString());
    }

    [Fact]
    public async Task Edit_refuses_an_inactive_new_reason_unknown_ids_and_archived_pupils()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var pointId = await AddAsync(admin, w.StudentId, w.NegativeId, null);

        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PutAsJsonAsync($"{Base}/points/{pointId}", new { reasonId = w.InactiveId, note = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await admin.PutAsJsonAsync($"{Base}/points/{pointId}", new { reasonId = "yoq-" + w.Tag, note = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await admin.PutAsJsonAsync($"{Base}/points/{Guid.NewGuid()}", new { reasonId = w.PositiveId, note = "" })).StatusCode);

        await fixture.Api.WithDbAsync(async db =>
        {
            var s = await db.Students.SingleAsync(x => x.Id == w.StudentId);
            s.IsArchived = true;
            await db.SaveChangesAsync();
        });
        Assert.Equal(HttpStatusCode.Conflict,
            (await admin.PutAsJsonAsync($"{Base}/points/{pointId}", new { reasonId = w.PositiveId, note = "" })).StatusCode);
    }

    [Fact]
    public async Task Edit_and_delete_keep_the_discipline_gate()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var pointId = await AddAsync(admin, w.StudentId, w.NegativeId, null);
        var body = new { reasonId = w.PositiveId, note = "" };

        using var viewer = await fixture.Api.ClientAsAsync(Roles.Staff, "discipline:view");
        using var teacher = await fixture.Api.ClientAsAsync(Roles.Teacher, "discipline");
        using var anonymous = fixture.Api.AnonymousClient();
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PutAsJsonAsync($"{Base}/points/{pointId}", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.PutAsJsonAsync($"{Base}/points/{pointId}", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PutAsJsonAsync($"{Base}/points/{pointId}", body)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.DeleteAsync($"{Base}/points/{pointId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.DeleteAsync($"{Base}/points/{pointId}")).StatusCode);

        // A staff editor moves the entry to a reason only when they are its owner.
        var (editorUser, _) = await fixture.Api.SeedUserAsync(Roles.Staff, permissions: ["discipline"]);
        using var editor = fixture.Api.ClientWithToken(
            fixture.Api.TokenFor(Roles.Staff, editorUser.Id, editorUser.FullName, editorUser.Email));
        Assert.Equal(HttpStatusCode.Forbidden, (await editor.PutAsJsonAsync($"{Base}/points/{pointId}", body)).StatusCode);
        await fixture.Api.WithDbAsync(async db =>
        {
            (await db.DisciplineReasons.SingleAsync(r => r.Id == w.PositiveId)).OwnerUserIds = [editorUser.Id];
            await db.SaveChangesAsync();
        });
        Assert.Equal(HttpStatusCode.OK, (await editor.PutAsJsonAsync($"{Base}/points/{pointId}", body)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await editor.DeleteAsync($"{Base}/points/{pointId}")).StatusCode);

        await fixture.Api.WithDbAsync(async db =>
        {
            Assert.False(await db.DisciplinePoints.AnyAsync(p => p.Id == pointId));
            Assert.True(await db.AuditLogs.AnyAsync(a =>
                a.EntityType == AuditService.EntityDisciplinePoint && a.EntityId == pointId && a.Action == "delete"));
        });
    }

    [Fact]
    public async Task New_entries_carry_the_author_user_id()
    {
        var w = await SeedAsync();
        var (user, _) = await fixture.Api.SeedUserAsync(Roles.Admin);
        using var admin = fixture.Api.ClientWithToken(fixture.Api.TokenFor(Roles.Admin, user.Id, user.FullName, user.Email));
        var pointId = await AddAsync(admin, w.StudentId, w.PositiveId, null);
        Assert.Equal(HttpStatusCode.OK,
            (await admin.PostAsJsonAsync($"{Base}/points/class", new { classId = w.ClassId, reasonId = w.PositiveId })).StatusCode);

        await fixture.Api.WithDbAsync(async db =>
        {
            var rows = await db.DisciplinePoints.AsNoTracking().Where(p => p.StudentId == w.StudentId).ToListAsync();
            Assert.Equal(2, rows.Count);
            Assert.All(rows, p => Assert.Equal(user.Id, p.CreatedByUserId));
            Assert.All(rows, p => Assert.Equal(user.FullName, p.CreatedBy));
        });
        _ = pointId;
    }

    // =====================================================================
    //  3. Harakatlar Excel and coverage
    // =====================================================================

    [Fact]
    public async Task Feed_export_has_the_filtered_rows()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        await AddAsync(admin, w.StudentId, w.NegativeId, "Darsda shovqin");
        await AddAsync(admin, w.OtherStudentId, w.PositiveId, "Olimpiada");

        var response = await admin.GetAsync($"{Base}/feed/export?className={w.ClassName}&sign=negative");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            response.Content.Headers.ContentType?.MediaType);
        var sheet = await FirstSheetTextAsync(response);
        Assert.Contains("Darsda shovqin", sheet);
        Assert.Contains("Salbiy", sheet);
        Assert.DoesNotContain("Olimpiada", sheet);

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync($"{Base}/feed/export?from=2026-10-10&to=2026-10-01")).StatusCode);
        using var teacher = await fixture.Api.ClientAsAsync(Roles.Teacher, "discipline");
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync($"{Base}/feed/export")).StatusCode);
    }

    [Fact]
    public async Task Coverage_lists_classes_without_incidents_in_the_period_first()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        await AddAsync(admin, w.StudentId, w.NegativeId, null);
        await AddAsync(admin, w.StudentId, w.PositiveId, null);

        var today = AppClock.Today.ToString("yyyy-MM-dd");
        var coverage = await ObjectAsync(admin, $"{Base}/coverage?from={today}&to={today}");
        Assert.Equal(today, coverage.GetProperty("from").GetString());
        var classes = coverage.GetProperty("classes").EnumerateArray().ToList();

        var covered = classes.Single(c => c.GetProperty("classId").GetString() == w.ClassId);
        Assert.Equal((2, 1, 1, 2), (covered.GetProperty("incidents").GetInt32(), covered.GetProperty("positive").GetInt32(),
            covered.GetProperty("negative").GetInt32(), covered.GetProperty("students").GetInt32()));
        var empty = classes.Single(c => c.GetProperty("classId").GetString() == w.EmptyClassId);
        Assert.Equal(0, empty.GetProperty("incidents").GetInt32());
        Assert.Equal(JsonValueKind.Null, empty.GetProperty("lastAt").ValueKind);
        // Uncovered classes first; a class without pupils is not listed at all.
        Assert.True(classes.IndexOf(empty) < classes.IndexOf(covered));
        Assert.DoesNotContain(classes, c => c.GetProperty("classId").GetString() == w.NoPupilsClassId);
        Assert.Equal(classes.Count(c => c.GetProperty("incidents").GetInt32() == 0), coverage.GetProperty("notAssigned").GetInt32());

        // Yesterday the covered class had nothing either.
        var yesterday = AppClock.Today.AddDays(-1).ToString("yyyy-MM-dd");
        var before = await ObjectAsync(admin, $"{Base}/coverage?from={yesterday}&to={yesterday}");
        Assert.Equal(0, before.GetProperty("classes").EnumerateArray()
            .Single(c => c.GetProperty("classId").GetString() == w.ClassId).GetProperty("incidents").GetInt32());

        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync($"{Base}/coverage?from=2026-10-10&to=2026-10-01")).StatusCode);
        using var viewer = await fixture.Api.ClientAsAsync(Roles.Staff, "discipline:view");
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"{Base}/coverage")).StatusCode);
        using var teacher = await fixture.Api.ClientAsAsync(Roles.Teacher, "discipline");
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync($"{Base}/coverage")).StatusCode);
    }

    // =====================================================================
    //  Helpers
    // =====================================================================

    private sealed record World(
        string Tag, string ClassId, string ClassName, string EmptyClassId, string NoPupilsClassId,
        string StudentId, string OtherStudentId,
        string PositiveId, string PositiveName, string NegativeId, string InactiveId);

    private static string Tag() => Guid.NewGuid().ToString("N")[..8];

    private async Task<World> SeedAsync()
    {
        var tag = Tag();
        var cls = new SchoolClass { Name = $"DI-{tag}", Grade = 4 };
        var empty = new SchoolClass { Name = $"DI-E-{tag}", Grade = 4 };
        var noPupils = new SchoolClass { Name = $"DI-N-{tag}", Grade = 4 };
        var s = GeneralSettingsFlagsTests.NewStudent($"Hodisa {tag}", cls.Name, "+998900000101");
        var other = GeneralSettingsFlagsTests.NewStudent($"Sinfdosh {tag}", cls.Name, "+998900000102");
        var quiet = GeneralSettingsFlagsTests.NewStudent($"Tinch {tag}", empty.Name, "+998900000103");
        var positive = new DisciplineReason { Name = $"Faol {tag}", Points = 3 };
        var negative = new DisciplineReason { Name = $"Urushdi {tag}", Points = -10 };
        var inactive = new DisciplineReason { Name = $"Eski {tag}", Points = -1, IsActive = false };

        await fixture.Api.WithDbAsync(async db =>
        {
            db.Classes.AddRange(cls, empty, noPupils);
            db.Students.AddRange(s, other, quiet);
            db.DisciplineReasons.AddRange(positive, negative, inactive);
            await db.SaveChangesAsync();
        });
        return new World(tag, cls.Id, cls.Name, empty.Id, noPupils.Id, s.Id, other.Id,
            positive.Id, positive.Name, negative.Id, inactive.Id);
    }

    private static async Task<string> AddAsync(HttpClient client, string studentId, string reasonId, string? note)
    {
        var response = await client.PostAsJsonAsync($"{Base}/points", new { studentId, reasonId, note });
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        return (await JsonAsync(response)).GetProperty("id").GetString()!;
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.Clone();
    }

    private static async Task<JsonElement> ObjectAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"{url} → {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return await JsonAsync(response);
    }

    /// <summary>.xlsx is a zip; ExcelExport writes inline strings, so the sheet XML holds the text.</summary>
    private static async Task<string> FirstSheetTextAsync(HttpResponseMessage response)
    {
        await using var stream = new MemoryStream(await response.Content.ReadAsByteArrayAsync());
        using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = zip.Entries.First(e => e.FullName.StartsWith("xl/worksheets/", StringComparison.Ordinal));
        await using var sheet = entry.Open();
        using var reader = new StreamReader(sheet, Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }
}
