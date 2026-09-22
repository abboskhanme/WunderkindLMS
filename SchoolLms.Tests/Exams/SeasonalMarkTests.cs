using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests.Exams;

// ===========================================================================
//  Seasonal marks — unit B4 (docs/modules/admission-and-testing.md §6.6, §8.6,
//  §5.14, §12 "Behaviour" / "RBAC").
// ===========================================================================
//
//  WHAT IS PINNED HERE
//  -------------------
//    · entry: bulk create / update / delete, the grid reading it back;
//    · rules: 0–100 half-up (refused outside, not clamped), comment ≥ 3 code
//      points, period combinations, the "quarter not configured" soft check;
//    · audit: one row per change, entity type "SeasonalMark", Score and
//      Comment in the before/after snapshots — the history dialog's only input;
//    · teacher ownership: own (class, subject) only, own class's pupils only,
//      the section key, and no admin routes;
//    · reports: coverage numbers (slots, the 100 % cap, sub-groups, drill-down),
//      the pivot's rows, the list's wire shape, the three exports;
//    · §5.14 guard clauses on subject and class delete.
//
//  ISOLATION: every test seeds its own classes, subjects, pupils and teachers
//  with a unique tag, and every report query is filtered to them, so the
//  shared database's other rows never enter an assertion.
// ===========================================================================

[Collection(SchoolLmsCollection.Name)]
public class SeasonalMarkTests(ApiFixture fixture)
{
    private const string Admin = "/api/admin/seasonal-marks";
    private const string Teacher = "/api/teacher/seasonal-marks";

    private const int Year = 2026;
    private const int Month = 3;
    private const string Period = "periodKind=monthly&year=2026&month=3";

    // =====================================================================
    //  1. Entry — bulk upsert and the grid
    // =====================================================================

    [Fact]
    public async Task Bulk_creates_updates_and_deletes_and_the_grid_reads_it_back()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var first = await BulkAsync(admin, Admin, w.ClassId, w.SubjectId,
            Row(w.Pupils[0], 87.5m, "Yaxshi natija"),
            Row(w.Pupils[1], 90m, null),
            Row(w.Pupils[2], null, "Faqat izoh"));
        Assert.Equal(new SeasonalBulkResultDto(3, 0, 0), first);

        var grid = await GridAsync(admin, Admin, w.ClassId, w.SubjectId);
        Assert.Equal(w.Pupils, grid.Select(r => r.StudentId));
        Assert.Equal(87.5m, grid[0].Score);
        Assert.Equal("Yaxshi natija", grid[0].Comment);
        Assert.Null(grid[2].Score);
        Assert.All(grid, r => Assert.NotNull(r.MarkId));

        // Changed, identical, emptied: one update, nothing, one delete.
        var second = await BulkAsync(admin, Admin, w.ClassId, w.SubjectId,
            Row(w.Pupils[0], 91m, "Yaxshi natija"),
            Row(w.Pupils[1], 90m, null),
            Row(w.Pupils[2], null, "   "));
        Assert.Equal(new SeasonalBulkResultDto(0, 1, 1), second);

        grid = await GridAsync(admin, Admin, w.ClassId, w.SubjectId);
        Assert.Equal(91m, grid[0].Score);
        Assert.Null(grid[2].MarkId);
        Assert.Null(grid[2].Comment);

        // An empty row for a pupil with nothing stored is not a delete.
        Assert.Equal(new SeasonalBulkResultDto(0, 0, 0),
            await BulkAsync(admin, Admin, w.ClassId, w.SubjectId, Row(w.Pupils[2], null, null)));
    }

    [Fact]
    public async Task Every_change_writes_one_audit_row_with_score_and_comment_in_the_snapshots()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        await BulkAsync(admin, Admin, w.ClassId, w.SubjectId, Row(w.Pupils[0], 70m, "Boshlanishi"));
        var markId = (await GridAsync(admin, Admin, w.ClassId, w.SubjectId))[0].MarkId!;
        await BulkAsync(admin, Admin, w.ClassId, w.SubjectId, Row(w.Pupils[0], 85.25m, "O'sish bor"));
        await BulkAsync(admin, Admin, w.ClassId, w.SubjectId, Row(w.Pupils[0], null, null));

        List<AuditLog> logs = [];
        await fixture.Api.WithDbAsync(async db => logs = await db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityType == SeasonalMarkService.AuditEntity && a.EntityId == markId)
            .ToListAsync());

        Assert.Equal(["create", "delete", "update"], logs.Select(l => l.Action).Order());
        Assert.All(logs, l => Assert.Equal(w.Pupils[0], l.StudentId));
        Assert.Equal("SeasonalMark", SeasonalMarkService.AuditEntity);

        var created = logs.Single(l => l.Action == "create");
        Assert.Null(created.Before);
        Assert.Equal((70m, "Boshlanishi"), ScoreAndComment(created.After));

        var updated = logs.Single(l => l.Action == "update");
        Assert.Equal((70m, "Boshlanishi"), ScoreAndComment(updated.Before));
        Assert.Equal((85.25m, "O'sish bor"), ScoreAndComment(updated.After));

        var deleted = logs.Single(l => l.Action == "delete");
        Assert.Equal((85.25m, "O'sish bor"), ScoreAndComment(deleted.Before));
        Assert.Null(deleted.After);

        // The history dialog reads exactly this URL (MarkHistoryModal.tsx).
        using var history = JsonDocument.Parse(await admin.GetStringAsync(
            $"/api/admin/audit?entityType=SeasonalMark&entityId={markId}"));
        Assert.Equal(3, history.RootElement.GetArrayLength());
    }

    /// <summary>
    /// The upsert finds a stored mark through <see cref="SeasonalMark.BuildPeriodKey"/>,
    /// the C# twin of the generated <c>period_key</c>. If the two ever disagreed the
    /// second save would INSERT a duplicate (a 409) instead of updating. Pinned for
    /// all three kinds — until ExamMigrationTests (unit D4) exists, this is the check.
    /// </summary>
    [Theory]
    [InlineData("monthly", 3, null)]
    [InlineData("quarterly", null, 1)]
    [InlineData("yearly", null, null)]
    public async Task The_upsert_key_is_the_generated_period_key(string kind, int? month, int? quarter)
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var results = new List<SeasonalBulkResultDto>();
        await JournalServiceTests.WithQuarterAsync(fixture.Api, async () =>
        {
            foreach (var score in new[] { 60m, 70m })
            {
                var response = await admin.PostAsJsonAsync($"{Admin}/bulk",
                    Bulk(w.ClassId, w.SubjectId, kind, month, quarter, Row(w.Pupils[0], score, null)));
                Assert.True(response.StatusCode == HttpStatusCode.OK,
                    $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
                results.Add((await response.Content.ReadFromJsonAsync<SeasonalBulkResultDto>())!);
            }
        });

        Assert.Equal([new SeasonalBulkResultDto(1, 0, 0), new SeasonalBulkResultDto(0, 1, 0)], results);
        List<SeasonalMark> stored = [];
        await fixture.Api.WithDbAsync(async db => stored = await db.SeasonalMarks.AsNoTracking()
            .Where(m => m.StudentId == w.Pupils[0]).ToListAsync());
        var mark = Assert.Single(stored);
        Assert.Equal(SeasonalMark.BuildPeriodKey(kind, Year, month, quarter), mark.PeriodKey);
        Assert.Equal(70m, mark.Score);
    }

    // =====================================================================
    //  2. Rules — score, comment, period
    // =====================================================================

    [Theory]
    [InlineData("0", "0")]
    [InlineData("100", "100")]
    [InlineData("87.555", "87.56")]
    [InlineData("87.554", "87.55")]
    [InlineData("99.995", "100")]
    public async Task Score_is_rounded_half_up_to_two_decimals(string sent, string stored)
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        await BulkAsync(admin, Admin, w.ClassId, w.SubjectId, Row(w.Pupils[0], decimal.Parse(sent), null));

        var grid = await GridAsync(admin, Admin, w.ClassId, w.SubjectId);
        Assert.Equal(decimal.Parse(stored), grid[0].Score);
    }

    /// <summary>
    /// Outside 0..100 after rounding is REFUSED, not clamped (the page refuses
    /// too — "150 is more likely a typo"), and nothing of the request is saved.
    /// </summary>
    [Theory]
    [InlineData("100.4")]
    [InlineData("100.005")]
    [InlineData("150")]
    [InlineData("-0.01")]
    public async Task Score_outside_0_to_100_is_refused_and_nothing_is_saved(string sent)
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var response = await admin.PostAsJsonAsync($"{Admin}/bulk", Bulk(w.ClassId, w.SubjectId,
            Row(w.Pupils[0], 50m, null),
            Row(w.Pupils[1], decimal.Parse(sent), null)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var message = await MessageAsync(response);
        Assert.Contains("0 dan 100 gacha", message);
        Assert.Contains(w.PupilNames[1], message); // the grid can point at the row
        Assert.Equal(0, await MarkCountAsync(w));
    }

    [Theory]
    [InlineData("ab")]
    [InlineData("  a  ")]
    [InlineData("😀a")] // three UTF-16 units, two characters — PostgreSQL's char_length says 2
    public async Task Comment_of_one_or_two_characters_is_refused(string comment)
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var response = await admin.PostAsJsonAsync($"{Admin}/bulk",
            Bulk(w.ClassId, w.SubjectId, Row(w.Pupils[0], 60m, comment)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("kamida 3", await MessageAsync(response));
        Assert.Equal(0, await MarkCountAsync(w));
    }

    [Fact]
    public async Task Comment_is_trimmed_and_blank_means_none()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        await BulkAsync(admin, Admin, w.ClassId, w.SubjectId,
            Row(w.Pupils[0], 60m, "  abc  "),
            Row(w.Pupils[1], 61m, "   "));

        var grid = await GridAsync(admin, Admin, w.ClassId, w.SubjectId);
        Assert.Equal("abc", grid[0].Comment);
        Assert.Null(grid[1].Comment);
    }

    [Theory]
    [InlineData("periodKind=monthly&year=2026", "oyni")]
    [InlineData("periodKind=yearly&year=2026&month=3", "faqat oylik")]
    [InlineData("periodKind=monthly&year=1999&month=3", "2000 dan 2100")]
    [InlineData("periodKind=weekly&year=2026", "turi noto'g'ri")]
    [InlineData("periodKind=quarterly&year=2026&quarter=5", "chorakni (1–4)")]
    public async Task An_inconsistent_period_is_refused(string period, string expected)
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var response = await admin.GetAsync($"{Admin}/students?classId={w.ClassId}&subjectId={w.SubjectId}&{period}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(expected, await MessageAsync(response));
    }

    /// <summary>
    /// §8.6: a quarterly mark needs a configured quarter with that number (a soft
    /// check — the column is an int, not a FK). The page recognises the refusal
    /// by the words "chorak sozlanmagan".
    /// </summary>
    [Fact]
    public async Task Quarterly_mark_needs_a_configured_quarter()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        await JournalServiceTests.WithQuarterAsync(fixture.Api, async () =>
        {
            // Only quarter 1 exists inside this block.
            var refused = await admin.PostAsJsonAsync($"{Admin}/bulk", Bulk(w.ClassId, w.SubjectId,
                "quarterly", month: null, quarter: 3, Row(w.Pupils[0], 70m, null)));
            Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
            Assert.Contains("chorak sozlanmagan", await MessageAsync(refused));

            var saved = await admin.PostAsJsonAsync($"{Admin}/bulk", Bulk(w.ClassId, w.SubjectId,
                "quarterly", month: null, quarter: 1, Row(w.Pupils[0], 70m, null)));
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
        });

        string? key = null;
        await fixture.Api.WithDbAsync(async db => key = await db.SeasonalMarks.AsNoTracking()
            .Where(m => m.StudentId == w.Pupils[0]).Select(m => m.PeriodKey).SingleAsync());
        Assert.Equal("Q:2026-1", key);
    }

    // =====================================================================
    //  3. Teacher — own pairs, own pupils, the section key
    // =====================================================================

    [Fact]
    public async Task Teacher_scope_holds_only_the_pairs_the_teacher_teaches()
    {
        var w = await SeedAsync();

        var scope = (await w.Teacher.GetFromJsonAsync<SeasonalScopeDto>($"{Teacher}/scope"))!;

        var pair = Assert.Single(scope.Pairs);
        Assert.Equal((w.ClassId, w.SubjectId, w.TeacherId), (pair.ClassId, pair.SubjectId, pair.TeacherId));
        Assert.Equal([w.ClassId], scope.Classes.Select(c => c.Id));

        // The admin scope of the same class shows every teacher's pair there.
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var all = (await admin.GetFromJsonAsync<SeasonalScopeDto>($"{Admin}/scope?classId={w.ClassId}"))!;
        var expected = new[] { (w.SubjectId, w.TeacherId), (w.OtherSubjectId, w.OtherTeacherId) };
        Assert.Equal(expected.Order(), all.Pairs.Select(p => (p.SubjectId, p.TeacherId)).Order());
        Assert.Equal([w.ClassId], all.Classes.Select(c => c.Id));
    }

    [Fact]
    public async Task Teacher_enters_marks_for_an_own_pair()
    {
        var w = await SeedAsync();

        var grid = await GridAsync(w.Teacher, Teacher, w.ClassId, w.SubjectId);
        Assert.Equal(w.Pupils, grid.Select(r => r.StudentId));

        Assert.Equal(new SeasonalBulkResultDto(2, 0, 0), await BulkAsync(w.Teacher, Teacher, w.ClassId, w.SubjectId,
            Row(w.Pupils[0], 77m, null), Row(w.Pupils[1], 88m, "A'lo ish")));

        string? author = null;
        await fixture.Api.WithDbAsync(async db => author = await db.SeasonalMarks.AsNoTracking()
            .Where(m => m.StudentId == w.Pupils[0]).Select(m => m.CreatedByUserId).SingleAsync());
        Assert.Equal(w.TeacherUserId, author);
    }

    /// <summary>§12 RBAC: "A teacher cannot write a seasonal mark for a (class, subject) they do not teach."</summary>
    [Fact]
    public async Task Teacher_cannot_read_or_write_a_pair_they_do_not_teach()
    {
        var w = await SeedAsync();

        foreach (var (classId, subjectId, pupil, why) in new[]
                 {
                     (w.ClassId, w.OtherSubjectId, w.Pupils[0], "own class, another teacher's subject"),
                     (w.OtherClassId, w.SubjectId, w.OtherPupil, "own subject, another teacher's class"),
                 })
        {
            var read = await w.Teacher.GetAsync(
                $"{Teacher}/students?classId={classId}&subjectId={subjectId}&{Period}");
            Assert.True(read.StatusCode == HttpStatusCode.Forbidden, $"{why}: read {read.StatusCode}");

            var write = await w.Teacher.PostAsJsonAsync($"{Teacher}/bulk",
                Bulk(classId, subjectId, Row(pupil, 50m, null)));
            Assert.True(write.StatusCode == HttpStatusCode.Forbidden, $"{why}: write {write.StatusCode}");
        }

        Assert.Equal(0, await MarkCountAsync(w));
    }

    /// <summary>The own pair cannot be used to reach a pupil of another class.</summary>
    [Fact]
    public async Task Teacher_cannot_mark_a_pupil_of_another_class_through_an_own_pair()
    {
        var w = await SeedAsync();

        var response = await w.Teacher.PostAsJsonAsync($"{Teacher}/bulk",
            Bulk(w.ClassId, w.SubjectId, Row(w.OtherPupil, 50m, null)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, await MarkCountAsync(w));
    }

    [Fact]
    public async Task Teacher_without_the_section_key_is_refused()
    {
        var w = await SeedAsync(teacherPermissions: [TeacherPermissions.Journal]);

        Assert.Equal(HttpStatusCode.Forbidden, (await w.Teacher.GetAsync($"{Teacher}/scope")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Teacher.GetAsync(
            $"{Teacher}/students?classId={w.ClassId}&subjectId={w.SubjectId}&{Period}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Teacher.PostAsJsonAsync($"{Teacher}/bulk",
            Bulk(w.ClassId, w.SubjectId, Row(w.Pupils[0], 50m, null)))).StatusCode);
    }

    [Fact]
    public async Task Teacher_is_refused_on_the_admin_routes()
    {
        var w = await SeedAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await w.Teacher.GetAsync(Admin)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await w.Teacher.PostAsJsonAsync($"{Admin}/bulk",
            Bulk(w.ClassId, w.SubjectId, Row(w.Pupils[0], 50m, null)))).StatusCode);
    }

    // =====================================================================
    //  4. RBAC — admin routes
    // =====================================================================

    [Fact]
    public async Task Staff_reads_without_the_key_but_writes_only_with_it()
    {
        var w = await SeedAsync();
        var body = Bulk(w.ClassId, w.SubjectId, Row(w.Pupils[0], 50m, null));

        using var plain = await fixture.Api.ClientAsAsync(Roles.Staff, "students");
        Assert.Equal(HttpStatusCode.OK, (await plain.GetAsync($"{Admin}?studentId={w.Pupils[0]}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await plain.PostAsJsonAsync($"{Admin}/bulk", body)).StatusCode);

        using var keyed = await fixture.Api.ClientAsAsync(Roles.Staff, "seasonalMarks");
        Assert.Equal(HttpStatusCode.OK, (await keyed.PostAsJsonAsync($"{Admin}/bulk", body)).StatusCode);
    }

    [Theory]
    [InlineData(Admin)]
    [InlineData(Admin + "/coverage?" + Period)]
    [InlineData(Teacher + "/scope")]
    public async Task Without_a_token_it_is_401(string url)
    {
        using var client = fixture.Api.AnonymousClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(url)).StatusCode);
    }

    // =====================================================================
    //  5. Coverage report
    // =====================================================================

    [Fact]
    public async Task Coverage_counts_pupil_subject_slots_and_the_drill_down_lists_them()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        await BulkAsync(admin, Admin, w.ClassId, w.SubjectId, Row(w.Pupils[0], 80m, null), Row(w.Pupils[1], null, "Izoh bor"));

        // Two ids as repeated keys: both teachers come back.
        var page = (await admin.GetFromJsonAsync<SeasonalPageDto<SeasonalCoverageRowDto>>(
            $"{Admin}/coverage?{Period}&teacherIds={w.TeacherId}&teacherIds={w.OtherTeacherId}"))!;
        Assert.Equal(2, page.Total);

        var row = page.Items.Single(r => r.TeacherId == w.TeacherId);
        Assert.Equal((3, 2, 1, 67), (row.TotalStudents, row.Marked, row.Unmarked, row.Percent));

        // The other teacher: 5-A other subject (3 pupils) + 5-B subject (1 pupil), nothing marked.
        var other = page.Items.Single(r => r.TeacherId == w.OtherTeacherId);
        Assert.Equal((4, 0, 4, 0), (other.TotalStudents, other.Marked, other.Unmarked, other.Percent));

        var marked = await DetailAsync(admin, w.TeacherId, "&hasMark=true");
        Assert.Equal(2, marked.Total);
        Assert.All(marked.Items, r => Assert.True(r.HasMark));
        Assert.Contains(marked.Items, r => r.StudentId == w.Pupils[1] && r.Score is null);

        var unmarked = await DetailAsync(admin, w.TeacherId, "&hasMark=false");
        Assert.Equal([w.Pupils[2]], unmarked.Items.Select(r => r.StudentId));
        Assert.Equal((w.ClassName, w.SubjectId), (unmarked.Items[0].ClassName, unmarked.Items[0].SubjectId));

        Assert.Equal(3, (await DetailAsync(admin, w.TeacherId, "")).Total);
    }

    /// <summary>
    /// §12: "Coverage percent is capped at 100 when a teacher has more marks than
    /// pupils." Three marks, then a pupil leaves: EduSchool's heads-count would
    /// say 3 of 2 = 150 %. A slot is a CURRENT pupil, so it is 2 of 2 and no
    /// negative "unmarked".
    /// </summary>
    [Fact]
    public async Task Coverage_never_exceeds_100_percent_when_there_are_more_marks_than_pupils()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        await BulkAsync(admin, Admin, w.ClassId, w.SubjectId,
            Row(w.Pupils[0], 80m, null), Row(w.Pupils[1], 81m, null), Row(w.Pupils[2], 82m, null));
        await ArchiveAsync(w.Pupils[2]);

        var row = Assert.Single((await admin.GetFromJsonAsync<SeasonalPageDto<SeasonalCoverageRowDto>>(
            $"{Admin}/coverage?{Period}&teacherIds={w.TeacherId}"))!.Items);

        Assert.Equal((2, 2, 0, 100), (row.TotalStudents, row.Marked, row.Unmarked, row.Percent));
        Assert.Equal(100, SeasonalCoverageReport.Percent(marked: 3, total: 2));
        Assert.Equal(0, SeasonalCoverageReport.Percent(marked: 0, total: 0));
    }

    /// <summary>A split lesson: the teacher of half 1 answers for half 1 only (the journal's roster rule).</summary>
    [Fact]
    public async Task Coverage_of_a_split_lesson_counts_only_the_teachers_half()
    {
        var w = await SeedAsync();
        var (halfTeacherId, _, _) = await TeacherClientAsync(TeacherPermissions.SeasonalMarks);
        var subject = new Subject { Name = $"Ingliz {w.Tag}" };
        await fixture.Api.WithDbAsync(async db =>
        {
            db.Subjects.Add(subject);
            db.ScheduleTemplates.Add(Template(w.ClassId, "split " + w.Tag, subject.Id, halfTeacherId, subGroup: 1));
            foreach (var (id, half) in w.Pupils.Zip(new[] { 1, 2, 0 }))
                (await db.Students.SingleAsync(s => s.Id == id)).SubGroup = half;
            await db.SaveChangesAsync();
        });
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var row = Assert.Single((await admin.GetFromJsonAsync<SeasonalPageDto<SeasonalCoverageRowDto>>(
            $"{Admin}/coverage?{Period}&teacherIds={halfTeacherId}"))!.Items);

        Assert.Equal(1, row.TotalStudents);
        Assert.Equal([w.Pupils[0]], (await DetailAsync(admin, halfTeacherId, "")).Items.Select(r => r.StudentId));
    }

    // =====================================================================
    //  6. Pivot and list
    // =====================================================================

    [Fact]
    public async Task Pivot_rows_are_the_pupils_of_the_chosen_classes_with_blank_cells_for_no_score()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        await BulkAsync(admin, Admin, w.ClassId, w.SubjectId, Row(w.Pupils[0], 87.5m, null), Row(w.Pupils[2], 50m, null));
        await BulkAsync(admin, Admin, w.ClassId, w.OtherSubjectId, Row(w.Pupils[1], null, "Faqat izoh"));
        await ArchiveAsync(w.Pupils[2]); // left the school after being marked

        var pivot = (await admin.GetFromJsonAsync<SeasonalPivotPageDto>(
            $"{Admin}/by-subjects?{Period}&classIds={w.ClassId}&classIds={w.OtherClassId}"
            + $"&subjectIds={w.OtherSubjectId}&subjectIds={w.SubjectId}"))!;

        // Columns by name: "Fizika …" before "Kimyo …".
        Assert.Equal([w.SubjectId, w.OtherSubjectId], pivot.Columns.Select(c => c.SubjectId));
        // Everyone of 7-A and 7-B — the archived pupil kept by their mark — in
        // class order, then by name.
        Assert.Equal(4, pivot.Total);
        Assert.Equal([.. w.Pupils, w.OtherPupil], pivot.Items.Select(r => r.StudentId));

        var byPupil = pivot.Items.ToDictionary(r => r.StudentId);
        Assert.Equal(87.5m, byPupil[w.Pupils[0]].Scores[w.SubjectId]);
        Assert.Empty(byPupil[w.Pupils[1]].Scores); // a comment-only mark is not a score
        Assert.Equal(50m, byPupil[w.Pupils[2]].Scores[w.SubjectId]);
        Assert.Equal(w.ClassName, byPupil[w.Pupils[2]].ClassName);
        Assert.Empty(byPupil[w.OtherPupil].Scores);
    }

    [Fact]
    public async Task Pivot_without_a_class_or_a_subject_is_refused()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var response = await admin.GetAsync($"{Admin}/by-subjects?{Period}&subjectIds=x");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("sinf", await MessageAsync(response));
    }

    /// <summary>The list row is exactly the shape `SeasonalMarkRowDto` in seasonalMarks.ts reads.</summary>
    [Fact]
    public async Task List_row_has_the_contract_shape_and_filters_by_period()
    {
        var w = await SeedAsync();
        var (adminUser, _) = await fixture.Api.SeedUserAsync(Roles.Admin);
        using var admin = fixture.Api.ClientWithToken(
            fixture.Api.TokenFor(Roles.Admin, adminUser.Id, adminUser.FullName, adminUser.Email));
        await BulkAsync(admin, Admin, w.ClassId, w.SubjectId, Row(w.Pupils[0], 87.5m, "Yaxshi natija"));

        using var doc = JsonDocument.Parse(await admin.GetStringAsync($"{Admin}?studentId={w.Pupils[0]}&{Period}"));
        var root = doc.RootElement;
        Assert.Equal(["items", "limit", "page", "total"], Keys(root));
        Assert.Equal(1, root.GetProperty("total").GetInt32());

        var item = root.GetProperty("items")[0];
        Assert.Equal(
            ["class", "comment", "createdByName", "id", "month", "periodKind", "periodLabel",
             "quarter", "score", "student", "subject", "updatedAt", "year"],
            Keys(item));
        Assert.Equal(["fullName", "id"], Keys(item.GetProperty("student")));
        Assert.Equal(w.ClassName, item.GetProperty("class").GetProperty("name").GetString());
        Assert.Equal("Mart 2026", item.GetProperty("periodLabel").GetString());
        Assert.Equal(87.5m, item.GetProperty("score").GetDecimal());
        Assert.Equal(JsonValueKind.Null, item.GetProperty("quarter").ValueKind);
        Assert.Equal(adminUser.FullName, item.GetProperty("createdByName").GetString());
        Assert.Matches(new Regex(@"^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}$"), item.GetProperty("updatedAt").GetString());

        // Another month of the same pupil is not in this filter.
        Assert.Equal(0, (await admin.GetFromJsonAsync<SeasonalPageDto<SeasonalMarkRowDto>>(
            $"{Admin}?studentId={w.Pupils[0]}&periodKind=monthly&year=2026&month=4"))!.Total);

        // A month without its kind is refused, not ignored.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.GetAsync($"{Admin}?month=3")).StatusCode);
    }

    // =====================================================================
    //  7. Inline edit and delete
    // =====================================================================

    [Fact]
    public async Task Put_replaces_score_and_comment_and_refuses_an_empty_mark()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        await BulkAsync(admin, Admin, w.ClassId, w.SubjectId, Row(w.Pupils[0], 60m, "Birinchi izoh"));
        var id = (await GridAsync(admin, Admin, w.ClassId, w.SubjectId))[0].MarkId!;

        var response = await admin.PutAsJsonAsync($"{Admin}/{id}", new { score = 72.345m, comment = (string?)null });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var row = (await response.Content.ReadFromJsonAsync<SeasonalMarkRowDto>())!;
        Assert.Equal((72.35m, (string?)null), (row.Score, row.Comment));

        var empty = await admin.PutAsJsonAsync($"{Admin}/{id}", new { score = (decimal?)null, comment = "  " });
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);

        var bounds = await admin.PutAsJsonAsync($"{Admin}/{id}", new { score = 101m, comment = (string?)null });
        Assert.Equal(HttpStatusCode.BadRequest, bounds.StatusCode);

        Assert.Equal(HttpStatusCode.NotFound,
            (await admin.PutAsJsonAsync($"{Admin}/{Guid.NewGuid()}", new { score = 50m, comment = (string?)null })).StatusCode);

        var logs = 0;
        await fixture.Api.WithDbAsync(async db => logs = await db.AuditLogs
            .CountAsync(a => a.EntityType == SeasonalMarkService.AuditEntity && a.EntityId == id && a.Action == "update"));
        Assert.Equal(1, logs);
    }

    [Fact]
    public async Task Delete_removes_the_mark_and_answers_404_afterwards()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        await BulkAsync(admin, Admin, w.ClassId, w.SubjectId, Row(w.Pupils[0], 60m, null));
        var id = (await GridAsync(admin, Admin, w.ClassId, w.SubjectId))[0].MarkId!;

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"{Admin}/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"{Admin}/{id}")).StatusCode);
        Assert.Equal(0, await MarkCountAsync(w));
    }

    // =====================================================================
    //  8. Exports
    // =====================================================================

    [Fact]
    public async Task The_three_exports_are_xlsx_and_a_bad_filter_is_a_json_400()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        await BulkAsync(admin, Admin, w.ClassId, w.SubjectId, Row(w.Pupils[0], 60m, "Izoh bor"));

        foreach (var url in new[]
                 {
                     $"{Admin}/export?studentId={w.Pupils[0]}",
                     $"{Admin}/by-subjects/export?{Period}&classIds={w.ClassId}&subjectIds={w.SubjectId}",
                     $"{Admin}/coverage/export?{Period}&teacherIds={w.TeacherId}",
                 })
        {
            var response = await admin.GetAsync(url);
            Assert.True(response.StatusCode == HttpStatusCode.OK, $"{url}: {response.StatusCode}");
            Assert.Equal(
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                response.Content.Headers.ContentType?.MediaType);
            var bytes = await response.Content.ReadAsByteArrayAsync();
            Assert.Equal("PK"u8.ToArray(), bytes[..2]); // a zip container
        }

        var bad = await admin.GetAsync($"{Admin}/export?periodKind=daily");
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.Contains("turi noto'g'ri", await MessageAsync(bad));
    }

    // =====================================================================
    //  9. §5.14 — guard clauses on existing delete endpoints
    // =====================================================================

    /// <summary>`seasonal_marks.subject_id` is ON DELETE RESTRICT: a readable 409, never a 500.</summary>
    [Fact]
    public async Task Subject_with_seasonal_marks_is_not_deleted_409()
    {
        var w = await SeedAsync();
        var lonely = new Subject { Name = $"Faqat baho {w.Tag}" }; // no timetable, no journal — only the mark
        await fixture.Api.WithDbAsync(async db => { db.Subjects.Add(lonely); await db.SaveChangesAsync(); });
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        await BulkAsync(admin, Admin, w.ClassId, lonely.Id, Row(w.Pupils[0], 60m, null));

        var response = await admin.DeleteAsync($"/api/admin/subjects/{lonely.Id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("1 ta mavsumiy baho", await MessageAsync(response));
        var exists = false;
        await fixture.Api.WithDbAsync(async db => exists = await db.Subjects.AnyAsync(s => s.Id == lonely.Id));
        Assert.True(exists);
    }

    /// <summary>`seasonal_marks.class_id` is ON DELETE RESTRICT: a readable 400 in the style of the pupil check.</summary>
    [Fact]
    public async Task Class_with_seasonal_marks_is_not_deleted_400()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        await BulkAsync(admin, Admin, w.OtherClassId, w.SubjectId, Row(w.OtherPupil, 60m, null));
        await ArchiveAsync(w.OtherPupil); // the pupil check counts active pupils only

        var response = await admin.DeleteAsync($"/api/admin/classes/{w.OtherClassId}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("mavsumiy baholash", await MessageAsync(response));
        var exists = false;
        await fixture.Api.WithDbAsync(async db => exists = await db.Classes.AnyAsync(c => c.Id == w.OtherClassId));
        Assert.True(exists);
    }

    // =====================================================================
    //  Seed and helpers
    // =====================================================================

    private sealed record World(
        string Tag,
        string ClassId, string ClassName, string OtherClassId,
        string SubjectId, string OtherSubjectId,
        IReadOnlyList<string> Pupils, IReadOnlyList<string> PupilNames, string OtherPupil,
        string TeacherId, string TeacherUserId, HttpClient Teacher,
        string OtherTeacherId);

    /// <summary>
    /// 7-A (three pupils) and 7-B (one pupil). The teacher under test teaches
    /// "Fizika" in 7-A only. Another teacher teaches "Kimyo" in 7-A and "Fizika"
    /// in 7-B — the two foreign pairs.
    /// </summary>
    private async Task<World> SeedAsync(string[]? teacherPermissions = null)
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var (teacherId, teacherUserId, teacher) =
            await TeacherClientAsync(teacherPermissions ?? [TeacherPermissions.SeasonalMarks]);
        var (otherTeacherId, _, _) = await TeacherClientAsync(TeacherPermissions.SeasonalMarks);

        var clsA = new SchoolClass { Name = $"7-A {tag}", Grade = 7 };
        var clsB = new SchoolClass { Name = $"7-B {tag}", Grade = 7 };
        var fizika = new Subject { Name = $"Fizika {tag}" };
        var kimyo = new Subject { Name = $"Kimyo {tag}" };
        var pupils = Enumerable.Range(1, 3)
            .Select(i => GeneralSettingsFlagsTests.NewStudent($"Baho {tag} {i}", clsA.Name, "+998900000071"))
            .ToList();
        var otherPupil = GeneralSettingsFlagsTests.NewStudent($"Baho {tag} B", clsB.Name, "+998900000072");

        await fixture.Api.WithDbAsync(async db =>
        {
            db.Classes.AddRange(clsA, clsB);
            db.Subjects.AddRange(fizika, kimyo);
            db.Students.AddRange([.. pupils, otherPupil]);
            db.ScheduleTemplates.AddRange(
                Template(clsA.Id, "A1 " + tag, fizika.Id, teacherId),
                Template(clsA.Id, "A2 " + tag, kimyo.Id, otherTeacherId),
                Template(clsB.Id, "B " + tag, fizika.Id, otherTeacherId));
            await db.SaveChangesAsync();
        });

        return new World(
            tag, clsA.Id, clsA.Name, clsB.Id, fizika.Id, kimyo.Id,
            [.. pupils.Select(p => p.Id)], [.. pupils.Select(p => p.FullName)], otherPupil.Id,
            teacherId, teacherUserId, teacher, otherTeacherId);
    }

    private static ScheduleTemplate Template(
        string classId, string name, string subjectId, string teacherId, int subGroup = 0)
    {
        var tpl = new ScheduleTemplate { ClassId = classId, Name = name };
        tpl.Lessons.Add(new ScheduleLesson
        {
            TemplateId = tpl.Id, Day = 0, Period = 1, SubjectId = subjectId, TeacherId = teacherId, SubGroup = subGroup,
        });
        return tpl;
    }

    /// <summary>A teacher account with exactly these section keys.</summary>
    private async Task<(string TeacherId, string UserId, HttpClient Client)> TeacherClientAsync(
        params string[] permissions)
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
        return (teacherId, user.Id, fixture.Api.ClientWithToken(
            fixture.Api.TokenFor(Roles.Teacher, user.Id, user.FullName, user.Email)));
    }

    private Task ArchiveAsync(string studentId) =>
        fixture.Api.WithDbAsync(async db =>
        {
            (await db.Students.SingleAsync(s => s.Id == studentId)).IsArchived = true;
            await db.SaveChangesAsync();
        });

    private async Task<int> MarkCountAsync(World w)
    {
        var count = -1;
        var ids = w.Pupils.Append(w.OtherPupil).ToList();
        await fixture.Api.WithDbAsync(async db =>
            count = await db.SeasonalMarks.CountAsync(m => ids.Contains(m.StudentId)));
        return count;
    }

    private static object Row(string studentId, decimal? score, string? comment) =>
        new { studentId, score, comment };

    private static object Bulk(string classId, string subjectId, params object[] rows) =>
        new { classId, subjectId, periodKind = "monthly", year = Year, month = Month, rows };

    private static object Bulk(
        string classId, string subjectId, string periodKind, int? month, int? quarter, params object[] rows) =>
        new { classId, subjectId, periodKind, year = Year, month, quarter, rows };

    private static async Task<SeasonalBulkResultDto> BulkAsync(
        HttpClient client, string root, string classId, string subjectId, params object[] rows)
    {
        var response = await client.PostAsJsonAsync($"{root}/bulk", Bulk(classId, subjectId, rows));
        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<SeasonalBulkResultDto>())!;
    }

    private static async Task<List<SeasonalEntryRowDto>> GridAsync(
        HttpClient client, string root, string classId, string subjectId)
    {
        var response = await client.GetAsync($"{root}/students?classId={classId}&subjectId={subjectId}&{Period}");
        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return (await response.Content.ReadFromJsonAsync<List<SeasonalEntryRowDto>>())!;
    }

    private static async Task<SeasonalPageDto<SeasonalCoverageDetailRowDto>> DetailAsync(
        HttpClient client, string teacherId, string extra) =>
        (await client.GetFromJsonAsync<SeasonalPageDto<SeasonalCoverageDetailRowDto>>(
            $"{Admin}/coverage/detail?{Period}&teacherId={teacherId}{extra}"))!;

    private static async Task<string> MessageAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("message").GetString() ?? "";
    }

    private static string[] Keys(JsonElement e) =>
        [.. e.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal)];

    /// <summary>
    /// `Score` and `Comment` out of an audit snapshot, matched case-insensitively
    /// like the history dialog does. Missing either key fails the test.
    /// </summary>
    private static (decimal? Score, string? Comment) ScoreAndComment(string? json)
    {
        Assert.NotNull(json);
        using var doc = JsonDocument.Parse(json);
        var props = doc.RootElement.EnumerateObject()
            .ToDictionary(p => p.Name.ToLowerInvariant(), p => p.Value.Clone());
        Assert.True(props.ContainsKey("score"), $"no score in {json}");
        Assert.True(props.ContainsKey("comment"), $"no comment in {json}");
        var score = props["score"];
        var comment = props["comment"];
        return (score.ValueKind == JsonValueKind.Null ? null : score.GetDecimal(),
                comment.ValueKind == JsonValueKind.Null ? null : comment.GetString());
    }
}
