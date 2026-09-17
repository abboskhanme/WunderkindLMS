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
/// G-15 — hisobotlar guruh darslarini qanday sanaydi
/// (docs/modules/students-parity.md §2.1.6, §4.3 slice C3).
///
/// <para>
/// <b>Isbotlanadigan yagona qoida</b> (<see cref="ClassAttainment"/> fayl boshidagi izoh):
/// o'quvchining bahosi HAR DOIM uning sinf rahbarligidagi sinfi ostida sanaladi, guruh
/// hisobotda alohida qator bo'lmaydi, har qator BIR MARTA sanaladi va guruh qatori faqat
/// o'sha guruhning faol a'zosiga tegishli.
/// </para>
/// <para>
/// <b>Va cut-over kafolati:</b> <c>school_meta.group_lessons_enabled</c> O'CHIQ ekan har
/// bir raqam bugungisining aynan o'zi. Shuning uchun har hisobot IKKI MARTA — o'chiq va
/// yoqilgan holatda — tekshiriladi va o'chiq holatdagi kutilgan qiymat guruh umuman
/// bo'lmagandagi qiymat bilan bir xil.
/// </para>
/// <para>
/// <b>Nega o'z bazasi.</b> O'chirgich butun maktab uchun BITTA qator; umumiy bazada uni
/// yoqib-o'chirish qo'shni test klassining natijasini siljitardi. Har test o'z bazasini
/// oladi, hovuzlar <see cref="DisposeAsync"/> da tozalanadi — aks holda konteynerdagi
/// <c>max_connections</c> tugaydi va keyingi klasslar <c>53300</c> bilan yiqiladi.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class ClassAttainmentTests(ApiFixture fixture) : IAsyncLifetime
{
    private readonly List<string> _connectionStrings = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        foreach (var connectionString in _connectionStrings)
            NpgsqlConnection.ClearPool(new NpgsqlConnection(connectionString));
        return Task.CompletedTask;
    }

    // =====================================================================
    //  1. Sinf o'zlashtirishi — `GET /api/admin/classes/{id}/performance`
    // =====================================================================

    /// <summary>
    /// O'chirgich o'chiq: guruh a'zoligi ham, guruh bahosi ham BOR, lekin sinf
    /// o'zlashtirishi guruhsiz maktabdagi kabi — faqat sinf darsidagi baho.
    /// </summary>
    [Fact]
    public async Task Ochirgich_ochiq_sinf_ozlashtirishi_guruhni_umuman_kormaydi()
    {
        await using var db = await NewDbAsync("perf_off");
        var w = await SeedAsync(db);

        var res = await new ClassAnalyticsController(db).Performance(w.ClassAId);
        var data = Assert.IsType<ClassPerformanceDataDto>(res.Value);

        Assert.Equal(["Matematika"], data.Subjects.Select(s => s.Name));
        var a1 = Row(data, w.A1);
        Assert.Equal(5, a1.Average);            // faqat sinfdagi matematika "5"
        Assert.Equal(100, a1.Attendance);       // maxraj — faqat sinf darsi
        Assert.Equal(5, Row(data, w.A2).Average);
        // Guruhning 5-B lik a'zosi 5-A qatorlarida yo'q.
        Assert.DoesNotContain(w.B1, data.Rows.Select(r => r.Student.Id));
    }

    /// <summary>
    /// O'chirgich yoqilgan: guruh bahosi o'quvchining SINFI ostida sanaladi
    /// (o'rtacha 5 dan 4 ga tushadi), guruh fani ustun bo'lib qo'shiladi, va
    /// guruh darsi davomat maxrajiga kiradi. Guruhda bo'lmagan sinfdoshning
    /// raqami esa O'ZGARMAYDI.
    /// </summary>
    [Fact]
    public async Task Ochirgich_yoqilgan_guruh_bahosi_oquvchining_sinfi_ostida_sanaladi()
    {
        await using var db = await NewDbAsync("perf_on");
        var w = await SeedAsync(db);
        await SetGroupLessonsAsync(db, true);

        var res = await new ClassAnalyticsController(db).Performance(w.ClassAId);
        var data = Assert.IsType<ClassPerformanceDataDto>(res.Value);

        Assert.Contains("Ingliz tili", data.Subjects.Select(s => s.Name));

        var a1 = Row(data, w.A1);
        Assert.Equal(5, a1.Grades[w.MathId]);
        Assert.Equal(3, a1.Grades[w.EnglishId]);
        Assert.Equal(4, a1.Average);            // (5 + 3) / 2
        Assert.Equal(100, a1.Attendance);       // ikkala dars ham o'tilgan, qoldirmagan

        // Guruhda bo'lmagan A2 — guruh fanidan bahosi yo'q, o'rtachasi o'sha-o'sha.
        var a2 = Row(data, w.A2);
        Assert.Equal(5, a2.Average);
        Assert.Equal(0, a2.Grades[w.EnglishId]);

        // Guruh o'z qatori bo'lib chiqmaydi va begona sinf o'quvchisi ham qo'shilmaydi.
        Assert.DoesNotContain(w.B1, data.Rows.Select(r => r.Student.Id));
    }

    /// <summary>
    /// Guruhni IKKI sinf boqadi: har bolaning bahosi FAQAT o'z sinfi qatorida
    /// ko'rinadi. Ya'ni bitta guruh bahosi ikki sinfga qo'shilib ketmaydi.
    /// </summary>
    [Fact]
    public async Task Guruh_bahosi_faqat_bolaning_oz_sinfida_sanaladi()
    {
        await using var db = await NewDbAsync("perf_two");
        var w = await SeedAsync(db);
        await SetGroupLessonsAsync(db, true);

        var controller = new ClassAnalyticsController(db);
        var inB = Assert.IsType<ClassPerformanceDataDto>((await controller.Performance(w.ClassBId)).Value);

        var b1 = Row(inB, w.B1);
        Assert.Equal(3, b1.Grades[w.EnglishId]);
        Assert.Equal(4, b1.Average);
        Assert.DoesNotContain(w.A1, inB.Rows.Select(r => r.Student.Id));
    }

    // =====================================================================
    //  2. Fanlar bo'yicha o'zlashtirish pivoti — grades-report/subjects
    // =====================================================================

    /// <summary>
    /// Pivotda guruh ALOHIDA QATOR bo'lmaydi: qatorlar faqat sinflar va maktab
    /// jamlamasi. Guruh fani esa sinfning USTUNI bo'lib qo'shiladi.
    /// </summary>
    [Fact]
    public async Task Pivotda_guruh_alohida_qator_bolmaydi_ustun_bolib_qoshiladi()
    {
        await using var db = await NewDbAsync("pivot");
        var w = await SeedAsync(db);

        var off = await SubjectAttainmentReport.BuildAsync(db, [w.ClassAId, w.ClassBId], [1]);
        Assert.Equal(["Matematika"], off.Subjects.Select(c => c.Name));
        Assert.All(off.Rows, r => Assert.Equal("class", r.Kind));

        await SetGroupLessonsAsync(db, true);
        var on = await SubjectAttainmentReport.BuildAsync(db, [w.ClassAId, w.ClassBId], [1]);

        Assert.Contains("Ingliz tili", on.Subjects.Select(c => c.Name));
        // Qatorlar hamon faqat SINFLAR — guruh nomi bilan qator yo'q.
        Assert.Equal(["class", "class"], on.Rows.Select(r => r.Kind));
        Assert.Equal(["5-A", "5-B"], on.Rows.Select(r => r.ClassName));

        var rowA = on.Rows.Single(r => r.ClassId == w.ClassAId);
        var english = rowA.Cells.Single(c => c.SubjectId == w.EnglishId);
        Assert.Equal(1, english.Values);        // faqat A1 ning bahosi
        Assert.Equal(3, english.Average);
    }

    /// <summary>
    /// Bitta (o'quvchi, fan, chorak) katakka BIR MARTA tushadi: bola shu fanni
    /// sinfda ham, guruhda ham o'qisa ikkala manbaning baholari BIRGA
    /// o'rtachalanadi (ikki alohida qiymat bo'lib emas).
    /// </summary>
    [Fact]
    public async Task Sinf_va_guruh_bir_fanda_bolsa_baholar_birga_ortachalanadi()
    {
        await using var db = await NewDbAsync("pivot_both");
        var w = await SeedAsync(db);
        await SetGroupLessonsAsync(db, true);

        // A1 ga SINFning ingliz tili darsidan ham baho qo'yildi ("5"),
        // guruhdagisi esa "3" edi.
        await db.JournalEntries.AddAsync(new JournalEntry
        {
            ClassId = w.ClassAId, SubjectId = w.EnglishId, Quarter = 1,
            StudentId = w.A1, Date = w.LessonDate, Period = 5, Grade = 5,
        });
        await db.SaveChangesAsync();

        var on = await SubjectAttainmentReport.BuildAsync(db, [w.ClassAId], [1]);
        var english = on.Rows.Single(r => r.ClassId == w.ClassAId).Cells.Single(c => c.SubjectId == w.EnglishId);

        // Ikki baho, lekin BITTA katak qiymati: (5 + 3) / 2 = 4.
        Assert.Equal(1, english.Values);
        Assert.Equal(4, english.Average);
    }

    // =====================================================================
    //  3. Bitta o'quvchining hisoboti — StudentReportBuilder
    // =====================================================================

    /// <summary>
    /// O'quvchi hisoboti: o'chirgich o'chiq bo'lsa guruh fani umuman yo'q;
    /// yoqilgandan keyin guruh bahosi ham, guruh darsidagi davomatsizlik ham
    /// hisobotga kiradi.
    /// </summary>
    [Fact]
    public async Task Oquvchi_hisoboti_guruh_bahosini_va_davomatsizligini_qoshadi()
    {
        await using var db = await NewDbAsync("student_report");
        var w = await SeedAsync(db);
        var a1 = await db.Students.SingleAsync(s => s.Id == w.A1);

        var off = await StudentReportBuilder.BuildAsync(db, a1);
        Assert.DoesNotContain(w.EnglishId, off.Grades.Keys);
        Assert.Equal(0, off.Attendance.MissedLessons.GetValueOrDefault(1));

        // Guruh darsini qoldirdi (sababli).
        await db.JournalEntries.AddAsync(new JournalEntry
        {
            ClassId = w.GroupId, OwnerKind = LessonOwnerKind.Group, SubjectId = w.EnglishId,
            Quarter = 1, StudentId = w.A1, Date = w.LessonDate, Period = 2, ReasonId = w.ReasonId,
        });
        await db.SaveChangesAsync();
        await SetGroupLessonsAsync(db, true);

        var on = await StudentReportBuilder.BuildAsync(db, a1);
        Assert.Equal(3, on.Grades[w.EnglishId][1]);
        Assert.Equal(1, on.Attendance.MissedLessons.GetValueOrDefault(1));
    }

    // =====================================================================
    //  4. Reyting — RatingService
    // =====================================================================

    /// <summary>
    /// Maktab reytingi guruh bahosini o'quvchining sinfi ostida sanaydi va
    /// qator sonini O'ZGARTIRMAYDI (guruh yangi qator qo'shmaydi).
    /// </summary>
    [Fact]
    public async Task Reyting_guruh_bahosini_sinf_ostida_sanaydi()
    {
        await using var db = await NewDbAsync("rating");
        var w = await SeedAsync(db);

        var off = await RatingService.SchoolAsync(db);
        Assert.Equal(3, off.Count);
        Assert.Equal(5, off.Single(r => r.Student.Id == w.A1).Average);

        await SetGroupLessonsAsync(db, true);
        var on = await RatingService.SchoolAsync(db);
        Assert.Equal(3, on.Count);
        var row = on.Single(r => r.Student.Id == w.A1);
        Assert.Equal(4, row.Average);
        Assert.Equal("5-A", row.ClassName);     // guruh emas, SINF nomi
    }

    // =====================================================================
    //  5. Fan progresi — SubjectProgressService
    // =====================================================================

    /// <summary>
    /// O'quvchining fan progresi guruh darslarini ham sanaydi (G-18), va
    /// o'chirgich o'chiq bo'lsa faqat sinfnikini.
    /// </summary>
    [Fact]
    public async Task Fan_progresi_guruh_darsini_ham_sanaydi()
    {
        await using var db = await NewDbAsync("progress");
        var w = await SeedAsync(db);
        var a1 = await db.Students.SingleAsync(s => s.Id == w.A1);

        var off = await SubjectProgressService.ForStudentAsync(db, a1, 1);
        Assert.DoesNotContain(w.EnglishId, off.Subjects.Select(s => s.SubjectId));

        await SetGroupLessonsAsync(db, true);
        var on = await SubjectProgressService.ForStudentAsync(db, a1, 1);
        var english = on.Subjects.Single(s => s.SubjectId == w.EnglishId);
        Assert.True(english.Planned > 0, "guruh darslari rejaga kirmadi");
        Assert.Equal(1, english.Conducted);

        // Guruhda bo'lmagan sinfdoshda guruh fani yo'q.
        var a2 = await db.Students.SingleAsync(s => s.Id == w.A2);
        var other = await SubjectProgressService.ForStudentAsync(db, a2, 1);
        Assert.DoesNotContain(w.EnglishId, other.Subjects.Select(s => s.SubjectId));
    }

    // =====================================================================
    //  6. O'qituvchi faolligi — TeacherActivityReport
    // =====================================================================

    /// <summary>
    /// Faqat guruhda dars beradigan o'qituvchi: o'chirgich o'chiq bo'lsa
    /// hisobotda "faollik yo'q", yoqilgandan keyin guruh darslari uning
    /// nomiga yoziladi va kesim qatori GURUH deb belgilanadi.
    /// </summary>
    [Fact]
    public async Task Guruh_oqituvchisining_darslari_faqat_ochirgich_yoqilganda_sanaladi()
    {
        await using var db = await NewDbAsync("teacher_report");
        var w = await SeedAsync(db);

        var off = (await TeacherActivityReport.BuildOverviewAsync(db, 1))
            .Single(r => r.TeacherId == w.GroupTeacherId);
        Assert.Equal("none", off.Status);
        Assert.Equal(0, off.Conducted);

        await SetGroupLessonsAsync(db, true);
        var detail = await TeacherActivityReport.BuildDetailAsync(db, w.GroupTeacherId, 1);
        Assert.NotNull(detail);
        Assert.Equal(1, detail!.Conducted);
        var row = Assert.Single(detail.Rows);
        Assert.Equal(LessonOwnerKind.Group, row.OwnerKind);
        Assert.Equal("Kuchli ingliz", row.ClassName);
        Assert.Equal("Ingliz tili", row.SubjectName);
    }

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    private static ClassStudentRowDto Row(ClassPerformanceDataDto data, string studentId) =>
        data.Rows.Single(r => r.Student.Id == studentId);

    private async Task<AppDbContext> NewDbAsync(string prefix)
    {
        var database = await fixture.Postgres.CreateDatabaseAsync("attain_" + prefix);
        _connectionStrings.Add(database.OwnerConnectionString);
        return PostgresFixture.NewContext(database.OwnerConnectionString);
    }

    internal static async Task SetGroupLessonsAsync(AppDbContext db, bool enabled)
    {
        var meta = await db.SchoolMeta.FirstAsync();
        meta.GroupLessonsEnabled = enabled;
        await db.SaveChangesAsync();
    }

    /// <summary>Seed natijasi — testlar shu id'lar bilan ishlaydi.</summary>
    private sealed record World(
        string ClassAId, string ClassBId, string GroupId,
        string MathId, string EnglishId, string ReasonId,
        string GroupTeacherId, string ClassTeacherId,
        string A1, string A2, string B1,
        string LessonDate);

    /// <summary>
    /// Ikki sinf (5-A, 5-B), har birida dushanba 1-darsda matematika; IKKALA
    /// sinfdan yig'ilgan "Kuchli ingliz" guruhi dushanba 2-darsda o'qiydi
    /// (a'zolari — A1 va B1). Matematikadan hamma "5", guruhdagi ingliz
    /// tilidan A1 va B1 "3" oladi. Barcha dars kunlari O'TGAN kunlar, ya'ni
    /// "reja" (Expected) hisoblanadi.
    ///
    /// <para>O'chirgich boshida O'CHIQ — har test uni o'zi yoqadi.</para>
    /// </summary>
    private static async Task<World> SeedAsync(AppDbContext db)
    {
        db.SchoolMeta.Add(new SchoolMeta { CurrentYear = "2026/2027", Name = "Test maktab" });
        var quarter = new QuarterPeriod { Quarter = 1, StartDate = "2026-09-01", EndDate = "2026-12-31" };
        db.Quarters.Add(quarter);

        var reason = new AbsenceReason { Name = "Sababli", Short = "S" };
        db.AbsenceReasons.Add(reason);

        var classA = new SchoolClass { Name = "5-A", Grade = 5 };
        var classB = new SchoolClass { Name = "5-B", Grade = 5 };
        db.Classes.AddRange(classA, classB);

        var math = new Subject { Name = "Matematika" };
        var english = new Subject { Name = "Ingliz tili", IsGroupable = true };
        db.Subjects.AddRange(math, english);

        var classTeacher = new Teacher { FullName = "Sinf ustozi", Category = "oliy" };
        var groupTeacher = new Teacher { FullName = "Guruh ustozi", Category = "oliy" };
        db.Teachers.AddRange(classTeacher, groupTeacher);

        var a1 = NewStudent("Anvar Anvarov", "5-A");
        var a2 = NewStudent("Aziza Azizova", "5-A");
        var b1 = NewStudent("Bobur Boburov", "5-B");
        db.Students.AddRange(a1, a2, b1);

        var tplA = Template(classA.Id, "5-A asosiy", math.Id, classTeacher.Id, period: 1);
        var tplB = Template(classB.Id, "5-B asosiy", math.Id, classTeacher.Id, period: 1);
        db.ScheduleTemplates.AddRange(tplA, tplB);

        var createdBy = await SystemUserIdAsync(db);
        var group = new StudyGroup
        {
            Name = "Kuchli ingliz",
            SubjectId = english.Id,
            CreatedBy = createdBy,
            CreatedAt = AppClock.NowInstant,
        };
        db.StudyGroups.Add(group);
        db.StudyGroupClasses.AddRange(
            new StudyGroupClass { GroupId = group.Id, ClassId = classA.Id },
            new StudyGroupClass { GroupId = group.Id, ClassId = classB.Id });
        db.StudyGroupTeachers.Add(new StudyGroupTeacher { GroupId = group.Id, TeacherId = groupTeacher.Id });

        var joined = new DateOnly(2026, 9, 1);
        foreach (var studentId in new[] { a1.Id, b1.Id })
            db.StudyGroupMembers.Add(new StudyGroupMember
            {
                GroupId = group.Id, SubjectId = english.Id, StudentId = studentId,
                JoinedOn = joined, CreatedBy = createdBy, CreatedAt = AppClock.NowInstant,
            });

        var groupId = group.Id.ToString();
        var groupTpl = Template(groupId, "Guruh jadvali", english.Id, groupTeacher.Id, period: 2);
        groupTpl.OwnerKind = LessonOwnerKind.Group;
        db.ScheduleTemplates.Add(groupTpl);


        // Jadvaldan kelib chiqadigan ANIQ sana. Chorak dushanbadan boshlanmasligi
        // mumkin, o'sha holda 1-haftaning dushanbasi chorak CHEGARASIDAN tashqarida
        // qoladi va jadval uni tashlab yuboradi — shuning uchun dushanbasi chorak
        // ichida bo'lgan BIRINCHI hafta olinadi.
        var week = ScheduleMath.GetQuarterWeeks(quarter.StartDate, quarter.EndDate)
            .First(x => string.CompareOrdinal(
                ScheduleMath.MondayOfISO(x.StartISO), quarter.StartDate) >= 0);
        var weekNo = week.Week;
        var date = ScheduleMath.MondayOfISO(week.StartISO);

        db.WeekAssignments.AddRange(
            new WeekAssignment { ClassId = classA.Id, Quarter = 1, Week = weekNo, TemplateId = tplA.Id },
            new WeekAssignment { ClassId = classB.Id, Quarter = 1, Week = weekNo, TemplateId = tplB.Id },
            new WeekAssignment
            {
                ClassId = groupId, Quarter = 1, Week = weekNo,
                TemplateId = groupTpl.Id, OwnerKind = LessonOwnerKind.Group,
            });

        db.LessonNotes.AddRange(
            Note(classA.Id, math.Id, date, 1),
            Note(classB.Id, math.Id, date, 1),
            Note(groupId, english.Id, date, 2, LessonOwnerKind.Group));

        foreach (var (studentId, classId) in new[] { (a1.Id, classA.Id), (a2.Id, classA.Id), (b1.Id, classB.Id) })
            db.JournalEntries.Add(new JournalEntry
            {
                ClassId = classId, SubjectId = math.Id, Quarter = 1,
                StudentId = studentId, Date = date, Period = 1, Grade = 5,
            });
        foreach (var studentId in new[] { a1.Id, b1.Id })
            db.JournalEntries.Add(new JournalEntry
            {
                ClassId = groupId, OwnerKind = LessonOwnerKind.Group, SubjectId = english.Id,
                Quarter = 1, StudentId = studentId, Date = date, Period = 2, Grade = 3,
            });

        await db.SaveChangesAsync();

        return new World(
            classA.Id, classB.Id, groupId,
            math.Id, english.Id, reason.Id,
            groupTeacher.Id, classTeacher.Id,
            a1.Id, a2.Id, b1.Id, date);
    }

    private static ScheduleTemplate Template(
        string ownerId, string name, string subjectId, string teacherId, int period) => new()
    {
        ClassId = ownerId,
        Name = name,
        Lessons = [new ScheduleLesson { Day = 0, Period = period, SubjectId = subjectId, TeacherId = teacherId }],
    };

    private static LessonNote Note(
        string ownerId, string subjectId, string date, int period,
        string ownerKind = LessonOwnerKind.Class) => new()
    {
        ClassId = ownerId, OwnerKind = ownerKind, SubjectId = subjectId, Quarter = 1,
        Date = date, Period = period, SubGroup = 0, Topic = "Mavzu", Conducted = true,
    };

    private static Student NewStudent(string fullName, string className) => new()
    {
        FullName = fullName,
        LastName = fullName.Split(' ')[^1],
        FirstName = fullName.Split(' ')[0],
        BirthDate = "2014-01-01",
        Gender = "male",
        ClassName = className,
        EnrollmentDate = "2026-09-01",
    };

    /// <summary><c>created_by</c> ustuni <c>users(id)</c> ga RESTRICT bilan bog'langan.</summary>
    private static async Task<string> SystemUserIdAsync(AppDbContext db)
    {
        var existing = await db.Users.FirstOrDefaultAsync();
        if (existing is not null) return existing.Id;
        var user = new AppUser
        {
            FullName = "Seed",
            Role = Roles.Admin,
            Email = $"class-attainment.{Guid.NewGuid():N}",
        };
        user.SetInitialPassword("Test-" + Guid.NewGuid().ToString("N")[..10]);
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }
}
