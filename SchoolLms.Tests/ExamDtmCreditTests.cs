using Npgsql;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// Blok test DTM ko'rinishida (mijoz, 2026-10-31): 1-blok 30 × 3,1, 2-blok 30 × 2,1, 3–5-bloklar
/// 10 × 1,1 (jami 189) — natija to'g'ri javoblar soni bilan kiritiladi. Sertifikati bor o'quvchi
/// o'sha fan blokidan ozod qilinadi va sertifikat turidagi daraja → foiz jadvali bo'yicha ball oladi.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class ExamDtmCreditTests(ApiFixture fixture) : IAsyncLifetime
{
    private static readonly ExamActor Actor = new(null, "Test");
    private readonly List<string> _connectionStrings = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        foreach (var cs in _connectionStrings) NpgsqlConnection.ClearPool(new NpgsqlConnection(cs));
        return Task.CompletedTask;
    }

    /* =====================================================================
     *  Kredit jadvali (sof funksiyalar)
     * ================================================================== */

    [Fact]
    public void Kredit_jadvali_tekshiriladi_va_normallashtiriladi()
    {
        var (value, error) = CertificateService.NormalizeExamCredits("A+=100 ; A = 93,3", false, true, "B,A,A+");
        Assert.Null(error);
        Assert.Equal("A+=100; A=93.3", value);

        Assert.NotNull(CertificateService.NormalizeExamCredits("A+=100; C=50", false, true, "B,A,A+").Error);
        Assert.NotNull(CertificateService.NormalizeExamCredits("A+=120", false, true, null).Error);
        Assert.NotNull(CertificateService.NormalizeExamCredits("A+", false, true, null).Error);
        Assert.NotNull(CertificateService.NormalizeExamCredits("A=100", true, false, null).Error);
        Assert.NotNull(CertificateService.NormalizeExamCredits("7=100", false, false, null).Error);
        Assert.Equal((null, null), CertificateService.NormalizeExamCredits("  ", true, false, null));
    }

    [Fact]
    public void Kredit_foizi_daraja_yoki_eng_yuqori_chegara_boyicha()
    {
        Assert.Equal(93.3m, CertificateService.ExamCreditPercent("A+=100; A=93.3", "a", null));
        Assert.Null(CertificateService.ExamCreditPercent("A+=100; A=93.3", "B", null));
        Assert.Equal(90m, CertificateService.ExamCreditPercent("7=100; 6.5=90; 6=80", null, 6.5m));
        Assert.Equal(100m, CertificateService.ExamCreditPercent("7=100; 6.5=90; 6=80", null, 8m));
        Assert.Null(CertificateService.ExamCreditPercent("7=100; 6.5=90; 6=80", null, 5.5m));
        Assert.Null(CertificateService.ExamCreditPercent(null, "A", 9m));
    }

    /* =====================================================================
     *  DTM bo'limlari
     * ================================================================== */

    [Fact]
    public async Task Dtm_bolimi_savol_x_ball_va_togri_javoblar_bilan_kiritiladi()
    {
        await using var db = await NewDbAsync("dtm");
        var w = await SeedAsync(db);

        var created = await ExamService.CreateAsync(db, Actor, new ExamUpsertRequest(
            "DTM", ExamKind.Block, ExamDelivery.Manual, null, null, "2026-11-10", null, null, null,
            [
                new ExamSectionInput(w.Main, null, 30, null, 0, 3.1m),
                new ExamSectionInput(w.Second, null, 30, null, 1, 2.1m),
                new ExamSectionInput(w.Common, null, 10, null, 2, 1.1m),
            ]));
        Assert.Null(created.Error);
        var exam = created.Value!;
        Assert.Equal([93m, 63m, 11m], exam.Sections.OrderBy(s => s.Order).Select(s => s.MaxScore!.Value));
        Assert.Equal(167m, exam.Sections.Sum(s => s.MaxScore!.Value));

        // Faqat bittasi berilsa — xato.
        var half = await ExamService.CreateAsync(db, Actor, new ExamUpsertRequest(
            "Yarim", ExamKind.Block, ExamDelivery.Manual, null, null, "2026-11-10", null, null, null,
            [new ExamSectionInput(w.Main, null, 30, null, 0)]));
        Assert.NotNull(half.Error);

        await ExamService.AddParticipantsAsync(db, Actor, exam.Id,
            new ExamAddParticipantsRequest(null, [w.Plain], null, null), callerHasAdmission: false);
        var table = (await ExamService.GetEntryTableAsync(db, exam.Id)).Value!;
        var main = table.Columns.Single(c => c.SubjectId == w.Main);
        Assert.Equal(30, main.QuestionCount);
        Assert.Equal(3.1m, main.PointsPerCorrect);
        var pid = Assert.Single(table.Rows).ParticipantId;

        var tooMany = await ExamService.SaveEntryAsync(db, Actor, exam.Id,
            [new ExamEntrySaveRow(pid, [new ExamEntrySaveScore(main.SectionId, null, 31)], false)]);
        Assert.NotNull(tooMany.Error);

        var ok = await ExamService.SaveEntryAsync(db, Actor, exam.Id,
            [new ExamEntrySaveRow(pid, [new ExamEntrySaveScore(main.SectionId, null, 25)], false)]);
        Assert.Null(ok.Error);

        var after = (await ExamService.GetEntryTableAsync(db, exam.Id)).Value!.Rows.Single();
        Assert.Equal(77.5m, after.Scores[main.SectionId]);
        Assert.Equal(25, after.Correct![main.SectionId]);
        Assert.Equal(77.5m, after.TotalPoints);
        Assert.Equal(167m, after.MaxPoints);
    }

    /* =====================================================================
     *  Sertifikat krediti
     * ================================================================== */

    [Fact]
    public async Task Sertifikatli_oquvchi_blokdan_ozod_va_kredit_avtomatik_yoziladi()
    {
        await using var db = await NewDbAsync("credit");
        var w = await SeedAsync(db);
        var exam = await NewDtmExamAsync(db, w, "2026-11-10");
        await ExamService.AddParticipantsAsync(db, Actor, exam.Id,
            new ExamAddParticipantsRequest(null, [w.Certified, w.Plain], null, null), callerHasAdmission: false);

        var table = (await ExamService.GetEntryTableAsync(db, exam.Id)).Value!;
        var main = table.Columns.Single(c => c.SubjectId == w.Main);
        var second = table.Columns.Single(c => c.SubjectId == w.Second);
        var certifiedRow = table.Rows.Single(r => r.FullName.StartsWith("Sertifikatli"));
        var plainRow = table.Rows.Single(r => r.FullName.StartsWith("Oddiy"));

        // 93 × 93.3% = 86.769 → 86.77. Oddiy o'quvchida kredit yo'q.
        var credit = certifiedRow.Credits![main.SectionId];
        Assert.Equal(86.77m, credit.Points);
        Assert.Equal("Milliy sertifikat A", credit.Label);
        Assert.Equal(86.77m, certifiedRow.Scores[main.SectionId]);
        Assert.Null(plainRow.Credits);

        // Kreditli katak uchun yuborilgan qiymat e'tiborga olinmaydi.
        var saved = await ExamService.SaveEntryAsync(db, Actor, exam.Id,
        [
            new ExamEntrySaveRow(certifiedRow.ParticipantId,
                [new ExamEntrySaveScore(main.SectionId, null, 2), new ExamEntrySaveScore(second.SectionId, null, 20)], false),
        ]);
        Assert.Null(saved.Error);

        var score = db.ExamSectionScores.Single(s => s.ParticipantId == certifiedRow.ParticipantId && s.SectionId == main.SectionId);
        Assert.Equal(86.77m, score.Points);
        Assert.Equal(w.CertificateId, score.CertificateId);
        Assert.Null(score.CorrectCount);

        var row = (await ExamService.GetEntryTableAsync(db, exam.Id)).Value!.Rows.Single(r => r.ParticipantId == certifiedRow.ParticipantId);
        Assert.Equal(86.77m + 42m, row.TotalPoints);

        var results = await ExamService.ListResultsAsync(db, new ExamResultListQuery { ExamId = exam.Id });
        var mine = results.Value!.Items.Single(r => r.ParticipantId == certifiedRow.ParticipantId);
        Assert.Equal("Milliy sertifikat A", mine.Scores.Single(s => s.SectionId == main.SectionId).Certificate);
        Assert.Null(mine.Scores.Single(s => s.SectionId == second.SectionId).Certificate);
    }

    [Fact]
    public async Task Muddati_otgan_yoki_keyin_berilgan_sertifikat_kredit_bermaydi()
    {
        await using var db = await NewDbAsync("expired");
        var w = await SeedAsync(db);

        // Sertifikat 2026-09-01 da berilgan, 2026-10-01 gacha amal qiladi.
        var cert = db.Certificates.Single(c => c.Id == w.CertificateId);
        cert.ExpiresOn = new DateOnly(2026, 10, 1);
        await db.SaveChangesAsync();

        foreach (var date in new[] { "2026-11-10", "2026-08-15" })
        {
            var exam = await NewDtmExamAsync(db, w, date);
            await ExamService.AddParticipantsAsync(db, Actor, exam.Id,
                new ExamAddParticipantsRequest(null, [w.Certified], null, null), callerHasAdmission: false);
            var row = Assert.Single((await ExamService.GetEntryTableAsync(db, exam.Id)).Value!.Rows);
            Assert.Null(row.Credits);
        }
    }

    /* =====================================================================
     *  Yordamchilar
     * ================================================================== */

    private sealed record World(string Main, string Second, string Common, string Certified, string Plain, Guid CertificateId);

    private static async Task<World> SeedAsync(AppDbContext db)
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var user = new AppUser { FullName = "Test", Role = Roles.SuperAdmin, Email = $"dtm.{tag}" };
        var main = new Subject { Name = $"Tarix {tag}" };
        var second = new Subject { Name = $"Huquq {tag}" };
        var common = new Subject { Name = $"Ona tili {tag}" };
        var c11 = new SchoolClass { Name = $"11-{tag}", Grade = 11 };
        db.Users.Add(user);
        db.Subjects.AddRange(main, second, common);
        db.Classes.Add(c11);
        var certified = NewStudent($"Sertifikatli {tag}", c11.Name);
        var plain = NewStudent($"Oddiy {tag}", c11.Name);
        db.Students.AddRange(certified, plain);

        var type = new CertificateType
        {
            Name = "Milliy sertifikat", UsesLevel = true, LevelOptions = "B,B+,A,A+",
            ExamCredits = "A+=100; A=93.3; B+=86.7; B=80", CreatedAt = DateTimeOffset.UtcNow,
        };
        db.CertificateTypes.Add(type);
        await db.SaveChangesAsync();

        var cert = new Certificate
        {
            StudentId = certified.Id, TypeId = type.Id, SubjectId = main.Id, Level = "A",
            IssuedOn = new DateOnly(2026, 9, 1), CreatedBy = user.Id, CreatedAt = DateTimeOffset.UtcNow,
        };
        db.Certificates.Add(cert);
        await db.SaveChangesAsync();

        return new World(main.Id, second.Id, common.Id, certified.Id, plain.Id, cert.Id);
    }

    private static async Task<ExamDto> NewDtmExamAsync(AppDbContext db, World w, string date)
    {
        var created = await ExamService.CreateAsync(db, Actor, new ExamUpsertRequest(
            $"DTM {Guid.NewGuid():N}"[..12], ExamKind.Block, ExamDelivery.Manual, null, null, date, null, null, null,
            [
                new ExamSectionInput(w.Main, null, 30, null, 0, 3.1m),
                new ExamSectionInput(w.Second, null, 30, null, 1, 2.1m),
                new ExamSectionInput(w.Common, null, 10, null, 2, 1.1m),
            ]));
        Assert.Null(created.Error);
        return created.Value!;
    }

    private static Student NewStudent(string fullName, string className) => new()
    {
        FullName = fullName, LastName = "Familiya", FirstName = "Ism", MiddleName = "Otasi",
        BirthDate = "2009-01-01", Address = "Toshkent", Gender = "male",
        ParentFullName = "Ota-ona", ParentLastName = "Familiya", ParentFirstName = "Ism", ParentMiddleName = "Otasi",
        ParentPhone = "+998901112233", ClassName = className, EnrollmentDate = "2025-09-01",
    };

    private async Task<AppDbContext> NewDbAsync(string prefix)
    {
        var database = await fixture.Postgres.CreateDatabaseAsync("dtm_" + prefix);
        _connectionStrings.Add(database.OwnerConnectionString);
        return PostgresFixture.NewContext(database.OwnerConnectionString);
    }
}
