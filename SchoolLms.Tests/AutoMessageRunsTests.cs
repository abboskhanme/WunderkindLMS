using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Dtos.Billing;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// Automatic messages, 2026-10-15 (docs/modules/auto-messages.md): runs with statuses, approval,
/// stopping, retries, the absence cut-off, the payment rule on the receipt, one delivery log for
/// automatic and manual sends, statistics and the new endpoints' permissions. Every Telegram call
/// lands in a recording handler — nothing leaves the machine.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class AutoMessageRunsTests(ApiFixture fixture) : IAsyncLifetime
{
    private const string Url = "/api/admin/auto-messages";
    private static readonly DateTime Morning = new(2026, 10, 13, 9, 0, 0);
    private readonly List<string> _connectionStrings = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        foreach (var cs in _connectionStrings) NpgsqlConnection.ClearPool(new NpgsqlConnection(cs));
        return Task.CompletedTask;
    }

    // =====================================================================
    //  Approval
    // =====================================================================

    [Fact]
    public async Task Tasdiq_kutadigan_qoida_yubormaydi_tasdiqlansa_yuboradi_chiqarilgan_qoladi()
    {
        var db = await FreshAsync("am_approve");
        var ali = Pupil("Aliyev Ali Valiyevich", "5-A", "2015-10-13");
        var bek = Pupil("Bekov Bek Karimovich", "5-A", "2014-10-13");
        var gul = Pupil("Gulova Gul", "5-A", "2015-10-13");        // no parent on Telegram
        db.Students.AddRange(ali, bek, gul);
        db.TelegramRegistrations.AddRange(
            new TelegramRegistration { StudentId = ali.Id, ChatId = 501, ParentName = "Karim aka" },
            new TelegramRegistration { StudentId = ali.Id, ChatId = 502, ParentName = "Malika opa" },
            new TelegramRegistration { StudentId = bek.Id, ChatId = 503 });
        var approver = new AppUser { FullName = "Direktor", Role = Roles.Admin, Email = $"d.{Guid.NewGuid():N}", PasswordHash = "x" };
        db.Users.Add(approver);
        await db.SaveChangesAsync();
        await EnableAsync(db, "birthday", "Tabrik, {ism}! {sana} {vaqt}", requiresApproval: true);

        var handler = new RecordingHandler();
        var service = new AutoMessageService(db, ConfiguredTelegram(handler));

        // Before 08:00 the run is only planned.
        Assert.Empty(await service.RunDueAsync(Morning.Date.AddHours(7)));
        var planned = await db.AutoMessageRuns.AsNoTracking().SingleAsync();
        Assert.Equal((AutoMessageStatus.Scheduled, "2026-10-13"), (planned.Status, planned.Period));
        Assert.Equal(AutoMessageService.ToInstant(Morning.Date.AddHours(8)), planned.PlannedAt);

        // Due: prepared, nothing sent.
        var due = Assert.Single(await service.RunDueAsync(Morning));
        Assert.Equal((3, 0, 3, 1), (due.Pupils, due.Sent, due.Pending, due.NoTelegram));
        Assert.Empty(handler.Texts);
        var run = await db.AutoMessageRuns.AsNoTracking().SingleAsync();
        Assert.Equal(AutoMessageStatus.PendingApproval, run.Status);
        var rows = await db.AutoMessageLog.AsNoTracking().ToListAsync();
        Assert.Equal(3, rows.Count(r => r.Status == AutoMessageStatus.PendingApproval && r.RunId == run.Id));
        Assert.Single(rows, r => r.StudentId == gul.Id && r.Status == BroadcastDelivery.NoTelegram);
        Assert.Contains("Tabrik, Ali! 13.10.2026 09:00", rows.First(r => r.ChatId == 501).Text);

        // Later ticks do not send a waiting run.
        Assert.Empty(await service.RunDueAsync(Morning.AddMinutes(30)));
        Assert.Empty(handler.Texts);

        // Bek is taken out; then the run is approved.
        Assert.Equal(1, await service.ExcludeAsync(run.Id, bek.Id));
        var approved = await service.ApproveAsync(run.Id, approver.Id, Morning.AddHours(1));
        Assert.Equal((2, 0), (approved.Sent, approved.Failed));
        Assert.Equal(["Tabrik, Ali! 13.10.2026 09:00", "Tabrik, Ali! 13.10.2026 09:00"], handler.Texts);

        run = await db.AutoMessageRuns.AsNoTracking().SingleAsync();
        Assert.Equal((AutoMessageStatus.Done, approver.Id, 2, 1, 1, 0), (run.Status, run.ApprovedBy, run.Sent, run.Cancelled, run.NoTelegram, run.Pending));
        Assert.NotNull(run.FinishedAt);
        var bekRow = await db.AutoMessageLog.AsNoTracking().SingleAsync(r => r.StudentId == bek.Id);
        Assert.Equal(AutoMessageStatus.Cancelled, bekRow.Status);
        Assert.Equal("2026-10-13", (await db.AutoMessageRules.AsNoTracking().SingleAsync(r => r.Key == "birthday")).LastPeriod);

        // Approving twice is refused; the run's history reads back with the approver's name.
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApproveAsync(run.Id, approver.Id, Morning.AddHours(1)));
        var dto = await service.GetRunAsync(run.Id);
        Assert.Equal("Direktor", dto!.ApprovedByName);
    }

    [Fact]
    public async Task Bekor_qilingan_yuborishdan_hech_narsa_ketmaydi()
    {
        var db = await FreshAsync("am_cancel");
        var ali = Pupil("Aliyev Ali", "5-A", "2015-10-13");
        db.Students.Add(ali);
        db.TelegramRegistrations.Add(new TelegramRegistration { StudentId = ali.Id, ChatId = 511 });
        await db.SaveChangesAsync();
        await EnableAsync(db, "birthday", "Tabrik, {ism}!", requiresApproval: true);

        var handler = new RecordingHandler();
        var service = new AutoMessageService(db, ConfiguredTelegram(handler));
        Assert.Single(await service.RunDueAsync(Morning));
        var run = await db.AutoMessageRuns.AsNoTracking().SingleAsync();

        Assert.True(await service.CancelAsync(run.Id, "staff-1"));
        Assert.False(await service.CancelAsync(run.Id, "staff-1"));   // already stopped
        Assert.Empty(await service.RunDueAsync(Morning.AddMinutes(10)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.ApproveAsync(run.Id, "staff-1", Morning));
        Assert.Empty(handler.Texts);

        run = await db.AutoMessageRuns.AsNoTracking().SingleAsync();
        Assert.Equal((AutoMessageStatus.Cancelled, "staff-1", 1), (run.Status, run.CancelledBy, run.Cancelled));
        Assert.Equal(AutoMessageStatus.Cancelled, (await db.AutoMessageLog.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public async Task Tasdiqlanmagan_yuborish_kun_oxirida_ozi_toxtatiladi()
    {
        var db = await FreshAsync("am_day_end");
        var ali = Pupil("Aliyev Ali", "5-A", "2015-10-13");
        var vali = Pupil("Valiyev Vali", "5-A", "2015-10-14");
        db.Students.AddRange(ali, vali);
        db.TelegramRegistrations.AddRange(
            new TelegramRegistration { StudentId = ali.Id, ChatId = 521 },
            new TelegramRegistration { StudentId = vali.Id, ChatId = 522 });
        await db.SaveChangesAsync();
        await EnableAsync(db, "birthday", "Tabrik, {ism}!", requiresApproval: true);

        var handler = new RecordingHandler();
        var service = new AutoMessageService(db, ConfiguredTelegram(handler));
        Assert.Single(await service.RunDueAsync(Morning));

        // Just after midnight: yesterday's waiting run is stopped by the system; today's is planned.
        Assert.Empty(await service.RunDueAsync(new DateTime(2026, 10, 14, 0, 5, 0)));
        var runs = await db.AutoMessageRuns.AsNoTracking().OrderBy(r => r.Period).ToListAsync();
        Assert.Equal(2, runs.Count);
        Assert.Equal((AutoMessageStatus.Cancelled, AutoMessageService.SystemUser), (runs[0].Status, runs[0].CancelledBy));
        Assert.Contains("muddati tugadi", runs[0].Note);
        Assert.Equal(("2026-10-14", AutoMessageStatus.Scheduled), (runs[1].Period, runs[1].Status));
        Assert.Equal(AutoMessageStatus.Cancelled, (await db.AutoMessageLog.AsNoTracking().SingleAsync(l => l.StudentId == ali.Id)).Status);
        Assert.Empty(handler.Texts);
    }

    [Fact]
    public void Qoida_turlari_togri_ajraladi()
    {
        Assert.True(AutoMessageKind.IsEventDriven("payment"));
        Assert.False(AutoMessageKind.IsEventDriven("absence"));
        Assert.True(AutoMessageKind.IsMonthly("invoice"));
        Assert.True(AutoMessageKind.IsMonthly("debt"));
        Assert.False(AutoMessageKind.IsMonthly("payment"));
        // The payment rule is never due on the clock.
        Assert.Null(AutoMessageService.DuePeriod(new AutoMessageRule { Key = "payment", SendTime = "00:00" }, Morning));
    }

    // =====================================================================
    //  Switching off, retries
    // =====================================================================

    [Fact]
    public async Task Qoida_ochirilsa_rejalashtirilgan_yuborish_toxtaydi()
    {
        var db = await FreshAsync("am_switch_off");
        await EnableAsync(db, "birthday", "Tabrik, {ism}!");
        var service = new AutoMessageService(db, ConfiguredTelegram(new RecordingHandler()));
        Assert.Empty(await service.RunDueAsync(Morning.Date.AddHours(7)));
        Assert.Equal(AutoMessageStatus.Scheduled, (await db.AutoMessageRuns.AsNoTracking().SingleAsync()).Status);

        Assert.Equal(1, await service.CancelOpenRunsAsync("birthday", "staff-1"));
        var run = await db.AutoMessageRuns.AsNoTracking().SingleAsync();
        Assert.Equal((AutoMessageStatus.Cancelled, "staff-1"), (run.Status, run.CancelledBy));
        Assert.Contains("Qoida o'chirildi", run.Note);
    }

    [Fact]
    public async Task Xato_ketganlar_keyingi_tekshiruvlarda_uch_martagacha_qayta_yuboriladi()
    {
        var db = await FreshAsync("am_retry");
        var ali = Pupil("Aliyev Ali", "5-A", "2015-10-13");
        var bek = Pupil("Bekov Bek", "5-A", "2015-10-13");
        db.Students.AddRange(ali, bek);
        db.TelegramRegistrations.AddRange(
            new TelegramRegistration { StudentId = ali.Id, ChatId = 601 },   // fails once, then gets through
            new TelegramRegistration { StudentId = bek.Id, ChatId = 603 });  // always refused
        await db.SaveChangesAsync();
        await EnableAsync(db, "birthday", "Tabrik, {ism}!");

        var handler = new RecordingHandler((chat, call) => chat == 601 ? call > 1 : chat != 603);
        var service = new AutoMessageService(db, ConfiguredTelegram(handler));

        var first = Assert.Single(await service.RunDueAsync(Morning));
        Assert.Equal((0, 2), (first.Sent, first.Failed));
        Assert.Equal("2026-10-13", (await db.AutoMessageRules.AsNoTracking().SingleAsync(r => r.Key == "birthday")).LastPeriod);

        // The period is closed, yet the failures are tried again on the next ticks.
        var second = Assert.Single(await service.RunDueAsync(Morning.AddMinutes(2)));
        Assert.Equal((1, 1), (second.Sent, second.Failed));
        var third = Assert.Single(await service.RunDueAsync(Morning.AddMinutes(4)));
        Assert.Equal((0, 1), (third.Sent, third.Failed));
        Assert.Empty(await service.RunDueAsync(Morning.AddMinutes(6)));   // three attempts used

        var rows = await db.AutoMessageLog.AsNoTracking().ToListAsync();
        var aliRow = rows.Single(r => r.ChatId == 601);
        var bekRow = rows.Single(r => r.ChatId == 603);
        Assert.Equal((BroadcastDelivery.Sent, 2, (string?)null), (aliRow.Status, aliRow.Attempts, aliRow.Error));
        Assert.Equal((BroadcastDelivery.Failed, AutoMessageService.MaxAttempts), (bekRow.Status, bekRow.Attempts));
        Assert.NotNull(bekRow.Error);
        Assert.Equal(5, handler.Calls);
        var run = await db.AutoMessageRuns.AsNoTracking().SingleAsync();
        Assert.Equal((AutoMessageStatus.Done, 1, 1), (run.Status, run.Sent, run.Failed));
    }

    // =====================================================================
    //  Absence cut-off ("11:30 gacha")
    // =====================================================================

    [Fact]
    public async Task Kesish_vaqtidan_keyingi_dars_hisoblanmaydi_belgilanmagan_sinf_otkaziladi()
    {
        var db = await FreshAsync("am_cutoff");
        var world = await AbsenceWorldAsync(db);
        db.LessonTimes.AddRange(
            new LessonTime { Period = 1, StartTime = "08:30", EndTime = "09:15" },
            new LessonTime { Period = 2, StartTime = "09:25", EndTime = "10:10" },
            new LessonTime { Period = 3, StartTime = "11:40", EndTime = "12:25" });
        await db.SaveChangesAsync();

        var rule = await db.AutoMessageRules.SingleAsync(r => r.Key == "absence");
        rule.SendTime = "12:30";
        rule.CutoffTime = "11:30";
        await db.SaveChangesAsync();

        var handler = new RecordingHandler();
        var run = await new AutoMessageService(db, ConfiguredTelegram(handler)).RunAsync(rule, AbsenceWorld.Monday);

        // Ali missed lessons 1–2 and came to lesson 3 (after 11:30): absent "till 11:30" — told.
        // Bobur (6-B) missed lesson 1, but lesson 2 of 6-B is not marked — the class is skipped.
        // Dilnoza (7-C) is told; Eva has no parent on Telegram.
        Assert.Equal((3, 2, 1), (run.Pupils, run.Sent, run.NoTelegram));
        Assert.Equal(2, handler.Texts.Count);
        Assert.Contains(handler.Texts, t => t.Contains("Aliyev Ali (5-A) 12.01.2026 kuni maktabga kelmadi"));
        Assert.Contains(handler.Texts, t => t.Contains("Dilova Dilnoza"));
        Assert.DoesNotContain(handler.Texts, t => t.Contains("Boburov"));
        Assert.Contains("6-B", run.Note);
        Assert.Contains("11:30", run.Note);
        Assert.Contains("1 o'quvchiga xabar ketmadi", run.Note);
        Assert.False(await db.AutoMessageLog.AnyAsync(l => l.StudentId == world.Bobur.Id));

        // Without the cut-off (lessons after the send time count) Ali was at school — partial, not absent.
        var notifier = new DailyAbsenceNotifier(db, ConfiguredTelegram(new RecordingHandler()),
            fixture.Api.Services.GetRequiredService<ILogger<DailyAbsenceNotifier>>());
        Assert.DoesNotContain((await notifier.PreviewAsync(AbsenceWorld.Monday)).Rows, r => r.StudentId == world.Ali.Id);
    }

    // =====================================================================
    //  The nurse's button in the same log
    // =====================================================================

    [Fact]
    public async Task Hamshira_tugmasi_ham_jurnalga_yoziladi()
    {
        var db = await FreshAsync("am_manual");
        var world = await AbsenceWorldAsync(db);
        var nurse = new AppUser { FullName = "Hamshira Opa", Role = Roles.Staff, Email = $"n.{Guid.NewGuid():N}", PasswordHash = "x" };
        db.Users.Add(nurse);
        await db.SaveChangesAsync();

        var handler = new RecordingHandler();
        var notifier = new DailyAbsenceNotifier(db, ConfiguredTelegram(handler),
            fixture.Api.Services.GetRequiredService<ILogger<DailyAbsenceNotifier>>());
        var result = await notifier.SendAsync(AbsenceWorld.Monday, nurse.Id);
        Assert.Equal(2, result.Sent);   // Bobur and Dilnoza (whole day absent, all lessons counted)

        var rows = await db.AutoMessageLog.AsNoTracking().ToListAsync();
        Assert.All(rows, r => Assert.Equal((AutoMessageKind.Absence, AbsenceWorld.Monday, AutoMessageSource.Manual, nurse.Id),
            (r.RuleKey, r.Period, r.Source, r.SentBy)));
        Assert.Equal(2, rows.Count(r => r.Status == BroadcastDelivery.Sent && r.Attempts == 1));
        Assert.Single(rows, r => r.StudentId == world.Eva.Id && r.Status == BroadcastDelivery.NoTelegram);

        var page = await new AutoMessageService(db, ConfiguredTelegram(handler)).LogPageAsync(
            new Application.Dtos.AutoMessageLogQuery(Source: AutoMessageSource.Manual, Search: "bobur"), 1, 10);
        var row = Assert.Single(page.Rows);
        Assert.Equal(("Hamshira Opa", 1), (row.SentByName, page.Total));
    }

    // =====================================================================
    //  Payment rule on the receipt
    // =====================================================================

    [Fact]
    public async Task Tolov_qoidasi_ochiq_bolsa_chek_odatiy_izoh_bilan_ketadi_va_jurnalda()
    {
        var (db, payment, handler, receipts) = await PaymentWorldAsync("am_pay_off");

        Assert.True(await receipts.SendToGuardianAsync(payment.Id));

        Assert.Equal(2, handler.Documents.Count);
        Assert.All(handler.Documents, d =>
        {
            Assert.StartsWith("To'lov cheki\n№ 777", d.Caption);
            Assert.Null(d.ParseMode);
        });
        Assert.Empty(handler.Texts);
        var rows = await db.AutoMessageLog.AsNoTracking().ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(
            (AutoMessageKind.Payment, "777", BroadcastDelivery.Sent, AutoMessageSource.Auto, 1, (Guid?)null),
            (r.RuleKey, r.RefId, r.Status, r.Source, r.Attempts, r.RunId)));

        // A manual resend: two more rows, marked manual with who pressed it.
        Assert.True(await receipts.SendToGuardianAsync(payment.Id, "cashier-1"));
        rows = await db.AutoMessageLog.AsNoTracking().ToListAsync();
        Assert.Equal(4, rows.Count);
        Assert.Equal(2, rows.Count(r => r.Source == AutoMessageSource.Manual && r.SentBy == "cashier-1"));
    }

    [Fact]
    public async Task Tolov_qoidasi_yoqilsa_shablon_chek_izohiga_aylanadi()
    {
        var (db, payment, handler, receipts) = await PaymentWorldAsync("am_pay_on");
        await EnableAsync(db, "payment", "**Rahmat**, {ota_ona}! {ism}: {summa} so'm, chek {chek}, {usul}, {sana} {vaqt}. Qarz: {qarzdorlik}");

        Assert.True(await receipts.SendToGuardianAsync(payment.Id));

        Assert.Equal(2, handler.Documents.Count);
        var karim = handler.Documents.Single(d => d.ChatId == 701);
        Assert.Equal("HTML", karim.ParseMode);
        Assert.Equal("<b>Rahmat</b>, Karim aka! Ali: 1 250 000 so'm, chek 777, Naqd pul, 11.09.2026 14:35. Qarz: 0", karim.Caption);
        Assert.StartsWith("<b>Rahmat</b>, Ota-ona!", handler.Documents.Single(d => d.ChatId == 702).Caption);
        Assert.Empty(handler.Texts);
        var rows = await db.AutoMessageLog.AsNoTracking().ToListAsync();
        Assert.Equal(2, rows.Count(r => r.Status == BroadcastDelivery.Sent && r.Text.StartsWith("<b>Rahmat</b>")));
    }

    [Fact]
    public async Task Tolov_qoidasining_uzun_matni_chekdan_keyin_alohida_ketadi()
    {
        var (db, payment, handler, receipts) = await PaymentWorldAsync("am_pay_long");
        var longText = "To'lov uchun rahmat, {ism}!\n" + string.Join("\n", Enumerable.Repeat("Maktab qoidalari: har oy 10-sanagacha to'lang.", 30));
        await EnableAsync(db, "payment", longText);

        Assert.True(await receipts.SendToGuardianAsync(payment.Id));

        // The receipt keeps its usual caption; the text follows as its own message.
        Assert.Equal(2, handler.Documents.Count);
        Assert.All(handler.Documents, d => Assert.StartsWith("To'lov cheki", d.Caption));
        Assert.Equal(2, handler.Texts.Count);
        Assert.All(handler.Texts, t => Assert.StartsWith("To'lov uchun rahmat, Ali!", t));
        Assert.True(handler.Texts[0].Length > 1024);
        Assert.Equal(2, await db.AutoMessageLog.CountAsync(r => r.Status == BroadcastDelivery.Sent && r.RuleKey == "payment"));
    }

    // =====================================================================
    //  Statistics
    // =====================================================================

    [Fact]
    public async Task Statistika_qoida_holat_va_kun_boyicha_sanaydi()
    {
        var db = await FreshAsync("am_stats");
        var ali = Pupil("Aliyev Ali", "5-A", "2015-01-01");
        db.Students.Add(ali);
        await db.SaveChangesAsync();
        var today = AppClock.Today;
        var now = AppClock.NowInstant;
        var yesterday = now.AddDays(-1);
        void Row(string rule, string period, long? chat, string status, DateTimeOffset at) => db.AutoMessageLog.Add(new AutoMessageLogEntry
        {
            RuleKey = rule, Period = period, StudentId = ali.Id, ChatId = chat, Status = status, At = at,
        });
        Row("birthday", "a", 1, BroadcastDelivery.Sent, now);
        Row("birthday", "a", 2, BroadcastDelivery.Sent, now);
        Row("birthday", "a", 3, BroadcastDelivery.Failed, now);
        Row("debt", "b", null, BroadcastDelivery.NoTelegram, now);
        Row("absence", "c", 1, AutoMessageStatus.PendingApproval, now);
        Row("absence", "d", 1, AutoMessageStatus.Cancelled, yesterday);
        Row("payment", "e", 1, BroadcastDelivery.Sent, now.AddDays(-30));    // outside the period
        await db.SaveChangesAsync();

        var stats = await new AutoMessageService(db, ConfiguredTelegram(new RecordingHandler()))
            .StatsAsync(today.AddDays(-1), today);
        Assert.Equal(2, stats.Totals[BroadcastDelivery.Sent]);
        Assert.Equal((1, 1, 1, 1), (stats.Totals[BroadcastDelivery.Failed], stats.Totals[BroadcastDelivery.NoTelegram],
            stats.Totals[AutoMessageStatus.PendingApproval], stats.Totals[AutoMessageStatus.Cancelled]));
        Assert.False(stats.Totals.ContainsKey("payment"));
        var iso = today.ToString("yyyy-MM-dd");
        Assert.Contains(stats.Cells, c => c is { RuleKey: "birthday", Status: BroadcastDelivery.Sent, Count: 2 } && c.Day == iso);
        Assert.Contains(stats.Cells, c => c is { RuleKey: "absence", Status: AutoMessageStatus.Cancelled, Count: 1 }
                                          && c.Day == today.AddDays(-1).ToString("yyyy-MM-dd"));
        Assert.Contains(stats.ByRule, c => c is { RuleKey: "birthday", Status: BroadcastDelivery.Failed, Count: 1 });
        Assert.DoesNotContain(stats.Cells, c => c.RuleKey == "payment");
    }

    // =====================================================================
    //  API and permissions
    // =====================================================================

    [Fact]
    public async Task Yangi_endpointlar_messages_ruxsati_bilan()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        using var teacher = await fixture.Api.ClientAsAsync(Roles.Teacher);
        using var staff = await fixture.Api.ClientAsAsync(Roles.Staff);
        using var messenger = await fixture.Api.ClientAsAsync(Roles.Staff, "messages");
        var unknown = Guid.NewGuid();

        string[] reads =
        [
            $"{Url}/runs", $"{Url}/runs?from=2026-10-01&to=2026-10-15&rule=absence", $"{Url}/stats?from=2026-10-01&to=2026-10-15",
            $"{Url}/log/paged?page=1&pageSize=20&status=sent&q=ali", $"{Url}/log/export?from=2026-10-01&to=2026-10-15",
        ];
        foreach (var url in reads)
        {
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await messenger.GetAsync(url)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync(url)).StatusCode);
            // The module's gate (AdminPerm "messages"): staff may read admin sections, never act without it.
            Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync(url)).StatusCode);
        }
        var export = await admin.GetAsync($"{Url}/log/export");
        Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", export.Content.Headers.ContentType?.MediaType);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync($"{Url}/stats?from=2026-10-15&to=2026-10-01")).StatusCode);

        string[] writes = [$"{Url}/runs/{unknown}/approve", $"{Url}/runs/{unknown}/cancel"];
        foreach (var url in writes)
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await teacher.PostAsJsonAsync(url, new { })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await staff.PostAsJsonAsync(url, new { })).StatusCode);
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.PostAsJsonAsync($"{Url}/runs/{unknown}/exclude", new { studentId = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync($"{Url}/runs/{unknown}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"{Url}/runs/{unknown}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsJsonAsync($"{Url}/runs/{unknown}/cancel", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.PostAsJsonAsync($"{Url}/runs/{unknown}/exclude", new { studentId = "x" })).StatusCode);

        // A waiting run in the shared database: stopped through the API and audited.
        var run = new AutoMessageRun
        {
            RuleKey = "birthday", Period = "2000-01-01", Status = AutoMessageStatus.PendingApproval,
            PlannedAt = AppClock.NowInstant, CreatedAt = AppClock.NowInstant,
        };
        await fixture.Api.WithDbAsync(async db => { db.AutoMessageRuns.Add(run); await db.SaveChangesAsync(); });
        try
        {
            // The test API has no bot token: approval is refused with a reason, not a silent nothing.
            var approve = await messenger.PostAsJsonAsync($"{Url}/runs/{run.Id}/approve", new { });
            Assert.Equal(HttpStatusCode.Conflict, approve.StatusCode);

            var cancel = await messenger.PostAsJsonAsync($"{Url}/runs/{run.Id}/cancel", new { });
            Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
            var body = await cancel.Content.ReadFromJsonAsync<JsonElement>();
            Assert.Equal("cancelled", body.GetProperty("status").GetString());
            Assert.Equal(HttpStatusCode.Conflict, (await messenger.PostAsJsonAsync($"{Url}/runs/{run.Id}/cancel", new { })).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict,
                (await messenger.PostAsJsonAsync($"{Url}/runs/{run.Id}/exclude", new { studentId = "x" })).StatusCode);
            await fixture.Api.WithDbAsync(async db =>
                Assert.True(await db.AuditLogs.AnyAsync(a => a.EntityType == "AutoMessageRun" && a.EntityId == run.Id.ToString() && a.Action == "cancel")));
        }
        finally
        {
            await fixture.Api.WithDbAsync(async db =>
            {
                await db.AutoMessageRuns.Where(r => r.Id == run.Id).ExecuteDeleteAsync();
            });
        }
    }

    [Fact]
    public async Task Qoida_tasdiq_va_kesish_vaqti_saqlanadi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        AutoMessageRule original = null!;
        await fixture.Api.WithDbAsync(async db => original = await db.AutoMessageRules.AsNoTracking().SingleAsync(r => r.Key == "absence"));
        try
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"{Url}/absence", new
            {
                enabled = false, sendTime = "12:00", dayOfMonth = 1, minAmount = 0, text = original.Text, cutoffTime = "25:61",
            })).StatusCode);
            var ok = await admin.PutAsJsonAsync($"{Url}/absence", new
            {
                enabled = false, sendTime = "12:00", dayOfMonth = 1, minAmount = 0, text = original.Text,
                requiresApproval = true, cutoffTime = "11:30",
            });
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            var dto = await ok.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(dto.GetProperty("requiresApproval").GetBoolean());
            Assert.Equal("11:30", dto.GetProperty("cutoffTime").GetString());

            // The payment rule never waits for approval — a receipt is not held back.
            await fixture.Api.WithDbAsync(async db =>
                Assert.False((await db.AutoMessageRules.AsNoTracking().SingleAsync(r => r.Key == "payment")).RequiresApproval, "seeded"));
            var pay = await admin.PutAsJsonAsync($"{Url}/payment", new
            {
                enabled = false, sendTime = "09:00", dayOfMonth = 1, minAmount = 0, text = "To'lov: {summa}", requiresApproval = true,
            });
            Assert.Equal(HttpStatusCode.OK, pay.StatusCode);
            var payDto = await pay.Content.ReadFromJsonAsync<JsonElement>();
            Assert.False(payDto.GetProperty("requiresApproval").GetBoolean(), payDto.ToString());
            Assert.True(payDto.GetProperty("eventDriven").GetBoolean());
            Assert.Contains(payDto.GetProperty("tokens").EnumerateArray(), t => t.GetProperty("label").GetString() == "To'lov summasi");

            var preview = await admin.PostAsJsonAsync($"{Url}/payment/preview", new { text = "{summa} so'm" });
            Assert.Equal("1 500 000 so'm", (await preview.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("sample").GetString());
        }
        finally
        {
            await fixture.Api.WithDbAsync(async db =>
            {
                var r = await db.AutoMessageRules.SingleAsync(x => x.Key == "absence");
                (r.Enabled, r.SendTime, r.Text, r.RequiresApproval, r.CutoffTime) =
                    (original.Enabled, original.SendTime, original.Text, original.RequiresApproval, original.CutoffTime);
                var p = await db.AutoMessageRules.SingleAsync(x => x.Key == "payment");
                p.Text = AutoMessageService.DefaultText["payment"];
                await db.SaveChangesAsync();
            });
        }
    }

    // =====================================================================
    //  Helpers
    // =====================================================================

    private async Task<AppDbContext> FreshAsync(string name)
    {
        var database = await fixture.Postgres.CreateDatabaseAsync(name);
        _connectionStrings.Add(database.OwnerConnectionString);
        return PostgresFixture.NewContext(database.OwnerConnectionString);
    }

    private static async Task EnableAsync(AppDbContext db, string key, string text, bool requiresApproval = false)
    {
        var rule = await db.AutoMessageRules.SingleAsync(r => r.Key == key);
        rule.Enabled = true;
        rule.Text = text;
        rule.RequiresApproval = requiresApproval;
        await db.SaveChangesAsync();
    }

    private static Student Pupil(string fullName, string className, string birthDate)
    {
        var s = GeneralSettingsFlagsTests.NewStudent(fullName, className, "+998900000001");
        s.Id = Guid.NewGuid().ToString();
        s.BirthDate = birthDate;
        s.FirstName = "";
        return s;
    }

    private sealed record AbsenceWorld(Student Ali, Student Bobur, Student Dilnoza, Student Eva)
    {
        public const string Monday = "2026-01-12";
    }

    /// <summary>
    /// 5-A: lessons 1–3 marked; Ali absent in 1–2, present in 3; Eva absent all day, no Telegram.
    /// 6-B: lessons 1–2 in the timetable, only lesson 1 marked; Bobur absent in 1; Dilnoza (7-C, one
    /// marked lesson) absent all day.
    /// </summary>
    private static async Task<AbsenceWorld> AbsenceWorldAsync(AppDbContext db)
    {
        const string monday = AbsenceWorld.Monday;
        var a5 = new SchoolClass { Name = "5-A", Grade = 5, Language = "uz" };
        var b6 = new SchoolClass { Name = "6-B", Grade = 6, Language = "uz" };
        var c7 = new SchoolClass { Name = "7-C", Grade = 7, Language = "uz" };
        var math = new Subject { Name = "Matematika" };
        db.Classes.AddRange(a5, b6, c7);
        db.Subjects.Add(math);
        var ali = Pupil("Aliyev Ali", "5-A", "2015-01-01");
        var eva = Pupil("Ergasheva Eva", "5-A", "2015-01-01");
        var bobur = Pupil("Boburov Bobur", "6-B", "2014-01-01");
        var dil = Pupil("Dilova Dilnoza", "7-C", "2013-01-01");
        db.Students.AddRange(ali, eva, bobur, dil);
        var unexcused = new AbsenceReason { Name = "Sababsiz", Short = "S" };
        db.AbsenceReasons.Add(unexcused);
        db.Quarters.Add(new QuarterPeriod { Quarter = 3, StartDate = "2026-01-10", EndDate = "2026-03-20" });
        db.TelegramRegistrations.AddRange(
            new TelegramRegistration { StudentId = ali.Id, ChatId = 901, ParentName = "Ota" },
            new TelegramRegistration { StudentId = bobur.Id, ChatId = 902 },
            new TelegramRegistration { StudentId = dil.Id, ChatId = 903 });
        await db.SaveChangesAsync();

        var week = ScheduleMath.GetQuarterWeeks("2026-01-10", "2026-03-20")
            .First(w => string.CompareOrdinal(monday, w.StartISO) >= 0 && string.CompareOrdinal(monday, w.EndISO) <= 0);
        ScheduleTemplate Template(SchoolClass c, params int[] periods) => new()
        {
            ClassId = c.Id, Name = "Asosiy",
            Lessons = [.. periods.Select(p => new ScheduleLesson { Day = 0, Period = p, SubjectId = math.Id })],
        };
        var t5 = Template(a5, 1, 2, 3);
        var t6 = Template(b6, 1, 2);
        var t7 = Template(c7, 1);
        db.ScheduleTemplates.AddRange(t5, t6, t7);
        await db.SaveChangesAsync();
        foreach (var (c, t) in new[] { (a5, t5), (b6, t6), (c7, t7) })
            db.WeekAssignments.Add(new WeekAssignment { ClassId = c.Id, Quarter = 3, Week = week.Week, TemplateId = t.Id });

        void Mark(SchoolClass c, int period) => db.DailyAttendanceMarks.Add(new DailyAttendanceMark
        {
            ClassId = c.Id, SubjectId = math.Id, Date = monday, Period = period, MarkedBy = "nurse", MarkedAt = AppClock.NowInstant,
        });
        void Absent(SchoolClass c, Student s, int period) => db.JournalEntries.Add(new JournalEntry
        {
            ClassId = c.Id, SubjectId = math.Id, StudentId = s.Id, Date = monday, Period = period, Quarter = 3, ReasonId = unexcused.Id,
        });
        foreach (var p in new[] { 1, 2, 3 }) { Mark(a5, p); Absent(a5, eva, p); }
        Absent(a5, ali, 1);
        Absent(a5, ali, 2);
        Mark(b6, 1);
        Absent(b6, bobur, 1);
        Mark(c7, 1);
        Absent(c7, dil, 1);
        await db.SaveChangesAsync();
        return new AbsenceWorld(ali, bobur, dil, eva);
    }

    private async Task<(AppDbContext Db, PaymentDto Payment, RecordingHandler Handler, ReceiptService Receipts)> PaymentWorldAsync(string name)
    {
        var db = await FreshAsync(name);
        var ali = Pupil("Aliyev Ali Valiyevich", "5-A", "2015-01-01");
        db.Students.Add(ali);
        db.TelegramRegistrations.AddRange(
            new TelegramRegistration { StudentId = ali.Id, ChatId = 701, ParentName = "Karim aka" },
            new TelegramRegistration { StudentId = ali.Id, ChatId = 702 });
        await db.SaveChangesAsync();

        var payment = new PaymentDto(
            Id: Guid.NewGuid(), ReceiptNo: 777, StudentId: ali.Id, StudentName: ali.FullName, Amount: 1_250_000m,
            Method: PaymentMethod.Cash, CashShiftId: Guid.NewGuid(), CashierId: "cashier-1", CashierName: "Kassir",
            Note: null, ReceivedAt: new DateTimeOffset(2026, 9, 11, 9, 35, 0, TimeSpan.Zero), ReversalOf: null, ReversedBy: null,
            Unallocated: 0m,
            Allocations: [new PaymentAllocationDto(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "tuition", "Maktab to'lovi",
                new DateOnly(2026, 9, 1), 1_250_000m)]);
        var handler = new RecordingHandler();
        var receipts = new ReceiptService(new OnePayment(payment), db, ConfiguredTelegram(handler),
            fixture.Api.Services.GetRequiredService<ILogger<ReceiptService>>());
        return (db, payment, handler, receipts);
    }

    private TelegramService ConfiguredTelegram(HttpMessageHandler handler)
    {
        var service = new TelegramService(
            fixture.Api.Services.GetRequiredService<IConfiguration>(),
            new Factory(handler),
            fixture.Api.Services.GetRequiredService<ILogger<TelegramService>>());
        service.Set("test-token", "test_bot");
        return service;
    }

    /// <summary>
    /// Answers every Telegram call (200, or 403 when <c>ok(chat, nthCallForThatChat)</c> says no) and
    /// keeps what was sent: message texts, and documents with their caption — nothing leaves the machine.
    /// </summary>
    private sealed class RecordingHandler(Func<long, int, bool>? ok = null) : HttpMessageHandler
    {
        private readonly Dictionary<long, int> _calls = [];
        public List<string> Texts { get; } = [];
        public List<(long ChatId, string? Caption, string? ParseMode)> Documents { get; } = [];
        public int Calls { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            long chat;
            if (request.Content is MultipartFormDataContent form)
            {
                var parts = new Dictionary<string, string>();
                foreach (var part in form)
                {
                    var partName = part.Headers.ContentDisposition?.Name?.Trim('"');
                    if (partName is null or "document") continue;
                    parts[partName] = await part.ReadAsStringAsync(ct);
                }
                chat = long.Parse(parts["chat_id"]);
                Documents.Add((chat, parts.GetValueOrDefault("caption"), parts.GetValueOrDefault("parse_mode")));
            }
            else
            {
                using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                chat = doc.RootElement.GetProperty("chat_id").GetInt64();
                Texts.Add(doc.RootElement.GetProperty("text").GetString()!);
            }
            Calls++;
            _calls[chat] = _calls.GetValueOrDefault(chat) + 1;
            var accepted = ok?.Invoke(chat, _calls[chat]) ?? true;
            return accepted
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"ok\":true}") }
                : new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent("{\"ok\":false,\"description\":\"Forbidden: bot was blocked by the user\"}") };
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    /// <summary>The payment service for the receipt: one payment to read, nothing else.</summary>
    private sealed class OnePayment(PaymentDto payment) : IPaymentService
    {
        public Task<PaymentDto?> GetAsync(Guid paymentId, CancellationToken ct = default) =>
            Task.FromResult<PaymentDto?>(paymentId == payment.Id ? payment : null);

        public Task<PaymentDto> AcceptAsync(AcceptPaymentRequest request, string cashierId, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<PaymentDto> ReverseAsync(Guid paymentId, string reason, string approverId, Guid? cashBoxId = null, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<PaymentDto>> ListAsync(PaymentQuery query, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<AllocationSuggestionDto>> SuggestAllocationAsync(string studentId, decimal amount, CancellationToken ct = default) =>
            throw new NotSupportedException();
    }
}
