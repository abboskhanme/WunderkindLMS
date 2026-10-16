using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

// ===========================================================================
//  Teachers no longer mark pupils' attendance (client, 2026-10-15; docs/modules/teacher-attendance-off.md).
//
//  `school_meta.teachers_mark_attendance`, default false. Off: a teacher's journal write keeps the
//  stored attendance reason (the nurse's) whatever it sends — never a 400 — and a teacher's clear keeps
//  a row that carries one. On: everything exactly as before. The admin journal is never affected.
//
//  World: one class, the teacher's subject on the timetable, two attendance reasons. The flag lives in
//  the single shared `school_meta` row, so every test flips it through SchoolMetaFlags, which restores it.
// ===========================================================================

[Collection(SchoolLmsCollection.Name)]
public class TeacherAttendanceFlagTests(ApiFixture fixture)
{
    private const string General = "/api/admin/settings/general";
    private const string TeacherJournal = "/api/teacher/journal";
    private const string AdminJournal = "/api/admin/journal";
    private const string Date = "2026-09-08";

    // =====================================================================
    //  1. The flag: default, migration default, settings round-trip, RBAC
    // =====================================================================

    [Fact]
    public async Task Default_is_false_for_no_row_and_for_the_column()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        await SchoolMetaFlags.WithoutRowAsync(fixture.Api, async () =>
        {
            using var json = await JsonAsync(admin, General);
            Assert.False(json.RootElement.GetProperty("teachersMarkAttendance").GetBoolean());
        });

        // The column's own DEFAULT — what the existing school gets when the migration runs.
        await fixture.Api.WithDbAsync(async db =>
        {
            var conn = db.Database.GetDbConnection();
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT column_default, is_nullable FROM information_schema.columns "
                              + "WHERE table_name = 'school_meta' AND column_name = 'teachers_mark_attendance'";
            await using var reader = await cmd.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync());
            Assert.Equal("false", reader.GetString(0));
            Assert.Equal("NO", reader.GetString(1));
        });
    }

    [Fact]
    public async Task Settings_round_trip_and_a_missing_field_leaves_it_unchanged()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        await SchoolMetaFlags.WithAsync(fixture.Api, m => m.TeachersMarkAttendance = false, async () =>
        {
            var on = await admin.PutAsJsonAsync(General, Body(teachersMark: true));
            Assert.Equal(HttpStatusCode.OK, on.StatusCode);
            using (var json = await JsonAsync(admin, General))
                Assert.True(json.RootElement.GetProperty("teachersMarkAttendance").GetBoolean());

            // An older client saves the four §5.5 flags only — the new one stays as it was.
            var old = await admin.PutAsJsonAsync(General, new
            {
                archiveOnlyNonDebtorStudents = true, makeAttendanceReasonRequired = false,
                isStudentGradeRequired = false, showLearningProgressInParentDashboard = true,
            });
            Assert.Equal(HttpStatusCode.OK, old.StatusCode);
            using (var json = await JsonAsync(admin, General))
                Assert.True(json.RootElement.GetProperty("teachersMarkAttendance").GetBoolean());

            Assert.Equal(HttpStatusCode.OK, (await admin.PutAsJsonAsync(General, Body(teachersMark: false))).StatusCode);
            using (var json = await JsonAsync(admin, General))
                Assert.False(json.RootElement.GetProperty("teachersMarkAttendance").GetBoolean());
        });
    }

    [Fact]
    public async Task Only_full_settings_permission_may_change_it()
    {
        using var viewer = await fixture.Api.ClientAsAsync(Roles.Staff, "settings:view");
        using var teacher = await fixture.Api.ClientAsAsync(Roles.Teacher, "settings");

        Assert.Equal(HttpStatusCode.Forbidden, (await viewer.PutAsJsonAsync(General, Body(teachersMark: true))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.PutAsJsonAsync(General, Body(teachersMark: true))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync(General)).StatusCode);
    }

    [Fact]
    public async Task Teacher_profile_carries_canMarkAttendance()
    {
        var w = await SeedAsync();
        await SchoolMetaFlags.WithAsync(fixture.Api, m => m.TeachersMarkAttendance = false, async () =>
        {
            using var json = await JsonAsync(w.Teacher, "/api/teacher/me");
            Assert.False(json.RootElement.GetProperty("canMarkAttendance").GetBoolean());
        });
        await SchoolMetaFlags.WithAsync(fixture.Api, m => m.TeachersMarkAttendance = true, async () =>
        {
            using var json = await JsonAsync(w.Teacher, "/api/teacher/me");
            Assert.True(json.RootElement.GetProperty("canMarkAttendance").GetBoolean());
        });
    }

    // =====================================================================
    //  2. Flag off — the nurse's mark survives every teacher write
    // =====================================================================

    [Fact]
    public async Task Off_teacher_put_with_another_reason_keeps_the_nurses_and_writes_the_grade() =>
        await SchoolMetaFlags.WithAsync(fixture.Api, m => m.TeachersMarkAttendance = false, async () =>
        {
            var w = await SeedAsync();
            await NurseMarksAsync(w, w.StudentA, w.SickId);

            // The Mini App re-sends what it loaded — or something else entirely, or "keldi" (null).
            Assert.Equal(HttpStatusCode.NoContent, (await Put(w.Teacher, TeacherJournal, w, w.StudentA, reasonId: w.TruantId, homework: 1)).StatusCode);
            Assert.Equal((w.SickId, (int?)null, 1), await CellAsync(w, w.StudentA));

            Assert.Equal(HttpStatusCode.NoContent, (await Put(w.Teacher, TeacherJournal, w, w.StudentA, reasonId: null, homework: 1)).StatusCode);
            Assert.Equal((w.SickId, (int?)null, 1), await CellAsync(w, w.StudentA));

            // A pupil the nurse left unmarked: the teacher's grade goes in, the teacher's reason does not.
            Assert.Equal(HttpStatusCode.NoContent, (await Put(w.Teacher, TeacherJournal, w, w.StudentB, grade: 5, reasonId: w.TruantId)).StatusCode);
            Assert.Equal(((string?)null, (int?)5, 0), await CellAsync(w, w.StudentB));
        });

    [Fact]
    public async Task Off_reason_required_gate_does_not_refuse_the_teachers_grade() =>
        await SchoolMetaFlags.WithAsync(fixture.Api, m =>
        {
            m.TeachersMarkAttendance = false;
            m.MakeAttendanceReasonRequired = true;
        }, async () =>
        {
            var w = await SeedAsync();
            await NurseMarksAsync(w, w.StudentA, w.SickId);

            var put = await Put(w.Teacher, TeacherJournal, w, w.StudentA, reasonId: "", homework: 3);
            Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
            Assert.Equal((w.SickId, (int?)null, 3), await CellAsync(w, w.StudentA));

            // The admin journal still enforces it.
            using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
            var adminPut = await Put(admin, AdminJournal, w, w.StudentB, reasonId: "");
            Assert.Equal(HttpStatusCode.BadRequest, adminPut.StatusCode);
        });

    [Fact]
    public async Task Off_teacher_clear_keeps_a_row_with_a_reason_and_deletes_one_without() =>
        await SchoolMetaFlags.WithAsync(fixture.Api, m => m.TeachersMarkAttendance = false, async () =>
        {
            var w = await SeedAsync();
            await NurseMarksAsync(w, w.StudentA, w.SickId);
            Assert.Equal(HttpStatusCode.NoContent, (await Put(w.Teacher, TeacherJournal, w, w.StudentA, homework: 2)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await Put(w.Teacher, TeacherJournal, w, w.StudentB, grade: 4)).StatusCode);

            Assert.Equal(HttpStatusCode.NoContent, (await Clear(w.Teacher, w, w.StudentA)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await Clear(w.Teacher, w, w.StudentB)).StatusCode);

            Assert.Equal((w.SickId, (int?)null, 0), await CellAsync(w, w.StudentA));
            Assert.Null(await CellOrNullAsync(w, w.StudentB));
        });

    [Fact]
    public async Task Off_teacher_write_with_nothing_left_creates_no_empty_row() =>
        await SchoolMetaFlags.WithAsync(fixture.Api, m => m.TeachersMarkAttendance = false, async () =>
        {
            var w = await SeedAsync();
            // Only a reason (the cached nurse's mark the nurse has meanwhile removed) and nothing else.
            Assert.Equal(HttpStatusCode.NoContent, (await Put(w.Teacher, TeacherJournal, w, w.StudentB, reasonId: w.SickId)).StatusCode);
            Assert.Null(await CellOrNullAsync(w, w.StudentB));
        });

    [Fact]
    public async Task Off_admin_journal_still_writes_and_clears_the_reason() =>
        await SchoolMetaFlags.WithAsync(fixture.Api, m => m.TeachersMarkAttendance = false, async () =>
        {
            var w = await SeedAsync();
            using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
            await NurseMarksAsync(w, w.StudentA, w.SickId);

            Assert.Equal(HttpStatusCode.NoContent, (await Put(admin, AdminJournal, w, w.StudentA, reasonId: w.TruantId)).StatusCode);
            Assert.Equal((w.TruantId, (int?)null, 0), await CellAsync(w, w.StudentA));

            var clear = await admin.DeleteAsync(
                $"{AdminJournal}?classId={w.ClassId}&subjectId={w.SubjectId}&quarter=1&studentId={w.StudentA}&date={Date}&period=1");
            Assert.Equal(HttpStatusCode.NoContent, clear.StatusCode);
            Assert.Null(await CellOrNullAsync(w, w.StudentA));
        });

    // =====================================================================
    //  3. Flag on — as before
    // =====================================================================

    [Fact]
    public async Task On_teacher_writes_and_clears_attendance_as_before() =>
        await SchoolMetaFlags.WithAsync(fixture.Api, m => m.TeachersMarkAttendance = true, async () =>
        {
            var w = await SeedAsync();
            await NurseMarksAsync(w, w.StudentA, w.SickId);

            Assert.Equal(HttpStatusCode.NoContent, (await Put(w.Teacher, TeacherJournal, w, w.StudentA, reasonId: w.TruantId)).StatusCode);
            Assert.Equal((w.TruantId, (int?)null, 0), await CellAsync(w, w.StudentA));

            Assert.Equal(HttpStatusCode.NoContent, (await Put(w.Teacher, TeacherJournal, w, w.StudentA, grade: 5)).StatusCode);
            Assert.Equal(((string?)null, (int?)5, 0), await CellAsync(w, w.StudentA));

            Assert.Equal(HttpStatusCode.NoContent, (await Put(w.Teacher, TeacherJournal, w, w.StudentB, reasonId: w.SickId)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await Clear(w.Teacher, w, w.StudentB)).StatusCode);
            Assert.Null(await CellOrNullAsync(w, w.StudentB));
        });

    [Fact]
    public async Task Teacher_of_another_subject_is_still_refused() =>
        await SchoolMetaFlags.WithAsync(fixture.Api, m => m.TeachersMarkAttendance = false, async () =>
        {
            var w = await SeedAsync();
            var (_, stranger) = await TeacherAsync(TeacherPermissions.Journal);
            Assert.Equal(HttpStatusCode.Forbidden, (await Put(stranger, TeacherJournal, w, w.StudentA, grade: 3)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await Clear(stranger, w, w.StudentA)).StatusCode);
        });

    // =====================================================================
    //  Helpers
    // =====================================================================

    private sealed record World(
        string ClassId, string SubjectId, string StudentA, string StudentB, string SickId, string TruantId,
        HttpClient Teacher);

    private async Task<World> SeedAsync()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var (teacherId, teacher) = await TeacherAsync(TeacherPermissions.Journal);
        var cls = new SchoolClass { Name = $"TA-{tag}", Grade = 7 };
        var subject = new Subject { Name = $"Tarix {tag}" };
        var sick = new AbsenceReason { Name = $"Kasal {tag}", Short = "K" };
        var truant = new AbsenceReason { Name = $"Sababsiz {tag}", Short = "S" };
        var a = GeneralSettingsFlagsTests.NewStudent($"Hamshira belgisi {tag}", cls.Name, "+998900000081");
        var b = GeneralSettingsFlagsTests.NewStudent($"Belgisiz {tag}", cls.Name, "+998900000082");

        await fixture.Api.WithDbAsync(async db =>
        {
            db.Classes.Add(cls);
            db.Subjects.Add(subject);
            db.AbsenceReasons.AddRange(sick, truant);
            db.Students.AddRange(a, b);
            var tpl = new ScheduleTemplate { ClassId = cls.Id, Name = "Asosiy " + tag };
            tpl.Lessons.Add(new ScheduleLesson { TemplateId = tpl.Id, Day = 1, Period = 1, SubjectId = subject.Id, TeacherId = teacherId });
            db.ScheduleTemplates.Add(tpl);
            await db.SaveChangesAsync();
        });
        return new World(cls.Id, subject.Id, a.Id, b.Id, sick.Id, truant.Id, teacher);
    }

    private async Task<(string TeacherId, HttpClient Client)> TeacherAsync(params string[] permissions)
    {
        var (user, _) = await fixture.Api.SeedUserAsync(Roles.Teacher);
        var teacherId = "";
        await fixture.Api.WithDbAsync(async db =>
        {
            var t = await db.Teachers.SingleAsync(x => x.UserId == user.Id);
            t.Permissions = [.. permissions];
            await db.SaveChangesAsync();
            teacherId = t.Id;
        });
        return (teacherId, fixture.Api.ClientWithToken(
            fixture.Api.TokenFor(Roles.Teacher, user.Id, user.FullName, user.Email)));
    }

    /// <summary>The nurse's mark — written straight into the cell, as DailyAttendanceService does.</summary>
    private Task NurseMarksAsync(World w, string studentId, string reasonId) =>
        fixture.Api.WithDbAsync(async db =>
        {
            db.JournalEntries.Add(new JournalEntry
            {
                ClassId = w.ClassId, SubjectId = w.SubjectId, Quarter = 1, StudentId = studentId,
                Date = Date, Period = 1, OwnerKind = LessonOwnerKind.Class, ReasonId = reasonId,
            });
            await db.SaveChangesAsync();
        });

    private static Task<HttpResponseMessage> Put(
        HttpClient client, string url, World w, string studentId,
        int? grade = null, string? reasonId = null, int homework = 0) =>
        client.PutAsJsonAsync(url, new
        {
            classId = w.ClassId, subjectId = w.SubjectId, quarter = 1, studentId, date = Date, period = 1,
            grade, reasonId, homework, behavior = 0, mastery = (int?)null,
        });

    private static Task<HttpResponseMessage> Clear(HttpClient client, World w, string studentId) =>
        client.DeleteAsync(
            $"{TeacherJournal}?classId={w.ClassId}&subjectId={w.SubjectId}&quarter=1&studentId={studentId}&date={Date}&period=1");

    private async Task<(string? ReasonId, int? Grade, int Homework)> CellAsync(World w, string studentId) =>
        await CellOrNullAsync(w, studentId) ?? throw new Xunit.Sdk.XunitException("The journal cell is missing.");

    private async Task<(string? ReasonId, int? Grade, int Homework)?> CellOrNullAsync(World w, string studentId)
    {
        (string?, int?, int)? cell = null;
        await fixture.Api.WithDbAsync(async db =>
        {
            var rows = await db.JournalEntries.AsNoTracking()
                .Where(e => e.ClassId == w.ClassId && e.StudentId == studentId && e.Date == Date && e.Period == 1)
                .ToListAsync();
            Assert.True(rows.Count <= 1, "More than one cell for one pupil and lesson.");
            if (rows.Count == 1) cell = (rows[0].ReasonId, rows[0].Grade, rows[0].Homework);
        });
        return cell;
    }

    private static object Body(bool teachersMark) => new
    {
        archiveOnlyNonDebtorStudents = true, makeAttendanceReasonRequired = false,
        isStudentGradeRequired = false, showLearningProgressInParentDashboard = true,
        teachersMarkAttendance = teachersMark,
    };

    private static async Task<JsonDocument> JsonAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.True(response.IsSuccessStatusCode,
            $"{url}: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync()}");
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }
}
