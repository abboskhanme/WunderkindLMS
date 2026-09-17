using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// G-18 — o'quvchi portali va ota-ona Mini App'i guruh darslarini ko'radi
/// (docs/modules/students-parity.md §2.1.6, §4.3 slice C3).
///
/// <para>
/// Bu yerda HAQIQIY HTTP so'rovlari yuboriladi: DTO shakli (camelCase maydonlar) va RBAC
/// darvozasi ham shu testlar bilan mahkamlanadi. Hisob-kitobning o'zi
/// <see cref="ClassAttainmentTests"/> da.
/// </para>
/// <para>
/// <b>O'chirgich umumiy bazada.</b> <c>school_meta.group_lessons_enabled</c> — butun maktab
/// uchun bitta qator, shuning uchun uni <see cref="WithGroupLessonsAsync"/> yoqadi va
/// <c>finally</c> da ASL holatiga qaytaradi. Bir kolleksiyadagi testlar ketma-ket yuradi,
/// ya'ni qo'shni klass o'chirgichni yoqilgan holatda ko'rmaydi.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class PupilPortalGroupTests(ApiFixture fixture)
{
    // =====================================================================
    //  1. Jadval
    // =====================================================================

    /// <summary>
    /// O'chirgich o'chiq bo'lsa jadvalda faqat sinf darsi; yoqilgandan keyin
    /// guruh darsi ham qo'shiladi va u <c>ownerKind=group</c> + guruh nomi
    /// bilan belgilanadi (ekran "guruh" yorlig'ini shundan chizadi).
    /// </summary>
    [Fact]
    public async Task Oquvchi_jadvali_guruh_darsini_faqat_ochirgich_yoqilganda_korsatadi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var off = await JsonAsync(admin, $"/api/student/schedule?studentId={w.MemberId}&quarter=1&week={w.Week}");
        Assert.Equal([w.MathName], Subjects(off));

        await WithGroupLessonsAsync(async () =>
        {
            var on = await JsonAsync(admin, $"/api/student/schedule?studentId={w.MemberId}&quarter=1&week={w.Week}");
            Assert.Equal([w.EnglishName, w.MathName], Subjects(on));

            var groupLesson = on.RootElement.EnumerateArray()
                .Single(x => x.GetProperty("subjectName").GetString() == w.EnglishName);
            Assert.Equal(LessonOwnerKind.Group, groupLesson.GetProperty("ownerKind").GetString());
            Assert.Equal(w.GroupName, groupLesson.GetProperty("ownerName").GetString());

            // Guruhda bo'lmagan sinfdosh guruh darsini KO'RMAYDI.
            var outsider = await JsonAsync(admin, $"/api/student/schedule?studentId={w.OutsiderId}&quarter=1&week={w.Week}");
            Assert.Equal([w.MathName], Subjects(outsider));
        });
    }

    // =====================================================================
    //  2. Uyga vazifa, jurnal, bosh sahifa
    // =====================================================================

    /// <summary>
    /// Uyga vazifa va jurnal qatorlari guruh darsidan ham keladi, va qator
    /// egasi bilan belgilanadi.
    /// </summary>
    [Fact]
    public async Task Uyga_vazifa_va_jurnal_guruh_darsini_qoshadi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var offHomework = await JsonAsync(admin, $"/api/student/homework?studentId={w.MemberId}&quarter=1");
        Assert.Equal([w.MathName], Subjects(offHomework));

        await WithGroupLessonsAsync(async () =>
        {
            var homework = await JsonAsync(admin, $"/api/student/homework?studentId={w.MemberId}&quarter=1");
            var groupRow = homework.RootElement.EnumerateArray()
                .Single(x => x.GetProperty("subjectName").GetString() == w.EnglishName);
            Assert.Equal(LessonOwnerKind.Group, groupRow.GetProperty("ownerKind").GetString());
            Assert.Equal(w.GroupName, groupRow.GetProperty("ownerName").GetString());
            Assert.Equal(w.GroupTopic, groupRow.GetProperty("topic").GetString());
            Assert.Equal(3, groupRow.GetProperty("grade").GetInt32());

            var journal = await JsonAsync(admin, $"/api/student/journal?studentId={w.MemberId}&quarter=1&week={w.Week}");
            var journalGroupRow = journal.RootElement.EnumerateArray()
                .Single(x => x.GetProperty("subjectName").GetString() == w.EnglishName);
            Assert.Equal(w.GroupTopic, journalGroupRow.GetProperty("topic").GetString());
            Assert.Equal(w.GroupName, journalGroupRow.GetProperty("ownerName").GetString());
        });
    }

    /// <summary>
    /// Fan progresi ekranida guruh fani paydo bo'ladi (G-18) — va o'chirgich
    /// o'chiq bo'lsa yo'q.
    /// </summary>
    [Fact]
    public async Task Fan_progresi_guruh_fanini_qoshadi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var off = await JsonAsync(admin, $"/api/student/subjects-progress?studentId={w.MemberId}&quarter=1");
        Assert.DoesNotContain(w.EnglishName, ProgressSubjects(off));

        await WithGroupLessonsAsync(async () =>
        {
            var on = await JsonAsync(admin, $"/api/student/subjects-progress?studentId={w.MemberId}&quarter=1");
            Assert.Contains(w.EnglishName, ProgressSubjects(on));
        });
    }

    // =====================================================================
    //  3. Ota-ona Mini App
    // =====================================================================

    /// <summary>
    /// Ota-ona Mini App jadvali ham guruh darsini ko'rsatadi, va vasiylik
    /// bo'yicha FAQAT o'z farzandini: begona bolaning id'si 404 beradi
    /// (403 emas — begona bolaning mavjudligi ham ma'lumot).
    /// </summary>
    [Fact]
    public async Task Ota_ona_mini_app_guruh_darsini_koradi_va_faqat_oz_farzandini()
    {
        var w = await SeedAsync();
        var parentUserId = await SeedGuardianAsync(w.Tag, w.MemberId);
        using var parent = fixture.Api.ClientWithToken(
            fixture.Api.TokenFor("parent", parentUserId, "Ota-ona"));

        await WithGroupLessonsAsync(async () =>
        {
            var schedule = await JsonAsync(
                parent, $"/api/tg/parent/children/{w.MemberId}/schedule?quarter=1&week={w.Week}");
            var groupLesson = schedule.RootElement.EnumerateArray()
                .Single(x => x.GetProperty("subjectName").GetString() == w.EnglishName);
            Assert.Equal(w.GroupName, groupLesson.GetProperty("ownerName").GetString());

            // RBAC: boshqa oilaning bolasi — 404.
            var foreign = await parent.GetAsync(
                $"/api/tg/parent/children/{w.OutsiderId}/schedule?quarter=1&week={w.Week}");
            Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);

            var foreignAttendance = await parent.GetAsync(
                $"/api/tg/parent/children/{w.OutsiderId}/attendance");
            Assert.Equal(HttpStatusCode.NotFound, foreignAttendance.StatusCode);
        });
    }

    /// <summary>
    /// Guruh darsidagi davomatsizlik ota-ona davomat ro'yxatiga tushadi.
    /// </summary>
    [Fact]
    public async Task Guruh_darsidagi_davomatsizlik_ota_ona_royxatida_korinadi()
    {
        var w = await SeedAsync();
        var parentUserId = await SeedGuardianAsync(w.Tag, w.MemberId);
        using var parent = fixture.Api.ClientWithToken(
            fixture.Api.TokenFor("parent", parentUserId, "Ota-ona"));

        await fixture.Api.WithDbAsync(async db =>
        {
            db.JournalEntries.Add(new JournalEntry
            {
                ClassId = w.GroupId, OwnerKind = LessonOwnerKind.Group, SubjectId = w.EnglishId,
                Quarter = 1, StudentId = w.MemberId, Date = w.LessonDate, Period = 2,
                ReasonId = w.ReasonId,
            });
            await db.SaveChangesAsync();
        });

        var off = await JsonAsync(parent, $"/api/tg/parent/children/{w.MemberId}/attendance");
        Assert.Empty(off.RootElement.GetProperty("rows").EnumerateArray());

        await WithGroupLessonsAsync(async () =>
        {
            var on = await JsonAsync(parent, $"/api/tg/parent/children/{w.MemberId}/attendance");
            var row = Assert.Single(on.RootElement.GetProperty("rows").EnumerateArray());
            Assert.Equal(w.EnglishName, row.GetProperty("subjectName").GetString());
        });
    }

    // =====================================================================
    //  4. RBAC — o'quvchi faqat o'zini ko'radi
    // =====================================================================

    /// <summary>
    /// <c>student</c> roli <c>?studentId=</c> bilan BOSHQA bolaning ma'lumotini
    /// ola olmaydi: parametr e'tiborsiz qoladi va javob o'z jadvali bo'ladi.
    /// Admin esa <c>?studentId=</c> siz 400 oladi.
    /// </summary>
    [Fact]
    public async Task Oquvchi_ozganing_jadvalini_kora_olmaydi_admin_esa_studentId_talab_qiladi()
    {
        var w = await SeedAsync();
        var (pupilUser, _) = await fixture.Api.SeedUserAsync(Roles.Student);
        await fixture.Api.WithDbAsync(async db =>
        {
            var me = await db.Students.SingleAsync(s => s.Id == w.MemberId);
            me.UserId = pupilUser.Id;
            await db.SaveChangesAsync();
        });
        using var student = fixture.Api.ClientWithToken(
            fixture.Api.TokenFor(Roles.Student, pupilUser.Id, "O'quvchi"));

        await WithGroupLessonsAsync(async () =>
        {
            // Begona id berildi — baribir O'ZINING (guruh darsi bor) jadvali qaytadi.
            var spoofed = await JsonAsync(
                student, $"/api/student/schedule?studentId={w.OutsiderId}&quarter=1&week={w.Week}");
            Assert.Equal([w.EnglishName, w.MathName], Subjects(spoofed));

            using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
            var noId = await admin.GetAsync($"/api/student/schedule?quarter=1&week={w.Week}");
            Assert.Equal(HttpStatusCode.BadRequest, noId.StatusCode);
        });
    }

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    private static List<string?> Subjects(JsonDocument doc) =>
        [.. doc.RootElement.EnumerateArray()
            .Select(x => x.GetProperty("subjectName").GetString())
            .Distinct()
            .Order(StringComparer.Ordinal)];

    private static List<string?> ProgressSubjects(JsonDocument doc) =>
        [.. doc.RootElement.GetProperty("subjects").EnumerateArray()
            .Select(x => x.GetProperty("subjectName").GetString())];

    private static async Task<JsonDocument> JsonAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"{url} → {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// Cut-over o'chirgichini yoqib turadi va ASL holatiga qaytaradi. Qaytarish
    /// <c>finally</c> da: test yiqilsa ham o'chirgich yoqilgan qolib ketmasin,
    /// aks holda keyingi klasslar boshqa raqam ko'rardi.
    /// </summary>
    private async Task WithGroupLessonsAsync(Func<Task> body)
    {
        bool before = false;
        string? createdId = null;
        await fixture.Api.WithDbAsync(async db =>
        {
            var meta = await db.SchoolMeta.OrderBy(m => m.Id).FirstOrDefaultAsync();
            if (meta is null)
            {
                // Avval INSERT, keyin UPDATE — `SchoolMetaFlags` dagi bilan bir xil sabab.
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

    private sealed record World(
        string Tag, string ClassId, string GroupId, string GroupName,
        string MathId, string MathName, string EnglishId, string EnglishName,
        string ReasonId, string MemberId, string OutsiderId,
        string LessonDate, string GroupTopic, int Week);

    /// <summary>
    /// Bitta sinf, ikkita o'quvchi (biri guruhda, biri yo'q), sinf jadvalida
    /// matematika va guruh jadvalida ingliz tili — ikkalasi ham 1-chorak
    /// 1-haftaga biriktirilgan, mavzulari va baholari bilan.
    ///
    /// <para>
    /// Umumiy bazada yuradi, shuning uchun har nom TAG bilan noyob qilinadi va
    /// har so'rov aniq o'quvchi id'si bilan chaqiriladi — qo'shni testlarning
    /// ma'lumoti aralashib ketmaydi.
    /// </para>
    /// </summary>
    private async Task<World> SeedAsync()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var cls = new SchoolClass { Name = $"7-{tag}", Grade = 7 };
        var math = new Subject { Name = $"Matematika {tag}" };
        var english = new Subject { Name = $"Ingliz tili {tag}", IsGroupable = true };
        var teacher = new Teacher { FullName = $"Ustoz {tag}", Category = "oliy" };
        var groupTeacher = new Teacher { FullName = $"Guruh ustozi {tag}", Category = "oliy" };
        var reason = new AbsenceReason { Name = $"Sababli {tag}", Short = "S" };

        var member = GeneralSettingsFlagsTests.NewStudent($"Guruhli {tag}", cls.Name, "+99890" + Rnd());
        var outsider = GeneralSettingsFlagsTests.NewStudent($"Guruhsiz {tag}", cls.Name, "+99890" + Rnd());

        // `study_groups.created_by` → `users(id)` RESTRICT: haqiqiy foydalanuvchi kerak.
        var (creator, _) = await fixture.Api.SeedUserAsync(Roles.Admin);
        var creatorId = creator.Id;
        var group = new StudyGroup
        {
            Name = $"Kuchli ingliz {tag}",
            SubjectId = english.Id,
            CreatedAt = AppClock.NowInstant,
        };
        var groupId = group.Id.ToString();
        var topic = $"Guruh mavzusi {tag}";
        string lessonDate = "";
        var weekNo = 1;

        await fixture.Api.WithDbAsync(async db =>
        {
            db.Classes.Add(cls);
            db.Subjects.AddRange(math, english);
            db.Teachers.AddRange(teacher, groupTeacher);
            db.AbsenceReasons.Add(reason);
            db.Students.AddRange(member, outsider);

            var quarter = await db.Quarters.FirstOrDefaultAsync(q => q.Quarter == 1);
            if (quarter is null)
            {
                quarter = new QuarterPeriod { Quarter = 1, StartDate = "2026-09-01", EndDate = "2026-12-31" };
                db.Quarters.Add(quarter);
            }

            var classTpl = new ScheduleTemplate { ClassId = cls.Id, Name = $"Sinf {tag}" };
            classTpl.Lessons.Add(new ScheduleLesson
            {
                Day = 0, Period = 1, SubjectId = math.Id, TeacherId = teacher.Id,
            });
            var groupTpl = new ScheduleTemplate
            {
                ClassId = groupId, Name = $"Guruh {tag}", OwnerKind = LessonOwnerKind.Group,
            };
            groupTpl.Lessons.Add(new ScheduleLesson
            {
                Day = 0, Period = 2, SubjectId = english.Id, TeacherId = groupTeacher.Id,
            });
            db.ScheduleTemplates.AddRange(classTpl, groupTpl);

            group.CreatedBy = creatorId;
            db.StudyGroups.Add(group);
            db.StudyGroupClasses.Add(new StudyGroupClass { GroupId = group.Id, ClassId = cls.Id });
            db.StudyGroupTeachers.Add(new StudyGroupTeacher { GroupId = group.Id, TeacherId = groupTeacher.Id });
            db.StudyGroupMembers.Add(new StudyGroupMember
            {
                GroupId = group.Id, SubjectId = english.Id, StudentId = member.Id,
                JoinedOn = new DateOnly(2026, 9, 1),
                CreatedBy = group.CreatedBy, CreatedAt = AppClock.NowInstant,
            });

            // Chorak dushanbadan boshlanmasligi mumkin — dushanbasi chorak ICHIDA
            // bo'lgan birinchi haftani olamiz, aks holda jadval Day=0 darsini
            // chorak chegarasidan tashqarida deb tashlab yuborardi.
            var week = ScheduleMath.GetQuarterWeeks(quarter.StartDate, quarter.EndDate)
                .First(x => string.CompareOrdinal(
                    ScheduleMath.MondayOfISO(x.StartISO), quarter.StartDate) >= 0);
            weekNo = week.Week;
            lessonDate = ScheduleMath.MondayOfISO(week.StartISO);

            db.WeekAssignments.AddRange(
                new WeekAssignment { ClassId = cls.Id, Quarter = 1, Week = weekNo, TemplateId = classTpl.Id },
                new WeekAssignment
                {
                    ClassId = groupId, Quarter = 1, Week = weekNo,
                    TemplateId = groupTpl.Id, OwnerKind = LessonOwnerKind.Group,
                });

            db.LessonNotes.AddRange(
                new LessonNote
                {
                    ClassId = cls.Id, SubjectId = math.Id, Quarter = 1, Date = lessonDate,
                    Period = 1, Topic = $"Sinf mavzusi {tag}", Conducted = true,
                },
                new LessonNote
                {
                    ClassId = groupId, OwnerKind = LessonOwnerKind.Group, SubjectId = english.Id,
                    Quarter = 1, Date = lessonDate, Period = 2, Topic = topic, Conducted = true,
                });

            db.JournalEntries.AddRange(
                new JournalEntry
                {
                    ClassId = cls.Id, SubjectId = math.Id, Quarter = 1, StudentId = member.Id,
                    Date = lessonDate, Period = 1, Grade = 5,
                },
                new JournalEntry
                {
                    ClassId = groupId, OwnerKind = LessonOwnerKind.Group, SubjectId = english.Id,
                    Quarter = 1, StudentId = member.Id, Date = lessonDate, Period = 2, Grade = 3,
                });

            await db.SaveChangesAsync();
        });

        return new World(
            tag, cls.Id, groupId, group.Name,
            math.Id, math.Name, english.Id, english.Name,
            reason.Id, member.Id, outsider.Id, lessonDate, topic, weekNo);
    }

    /// <summary>Farzandlarga bog'langan vasiy va uning <c>parent</c> akkaunti.</summary>
    private async Task<string> SeedGuardianAsync(string tag, params string[] studentIds)
    {
        var phone = "+99897" + Rnd();
        var (user, _) = await fixture.Api.SeedUserAsync("parent", email: phone);
        await fixture.Api.WithDbAsync(async db =>
        {
            var guardian = new Guardian { FullName = $"Vasiy {tag}", Phone = phone, UserId = user.Id };
            db.Guardians.Add(guardian);
            foreach (var id in studentIds)
                db.StudentGuardians.Add(new StudentGuardian
                {
                    StudentId = id, GuardianId = guardian.Id, IsPrimary = true,
                });
            await db.SaveChangesAsync();
        });
        return user.Id;
    }

    private static string Rnd() => Random.Shared.Next(1_000_000, 9_999_999).ToString();
}
