using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Auth;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// Teacher attendance over a date range, tied to the timetable (client, 2026-10-15: "Ustozlar
/// davomatini ikki davrli qilish … davomatda faqat o'sha kuni darsi bor ustozlar chiqishi";
/// docs/modules/teacher-attendance-range.md).
///
/// <para>
/// The helper and the turnstile recompute run on their own databases with fixed September 2026
/// dates. The API tests share the app database, so they set the quarter for the test only (as
/// LessonSubstitutionTests does), add their holiday under a unique name and remove it afterwards,
/// and assert only about the teachers they seeded — other tests' timetables share the same weeks.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class TeacherAttendanceRangeTests(ApiFixture fixture) : IAsyncLifetime
{
    private const string Api = "/api/admin/teacher-attendance";
    private readonly List<string> _connectionStrings = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        foreach (var connectionString in _connectionStrings)
            NpgsqlConnection.ClearPool(new NpgsqlConnection(connectionString));
        return Task.CompletedTask;
    }

    /* =====================================================================
     *  TeacherDaySchedule — fixed dates, own database
     *  Quarter 1 starts Tue 2026-09-01, so week 2 = Mon 07.09 … Sat 12.09.
     * ================================================================== */

    [Fact]
    public async Task Jadval_boyicha_faqat_darsi_bor_kunlar_bayram_yakshanba_chorakdan_tashqari_yoq()
    {
        await using var db = await NewDbAsync("schedule");
        var w = await SeedFixedWeekAsync(db);

        var s = await TeacherDaySchedule.LoadAsync(db, new DateOnly(2026, 8, 29), new DateOnly(2026, 9, 14));

        Assert.Equal(17, s.Days.Count);
        var aug31 = s.Days.Single(d => d.Date == new DateOnly(2026, 8, 31));
        Assert.False(aug31.InQuarter);
        Assert.Empty(s.TeachersOn(aug31.Date));

        Assert.Equal(2, s.TeachersOn(Sep(7))[w.A]);
        Assert.False(s.Teaches(w.B, Sep(7)));
        Assert.Equal(1, s.TeachersOn(Sep(8))[w.B]);
        Assert.Equal(1, s.TeachersOn(Sep(12))[w.B]);

        // Wednesday is a holiday: A's lesson that day does not count.
        var wed = s.Days.Single(d => d.Date == Sep(9));
        Assert.True(wed.IsHoliday);
        Assert.Equal("Test bayram", wed.HolidayName);
        Assert.Empty(s.TeachersOn(Sep(9)));

        Assert.True(s.Days.Single(d => d.Date == Sep(13)).IsSunday);
        Assert.Empty(s.TeachersOn(Sep(13)));
        // Week 3 has no timetable assigned — nobody teaches.
        Assert.Empty(s.TeachersOn(Sep(14)));

        Assert.Equal(new[] { w.A, w.B }.Order(), s.TeacherIds.Order());
        Assert.DoesNotContain(w.C, s.TeacherIds);
    }

    [Fact]
    public async Task Orindosh_darsni_oladi_asosiy_ustoz_royxatda_qoladi_bekor_qilingani_hisoblanmaydi()
    {
        await using var db = await NewDbAsync("substitution");
        var w = await SeedFixedWeekAsync(db);
        var admin = await SeedAdminAsync(db);
        db.LessonSubstitutions.AddRange(
            // Mon: A's second lesson goes to D.
            Substitution(Sep(7), 2, w, w.A, w.D, admin),
            // Tue: B's only lesson goes to D; an earlier, cancelled pick of C does not count.
            Substitution(Sep(8), 1, w, w.B, w.C, admin, cancelled: true),
            Substitution(Sep(8), 1, w, w.B, w.D, admin),
            // Wed is a holiday: a substitution there gives nobody a lesson.
            Substitution(Sep(9), 1, w, w.A, w.D, admin));
        await db.SaveChangesAsync();

        var s = await TeacherDaySchedule.LoadAsync(db, Sep(7), Sep(13));

        Assert.Equal(1, s.TeachersOn(Sep(7))[w.A]);
        Assert.Equal(1, s.TeachersOn(Sep(7))[w.D]);
        // B gave away every lesson but stays listed, so B can still be marked absent.
        Assert.True(s.Teaches(w.B, Sep(8)));
        Assert.Equal(0, s.TeachersOn(Sep(8))[w.B]);
        Assert.Equal(1, s.TeachersOn(Sep(8))[w.D]);
        Assert.DoesNotContain(w.C, s.TeacherIds);
        Assert.Empty(s.TeachersOn(Sep(9)));
    }

    /* =====================================================================
     *  TurnstileService.RecomputeAsync — fixed past dates, own database
     * ================================================================== */

    [Fact]
    public async Task Turniket_darsi_yoq_kunga_kelmadi_yozmaydi()
    {
        await using var db = await NewDbAsync("recompute");
        var w = await SeedFixedWeekAsync(db);
        var a = await db.Teachers.SingleAsync(t => t.Id == w.A);
        a.DeviceUserId = "7001";
        // Thu 10.09 — no lessons, but A came: the event still gives "present", as before.
        db.TurnstileEvents.Add(new TurnstileEvent
        {
            TeacherId = w.A, DeviceUserId = "7001", EventAt = "2026-09-10T08:05:00", Direction = "in",
            DeviceName = "Test", CreatedAt = "2026-09-10T08:05:01",
        });
        db.TeacherAttendances.AddRange(
            // Written by the old rule on a day without lessons — dropped by the recompute.
            new TeacherAttendance { TeacherId = w.A, Date = "2026-09-12", Status = "absent", Source = "turnstile" },
            // Set by the admin — never touched.
            new TeacherAttendance { TeacherId = w.A, Date = "2026-09-11", Status = "absent", Source = "manual" });
        await db.SaveChangesAsync();

        await new TurnstileService().RecomputeAsync(db, new DateTime(2026, 9, 7), new DateTime(2026, 9, 13));

        var rows = (await db.TeacherAttendances.AsNoTracking().Where(x => x.TeacherId == w.A).ToListAsync())
            .ToDictionary(x => x.Date, x => (x.Status, x.Source));
        Assert.Equal(("absent", "turnstile"), rows["2026-09-07"]); // Mon: lessons, no event
        Assert.False(rows.ContainsKey("2026-09-08"));              // Tue: no lessons
        Assert.False(rows.ContainsKey("2026-09-09"));              // Wed: lesson, but a holiday
        Assert.Equal(("present", "turnstile"), rows["2026-09-10"]);
        Assert.Equal(("absent", "manual"), rows["2026-09-11"]);
        Assert.False(rows.ContainsKey("2026-09-12"));              // Sat: stale auto-absent removed
        Assert.False(rows.ContainsKey("2026-09-13"));              // Sunday
    }

    /* =====================================================================
     *  API — shared database, a past week inside a quarter set for the test
     * ================================================================== */

    [Fact]
    public async Task Davr_boardi_faqat_darsi_bor_ustozlarni_va_ularning_dars_kunlarini_qaytaradi()
    {
        await WithWorldAsync(async w =>
        {
            var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
            var board = (await admin.GetFromJsonAsync<TeacherAttendanceBoardDto>(
                $"{Api}?from={Iso(w.Monday)}&to={Iso(w.Monday.AddDays(6))}"))!;

            Assert.Equal(Iso(w.Monday), board.From);
            Assert.Equal(Iso(w.Monday.AddDays(6)), board.To);
            var ids = board.Teachers.Select(t => t.Id).ToList();
            Assert.Contains(w.A, ids);
            Assert.Contains(w.B, ids);
            Assert.Contains(w.D, ids);          // the substitute
            Assert.Contains(w.C, ids);          // no lessons, but has entries — nothing recorded disappears
            Assert.DoesNotContain(w.E, ids);    // no lessons, no entries

            var lessons = board.Lessons!.Where(l => w.All.Contains(l.TeacherId))
                .ToDictionary(l => (l.TeacherId, l.Date), l => l.Lessons);
            Assert.Equal(1, lessons[(w.A, Iso(w.Monday))]);              // 2 own − 1 given to D
            Assert.Equal(1, lessons[(w.D, Iso(w.Monday))]);
            Assert.Equal(1, lessons[(w.B, Iso(w.Monday.AddDays(1)))]);
            Assert.False(lessons.ContainsKey((w.A, Iso(w.Monday.AddDays(2))))); // holiday
            Assert.DoesNotContain(lessons.Keys, k => k.TeacherId == w.C);

            Assert.Equal(7, board.Days!.Count);
            var wed = board.Days.Single(d => d.Date == Iso(w.Monday.AddDays(2)));
            Assert.True(wed.IsHoliday);
            Assert.Equal(w.HolidayName, wed.HolidayName);
            Assert.True(board.Days.Single(d => d.Date == Iso(w.Monday.AddDays(6))).IsSunday);
            Assert.All(board.Days, d => Assert.True(d.InQuarter));
            Assert.True(board.Days.Single(d => d.Date == Iso(w.Monday)).Teachers >= 2);

            // Entries for the range come back as before.
            Assert.Contains(board.Entries, e => e.TeacherId == w.C && e.Date == Iso(w.Monday.AddDays(3)));
        });
    }

    [Fact]
    public async Task Kunni_belgilash_faqat_osha_kuni_darsi_bor_ustozlarga_tegadi()
    {
        await WithWorldAsync(async w =>
        {
            var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
            var monday = Iso(w.Monday);

            Assert.Equal(HttpStatusCode.NoContent,
                (await admin.PutAsJsonAsync($"{Api}/day", new { date = monday, status = "present" })).StatusCode);
            var marked = await StatusesAsync(w, monday);
            Assert.Equal("present", marked[w.A]);
            Assert.Equal("present", marked[w.D]);
            Assert.False(marked.ContainsKey(w.B));
            Assert.False(marked.ContainsKey(w.E));
            Assert.Equal("absent", marked[w.C]); // not scheduled — left as it was

            Assert.Equal(HttpStatusCode.NoContent,
                (await admin.PutAsJsonAsync($"{Api}/day", new { date = monday, status = (string?)null })).StatusCode);
            var cleared = await StatusesAsync(w, monday);
            Assert.False(cleared.ContainsKey(w.A));
            Assert.False(cleared.ContainsKey(w.D));
            Assert.Equal("absent", cleared[w.C]);

            // A holiday and a Sunday: nobody teaches, nothing to mark.
            foreach (var day in new[] { w.Monday.AddDays(2), w.Monday.AddDays(6) })
                Assert.Equal("Bu kuni darsi bor ustoz yo'q", await MessageAsync(
                    await admin.PutAsJsonAsync($"{Api}/day", new { date = Iso(day), status = "present" })));
        });
    }

    [Fact]
    public async Task Katak_darsi_yoq_kunga_belgilanmaydi_lekin_tozalanadi()
    {
        await WithWorldAsync(async w =>
        {
            var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
            var monday = Iso(w.Monday);

            Assert.Equal("Bu kuni ustozning darsi yo'q", await MessageAsync(await PutCellAsync(admin, w.B, monday, "present")));
            Assert.Equal("Bu kuni ustozning darsi yo'q", await MessageAsync(await PutCellAsync(admin, w.A, Iso(w.Monday.AddDays(2)), "absent")));
            Assert.Equal("Bu kuni ustozning darsi yo'q", await MessageAsync(await PutCellAsync(admin, w.C, Iso(w.Monday.AddDays(3)), "late")));

            Assert.Equal(HttpStatusCode.NoContent, (await PutCellAsync(admin, w.A, monday, "late")).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await PutCellAsync(admin, w.D, monday, "absent")).StatusCode);
            // Clearing is always allowed — C's old mark on a day without lessons can be removed.
            Assert.Equal(HttpStatusCode.NoContent, (await PutCellAsync(admin, w.C, monday, null)).StatusCode);

            var rows = await StatusesAsync(w, monday);
            Assert.Equal("late", rows[w.A]);
            Assert.Equal("absent", rows[w.D]);
            Assert.False(rows.ContainsKey(w.B));
            Assert.False(rows.ContainsKey(w.C));
        });
    }

    [Fact]
    public async Task Dashboard_faqat_darsi_borlarni_korsatadi_darsi_yoqqa_kelmadi_qoyilmaydi()
    {
        await WithWorldAsync(async w =>
        {
            var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
            var dash = (await admin.GetFromJsonAsync<TeacherAttendanceDashboardDto>($"{Api}/dashboard?date={Iso(w.Monday)}"))!;
            var rows = dash.Rows.ToDictionary(r => r.TeacherId);

            Assert.True(rows[w.A].HasLessons);
            Assert.Equal(1, rows[w.A].Lessons);
            Assert.Equal("absent", rows[w.A].Status); // past day, lessons, no entry → live "kelmadi"
            Assert.True(rows[w.D].HasLessons);
            Assert.False(rows.ContainsKey(w.B));
            Assert.False(rows.ContainsKey(w.E));
            // C has no lessons but an entry that day: listed, flagged, the entry shown as recorded.
            Assert.False(rows[w.C].HasLessons);
            Assert.Equal("absent", rows[w.C].Status);
            Assert.True(dash.NoLessonTeachers >= 3); // B, C, E at least
            Assert.Equal(dash.Rows.Count, dash.Summary.Total);

            // Tuesday: C has no entry → not listed at all, never a live "kelmadi".
            var tue = (await admin.GetFromJsonAsync<TeacherAttendanceDashboardDto>($"{Api}/dashboard?date={Iso(w.Monday.AddDays(1))}"))!;
            Assert.DoesNotContain(tue.Rows, r => r.TeacherId == w.C || r.TeacherId == w.A);
            Assert.Equal("absent", tue.Rows.Single(r => r.TeacherId == w.B).Status);
        });
    }

    [Fact]
    public async Task Davr_tekshiruvi_400_qaytaradi_oy_parametri_ishlashda_davom_etadi()
    {
        var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        Assert.Contains("keyin", await MessageAsync(await admin.GetAsync($"{Api}?from=2026-09-30&to=2026-09-01")));
        Assert.Contains("93", await MessageAsync(await admin.GetAsync($"{Api}?from=2026-09-01&to=2026-12-03")));
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync($"{Api}?from=2026-13-01&to=2026-12-31")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync($"{Api}?from=2026-09-01")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync($"{Api}?month=2026-xx")).StatusCode);

        var max = (await admin.GetFromJsonAsync<TeacherAttendanceBoardDto>($"{Api}?from=2026-09-01&to=2026-12-02"))!;
        Assert.Equal(93, max.Days!.Count);

        var month = (await admin.GetFromJsonAsync<TeacherAttendanceBoardDto>($"{Api}?month=2026-02"))!;
        Assert.Equal("2026-02-01", month.From);
        Assert.Equal("2026-02-28", month.To);
        Assert.Equal(28, month.Days!.Count);
    }

    [Fact]
    public async Task Ruxsatlar_anonim_401_oqituvchi_403_teachers_ruxsatisiz_xodim_yoza_olmaydi()
    {
        const string range = "?from=2031-01-06&to=2031-01-11";
        using var anonymous = fixture.Api.AnonymousClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Api + range)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await anonymous.PutAsJsonAsync($"{Api}/day", new { date = "2031-01-06", status = "present" })).StatusCode);

        using var teacher = await fixture.Api.ClientAsAsync(Roles.Teacher);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync(Api + range)).StatusCode);

        // The neighbours' gate: staff may read any admin section, write only with the section key.
        using var staff = await fixture.Api.ClientAsAsync(Roles.Staff, "students");
        Assert.Equal(HttpStatusCode.OK, (await staff.GetAsync(Api + range)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await staff.PutAsJsonAsync($"{Api}/day", new { date = "2031-01-06", status = "present" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PutCellAsync(staff, "x", "2031-01-06", null)).StatusCode);

        using var keeper = await fixture.Api.ClientAsAsync(Roles.Staff, "teachers");
        Assert.Equal(HttpStatusCode.NoContent,
            (await keeper.PutAsJsonAsync($"{Api}/day", new { date = "2031-01-06", status = (string?)null })).StatusCode);
    }

    // =================================================================

    /// <summary>
    /// A, B, D and E teach or not as named; C has no lessons. In the fixed-date databases the
    /// substitute D has no own lessons; in the shared-database world A gives one Monday lesson to D.
    /// </summary>
    private sealed record World(
        string ClassId, string SubjectId, string A, string B, string C, string D, string E,
        DateOnly Monday, string HolidayName)
    {
        public HashSet<string> All => [A, B, C, D, E];
    }

    private static DateOnly Sep(int day) => new(2026, 9, day);

    private static string Iso(DateOnly d) => d.ToString("yyyy-MM-dd");

    private static int Dow(DateOnly d) => ((int)d.DayOfWeek + 6) % 7;

    private async Task<AppDbContext> NewDbAsync(string prefix)
    {
        var database = await fixture.Postgres.CreateDatabaseAsync("tatt_" + prefix);
        _connectionStrings.Add(database.OwnerConnectionString);
        return PostgresFixture.NewContext(database.OwnerConnectionString);
    }

    /// <summary>
    /// Quarter 1 from 2026-09-01, a holiday on Wed 09.09, and one class whose week-2 timetable has
    /// A on Mon (periods 1, 2) and Wed (period 1), B on Tue and Sat (period 1). C and D teach nothing.
    /// </summary>
    private static async Task<World> SeedFixedWeekAsync(AppDbContext db)
    {
        db.Quarters.Add(new QuarterPeriod { Quarter = 1, StartDate = "2026-09-01", EndDate = "2026-10-31" });
        db.Holidays.Add(new Holiday { Date = "2026-09-09", Name = "Test bayram" });
        var (a, b, c, d, e) = (NewTeacher("A"), NewTeacher("B"), NewTeacher("C"), NewTeacher("D"), NewTeacher("E"));
        db.Teachers.AddRange(a, b, c, d, e);
        var cls = new SchoolClass { Name = "5-T", Grade = 5 };
        var subject = new Subject { Name = "Matematika" };
        db.Classes.Add(cls);
        db.Subjects.Add(subject);
        var tpl = new ScheduleTemplate { ClassId = cls.Id, Name = "Asosiy" };
        foreach (var (day, period, teacher) in new[] { (0, 1, a), (0, 2, a), (2, 1, a), (1, 1, b), (5, 1, b) })
            tpl.Lessons.Add(new ScheduleLesson { TemplateId = tpl.Id, Day = day, Period = period, SubjectId = subject.Id, TeacherId = teacher.Id });
        db.ScheduleTemplates.Add(tpl);
        db.WeekAssignments.Add(new WeekAssignment { ClassId = cls.Id, Quarter = 1, Week = 2, TemplateId = tpl.Id });
        await db.SaveChangesAsync();
        return new World(cls.Id, subject.Id, a.Id, b.Id, c.Id, d.Id, e.Id, Sep(7), "Test bayram");
    }

    private static Teacher NewTeacher(string name) => new() { FullName = $"Ustoz {name} {Guid.NewGuid():N}"[..20] };

    private static async Task<string> SeedAdminAsync(AppDbContext db)
    {
        var user = new AppUser { FullName = "Admin", Role = Roles.Admin, Email = $"tatt.{Guid.NewGuid():N}" };
        user.SetInitialPassword("Test-" + Guid.NewGuid().ToString("N")[..10]);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }

    private static LessonSubstitution Substitution(
        DateOnly date, int period, World w, string absent, string substitute, string actor, bool cancelled = false) => new()
    {
        Date = date, Period = period, OwnerKind = LessonOwnerKind.Class, OwnerId = w.ClassId, SubjectId = w.SubjectId,
        AbsentTeacherId = absent, SubstituteTeacherId = substitute, CreatedBy = actor, CreatedAt = AppClock.NowInstant,
        CancelledAt = cancelled ? AppClock.NowInstant : null, CancelledBy = cancelled ? actor : null,
    };

    /// <summary>
    /// The shared database's quarter is replaced for the test (today ± 40 days) and restored after.
    /// The world's week is the one two weeks back (all of it in the past): A teaches Mon (periods 1, 2,
    /// period 2 given to D) and Wed (a holiday), B Tue; C has entries on Mon (absent) and Thu (present)
    /// but no lessons; E has nothing.
    /// </summary>
    private async Task WithWorldAsync(Func<World, Task> body)
    {
        var today = AppClock.Today;
        var monday = today.AddDays(-Dow(today) - 14);
        var tag = Guid.NewGuid().ToString("N")[..8];
        var holidayName = "Test bayrami " + tag;
        List<QuarterPeriod> saved = [];
        await fixture.Api.WithDbAsync(async db =>
        {
            saved = await db.Quarters.AsNoTracking().ToListAsync();
            await db.Quarters.ExecuteDeleteAsync();
            db.Quarters.Add(new QuarterPeriod { Quarter = 1, StartDate = Iso(today.AddDays(-40)), EndDate = Iso(today.AddDays(40)) });
            await db.SaveChangesAsync();
        });
        try
        {
            World w = default!;
            await fixture.Api.WithDbAsync(async db =>
            {
                var place = LessonSubstitutionService.Place(await db.Quarters.AsNoTracking().ToListAsync(), monday)!.Value;
                var admin = await SeedAdminAsync(db);
                var (a, b, c, d, e) = (NewTeacher("A"), NewTeacher("B"), NewTeacher("C"), NewTeacher("D"), NewTeacher("E"));
                db.Teachers.AddRange(a, b, c, d, e);
                var cls = new SchoolClass { Name = $"TA-{tag}", Grade = 5 };
                var subject = new Subject { Name = $"Fan {tag}" };
                db.Classes.Add(cls);
                db.Subjects.Add(subject);
                var tpl = new ScheduleTemplate { ClassId = cls.Id, Name = "Asosiy " + tag };
                foreach (var (day, period, teacher) in new[] { (0, 1, a), (0, 2, a), (2, 1, a), (1, 1, b) })
                    tpl.Lessons.Add(new ScheduleLesson { TemplateId = tpl.Id, Day = day, Period = period, SubjectId = subject.Id, TeacherId = teacher.Id });
                db.ScheduleTemplates.Add(tpl);
                db.WeekAssignments.Add(new WeekAssignment { ClassId = cls.Id, Quarter = place.Quarter, Week = place.Week, TemplateId = tpl.Id });
                db.Holidays.Add(new Holiday { Date = Iso(monday.AddDays(2)), Name = holidayName });
                db.TeacherAttendances.AddRange(
                    new TeacherAttendance { TeacherId = c.Id, Date = Iso(monday), Status = "absent" },
                    new TeacherAttendance { TeacherId = c.Id, Date = Iso(monday.AddDays(3)), Status = "present" });
                await db.SaveChangesAsync();
                w = new World(cls.Id, subject.Id, a.Id, b.Id, c.Id, d.Id, e.Id, monday, holidayName);
                db.LessonSubstitutions.Add(Substitution(monday, 2, w, a.Id, d.Id, admin));
                await db.SaveChangesAsync();
            });
            await body(w);
        }
        finally
        {
            await fixture.Api.WithDbAsync(async db =>
            {
                // Holidays and quarters are shared by the whole school — put back exactly what was there.
                await db.Holidays.Where(h => h.Name == holidayName).ExecuteDeleteAsync();
                await db.Quarters.ExecuteDeleteAsync();
                db.Quarters.AddRange(saved);
                await db.SaveChangesAsync();
            });
        }
    }

    private async Task<Dictionary<string, string>> StatusesAsync(World w, string date)
    {
        var result = new Dictionary<string, string>();
        await fixture.Api.WithDbAsync(async db =>
        {
            var ids = w.All.ToList();
            result = await db.TeacherAttendances.AsNoTracking()
                .Where(a => a.Date == date && ids.Contains(a.TeacherId))
                .ToDictionaryAsync(a => a.TeacherId, a => a.Status);
        });
        return result;
    }

    private static Task<HttpResponseMessage> PutCellAsync(HttpClient client, string teacherId, string date, string? status) =>
        client.PutAsJsonAsync(Api, new { teacherId, date, status, note = (string?)null });

    private static async Task<string> MessageAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("message").GetString() ?? "";
    }
}
