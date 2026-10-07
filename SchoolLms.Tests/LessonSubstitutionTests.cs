using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// O'rindosh ustoz (client, 2026-10-07): "hodim dars o'tmagan holatda boshqa hodim bilan almashtirish
/// holatini qo'shib ketish". The admin names a substitute for a lesson on a date; the substitute sees
/// it, may fill that journal on that date only, is paid for it, and the absent teacher is not.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class LessonSubstitutionTests(ApiFixture fixture)
{
    private const string Api = "/api/admin/substitutions";
    private const int Quarter = 1;
    private const int Period = 3;

    [Fact]
    public async Task Orindosh_darsni_jadvalda_koradi_va_faqat_osha_kuni_jurnal_toldiradi()
    {
        await WithQuarterAsync(async date =>
        {
            var w = await SeedAsync(date);
            var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

            var day = await admin.GetFromJsonAsync<SubstitutionDayDto>($"{Api}/day?teacherId={w.AbsentId}&date={Iso(date)}");
            var lesson = Assert.Single(day!.Lessons);
            Assert.Equal(Period, lesson.Period);
            Assert.Null(lesson.SubstitutionId);
            Assert.Contains(day.Teachers, t => t.Id == w.SubstituteId && t.BusyPeriods.Count == 0);
            Assert.Contains(day.Teachers, t => t.Id == w.BusyId && t.BusyPeriods.Contains(Period));

            using var sub = SubstituteClient(w);
            var other = date.AddDays(date.DayOfWeek == DayOfWeek.Monday ? 1 : -1);
            Assert.Equal(HttpStatusCode.Forbidden, (await PutGradeAsync(sub, w, date)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await sub.GetAsync($"/api/teacher/journal/students?classId={w.ClassId}")).StatusCode);

            var created = await CreateAsync(admin, w, date, w.SubstituteId);
            Assert.Equal(HttpStatusCode.OK, created.StatusCode);

            var place = await PlaceAsync(date);
            var mine = await sub.GetFromJsonAsync<List<TeacherLessonDto>>($"/api/teacher/schedule?quarter={place.Quarter}&week={place.Week}");
            var covered = Assert.Single(mine!);
            Assert.Equal(w.ClassId, covered.ClassId);
            Assert.Equal(w.AbsentName, covered.SubstituteFor);

            using var absent = fixture.Api.ClientWithToken(fixture.Api.TokenFor(Roles.Teacher, w.AbsentUserId, w.AbsentName, w.AbsentEmail));
            var theirs = await absent.GetFromJsonAsync<List<TeacherLessonDto>>($"/api/teacher/schedule?quarter={place.Quarter}&week={place.Week}");
            Assert.Equal(w.SubstituteName, Assert.Single(theirs!).SubstitutedBy);

            Assert.Equal(HttpStatusCode.NoContent, (await PutGradeAsync(sub, w, date)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await PutGradeAsync(sub, w, other)).StatusCode);
            var notes = await sub.GetAsync($"/api/teacher/journal/notes?classId={w.ClassId}&subjectId={w.SubjectId}&quarter={Quarter}");
            Assert.Equal(HttpStatusCode.OK, notes.StatusCode);
            var students = await sub.GetFromJsonAsync<List<StudentDto>>($"/api/teacher/journal/students?classId={w.ClassId}");
            Assert.Contains(students!, s => s.Id == w.StudentId);
        });
    }

    [Fact]
    public async Task Band_ustoz_ozi_va_jadvalda_yoq_dars_rad_etiladi()
    {
        await WithQuarterAsync(async date =>
        {
            var w = await SeedAsync(date);
            var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

            Assert.Contains("band", await MessageAsync(await CreateAsync(admin, w, date, w.BusyId)), StringComparison.Ordinal);
            Assert.Equal(HttpStatusCode.BadRequest, (await CreateAsync(admin, w, date, w.AbsentId)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await CreateAsync(admin, w, date, w.SubstituteId, period: Period + 1)).StatusCode);
            var sunday = date.AddDays(7 - (((int)date.DayOfWeek + 6) % 7) - 1);
            Assert.Equal(HttpStatusCode.BadRequest, (await CreateAsync(admin, w, sunday, w.SubstituteId)).StatusCode);

            await fixture.Api.WithDbAsync(async db =>
                Assert.False(await db.LessonSubstitutions.AnyAsync(s => s.AbsentTeacherId == w.AbsentId)));
        });
    }

    [Fact]
    public async Task Qayta_tayinlash_eskisini_bekor_qiladi_bekor_qilingach_kirish_yopiladi()
    {
        await WithQuarterAsync(async date =>
        {
            var w = await SeedAsync(date);
            var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
            var (second, secondTeacher) = await SeedTeacherAsync("Ikkinchi o'rindosh", [TeacherPermissions.Journal]);

            Assert.Equal(HttpStatusCode.OK, (await CreateAsync(admin, w, date, w.SubstituteId)).StatusCode);
            var again = await CreateAsync(admin, w, date, secondTeacher);
            Assert.Equal(HttpStatusCode.OK, again.StatusCode);

            var list = await admin.GetFromJsonAsync<List<SubstitutionDto>>($"{Api}?from={Iso(date)}&to={Iso(date)}&teacherId={w.AbsentId}");
            var standing = Assert.Single(list!);
            Assert.Equal(secondTeacher, standing.SubstituteTeacherId);

            using var first = SubstituteClient(w);
            Assert.Equal(HttpStatusCode.Forbidden, (await PutGradeAsync(first, w, date)).StatusCode);
            using var next = fixture.Api.ClientWithToken(fixture.Api.TokenFor(Roles.Teacher, second.Id, second.FullName, second.Email));
            Assert.Equal(HttpStatusCode.NoContent, (await PutGradeAsync(next, w, date)).StatusCode);

            Assert.Equal(HttpStatusCode.NoContent, (await admin.PostAsync($"{Api}/{standing.Id}/cancel", null)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await PutGradeAsync(next, w, date)).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsync($"{Api}/{standing.Id}/cancel", null)).StatusCode);
        });
    }

    [Fact]
    public async Task Maosh_orindoshga_bir_dars_qoshiladi_asosiy_ustozdan_bir_dars_ayiriladi()
    {
        await WithQuarterAsync(async date =>
        {
            var w = await SeedAsync(date);
            var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
            var month = date.ToString("yyyy-MM");

            Assert.Equal(HttpStatusCode.OK, (await CreateAsync(admin, w, date, w.SubstituteId)).StatusCode);
            var after = await RatesAsync(admin, month);

            Assert.Equal(1, after[w.SubstituteId].SubstitutionLessons);
            Assert.Equal(-1, after[w.AbsentId].SubstitutionLessons);

            // The whole day already marked absent: that lesson is not taken off twice.
            await fixture.Api.WithDbAsync(async db =>
            {
                db.TeacherAttendances.Add(new TeacherAttendance { TeacherId = w.AbsentId, Date = Iso(date), Status = "absent" });
                await db.SaveChangesAsync();
            });
            var marked = await RatesAsync(admin, month);
            Assert.Equal(0, marked[w.AbsentId].SubstitutionLessons);
            Assert.Equal(1, marked[w.SubstituteId].SubstitutionLessons);
        });
    }

    [Fact]
    public async Task Faollik_hisobotida_orindosh_darsi_orindoshga_yoziladi()
    {
        await WithQuarterAsync(async date =>
        {
            var w = await SeedAsync(date);
            var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
            Assert.Equal(HttpStatusCode.OK, (await CreateAsync(admin, w, date, w.SubstituteId)).StatusCode);
            await fixture.Api.WithDbAsync(async db =>
            {
                db.LessonNotes.Add(new LessonNote
                {
                    ClassId = w.ClassId, SubjectId = w.SubjectId, Quarter = Quarter, Date = Iso(date), Period = Period,
                    Topic = "Kasrlar", Conducted = true,
                });
                await db.SaveChangesAsync();
            });

            var rows = (await admin.GetFromJsonAsync<List<TeacherReportRowDto>>($"/api/admin/teacher-reports?quarter={Quarter}"))!
                .ToDictionary(r => r.TeacherId);
            Assert.Equal(1, rows[w.SubstituteId].Expected);
            Assert.Equal(1, rows[w.SubstituteId].Conducted);
            Assert.False(rows.TryGetValue(w.AbsentId, out var absent) && (absent.Expected > 0 || absent.Conducted > 0));
        });
    }

    /// <summary>The delta is paid at the teacher's own rate (with bonus) and never drives pay below 0.</summary>
    [Fact]
    public void Orindosh_darsi_ustozning_oz_narxida_hisoblanadi()
    {
        var meta = new SchoolMeta { SalaryRateOliy = 50_000m, SalaryRate2 = 30_000m };
        int[] none = [0, 0, 0, 0, 0, 0];
        int[] mondays = [1, 0, 0, 0, 0, 0];
        IReadOnlyList<(string, string)> quarters = [("2026-09-01", "2026-12-31")];

        // No own lessons, covered two: 2 × 30 000 + 10% bonus.
        Assert.Equal(66_000m, TeacherSalaryCalc.MonthlyForMonth(none, "2", meta, "2026-10", null, [], 10m, quarters, lessonDelta: 2));
        // Four October Mondays (5, 12, 19, 26), one of them covered by someone else.
        var full = TeacherSalaryCalc.MonthlyForMonth(mondays, "oliy", meta, "2026-10", null, [], 0m, quarters);
        Assert.Equal(200_000m, full);
        Assert.Equal(150_000m, TeacherSalaryCalc.MonthlyForMonth(mondays, "oliy", meta, "2026-10", null, [], 0m, quarters, lessonDelta: -1));
        Assert.Equal(0m, TeacherSalaryCalc.MonthlyForMonth(none, "oliy", meta, "2026-10", null, [], 0m, quarters, lessonDelta: -3));
    }

    [Theory]
    [InlineData(Roles.Staff)]
    [InlineData(Roles.Teacher)]
    [InlineData(Roles.Cashier)]
    public async Task Teachers_ruxsatisiz_orindosh_tayinlab_bolmaydi(string role)
    {
        using var client = await fixture.Api.ClientAsAsync(role);
        var response = await client.PostAsJsonAsync(Api, new
        {
            date = "2026-10-05", absentTeacherId = "x", reason = (string?)null,
            items = new[] { new { period = 1, ownerId = "c", subjectId = "s", subGroup = 0, substituteTeacherId = "y" } },
        });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Orindoshga_botdan_xabar_boradi()
    {
        var (user, teacherId) = await SeedTeacherAsync("Xabar oluvchi", []);
        var chat = Random.Shared.NextInt64(1_000_000_000, 9_000_000_000);
        await fixture.Api.WithDbAsync(async db =>
        {
            db.TelegramRegistrations.Add(new TelegramRegistration { TeacherId = teacherId, StudentId = "", ChatId = chat, ParentName = user.FullName });
            await db.SaveChangesAsync();
        });
        var row = new SubstitutionDto(
            Guid.NewGuid(), "2026-10-05", 2, LessonOwnerKind.Class, "c", "5-A", "s", "Matematika", 0,
            "a", "Asosiy Ustoz", teacherId, user.FullName, null, "Admin", AppClock.NowInstant);

        var handler = new RecordingHandler();
        await fixture.Api.WithDbAsync(async db =>
        {
            var notifier = new SubstitutionNotifier(db, ConfiguredTelegram(handler),
                fixture.Api.Services.GetRequiredService<ILogger<SubstitutionNotifier>>());
            Assert.Equal(1, await notifier.AssignedAsync([row]));
            Assert.Equal(1, await notifier.CancelledAsync([row]));
        });

        var sent = handler.Requests;
        Assert.Equal(chat, sent[0].GetProperty("chat_id").GetInt64());
        var text = sent[0].GetProperty("text").GetString()!;
        Assert.Contains("o'rindosh dars biriktirildi", text, StringComparison.Ordinal);
        Assert.Contains("05.10.2026 (dushanba), 2-dars", text, StringComparison.Ordinal);
        Assert.Contains("5-A · Matematika", text, StringComparison.Ordinal);
        Assert.Contains("Asosiy ustoz: Asosiy Ustoz", text, StringComparison.Ordinal);
        Assert.Contains("bekor qilindi", sent[1].GetProperty("text").GetString()!, StringComparison.Ordinal);
    }

    // =================================================================

    private sealed record World(
        string ClassId, string SubjectId, string StudentId,
        string AbsentId, string AbsentUserId, string AbsentName, string AbsentEmail,
        string SubstituteId, string SubstituteUserId, string SubstituteName, string SubstituteEmail,
        string BusyId);

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd");

    /// <summary>
    /// The quarter row is shared by the whole school — set for the test only (a window around today,
    /// so "yesterday" is a past lesson day) and restored afterwards. The body gets that lesson day.
    /// </summary>
    private async Task WithQuarterAsync(Func<DateOnly, Task> body)
    {
        var today = AppClock.Today;
        var date = today.AddDays(-1);
        while (date.DayOfWeek == DayOfWeek.Sunday) date = date.AddDays(-1);
        List<QuarterPeriod> saved = [];
        await fixture.Api.WithDbAsync(async db =>
        {
            saved = await db.Quarters.AsNoTracking().ToListAsync();
            await db.Quarters.ExecuteDeleteAsync();
            db.Quarters.Add(new QuarterPeriod
            {
                Quarter = Quarter, StartDate = Iso(today.AddDays(-40)), EndDate = Iso(today.AddDays(40)), GradesOpen = true,
            });
            await db.SaveChangesAsync();
        });
        try
        {
            await body(date);
        }
        finally
        {
            await fixture.Api.WithDbAsync(async db =>
            {
                await db.Quarters.ExecuteDeleteAsync();
                db.Quarters.AddRange(saved);
                await db.SaveChangesAsync();
            });
        }
    }

    private async Task<(int Quarter, int Week, int Day)> PlaceAsync(DateOnly date)
    {
        (int, int, int) place = default;
        await fixture.Api.WithDbAsync(async db => place = (await LessonSubstitutionService.PlaceAsync(db, date))!.Value);
        return place;
    }

    /// <summary>
    /// A class with one lesson on <paramref name="date"/> (period 3, the absent teacher), a free
    /// teacher, and a teacher busy in another class at the same period — all on that week's timetable.
    /// </summary>
    private async Task<World> SeedAsync(DateOnly date)
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var (absent, absentId) = await SeedTeacherAsync("Asosiy " + tag, [TeacherPermissions.Journal, TeacherPermissions.Schedule]);
        var (sub, subId) = await SeedTeacherAsync("O'rindosh " + tag, [TeacherPermissions.Journal, TeacherPermissions.Schedule]);
        var (_, busyId) = await SeedTeacherAsync("Band " + tag, [TeacherPermissions.Journal]);
        var place = await PlaceAsync(date);

        var cls = new SchoolClass { Name = $"OR-{tag}", Grade = 5 };
        var busyClass = new SchoolClass { Name = $"ORB-{tag}", Grade = 6 };
        var subject = new Subject { Name = $"Fan {tag}" };
        var student = GeneralSettingsFlagsTests.NewStudent($"O'quvchi {tag}", cls.Name, "+998900000031");
        await fixture.Api.WithDbAsync(async db =>
        {
            db.Classes.AddRange(cls, busyClass);
            db.Subjects.Add(subject);
            db.Students.Add(student);
            foreach (var (owner, teacher) in new[] { (cls, absentId), (busyClass, busyId) })
            {
                var tpl = new ScheduleTemplate { ClassId = owner.Id, Name = "Asosiy " + tag };
                tpl.Lessons.Add(new ScheduleLesson
                {
                    TemplateId = tpl.Id, Day = place.Day, Period = Period, SubjectId = subject.Id, TeacherId = teacher,
                });
                db.ScheduleTemplates.Add(tpl);
                db.WeekAssignments.Add(new WeekAssignment { ClassId = owner.Id, Quarter = place.Quarter, Week = place.Week, TemplateId = tpl.Id });
            }
            await db.SaveChangesAsync();
        });
        return new World(cls.Id, subject.Id, student.Id,
            absentId, absent.Id, absent.FullName, absent.Email,
            subId, sub.Id, sub.FullName, sub.Email, busyId);
    }

    private async Task<(AppUser User, string TeacherId)> SeedTeacherAsync(string name, string[] permissions)
    {
        var (user, _) = await fixture.Api.SeedUserAsync(Roles.Teacher);
        string teacherId = string.Empty;
        await fixture.Api.WithDbAsync(async db =>
        {
            var teacher = await db.Teachers.SingleAsync(t => t.UserId == user.Id);
            teacher.FullName = name;
            teacher.Permissions = [.. permissions];
            await db.SaveChangesAsync();
            teacherId = teacher.Id;
        });
        user.FullName = name;
        return (user, teacherId);
    }

    private HttpClient SubstituteClient(World w) =>
        fixture.Api.ClientWithToken(fixture.Api.TokenFor(Roles.Teacher, w.SubstituteUserId, w.SubstituteName, w.SubstituteEmail));

    private static Task<HttpResponseMessage> CreateAsync(HttpClient admin, World w, DateOnly date, string substituteId, int period = Period) =>
        admin.PostAsJsonAsync(Api, new
        {
            date = Iso(date), absentTeacherId = w.AbsentId, reason = "Kasal",
            items = new[] { new { period, ownerId = w.ClassId, subjectId = w.SubjectId, subGroup = 0, substituteTeacherId = substituteId } },
        });

    private static Task<HttpResponseMessage> PutGradeAsync(HttpClient teacher, World w, DateOnly date) =>
        teacher.PutAsJsonAsync("/api/teacher/journal", new
        {
            classId = w.ClassId, subjectId = w.SubjectId, quarter = Quarter, studentId = w.StudentId,
            date = Iso(date), period = Period, grade = 5, reasonId = (string?)null, homework = 0, behavior = 0, mastery = (int?)null,
        });

    private static async Task<Dictionary<string, TeacherPayrollRowDto>> RatesAsync(HttpClient admin, string month) =>
        (await admin.GetFromJsonAsync<SalaryRatesDto>($"/api/admin/salary-rates?month={month}"))!.Teachers.ToDictionary(t => t.Id);

    private static async Task<string> MessageAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("message").GetString() ?? "";
    }

    private TelegramService ConfiguredTelegram(RecordingHandler handler)
    {
        var service = new TelegramService(
            fixture.Api.Services.GetRequiredService<IConfiguration>(),
            new SingleHandlerHttpClientFactory(handler),
            fixture.Api.Services.GetRequiredService<ILogger<TelegramService>>());
        service.Set("test-token", "test_bot");
        return service;
    }

    /// <summary>Records every request body and answers 200 — nothing leaves the machine.</summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly List<JsonElement> _requests = [];

        public IReadOnlyList<JsonElement> Requests
        {
            get { lock (_requests) return [.. _requests]; }
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "{}" : await request.Content.ReadAsStringAsync(cancellationToken);
            using var json = JsonDocument.Parse(body);
            lock (_requests) _requests.Add(json.RootElement.Clone());
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"ok\":true}") };
        }
    }

    private sealed class SingleHandlerHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
