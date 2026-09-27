using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using SchoolLms.Application.HrTasks;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;
using static SchoolLms.Tests.HrTasks.HrTasksKit;

namespace SchoolLms.Tests.HrTasks;

// ===========================================================================
//  HR topshiriqlari — scheduled work against the real database, with a fixed
//  "now" and a fake Telegram sender: generation (idempotency, weekdays,
//  monthly incl. short months, no past-due dates, position targets, inactive
//  employees), the reopen window, reminders, the evening digest (recipients,
//  scope, empty-skip), legacy close, and hr_job_runs idempotency of the tick.
//  Far-future dates keep these rows apart from the other tests' data.
// ===========================================================================

[Collection(SchoolLmsCollection.Name)]
public class HrTasksJobsTests(ApiFixture fixture)
{
    private readonly HrTasksKit kit = new(fixture);

    private static HrTaskTemplate Template(string employeeId, string recurrence, short[]? weekdays = null, short[]? days = null,
        TimeOnly? due = null) => new()
    {
        Title = "Shablon " + Guid.NewGuid().ToString("N")[..6], TargetType = HrTargetTypes.Employee, TargetEmployeeId = employeeId,
        Recurrence = recurrence, Weekdays = weekdays ?? [], DaysOfMonth = days ?? [], DueTime = due,
        OverduePolicy = HrOverduePolicies.KeepOpen, CreatedAt = DateTimeOffset.UtcNow, UpdatedAt = DateTimeOffset.UtcNow,
    };

    private async Task<HrTaskTemplate> SaveAsync(HrTaskTemplate template)
    {
        await fixture.Api.WithDbAsync(async db => { db.HrTaskTemplates.Add(template); await db.SaveChangesAsync(); });
        return template;
    }

    private async Task<List<HrTask>> TasksOfAsync(string templateId)
    {
        var list = new List<HrTask>();
        await fixture.Api.WithDbAsync(async db =>
            list = await db.HrTasks.AsNoTracking().Where(t => t.TemplateId == templateId).OrderBy(t => t.DueAt).ToListAsync());
        return list;
    }

    [Fact]
    public async Task Nusxa_yaratish_idempotent_va_hafta_kuniga_bogliq()
    {
        var e = await kit.PersonAsync();
        var weekly = await SaveAsync(Template(e.Employee!.Id, HrRecurrences.Weekly, [1, 3], due: new TimeOnly(18, 0))); // Mon, Wed
        var monday = new DateOnly(2032, 3, 1);
        Assert.Equal(DayOfWeek.Monday, monday.DayOfWeek);
        var now = At(2032, 3, 1, 0, 10);

        await kit.WithServiceAsync(async (s, _) =>
        {
            var first = await s.GenerateAsync(monday, weekly.Id, now);
            Assert.Equal(1, first.Created);
            var again = await s.GenerateAsync(monday, weekly.Id, now);
            Assert.Equal(0, again.Created);
            Assert.Equal(1, again.Skipped);
            Assert.Equal(0, (await s.GenerateAsync(monday.AddDays(1), weekly.Id, At(2032, 3, 2, 0, 10))).Templates); // Tuesday
            Assert.Equal(1, (await s.GenerateAsync(monday.AddDays(2), weekly.Id, At(2032, 3, 3, 0, 10))).Created); // Wednesday
        });

        var tasks = await TasksOfAsync(weekly.Id);
        Assert.Equal(2, tasks.Count);
        var t = tasks[0];
        Assert.Equal(HrTaskSources.Template, t.Source);
        Assert.Equal(monday, t.PeriodStart);
        Assert.Equal(At(2032, 3, 1, 18), t.DueAt);
        Assert.Null(t.CreatedBy);
        Assert.Equal(weekly.Title, t.Title); // snapshot
    }

    [Fact]
    public async Task Oylik_shablon_butun_oy_qisqa_oy_va_otgan_sana()
    {
        var e = await kit.PersonAsync();
        var monthly = await SaveAsync(Template(e.Employee!.Id, HrRecurrences.Monthly, days: [15, 31], due: new TimeOnly(18, 0)));
        await kit.WithServiceAsync(async (s, _) =>
        {
            // April: 15 and 30 (31 → the last day), both at the start of the month
            Assert.Equal(2, (await s.GenerateAsync(new DateOnly(2032, 4, 1), monthly.Id, At(2032, 4, 1, 0, 10))).Created);
            Assert.Equal(0, (await s.GenerateAsync(new DateOnly(2032, 4, 2), monthly.Id, At(2032, 4, 2, 0, 10))).Created);
        });
        var april = await TasksOfAsync(monthly.Id);
        Assert.Equal([At(2032, 4, 15, 18), At(2032, 4, 30, 18)], april.Select(t => t.DueAt));
        Assert.All(april, t => Assert.Equal(new DateOnly(2032, 4, 1), t.PeriodStart));

        // a template added mid-month: the 15th already passed → only the 31st (clamped to 31 in May)
        var late = await SaveAsync(Template(e.Employee.Id, HrRecurrences.Monthly, days: [15, 31]));
        await kit.WithServiceAsync(async (s, _) =>
            Assert.Equal(1, (await s.GenerateAsync(new DateOnly(2032, 5, 20), late.Id, At(2032, 5, 20, 0, 10))).Created));
        Assert.Equal(At(2032, 5, 31, 23, 59), Assert.Single(await TasksOfAsync(late.Id)).DueAt);
    }

    [Fact]
    public async Task Lavozim_faol_xodimlarga_nofaolga_hech_narsa()
    {
        var position = new HrPosition { Name = "Lavozim " + Guid.NewGuid().ToString("N")[..8] };
        var active = await kit.PersonAsync();
        var fired = await kit.PersonAsync(active: false);
        await fixture.Api.WithDbAsync(async db =>
        {
            db.HrPositions.Add(position);
            await db.SaveChangesAsync();
            foreach (var id in new[] { active.Employee!.Id, fired.Employee!.Id })
                (await db.HrEmployees.SingleAsync(x => x.Id == id)).PositionId = position.Id;
            await db.SaveChangesAsync();
        });
        var template = Template(active.Employee!.Id, HrRecurrences.Daily, [1, 2, 3, 4, 5, 6, 7]);
        template.TargetType = HrTargetTypes.Position;
        template.TargetEmployeeId = null;
        template.TargetPositionId = position.Id;
        await SaveAsync(template);
        var direct = await SaveAsync(Template(fired.Employee!.Id, HrRecurrences.Daily, [1, 2, 3, 4, 5, 6, 7]));

        await kit.WithServiceAsync(async (s, _) =>
        {
            Assert.Equal(1, (await s.GenerateAsync(new DateOnly(2032, 6, 7), template.Id, At(2032, 6, 7, 0, 10))).Created);
            Assert.Equal(0, (await s.GenerateAsync(new DateOnly(2032, 6, 7), direct.Id, At(2032, 6, 7, 0, 10))).Created);
        });
        Assert.Equal(active.Employee.Id, Assert.Single(await TasksOfAsync(template.Id)).EmployeeId);
        Assert.Empty(await TasksOfAsync(direct.Id));
    }

    [Fact]
    public async Task Qayta_ochish_faqat_osha_kuni_va_yakundan_oldin()
    {
        var e = await kit.PersonAsync();
        var morning = At(2034, 5, 5, 10);
        var task = await kit.TaskAsync(e.Employee!.Id, At(2034, 5, 5, 18));
        var yesterday = await kit.TaskAsync(e.Employee.Id, At(2034, 5, 4, 18), HrTaskStatuses.Done, completedAt: At(2034, 5, 4, 17), comment: "ok");

        await kit.WithServiceAsync(async (s, _) =>
        {
            var me = await s.MeAsync(e.User.Id);
            await s.CompleteAsync(me, task.Id, new HrCompleteInput("Tayyor", []));
            Assert.Equal("open", (await s.ReopenAsync(me, task.Id)).Status);
            await s.CompleteAsync(me, task.Id, new HrCompleteInput("Tayyor", []));
            var ex = await Assert.ThrowsAsync<HrTasksException>(() => s.ReopenAsync(me, yesterday.Id));
            Assert.Contains("reopen_window", ex.Message);
        }, now: morning);

        // today's digest ran → no more reopening
        await fixture.Api.WithDbAsync(async db =>
        {
            if (!await db.HrJobRuns.AnyAsync(r => r.JobName == HrJobs.Digest && r.RunDate == new DateOnly(2034, 5, 5)))
                db.HrJobRuns.Add(new HrJobRun { JobName = HrJobs.Digest, RunDate = new DateOnly(2034, 5, 5) });
            await db.SaveChangesAsync();
        });
        await kit.WithServiceAsync(async (s, _) =>
        {
            var me = await s.MeAsync(e.User.Id);
            var ex = await Assert.ThrowsAsync<HrTasksException>(() => s.ReopenAsync(me, task.Id));
            Assert.Equal("hr_task_not_editable", ex.Code);
            Assert.Contains("digest_sent", ex.Message);
        }, now: morning.AddHours(10));
    }

    [Fact]
    public async Task Eslatma_bir_marta_eng_katta_offset_kun_oxiri_otkaziladi()
    {
        var sender = new FakeHrSender();
        var e = await kit.PersonAsync(telegram: true);
        var now = At(2038, 2, 3, 15);
        var soon = await kit.TaskAsync(e.Employee!.Id, now.AddMinutes(50), title: "Tez orada");
        await kit.TaskAsync(e.Employee.Id, At(2038, 2, 3, 23, 59), title: "Kun oxiri"); // no due time → skipped

        await kit.WithServiceAsync(async (s, _) =>
        {
            Assert.Equal(1, await s.SendRemindersAsync(now));
            Assert.Equal(0, await s.SendRemindersAsync(now)); // 120 and 60 are claimed
            Assert.Equal(1, await s.SendRemindersAsync(now.AddMinutes(25))); // the 30-minute one
            Assert.Equal(0, await s.SendRemindersAsync(now.AddMinutes(60))); // past the deadline
        }, sender);

        var texts = sender.To(e.Chat!.Value);
        Assert.Equal(["⏰ 50 daqiqadan keyin muddati tugaydi:\n• Tez orada", "⏰ 25 daqiqadan keyin muddati tugaydi:\n• Tez orada"], texts);
        await fixture.Api.WithDbAsync(async db =>
            Assert.Equal([30, 60, 120], (await db.HrTaskReminders.Where(r => r.TaskId == soon.Id).Select(r => (int)r.OffsetMinutes).ToListAsync()).Order()));
    }

    [Fact]
    public async Task Kunlik_yakun_direktorga_hammasi_managerga_shoxi_boshiga_yuborilmaydi()
    {
        var sender = new FakeHrSender();
        var day = new DateOnly(2036, 11, 12);
        var director = await kit.PersonAsync(Roles.Staff, HrRoles.Director, [HrTaskAccess.PermissionKey], telegram: true, name: "Direktor Yakun");
        var manager = await kit.PersonAsync(Roles.Staff, HrRoles.Manager, [HrTaskAccess.PermissionKey], telegram: true, name: "Rahbar Yakun");
        var idle = await kit.PersonAsync(Roles.Staff, HrRoles.Manager, [HrTaskAccess.PermissionKey], telegram: true, name: "Bekor Rahbar");
        var mine = await kit.PersonAsync(name: "Shoxdagi Xodim");
        var foreign = await kit.PersonAsync(name: "Tashqi Xodim");
        var idleSub = await kit.PersonAsync(name: "Ishsiz Xodim");
        var viewerNoRow = await kit.PersonAsync(Roles.Staff, hrRole: null, permissions: [HrTaskAccess.PermissionKey], telegram: true);
        await kit.ReportsToAsync(mine.Employee!.Id, manager.Employee!.Id);
        await kit.ReportsToAsync(idleSub.Employee!.Id, idle.Employee!.Id);

        await kit.TaskAsync(mine.Employee.Id, At(2036, 11, 12, 18), HrTaskStatuses.Done, title: "Bajarilgan ish",
            completedAt: At(2036, 11, 12, 17), comment: "tayyor");
        await kit.TaskAsync(mine.Employee.Id, At(2036, 11, 12, 18), title: "Ochiq ish");
        await kit.TaskAsync(foreign.Employee!.Id, At(2036, 11, 12, 18), title: "Tashqi ish");

        await kit.WithServiceAsync(async (s, _) => await s.SendDigestAsync(day, At(2036, 11, 12, 19)), sender);

        var toDirector = string.Join("\n", sender.To(director.Chat!.Value));
        Assert.Contains("📊 <b>12.11.2036 — kunlik yakun</b>", toDirector);
        Assert.Contains("Shoxdagi Xodim", toDirector);
        Assert.Contains("Tashqi ish", toDirector);

        var toManager = Assert.Single(sender.To(manager.Chat!.Value));
        Assert.Contains("✅ Bajarilgan: 1 / 2 (50%)", toManager);
        Assert.Contains("   ✅ Bajarilgan ish (izoh: tayyor)", toManager);
        Assert.Contains("   ⏳ Ochiq ish — muddat 12.11 18:00, ochiq", toManager);
        Assert.DoesNotContain("Tashqi", toManager);

        Assert.Empty(sender.To(idle.Chat!.Value)); // empty subtree digest → not sent
        Assert.Empty(sender.To(viewerNoRow.Chat!.Value)); // a panel user without an HR row has no scope
    }

    [Fact]
    public async Task Eski_close_qatorlari_yopiladi_keep_open_ochiq_qoladi()
    {
        var e = await kit.PersonAsync();
        var legacy = await kit.TaskAsync(e.Employee!.Id, At(2033, 1, 10, 18), policy: HrOverduePolicies.Close);
        var modern = await kit.TaskAsync(e.Employee.Id, At(2033, 1, 10, 18));
        await kit.WithServiceAsync(async (s, _) => Assert.True(await s.CloseOverdueAsync(At(2033, 1, 10, 23, 50)) >= 1));
        await fixture.Api.WithDbAsync(async db =>
        {
            Assert.Equal(HrTaskStatuses.Missed, (await db.HrTasks.SingleAsync(t => t.Id == legacy.Id)).Status);
            Assert.Equal(HrTaskStatuses.Open, (await db.HrTasks.SingleAsync(t => t.Id == modern.Id)).Status);
        });
    }

    [Fact]
    public async Task Rejalashtiruvchi_har_ishni_kuniga_bir_marta_bajaradi()
    {
        var e = await kit.PersonAsync();
        var template = await SaveAsync(Template(e.Employee!.Id, HrRecurrences.Daily, [1, 2, 3, 4, 5, 6, 7], due: new TimeOnly(21, 0)));
        var scopes = fixture.Api.Services.GetRequiredService<IServiceScopeFactory>();
        var scheduler = new HrTasksScheduler(scopes, NullLogger<HrTasksScheduler>.Instance);
        var day = new DateOnly(2037, 2, 2); // a Monday — a digest day
        var evening = At(2037, 2, 2, 20);

        var first = await scheduler.TickAsync(evening);
        foreach (var job in new[] { "generated", "morning_sent", "digest_sent" })
            Assert.True(first.ContainsKey(job), job);
        Assert.False(first.ContainsKey("closed")); // 23:50 not reached yet

        var second = await scheduler.TickAsync(evening.AddMinutes(5));
        Assert.DoesNotContain("generated", second.Keys);
        Assert.DoesNotContain("digest_sent", second.Keys);

        var night = await scheduler.TickAsync(At(2037, 2, 2, 23, 55));
        Assert.True(night.ContainsKey("closed"));
        Assert.True(night.ContainsKey("drafts_cleaned"));
        Assert.False((await scheduler.TickAsync(At(2037, 2, 2, 23, 59))).ContainsKey("closed"));

        await fixture.Api.WithDbAsync(async db =>
        {
            var runs = await db.HrJobRuns.Where(r => r.RunDate == day).ToListAsync();
            foreach (var job in new[] { HrJobs.Generate, HrJobs.Morning, HrJobs.Digest, HrJobs.CloseOverdue, HrJobs.CleanDrafts })
            {
                var run = Assert.Single(runs, r => r.JobName == job);
                Assert.Equal(HrJobs.StatusOk, run.Status);
                Assert.NotNull(run.FinishedAt);
            }
            // generation ran ONCE: one instance of our template for that day
            Assert.Single(await db.HrTasks.Where(t => t.TemplateId == template.Id && t.PeriodStart == day).ToListAsync());
        });
    }
}
