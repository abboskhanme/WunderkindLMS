using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// Arxivdagi o'quvchi sinfda ham, guruhda ham qolmaydi (mijoz, 2026-09-29: "arxivdagi barcha
/// o'quvchilar sinfdan chiqarilsin ... hamma ishlar faqat active o'quvchilar bilan"), va
/// boshlanishi kutilayotgan obuna "Yopilgan" emas, "kelgusi" bo'lib ko'rinadi.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class ArchivedStudentsLeaveClassTests(ApiFixture fixture)
{
    private const string Students = "/api/admin/students";
    private const string Classes = "/api/admin/classes";

    /// <summary>billing_seed.sql dagi barqaror "O'qish" toifasi.</summary>
    private static readonly Guid TuitionCategory = new("00000000-0000-0000-0000-0000000000c1");

    [Fact]
    public async Task Arxivlangan_oquvchi_sinfdan_chiqadi_tarix_va_sinf_darajasi_qoladi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var (cls, student, other) = await SeedClassAsync(grade: 5);

        var archive = await admin.PostAsJsonAsync($"{Students}/{student}/archive", new { reason = "Maktabdan ketdi" });
        Assert.Equal(HttpStatusCode.NoContent, archive.StatusCode);

        await fixture.Api.WithDbAsync(async db =>
        {
            var s = await db.Students.AsNoTracking().SingleAsync(x => x.Id == student);
            Assert.True(s.IsArchived);
            Assert.Equal(string.Empty, s.ClassName);
            Assert.Equal((short?)5, s.TargetGrade);
            var membership = await db.ClassMemberships.AsNoTracking().SingleAsync(m => m.StudentId == student);
            Assert.NotNull(membership.LeftOn);
            Assert.Equal(cls.Id, membership.ClassId);
            Assert.StartsWith("Arxivlandi", membership.LeaveReason);

            // The class roster (attendance, journal, ...) keeps only the active pupil.
            var owner = await LessonRoster.OwnerAsync(db, cls.Id);
            var roster = await LessonRoster.ForLessonAsync(db, owner!, includeArchived: true);
            Assert.Equal([other], roster.Select(r => r.Id));
        });
    }

    [Fact]
    public async Task Sinf_arxivlansa_oquvchilar_sinfdan_chiqadi_qaytarilsa_sinfga_qaytadi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var (cls, a, b) = await SeedClassAsync(grade: 11);

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"{Classes}/{cls.Id}/archive", null)).StatusCode);
        await fixture.Api.WithDbAsync(async db =>
        {
            var rows = await db.Students.AsNoTracking().Where(s => s.Id == a || s.Id == b).ToListAsync();
            Assert.All(rows, s => Assert.True(s.IsArchived && s.ArchivedWithClass && s.ClassName == string.Empty));
            Assert.Equal(0, await db.ClassMemberships.CountAsync(m => (m.StudentId == a || m.StudentId == b) && m.LeftOn == null));
        });

        var restore = await admin.PostAsync($"{Classes}/{cls.Id}/unarchive", null);
        Assert.Equal(HttpStatusCode.OK, restore.StatusCode);
        using (var body = JsonDocument.Parse(await restore.Content.ReadAsStringAsync()))
            Assert.Equal(2, body.RootElement.GetProperty("restoredStudents").GetInt32());
        await fixture.Api.WithDbAsync(async db =>
        {
            var rows = await db.Students.AsNoTracking().Where(s => s.Id == a || s.Id == b).ToListAsync();
            Assert.All(rows, s => Assert.True(!s.IsArchived && s.ClassName == cls.Name && s.TargetGrade == null));
            Assert.Equal(2, await db.ClassMemberships.CountAsync(m => (m.StudentId == a || m.StudentId == b) && m.LeftOn == null && m.ClassId == cls.Id));
        });
    }

    [Fact]
    public async Task Boshlanmagan_obuna_amaldagi_royxatda_kelgusi_deb_keladi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var (_, student, _) = await SeedClassAsync(grade: 7);
        var today = AppClock.Today;
        var nextMonth = new DateOnly(today.Year, today.Month, 1).AddMonths(1);
        var actor = (await fixture.Api.SeedUserAsync(Roles.Admin)).User.Id;
        await fixture.Api.WithDbAsync(async db =>
        {
            db.StudentSubscriptions.Add(new StudentSubscription
            {
                StudentId = student, CategoryId = TuitionCategory, MonthlyAmount = 160_000m,
                StartsOn = nextMonth, CreatedBy = actor, CreatedAt = AppClock.NowInstant,
            });
            await db.SaveChangesAsync();
        });

        var rows = await admin.GetFromJsonAsync<JsonElement>($"/api/admin/billing/subscriptions?studentId={student}&activeOnly=true");
        var row = Assert.Single(rows.EnumerateArray());
        Assert.False(row.GetProperty("isActive").GetBoolean());
        Assert.True(row.GetProperty("isUpcoming").GetBoolean());
        Assert.Equal(nextMonth.ToString("yyyy-MM-dd"), row.GetProperty("startsOn").GetString());
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>Sinf (arxivlanmagan) + ikki o'quvchi, har birining ochiq sinf a'zoligi bilan.</summary>
    private async Task<(SchoolClass Class, string A, string B)> SeedClassAsync(int grade)
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        var cls = new SchoolClass { Name = $"{grade}-Z{tag}", Grade = grade, MonthlyFee = 1_000_000m };
        Student Pupil(string n) => new()
        {
            FullName = $"{n} {tag}", LastName = tag, FirstName = n, BirthDate = "2014-01-01",
            Gender = "male", ClassName = cls.Name, EnrollmentDate = "2026-09-01",
        };
        var a = Pupil("Ketgan");
        var b = Pupil("Qolgan");
        await fixture.Api.WithDbAsync(async db =>
        {
            db.Classes.Add(cls);
            db.Students.AddRange(a, b);
            foreach (var s in new[] { a, b })
                db.ClassMemberships.Add(new ClassMembership
                {
                    StudentId = s.Id, ClassId = cls.Id, JoinedOn = new DateOnly(2026, 9, 1), CreatedAt = AppClock.NowInstant,
                });
            await db.SaveChangesAsync();
        });
        return (cls, a.Id, b.Id);
    }
}
