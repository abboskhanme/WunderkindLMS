using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// What the audit log now records (docs/modules/audit-log.md, 2026-10-15): the pupil card
/// (create, changed fields only, password, archive/restore under the pupil, delete), teachers,
/// classes, roles, timetable, holidays, discipline points, guardians and the status catalog.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class AuditTrailWritersTests(ApiFixture fixture)
{
    private const string Students = "/api/admin/students";

    [Fact]
    public async Task Oquvchi_kartasi_yaratish_tahrir_parol_arxiv_qaytarish_ochirish()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var tag = Tag();
        var cls = await SeedClassAsync(tag);

        var payload = Payload(tag, cls.Name);
        var created = await admin.PostAsJsonAsync(Students, payload);
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var id = (await JsonAsync(created)).GetProperty("id").GetString()!;

        // Only the phone changes — the row names that field and nothing else.
        payload["phone"] = "+998901112233";
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"{Students}/{id}", payload)).StatusCode);
        // Saving the same values again writes nothing.
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"{Students}/{id}", payload)).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"{Students}/{id}/reset-password", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.PostAsJsonAsync($"{Students}/{id}/archive", new { reason = "Ko'chib ketdi" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsJsonAsync($"{Students}/{id}/restore", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"{Students}/{id}")).StatusCode);

        await fixture.Api.WithDbAsync(async db =>
        {
            var rows = await db.AuditLogs.AsNoTracking().Where(a => a.StudentId == id)
                .OrderBy(a => a.Timestamp).ThenBy(a => a.Action).ToListAsync();
            var card = rows.Where(a => a.EntityType == AuditService.EntityStudent).ToList();
            Assert.Single(card, a => a.Action == "create");
            var update = Assert.Single(card, a => a.Action == "update");
            Assert.Contains("Telefon", update.Summary);
            using (var after = JsonDocument.Parse(update.After!))
                Assert.Equal(["Telefon"], after.RootElement.EnumerateObject().Select(p => p.Name));
            var password = Assert.Single(card, a => a.Action == "password");
            Assert.Null(password.After);
            Assert.Single(card, a => a.Action == "archive");
            Assert.Single(card, a => a.Action == "restore");
            // No more archive rows under the finance type.
            Assert.DoesNotContain(rows, a => a.EntityType == AuditService.EntityStudentDiscount);
            Assert.Single(rows, a => a.EntityType == AuditService.EntityStudentDelete && a.Action == "delete");
        });
    }

    [Fact]
    public async Task Oqituvchi_sinf_va_rollar()
    {
        using var superadmin = await fixture.Api.ClientAsAsync(Roles.SuperAdmin);
        var tag = Tag();
        var cls = await SeedClassAsync(tag);
        var teacher = new Teacher { FullName = $"Ustoz {tag}", Phone = "+998900000000" };
        await fixture.Api.WithDbAsync(async db => { db.Teachers.Add(teacher); await db.SaveChangesAsync(); });

        Assert.Equal(HttpStatusCode.NoContent,
            (await superadmin.PostAsJsonAsync($"/api/admin/teachers/{teacher.Id}/archive", new { reason = "Ketdi" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await superadmin.PostAsJsonAsync($"/api/admin/teachers/{teacher.Id}/restore", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent,
            (await superadmin.PutAsJsonAsync($"/api/admin/classes/{cls.Id}/homeroom-teachers", new { teacherIds = new[] { teacher.Id } })).StatusCode);

        // Role: create, change permissions, give it to a staff member, take it back, delete.
        var role = await JsonAsync(await superadmin.PostAsJsonAsync("/api/admin/roles",
            new { name = $"Rol {tag}", description = "", permissions = new[] { "students" } }));
        var roleId = role.GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.OK, (await superadmin.PutAsJsonAsync($"/api/admin/roles/{roleId}",
            new { name = $"Rol {tag}", description = "", permissions = new[] { "students", "classes" } })).StatusCode);
        var (staff, _) = await fixture.Api.SeedUserAsync(Roles.Staff, fullName: $"Kotiba {tag}");
        Assert.Equal(HttpStatusCode.OK, (await superadmin.PutAsJsonAsync($"/api/admin/staff/{staff.Id}/role", new { accessRoleId = roleId })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await superadmin.PutAsJsonAsync($"/api/admin/staff/{staff.Id}/role", new { accessRoleId = (string?)null })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await superadmin.DeleteAsync($"/api/admin/roles/{roleId}")).StatusCode);

        await fixture.Api.WithDbAsync(async db =>
        {
            var teacherRows = await db.AuditLogs.AsNoTracking().Where(a => a.TeacherId == teacher.Id).ToListAsync();
            Assert.Single(teacherRows, a => a.EntityType == AuditService.EntityTeacher && a.Action == "archive");
            Assert.Single(teacherRows, a => a.EntityType == AuditService.EntityTeacher && a.Action == "restore");
            Assert.DoesNotContain(teacherRows, a => a.EntityType == AuditService.EntityTeacherSalary);

            Assert.True(await db.AuditLogs.AnyAsync(a => a.EntityType == AuditService.EntitySchoolClass && a.EntityId == cls.Id
                                                         && a.Summary.Contains(teacher.FullName)));

            var roleRows = await db.AuditLogs.AsNoTracking().Where(a => a.EntityType == AuditService.EntityAccessRole && a.EntityId == roleId)
                .ToListAsync();
            Assert.Equal(["create", "delete", "update"], roleRows.Select(a => a.Action).Order());
            var change = roleRows.Single(a => a.Action == "update");
            Assert.Contains("classes", change.After);
            Assert.Equal(2, await db.AuditLogs.CountAsync(a => a.EntityType == AuditService.EntityRoleAssignment && a.EntityId == staff.Id));
        });
    }

    [Fact]
    public async Task Jadval_bayram_intizom_vasiy_va_holat_katalogi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var tag = Tag();
        var cls = await SeedClassAsync(tag);
        var subject = new Subject { Name = $"Fan {tag}" };
        var teacher = new Teacher { FullName = $"Jadval ustozi {tag}" };
        var reason = new DisciplineReason { Name = $"Kechikdi {tag}", Points = -2 };
        var pupil = new Student { FullName = $"Bola {tag}", ClassName = cls.Name, EnrollmentDate = "2026-09-01", Gender = "male" };
        await fixture.Api.WithDbAsync(async db =>
        {
            db.Subjects.Add(subject);
            db.Teachers.Add(teacher);
            db.DisciplineReasons.Add(reason);
            db.Students.Add(pupil);
            await db.SaveChangesAsync();
        });

        // Timetable: a template and one cell.
        var tpl = await JsonAsync(await admin.PostAsJsonAsync($"/api/admin/classes/{cls.Id}/schedule-templates", new { name = $"Asosiy {tag}" }));
        var tplId = tpl.GetProperty("id").GetString()!;
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/admin/classes/{cls.Id}/schedule-templates/{tplId}/cell",
            new { day = 0, period = 1, lessons = new[] { new { day = 0, period = 1, subjectId = subject.Id, teacherId = teacher.Id, subGroup = 0 } } })).StatusCode);

        // Holiday far in the future (removed again — the shared database has other users).
        var date = $"2031-0{Random.Shared.Next(1, 9)}-1{Random.Shared.Next(0, 9)}";
        Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync("/api/admin/holidays", new { date, name = $"Bayram {tag}" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/admin/holidays?date={date}")).StatusCode);

        // Discipline point, then removed.
        var point = await JsonAsync(await admin.PostAsJsonAsync("/api/admin/discipline/points",
            new { studentId = pupil.Id, reasonId = reason.Id, note = "darsga kech" }));
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/admin/discipline/points/{point.GetProperty("id").GetString()}")).StatusCode);

        // Guardian attached from the pupil card.
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync($"{Students}/{pupil.Id}/guardians",
            new { fullName = $"Ona {tag}", phone = "+99893" + Random.Shared.Next(1_000_000, 9_999_999), relation = "parent" })).StatusCode);

        // Status catalog.
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync("/api/admin/student-statuses", new { name = $"Holat {tag}" })).StatusCode);

        await fixture.Api.WithDbAsync(async db =>
        {
            var schedule = await db.AuditLogs.AsNoTracking().Where(a => a.EntityType == AuditService.EntityScheduleTemplate && a.EntityId == tplId)
                .ToListAsync();
            Assert.Single(schedule, a => a.Action == "create");
            var cell = Assert.Single(schedule, a => a.Action == "update");
            Assert.Contains("Dushanba, 1-dars", cell.After);
            Assert.Contains(subject.Name, cell.After);

            Assert.Equal(["create", "delete"], await db.AuditLogs.Where(a => a.EntityType == AuditService.EntityHoliday && a.EntityId == date)
                .Select(a => a.Action).OrderBy(a => a).ToListAsync());
            Assert.Equal(["create", "delete"], await db.AuditLogs.Where(a => a.EntityType == AuditService.EntityDisciplinePoint && a.StudentId == pupil.Id)
                .Select(a => a.Action).OrderBy(a => a).ToListAsync());
            Assert.True(await db.AuditLogs.AnyAsync(a => a.EntityType == AuditService.EntityGuardian && a.StudentId == pupil.Id && a.Action == "create"));
            Assert.True(await db.AuditLogs.AnyAsync(a => a.EntityType == AuditService.EntityStudentStatus && a.Summary.Contains($"Holat {tag}")));
        });
    }

    // ------------------------------------------------------------------

    private static string Tag() => "AW" + Guid.NewGuid().ToString("N")[..8];

    private async Task<SchoolClass> SeedClassAsync(string tag)
    {
        var cls = new SchoolClass { Name = $"7-{tag}", Grade = 7 };
        await fixture.Api.WithDbAsync(async db => { db.Classes.Add(cls); await db.SaveChangesAsync(); });
        return cls;
    }

    private static Dictionary<string, object?> Payload(string tag, string className) => new()
    {
        ["fullName"] = $"O'quvchi {tag}",
        ["birthDate"] = "2015-05-05",
        ["address"] = "Toshkent",
        ["gender"] = "male",
        ["parentFullName"] = $"Ota-ona {tag}",
        ["parentPhone"] = "+998" + Random.Shared.Next(100_000_000, 999_999_999),
        ["className"] = className,
        ["enrollmentDate"] = "2026-09-01",
    };

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.Clone();
    }
}
