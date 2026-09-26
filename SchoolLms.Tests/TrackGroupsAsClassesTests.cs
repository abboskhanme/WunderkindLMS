using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Auth;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Server.Controllers;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// Yo'nalish guruhlari sinf kabi — docs/modules/track-groups-as-classes.md (2026-09-26),
/// va "Davomat belgilash" statistikaga tushishi (HeldLessons qoidasi).
///
/// <para>
/// <b>Nega ko'pchilik testda o'z bazasi.</b> Tanlagich ro'yxati, kunlik davomat, analitika va
/// bosh sahifa BUTUN maktabni yig'adi; o'chirgich (<c>group_lessons_enabled</c>) ham bitta
/// qator. Umumiy bazada bu testlar qo'shnilarining raqamini o'zgartirardi. Guruh formasi
/// (API) testlari esa umumiy bazada — noyob nomlar bilan.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class TrackGroupsAsClassesTests(ApiFixture fixture) : IAsyncLifetime
{
    private const int Quarter = 1;

    /// <summary>Chorakning 1-haftasi dushanbasi.</summary>
    private const string Monday = "2026-08-31";

    private const int ClassPeriod = 1;
    private const int TrackPeriod = 2;
    private const int OrdinaryPeriod = 3;

    private const string Groups = "/api/admin/study-groups";

    private readonly List<string> _connectionStrings = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        foreach (var cs in _connectionStrings) NpgsqlConnection.ClearPool(new NpgsqlConnection(cs));
        return Task.CompletedTask;
    }

    /* =====================================================================
     *  1. Almashtirish qoidasi va tartib
     * ================================================================== */

    [Fact]
    public async Task Tanlagich_yonalish_boqadigan_sinflarni_yashiradi_va_tartiblaydi()
    {
        await using var db = await NewDbAsync("picker");
        var w = await SeedAsync(db);

        var off = await LessonRoster.PickerOwnersAsync(db);
        Assert.Equal([w.Class5A, w.Class7A, w.TrackId], off.Select(o => o.Id));
        Assert.True(off[^1].IsTrack);
        Assert.Null(off[^1].SubjectId);
        Assert.Equal(new[] { w.Class9A, w.Class9B }.Order(), off[^1].ClassIds.Order());

        // Oddiy guruh — faqat o'chirgich yoqilganda, yo'nalishlardan KEYIN.
        await SetSwitchAsync(db, true);
        var on = await LessonRoster.PickerOwnersAsync(db);
        Assert.Equal([w.Class5A, w.Class7A, w.TrackId, w.OrdinaryId], on.Select(o => o.Id));

        // Hidden-class rule: faqat FAOL yo'nalish guruhi yashiradi.
        Assert.Equal(new[] { w.Class9A, w.Class9B }.Order(), (await LessonRoster.TrackFedClassIdsAsync(db)).Order());
    }

    /* =====================================================================
     *  2. O'chirgichga bog'liq emas
     * ================================================================== */

    [Fact]
    public async Task Yonalish_darslari_ochirgich_ochiq_bolsa_ham_tirik_oddiy_guruh_esa_yoq()
    {
        await using var db = await NewDbAsync("live");
        var w = await SeedAsync(db);

        Assert.False(await LessonRoster.GroupLessonsEnabledAsync(db));

        var trackColumns = await JournalService.ComputeColumnsAsync(db, w.TrackId, w.Physics, Quarter);
        Assert.Equal(Monday, Assert.Single(trackColumns).Date);
        Assert.Empty(await JournalService.ComputeColumnsAsync(db, w.OrdinaryId, w.English, Quarter));

        var live = await LessonRoster.LiveOwnersAsync(db);
        Assert.True(live[w.TrackId].IsTrack);
        Assert.False(live.ContainsKey(w.OrdinaryId));

        // O'quvchi jadvali (portal / Mini App): yo'nalish darsi bor, oddiy guruh darsi yo'q.
        var p9a = await db.Students.FirstAsync(s => s.Id == w.P9A);
        var lessons = await PupilTimetable.ForWeekAsync(db, p9a, Quarter, 1);
        var lesson = Assert.Single(lessons);
        Assert.Equal(w.TrackId, lesson.Owner.Id);
        Assert.Equal(w.Physics, lesson.SubjectId);

        var p5 = await db.Students.FirstAsync(s => s.Id == w.P5);
        Assert.DoesNotContain(await PupilTimetable.ForWeekAsync(db, p5, Quarter, 1), l => l.Owner.IsGroup);

        // Haftaga biriktirish: yo'nalish — mumkin, oddiy guruh — 409 (o'chirgich).
        var trackOwner = (await LessonRoster.OwnerAsync(db, w.TrackId))!;
        var ordinaryOwner = (await LessonRoster.OwnerAsync(db, w.OrdinaryId))!;
        Assert.True(await LessonRoster.LessonsLiveAsync(db, trackOwner));
        Assert.False(await LessonRoster.LessonsLiveAsync(db, ordinaryOwner));
    }

    [Fact]
    public async Task Sinf_va_yonalish_orasidagi_oquvchi_toqnashuvi_ziddiyat()
    {
        await using var db = await NewDbAsync("clash");
        var w = await SeedAsync(db);

        // 9-A ning eski sinf jadvali: dushanba, AYNAN yo'nalish darsi soatida.
        var tpl = new ScheduleTemplate
        {
            ClassId = w.Class9A, Name = "9-A eski",
            Lessons = [new ScheduleLesson { Day = 0, Period = TrackPeriod, SubjectId = w.Math, TeacherId = w.TeacherId }],
        };
        db.ScheduleTemplates.Add(tpl);
        db.WeekAssignments.Add(new WeekAssignment { ClassId = w.Class9A, Quarter = Quarter, Week = 1, TemplateId = tpl.Id });
        await db.SaveChangesAsync();

        var track = (await LessonRoster.OwnerAsync(db, w.TrackId))!;
        var clash = await ScheduleConflicts.ForTemplateAsync(db, track, w.TrackTemplateId, [(0, TrackPeriod, 0)]);
        var c = Assert.Single(clash);
        Assert.Equal(w.Class9A, c.Other.Id);
        Assert.Contains(c.StudentNames, n => n.StartsWith("Nodir"));
    }

    /* =====================================================================
     *  3. Jurnal
     * ================================================================== */

    [Fact]
    public async Task Yonalish_darsiga_jurnal_yoziladi_ochirgich_ochiq_bolsa_ham()
    {
        await using var db = await NewDbAsync("journal");
        var w = await SeedAsync(db);

        Assert.Null(await JournalService.SetEntryAsync(db, new SetJournalEntryRequest(
            w.TrackId, w.Physics, Quarter, w.P9A, Monday, TrackPeriod, 5, null)));

        var entry = Assert.Single(await db.JournalEntries.Where(e => e.ClassId == w.TrackId).ToListAsync());
        Assert.Equal(LessonOwnerKind.Group, entry.OwnerKind);
        Assert.Equal(0, entry.SubGroup);
        Assert.Equal(5, entry.Grade);

        // Oddiy guruh — o'chirgich o'chiq, yozilmaydi.
        Assert.Equal(JournalService.GroupLessonsOffMessage, await JournalService.SetEntryAsync(db,
            new SetJournalEntryRequest(w.OrdinaryId, w.English, Quarter, w.P5, Monday, OrdinaryPeriod, 5, null)));

        // Ro'yxat — yo'nalish a'zolari.
        var roster = await LessonRoster.ForLessonAsync(db, w.TrackId);
        Assert.Equal(new[] { w.P9A, w.P9B }.Order(), roster.Select(s => s.Id).Order());
    }

    /* =====================================================================
     *  4. Davomat belgilash (DailyMarkingPage)
     * ================================================================== */

    [Fact]
    public async Task Kunlik_davomat_royxati_tartibi_va_yonalish_darsini_belgilash()
    {
        await using var db = await NewDbAsync("daily");
        var w = await SeedAsync(db);
        var service = new DailyAttendanceService(db);

        var overview = await service.OverviewAsync(Monday);
        Assert.Equal([w.Class5A, w.Class7A, w.TrackId], overview.Classes.Select(c => c.ClassId));
        var trackRow = overview.Classes[^1];
        Assert.Equal(LessonOwnerKind.Group, trackRow.OwnerKind);
        Assert.True(trackRow.IsTrack);
        Assert.Equal(2, trackRow.StudentCount);
        Assert.Equal(1, trackRow.LessonCount);

        var day = (await service.ClassDayAsync(w.TrackId, Monday))!;
        Assert.Equal(LessonOwnerKind.Group, day.OwnerKind);
        Assert.Equal(new[] { w.P9A, w.P9B }.Order(), day.Students.Select(s => s.StudentId).Order());
        var lesson = Assert.Single(day.Lessons);
        Assert.Equal(w.Physics, lesson.SubjectId);
        Assert.Equal(TrackPeriod, lesson.Period);

        Assert.Null(await service.SaveAsync(new SaveDailyAttendanceRequest(
            w.TrackId, Monday, w.Physics, TrackPeriod, [new(w.P9A, w.AbsentReasonId)]), w.UserId));

        var entry = Assert.Single(await db.JournalEntries.Where(e => e.ClassId == w.TrackId).ToListAsync());
        Assert.Equal(LessonOwnerKind.Group, entry.OwnerKind);
        Assert.Equal(w.P9A, entry.StudentId);
        Assert.Equal(0, entry.SubGroup);
        // Davomat darsni "o'tildi" qilmaydi — buni o'qituvchi aytadi.
        Assert.Empty(await db.LessonNotes.ToListAsync());

        var after = await service.OverviewAsync(Monday);
        Assert.Equal(1, after.Classes.Single(c => c.ClassId == w.TrackId).MarkedLessons);

        // Oddiy guruh kunlik davomatga kirmaydi.
        Assert.Null(await service.ClassDayAsync(w.OrdinaryId, Monday));
    }

    /// <summary>
    /// O'tgan sanaga davomat (mijoz, 2026-09-26): ro'yxat O'SHA KUNDAGI a'zolardan — keyinroq
    /// qo'shilgan o'quvchi oldingi kunda chiqmaydi, keyingi kunda chiqadi. Hisobotlar ham a'zolik
    /// sanasiga qaraydi, shuning uchun ekran va hisobot bir xil ro'yxatni ko'radi.
    /// </summary>
    [Fact]
    public async Task Otgan_sana_royxati_osha_kundagi_azolardan()
    {
        await using var db = await NewDbAsync("pastdate");
        var w = await SeedAsync(db);
        var late = NewStudent("Kechroq Qo'shilgan", "9-A");
        db.Students.Add(late);
        db.StudyGroupMembers.Add(Member(Guid.Parse(w.TrackId), null, late.Id, new DateOnly(2026, 9, 7), w.UserId));
        await db.SaveChangesAsync();
        var service = new DailyAttendanceService(db);

        var before = (await service.ClassDayAsync(w.TrackId, Monday))!;
        Assert.DoesNotContain(before.Students, s => s.StudentId == late.Id);
        Assert.Equal(2, (await service.OverviewAsync(Monday)).Classes.Single(c => c.ClassId == w.TrackId).StudentCount);

        var after = (await service.ClassDayAsync(w.TrackId, "2026-09-07"))!;
        Assert.Contains(after.Students, s => s.StudentId == late.Id);
    }

    /* =====================================================================
     *  5. HeldLessons — belgilangan dars statistikada "bo'lgan"
     * ================================================================== */

    [Fact]
    public async Task Davomat_belgilangan_dars_statistikaga_tushadi_oqituvchi_hisobotiga_emas()
    {
        await using var db = await NewDbAsync("held");
        var w = await SeedAsync(db);
        var service = new DailyAttendanceService(db);

        // Hech qanday dars izohi yo'q: yo'nalishda P9A kelmadi, 5-A da hamma keldi.
        Assert.Null(await service.SaveAsync(new SaveDailyAttendanceRequest(
            w.TrackId, Monday, w.Physics, TrackPeriod, [new(w.P9A, w.AbsentReasonId)]), w.UserId));
        Assert.Null(await service.SaveAsync(new SaveDailyAttendanceRequest(
            w.Class5A, Monday, w.Math, ClassPeriod, []), w.UserId));
        Assert.Empty(await db.LessonNotes.ToListAsync());

        var held = await HeldLessons.ListAsync(db);
        Assert.Equal(2, held.Count);
        Assert.Contains(held, h => h.ClassId == w.TrackId && h.OwnerKind == LessonOwnerKind.Group);

        // Davomat analitikasi: yo'nalish qatori (9-A/9-B o'rnida) va 5-A.
        var analytics = await AttendanceAnalytics.BuildAsync(db, null, Monday, Monday, Monday);
        Assert.Equal(2, analytics.Total.Lessons);
        Assert.Equal(3, analytics.Total.Opportunities);       // 2 yo'nalish a'zosi + 1 (5-A)
        Assert.Equal(1, analytics.Total.Absent);
        Assert.Equal(0, analytics.Total.Unchecked);           // belgilangan darsda izohsiz = keldi
        Assert.DoesNotContain(analytics.Classes, c => c.ClassId == w.Class9A);
        var trackRow = analytics.Classes.Single(c => c.ClassId == w.TrackId);
        Assert.Equal(LessonOwnerKind.Group, trackRow.OwnerKind);
        Assert.Equal(2, trackRow.Students);
        Assert.Equal(1, trackRow.Tally.Absent);
        Assert.Equal(1, analytics.Classes.Single(c => c.ClassId == w.Class5A).Tally.Present);

        // Bitta yo'nalish tanlanganda — faqat uning a'zolari.
        var onlyTrack = await AttendanceAnalytics.BuildAsync(db, w.TrackId, Monday, Monday, Monday);
        Assert.Equal(2, onlyTrack.Total.Opportunities);

        // O'quvchi profili (portal/Mini App ham shu builder'dan).
        var p9a = await db.Students.FirstAsync(s => s.Id == w.P9A);
        var profile = await StudentProfileBuilder.BuildAsync(db, p9a);
        Assert.Equal(1, profile.Conducted);
        Assert.Equal(0, profile.Attended);
        var p9b = await db.Students.FirstAsync(s => s.Id == w.P9B);
        Assert.Equal(100, (await StudentProfileBuilder.BuildAsync(db, p9b)).AttendancePct);

        // Bosh sahifa davomati: 9-A (P9A yo'q) — 0%, 9-B — 100%, 5-A — 100%.
        var dashboard = (await new DashboardController(db).Get()).Value!;
        Assert.Equal(0, dashboard.ClassPerformance.Single(c => c.ClassId == w.Class9A).AttendanceRate);
        Assert.Equal(100, dashboard.ClassPerformance.Single(c => c.ClassId == w.Class9B).AttendanceRate);
        Assert.Equal(100, dashboard.ClassPerformance.Single(c => c.ClassId == w.Class5A).AttendanceRate);
        Assert.Equal(67, dashboard.Stats.AttendanceRate);

        // O'qituvchi ishi/maoshi — faqat LessonNote.Conducted: o'tilgan dars 0 bo'lib qoladi.
        var teacherRow = (await TeacherActivityReport.BuildOverviewAsync(db, Quarter))
            .Single(r => r.TeacherId == w.TeacherId);
        Assert.Equal(0, teacherRow.Conducted);

        // Intizom hisoboti ham shu qoidadan.
        var discipline = await AttendanceDisciplineReport.BuildAsync(db, Monday, Monday);
        Assert.Equal(1, discipline.Totals.Absences);
    }

    /* =====================================================================
     *  6. Guruh formasi — fan ixtiyoriy, bitta yo'nalish (API, umumiy baza)
     * ================================================================== */

    [Fact]
    public async Task Yonalish_guruhi_fansiz_yaratiladi_va_tahrirlanadi()
    {
        var s = await SeedSharedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var res = await admin.PostAsJsonAsync(Groups, new
        {
            name = $"Aniq {s.Tag}", subjectId = (string?)null, classIds = new[] { s.ClassId },
            teacherIds = new[] { s.TeacherId }, isTrack = true, studentIds = new[] { s.StudentA },
        });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var dto = (await res.Content.ReadFromJsonAsync<StudyGroupDetailDto>())!;
        Assert.True(dto.IsTrack);
        Assert.Null(dto.SubjectId);
        Assert.Single(dto.Members);

        // Fan yuborilsa ham yo'nalish fansiz saqlanadi.
        var upd = await admin.PutAsJsonAsync($"{Groups}/{dto.Id}", new
        {
            name = $"Aniq fanlar {s.Tag}", subjectId = s.SubjectId, classIds = new[] { s.ClassId },
            teacherIds = new[] { s.TeacherId }, isTrack = true,
        });
        Assert.Equal(HttpStatusCode.OK, upd.StatusCode);
        var updated = (await upd.Content.ReadFromJsonAsync<StudyGroupDetailDto>())!;
        Assert.Null(updated.SubjectId);
        Assert.Equal($"Aniq fanlar {s.Tag}", updated.Name);
        Assert.Single(updated.Members); // ro'yxat yuborilmadi — tegilmadi

        // Oddiy guruh — fan hamon majburiy.
        var ordinary = await admin.PostAsJsonAsync(Groups, new
        {
            name = $"Oddiy {s.Tag}", subjectId = (string?)null, classIds = new[] { s.ClassId },
            teacherIds = new[] { s.TeacherId },
        });
        Assert.Equal(HttpStatusCode.BadRequest, ordinary.StatusCode);
        Assert.Contains(StudyGroupService.SubjectRequiredMessage, await ordinary.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Oquvchi_faqat_bitta_yonalish_guruhida()
    {
        var s = await SeedSharedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var first = await CreateTrackAsync(admin, s, $"Filologiya {s.Tag}", [s.StudentA]);
        var second = await CreateTrackAsync(admin, s, $"Tabiiy {s.Tag}", [s.StudentB]);

        var add = await admin.PostAsJsonAsync($"{Groups}/{second}/members", new { studentIds = new[] { s.StudentA } });
        Assert.Equal(HttpStatusCode.BadRequest, add.StatusCode);
        Assert.Contains("yo'nalish", await add.Content.ReadAsStringAsync());

        // Nomzodlar oynasi: A — birinchi yo'nalishda band.
        var candidates = await admin.GetFromJsonAsync<List<GroupCandidateDto>>(
            $"{Groups}/candidates?classIds={s.ClassId}&track=true&excludeGroupId={second}");
        Assert.Equal(first, candidates!.Single(c => c.StudentId == s.StudentA).CurrentGroupId);

        // Yo'nalish → yo'nalish o'tkazish mumkin; natijada bitta faol a'zolik.
        var memberId = Guid.Empty;
        await fixture.Api.WithDbAsync(async db => memberId = await db.StudyGroupMembers
            .Where(m => m.GroupId == first && m.StudentId == s.StudentA && m.LeftOn == null)
            .Select(m => m.Id).SingleAsync());
        var transfer = await admin.PostAsJsonAsync($"{Groups}/members/{memberId}/transfer", new { toGroupId = second });
        Assert.Equal(HttpStatusCode.NoContent, transfer.StatusCode);
        List<StudyGroupMember> active = [];
        await fixture.Api.WithDbAsync(async db => active = await db.StudyGroupMembers
            .Where(m => m.StudentId == s.StudentA && m.LeftOn == null).ToListAsync());
        Assert.Equal(second, Assert.Single(active).GroupId);
        Assert.Null(active[0].SubjectId);
    }

    [Fact]
    public async Task Ega_royxati_endpointi_tartib_bilan_qaytadi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var res = await admin.GetAsync("/api/admin/schedule/owners");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var owners = (await res.Content.ReadFromJsonAsync<List<LessonOwnerItem>>())!;
        // Sinflar har doim guruhlardan OLDIN.
        var firstGroup = owners.FindIndex(o => o.Kind == LessonOwnerKind.Group);
        if (firstGroup >= 0) Assert.DoesNotContain(owners.Skip(firstGroup), o => o.Kind == LessonOwnerKind.Class);

        using var teacher = await fixture.Api.ClientAsAsync(Roles.Teacher);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync("/api/admin/schedule/owners")).StatusCode);
    }

    /* =====================================================================
     *  Yordamchilar
     * ================================================================== */

    private sealed record World(
        string Class5A, string Class7A, string Class9A, string Class9B,
        string TrackId, string OrdinaryId, string TrackTemplateId,
        string Math, string Physics, string English,
        string P5, string P9A, string P9B,
        string TeacherId, string AbsentReasonId, string UserId);

    private async Task<AppDbContext> NewDbAsync(string prefix)
    {
        var database = await fixture.Postgres.CreateDatabaseAsync("tgc_" + prefix);
        _connectionStrings.Add(database.OwnerConnectionString);
        return PostgresFixture.NewContext(database.OwnerConnectionString);
    }

    private static async Task SetSwitchAsync(AppDbContext db, bool enabled)
    {
        var meta = await db.SchoolMeta.FirstAsync();
        meta.GroupLessonsEnabled = enabled;
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// 5-A (P5), 7-A (bo'sh), 9-A (P9A), 9-B (P9B); yo'nalish "Aniq fanlar" (9-A + 9-B,
    /// fansiz, a'zolar P9A va P9B, dushanba 2-dars fizika); oddiy guruh "Ingliz" (5-A, P5,
    /// dushanba 3-dars). 5-A sinf jadvali: dushanba 1-dars matematika. Hammasi 1-haftaga
    /// biriktirilgan; o'chirgich O'CHIQ.
    /// </summary>
    private static async Task<World> SeedAsync(AppDbContext db)
    {
        db.SchoolMeta.Add(new SchoolMeta { CurrentYear = "2026/2027", Name = "Test maktab" });
        db.Quarters.Add(new QuarterPeriod
        {
            Quarter = Quarter, StartDate = "2026-08-31", EndDate = "2026-12-31", GradesOpen = true,
        });

        var c5 = new SchoolClass { Name = "5-A", Grade = 5 };
        var c7 = new SchoolClass { Name = "7-A", Grade = 7 };
        var c9a = new SchoolClass { Name = "9-A", Grade = 9 };
        var c9b = new SchoolClass { Name = "9-B", Grade = 9 };
        db.Classes.AddRange(c9b, c7, c9a, c5);

        var math = new Subject { Name = "Matematika" };
        var physics = new Subject { Name = "Fizika" };
        var english = new Subject { Name = "Ingliz tili" };
        db.Subjects.AddRange(math, physics, english);

        var teacher = new Teacher { FullName = "Yo'nalish ustozi" };
        db.Teachers.Add(teacher);
        var absent = new AbsenceReason { Name = "Sababsiz", Short = "S", Points = -10 };
        db.AbsenceReasons.Add(absent);

        var p5 = NewStudent("Olim Olimov", c5.Name);
        var p9a = NewStudent("Nodir Nodirov", c9a.Name);
        var p9b = NewStudent("Botir Botirov", c9b.Name);
        db.Students.AddRange(p5, p9a, p9b);

        var user = new AppUser { FullName = "Seed", Role = Roles.Admin, Email = $"tgc.{Guid.NewGuid():N}" };
        user.SetInitialPassword("Test-" + Guid.NewGuid().ToString("N")[..10]);
        db.Users.Add(user);
        await db.SaveChangesAsync();

        var track = new StudyGroup
        {
            Name = "Aniq fanlar", SubjectId = null, IsTrack = true, CreatedBy = user.Id, CreatedAt = AppClock.NowInstant,
        };
        var ordinary = new StudyGroup
        {
            Name = "Ingliz", SubjectId = english.Id, CreatedBy = user.Id, CreatedAt = AppClock.NowInstant,
        };
        db.StudyGroups.AddRange(track, ordinary);
        db.StudyGroupClasses.AddRange(
            new StudyGroupClass { GroupId = track.Id, ClassId = c9a.Id },
            new StudyGroupClass { GroupId = track.Id, ClassId = c9b.Id },
            new StudyGroupClass { GroupId = ordinary.Id, ClassId = c5.Id });
        db.StudyGroupTeachers.AddRange(
            new StudyGroupTeacher { GroupId = track.Id, TeacherId = teacher.Id },
            new StudyGroupTeacher { GroupId = ordinary.Id, TeacherId = teacher.Id });
        var joined = new DateOnly(2026, 8, 31);
        db.StudyGroupMembers.AddRange(
            Member(track.Id, null, p9a.Id, joined, user.Id),
            Member(track.Id, null, p9b.Id, joined, user.Id),
            Member(ordinary.Id, english.Id, p5.Id, joined, user.Id));

        var classTpl = new ScheduleTemplate
        {
            ClassId = c5.Id, Name = "5-A asosiy",
            Lessons = [new ScheduleLesson { Day = 0, Period = ClassPeriod, SubjectId = math.Id, TeacherId = teacher.Id }],
        };
        var trackTpl = new ScheduleTemplate
        {
            ClassId = track.Id.ToString(), Name = "Aniq fanlar asosiy", OwnerKind = LessonOwnerKind.Group,
            Lessons = [new ScheduleLesson { Day = 0, Period = TrackPeriod, SubjectId = physics.Id, TeacherId = teacher.Id }],
        };
        var ordinaryTpl = new ScheduleTemplate
        {
            ClassId = ordinary.Id.ToString(), Name = "Ingliz asosiy", OwnerKind = LessonOwnerKind.Group,
            Lessons = [new ScheduleLesson { Day = 0, Period = OrdinaryPeriod, SubjectId = english.Id, TeacherId = teacher.Id }],
        };
        db.ScheduleTemplates.AddRange(classTpl, trackTpl, ordinaryTpl);
        db.WeekAssignments.AddRange(
            new WeekAssignment { ClassId = c5.Id, Quarter = Quarter, Week = 1, TemplateId = classTpl.Id },
            new WeekAssignment
            {
                ClassId = track.Id.ToString(), Quarter = Quarter, Week = 1, TemplateId = trackTpl.Id,
                OwnerKind = LessonOwnerKind.Group,
            },
            new WeekAssignment
            {
                ClassId = ordinary.Id.ToString(), Quarter = Quarter, Week = 1, TemplateId = ordinaryTpl.Id,
                OwnerKind = LessonOwnerKind.Group,
            });
        await db.SaveChangesAsync();

        return new World(
            c5.Id, c7.Id, c9a.Id, c9b.Id,
            track.Id.ToString(), ordinary.Id.ToString(), trackTpl.Id,
            math.Id, physics.Id, english.Id,
            p5.Id, p9a.Id, p9b.Id,
            teacher.Id, absent.Id, user.Id);
    }

    private static StudyGroupMember Member(Guid groupId, string? subjectId, string studentId, DateOnly joined, string by) => new()
    {
        GroupId = groupId, SubjectId = subjectId, StudentId = studentId, JoinedOn = joined,
        CreatedBy = by, CreatedAt = AppClock.NowInstant,
    };

    private static Student NewStudent(string fullName, string className) => new()
    {
        FullName = fullName,
        LastName = fullName.Split(' ')[^1],
        FirstName = fullName.Split(' ')[0],
        BirthDate = "2010-01-01",
        Gender = "male",
        ClassName = className,
        EnrollmentDate = "2025-09-01",
    };

    private sealed record Shared(string Tag, string ClassId, string TeacherId, string SubjectId, string StudentA, string StudentB);

    private async Task<Shared> SeedSharedAsync()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var cls = new SchoolClass { Name = $"10-{tag}", Grade = 10 };
        var teacher = new Teacher { FullName = $"Ustoz {tag}" };
        var subject = new Subject { Name = $"Fan {tag}" };
        var a = NewStudent($"Ali {tag}", cls.Name);
        var b = NewStudent($"Vali {tag}", cls.Name);
        await fixture.Api.WithDbAsync(async db =>
        {
            db.Classes.Add(cls); db.Teachers.Add(teacher); db.Subjects.Add(subject);
            db.Students.AddRange(a, b);
            await db.SaveChangesAsync();
        });
        return new Shared(tag, cls.Id, teacher.Id, subject.Id, a.Id, b.Id);
    }

    private static async Task<Guid> CreateTrackAsync(HttpClient admin, Shared s, string name, string[] students)
    {
        var res = await admin.PostAsJsonAsync(Groups, new
        {
            name, classIds = new[] { s.ClassId }, teacherIds = new[] { s.TeacherId },
            isTrack = true, studentIds = students,
        });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return (await res.Content.ReadFromJsonAsync<StudyGroupDetailDto>())!.Id;
    }
}
