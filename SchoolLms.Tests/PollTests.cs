using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// So'rovnomalar va NPS, KPI (client, 2026-10-07): "ota-onalar va o'qituvchilar shaxsiy kabinetiga
/// so'rovnoma olishni kiritib ketish va NPS ko'rsatkichini o'lchay olish — hodimlar va maktab
/// rahbariyatining samaradorlik ko'rsatkichini o'lchash".
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class PollTests(ApiFixture fixture)
{
    private const string Api = "/api/admin/polls";

    [Fact]
    public async Task Ota_ona_farzandi_ustozlarini_baholaydi_anonim_va_bir_marta()
    {
        await WithQuarterAsync(async () =>
        {
            var w = await SeedParentAsync();
            var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
            var poll = await CreateOpenAsync(admin, new
            {
                title = "Maktab haqida fikringiz", description = "Choraklik so'rovnoma", audience = "parents", anonymous = true,
                closesOn = (string?)null,
                questions = new object[]
                {
                    new { kind = "nps", text = "Maktabimizni tanishlaringizga tavsiya qilasizmi?", options = (string[]?)null, required = true, target = "school" },
                    new { kind = "nps", text = "Ustozni tavsiya qilasizmi?", options = (string[]?)null, required = true, target = "teachers" },
                    new { kind = "choice", text = "Ovqat sifati", options = new[] { "Yaxshi", "O'rtacha", "Yomon" }, required = false, target = "school" },
                    new { kind = "text", text = "Taklifingiz", options = (string[]?)null, required = false, target = "school" },
                },
            });

            using var parent = ParentClient(w);
            var open = await parent.GetFromJsonAsync<List<PortalPollDto>>($"/api/student/polls?studentId={w.StudentId}");
            var mine = Assert.Single(open!, p => p.Id == poll.Id);
            Assert.False(mine.Answered);
            Assert.Equal(new[] { w.TeacherA, w.TeacherB }.Order(), mine.Teachers.Select(t => t.Id).Order());
            Assert.Contains(mine.Teachers, t => t.Id == w.TeacherA && t.Note == w.SubjectName);
            var q = mine.Questions;

            // A teacher who does not teach the child cannot be rated.
            var stranger = await SubmitAsync(parent, w, poll.Id, [
                new(q[0].Id, null, 10, null, null), new(q[1].Id, "someone-else", 9, null, null)]);
            Assert.Equal(HttpStatusCode.BadRequest, stranger.StatusCode);
            var missing = await SubmitAsync(parent, w, poll.Id, [new(q[1].Id, w.TeacherA, 9, null, null)]);
            Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);

            var ok = await SubmitAsync(parent, w, poll.Id, [
                new(q[0].Id, null, 9, null, null),
                new(q[1].Id, w.TeacherA, 10, null, null), new(q[1].Id, w.TeacherB, 4, null, null),
                new(q[2].Id, null, null, "Yaxshi", null),
                new(q[3].Id, null, null, null, "  Sport to'garaklari ko'paysa  ")]);
            Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await SubmitAsync(parent, w, poll.Id, [new(q[0].Id, null, 1, null, null)])).StatusCode);
            Assert.True(Assert.Single((await parent.GetFromJsonAsync<List<PortalPollDto>>($"/api/student/polls?studentId={w.StudentId}"))!, p => p.Id == poll.Id).Answered);

            await fixture.Api.WithDbAsync(async db =>
            {
                var row = await db.PollResponses.SingleAsync(r => r.PollId == poll.Id);
                Assert.Null(row.StudentId);                                   // anonymous: no id is kept
                Assert.DoesNotContain(w.StudentId, row.RespondentKey, StringComparison.Ordinal);
                Assert.Equal(w.ClassName, row.ClassName);
            });

            var results = await admin.GetFromJsonAsync<PollResultsDto>($"{Api}/{poll.Id}/results");
            Assert.Equal(1, results!.Responses);
            Assert.Equal([w.ClassName], results.Classes);
            var school = results.Questions[0];
            Assert.Equal(100, school.Nps);
            var perTeacher = results.Questions[1];
            Assert.Equal(0, perTeacher.Nps);                                  // one promoter, one detractor
            Assert.Equal(100, Assert.Single(perTeacher.Subjects, s => s.Id == w.TeacherA).Nps);
            Assert.Equal(-100, Assert.Single(perTeacher.Subjects, s => s.Id == w.TeacherB).Nps);
            Assert.Equal(1, Assert.Single(results.Questions[2].Distribution, d => d.Label == "Yaxshi").Count);
            var text = Assert.Single(results.Questions[3].Texts);
            Assert.Equal("Sport to'garaklari ko'paysa", text.Text);
            Assert.Null(text.RespondentName);
            Assert.Equal(0, (await admin.GetFromJsonAsync<PollResultsDto>($"{Api}/{poll.Id}/results?className=Boshqa"))!.Responses);
        });
    }

    [Fact]
    public async Task Notogri_savollar_rad_etiladi_javob_kelgach_savollar_qulflanadi()
    {
        var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var wrongTarget = await admin.PostAsJsonAsync(Api, Input("teachers", [Question("nps", "Ustoz?", "teachers")]));
        Assert.Equal(HttpStatusCode.BadRequest, wrongTarget.StatusCode);
        var oneOption = await admin.PostAsJsonAsync(Api, Input("parents", [new { kind = "choice", text = "?", options = new[] { "Ha" }, required = true, target = "school" }]));
        Assert.Equal(HttpStatusCode.BadRequest, oneOption.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync(Api, Input("parents", []))).StatusCode);

        var (teacher, teacherId) = await SeedTeacherAsync();
        var poll = await CreateOpenAsync(admin, Input("teachers", [Question("stars", "Ish sharoiti", "school")]));
        using var client = fixture.Api.ClientWithToken(fixture.Api.TokenFor(Roles.Teacher, teacher.Id, teacher.FullName, teacher.Email));
        var q = poll.Questions[0];
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/teacher/polls/{poll.Id}", new { answers = new[] { new { questionId = q.Id, subjectId = (string?)null, score = 6 } } })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync($"/api/teacher/polls/{poll.Id}", new { answers = new[] { new { questionId = q.Id, subjectId = (string?)null, score = 4 } } })).StatusCode);

        var changed = await admin.PutAsJsonAsync($"{Api}/{poll.Id}", Input("teachers", [Question("nps", "Boshqa savol", "school")]));
        Assert.Equal(HttpStatusCode.Conflict, changed.StatusCode);
        var renamed = await admin.PutAsJsonAsync($"{Api}/{poll.Id}", Input("teachers", [Question("stars", "Ish sharoiti", "school")], title: "Yangi nom"));
        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"{Api}/{poll.Id}")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync($"{Api}/{poll.Id}/close", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/api/teacher/polls/{poll.Id}", new { answers = Array.Empty<object>() })).StatusCode);
        Assert.DoesNotContain((await client.GetFromJsonAsync<List<PortalPollDto>>("/api/teacher/polls"))!, p => p.Id == poll.Id);
        _ = teacherId;
    }

    [Fact]
    public async Task Ustoz_rahbariyatni_baholaydi_va_kpi_da_korinadi()
    {
        var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var manager = await SeedEmployeeAsync(HrRoles.Manager);
        var (teacher, teacherId) = await SeedTeacherAsync();
        var poll = await CreateOpenAsync(admin, Input("teachers", [Question("nps", "Rahbaringizni tavsiya qilasizmi?", "management")]));

        using var client = fixture.Api.ClientWithToken(fixture.Api.TokenFor(Roles.Teacher, teacher.Id, teacher.FullName, teacher.Email));
        var open = Assert.Single((await client.GetFromJsonAsync<List<PortalPollDto>>("/api/teacher/polls"))!, p => p.Id == poll.Id);
        Assert.Contains(open.Managers, m => m.Id == manager.Id);
        var ok = await client.PostAsJsonAsync($"/api/teacher/polls/{poll.Id}",
            new { answers = new[] { new { questionId = open.Questions[0].Id, subjectId = manager.Id, score = 10 } } });
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);

        var kpi = await admin.GetFromJsonAsync<KpiReportDto>("/api/admin/kpi");
        var row = Assert.Single(kpi!.Rows, r => r.EmployeeId == manager.Id);
        Assert.Equal(100, row.StaffNpsRaw);
        Assert.Equal(100, row.StaffNps);
        Assert.Equal(1, row.StaffAnswers);
        _ = teacherId;
    }

    [Fact]
    public async Task Kpi_topshiriq_jarima_va_vaznlar_boyicha_hisoblanadi()
    {
        var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var employee = await SeedEmployeeAsync(HrRoles.Employee);
        var idle = await SeedEmployeeAsync(HrRoles.Employee);
        var now = AppClock.NowInstant;
        var month = AppClock.Today.ToString("yyyy-MM");
        await fixture.Api.WithDbAsync(async db =>
        {
            db.HrTasks.AddRange(
                Task(employee.Id, now.AddMinutes(-5), HrTaskStatuses.Done, late: false),
                Task(employee.Id, now.AddMinutes(-4), HrTaskStatuses.Done, late: true),
                Task(employee.Id, now.AddMinutes(-3), HrTaskStatuses.Open, late: false),
                Task(employee.Id, now.AddMinutes(-2), HrTaskStatuses.Cancelled, late: false));
            await db.SaveChangesAsync();
        });

        var row = Assert.Single((await admin.GetFromJsonAsync<KpiReportDto>($"/api/admin/kpi?month={month}"))!.Rows, r => r.EmployeeId == employee.Id);
        Assert.Equal(3, row.TasksTotal);
        Assert.Equal(1, row.TasksOnTime);
        Assert.Equal(33.3, row.Tasks);
        Assert.Null(row.LessonAnalysis);                                     // not a teacher
        Assert.Equal(100, row.Penalties);
        // Nothing measured: no fines alone is not a perfect score.
        var nothing = Assert.Single((await admin.GetFromJsonAsync<KpiReportDto>($"/api/admin/kpi?month={month}"))!.Rows, r => r.EmployeeId == idle.Id);
        Assert.Null(nothing.Total);
        Assert.Equal(100, nothing.Penalties);

        var weights = await admin.GetFromJsonAsync<KpiWeightsDto>("/api/admin/kpi/weights");
        try
        {
            var bad = await admin.PutAsJsonAsync("/api/admin/kpi/weights", new KpiWeightsDto(0, 0, 0, 0, 0, 0, 0));
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
            var only = await admin.PutAsJsonAsync("/api/admin/kpi/weights", new KpiWeightsDto(1, 0, 0, 0, 0, 1, 0));
            Assert.Equal(HttpStatusCode.OK, only.StatusCode);
            var after = Assert.Single((await admin.GetFromJsonAsync<KpiReportDto>($"/api/admin/kpi?month={month}"))!.Rows, r => r.EmployeeId == employee.Id);
            Assert.Equal(66.7, after.Total);                                  // (33.3 + 100) / 2
        }
        finally
        {
            await admin.PutAsJsonAsync("/api/admin/kpi/weights", weights);
        }
    }

    [Fact]
    public void Kpi_jami_faqat_bali_bor_korsatkichlardan_olinadi()
    {
        Assert.Equal(75.0, KpiService.Total((50, 1), (100, 1), (null, 5)));
        Assert.Equal(80.0, KpiService.Total((60, 1), (90, 2), (10, 0)));
        Assert.Null(KpiService.Total((null, 1), (20, 0)));
        Assert.Equal((null, 0, 0, 0), PollService.NpsOf([]));
        Assert.Equal((20, 2, 2, 1), PollService.NpsOf([10, 9, 8, 7, 6]));
    }

    [Theory]
    [InlineData(Roles.Staff)]
    [InlineData(Roles.Teacher)]
    [InlineData(Roles.Cashier)]
    public async Task Ruxsatsiz_rol_sorovnoma_va_kpi_ni_ochmaydi(string role)
    {
        using var client = await fixture.Api.ClientAsAsync(role);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Api)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync(Api, Input("parents", [Question("nps", "?", "school")]))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/kpi")).StatusCode);
    }

    [Fact]
    public async Task Ruxsati_bor_xodim_sorovnoma_tuzadi()
    {
        using var staff = await fixture.Api.ClientAsAsync(Roles.Staff, PollService.Permission);
        var created = await staff.PostAsJsonAsync(Api, Input("parents", [Question("nps", "Tavsiya?", "school")]));
        Assert.Equal(HttpStatusCode.OK, created.StatusCode);
        var poll = (await created.Content.ReadFromJsonAsync<PollDto>())!;
        Assert.Equal(HttpStatusCode.NoContent, (await staff.DeleteAsync($"{Api}/{poll.Id}")).StatusCode);
    }

    [Fact]
    public async Task Elon_ota_onalarga_bir_marta_boradi_ustozlarga_bormaydi()
    {
        var parentChat = Random.Shared.NextInt64(1_000_000_000, 9_000_000_000);
        var teacherChat = parentChat + 1;
        var (_, teacherId) = await SeedTeacherAsync();
        await fixture.Api.WithDbAsync(async db =>
        {
            db.TelegramRegistrations.Add(new TelegramRegistration { StudentId = "s", ChatId = parentChat, ParentName = "Ota" });
            db.TelegramRegistrations.Add(new TelegramRegistration { TeacherId = teacherId, StudentId = "", ChatId = teacherChat, ParentName = "Ustoz" });
            await db.SaveChangesAsync();

            var parents = await PollNotifier.RecipientsAsync(db, PollAudience.Parents);
            Assert.Contains(parentChat, parents);
            Assert.DoesNotContain(teacherChat, parents);
            var teachers = await PollNotifier.RecipientsAsync(db, PollAudience.Teachers);
            Assert.Contains(teacherChat, teachers);
            Assert.DoesNotContain(parentChat, teachers);
        });

        var poll = new Poll { Title = "Fikringiz", Description = "Qisqa", Audience = PollAudience.Parents, ClosesOn = new DateOnly(2026, 10, 20) };
        var text = PollNotifier.Text(poll);
        Assert.Contains("📋 Yangi so'rovnoma: Fikringiz", text, StringComparison.Ordinal);
        Assert.Contains("20.10.2026 gacha", text, StringComparison.Ordinal);
        Assert.Contains("anonim", text, StringComparison.Ordinal);

        var handler = new RecordingHandler();
        await fixture.Api.WithDbAsync(async db =>
        {
            var notifier = new PollNotifier(db, ConfiguredTelegram(handler), fixture.Api.Services.GetRequiredService<ILogger<PollNotifier>>());
            var (total, sent) = await notifier.AnnounceAsync(poll);
            Assert.Equal(total, sent);
            Assert.True(total >= 1);
        });
        Assert.Contains(handler.Requests, r => r.GetProperty("chat_id").GetInt64() == parentChat);
        Assert.DoesNotContain(handler.Requests, r => r.GetProperty("chat_id").GetInt64() == teacherChat);

        // The endpoint: only an open poll, and only once (the bot is not configured in tests).
        var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var draft = (await (await admin.PostAsJsonAsync(Api, Input("parents", [Question("nps", "?", "school")]))).Content.ReadFromJsonAsync<PollDto>())!;
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync($"{Api}/{draft.Id}/notify", null)).StatusCode);
        await admin.DeleteAsync($"{Api}/{draft.Id}");
    }

    // =================================================================

    private sealed record ParentWorld(string StudentId, string ClassName, string ParentUserId, string Phone,
        string TeacherA, string TeacherB, string SubjectName);

    private static object Question(string kind, string text, string target) =>
        new { kind, text, options = (string[]?)null, required = true, target };

    private static object Input(string audience, object[] questions, string title = "So'rovnoma") =>
        new { title, description = (string?)null, audience, anonymous = true, closesOn = (string?)null, questions };

    private static async Task<PollDto> CreateOpenAsync(HttpClient admin, object input)
    {
        var created = await admin.PostAsJsonAsync(Api, input);
        Assert.True(created.IsSuccessStatusCode, await created.Content.ReadAsStringAsync());
        var poll = (await created.Content.ReadFromJsonAsync<PollDto>())!;
        var opened = await admin.PostAsync($"{Api}/{poll.Id}/open", null);
        Assert.True(opened.IsSuccessStatusCode, await opened.Content.ReadAsStringAsync());
        return (await opened.Content.ReadFromJsonAsync<PollDto>())!;
    }

    private HttpClient ParentClient(ParentWorld w) =>
        fixture.Api.ClientWithToken(fixture.Api.TokenFor("parent", w.ParentUserId, "Ota-ona", w.Phone));

    private static Task<HttpResponseMessage> SubmitAsync(HttpClient client, ParentWorld w, Guid pollId, PollAnswerInput[] answers) =>
        client.PostAsJsonAsync($"/api/student/polls/{pollId}?studentId={w.StudentId}", new PollSubmitRequest([.. answers]));

    /// <summary>The quarter row is shared — a window around today for the test only, restored after.</summary>
    private async Task WithQuarterAsync(Func<Task> body)
    {
        var today = AppClock.Today;
        List<QuarterPeriod> saved = [];
        await fixture.Api.WithDbAsync(async db =>
        {
            saved = await db.Quarters.AsNoTracking().ToListAsync();
            await db.Quarters.ExecuteDeleteAsync();
            db.Quarters.Add(new QuarterPeriod
            {
                Quarter = 1, StartDate = today.AddDays(-40).ToString("yyyy-MM-dd"), EndDate = today.AddDays(40).ToString("yyyy-MM-dd"), GradesOpen = true,
            });
            await db.SaveChangesAsync();
        });
        try
        {
            await body();
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

    /// <summary>A parent, their child, and the child's class timetable this week: two teachers.</summary>
    private async Task<ParentWorld> SeedParentAsync()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var phone = "99893" + Random.Shared.Next(1_000_000, 9_999_999);
        var (parentUser, _) = await fixture.Api.SeedUserAsync("parent", email: phone);
        var (_, teacherA) = await SeedTeacherAsync();
        var (_, teacherB) = await SeedTeacherAsync();
        var cls = new SchoolClass { Name = $"PL-{tag}", Grade = 7 };
        var subject = new Subject { Name = $"Fizika {tag}" };
        var other = new Subject { Name = $"Kimyo {tag}" };
        var student = GeneralSettingsFlagsTests.NewStudent($"Bola {tag}", cls.Name, phone);
        await fixture.Api.WithDbAsync(async db =>
        {
            var (quarter, week) = await PortalSchedule.CurrentQuarterWeekAsync(db);
            db.Classes.Add(cls);
            db.Subjects.AddRange(subject, other);
            db.Students.Add(student);
            var tpl = new ScheduleTemplate { ClassId = cls.Id, Name = "Asosiy " + tag };
            tpl.Lessons.Add(new ScheduleLesson { TemplateId = tpl.Id, Day = 0, Period = 1, SubjectId = subject.Id, TeacherId = teacherA });
            tpl.Lessons.Add(new ScheduleLesson { TemplateId = tpl.Id, Day = 1, Period = 2, SubjectId = other.Id, TeacherId = teacherB });
            db.ScheduleTemplates.Add(tpl);
            db.WeekAssignments.Add(new WeekAssignment { ClassId = cls.Id, Quarter = quarter, Week = week, TemplateId = tpl.Id });
            await db.SaveChangesAsync();
        });
        return new ParentWorld(student.Id, cls.Name, parentUser.Id, phone, teacherA, teacherB, subject.Name);
    }

    private async Task<(AppUser User, string TeacherId)> SeedTeacherAsync()
    {
        var (user, _) = await fixture.Api.SeedUserAsync(Roles.Teacher);
        string teacherId = string.Empty;
        await fixture.Api.WithDbAsync(async db =>
            teacherId = await db.Teachers.Where(t => t.UserId == user.Id).Select(t => t.Id).SingleAsync());
        return (user, teacherId);
    }

    /// <summary>An active HR employee with the given HR role (reuses the row a user may already have).</summary>
    private async Task<HrEmployee> SeedEmployeeAsync(string hrRole)
    {
        var (user, _) = await fixture.Api.SeedUserAsync(Roles.Staff);
        HrEmployee employee = null!;
        await fixture.Api.WithDbAsync(async db =>
        {
            employee = await db.HrEmployees.FirstOrDefaultAsync(e => e.UserId == user.Id)
                       ?? db.HrEmployees.Add(new HrEmployee { UserId = user.Id, FullName = "Xodim " + user.Id[..6] }).Entity;
            employee.HrRole = hrRole;
            employee.IsActive = true;
            await db.SaveChangesAsync();
        });
        return employee;
    }

    private static HrTask Task(string employeeId, DateTimeOffset dueAt, string status, bool late) => new()
    {
        EmployeeId = employeeId, Title = "Hisobot", DueAt = dueAt, PeriodStart = AppClock.Today, Status = status, IsLate = late,
        CompletedAt = status == HrTaskStatuses.Done ? dueAt : null,
    };

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
