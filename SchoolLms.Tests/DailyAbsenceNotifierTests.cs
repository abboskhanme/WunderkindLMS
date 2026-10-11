using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// One button after the nurse's roll-call (client, 2026-10-11: "kelmagan o'quvchini ota onasiga ...
/// avtomatik habar yuborish"): only pupils absent the WHOLE day get a Telegram message, once per day.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class DailyAbsenceNotifierTests(ApiFixture fixture) : IAsyncLifetime
{
    private const string Monday = "2026-01-12";
    private readonly List<string> _connectionStrings = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        foreach (var cs in _connectionStrings)
            NpgsqlConnection.ClearPool(new NpgsqlConnection(cs));
        return Task.CompletedTask;
    }

    [Fact]
    public async Task Faqat_kun_boyi_kelmaganlarga_bir_marta_yuboriladi()
    {
        var database = await fixture.Postgres.CreateDatabaseAsync("absence_notice");
        _connectionStrings.Add(database.OwnerConnectionString);
        await using var db = PostgresFixture.NewContext(database.OwnerConnectionString);

        var cls = new SchoolClass { Name = "5-A", Grade = 5, Language = "uz" };
        var chemistry = new Subject { Name = "Kimyo" };
        db.Classes.Add(cls);
        db.Subjects.Add(chemistry);
        var ali = Pupil("Aliyev Ali");      // absent all day, parent on Telegram
        var bek = Pupil("Berdiyev Bek");    // missed one lesson only — was at school
        var eva = Pupil("Ergasheva Eva");   // absent all day, parent NOT on Telegram
        var dil = Pupil("Dilova Dil");      // came
        db.Students.AddRange(ali, bek, eva, dil);
        var unexcused = new AbsenceReason { Name = "Sababsiz", Short = "S" };
        var sick = new AbsenceReason { Name = "Kasal", Short = "K" };
        db.AbsenceReasons.AddRange(unexcused, sick);
        db.Quarters.Add(new QuarterPeriod { Quarter = 3, StartDate = "2026-01-10", EndDate = "2026-03-20" });
        var nurse = new AppUser { FullName = "Hamshira", Role = Roles.Staff, Email = $"n.{Guid.NewGuid():N}", PasswordHash = "x" };
        db.Users.Add(nurse);
        db.TelegramRegistrations.AddRange(
            new TelegramRegistration { StudentId = ali.Id, ChatId = 1001, ParentName = "Ota" },
            new TelegramRegistration { StudentId = bek.Id, ChatId = 1002, ParentName = "Ona" });
        await db.SaveChangesAsync();

        var week = ScheduleMath.GetQuarterWeeks("2026-01-10", "2026-03-20")
            .First(w => string.CompareOrdinal(Monday, w.StartISO) >= 0 && string.CompareOrdinal(Monday, w.EndISO) <= 0);
        var tpl = new ScheduleTemplate
        {
            ClassId = cls.Id, Name = "Asosiy",
            Lessons = [.. new[] { 1, 2 }.Select(p => new ScheduleLesson { Day = 0, Period = p, SubjectId = chemistry.Id })],
        };
        db.ScheduleTemplates.Add(tpl);
        await db.SaveChangesAsync();
        db.WeekAssignments.Add(new WeekAssignment { ClassId = cls.Id, Quarter = 3, Week = week.Week, TemplateId = tpl.Id });

        void Row(Student s, int period, AbsenceReason r) => db.JournalEntries.Add(new JournalEntry
        {
            ClassId = cls.Id, SubjectId = chemistry.Id, StudentId = s.Id, Date = Monday, Period = period,
            Quarter = 3, ReasonId = r.Id,
        });
        foreach (var period in new[] { 1, 2 })
        {
            db.DailyAttendanceMarks.Add(new DailyAttendanceMark
            {
                ClassId = cls.Id, SubjectId = chemistry.Id, Date = Monday, Period = period,
                MarkedBy = nurse.Id, MarkedAt = AppClock.NowInstant,
            });
            Row(ali, period, sick);
            Row(eva, period, unexcused);
        }
        Row(bek, 2, unexcused);
        await db.SaveChangesAsync();

        var handler = new RecordingHandler();
        var notifier = new DailyAbsenceNotifier(db, ConfiguredTelegram(handler),
            fixture.Api.Services.GetRequiredService<ILogger<DailyAbsenceNotifier>>());

        var preview = await notifier.PreviewAsync(Monday);
        Assert.Equal(["Aliyev Ali", "Ergasheva Eva"], preview.Rows.Select(r => r.FullName));   // not Bek, not Dil
        Assert.Equal((1, 0, 1), (preview.Ready, preview.AlreadySent, preview.NoTelegram));
        Assert.Equal("Kasal", preview.Rows[0].ReasonName);

        var first = await notifier.SendAsync(Monday, nurse.Id);
        Assert.Equal((1, 0, 1, 0), (first.Sent, first.AlreadySent, first.NoTelegram, first.Failed));
        var message = Assert.Single(handler.Requests);
        Assert.Equal(1001, message.GetProperty("chat_id").GetInt64());
        var text = message.GetProperty("text").GetString()!;
        Assert.Contains("Aliyev Ali 12.01.2026 kuni maktabga kelmadi", text);
        Assert.Contains("Sabab: Kasal", text);

        // The button again: nothing new goes out.
        var second = await notifier.SendAsync(Monday, nurse.Id);
        Assert.Equal((0, 1), (second.Sent, second.AlreadySent));
        Assert.Single(handler.Requests);
        var notice = await db.AbsenceNotices.AsNoTracking().SingleAsync();
        Assert.Equal((ali.Id, new DateOnly(2026, 1, 12), sick.Id, 1, nurse.Id),
            (notice.StudentId, notice.Date, notice.ReasonId, notice.Chats, notice.SentBy));
        Assert.NotNull((await notifier.PreviewAsync(Monday)).Rows[0].SentAt);
    }

    [Fact]
    public async Task Yuborish_attendanceMark_ruxsatini_talab_qiladi()
    {
        using var noPerm = await fixture.Api.ClientAsAsync(Roles.Staff);
        Assert.Equal(HttpStatusCode.OK,
            (await noPerm.GetAsync($"/api/admin/attendance/daily/absence-notices?date={Monday}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await noPerm.PostAsJsonAsync("/api/admin/attendance/daily/absence-notices", new { date = Monday })).StatusCode);

        using var teacher = await fixture.Api.ClientAsAsync(Roles.Teacher);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await teacher.PostAsJsonAsync("/api/admin/attendance/daily/absence-notices", new { date = Monday })).StatusCode);

        // Viewing attendance is not marking it (2026-10-11: "kunduzgi davomat faqat hamshirada").
        using var director = await fixture.Api.ClientAsAsync(Roles.Staff, "attendance", "attendanceEvening", "attendanceDorm");
        Assert.Equal(HttpStatusCode.Forbidden,
            (await director.PostAsJsonAsync("/api/admin/attendance/daily/absence-notices", new { date = Monday })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await director.PostAsJsonAsync("/api/admin/attendance/daily", new { classId = "x", date = Monday, subjectId = "x", period = 1, marks = Array.Empty<object>() })).StatusCode);

        // The test API has no bot token: the nurse is told so instead of a silent "0 sent".
        using var nurse = await fixture.Api.ClientAsAsync(Roles.Staff, "attendanceMark");
        var response = await nurse.PostAsJsonAsync("/api/admin/attendance/daily/absence-notices", new { date = Monday });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Telegram bot sozlanmagan", await response.Content.ReadAsStringAsync());
    }

    /// <summary>Push was removed (2026-10-11): the pick-up flow now reaches people on Telegram.</summary>
    [Fact]
    public async Task Olib_ketish_xabarlari_Telegram_orqali()
    {
        var database = await fixture.Postgres.CreateDatabaseAsync("pickup_tg");
        _connectionStrings.Add(database.OwnerConnectionString);
        await using var db = PostgresFixture.NewContext(database.OwnerConnectionString);
        var cls = new SchoolClass { Name = "3-B", Grade = 3, Language = "uz" };
        var pupil = Pupil("Karimov Karim");
        pupil.ClassName = "3-B";
        var teacher = new Teacher { FullName = "Rahbar" };
        db.Classes.Add(cls);
        db.Students.Add(pupil);
        db.Teachers.Add(teacher);
        await db.SaveChangesAsync();
        db.ClassHomeroomTeachers.Add(new ClassHomeroomTeacher { ClassId = cls.Id, TeacherId = teacher.Id });
        db.TelegramRegistrations.AddRange(
            new TelegramRegistration { TeacherId = teacher.Id, ChatId = 2001, ParentName = "Rahbar" },
            new TelegramRegistration { StudentId = pupil.Id, ChatId = 2002, ParentName = "Ota" });
        await db.SaveChangesAsync();

        var handler = new RecordingHandler();
        var telegram = ConfiguredTelegram(handler);
        await PickupService.NotifyHomeroomAsync(db, telegram, pupil);
        await PickupService.NotifyParentAsync(db, telegram, pupil.Id, "Topshirildi");

        Assert.Equal([2001L, 2002L], handler.Requests.Select(r => r.GetProperty("chat_id").GetInt64()));
        Assert.Contains("Karimov Karim (3-B)", handler.Requests[0].GetProperty("text").GetString());
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

    private static Student Pupil(string fullName) => new()
    {
        FullName = fullName, LastName = fullName.Split(' ')[0], FirstName = fullName.Split(' ')[1],
        BirthDate = "2015-01-01", Gender = "male", ClassName = "5-A", EnrollmentDate = "2025-09-01",
    };

    /// <summary>Har so'rov tanasini yozib oladi va 200 qaytaradi — tarmoqqa hech narsa chiqmaydi.</summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly List<JsonElement> _requests = [];

        public IReadOnlyList<JsonElement> Requests
        {
            get { lock (_requests) return [.. _requests]; }
        }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
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
