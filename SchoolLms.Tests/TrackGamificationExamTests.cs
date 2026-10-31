using System.Net;
using System.Net.Http.Json;
using Npgsql;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// 9–11-sinflar yo'nalish bo'yicha o'qiydi (mijoz, 2026-10-30): gamifikatsiya (Ballar nazorati,
/// yo'nalishga ball) va blok test (yo'nalish bo'yicha ishtirokchi qo'shish, natijalarda yo'nalish)
/// sinf bilan bir qatorda yo'nalish guruhini ham tushunadi — docs/modules/track-groups-as-classes.md.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class TrackGamificationExamTests(ApiFixture fixture) : IAsyncLifetime
{
    private const string Discipline = "/api/admin/discipline";

    private readonly List<string> _connectionStrings = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        foreach (var cs in _connectionStrings) NpgsqlConnection.ClearPool(new NpgsqlConnection(cs));
        return Task.CompletedTask;
    }

    /* =====================================================================
     *  Blok test
     * ================================================================== */

    [Fact]
    public async Task Blok_testga_yonalish_faol_azolari_qoshiladi()
    {
        await using var db = await NewDbAsync("exam_add");
        var w = await SeedAsync(db, $"T-{Tag()}");
        var exam = await NewBlockExamAsync(db, "2026-10-20");

        var result = await ExamService.AddParticipantsAsync(db, Actor, exam.Id,
            new ExamAddParticipantsRequest(null, null, null, [w.TrackId.ToString()]), callerHasAdmission: false);
        Assert.Null(result.Error);
        Assert.Equal(2, result.Value!.Added);

        var rows = db.ExamParticipants.Where(p => p.ExamId == exam.Id).ToList();
        // 9-A va 10-B dan bittadan; chiqib ketgan va arxivdagi a'zo — yo'q.
        Assert.Equal(new[] { w.P9, w.P10 }.Order(), rows.Select(r => r.StudentId!).Order());
        // Sinf nusxasi — o'quvchining o'z sinfi (hujjat va hisobot uchun).
        Assert.Equal(w.Class9, rows.Single(r => r.StudentId == w.P9).ClassId);
        Assert.Equal(w.Class10, rows.Single(r => r.StudentId == w.P10).ClassId);

        // Qayta qo'shish — idempotent.
        var again = await ExamService.AddParticipantsAsync(db, Actor, exam.Id,
            new ExamAddParticipantsRequest(null, null, null, [w.TrackId.ToString()]), callerHasAdmission: false);
        Assert.Equal(0, again.Value!.Added);
        Assert.Equal(2, again.Value.Skipped);
    }

    [Fact]
    public async Task Oddiy_guruh_yoki_nomalum_id_yonalish_emas()
    {
        await using var db = await NewDbAsync("exam_bad");
        var w = await SeedAsync(db, $"T-{Tag()}");
        var exam = await NewBlockExamAsync(db, "2026-10-20");

        foreach (var id in new[] { w.OrdinaryId.ToString(), Guid.NewGuid().ToString(), "not-a-guid" })
        {
            var r = await ExamService.AddParticipantsAsync(db, Actor, exam.Id,
                new ExamAddParticipantsRequest(null, null, null, [id]), callerHasAdmission: false);
            Assert.NotNull(r.Error);
            Assert.Equal(ExamService.TrackNotFoundMessage, r.Error!.Message);
        }
        Assert.Empty(db.ExamParticipants.Where(p => p.ExamId == exam.Id));
    }

    [Fact]
    public async Task Natijalarda_yonalish_nomi_va_yonalish_filtri()
    {
        await using var db = await NewDbAsync("exam_results");
        var w = await SeedAsync(db, $"T-{Tag()}");
        var exam = await NewBlockExamAsync(db, "2026-10-20");
        await ExamService.AddParticipantsAsync(db, Actor, exam.Id,
            new ExamAddParticipantsRequest(null, null, [w.Class5], null), callerHasAdmission: false);
        await ExamService.AddParticipantsAsync(db, Actor, exam.Id,
            new ExamAddParticipantsRequest(null, null, null, [w.TrackId.ToString()]), callerHasAdmission: false);

        var all = await ExamService.ListResultsAsync(db, new ExamResultListQuery { ExamId = exam.Id });
        Assert.Null(all.Error);
        Assert.Equal(3, all.Value!.Total);
        Assert.Equal(w.TrackName, all.Value.Items.Single(r => r.FullName.StartsWith("Toqqiz")).TrackName);
        Assert.Null(all.Value.Items.Single(r => r.FullName.StartsWith("Beshinchi")).TrackName);

        var byTrack = await ExamService.ListResultsAsync(db,
            new ExamResultListQuery { ExamId = exam.Id, GroupId = w.TrackId.ToString() });
        Assert.Equal(2, byTrack.Value!.Total);
        Assert.All(byTrack.Value.Items, r => Assert.Equal(w.TrackName, r.TrackName));

        var bad = await ExamService.ListResultsAsync(db, new ExamResultListQuery { GroupId = "x" });
        Assert.NotNull(bad.Error);
    }

    [Fact]
    public async Task Natijadagi_yonalish_test_kunidagi_azolik_boyicha()
    {
        await using var db = await NewDbAsync("exam_onday");
        var w = await SeedAsync(db, $"T-{Tag()}");
        var exam = await NewBlockExamAsync(db, "2026-10-20");
        await ExamService.AddParticipantsAsync(db, Actor, exam.Id,
            new ExamAddParticipantsRequest(null, [w.Left], null, null), callerHasAdmission: false);

        // "Left" yo'nalishdan 2026-10-25 da chiqqan — 20-oktabrdagi testda u hali yo'nalishda edi.
        var rows = await ExamService.ListResultsAsync(db, new ExamResultListQuery { ExamId = exam.Id });
        Assert.Equal(w.TrackName, Assert.Single(rows.Value!.Items).TrackName);
    }

    /* =====================================================================
     *  Gamifikatsiya (umumiy baza, noyob nomlar)
     * ================================================================== */

    [Fact]
    public async Task Yonalishga_ball_faol_azolarga_yoziladi_va_ballar_nazoratida_yonalish_chiqadi()
    {
        var tag = Tag();
        World w = default!;
        var reason = new DisciplineReason { Name = $"Faollik {tag}", Points = 2, IsActive = true };
        await fixture.Api.WithDbAsync(async db =>
        {
            w = await SeedAsync(db, $"Yo'nalish {tag}");
            db.DisciplineReasons.Add(reason);
            await db.SaveChangesAsync();
        });

        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var res = await admin.PostAsJsonAsync($"{Discipline}/points/class",
            new { classId = w.TrackId.ToString(), reasonId = reason.Id });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var body = (await res.Content.ReadFromJsonAsync<ClassDisciplinePointResultDto>())!;
        Assert.Equal(2, body.Applied);
        Assert.Equal(new[] { w.P9, w.P10 }.Order(), body.Items.Select(i => i.StudentId).Order());

        var scores = (await admin.GetFromJsonAsync<List<DisciplineScoreRowDto>>($"{Discipline}/scores"))!;
        Assert.Equal(w.TrackName, scores.Single(s => s.StudentId == w.P9).TrackName);
        Assert.Null(scores.Single(s => s.StudentId == w.P5).TrackName);

        // Oddiy guruh — sinf ham, yo'nalish ham emas.
        var ordinary = await admin.PostAsJsonAsync($"{Discipline}/points/class",
            new { classId = w.OrdinaryId.ToString(), reasonId = reason.Id });
        Assert.Equal(HttpStatusCode.NotFound, ordinary.StatusCode);
    }

    /* =====================================================================
     *  Yordamchilar
     * ================================================================== */

    private static readonly ExamActor Actor = new(null, "Test");

    private static string Tag() => Guid.NewGuid().ToString("N")[..8];

    private sealed record World(
        string Class5, string Class9, string Class10, Guid TrackId, string TrackName, Guid OrdinaryId,
        string P5, string P9, string P10, string Left);

    /// <summary>
    /// 5-sinf (oddiy), 9- va 10-sinf; yo'nalish guruhi: 9 va 10 dan faol a'zolar, 2026-10-25 da chiqqan
    /// a'zo va arxivdagi o'quvchi. Plus oddiy (fanli) guruh.
    /// </summary>
    private static async Task<World> SeedAsync(AppDbContext db, string trackName)
    {
        var tag = Tag();
        var user = new AppUser { FullName = "Test", Role = Roles.SuperAdmin, Email = $"tge.{tag}" };
        var subject = new Subject { Name = $"Ingliz {tag}" };
        var c5 = new SchoolClass { Name = $"5-{tag}", Grade = 5 };
        var c9 = new SchoolClass { Name = $"9-{tag}", Grade = 9 };
        var c10 = new SchoolClass { Name = $"10-{tag}", Grade = 10 };
        db.Users.Add(user);
        db.Subjects.Add(subject);
        db.Classes.AddRange(c5, c9, c10);

        var p5 = NewStudent($"Beshinchi {tag}", c5.Name);
        var p9 = NewStudent($"Toqqiz {tag}", c9.Name);
        var p10 = NewStudent($"Ochinchi {tag}", c10.Name);
        var left = NewStudent($"Chiqqan {tag}", c10.Name);
        var archived = NewStudent($"Arxiv {tag}", c9.Name);
        archived.IsArchived = true;
        db.Students.AddRange(p5, p9, p10, left, archived);
        await db.SaveChangesAsync();

        var track = new StudyGroup { Name = trackName, IsTrack = true, CreatedBy = user.Id, CreatedAt = AppClock.NowInstant };
        var ordinary = new StudyGroup
        {
            Name = $"Ingliz guruhi {tag}", SubjectId = subject.Id, CreatedBy = user.Id, CreatedAt = AppClock.NowInstant,
        };
        db.StudyGroups.AddRange(track, ordinary);
        db.StudyGroupClasses.AddRange(
            new StudyGroupClass { GroupId = track.Id, ClassId = c9.Id },
            new StudyGroupClass { GroupId = track.Id, ClassId = c10.Id });
        var joined = new DateOnly(2026, 9, 1);
        db.StudyGroupMembers.AddRange(
            Member(track.Id, p9.Id, joined, user.Id),
            Member(track.Id, p10.Id, joined, user.Id),
            Member(track.Id, archived.Id, joined, user.Id),
            Member(track.Id, left.Id, joined, user.Id, leftOn: new DateOnly(2026, 10, 25)));
        await db.SaveChangesAsync();

        return new World(c5.Id, c9.Id, c10.Id, track.Id, track.Name, ordinary.Id, p5.Id, p9.Id, p10.Id, left.Id);
    }

    private static async Task<Exam> NewBlockExamAsync(AppDbContext db, string date)
    {
        var exam = new Exam
        {
            Title = $"Blok test {Tag()}", Kind = ExamKind.Block, Delivery = ExamDelivery.Manual,
            ExamDate = date, Status = ExamStatus.Draft,
        };
        db.Exams.Add(exam);
        await db.SaveChangesAsync();
        return exam;
    }

    private static StudyGroupMember Member(Guid groupId, string studentId, DateOnly joined, string by, DateOnly? leftOn = null) => new()
    {
        GroupId = groupId, SubjectId = null, StudentId = studentId, JoinedOn = joined, LeftOn = leftOn,
        CreatedBy = by, CreatedAt = AppClock.NowInstant,
    };

    private static Student NewStudent(string fullName, string className) => new()
    {
        FullName = fullName, LastName = "Familiya", FirstName = "Ism", MiddleName = "Otasi",
        BirthDate = "2010-01-01", Address = "Toshkent", Gender = "male",
        ParentFullName = "Ota-ona", ParentLastName = "Familiya", ParentFirstName = "Ism", ParentMiddleName = "Otasi",
        ParentPhone = "+998901112233", ClassName = className, EnrollmentDate = "2025-09-01",
    };

    private async Task<AppDbContext> NewDbAsync(string prefix)
    {
        var database = await fixture.Postgres.CreateDatabaseAsync("tge_" + prefix);
        _connectionStrings.Add(database.OwnerConnectionString);
        return PostgresFixture.NewContext(database.OwnerConnectionString);
    }
}
