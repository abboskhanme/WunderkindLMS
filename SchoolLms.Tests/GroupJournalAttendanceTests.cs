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
/// G-12 (jurnal) va G-13 (davomat) — docs/modules/students-parity.md §2.1.6, §4.3.
///
/// <para>
/// <b>Har test IKKI HOLATNI ham yozadi.</b> Cut-over o'chirgichi
/// (<c>school_meta.group_lessons_enabled</c>) o'chiq bo'lganda javob BUGUNGI
/// javob bo'lishi shart — ya'ni guruh a'zoligi ham, guruh jadvali ham, guruh
/// darsining izohi ham bazada TURSA HAM hech bir raqamga ta'sir qilmasligi
/// kerak. Shuning uchun quyidagi olam guruh bilan birga quriladi, keyin
/// o'chirgich o'chiq holatda "bugungi" raqam, yoqilgan holatda esa "yangi"
/// raqam OCHIQ yoziladi.
/// </para>
/// <para>
/// <b>Nega o'z bazasi.</b> O'chirgich butun maktabga bitta qator, davomat
/// analitikasi esa butun maktabni yig'adi: umumiy bazada bu testlar
/// qo'shnilarining raqamini o'zgartirardi.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class GroupJournalAttendanceTests(ApiFixture fixture) : IAsyncLifetime
{
    /// <summary>Chorak 1 = 2026-09-01 … 2026-12-31; 1-hafta dushanbasi 2026-08-31.</summary>
    private const int Quarter = 1;

    /// <summary>Guruh darsi shu sanada o'tilgan (chorakning 1-haftasi, dushanba).</summary>
    private const string GroupDate = "2026-08-31";

    /// <summary>Sinf darsi shu sanada o'tilgan (o'sha dushanba, boshqa dars raqami).</summary>
    private const string ClassDate = "2026-08-31";

    private readonly List<string> _connectionStrings = [];

    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>Har test o'z bazasini oladi — hovuzlar shu yerda bo'shatiladi.</summary>
    public Task DisposeAsync()
    {
        foreach (var connectionString in _connectionStrings)
            NpgsqlConnection.ClearPool(new NpgsqlConnection(connectionString));
        return Task.CompletedTask;
    }

    /* =====================================================================
     *  1. G-12 — jurnal ustunlari va yozish
     * ================================================================== */

    /// <summary>
    /// <b>ESKI: 0 ta ustun. YANGI: 1 ta ustun.</b>
    ///
    /// <para>
    /// Guruhning jadvali va haftaga biriktirishi BOR, lekin o'chirgich o'chiq
    /// ekan jurnalda birorta dars ustuni chiqmaydi. Sinfning ustunlari esa
    /// ikkala holatda ham BIR XIL.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Guruh_ustunlari_ochirgichgacha_yoq_keyin_paydo_boladi()
    {
        await using var db = await NewDbAsync("columns");
        var w = await SeedAsync(db);

        Assert.Empty(await JournalService.ComputeColumnsAsync(db, w.GroupId, w.EnglishId, Quarter));
        var classColumnsOff = await JournalService.ComputeColumnsAsync(db, w.ClassAId, w.MathId, Quarter);
        Assert.Single(classColumnsOff);

        await SetSwitchAsync(db, true);

        var groupColumns = await JournalService.ComputeColumnsAsync(db, w.GroupId, w.EnglishId, Quarter);
        var column = Assert.Single(groupColumns);
        Assert.Equal(GroupDate, column.Date);
        Assert.Equal(0, column.SubGroup); // guruhda sinf ichidagi bo'linish yo'q

        // Sinfning ustunlari qimirlamadi.
        Assert.Equal(
            classColumnsOff,
            await JournalService.ComputeColumnsAsync(db, w.ClassAId, w.MathId, Quarter));
    }

    /// <summary>
    /// O'chirgich o'chiq ekan guruh jurnaliga YOZIB BO'LMAYDI — ochiq xato
    /// matni qaytadi va bazada birorta qator paydo bo'lmaydi.
    /// </summary>
    [Fact]
    public async Task Ochirgich_ochiq_bolsa_guruh_jurnaliga_yozilmaydi()
    {
        await using var db = await NewDbAsync("write_off");
        var w = await SeedAsync(db);

        var error = await JournalService.SetEntryAsync(db, Entry(w, w.A1, grade: 5));
        Assert.Equal(JournalService.GroupLessonsOffMessage, error);
        Assert.Empty(await db.JournalEntries.Where(e => e.ClassId == w.GroupId).ToListAsync());

        var noteError = await JournalService.SetNoteAsync(db, Note(w, conducted: true));
        Assert.Equal(JournalService.GroupLessonsOffMessage, noteError);
        Assert.Empty(await db.LessonNotes.Where(n => n.ClassId == w.GroupId).ToListAsync());
    }

    /// <summary>
    /// Yoqilgach guruh katagi yoziladi va qator <c>owner_kind='group'</c> bilan
    /// belgilanadi. <b>Bo'linish har doim 0</b> — o'quvchining
    /// <c>Student.SubGroup</c> i 1 bo'lsa ham: guruhda sinf ichidagi bo'linish
    /// yo'q (§2.1.3).
    /// </summary>
    [Fact]
    public async Task Guruh_katagi_owner_kind_group_va_sub_group_0_bilan_yoziladi()
    {
        await using var db = await NewDbAsync("write_on");
        var w = await SeedAsync(db);
        await SetSwitchAsync(db, true);

        Assert.Null(await JournalService.SetEntryAsync(db, Entry(w, w.A1, grade: 5)));

        var entry = Assert.Single(await db.JournalEntries.Where(e => e.ClassId == w.GroupId).ToListAsync());
        Assert.Equal(LessonOwnerKind.Group, entry.OwnerKind);
        Assert.Equal(0, entry.SubGroup);
        // A1 ning sinf ichidagi guruhi 1 — lekin guruh qatoriga ko'chirilmadi.
        Assert.Equal(1, (await db.Students.FindAsync(w.A1))!.SubGroup);

        // Birinchi baho darsni "o'tildi" deb belgilaydi — izoh ham guruhniki.
        var note = Assert.Single(await db.LessonNotes.Where(n => n.ClassId == w.GroupId).ToListAsync());
        Assert.Equal(LessonOwnerKind.Group, note.OwnerKind);
        Assert.True(note.Conducted);
        Assert.Equal(0, note.SubGroup);
    }

    /// <summary>
    /// Guruh darsiga 1/2-guruh izohi yozilmaydi — server aniq xato matni bilan
    /// rad etadi (§2.1.4).
    /// </summary>
    [Fact]
    public async Task Guruh_darsida_bolinish_izohi_rad_etiladi()
    {
        await using var db = await NewDbAsync("subgroup_note");
        var w = await SeedAsync(db);
        await SetSwitchAsync(db, true);

        var error = await JournalService.SetNoteAsync(db, Note(w, conducted: true, subGroup: 1));
        Assert.Equal(JournalService.SubGroupOnGroupMessage, error);
        Assert.Empty(await db.LessonNotes.Where(n => n.ClassId == w.GroupId).ToListAsync());
    }

    /// <summary>
    /// Guruh qatorlari SINF jurnalining javobiga sizib chiqmaydi va aksincha —
    /// kalit endi <c>owner_kind</c> bilan ham ajratiladi.
    /// </summary>
    [Fact]
    public async Task Sinf_va_guruh_jurnallari_aralashmaydi()
    {
        await using var db = await NewDbAsync("isolation");
        var w = await SeedAsync(db);
        await SetSwitchAsync(db, true);

        Assert.Null(await JournalService.SetEntryAsync(db, Entry(w, w.A1, grade: 5)));
        Assert.Null(await JournalService.SetEntryAsync(db, new SetJournalEntryRequest(
            w.ClassAId, w.MathId, Quarter, w.A1, ClassDate, ClassPeriod, 3, null)));

        var groupEntries = await JournalService.GetEntriesAsync(db, w.GroupId, w.EnglishId, Quarter);
        Assert.Equal(5, Assert.Single(groupEntries).Grade);

        var classEntries = await JournalService.GetEntriesAsync(db, w.ClassAId, w.MathId, Quarter);
        Assert.Equal(3, Assert.Single(classEntries).Grade);
    }

    /// <summary>
    /// <c>JournalSettingsGuard</c> ning ro'yxati endi <see cref="LessonRoster"/>
    /// dan: guruh darsini yopish uchun FAQAT guruh a'zolari baholangan bo'lishi
    /// kerak. Sinfdagi guruhsiz bola darsni ushlab turmaydi.
    /// </summary>
    [Fact]
    public async Task Guruh_darsini_yopish_uchun_faqat_azolar_baholanadi()
    {
        await using var db = await NewDbAsync("guard");
        var w = await SeedAsync(db);
        await SetSwitchAsync(db, true);

        var meta = await db.SchoolMeta.FirstAsync();
        meta.IsStudentGradeRequired = true;
        await db.SaveChangesAsync();

        // A1 — guruhda, A2 — guruhda EMAS. A1 baholandi, B1 hali yo'q.
        Assert.Null(await JournalService.SetEntryAsync(db, Entry(w, w.A1, grade: 5)));
        Assert.False(await JournalSettingsGuard.SlotFullyGradedAsync(
            db, w.GroupId, w.EnglishId, Quarter, GroupDate, GroupPeriod, 0));

        // B1 ham baholandi — A2 (guruhsiz) so'ralmaydi, ya'ni dars yopiladi.
        Assert.Null(await JournalService.SetEntryAsync(db, Entry(w, w.B1, grade: 4)));
        Assert.True(await JournalSettingsGuard.SlotFullyGradedAsync(
            db, w.GroupId, w.EnglishId, Quarter, GroupDate, GroupPeriod, 0));
    }

    /* =====================================================================
     *  2. G-13 — davomat analitikasi
     * ================================================================== */

    /// <summary>
    /// <b>ESKI: 5-A da 2 ta imkoniyat. YANGI: 3 ta.</b>
    ///
    /// <para>
    /// Sinfda bitta o'tilgan dars × 2 o'quvchi = 2 imkoniyat. O'chirgich
    /// yoqilgach guruhning o'tilgan darsi ham qo'shiladi: 5-A dan guruhda
    /// FAQAT A1 bor, ya'ni +1. Guruh alohida QATOR bo'lmaydi — u
    /// qatnashuvchining o'z sinfi ostida sanaladi (§2.1.6 G-15).
    /// </para>
    /// </summary>
    [Fact]
    public async Task Davomat_analitikasi_guruh_darsini_oquvchining_sinfiga_qoshadi()
    {
        await using var db = await NewDbAsync("analytics");
        var w = await SeedAsync(db);
        await MarkConductedAsync(db, w);

        var off = await AttendanceAnalytics.BuildAsync(db, null, GroupDate, GroupDate, GroupDate);
        var offA = off.Classes.Single(c => c.ClassId == w.ClassAId);
        Assert.Equal(2, offA.Tally.Opportunities);
        Assert.Equal(1, offA.Tally.Lessons);
        Assert.Equal(3, off.Total.Opportunities); // 5-A: 2, 5-B: 1

        await SetSwitchAsync(db, true);

        var on = await AttendanceAnalytics.BuildAsync(db, null, GroupDate, GroupDate, GroupDate);
        var onA = on.Classes.Single(c => c.ClassId == w.ClassAId);
        Assert.Equal(3, onA.Tally.Opportunities);  // +A1 ning guruh darsi
        Assert.Equal(2, onA.Tally.Lessons);        // sinf darsi + guruh darsi
        var onB = on.Classes.Single(c => c.ClassId == w.ClassBId);
        Assert.Equal(2, onB.Tally.Opportunities);  // +B1 ning guruh darsi
        Assert.Equal(5, on.Total.Opportunities);

        // Guruh ro'yxatda ALOHIDA qator emas.
        Assert.DoesNotContain(on.Classes, c => c.ClassId == w.GroupId);
    }

    /// <summary>
    /// Bitta sinf tanlanganda guruh darsi FAQAT o'sha sinfning bolalari uchun
    /// sanaladi — boshqa boquvchi sinfning a'zosi hisobga kirmaydi.
    /// </summary>
    [Fact]
    public async Task Sinf_filtri_guruh_darsini_ham_chegaralaydi()
    {
        await using var db = await NewDbAsync("analytics_filter");
        var w = await SeedAsync(db);
        await MarkConductedAsync(db, w);
        await SetSwitchAsync(db, true);

        var onlyA = await AttendanceAnalytics.BuildAsync(db, w.ClassAId, GroupDate, GroupDate, GroupDate);
        Assert.Equal(3, onlyA.Total.Opportunities); // sinf 2 + guruhdan faqat A1
        Assert.Single(onlyA.Classes);
    }

    /// <summary>
    /// Davomat belgisi guruh darsida qo'yilsa — u o'quvchining SINFI qatorida
    /// "yo'q" bo'lib ko'rinadi (ball va foiz bir manbadan hisoblanishi uchun).
    /// </summary>
    [Fact]
    public async Task Guruh_darsidagi_yoqlik_oquvchining_sinfida_korinadi()
    {
        await using var db = await NewDbAsync("analytics_absent");
        var w = await SeedAsync(db);
        await MarkConductedAsync(db, w);
        await SetSwitchAsync(db, true);

        await db.JournalEntries.AddAsync(new JournalEntry
        {
            ClassId = w.GroupId, SubjectId = w.EnglishId, Quarter = Quarter, StudentId = w.A1,
            Date = GroupDate, Period = GroupPeriod, ReasonId = w.AbsentReasonId,
            OwnerKind = LessonOwnerKind.Group,
        });
        await db.SaveChangesAsync();

        var on = await AttendanceAnalytics.BuildAsync(db, null, GroupDate, GroupDate, GroupDate);
        var onA = on.Classes.Single(c => c.ClassId == w.ClassAId);
        Assert.Equal(1, onA.Tally.Absent);
        Assert.Equal(1, on.Total.Absent);
    }

    /* =====================================================================
     *  3. G-13 — intizom hisoboti
     * ================================================================== */

    /// <summary>
    /// <b>ESKI: A1 uchun 1 imkoniyat, 0 ball. YANGI: 2 imkoniyat, −10 ball.</b>
    ///
    /// <para>
    /// Guruh darsida qo'yilgan yo'qlik ham intizomiy ballni yeydi — aks holda
    /// "Ballar nazorati" va bu hisobot bir o'quvchi uchun ikki xil raqam
    /// ko'rsatardi.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Intizom_hisoboti_guruh_darsini_ham_sanaydi()
    {
        await using var db = await NewDbAsync("discipline");
        var w = await SeedAsync(db);
        await MarkConductedAsync(db, w);

        await db.JournalEntries.AddAsync(new JournalEntry
        {
            ClassId = w.GroupId, SubjectId = w.EnglishId, Quarter = Quarter, StudentId = w.A1,
            Date = GroupDate, Period = GroupPeriod, ReasonId = w.AbsentReasonId,
            OwnerKind = LessonOwnerKind.Group,
        });
        await db.SaveChangesAsync();

        var off = await AttendanceDisciplineReport.BuildAsync(db, GroupDate, GroupDate);
        Assert.Equal(3, off.Totals.Opportunities);         // faqat sinf darslari
        Assert.Equal(0, off.Totals.AttendancePoints);      // guruh belgisi ko'rinmaydi
        Assert.DoesNotContain(off.Students, s => s.StudentId == w.A1);

        await SetSwitchAsync(db, true);

        var on = await AttendanceDisciplineReport.BuildAsync(db, GroupDate, GroupDate);
        Assert.Equal(5, on.Totals.Opportunities);
        Assert.Equal(1, on.Totals.Absences);
        Assert.Equal(-10, on.Totals.AttendancePoints);
        var row = on.Students.Single(s => s.StudentId == w.A1);
        Assert.Equal(2, row.Opportunities);                // sinf darsi + guruh darsi
        Assert.Equal(1, row.Absences);
        Assert.Equal(-10, row.AttendancePoints);
    }

    /* =====================================================================
     *  Yordamchilar
     * ================================================================== */

    /// <summary>Guruh darsining raqami (dushanba).</summary>
    private const int GroupPeriod = 4;

    /// <summary>Sinf darsining raqami (o'sha dushanba).</summary>
    private const int ClassPeriod = 1;

    private static SetJournalEntryRequest Entry(World w, string studentId, int? grade) =>
        new(w.GroupId, w.EnglishId, Quarter, studentId, GroupDate, GroupPeriod, grade, null);

    private static SetLessonNoteRequest Note(World w, bool conducted, int subGroup = 0) =>
        new(w.GroupId, w.EnglishId, Quarter, GroupDate, GroupPeriod, "Mavzu", null, conducted, subGroup);

    private async Task<AppDbContext> NewDbAsync(string prefix)
    {
        var database = await fixture.Postgres.CreateDatabaseAsync("gja_" + prefix);
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
    /// Sinf darsini ham, guruh darsini ham "o'tildi" deb belgilaydi — davomat
    /// maxraji faqat o'tilgan darslardan quriladi.
    /// </summary>
    private static async Task MarkConductedAsync(AppDbContext db, World w)
    {
        db.LessonNotes.AddRange(
            new LessonNote
            {
                ClassId = w.ClassAId, SubjectId = w.MathId, Quarter = Quarter,
                Date = ClassDate, Period = ClassPeriod, Conducted = true,
            },
            new LessonNote
            {
                ClassId = w.ClassBId, SubjectId = w.MathId, Quarter = Quarter,
                Date = ClassDate, Period = ClassPeriod, Conducted = true,
            },
            new LessonNote
            {
                ClassId = w.GroupId, SubjectId = w.EnglishId, Quarter = Quarter,
                Date = GroupDate, Period = GroupPeriod, Conducted = true,
                OwnerKind = LessonOwnerKind.Group,
            });
        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Ikki sinf (5-A: A1, A2; 5-B: B1) va bitta guruh (ingliz tili), unda
    /// IKKALA sinfdan bittadan bola: A1 va B1. Guruhning o'z jadvali
    /// (dushanba, 4-dars) 1-haftaga biriktirilgan — lekin o'chirgich O'CHIQ.
    /// </summary>
    private static async Task<World> SeedAsync(AppDbContext db)
    {
        db.SchoolMeta.Add(new SchoolMeta { CurrentYear = "2026/2027", Name = "Test maktab" });
        db.Quarters.Add(new QuarterPeriod
        {
            Quarter = Quarter, StartDate = "2026-08-31", EndDate = "2026-12-31", GradesOpen = true,
        });

        var classA = new SchoolClass { Name = "5-A", Grade = 5 };
        var classB = new SchoolClass { Name = "5-B", Grade = 5 };
        db.Classes.AddRange(classA, classB);

        var math = new Subject { Name = "Matematika" };
        var english = new Subject { Name = "Ingliz tili", IsGroupable = true };
        db.Subjects.AddRange(math, english);

        var classTeacher = new Teacher { FullName = "Sinf ustozi" };
        var groupTeacher = new Teacher { FullName = "Guruh ustozi" };
        db.Teachers.AddRange(classTeacher, groupTeacher);

        var absent = new AbsenceReason { Name = "Kelmadi", Short = "K", Points = -10 };
        db.AbsenceReasons.Add(absent);

        var a1 = NewStudent("Anvar Anvarov", classA.Name, subGroup: 1);
        var a2 = NewStudent("Aziza Azizova", classA.Name, subGroup: 2);
        var b1 = NewStudent("Bobur Boburov", classB.Name, subGroup: 0);
        db.Students.AddRange(a1, a2, b1);

        var user = new AppUser
        {
            FullName = "Seed", Role = Roles.Admin, Email = $"gja.{Guid.NewGuid():N}",
        };
        user.SetInitialPassword("Test-" + Guid.NewGuid().ToString("N")[..10]);
        db.Users.Add(user);

        var tplA = new ScheduleTemplate
        {
            ClassId = classA.Id, Name = "5-A asosiy",
            Lessons =
            [
                new ScheduleLesson
                {
                    Day = 0, Period = ClassPeriod, SubjectId = math.Id, TeacherId = classTeacher.Id,
                },
            ],
        };
        var tplB = new ScheduleTemplate
        {
            ClassId = classB.Id, Name = "5-B asosiy",
            Lessons =
            [
                new ScheduleLesson
                {
                    Day = 0, Period = ClassPeriod, SubjectId = math.Id, TeacherId = classTeacher.Id,
                },
            ],
        };
        db.ScheduleTemplates.AddRange(tplA, tplB);
        await db.SaveChangesAsync();

        var group = new StudyGroup
        {
            Name = "Kuchli ingliz",
            SubjectId = english.Id,
            CreatedBy = user.Id,
            CreatedAt = AppClock.NowInstant,
        };
        db.StudyGroups.Add(group);
        db.StudyGroupClasses.AddRange(
            new StudyGroupClass { GroupId = group.Id, ClassId = classA.Id },
            new StudyGroupClass { GroupId = group.Id, ClassId = classB.Id });
        db.StudyGroupTeachers.Add(new StudyGroupTeacher { GroupId = group.Id, TeacherId = groupTeacher.Id });

        var joined = new DateOnly(2026, 8, 31);
        db.StudyGroupMembers.AddRange(
            NewMember(group, english.Id, a1.Id, joined, user.Id),
            NewMember(group, english.Id, b1.Id, joined, user.Id));

        var groupTpl = new ScheduleTemplate
        {
            ClassId = group.Id.ToString(),
            Name = "Guruh jadvali",
            OwnerKind = LessonOwnerKind.Group,
            Lessons =
            [
                new ScheduleLesson
                {
                    Day = 0, Period = GroupPeriod, SubjectId = english.Id, TeacherId = groupTeacher.Id,
                },
            ],
        };
        db.ScheduleTemplates.Add(groupTpl);

        db.WeekAssignments.AddRange(
            new WeekAssignment { ClassId = classA.Id, Quarter = Quarter, Week = 1, TemplateId = tplA.Id },
            new WeekAssignment { ClassId = classB.Id, Quarter = Quarter, Week = 1, TemplateId = tplB.Id },
            new WeekAssignment
            {
                ClassId = group.Id.ToString(), Quarter = Quarter, Week = 1,
                TemplateId = groupTpl.Id, OwnerKind = LessonOwnerKind.Group,
            });

        await db.SaveChangesAsync();

        return new World(
            classA.Id, classB.Id, group.Id.ToString(),
            math.Id, english.Id,
            a1.Id, a2.Id, b1.Id,
            classTeacher.Id, groupTeacher.Id, absent.Id);
    }

    private static StudyGroupMember NewMember(
        StudyGroup group, string subjectId, string studentId, DateOnly joined, string createdBy) => new()
    {
        GroupId = group.Id,
        SubjectId = subjectId,
        StudentId = studentId,
        JoinedOn = joined,
        CreatedBy = createdBy,
        CreatedAt = AppClock.NowInstant,
    };

    private static Student NewStudent(string fullName, string className, int subGroup) => new()
    {
        FullName = fullName,
        LastName = fullName.Split(' ')[^1],
        FirstName = fullName.Split(' ')[0],
        BirthDate = "2014-01-01",
        Gender = "male",
        ClassName = className,
        SubGroup = subGroup,
        EnrollmentDate = "2026-08-31",
    };

    /// <summary>Seed natijasi — testlar shu id'lar bilan ishlaydi.</summary>
    private sealed record World(
        string ClassAId,
        string ClassBId,
        string GroupId,
        string MathId,
        string EnglishId,
        string A1,
        string A2,
        string B1,
        string ClassTeacherId,
        string GroupTeacherId,
        string AbsentReasonId);
}
