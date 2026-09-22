using System.Net;
using System.Net.Http.Json;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// Jurnal → sinf → fan oqimi (EduSchool kabi, 2026-09-23): sinflar ro'yxatida ta'lim tili
/// va sinf rahbari, sinf fanlari o'qituvchilari bilan, va jurnalda bahosi qolgan, lekin
/// endi ro'yxatda yo'q o'quvchilar (arxiv / boshqa sinf).
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class JournalNavigationTests(ApiFixture fixture)
{
    private sealed record World(string ClassId, string ClassName, string MathId, string ArtId,
        string Active, string Archived, string Moved);

    private async Task<World> SeedAsync()
    {
        var tag = Guid.NewGuid().ToString("N")[..5];
        var cls = new SchoolClass { Name = $"9-{tag}", Grade = 9, Language = "ru" };
        var math = new Subject { Name = $"Matematika {tag}" };
        var art = new Subject { Name = $"Tasviriy {tag}" };
        var t1 = new Teacher { FullName = $"Aliyeva {tag}", Category = "oliy", HomeroomClass = cls.Name };
        var t2 = new Teacher { FullName = $"Boboyev {tag}", Category = "oliy" };
        Student Pupil(string name, string className, bool archived = false) => new()
        {
            FullName = $"{name} {tag}", LastName = name, FirstName = tag, BirthDate = "2012-01-01",
            Gender = "male", ClassName = className, EnrollmentDate = "2025-09-01", IsArchived = archived,
        };
        var active = Pupil("Faol", cls.Name);
        var archived = Pupil("Arxiv", cls.Name, archived: true);
        var moved = Pupil("Kochgan", "boshqa-sinf");

        var tpl = new ScheduleTemplate { ClassId = cls.Id, Name = "asosiy" };
        tpl.Lessons.Add(new ScheduleLesson { TemplateId = tpl.Id, Day = 0, Period = 1, SubjectId = math.Id, TeacherId = t1.Id });
        tpl.Lessons.Add(new ScheduleLesson { TemplateId = tpl.Id, Day = 1, Period = 2, SubjectId = math.Id, TeacherId = t2.Id });
        tpl.Lessons.Add(new ScheduleLesson { TemplateId = tpl.Id, Day = 2, Period = 3, SubjectId = art.Id, TeacherId = t2.Id });

        JournalEntry Entry(Student s) => new()
        {
            ClassId = cls.Id, SubjectId = math.Id, Quarter = 1, StudentId = s.Id,
            Date = "2025-09-08", Period = 1, Grade = 5,
        };

        await fixture.Api.WithDbAsync(async db =>
        {
            db.Classes.Add(cls);
            db.Subjects.AddRange(math, art);
            db.Teachers.AddRange(t1, t2);
            db.Students.AddRange(active, archived, moved);
            db.ScheduleTemplates.Add(tpl);
            db.JournalEntries.AddRange(Entry(active), Entry(archived), Entry(moved));
            await db.SaveChangesAsync();
        });
        return new World(cls.Id, cls.Name, math.Id, art.Id, active.Id, archived.Id, moved.Id);
    }

    [Fact]
    public async Task Sinflar_royxatida_til_va_sinf_rahbari_bor()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var owners = (await admin.GetFromJsonAsync<List<JournalOwnerDto>>("/api/admin/journal/owners"))!;
        var mine = Assert.Single(owners, o => o.Id == w.ClassId);
        Assert.Equal("ru", mine.Language);
        Assert.StartsWith("Aliyeva", mine.HomeroomTeacher);
        Assert.Equal(1, mine.StudentCount);
    }

    [Fact]
    public async Task Sinf_fanlari_jadvaldagi_oqituvchilar_bilan()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var subjects = (await admin.GetFromJsonAsync<List<JournalSubjectDto>>(
            $"/api/admin/journal/subjects?classId={w.ClassId}"))!;

        Assert.Equal(2, subjects.Count);
        var math = Assert.Single(subjects, s => s.SubjectId == w.MathId);
        Assert.Equal(2, math.Teachers.Count);
        Assert.Single(Assert.Single(subjects, s => s.SubjectId == w.ArtId).Teachers);
    }

    [Fact]
    public async Task Sobiq_oquvchilar_arxivdagi_va_kochganlar()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var former = (await admin.GetFromJsonAsync<List<StudentDto>>(
            $"/api/admin/journal/former-students?classId={w.ClassId}&subjectId={w.MathId}&quarter=1"))!;

        Assert.Equal(new[] { w.Archived, w.Moved }.Order(), former.Select(s => s.Id).Order());
        Assert.DoesNotContain(former, s => s.Id == w.Active);
    }

    [Theory]
    [InlineData(Roles.Teacher)]
    [InlineData(Roles.Cashier)]
    public async Task Boshqa_rollar_403(string role)
    {
        using var client = await fixture.Api.ClientAsAsync(role);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/journal/subjects?classId=x")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync("/api/admin/journal/former-students?classId=x&subjectId=y&quarter=1")).StatusCode);
    }
}
