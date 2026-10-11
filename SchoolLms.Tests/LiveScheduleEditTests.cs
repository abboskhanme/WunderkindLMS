using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// Faol jadvalga o'zgartirish kiritish (mijoz, 2026-10-11: "Dars jadvaliga o'zgartirish kiritish",
/// "muharrir noqulay"): katak to'g'ridan-to'g'ri tahrirlanadi, o'tgan haftalar esa eski jadvalda qoladi.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class LiveScheduleEditTests(ApiFixture fixture) : IAsyncLifetime
{
    private readonly List<string> _connectionStrings = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        foreach (var cs in _connectionStrings)
            NpgsqlConnection.ClearPool(new NpgsqlConnection(cs));
        return Task.CompletedTask;
    }

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    private async Task<AppDbContext> NewDbAsync()
    {
        var database = await fixture.Postgres.CreateDatabaseAsync("live_schedule");
        _connectionStrings.Add(database.OwnerConnectionString);
        return PostgresFixture.NewContext(database.OwnerConnectionString);
    }

    /// <summary>Bugunni o'z ichiga olgan chorak va unga to'liq biriktirilgan "Asosiy" shablon.</summary>
    private static async Task<(SchoolClass Cls, ScheduleTemplate Tpl, List<ScheduleMath.WeekRange> Weeks)> SeedAsync(
        AppDbContext db, DateOnly from, DateOnly to)
    {
        var cls = new SchoolClass { Name = "5-A", Grade = 5 };
        var math = new Subject { Name = "Matematika" };
        db.Classes.Add(cls);
        db.Subjects.Add(math);
        db.Quarters.Add(new QuarterPeriod { Quarter = 1, StartDate = Iso(from), EndDate = Iso(to) });
        var tpl = new ScheduleTemplate
        {
            ClassId = cls.Id, Name = "Asosiy",
            Lessons = [new ScheduleLesson { Day = 0, Period = 1, SubjectId = math.Id }],
        };
        db.ScheduleTemplates.Add(tpl);
        var weeks = ScheduleMath.GetQuarterWeeks(Iso(from), Iso(to));
        db.WeekAssignments.AddRange(weeks.Select(w =>
            new WeekAssignment { ClassId = cls.Id, Quarter = 1, Week = w.Week, TemplateId = tpl.Id }));
        await db.SaveChangesAsync();
        return (cls, tpl, weeks);
    }

    [Fact]
    public async Task Otgan_haftalar_eski_jadvalda_qoladi_ozgarish_nusxaga_yoziladi()
    {
        await using var db = await NewDbAsync();
        var today = AppClock.Today;
        var (cls, tpl, weeks) = await SeedAsync(db, today.AddDays(-40), today.AddDays(40));
        var monday = LiveScheduleEdit.ThisMonday();

        var target = await LiveScheduleEdit.PrepareAsync(db, tpl);
        Assert.Null(target.Error);
        Assert.Same(tpl, target.Archived);
        Assert.NotEqual(tpl.Id, target.Template!.Id);
        await db.SaveChangesAsync();

        // Nusxa eski nomni oladi va darslarini; eskisi "… gacha" deb qayta nomlanadi.
        var until = DateOnly.ParseExact(monday, "yyyy-MM-dd", CultureInfo.InvariantCulture).AddDays(-1);
        Assert.Equal($"Asosiy · {until:dd.MM.yyyy} gacha", tpl.Name);
        var copy = await db.ScheduleTemplates.AsNoTracking().Include(t => t.Lessons).SingleAsync(t => t.Id == target.Template.Id);
        Assert.Equal("Asosiy", copy.Name);
        Assert.Single(copy.Lessons);

        // O'tgan haftalar — eskisida, joriy va keyingilari — nusxada.
        var byWeek = await db.WeekAssignments.AsNoTracking().Where(a => a.ClassId == cls.Id)
            .ToDictionaryAsync(a => a.Week, a => a.TemplateId);
        foreach (var w in weeks)
            Assert.Equal(string.CompareOrdinal(w.StartISO, monday) < 0 ? tpl.Id : copy.Id, byWeek[w.Week]);

        // Shu hafta yana tahrirlansa — nusxa endi faqat joriy/keyingi haftalarda, joyida o'zgaradi.
        var again = await LiveScheduleEdit.PrepareAsync(db, await db.ScheduleTemplates.Include(t => t.Lessons).SingleAsync(t => t.Id == copy.Id));
        Assert.Null(again.Archived);
        Assert.Equal(copy.Id, again.Template!.Id);

        // Eski versiya (faqat o'tgan haftalarda) o'zgartirilmaydi.
        var old = await LiveScheduleEdit.PrepareAsync(db, tpl);
        Assert.NotNull(old.Error);
        Assert.Null(old.Template);
    }

    [Fact]
    public async Task Qoralama_va_kelajakdagi_jadval_joyida_ozgaradi()
    {
        await using var db = await NewDbAsync();
        var today = AppClock.Today;
        // Chorak keyingi haftadan boshlanadi — o'tgan haftasi yo'q.
        var (_, future, _) = await SeedAsync(db, today.AddDays(8), today.AddDays(60));
        var draft = new ScheduleTemplate { ClassId = future.ClassId, Name = "Qoralama" };
        db.ScheduleTemplates.Add(draft);
        await db.SaveChangesAsync();

        foreach (var tpl in new[] { future, draft })
        {
            var target = await LiveScheduleEdit.PrepareAsync(db, tpl);
            Assert.Null(target.Error);
            Assert.Null(target.Archived);
            Assert.Same(tpl, target.Template);
        }
        Assert.Equal(2, await db.ScheduleTemplates.CountAsync());
    }

    [Fact]
    public async Task Endpoint_katakni_saqlaydi_tekshiradi_va_ruxsat_soraydi()
    {
        var tag = Guid.NewGuid().ToString("N")[..6];
        var cls = new SchoolClass { Name = $"LE-{tag}", Grade = 5 };
        var subject = new Subject { Name = $"Fan {tag}" };
        var tpl = new ScheduleTemplate { ClassId = cls.Id, Name = $"Qoralama {tag}" };
        await fixture.Api.WithDbAsync(async db =>
        {
            db.Classes.Add(cls);
            db.Subjects.Add(subject);
            db.ScheduleTemplates.Add(tpl);
            await db.SaveChangesAsync();
        });
        var url = $"/api/admin/classes/{cls.Id}/schedule-templates/{tpl.Id}/live-cell";
        var cell = new SetCellRequest(1, 3, [new ScheduleLessonDto(1, 3, subject.Id, "", 0)]);

        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var saved = await admin.PutAsJsonAsync(url, cell);
        Assert.True(saved.IsSuccessStatusCode, await saved.Content.ReadAsStringAsync());
        var result = await saved.Content.ReadFromJsonAsync<LiveCellResultDto>();
        Assert.Equal(tpl.Id, result!.Template.Id);
        Assert.Null(result.ArchivedId);
        Assert.Equal(subject.Id, Assert.Single(result.Template.Lessons).SubjectId);

        var bad = new SetCellRequest(1, 3, [new ScheduleLessonDto(2, 3, subject.Id, "", 0)]);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(url, bad)).StatusCode);

        // Sudrab o'tkazish: 3-soatdagi dars 5-soatga, 3-soat bo'shaydi — bitta so'rovda.
        var moved = await admin.PutAsJsonAsync(url + "s", new LiveCellsRequest(
        [
            new SetCellRequest(1, 3, []),
            new SetCellRequest(1, 5, [new ScheduleLessonDto(1, 5, subject.Id, "", 0)]),
        ]));
        Assert.True(moved.IsSuccessStatusCode, await moved.Content.ReadAsStringAsync());
        var after = (await moved.Content.ReadFromJsonAsync<LiveCellResultDto>())!.Template;
        Assert.Equal(5, Assert.Single(after.Lessons).Period);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(url + "s", new LiveCellsRequest(
            [new SetCellRequest(1, 3, []), new SetCellRequest(1, 3, [])]))).StatusCode);

        using var teacher = await fixture.Api.ClientAsAsync(Roles.Teacher);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.PutAsJsonAsync(url, cell)).StatusCode);
        using var viewer = await fixture.Api.ClientAsAsync(Roles.Staff, "schedule:view");
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PutAsJsonAsync(url, cell)).StatusCode);
    }
}
