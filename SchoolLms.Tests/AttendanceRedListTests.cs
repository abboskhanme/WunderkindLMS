using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// Attendance red list (docs/modules/attendance-red-list.md, 2026-10-15): the scan opens one case per pupil
/// who misses school day after day without a reason, teachers work their own pupils' cases, management
/// sees everything, and nothing reaches a parent.
///
/// <para>Scan tests run on their own fresh database — the scan reads the whole school, so a neighbour's
/// journal row would change the numbers. API tests use the shared one and insert cases directly.</para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class AttendanceRedListTests(ApiFixture fixture) : IAsyncLifetime
{
    private const string AdminUrl = "/api/admin/attendance/red-list";
    private const string TeacherUrl = "/api/teacher/homeroom/red-list";
    private readonly List<string> _connectionStrings = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        foreach (var cs in _connectionStrings) NpgsqlConnection.ClearPool(new NpgsqlConnection(cs));
        return Task.CompletedTask;
    }

    // 2026-03-05 is a Thursday: Thu, Fri, (weekend), Mon, Tue.
    private static readonly string[] Week = ["2026-03-05", "2026-03-06", "2026-03-09", "2026-03-10"];

    // =====================================================================
    //  Scan: the rules
    // =====================================================================

    [Fact]
    public async Task Uch_kun_ketma_ket_sababsiz_holat_ochadi_qisman_kun_uzadi_dam_olish_va_tekshirilmagan_kun_neytral()
    {
        var db = await FreshAsync("redlist_streak");
        var s = await SchoolAsync(db, "5-A", "Ali", "Bek", "Dil", "Eli", "Kasal", "Kech");
        // Monday nobody took attendance: a pupil without a row is "not checked" that day.
        Lessons(db, s, Week, unmarked: "2026-03-09");
        Days(db, s, "Ali", Week, "UUU.");   // Thu Fri Mon away, back on Tuesday
        Days(db, s, "Bek", Week, "UPUU");   // Friday only one lesson missed — the run breaks
        Days(db, s, "Dil", Week, "UU.U");   // Monday unchecked — neither breaks nor counts
        Days(db, s, "Eli", Week, "....");
        Days(db, s, "Kasal", Week, "EEEE"); // excused every day
        Days(db, s, "Kech", Week, "LLLL");  // late, i.e. in class
        await db.SaveChangesAsync();

        var result = await Service(db).ScanAsync(new DateOnly(2026, 3, 10));
        Assert.Equal(2, result.Opened);

        var cases = await db.AttendanceCases.AsNoTracking().ToListAsync();
        Assert.Equal(new[] { s.Pupils["Ali"], s.Pupils["Dil"] }.Order(), cases.Select(c => c.StudentId).Order());

        var ali = cases.Single(c => c.StudentId == s.Pupils["Ali"]);
        Assert.Equal(AttendanceCaseTrigger.Consecutive, ali.Trigger);
        Assert.Equal(AttendanceCaseStatus.ContactNeeded, ali.Status);
        Assert.Equal(3, ali.Streak);
        Assert.Equal(new DateOnly(2026, 3, 9), ali.LastAbsentOn);
        Assert.Equal(new DateOnly(2026, 3, 10), ali.LastPresentOn);   // "qaytdi"
        Assert.Equal([new DateOnly(2026, 3, 9), new DateOnly(2026, 3, 6), new DateOnly(2026, 3, 5)], ali.AbsentDates);
        Assert.Equal(new DateOnly(2026, 3, 10), ali.OpenedOn);

        var dil = cases.Single(c => c.StudentId == s.Pupils["Dil"]);
        Assert.Equal(3, dil.Streak);
        Assert.Equal(new DateOnly(2026, 3, 10), dil.LastAbsentOn);
        Assert.Null(dil.LastPresentOn);

        // Opening is audited, by the system.
        Assert.Equal(2, await db.AuditLogs.CountAsync(a => a.EntityType == AttendanceRedListService.AuditEntity && a.Action == "open"));
    }

    [Fact]
    public async Task Sababli_kunlar_hisoblanmaydi_sozlama_yoqilsa_hisoblanadi_qayta_tekshiruv_ikkinchi_holat_ochmaydi()
    {
        var db = await FreshAsync("redlist_excused");
        var s = await SchoolAsync(db, "6-B", "Ali", "Kasal");
        Lessons(db, s, Week);
        Days(db, s, "Ali", Week, "UUUU");
        Days(db, s, "Kasal", Week, "EEEE");
        await db.SaveChangesAsync();
        var service = Service(db);
        var tuesday = new DateOnly(2026, 3, 10);

        Assert.Equal(1, (await service.ScanAsync(tuesday)).Opened);
        // Idempotent: the same day again opens nothing and changes nothing.
        var again = await service.ScanAsync(tuesday);
        Assert.Equal((0, 0, 1), (again.Opened, again.Updated, again.Open));
        Assert.Equal(4, (await db.AttendanceCases.AsNoTracking().SingleAsync()).Streak);

        var settings = await db.AttendanceRedListSettings.SingleAsync();
        settings.CountExcused = true;
        await db.SaveChangesAsync();
        Assert.Equal(1, (await service.ScanAsync(tuesday)).Opened);
        var kasal = await db.AttendanceCases.AsNoTracking().SingleAsync(c => c.StudentId == s.Pupils["Kasal"]);
        Assert.Equal(4, kasal.Streak);

        // The database itself refuses a second open case for the same pupil.
        await using var other = PostgresFixture.NewContext(_connectionStrings[^1]);
        other.AttendanceCases.Add(new AttendanceCase
        {
            StudentId = s.Pupils["Ali"], OpenedOn = tuesday, CreatedAt = AppClock.NowInstant, UpdatedAt = AppClock.NowInstant,
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => other.SaveChangesAsync());
    }

    [Fact]
    public async Task Oyna_qoidasi_14_kunda_5_kun_sababsiz()
    {
        var db = await FreshAsync("redlist_window");
        var s = await SchoolAsync(db, "7-A", "Fazo", "Gul");
        string[] twoWeeks =
        [
            "2026-03-02", "2026-03-03", "2026-03-04", "2026-03-05", "2026-03-06",
            "2026-03-09", "2026-03-10", "2026-03-11", "2026-03-12", "2026-03-13",
        ];
        Lessons(db, s, twoWeeks);
        Days(db, s, "Fazo", twoWeeks, "U.U.U.U.U.");   // five days, never two in a row
        Days(db, s, "Gul", twoWeeks, "U.U.U.U...");    // four
        await db.SaveChangesAsync();

        var result = await Service(db).ScanAsync(new DateOnly(2026, 3, 13));
        Assert.Equal(1, result.Opened);
        var fazo = await db.AttendanceCases.AsNoTracking().SingleAsync();
        Assert.Equal(s.Pupils["Fazo"], fazo.StudentId);
        Assert.Equal(AttendanceCaseTrigger.Window, fazo.Trigger);
        Assert.Equal((5, 1), (fazo.WindowCount, fazo.Streak));
        Assert.Equal(new DateOnly(2026, 3, 13), fazo.LastPresentOn);
    }

    [Fact]
    public async Task Amal_holatni_ozgartiradi_hal_boldi_yopadi_keyingi_seriya_yangi_holat_ochadi()
    {
        var db = await FreshAsync("redlist_actions");
        var s = await SchoolAsync(db, "8-A", "Ali");
        Lessons(db, s, ["2026-03-05", "2026-03-06", "2026-03-09"]);
        Days(db, s, "Ali", ["2026-03-05", "2026-03-06", "2026-03-09"], "UUU");
        var staff = new AppUser { FullName = "Zavuch", Role = Roles.Staff, Email = "zavuch.redlist" };
        db.Users.Add(staff);
        await db.SaveChangesAsync();
        var service = Service(db);
        var actor = new RedListActor(staff.Id, staff.FullName, AttendanceCaseActorType.Staff);

        await service.ScanAsync(new DateOnly(2026, 3, 9));
        var first = await db.AttendanceCases.AsNoTracking().SingleAsync();

        var blank = await Assert.ThrowsAsync<AttendanceCaseException>(() =>
            service.AddActionAsync(first.Id, new AddAttendanceCaseActionRequest("qayta_qongiroq", "  ", null), actor));
        Assert.Equal(400, blank.Status);
        var badStatus = await Assert.ThrowsAsync<AttendanceCaseException>(() =>
            service.AddActionAsync(first.Id, new AddAttendanceCaseActionRequest("sms", "x", null), actor));
        Assert.Equal(400, badStatus.Status);

        var callBack = await service.AddActionAsync(first.Id,
            new AddAttendanceCaseActionRequest("qayta_qongiroq", "Onasi kechqurun qo'ng'iroq qilishni so'radi", null), actor);
        Assert.Equal(AttendanceCaseStatus.CallBack, callBack.Case.Status);
        Assert.Equal(AppClock.Today.AddDays(1), callBack.Case.CallbackOn);   // tomorrow when not given
        Assert.Equal("Zavuch", Assert.Single(callBack.Actions).ActorName);

        var resolved = await service.AddActionAsync(first.Id,
            new AddAttendanceCaseActionRequest("hal_boldi", "Kasal bo'lgan, ma'lumotnoma olib keladi", null), actor);
        Assert.Equal(AttendanceCaseStatus.Resolved, resolved.Case.Status);
        Assert.NotNull(resolved.Case.ClosedAt);
        Assert.Null(resolved.Case.CallbackOn);
        Assert.Equal("Zavuch", resolved.Case.ClosedByName);
        var closed = await Assert.ThrowsAsync<AttendanceCaseException>(() =>
            service.AddActionAsync(first.Id, new AddAttendanceCaseActionRequest("aloqa_kerak", "Yana", null), actor));
        Assert.Equal(409, closed.Status);

        // The same absences do not reopen a resolved case...
        Assert.Equal(0, (await service.ScanAsync(new DateOnly(2026, 3, 9))).Opened);

        // ...a new run after it does.
        string[] next = ["2026-03-10", "2026-03-11", "2026-03-12"];
        Lessons(db, s, next);
        Days(db, s, "Ali", next, "UUU");
        await db.SaveChangesAsync();
        Assert.Equal(1, (await service.ScanAsync(new DateOnly(2026, 3, 12))).Opened);
        var second = await db.AttendanceCases.AsNoTracking().SingleAsync(c => c.ClosedAt == null);
        Assert.Equal((3, new DateOnly(2026, 3, 12)), (second.Streak, second.LastAbsentOn));

        // Undoing the old "hal_boldi" would leave two open cases: refused.
        var resolvedAction = resolved.Actions.First();
        var reopen = await Assert.ThrowsAsync<AttendanceCaseException>(() =>
            service.DeleteActionAsync(resolvedAction.Id, staff.Id));
        Assert.Equal(409, reopen.Status);

        // Deleting a mistaken action puts the case back where it was.
        var unreachable = await service.AddActionAsync(second.Id,
            new AddAttendanceCaseActionRequest("boglanib_bolmadi", "Telefon o'chiq", null), actor);
        Assert.True(unreachable.Case.Overdue);
        Assert.Equal("unreachable", unreachable.Case.OverdueReason);
        var undone = await service.DeleteActionAsync(unreachable.Actions.Single().Id, staff.Id);
        Assert.Equal(AttendanceCaseStatus.ContactNeeded, undone.Case.Status);
        Assert.Null(undone.Case.LastActionAt);
        Assert.Empty(undone.Actions);
        Assert.NotNull((await db.AttendanceCaseActions.AsNoTracking().SingleAsync(a => a.CaseId == second.Id)).DeletedAt);
        Assert.True(await db.AuditLogs.AnyAsync(a => a.EntityType == AttendanceRedListService.AuditEntity && a.Action == "delete_action"));
    }

    // =====================================================================
    //  Telegram: teachers on a new case, management digest — never parents
    // =====================================================================

    [Fact]
    public async Task Yangi_holat_haqida_sinf_rahbari_va_yonalish_ustoziga_xabar_boshqa_sinf_ustoziga_yoq()
    {
        var db = await FreshAsync("redlist_notify");
        await EnableNoticesAsync(db);
        var s = await SchoolAsync(db, "9-A", "Ali", "Bek");
        var other = new SchoolClass { Name = "9-B", Grade = 9, Language = "uz" };
        db.Classes.Add(other);
        var homeroom = new Teacher { FullName = "Rahbar Ustoz" };
        var stranger = new Teacher { FullName = "Begona Ustoz" };
        var trackTeacher = new Teacher { FullName = "Guruh Ustoz" };
        db.Teachers.AddRange(homeroom, stranger, trackTeacher);
        var creator = new AppUser { FullName = "Admin", Role = Roles.Admin, Email = "admin.redlist" };
        db.Users.Add(creator);
        var track = new StudyGroup { Name = "Aniq fanlar", IsTrack = true, CreatedBy = creator.Id, CreatedAt = AppClock.NowInstant };
        db.StudyGroups.Add(track);
        await db.SaveChangesAsync();
        db.ClassHomeroomTeachers.AddRange(
            new ClassHomeroomTeacher { ClassId = s.ClassId, TeacherId = homeroom.Id, CreatedAt = AppClock.NowInstant },
            new ClassHomeroomTeacher { ClassId = other.Id, TeacherId = stranger.Id, CreatedAt = AppClock.NowInstant });
        db.StudyGroupTeachers.Add(new StudyGroupTeacher { GroupId = track.Id, TeacherId = trackTeacher.Id });
        db.StudyGroupMembers.Add(new StudyGroupMember
        {
            GroupId = track.Id, StudentId = s.Pupils["Ali"], JoinedOn = new DateOnly(2025, 9, 1),
            CreatedBy = creator.Id, CreatedAt = AppClock.NowInstant,
        });
        db.TelegramRegistrations.AddRange(
            new TelegramRegistration { StudentId = "", TeacherId = homeroom.Id, ChatId = 1001 },
            new TelegramRegistration { StudentId = "", TeacherId = stranger.Id, ChatId = 1002 },
            new TelegramRegistration { StudentId = "", TeacherId = trackTeacher.Id, ChatId = 1003 },
            // Ali's parent: must never hear from this module.
            new TelegramRegistration { StudentId = s.Pupils["Ali"], ChatId = 1009, ParentName = "Ota" });
        Lessons(db, s, Week);
        Days(db, s, "Ali", Week, "UUUU");
        await db.SaveChangesAsync();

        var handler = new RecordingHandler();
        var service = new AttendanceRedListService(db, ConfiguredTelegram(handler));
        var result = await service.ScanAsync(new DateOnly(2026, 3, 10));
        Assert.Equal((1, 2), (result.Opened, result.Notified));
        Assert.Equal([1001L, 1003L], handler.Sent.Select(m => m.Chat).Order());
        var text = handler.Sent[0].Text;
        Assert.Contains("Ali Test (9-A) — ketma-ket 4 kun sababsiz kelmadi", text);
        Assert.Contains("Qizil ro'yxat", text);
        Assert.NotNull((await db.AttendanceCases.AsNoTracking().SingleAsync()).NotifiedAt);

        // Nothing new — no new message.
        await service.ScanAsync(new DateOnly(2026, 3, 10));
        Assert.Equal(2, handler.Sent.Count);

        // Switched off: the case opens, nobody is messaged.
        var settings = await db.AttendanceRedListSettings.SingleAsync();
        settings.NotifyTeachers = false;
        await db.SaveChangesAsync();
        Days(db, s, "Bek", Week, "UUUU");
        await db.SaveChangesAsync();
        var quiet = await service.ScanAsync(new DateOnly(2026, 3, 10));
        Assert.Equal((1, 0), (quiet.Opened, quiet.Notified));
        Assert.Equal(2, handler.Sent.Count);
        Assert.DoesNotContain(handler.Sent, m => m.Chat == 1009);
    }

    [Fact]
    public async Task Rahbariyatga_kunlik_xabar_faqat_kalit_egasiga_kuniga_bir_marta()
    {
        var db = await FreshAsync("redlist_digest");
        await EnableNoticesAsync(db);
        var s = await SchoolAsync(db, "10-A", "Ali");
        Lessons(db, s, Week);
        Days(db, s, "Ali", Week, "UUUU");
        var deputy = new AppUser { FullName = "Zavuch", Role = Roles.Staff, Email = "zavuch.digest", Permissions = [AttendanceRedListService.PermissionKey] };
        var viewer = new AppUser { FullName = "Kuzatuvchi", Role = Roles.Staff, Email = "viewer.digest", Permissions = ["attendance"] };
        db.Users.AddRange(deputy, viewer);
        db.TelegramAccounts.AddRange(
            new TelegramAccount { TelegramUserId = 2001, UserId = deputy.Id, DisplayName = "Zavuch" },
            new TelegramAccount { TelegramUserId = 2002, UserId = viewer.Id, DisplayName = "Kuzatuvchi" });
        await db.SaveChangesAsync();

        var handler = new RecordingHandler();
        var service = new AttendanceRedListService(db, ConfiguredTelegram(handler));
        var tuesday = new DateOnly(2026, 3, 10);
        await service.ScanAsync(tuesday);
        var c = await db.AttendanceCases.AsNoTracking().SingleAsync();
        await service.AddActionAsync(c.Id, new AddAttendanceCaseActionRequest("boglanib_bolmadi", "Ikki raqam ham javob bermadi", null),
            new RedListActor(deputy.Id, deputy.FullName, AttendanceCaseActorType.Staff));

        Assert.Null(await service.RunDueAsync(new DateTime(2026, 3, 10, 12, 59, 0)));   // before the first slot
        Assert.NotNull(await service.RunDueAsync(new DateTime(2026, 3, 10, 13, 5, 0)));
        var digest = Assert.Single(handler.Sent);
        Assert.Equal(2001, digest.Chat);
        Assert.Contains("Ali Test (10-A) — bog'lanib bo'lmadi", digest.Text);

        Assert.Null(await service.RunDueAsync(new DateTime(2026, 3, 10, 13, 30, 0)));     // slot done
        Assert.NotNull(await service.RunDueAsync(new DateTime(2026, 3, 10, 18, 1, 0)));    // evening scan, no second digest
        Assert.Single(handler.Sent);
        Assert.Null(await service.RunDueAsync(new DateTime(2026, 3, 15, 13, 5, 0)));       // Sunday
    }

    // =====================================================================
    //  Pure rules
    // =====================================================================

    [Theory]
    [InlineData("2026-03-10 12:59", null, null)]
    [InlineData("2026-03-10 13:00", null, "2026-03-10 13:00")]
    [InlineData("2026-03-10 17:00", "2026-03-10 13:00", null)]
    [InlineData("2026-03-10 18:30", "2026-03-10 13:00", "2026-03-10 18:00")]
    [InlineData("2026-03-10 19:00", "2026-03-10 18:00", null)]
    [InlineData("2026-03-11 13:10", "2026-03-10 18:00", "2026-03-11 13:00")]
    public void Qachon_tekshiradi(string now, string? last, string? expected)
    {
        var at = DateTime.ParseExact(now, "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        Assert.Equal(expected, AttendanceRedListService.DueSlot(at, last));
    }

    [Fact]
    public void Eski_seriya_va_yopilgan_holatdan_oldingi_kunlar_hisoblanmaydi_sababli_kun_seriyani_uzadi()
    {
        var settings = new AttendanceRedListSettings();
        static (DateOnly, RedListDay) D(int day, RedListDay kind) => (new DateOnly(2026, 3, day), kind);
        var today = new DateOnly(2026, 3, 31);

        // A run of three that ended 20 days ago (outside the 14-day window): not opened again.
        var old = RedListCriteria.Evaluate([D(9, RedListDay.Missed), D(10, RedListDay.Missed), D(11, RedListDay.Missed),
            D(12, RedListDay.Came)], today, settings, since: null);
        Assert.Equal(3, old.LatestRun);
        Assert.False(old.Opens);

        // An excused day in between: two runs of two, not one of four.
        var excused = RedListCriteria.Evaluate([D(25, RedListDay.Missed), D(26, RedListDay.Missed), D(27, RedListDay.Excused),
            D(30, RedListDay.Missed), D(31, RedListDay.Missed)], today, settings, since: null);
        Assert.Equal(2, excused.LatestRun);
        Assert.False(excused.ConsecutiveMet);

        // Days up to the resolved case's last absence are not read.
        var days = new[] { D(26, RedListDay.Missed), D(27, RedListDay.Missed), D(30, RedListDay.Missed), D(31, RedListDay.Missed) };
        Assert.True(RedListCriteria.Evaluate(days, today, settings, since: null).ConsecutiveMet);
        var after = RedListCriteria.Evaluate(days, today, settings, since: new DateOnly(2026, 3, 27));
        Assert.Equal((2, 2), (after.LatestRun, after.WindowCount));
        Assert.False(after.Opens);
    }

    [Fact]
    public void Muddati_otgan_boglanib_bolmadi_qongiroq_kuni_otgan_yoki_ikki_kun_amalsiz()
    {
        var today = new DateOnly(2026, 3, 12);
        AttendanceCase Case(string status, DateOnly opened, DateOnly? callback = null, DateTimeOffset? lastAction = null) =>
            new() { Status = status, OpenedOn = opened, CallbackOn = callback, LastActionAt = lastAction };

        Assert.Equal((false, (string?)null), Due(Case("aloqa_kerak", today.AddDays(-1))));
        Assert.Equal((true, "no_action"), Due(Case("aloqa_kerak", today.AddDays(-2))));
        Assert.Equal((true, "unreachable"), Due(Case("boglanib_bolmadi", today)));
        // A call-back agreed for a later day is waiting by agreement, however old the last action.
        Assert.Equal((false, (string?)null), Due(Case("qayta_qongiroq", today.AddDays(-9), today.AddDays(1),
            new DateTimeOffset(2026, 3, 3, 8, 0, 0, TimeSpan.Zero))));
        Assert.Equal((true, "callback_due"), Due(Case("qayta_qongiroq", today.AddDays(-9), today.AddDays(-1))));

        (bool, string?) Due(AttendanceCase c)
        {
            var (overdue, reason, _) = AttendanceRedListService.OverdueOf(c, today, escalateAfterDays: 2);
            return (overdue, reason);
        }
    }

    // =====================================================================
    //  API: teacher ownership and admin permissions (shared database)
    // =====================================================================

    [Fact]
    public async Task Ustoz_faqat_oz_sinfi_holatlarini_koradi_va_ishlaydi()
    {
        var w = await SeedApiAsync();

        var list = (await w.Homeroom.GetFromJsonAsync<TeacherRedListDto>(TeacherUrl))!;
        Assert.Equal([w.ClassA], list.Groups);
        var mine = Assert.Single(list.Cases);
        Assert.Equal(w.AliCase, mine.Case.Id);
        Assert.Contains(mine.Case.Contacts, c => c.Phone == "+998901112233");
        Assert.Equal((3, 14, 5), (list.ConsecutiveDays, list.WindowDays, list.WindowUnexcused));

        var callbackOn = AppClock.Today.AddDays(2).ToString("yyyy-MM-dd");
        var ok = await w.Homeroom.PostAsJsonAsync($"{TeacherUrl}/{w.AliCase}/actions",
            new { status = "qayta_qongiroq", comment = "Otasi bilan gaplashildi, ertaga qayta", callbackOn });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var detail = (await ok.Content.ReadFromJsonAsync<AttendanceCaseDetailDto>())!;
        Assert.Equal((AttendanceCaseStatus.CallBack, AppClock.Today.AddDays(2)), (detail.Case.Status, detail.Case.CallbackOn));
        Assert.Equal(AttendanceCaseActorType.Teacher, detail.Actions.Single().ActorType);

        // Another class's case — as if it did not exist.
        Assert.Equal(HttpStatusCode.NotFound, (await w.Homeroom.PostAsJsonAsync($"{TeacherUrl}/{w.ValiCase}/actions",
            new { status = "hal_boldi", comment = "x" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await w.Homeroom.PostAsJsonAsync($"{TeacherUrl}/{w.AliCase}/actions",
            new { status = "hal_boldi", comment = "" })).StatusCode);

        // A teacher who leads no class sees nothing.
        var (_, plain) = await TeacherAsync();
        var empty = (await plain.GetFromJsonAsync<TeacherRedListDto>(TeacherUrl))!;
        Assert.Empty(empty.Groups);
        Assert.Empty(empty.Cases);
        Assert.Equal(HttpStatusCode.NotFound, (await plain.PostAsJsonAsync($"{TeacherUrl}/{w.AliCase}/actions",
            new { status = "hal_boldi", comment = "x" })).StatusCode);

        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync(TeacherUrl)).StatusCode);
        using var anonymous = fixture.Api.AnonymousClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(TeacherUrl)).StatusCode);
    }

    [Fact]
    public async Task Admin_tomoni_korish_xodimga_ochiq_ishlash_faqat_attendanceCases_kaliti_bilan()
    {
        var w = await SeedApiAsync();

        using var anonymous = fixture.Api.AnonymousClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(AdminUrl)).StatusCode);
        using var teacher = await fixture.Api.ClientAsAsync(Roles.Teacher);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync(AdminUrl)).StatusCode);

        // Attendance viewer: reads everything, writes nothing.
        using var viewer = await fixture.Api.ClientAsAsync(Roles.Staff, "attendance");
        var list = (await viewer.GetFromJsonAsync<AttendanceCaseListDto>($"{AdminUrl}?className={Uri.EscapeDataString(w.ClassA)}"))!;
        Assert.Equal(w.AliCase, Assert.Single(list.Rows).Id);
        Assert.True(list.Counts["open"] >= 2);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"{AdminUrl}/{w.AliCase}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"{AdminUrl}/open")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await viewer.GetAsync($"{AdminUrl}/settings")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsJsonAsync($"{AdminUrl}/{w.ValiCase}/actions",
            new { status = "boglanib_bolmadi", comment = "Javob yo'q" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PostAsync($"{AdminUrl}/scan", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PutAsJsonAsync($"{AdminUrl}/settings", Defaults())).StatusCode);

        // The queue key: works the case and undoes a mistake.
        using var deputy = await fixture.Api.ClientAsAsync(Roles.Staff, AttendanceRedListService.PermissionKey);
        var added = await deputy.PostAsJsonAsync($"{AdminUrl}/{w.ValiCase}/actions",
            new { status = "boglanib_bolmadi", comment = "Ikki raqam ham javob bermadi" });
        Assert.Equal(HttpStatusCode.OK, added.StatusCode);
        var detail = (await added.Content.ReadFromJsonAsync<AttendanceCaseDetailDto>())!;
        Assert.Equal((AttendanceCaseStatus.Unreachable, true), (detail.Case.Status, detail.Case.Overdue));
        Assert.Equal(AttendanceCaseActorType.Staff, detail.Actions.Single().ActorType);

        var overdue = (await viewer.GetFromJsonAsync<AttendanceCaseListDto>($"{AdminUrl}?overdue=true&status=boglanib_bolmadi"))!;
        Assert.Contains(overdue.Rows, r => r.Id == w.ValiCase);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await viewer.DeleteAsync($"{AdminUrl}/actions/{detail.Actions.Single().Id}")).StatusCode);
        var undone = await deputy.DeleteAsync($"{AdminUrl}/actions/{detail.Actions.Single().Id}");
        Assert.Equal(HttpStatusCode.OK, undone.StatusCode);
        Assert.Equal(AttendanceCaseStatus.ContactNeeded,
            (await undone.Content.ReadFromJsonAsync<AttendanceCaseDetailDto>())!.Case.Status);

        // Settings: validated, audited, then put back (shared database).
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        try
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"{AdminUrl}/settings",
                Defaults() with { WindowDays = 4, WindowUnexcused = 5 })).StatusCode);
            var saved = await admin.PutAsJsonAsync($"{AdminUrl}/settings", Defaults() with { ConsecutiveDays = 4 });
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            Assert.Equal(4, (await saved.Content.ReadFromJsonAsync<AttendanceRedListSettingsDto>())!.ConsecutiveDays);
        }
        finally
        {
            Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync($"{AdminUrl}/settings", Defaults())).StatusCode);
        }
    }

    // =====================================================================
    //  Helpers
    // =====================================================================

    /// <summary>The Telegram notices ship off; the notice tests switch them on.</summary>
    private static async Task EnableNoticesAsync(AppDbContext db)
    {
        var settings = await db.AttendanceRedListSettings.SingleAsync();
        settings.NotifyTeachers = true;
        settings.NotifyManagement = true;
        await db.SaveChangesAsync();
    }

    private static SaveAttendanceRedListSettingsRequest Defaults() => new(3, 14, 5, false, 2, false, false);

    private sealed record ApiWorld(HttpClient Homeroom, string ClassA, Guid AliCase, Guid ValiCase);

    private async Task<ApiWorld> SeedApiAsync()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var (teacherId, homeroom) = await TeacherAsync();
        var a = new SchoolClass { Name = $"RL{tag}-A", Grade = 5, Language = "uz" };
        var b = new SchoolClass { Name = $"RL{tag}-B", Grade = 5, Language = "uz" };
        var ali = Pupil($"Ali {tag}", a.Name);
        var vali = Pupil($"Vali {tag}", b.Name);
        var aliCase = new AttendanceCase
        {
            StudentId = ali.Id, OpenedOn = AppClock.Today, Streak = 3, WindowCount = 3, LastAbsentOn = AppClock.Today,
            AbsentDates = [AppClock.Today], CreatedAt = AppClock.NowInstant, UpdatedAt = AppClock.NowInstant,
        };
        var valiCase = new AttendanceCase
        {
            StudentId = vali.Id, OpenedOn = AppClock.Today, Streak = 3, WindowCount = 3, LastAbsentOn = AppClock.Today,
            CreatedAt = AppClock.NowInstant, UpdatedAt = AppClock.NowInstant,
        };
        await fixture.Api.WithDbAsync(async db =>
        {
            db.Classes.AddRange(a, b);
            db.Students.AddRange(ali, vali);
            await db.SaveChangesAsync();
            db.ClassHomeroomTeachers.Add(new ClassHomeroomTeacher { ClassId = a.Id, TeacherId = teacherId, CreatedAt = AppClock.NowInstant });
            db.AttendanceCases.AddRange(aliCase, valiCase);
            await db.SaveChangesAsync();
        });
        return new ApiWorld(homeroom, a.Name, aliCase.Id, valiCase.Id);
    }

    private async Task<(string TeacherId, HttpClient Client)> TeacherAsync()
    {
        var (user, _) = await fixture.Api.SeedUserAsync(Roles.Teacher);
        var teacherId = "";
        await fixture.Api.WithDbAsync(async db =>
            teacherId = (await db.Teachers.SingleAsync(t => t.UserId == user.Id)).Id);
        return (teacherId, fixture.Api.ClientWithToken(
            fixture.Api.TokenFor(Roles.Teacher, user.Id, user.FullName, user.Email)));
    }

    private sealed record School(
        string ClassId, string ClassName, string Math, string Physics,
        string Unexcused, string Excused, string Late, Dictionary<string, string> Pupils);

    private async Task<AppDbContext> FreshAsync(string name)
    {
        var database = await fixture.Postgres.CreateDatabaseAsync(name);
        _connectionStrings.Add(database.OwnerConnectionString);
        return PostgresFixture.NewContext(database.OwnerConnectionString);
    }

    private static async Task<School> SchoolAsync(AppDbContext db, string className, params string[] pupils)
    {
        var cls = new SchoolClass { Name = className, Grade = 5, Language = "uz" };
        var math = new Subject { Name = "Matematika" };
        var physics = new Subject { Name = "Fizika" };
        var unexcused = new AbsenceReason { Name = "Sababsiz", Short = "S" };
        var excused = new AbsenceReason { Name = "Kasal", Short = "K" };
        var late = new AbsenceReason { Name = "Kech keldi", Short = "Kch", IsLate = true };
        db.Classes.Add(cls);
        db.Subjects.AddRange(math, physics);
        db.AbsenceReasons.AddRange(unexcused, excused, late);
        var students = pupils.ToDictionary(p => p, p => Pupil($"{p} Test", className));
        db.Students.AddRange(students.Values);
        await db.SaveChangesAsync();
        return new School(cls.Id, className, math.Id, physics.Id, unexcused.Id, excused.Id, late.Id,
            students.ToDictionary(kv => kv.Key, kv => kv.Value.Id));
    }

    /// <summary>Two conducted lessons a day; the nurse marked them unless the date is in <paramref name="unmarked"/>.</summary>
    private static void Lessons(AppDbContext db, School s, IEnumerable<string> dates, params string[] unmarked)
    {
        foreach (var date in dates)
            foreach (var (subject, period) in new[] { (s.Math, 1), (s.Physics, 2) })
            {
                db.LessonNotes.Add(new LessonNote
                {
                    ClassId = s.ClassId, SubjectId = subject, Quarter = 3, Date = date, Period = period,
                    Topic = "Mavzu", Conducted = true,
                });
                if (unmarked.Contains(date)) continue;
                db.DailyAttendanceMarks.Add(new DailyAttendanceMark
                {
                    ClassId = s.ClassId, SubjectId = subject, Date = date, Period = period,
                    MarkedBy = "nurse", MarkedAt = AppClock.NowInstant,
                });
            }
    }

    /// <summary>
    /// One character per date: U — unexcused all day, E — excused all day, P — unexcused in the first
    /// lesson only, L — late to the first lesson, "." — nothing written.
    /// </summary>
    private static void Days(AppDbContext db, School s, string pupil, IReadOnlyList<string> dates, string pattern)
    {
        Assert.Equal(dates.Count, pattern.Length);
        for (var i = 0; i < dates.Count; i++)
        {
            (string? First, string? Second) reasons = pattern[i] switch
            {
                'U' => (s.Unexcused, s.Unexcused),
                'E' => (s.Excused, s.Excused),
                'P' => (s.Unexcused, null),
                'L' => (s.Late, null),
                _ => (null, null),
            };
            if (reasons.First is { } first) Mark(s.Math, 1, first);
            if (reasons.Second is { } second) Mark(s.Physics, 2, second);

            void Mark(string subject, int period, string reason) => db.JournalEntries.Add(new JournalEntry
            {
                ClassId = s.ClassId, SubjectId = subject, Quarter = 3, StudentId = s.Pupils[pupil],
                Date = dates[i], Period = period, ReasonId = reason,
            });
        }
    }

    private static Student Pupil(string fullName, string className)
    {
        var p = GeneralSettingsFlagsTests.NewStudent(fullName, className, "+998901112233");
        p.Id = Guid.NewGuid().ToString();
        return p;
    }

    private AttendanceRedListService Service(AppDbContext db) =>
        new(db, ConfiguredTelegram(new RecordingHandler()));

    private TelegramService ConfiguredTelegram(HttpMessageHandler handler)
    {
        var service = new TelegramService(
            fixture.Api.Services.GetRequiredService<IConfiguration>(),
            new Factory(handler),
            fixture.Api.Services.GetRequiredService<ILogger<TelegramService>>());
        service.Set("test-token", "test_bot");
        return service;
    }

    /// <summary>Answers every send with 200 and keeps chat and text — nothing leaves the machine.</summary>
    private sealed class RecordingHandler : HttpMessageHandler
    {
        public List<(long Chat, string Text)> Sent { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            using var doc = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
            Sent.Add((doc.RootElement.GetProperty("chat_id").GetInt64(), doc.RootElement.GetProperty("text").GetString()!));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"ok\":true}") };
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }
}
