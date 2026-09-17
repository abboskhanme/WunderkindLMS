using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// "BALLAR NAZORATI" va guruh darslari — <c>GET /api/admin/discipline/scores</c>
/// (docs/modules/students-parity.md §2.1.6, cut-over §4.3).
///
/// <para>
/// Ball ekrani EGADAN BEXABAR: <c>DisciplineController.BuildScoresAsync</c> jurnal
/// qatorlarini <c>where reason_id is not null</c> bilan oladi va <c>class_id</c> /
/// <c>owner_kind</c> ga umuman qaramaydi. Ya'ni guruh darsida qo'yilgan davomat
/// belgisi ham bolaning qoldig'idan yechiladi — <b>va bu aynan kerak bo'lgan
/// narsa</b>: intizom bola darsda qanday bo'lgani haqida, dars kimniki ekani
/// haqida emas.
/// </para>
/// <para>
/// Lekin buni ayta oladigan birorta test yo'q edi, ya'ni kimdir "guruh qatorlari
/// hisobotlarda faqat o'chirgich yoqilganda sanaladi" qoidasini bu yerga ham
/// ko'chirib qo'yishi va ballarni JIMGINA siljitib yuborishi mumkin edi. Shuning
/// uchun qoida IKKALA holatda ham qotirib qo'yiladi: o'chirgich O'CHIQ va
/// YOQILGAN — raqam BIR XIL.
/// </para>
/// <para>
/// Amalda o'chirgich o'chiq turganda guruh darsi umuman yozilmaydi (jurnal guruh
/// egasini qabul qilmaydi), shuning uchun "o'chiq" holat ishlab chiqarishda
/// bo'sh bo'ladi — bu test uni ATAYLAB sun'iy yaratadi: tekshirilayotgan narsa
/// "raqam o'chirgichga qarab siljimaydi" degan gap.
/// </para>
/// <para>
/// Testlar UMUMIY bazada yuradi: <c>/scores</c> butun maktabni qaytaradi, shuning
/// uchun qatorlar o'quvchi id'si bo'yicha ajratiladi.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class DisciplineGroupScoreTests(ApiFixture fixture)
{
    private const string Scores = "/api/admin/discipline/scores";

    /// <summary>
    /// Guruh darsidagi davomat belgisi qoldiqdan YECHILADI, va raqam o'chirgich
    /// o'chiq bo'lganda ham, yoqilgan bo'lganda ham BIR XIL:
    /// 100 − 5 (sinf darsi) − 5 (guruh darsi) = 90.
    /// </summary>
    [Fact]
    public async Task Guruh_darsidagi_davomat_balli_ochirgichdan_qatiy_nazar_sanaladi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var off = await RowAsync(admin, w.PupilId);
        Assert.Equal(0, off.Plus);
        Assert.Equal(10, off.Minus);
        Assert.Equal(90, off.Remaining);

        await WithGroupLessonsAsync(async () =>
        {
            var on = await RowAsync(admin, w.PupilId);
            Assert.Equal(off.Plus, on.Plus);
            Assert.Equal(off.Minus, on.Minus);
            Assert.Equal(off.Remaining, on.Remaining);
        });
    }

    /// <summary>
    /// Ball FAQAT belgi qo'yilgan bolaga tegishli: o'sha guruh darsida boshqa
    /// bola belgilanmagan bo'lsa — uning qoldig'i 100 da qoladi (ikkala holatda
    /// ham).
    /// </summary>
    [Fact]
    public async Task Guruhdoshning_balli_tegilmaydi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        Assert.Equal(100, (await RowAsync(admin, w.GroupMateId)).Remaining);

        await WithGroupLessonsAsync(async () =>
            Assert.Equal(100, (await RowAsync(admin, w.GroupMateId)).Remaining));
    }

    /// <summary>
    /// Qo'lda kiritilgan rag'bat guruh qatorining USTIGA qo'shiladi (o'rnini
    /// bosmaydi): 100 + 3 − 10 = 93.
    /// </summary>
    [Fact]
    public async Task Qolda_kiritilgan_ball_guruh_qatori_ustiga_qoshiladi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        await fixture.Api.WithDbAsync(async db =>
        {
            db.DisciplinePoints.Add(new DisciplinePoint
            {
                StudentId = w.PupilId, ReasonId = w.BonusReasonId, ReasonName = "Rag'bat",
                Points = 3, Note = "", CreatedBy = "Test",
                CreatedAt = AppClock.Now.ToString("yyyy-MM-ddTHH:mm:ss"),
            });
            await db.SaveChangesAsync();
        });

        await WithGroupLessonsAsync(async () =>
        {
            var row = await RowAsync(admin, w.PupilId);
            Assert.Equal(3, row.Plus);
            Assert.Equal(10, row.Minus);
            Assert.Equal(93, row.Remaining);
        });
    }

    /// <summary>Darvoza o'zgarmadi: <c>AdminPerm("discipline")</c> — o'qituvchi 403, token'siz 401.</summary>
    [Fact]
    public async Task Ruxsatsiz_rol_ballar_nazoratini_kora_olmaydi()
    {
        using var teacher = await fixture.Api.ClientAsAsync(Roles.Teacher, "discipline");
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync(Scores)).StatusCode);

        using var anonymous = fixture.Api.AnonymousClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Scores)).StatusCode);
    }

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    private sealed record ScoreRow(
        string StudentId, string FullName, string ClassName, int Plus, int Minus, int Remaining);

    private static async Task<ScoreRow> RowAsync(HttpClient client, string studentId)
    {
        var response = await client.GetAsync(Scores);
        Assert.True(response.IsSuccessStatusCode,
            $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        var rows = await response.Content.ReadFromJsonAsync<List<ScoreRow>>() ?? [];
        return Assert.Single(rows, r => r.StudentId == studentId);
    }

    /// <summary>Cut-over o'chirgichini yoqib turadi va ASL holatiga qaytaradi.</summary>
    private async Task WithGroupLessonsAsync(Func<Task> body)
    {
        var before = false;
        string? createdId = null;
        await fixture.Api.WithDbAsync(async db =>
        {
            var meta = await db.SchoolMeta.OrderBy(m => m.Id).FirstOrDefaultAsync();
            if (meta is null)
            {
                meta = new SchoolMeta();
                db.SchoolMeta.Add(meta);
                await db.SaveChangesAsync();
                createdId = meta.Id;
            }
            before = meta.GroupLessonsEnabled;
            meta.GroupLessonsEnabled = true;
            await db.SaveChangesAsync();
        });
        try
        {
            await body();
        }
        finally
        {
            await fixture.Api.WithDbAsync(async db =>
            {
                if (createdId is not null)
                {
                    await db.SchoolMeta.Where(m => m.Id == createdId).ExecuteDeleteAsync();
                    return;
                }
                var meta = await db.SchoolMeta.OrderBy(m => m.Id).FirstAsync();
                meta.GroupLessonsEnabled = before;
                await db.SaveChangesAsync();
            });
        }
    }

    private sealed record World(string PupilId, string GroupMateId, string BonusReasonId);

    /// <summary>
    /// Bitta sinf, ikkita o'quvchi va bitta guruh. Tekshiriladigan bolaga IKKI
    /// davomatsizlik belgisi qo'yilgan: biri SINF darsida, biri GURUH darsida —
    /// sababning balli ikkalasida ham −5. Guruhdoshiga hech narsa qo'yilmagan.
    /// </summary>
    private async Task<World> SeedAsync()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var cls = new SchoolClass { Name = $"9-{tag}", Grade = 9 };
        var english = new Subject { Name = $"Ingliz tili {tag}", IsGroupable = true };
        var reason = new AbsenceReason { Name = $"Sababsiz {tag}", Short = "N", Points = -5 };
        var bonus = new DisciplineReason { Name = $"Rag'bat {tag}", Points = 3, IsActive = true };

        var pupil = GeneralSettingsFlagsTests.NewStudent($"Ball {tag}", cls.Name, "+99890" + Rnd());
        var groupMate = GeneralSettingsFlagsTests.NewStudent($"Guruhdosh {tag}", cls.Name, "+99890" + Rnd());

        var (creator, _) = await fixture.Api.SeedUserAsync(Roles.Admin);
        var group = new StudyGroup
        {
            Name = $"Kuchli ingliz {tag}",
            SubjectId = english.Id,
            CreatedBy = creator.Id,
            CreatedAt = AppClock.NowInstant,
        };

        await fixture.Api.WithDbAsync(async db =>
        {
            db.Classes.Add(cls);
            db.Subjects.Add(english);
            db.AbsenceReasons.Add(reason);
            db.DisciplineReasons.Add(bonus);
            db.Students.AddRange(pupil, groupMate);
            db.StudyGroups.Add(group);
            db.StudyGroupClasses.Add(new StudyGroupClass { GroupId = group.Id, ClassId = cls.Id });

            var joined = new DateOnly(2026, 9, 1);
            foreach (var id in new[] { pupil.Id, groupMate.Id })
                db.StudyGroupMembers.Add(new StudyGroupMember
                {
                    GroupId = group.Id, SubjectId = english.Id, StudentId = id,
                    JoinedOn = joined, CreatedBy = creator.Id, CreatedAt = AppClock.NowInstant,
                });

            db.JournalEntries.AddRange(
                new JournalEntry
                {
                    ClassId = cls.Id, SubjectId = english.Id, Quarter = 1, StudentId = pupil.Id,
                    Date = "2026-09-07", Period = 1, ReasonId = reason.Id,
                },
                new JournalEntry
                {
                    ClassId = group.Id.ToString(), OwnerKind = LessonOwnerKind.Group,
                    SubjectId = english.Id, Quarter = 1, StudentId = pupil.Id,
                    Date = "2026-09-08", Period = 2, ReasonId = reason.Id,
                });

            await db.SaveChangesAsync();
        });

        return new World(pupil.Id, groupMate.Id, bonus.Id);
    }

    private static string Rnd() => Random.Shared.Next(1_000_000, 9_999_999).ToString();
}
