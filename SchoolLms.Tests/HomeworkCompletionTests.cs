using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

// ===========================================================================
//  "Uy vazifasi bajarilishi" — homework statuses 0..5 and the pupil × lesson sheet
//  (docs/modules/homework-completion.md, client 2026-10-15).
// ===========================================================================
//
//  World: one class, one subject on the timetable (Tue 1st lesson, Thu 2nd lesson) assigned to week 2
//  of quarter 1 (2026-09-01 … 2026-10-31) → lessons 2026-09-08 p1 and 2026-09-10 p2. A second class
//  whose subject another teacher teaches, for the ownership checks.
//
//  The quarter row is shared by the whole run, so every test that needs it runs inside
//  JournalServiceTests.WithQuarterAsync, which restores the original rows afterwards.
// ===========================================================================

[Collection(SchoolLmsCollection.Name)]
public class HomeworkCompletionTests(ApiFixture fixture)
{
    private const string AdminSheet = "/api/admin/journal/homework";
    private const string TeacherSheet = "/api/teacher/journal/homework";
    private const string AdminJournal = "/api/admin/journal";
    private const string TeacherJournal = "/api/teacher/journal";

    private const string Tue = "2026-09-08";
    private const string Thu = "2026-09-10";
    private const string From = "2026-09-07";
    private const string To = "2026-09-13";

    // =====================================================================
    //  1. Values: 0..5 accepted, anything else rejected — on every write path
    // =====================================================================

    [Fact]
    public async Task Journal_cell_accepts_0_to_5_and_rejects_6_admin_and_teacher()
    {
        var w = await SeedAsync(students: 1);
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var s = w.StudentIds[0];

        for (var status = 0; status <= 5; status++)
        {
            var ok = await PutCell(admin, AdminJournal, w, s, Tue, 1, homework: status);
            Assert.True(ok.StatusCode == HttpStatusCode.NoContent, $"admin {status}: {await ok.Content.ReadAsStringAsync()}");
            var teacherOk = await PutCell(w.TeacherClient, TeacherJournal, w, s, Tue, 1, homework: status);
            Assert.True(teacherOk.StatusCode == HttpStatusCode.NoContent, $"teacher {status}: {await teacherOk.Content.ReadAsStringAsync()}");
        }

        foreach (var bad in new[] { 6, -1 })
        {
            var adminBad = await PutCell(admin, AdminJournal, w, s, Thu, 2, homework: bad);
            Assert.Equal(HttpStatusCode.BadRequest, adminBad.StatusCode);
            Assert.Contains("Uy vazifasi holati", await adminBad.Content.ReadAsStringAsync());
            Assert.Equal(HttpStatusCode.BadRequest,
                (await PutCell(w.TeacherClient, TeacherJournal, w, s, Thu, 2, homework: bad)).StatusCode);
        }

        // The rejected writes left nothing behind.
        await fixture.Api.WithDbAsync(async db =>
            Assert.Empty(await db.JournalEntries.AsNoTracking()
                .Where(e => e.ClassId == w.ClassId && e.Date == Thu).ToListAsync()));
    }

    [Fact]
    public async Task Bulk_write_accepts_0_to_5_and_rejects_6_admin_and_teacher() =>
        await JournalServiceTests.WithQuarterAsync(fixture.Api, async () =>
        {
            var w = await SeedAsync(students: 1);
            using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
            var s = w.StudentIds[0];

            for (var status = 0; status <= 5; status++)
            {
                Assert.Equal(HttpStatusCode.NoContent, (await PutMarks(admin, AdminSheet, w, (s, Tue, 1, status))).StatusCode);
                Assert.Equal(HttpStatusCode.NoContent, (await PutMarks(w.TeacherClient, TeacherSheet, w, (s, Tue, 1, status))).StatusCode);
            }

            // One bad mark rejects the whole request — the good one is not written either.
            foreach (var client in new[] { (admin, AdminSheet), (w.TeacherClient, TeacherSheet) })
            {
                var bad = await PutMarks(client.Item1, client.Item2, w, (s, Thu, 2, 1), (s, Tue, 1, 6));
                Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
                Assert.Contains("Uy vazifasi holati", await bad.Content.ReadAsStringAsync());
            }
            await fixture.Api.WithDbAsync(async db =>
                Assert.Empty(await db.JournalEntries.AsNoTracking()
                    .Where(e => e.ClassId == w.ClassId && e.Date == Thu).ToListAsync()));
        });

    /// <summary>1 and 2 were written long before this feature — they still mean done / not done at all.</summary>
    [Fact]
    public async Task Old_values_1_and_2_keep_their_meaning() =>
        await JournalServiceTests.WithQuarterAsync(fixture.Api, async () =>
        {
            var w = await SeedAsync(students: 2);
            using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
            var (a, b) = (w.StudentIds[0], w.StudentIds[1]);

            Assert.Equal(HttpStatusCode.NoContent, (await PutCell(admin, AdminJournal, w, a, Tue, 1, homework: 1)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await PutCell(admin, AdminJournal, w, b, Tue, 1, homework: 2)).StatusCode);

            var entries = await ArrayAsync(admin, $"{AdminJournal}?classId={w.ClassId}&subjectId={w.SubjectId}&quarter=1");
            Assert.Equal(1, entries.Single(e => e.GetProperty("studentId").GetString() == a).GetProperty("homework").GetInt32());
            Assert.Equal(2, entries.Single(e => e.GetProperty("studentId").GetString() == b).GetProperty("homework").GetInt32());

            var sheet = await SheetAsync(admin, AdminSheet, w);
            var rowA = Row(sheet, a);
            var rowB = Row(sheet, b);
            Assert.Equal(1, rowA.GetProperty("summary").GetProperty("done").GetInt32());
            Assert.Equal(1, rowB.GetProperty("summary").GetProperty("notDone").GetInt32());

            // Profile: the old fields count only 1 and 2.
            var profile = await ObjectAsync(admin, $"/api/admin/students/{a}/profile");
            Assert.Equal(1, profile.GetProperty("homeworkDone").GetInt32());
            Assert.Equal(0, profile.GetProperty("homeworkMissed").GetInt32());
        });

    // =====================================================================
    //  2. The sheet: columns, rows, derived Kelmadi, tallies
    // =====================================================================

    [Fact]
    public async Task Sheet_returns_lessons_rows_derived_absent_and_percentages() =>
        await JournalServiceTests.WithQuarterAsync(fixture.Api, async () =>
        {
            var w = await SeedAsync(students: 3);
            using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
            var (a, b, c) = (w.StudentIds[0], w.StudentIds[1], w.StudentIds[2]);

            await fixture.Api.WithDbAsync(async db =>
            {
                db.LessonNotes.Add(new LessonNote
                {
                    ClassId = w.ClassId, SubjectId = w.SubjectId, Quarter = 1, Date = Tue, Period = 1,
                    Topic = "Ot so'z turkumi", Homework = "45-bet, 3-mashq",
                });
                await db.SaveChangesAsync();
            });
            // C was absent on Tuesday (attendance only, no homework mark) → Kelmadi, derived.
            Assert.Equal(HttpStatusCode.NoContent,
                (await PutCell(admin, AdminJournal, w, c, Tue, 1, reasonId: w.AbsentReasonId)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await PutMarks(admin, AdminSheet, w,
                (a, Tue, 1, HomeworkStatus.Done), (a, Thu, 2, HomeworkStatus.Partial),
                (b, Tue, 1, HomeworkStatus.Late), (b, Thu, 2, HomeworkStatus.NotDone),
                (c, Thu, 2, HomeworkStatus.Absent))).StatusCode);

            var sheet = await SheetAsync(admin, AdminSheet, w);
            Assert.Equal(From, sheet.GetProperty("from").GetString());
            Assert.Equal(To, sheet.GetProperty("to").GetString());

            var lessons = sheet.GetProperty("lessons").EnumerateArray().ToList();
            Assert.Equal(2, lessons.Count);
            Assert.Equal(Tue, lessons[0].GetProperty("date").GetString());
            Assert.Equal(1, lessons[0].GetProperty("period").GetInt32());
            Assert.True(lessons[0].GetProperty("hasHomework").GetBoolean());
            Assert.Equal("45-bet, 3-mashq", lessons[0].GetProperty("homework").GetString());
            Assert.Equal(Thu, lessons[1].GetProperty("date").GetString());
            Assert.False(lessons[1].GetProperty("hasHomework").GetBoolean());

            // Rows: the class roster, cells aligned with the lessons.
            Assert.Equal(3, sheet.GetProperty("rows").GetArrayLength());
            var rowC = Row(sheet, c);
            var cellsC = rowC.GetProperty("cells").EnumerateArray().ToList();
            Assert.Equal(HomeworkStatus.Absent, cellsC[0].GetProperty("status").GetInt32());
            Assert.True(cellsC[0].GetProperty("derived").GetBoolean());
            Assert.Equal(HomeworkStatus.Absent, cellsC[1].GetProperty("status").GetInt32());
            Assert.False(cellsC[1].GetProperty("derived").GetBoolean());

            // Per pupil: (done + ½ partial + ½ late) ÷ marked; Kelmadi stays out of the denominator.
            AssertTally(Row(sheet, a).GetProperty("summary"), done: 1, partial: 1, notDone: 0, late: 0, absent: 0, marked: 2, percent: 75m);
            AssertTally(Row(sheet, b).GetProperty("summary"), done: 0, partial: 0, notDone: 1, late: 1, absent: 0, marked: 2, percent: 25m);
            AssertTally(rowC.GetProperty("summary"), done: 0, partial: 0, notDone: 0, late: 0, absent: 2, marked: 0, percent: null);

            // Per lesson and in total.
            AssertTally(lessons[0].GetProperty("summary"), done: 1, partial: 0, notDone: 0, late: 1, absent: 1, marked: 2, percent: 75m);
            AssertTally(lessons[1].GetProperty("summary"), done: 0, partial: 1, notDone: 1, late: 0, absent: 1, marked: 2, percent: 25m);
            AssertTally(sheet.GetProperty("total"), done: 1, partial: 1, notDone: 1, late: 1, absent: 2, marked: 4, percent: 50m);

            // The derived Kelmadi was never written.
            await fixture.Api.WithDbAsync(async db =>
            {
                var cell = await db.JournalEntries.AsNoTracking()
                    .SingleAsync(e => e.ClassId == w.ClassId && e.StudentId == c && e.Date == Tue);
                Assert.Equal(HomeworkStatus.None, cell.Homework);
            });

            // Student profile: the new statuses are counted next to the old two (and per quarter).
            var profileB = await ObjectAsync(admin, $"/api/admin/students/{b}/profile");
            Assert.Equal((0, 1, 0, 1, 0), (
                profileB.GetProperty("homeworkDone").GetInt32(), profileB.GetProperty("homeworkMissed").GetInt32(),
                profileB.GetProperty("homeworkPartial").GetInt32(), profileB.GetProperty("homeworkLate").GetInt32(),
                profileB.GetProperty("homeworkAbsent").GetInt32()));
            var q1 = profileB.GetProperty("marksTrend").EnumerateArray().Single(m => m.GetProperty("quarter").GetInt32() == 1);
            Assert.Equal(1, q1.GetProperty("homeworkLate").GetInt32());
            var profileC = await ObjectAsync(admin, $"/api/admin/students/{c}/profile");
            Assert.Equal(1, profileC.GetProperty("homeworkAbsent").GetInt32()); // the stored one only
            var profileA = await ObjectAsync(admin, $"/api/admin/students/{a}/profile");
            Assert.Equal(1, profileA.GetProperty("homeworkPartial").GetInt32());
        });

    /// <summary>A lateness reason means the pupil was there — no derived Kelmadi.</summary>
    [Fact]
    public async Task Late_reason_is_not_shown_as_absent() =>
        await JournalServiceTests.WithQuarterAsync(fixture.Api, async () =>
        {
            var w = await SeedAsync(students: 1);
            using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
            var s = w.StudentIds[0];
            Assert.Equal(HttpStatusCode.NoContent,
                (await PutCell(admin, AdminJournal, w, s, Tue, 1, reasonId: w.LateReasonId)).StatusCode);

            var cell = Row(await SheetAsync(admin, AdminSheet, w), s).GetProperty("cells")[0];
            Assert.Equal(HomeworkStatus.None, cell.GetProperty("status").GetInt32());
            Assert.False(cell.GetProperty("derived").GetBoolean());
        });

    [Fact]
    public async Task Sheet_range_and_owner_are_validated() =>
        await JournalServiceTests.WithQuarterAsync(fixture.Api, async () =>
        {
            var w = await SeedAsync(students: 1);
            using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
            string Url(string from, string to, string? owner = null) =>
                $"{AdminSheet}?ownerId={owner ?? w.ClassId}&subjectId={w.SubjectId}&from={from}&to={to}";

            Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync(Url(To, From))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync(Url("2026-01-01", "2026-09-13"))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync(Url("08.09.2026", To))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync(Url(From, To, owner: "no-such-class"))).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,
                (await admin.GetAsync(Url(From, To) + "&ownerKind=group")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(Url(From, To) + "&ownerKind=class")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound,
                (await PutMarks(admin, AdminSheet, w, "no-such-class", w.SubjectId, (w.StudentIds[0], Tue, 1, 1))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest,
                (await PutMarks(admin, AdminSheet, w, w.ClassId, "no-such-subject", (w.StudentIds[0], Tue, 1, 1))).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await PutMarks(admin, AdminSheet, w)).StatusCode); // no marks

            // The export is the same table as .xlsx.
            var xlsx = await admin.GetAsync(Url(From, To).Replace(AdminSheet, AdminSheet + "/export"));
            Assert.Equal(HttpStatusCode.OK, xlsx.StatusCode);
            Assert.Equal("application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                xlsx.Content.Headers.ContentType?.MediaType);
        });

    // =====================================================================
    //  3. Bulk write: creates, updates, leaves the rest of the cell alone
    // =====================================================================

    [Fact]
    public async Task Bulk_write_creates_and_updates_without_touching_grade_or_reason() =>
        await JournalServiceTests.WithQuarterAsync(fixture.Api, async () =>
        {
            var w = await SeedAsync(students: 3);
            using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
            var (graded, late, fresh) = (w.StudentIds[0], w.StudentIds[1], w.StudentIds[2]);

            Assert.Equal(HttpStatusCode.NoContent,
                (await PutCell(admin, AdminJournal, w, graded, Tue, 1, grade: 5, behavior: 1, mastery: 80)).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent,
                (await PutCell(admin, AdminJournal, w, late, Tue, 1, grade: 4, reasonId: w.LateReasonId)).StatusCode);

            Assert.Equal(HttpStatusCode.NoContent, (await PutMarks(admin, AdminSheet, w,
                (graded, Tue, 1, HomeworkStatus.Partial),
                (late, Tue, 1, HomeworkStatus.Late),
                (fresh, Thu, 2, HomeworkStatus.Done))).StatusCode);

            await fixture.Api.WithDbAsync(async db =>
            {
                var rows = await db.JournalEntries.AsNoTracking()
                    .Where(e => e.ClassId == w.ClassId && e.SubjectId == w.SubjectId).ToListAsync();
                var g = rows.Single(e => e.StudentId == graded);
                Assert.Equal((5, (string?)null, HomeworkStatus.Partial, 1, (int?)80), (g.Grade!.Value, g.ReasonId, g.Homework, g.Behavior, g.Mastery));
                var l = rows.Single(e => e.StudentId == late);
                Assert.Equal((4, w.LateReasonId, HomeworkStatus.Late), (l.Grade!.Value, l.ReasonId, l.Homework));
                var f = rows.Single(e => e.StudentId == fresh);
                Assert.Equal((Thu, 2, 1, HomeworkStatus.Done, (int?)null, (string?)null),
                    (f.Date, f.Period, f.Quarter, f.Homework, f.Grade, f.ReasonId));
                Assert.Equal(LessonOwnerKind.Class, f.OwnerKind);

                // A filled cell marks the lesson "o'tildi" — as a cell saved in the journal does.
                Assert.True(await db.LessonNotes.AsNoTracking().AnyAsync(n =>
                    n.ClassId == w.ClassId && n.SubjectId == w.SubjectId && n.Date == Thu && n.Period == 2 && n.Conducted));
            });

            // Clearing: a homework-only cell disappears, a graded cell keeps its grade.
            Assert.Equal(HttpStatusCode.NoContent, (await PutMarks(admin, AdminSheet, w,
                (graded, Tue, 1, HomeworkStatus.None), (fresh, Thu, 2, HomeworkStatus.None))).StatusCode);
            await fixture.Api.WithDbAsync(async db =>
            {
                var rows = await db.JournalEntries.AsNoTracking()
                    .Where(e => e.ClassId == w.ClassId && e.SubjectId == w.SubjectId).ToListAsync();
                Assert.DoesNotContain(rows, e => e.StudentId == fresh);
                var g = rows.Single(e => e.StudentId == graded);
                Assert.Equal((5, HomeworkStatus.None), (g.Grade!.Value, g.Homework));
            });
        });

    // =====================================================================
    //  4. The journal's guards
    // =====================================================================

    [Fact]
    public async Task Bulk_write_respects_archive_roster_and_future_date() =>
        await JournalServiceTests.WithQuarterAsync(fixture.Api, async () =>
        {
            var w = await SeedAsync(students: 2);
            using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
            var (active, archived) = (w.StudentIds[0], w.StudentIds[1]);
            await fixture.Api.WithDbAsync(async db =>
            {
                var s = await db.Students.SingleAsync(x => x.Id == archived);
                s.IsArchived = true;
                await db.SaveChangesAsync();
            });

            var archivedResponse = await PutMarks(admin, AdminSheet, w, (archived, Tue, 1, HomeworkStatus.Done));
            Assert.Equal(HttpStatusCode.BadRequest, archivedResponse.StatusCode);
            Assert.Contains("arxivda", await archivedResponse.Content.ReadAsStringAsync());

            // A pupil of another class is not on this roster.
            Assert.Equal(HttpStatusCode.BadRequest,
                (await PutMarks(admin, AdminSheet, w, (w.OtherStudentId, Tue, 1, HomeworkStatus.Done))).StatusCode);

            // A teacher cannot mark a lesson that has not happened yet (the teacher journal's rule).
            var future = AppClock.Today.AddDays(7).ToString("yyyy-MM-dd");
            var futureResponse = await PutMarks(w.TeacherClient, TeacherSheet, w, (active, future, 1, HomeworkStatus.Done));
            Assert.Equal(HttpStatusCode.BadRequest, futureResponse.StatusCode);
            Assert.Contains("kelajakdagi", await futureResponse.Content.ReadAsStringAsync());

            await fixture.Api.WithDbAsync(async db =>
                Assert.Empty(await db.JournalEntries.AsNoTracking().Where(e => e.ClassId == w.ClassId).ToListAsync()));
        });

    /// <summary>
    /// With "baho majburiy" on, a homework mark does not close the lesson while a present pupil has
    /// no grade — the journal's automatic path waits the same way.
    /// </summary>
    [Fact]
    public async Task Grade_required_flag_delays_the_automatic_conducted_mark() =>
        await JournalServiceTests.WithQuarterAsync(fixture.Api, () =>
            SchoolMetaFlags.WithAsync(fixture.Api, m => m.IsStudentGradeRequired = true, async () =>
            {
                var w = await SeedAsync(students: 1);
                using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
                var s = w.StudentIds[0];

                Assert.Equal(HttpStatusCode.NoContent, (await PutMarks(admin, AdminSheet, w, (s, Tue, 1, HomeworkStatus.Done))).StatusCode);
                await fixture.Api.WithDbAsync(async db =>
                    Assert.False(await db.LessonNotes.AsNoTracking().AnyAsync(n =>
                        n.ClassId == w.ClassId && n.Date == Tue && n.Period == 1 && n.Conducted)));

                Assert.Equal(HttpStatusCode.NoContent, (await PutCell(admin, AdminJournal, w, s, Tue, 1, grade: 5, homework: 1)).StatusCode);
                await fixture.Api.WithDbAsync(async db =>
                    Assert.True(await db.LessonNotes.AsNoTracking().AnyAsync(n =>
                        n.ClassId == w.ClassId && n.Date == Tue && n.Period == 1 && n.Conducted)));
            }));

    // =====================================================================
    //  5. Access: anonymous, staff without the key, teacher ownership
    // =====================================================================

    [Fact]
    public async Task Anonymous_gets_401_everywhere()
    {
        using var client = fixture.Api.AnonymousClient();
        foreach (var url in new[] { AdminSheet, TeacherSheet })
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"{url}?ownerId=x&subjectId=y")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.PutAsJsonAsync(url, new
            {
                ownerId = "x", subjectId = "y", marks = Array.Empty<object>(),
            })).StatusCode);
        }
    }

    /// <summary>
    /// Admin endpoints: a teacher is refused outright; staff may read (AdminPerm's open read) but only the
    /// `journal` key writes.
    /// </summary>
    [Fact]
    public async Task Admin_endpoints_need_the_journal_key_to_write_and_refuse_teachers() =>
        await JournalServiceTests.WithQuarterAsync(fixture.Api, async () =>
        {
            var w = await SeedAsync(students: 1);
            var s = w.StudentIds[0];

            Assert.Equal(HttpStatusCode.Forbidden, (await w.TeacherClient.GetAsync(SheetUrl(AdminSheet, w))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await PutMarks(w.TeacherClient, AdminSheet, w, (s, Tue, 1, 1))).StatusCode);

            using var plain = await fixture.Api.ClientAsAsync(Roles.Staff, "students");
            Assert.Equal(HttpStatusCode.OK, (await plain.GetAsync(SheetUrl(AdminSheet, w))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await PutMarks(plain, AdminSheet, w, (s, Tue, 1, 1))).StatusCode);

            using var keyed = await fixture.Api.ClientAsAsync(Roles.Staff, "journal");
            Assert.Equal(HttpStatusCode.NoContent, (await PutMarks(keyed, AdminSheet, w, (s, Tue, 1, 1))).StatusCode);
        });

    [Fact]
    public async Task Teacher_reads_and_writes_only_own_class_and_subject() =>
        await JournalServiceTests.WithQuarterAsync(fixture.Api, async () =>
        {
            var w = await SeedAsync(students: 1);
            var s = w.StudentIds[0];
            var teacher = w.TeacherClient;

            Assert.Equal(HttpStatusCode.OK, (await teacher.GetAsync(SheetUrl(TeacherSheet, w))).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await PutMarks(teacher, TeacherSheet, w, (s, Tue, 1, 1))).StatusCode);
            var xlsx = await teacher.GetAsync(SheetUrl(TeacherSheet + "/export", w));
            Assert.Equal(HttpStatusCode.OK, xlsx.StatusCode);

            foreach (var (ownerId, subjectId, why) in new[]
                     {
                         (w.ClassId, w.OtherSubjectId, "own class, foreign subject"),
                         (w.OtherClassId, w.SubjectId, "foreign class"),
                     })
            {
                Assert.True(HttpStatusCode.Forbidden ==
                    (await teacher.GetAsync(SheetUrl(TeacherSheet, w, ownerId, subjectId))).StatusCode, why);
                Assert.True(HttpStatusCode.Forbidden ==
                    (await teacher.GetAsync(SheetUrl(TeacherSheet + "/export", w, ownerId, subjectId))).StatusCode, why);
                Assert.True(HttpStatusCode.Forbidden ==
                    (await PutMarks(teacher, TeacherSheet, w, ownerId, subjectId, (s, Tue, 1, 1))).StatusCode, why);
            }

            // Without the journal permission even a class + subject the teacher does teach is closed.
            var (noJournalId, noJournal) = await TeacherClientAsync(TeacherPermissions.Schedule);
            await fixture.Api.WithDbAsync(async db =>
            {
                var tpl = await db.ScheduleTemplates.SingleAsync(t => t.ClassId == w.ClassId);
                db.Set<ScheduleLesson>().Add(new ScheduleLesson
                {
                    TemplateId = tpl.Id, Day = 4, Period = 4, SubjectId = w.SubjectId, TeacherId = noJournalId,
                });
                await db.SaveChangesAsync();
            });
            Assert.Equal(HttpStatusCode.Forbidden, (await noJournal.GetAsync(SheetUrl(TeacherSheet, w))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await PutMarks(noJournal, TeacherSheet, w, (s, Tue, 1, 1))).StatusCode);
        });

    // =====================================================================
    //  Helpers
    // =====================================================================

    private sealed record World(
        string Tag, string ClassId, string SubjectId, string OtherSubjectId, List<string> StudentIds,
        string OtherClassId, string OtherStudentId, string AbsentReasonId, string LateReasonId,
        HttpClient TeacherClient);

    private async Task<World> SeedAsync(int students)
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var (teacherId, teacherClient) = await TeacherClientAsync(TeacherPermissions.Journal);
        var cls = new SchoolClass { Name = $"HW-{tag}", Grade = 6 };
        var otherCls = new SchoolClass { Name = $"HW-B-{tag}", Grade = 6 };
        var subject = new Subject { Name = $"Ona tili {tag}" };
        var otherSubject = new Subject { Name = $"Fizika {tag}" };
        var otherTeacher = new Teacher { FullName = $"Boshqa o'qituvchi {tag}" };
        var absent = new AbsenceReason { Name = $"Kelmadi {tag}", Short = "K", IsLate = false };
        var late = new AbsenceReason { Name = $"Kechikdi {tag}", Short = "Kch", IsLate = true };
        var ids = new List<string>();
        var otherStudent = GeneralSettingsFlagsTests.NewStudent($"Boshqa {tag}", otherCls.Name, "+998900000071");

        await fixture.Api.WithDbAsync(async db =>
        {
            db.Classes.AddRange(cls, otherCls);
            db.Subjects.AddRange(subject, otherSubject);
            db.Teachers.Add(otherTeacher);
            db.AbsenceReasons.AddRange(absent, late);
            for (var i = 0; i < students; i++)
            {
                var s = GeneralSettingsFlagsTests.NewStudent($"Vazifa {i} {tag}", cls.Name, "+998900000070");
                db.Students.Add(s);
                ids.Add(s.Id);
            }
            db.Students.Add(otherStudent);

            // Own class: the teacher's subject on Tuesday (1st) and Thursday (2nd), in week 2.
            var tpl = new ScheduleTemplate { ClassId = cls.Id, Name = "Asosiy " + tag };
            tpl.Lessons.Add(new ScheduleLesson { TemplateId = tpl.Id, Day = 1, Period = 1, SubjectId = subject.Id, TeacherId = teacherId });
            tpl.Lessons.Add(new ScheduleLesson { TemplateId = tpl.Id, Day = 3, Period = 2, SubjectId = subject.Id, TeacherId = teacherId });
            // The other subject of this class belongs to someone else.
            tpl.Lessons.Add(new ScheduleLesson { TemplateId = tpl.Id, Day = 2, Period = 3, SubjectId = otherSubject.Id, TeacherId = otherTeacher.Id });
            db.ScheduleTemplates.Add(tpl);
            db.WeekAssignments.Add(new WeekAssignment { ClassId = cls.Id, Quarter = 1, Week = 2, TemplateId = tpl.Id });

            // The other class: same subject, another teacher.
            var otherTpl = new ScheduleTemplate { ClassId = otherCls.Id, Name = "B " + tag };
            otherTpl.Lessons.Add(new ScheduleLesson { TemplateId = otherTpl.Id, Day = 1, Period = 1, SubjectId = subject.Id, TeacherId = otherTeacher.Id });
            db.ScheduleTemplates.Add(otherTpl);
            db.WeekAssignments.Add(new WeekAssignment { ClassId = otherCls.Id, Quarter = 1, Week = 2, TemplateId = otherTpl.Id });

            await db.SaveChangesAsync();
        });

        return new World(tag, cls.Id, subject.Id, otherSubject.Id, ids, otherCls.Id, otherStudent.Id,
            absent.Id, late.Id, teacherClient);
    }

    private async Task<(string TeacherId, HttpClient Client)> TeacherClientAsync(params string[] permissions)
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

    private static string SheetUrl(string url, World w, string? ownerId = null, string? subjectId = null) =>
        $"{url}?ownerId={ownerId ?? w.ClassId}&subjectId={subjectId ?? w.SubjectId}&from={From}&to={To}";

    private static Task<HttpResponseMessage> PutCell(
        HttpClient client, string url, World w, string studentId, string date, int period,
        int? grade = null, string? reasonId = null, int homework = 0, int behavior = 0, int? mastery = null) =>
        client.PutAsJsonAsync(url, new
        {
            classId = w.ClassId, subjectId = w.SubjectId, quarter = 1, studentId,
            date, period, grade, reasonId, homework, behavior, mastery,
        });

    private static Task<HttpResponseMessage> PutMarks(
        HttpClient client, string url, World w, params (string StudentId, string Date, int Period, int Status)[] marks) =>
        PutMarks(client, url, w, w.ClassId, w.SubjectId, marks);

    private static Task<HttpResponseMessage> PutMarks(
        HttpClient client, string url, World w, string ownerId, string subjectId,
        params (string StudentId, string Date, int Period, int Status)[] marks) =>
        client.PutAsJsonAsync(url, new
        {
            ownerId, subjectId,
            marks = marks.Select(m => new { studentId = m.StudentId, date = m.Date, period = m.Period, subGroup = 0, status = m.Status }),
        });

    private static async Task<JsonElement> SheetAsync(HttpClient client, string url, World w) =>
        await ObjectAsync(client, SheetUrl(url, w));

    private static JsonElement Row(JsonElement sheet, string studentId) =>
        sheet.GetProperty("rows").EnumerateArray().Single(r => r.GetProperty("studentId").GetString() == studentId);

    private static void AssertTally(
        JsonElement t, int done, int partial, int notDone, int late, int absent, int marked, decimal? percent)
    {
        Assert.Equal(done, t.GetProperty("done").GetInt32());
        Assert.Equal(partial, t.GetProperty("partial").GetInt32());
        Assert.Equal(notDone, t.GetProperty("notDone").GetInt32());
        Assert.Equal(late, t.GetProperty("late").GetInt32());
        Assert.Equal(absent, t.GetProperty("absent").GetInt32());
        Assert.Equal(marked, t.GetProperty("marked").GetInt32());
        var p = t.GetProperty("percent");
        if (percent is null) Assert.Equal(JsonValueKind.Null, p.ValueKind);
        else Assert.Equal(percent.Value, p.GetDecimal());
    }

    private static async Task<JsonElement> ObjectAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"{url} → {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.Clone();
    }

    private static async Task<List<JsonElement>> ArrayAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"{url} → {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.EnumerateArray().Select(e => e.Clone()).ToList();
    }
}
