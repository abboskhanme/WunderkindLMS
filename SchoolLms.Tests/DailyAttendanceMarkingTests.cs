using System.Net;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// DAVOMAT — sinf va DARS SOATI bo'yicha belgilash (mijoz, 2026-09-18:
/// "sinf tanlansa o'sha soatda darsiga ko'ra sinfni davomat qilish mumkin
/// bo'lsin"). Mantiq: <see cref="DailyAttendanceService"/>.
///
/// <para>
/// <b>Eng muhim test</b> — <c>Bahosi_bor_qator_davomat_tozalanganda_saqlanib_qoladi</c>:
/// davomat belgilash BAHOGA tegmasligi kerak. Xodim kunni qayta saqlaganda
/// o'qituvchining bahosi o'chib ketsa, buni faqat chorak oxirida, baho
/// yo'qolganda sezishardi.
/// </para>
///
/// <para>
/// Arifmetika testlari O'Z bazasida ishlaydi (<c>NewDbAsync</c>) —
/// <c>AttendanceAnalyticsTests</c> dagi bilan bir xil sabab: ro'yxat butun
/// maktabni sanaydi, qo'shni testning bitta sinfi ham raqamni buzardi.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class DailyAttendanceMarkingTests(ApiFixture fixture) : IAsyncLifetime
{
    private readonly List<string> _connectionStrings = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        foreach (var cs in _connectionStrings)
            NpgsqlConnection.ClearPool(new NpgsqlConnection(cs));
        return Task.CompletedTask;
    }

    private const string Overview = "/api/admin/attendance/daily/overview?date=2026-01-12";

    /// <summary>2026-01-12 — dushanba (jadvalda 0-kun).</summary>
    private const string Monday = "2026-01-12";

    // =====================================================================
    //  1. RUXSAT
    // =====================================================================

    [Fact]
    public async Task Tokensiz_sorov_401()
    {
        using var client = fixture.Api.AnonymousClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Overview)).StatusCode);
    }

    /// <summary>
    /// O'qituvchi, kassir va ota-ona — yopiq: bu BUTUN MAKTAB kuni, bitta
    /// sinfning jurnali emas. Mas'ul xodimga esa aynan `attendance` ruxsati
    /// beriladi va u shu menyudan ishlaydi.
    /// </summary>
    [Theory]
    [InlineData(Roles.Teacher)]
    [InlineData(Roles.Cashier)]
    [InlineData("parent")]
    public async Task Oqituvchi_kassir_va_ota_ona_kira_olmaydi_403(string role)
    {
        using var client = await fixture.Api.ClientAsAsync(role, "attendance");

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Overview)).StatusCode);
    }

    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Staff)]
    public async Task Davomat_ruxsatli_xodim_royxatni_ochadi_200(string role)
    {
        using var client = await fixture.Api.ClientAsAsync(role, "attendance");

        var response = await client.GetAsync(Overview);

        Assert.True(response.IsSuccessStatusCode, $"{(int)response.StatusCode} {response.StatusCode}");
    }

    // =====================================================================
    //  2. Saqlash
    // =====================================================================

    /// <summary>
    /// Belgi FAQAT tanlangan dars soatiga tushadi — qo'shni soat tegilmaydi.
    /// Mijoz aynan shuni so'radi: "o'sha soatda darsiga ko'ra".
    /// </summary>
    [Fact]
    public async Task Belgi_faqat_tanlangan_dars_soatiga_yoziladi()
    {
        await using var db = await NewDbAsync("one_lesson");
        var s = await SeedAsync(db);
        var service = new DailyAttendanceService(db);

        var error = await service.SaveAsync(
            new SaveDailyAttendanceRequest(s.ClassId, Monday, s.Math, 1, [new(s.A, s.Illness)]),
            s.ActorId);

        Assert.Null(error);

        var row = Assert.Single(await db.JournalEntries.AsNoTracking()
            .Where(e => e.Date == Monday && e.ReasonId != null).ToListAsync());
        Assert.Equal(s.A, row.StudentId);
        Assert.Equal(s.Math, row.SubjectId);
        Assert.Equal(1, row.Period);
        Assert.Equal(s.Illness, row.ReasonId);
        // Chorak jurnal qatoriga yozildi — hisobotlar shu ustun bo'yicha yig'adi.
        Assert.Equal(3, row.Quarter);

        // Ikkinchi dars (Fizika, 2-soat) hali belgilanmagan.
        var day = await service.ClassDayAsync(s.ClassId, Monday);
        Assert.NotNull(day);
        Assert.Equal(2, day.Lessons.Count);
        Assert.True(day.Lessons.Single(l => l.Period == 1).Marked);
        Assert.False(day.Lessons.Single(l => l.Period == 2).Marked);
    }

    /// <summary>
    /// Ekrandagi uchta tugma katalogdagi ikki sababga bog'lanadi va qaysi
    /// biriga ekanini javobning O'ZI aytadi (xizmat izohi: `ResolveReasons`).
    /// </summary>
    [Fact]
    public async Task Qizil_va_sariq_tugma_katalogdagi_sabablarga_boglanadi()
    {
        await using var db = await NewDbAsync("reasons");
        var s = await SeedAsync(db);

        var day = await new DailyAttendanceService(db).ClassDayAsync(s.ClassId, Monday);

        Assert.NotNull(day);
        Assert.Equal(s.Unexcused, day.AbsentReasonId);   // qizil — "Sababsiz"
        Assert.Equal("Sababsiz", day.AbsentReasonName);
        Assert.Equal(s.Illness, day.ExcusedReasonId);    // sariq — "Kasal"
        Assert.Equal("Kasal", day.ExcusedReasonName);
    }

    /// <summary>
    /// ENG MUHIM: qayta saqlashda o'quvchi "keldi" bo'lsa, sababi olib
    /// tashlanadi — lekin BAHOSI joyida qoladi. Bahosi yo'q bo'sh qator esa
    /// butunlay o'chadi (jurnalda axlat qolmaydi).
    /// </summary>
    [Fact]
    public async Task Bahosi_bor_qator_davomat_tozalanganda_saqlanib_qoladi()
    {
        await using var db = await NewDbAsync("keep_grade");
        var s = await SeedAsync(db);
        var service = new DailyAttendanceService(db);

        await service.SaveAsync(
            new SaveDailyAttendanceRequest(s.ClassId, Monday, s.Math, 1, [new(s.A, s.Unexcused)]),
            s.ActorId);

        // O'qituvchi o'sha darsga baho qo'ydi (mavjud qatorning ustiga).
        var graded = await db.JournalEntries.FirstAsync(e => e.StudentId == s.A && e.Period == 1);
        graded.Grade = 5;
        await db.SaveChangesAsync();

        // Xodim xatosini to'g'rilaydi: aslida o'quvchi kelgan edi.
        var error = await service.SaveAsync(
            new SaveDailyAttendanceRequest(s.ClassId, Monday, s.Math, 1, []), s.ActorId);
        Assert.Null(error);

        var kept = Assert.Single(await db.JournalEntries.AsNoTracking()
            .Where(e => e.StudentId == s.A && e.Date == Monday).ToListAsync());
        Assert.Equal(1, kept.Period);
        Assert.Equal(5, kept.Grade);             // BAHO JOYIDA
        Assert.Null(kept.ReasonId);              // davomat esa olib tashlandi
    }

    /// <summary>
    /// Hamma kelgan DARS ham SAQLANADI: aks holda "hammasi keldi" bilan "hali
    /// belgilanmagan" bir xil ko'rinardi — belgi jadvali mana shu farq uchun
    /// bor (`DailyAttendance.cs` boshidagi izoh).
    /// </summary>
    [Fact]
    public async Task Hamma_kelgan_dars_ham_belgilandi_deb_qoladi()
    {
        await using var db = await NewDbAsync("all_present");
        var s = await SeedAsync(db);
        var service = new DailyAttendanceService(db);

        Assert.Null(await service.SaveAsync(
            new SaveDailyAttendanceRequest(s.ClassId, Monday, s.Math, 1, []), s.ActorId));

        var day = await service.ClassDayAsync(s.ClassId, Monday);

        Assert.NotNull(day);
        var lesson = day.Lessons.Single(l => l.Period == 1);
        Assert.True(lesson.Marked);
        Assert.Empty(lesson.Marks);
        Assert.False(await db.JournalEntries.AnyAsync(e => e.Date == Monday && e.ReasonId != null));
    }

    /// <summary>Kech kelgan alohida sanaladi — yo'qlikka qo'shilmaydi.</summary>
    [Fact]
    public async Task Kech_kelgan_yoqlikka_qoshilmaydi()
    {
        await using var db = await NewDbAsync("late");
        var s = await SeedAsync(db);
        var service = new DailyAttendanceService(db);

        await service.SaveAsync(
            new SaveDailyAttendanceRequest(s.ClassId, Monday, s.Math, 1,
                [new(s.A, s.Illness), new(s.B, s.Late)]), s.ActorId);

        var overview = await service.OverviewAsync(Monday);
        var row = Assert.Single(overview.Classes, c => c.ClassId == s.ClassId);

        Assert.Equal(1, row.AbsentCount);
        Assert.Equal(1, row.LateCount);
        Assert.Equal(1, row.MarkedLessons);
        // Ikkinchi soat qolgani uchun sinf hali "belgilandi" emas.
        Assert.Equal(0, overview.MarkedClasses);
    }

    /// <summary>
    /// Ro'yxat ishning holatini ko'rsatadi: nechta sinf belgilangan, nechtasi
    /// qolgan, kim belgilagan.
    /// </summary>
    [Fact]
    public async Task Royxat_belgilangan_va_qolgan_sinflarni_korsatadi()
    {
        await using var db = await NewDbAsync("overview");
        var s = await SeedAsync(db);
        await AddClassAsync(db, "6-A", 6);
        var service = new DailyAttendanceService(db);

        // Kunning IKKALA soatini ham belgilaymiz — shunda sinf "tugallandi".
        await service.SaveAsync(
            new SaveDailyAttendanceRequest(s.ClassId, Monday, s.Math, 1, [new(s.A, s.Unexcused)]),
            s.ActorId);
        await service.SaveAsync(
            new SaveDailyAttendanceRequest(s.ClassId, Monday, s.Physics, 2, []), s.ActorId);

        var overview = await service.OverviewAsync(Monday);

        Assert.Equal(2, overview.TotalClasses);
        Assert.Equal(1, overview.MarkedClasses);

        var marked = Assert.Single(overview.Classes, c => c.ClassName == "5-A");
        Assert.Equal(2, marked.LessonCount);
        Assert.Equal(2, marked.MarkedLessons);
        Assert.Equal(1, marked.AbsentCount);

        var pending = Assert.Single(overview.Classes, c => c.ClassName == "6-A");
        Assert.Equal(0, pending.MarkedLessons);
        Assert.Equal(0, pending.AbsentCount);

        // Kun yakunida SOATLAR ham sanaladi (mijoz, 2026-09-19): 5-A ning
        // ikkala soati belgilangan, 6-A da esa jadval yo'q.
        Assert.Equal(2, overview.MarkedLessons);
        Assert.Equal(2, overview.TotalLessons);
    }

    /// <summary>
    /// Ikki chegara: katalogda yo'q sabab va boshqa sinfning o'quvchisi —
    /// ikkalasi ham rad etiladi va bitta ham qator yozilmaydi.
    /// </summary>
    [Fact]
    public async Task Notogri_sabab_va_begona_oquvchi_rad_etiladi()
    {
        await using var db = await NewDbAsync("validation");
        var s = await SeedAsync(db);
        var other = await AddClassAsync(db, "7-A", 7);
        var service = new DailyAttendanceService(db);

        Assert.NotNull(await service.SaveAsync(
            new SaveDailyAttendanceRequest(s.ClassId, Monday, s.Math, 1, [new(s.A, "yo'q-sabab")]),
            s.ActorId));

        Assert.NotNull(await service.SaveAsync(
            new SaveDailyAttendanceRequest(s.ClassId, Monday, s.Math, 1,
                [new(other.StudentId, s.Illness)]),
            s.ActorId));

        // Jadvalda yo'q dars — rad etiladi (fan to'g'ri, soat noto'g'ri).
        Assert.NotNull(await service.SaveAsync(
            new SaveDailyAttendanceRequest(s.ClassId, Monday, s.Math, 7, [new(s.A, s.Illness)]),
            s.ActorId));

        Assert.False(await db.JournalEntries.AnyAsync(e => e.Date == Monday));
        Assert.False(await db.DailyAttendanceMarks.AnyAsync(m => m.Date == Monday));
    }

    /// <summary>
    /// Dars yo'q kunda (yakshanba yoki jadvalsiz hafta) davomat belgilanmaydi —
    /// aks holda hech qayerga tushmaydigan "belgilandi" qatori qolardi.
    /// </summary>
    [Fact]
    public async Task Darssiz_kunda_belgilab_bolmaydi()
    {
        await using var db = await NewDbAsync("no_lessons");
        var s = await SeedAsync(db);
        var service = new DailyAttendanceService(db);

        // 2026-01-11 — yakshanba.
        var error = await service.SaveAsync(
            new SaveDailyAttendanceRequest(s.ClassId, "2026-01-11", s.Math, 1, [new(s.A, s.Illness)]),
            s.ActorId);

        Assert.NotNull(error);
        Assert.False(await db.DailyAttendanceMarks.AnyAsync());
    }

    /// <summary>
    /// O'qituvchi jurnalda belgilagan yo'qlik ekranda KO'RINADI (xodim uni
    /// ko'rmay turib ustiga yozib yubormasin) — lekin dars "belgilandi"
    /// bo'lmaydi: uni xodim saqlagani yo'q.
    /// </summary>
    [Fact]
    public async Task Oqituvchi_belgilagan_yoqlik_ekranda_korinadi()
    {
        await using var db = await NewDbAsync("teacher_mark");
        var s = await SeedAsync(db);

        db.JournalEntries.Add(new JournalEntry
        {
            ClassId = s.ClassId, SubjectId = s.Math, Quarter = 3, StudentId = s.B,
            Date = Monday, Period = 1, ReasonId = s.Unexcused,
        });
        await db.SaveChangesAsync();

        var day = await new DailyAttendanceService(db).ClassDayAsync(s.ClassId, Monday);

        Assert.NotNull(day);
        var lesson = day.Lessons.Single(l => l.Period == 1);
        Assert.Equal(s.Unexcused, lesson.Marks[s.B]);
        Assert.False(lesson.Marked);
    }

    /// <summary>
    /// Kunlik davomat HISOBOTI ham "belgilandi" ni ko'rsatadi: 0 ta yo'q
    /// bo'lgan dars "hammasi keldi" ni ham, "hech kim belgilamagan" ni ham
    /// bildirishi mumkin edi — zavuch uchun bu ikkisi boshqa narsa.
    /// </summary>
    [Fact]
    public async Task Kunlik_hisobot_belgilangan_darsni_korsatadi()
    {
        await using var db = await NewDbAsync("report_flag");
        var s = await SeedAsync(db);

        await new DailyAttendanceService(db).SaveAsync(
            new SaveDailyAttendanceRequest(s.ClassId, Monday, s.Math, 1, []), s.ActorId);

        // Hisobot kontrollerining o'zi emas, o'sha ma'lumot manbai tekshiriladi:
        // belgi jadvalida aynan shu dars turibdi.
        var mark = Assert.Single(await db.DailyAttendanceMarks.AsNoTracking()
            .Where(m => m.ClassId == s.ClassId && m.Date == Monday).ToListAsync());
        Assert.Equal(s.Math, mark.SubjectId);
        Assert.Equal(1, mark.Period);
        Assert.Equal(0, mark.AbsentCount);

        // Ikkinchi dars belgilanmagan — hisobotda "Belgilanmagan" bo'lib chiqadi.
        Assert.False(await db.DailyAttendanceMarks.AsNoTracking()
            .AnyAsync(m => m.SubjectId == s.Physics && m.Date == Monday));
    }

    /// <summary>
    /// BO'LINGAN DARS (til guruhi): faqat o'sha guruhning o'quvchilari
    /// belgilanadi. Ikkinchi yarim sinf boshqa xonada — ularni "kelmadi"
    /// deb yozib qo'yish eng zararli xato bo'lardi.
    /// </summary>
    [Fact]
    public async Task Bolingan_darsda_faqat_oz_guruhi_belgilanadi()
    {
        await using var db = await NewDbAsync("subgroup");
        var s = await SeedAsync(db);

        // A — 1-guruh, B — 2-guruh; 3-soatda ikkita parallel dars.
        var a = await db.Students.FindAsync(s.A);
        var b = await db.Students.FindAsync(s.B);
        a!.SubGroup = 1;
        b!.SubGroup = 2;
        var tpl = await db.ScheduleTemplates.Include(t => t.Lessons)
            .FirstAsync(t => t.ClassId == s.ClassId);
        tpl.Lessons.Add(new ScheduleLesson { Day = 0, Period = 3, SubjectId = s.Math, SubGroup = 1 });
        tpl.Lessons.Add(new ScheduleLesson { Day = 0, Period = 3, SubjectId = s.Physics, SubGroup = 2 });
        await db.SaveChangesAsync();

        var service = new DailyAttendanceService(db);
        var day = await service.ClassDayAsync(s.ClassId, Monday);
        Assert.NotNull(day);

        var first = day.Lessons.Single(l => l.Period == 3 && l.SubGroup == 1);
        var second = day.Lessons.Single(l => l.Period == 3 && l.SubGroup == 2);
        Assert.Equal([s.A], first.StudentIds);
        Assert.Equal([s.B], second.StudentIds);

        // Boshqa guruhning o'quvchisini belgilash — rad etiladi.
        Assert.NotNull(await service.SaveAsync(
            new SaveDailyAttendanceRequest(s.ClassId, Monday, s.Math, 3, [new(s.B, s.Illness)], 1),
            s.ActorId));

        // O'z guruhi — yoziladi.
        Assert.Null(await service.SaveAsync(
            new SaveDailyAttendanceRequest(s.ClassId, Monday, s.Math, 3, [new(s.A, s.Illness)], 1),
            s.ActorId));

        var row = Assert.Single(await db.JournalEntries.AsNoTracking()
            .Where(e => e.Date == Monday && e.Period == 3 && e.ReasonId != null).ToListAsync());
        Assert.Equal(s.A, row.StudentId);
    }

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    private sealed record Seed(
        string ClassId, string Math, string Physics,
        string A, string B, string Illness, string Unexcused, string Late, string ActorId);

    private sealed record ExtraClass(string ClassId, string StudentId);

    private async Task<AppDbContext> NewDbAsync(string prefix)
    {
        var database = await fixture.Postgres.CreateDatabaseAsync("daily_att_" + prefix);
        _connectionStrings.Add(database.OwnerConnectionString);
        return PostgresFixture.NewContext(database.OwnerConnectionString);
    }

    /// <summary>Bitta sinf, ikki fan (dushanba 1- va 2-dars), ikki o'quvchi, sabablar.</summary>
    private static async Task<Seed> SeedAsync(AppDbContext db)
    {
        var cls = new SchoolClass { Name = "5-A", Grade = 5, Language = "uz" };
        var math = new Subject { Name = "Matematika" };
        var physics = new Subject { Name = "Fizika" };
        db.Classes.Add(cls);
        db.Subjects.AddRange(math, physics);

        var a = NewStudent("Aliyev Ali", "5-A");
        var b = NewStudent("Berdiyev Bek", "5-A");
        db.Students.AddRange(a, b);

        var illness = new AbsenceReason { Name = "Kasal", Short = "K" };
        var unexcused = new AbsenceReason { Name = "Sababsiz", Short = "SS" };
        var late = new AbsenceReason { Name = "Kech keldi", Short = "Kch", IsLate = true };
        db.AbsenceReasons.AddRange(illness, unexcused, late);

        db.Quarters.Add(new QuarterPeriod { Quarter = 3, StartDate = "2026-01-10", EndDate = "2026-03-20" });

        var tpl = new ScheduleTemplate
        {
            ClassId = cls.Id,
            Name = "Asosiy",
            Lessons =
            [
                new ScheduleLesson { Day = 0, Period = 1, SubjectId = math.Id },
                new ScheduleLesson { Day = 0, Period = 2, SubjectId = physics.Id },
            ],
        };
        db.ScheduleTemplates.Add(tpl);

        var actor = new AppUser
        {
            FullName = "Mas'ul xodim",
            Role = Roles.Staff,
            Email = "davomat@example.test",
            PasswordHash = "x",
        };
        db.Users.Add(actor);

        await db.SaveChangesAsync();

        // Haftaga shablonni biriktiramiz — kunning darslari shu orqali topiladi
        // (`AttendanceController.GetDaily` bilan bir xil yo'l).
        var week = ScheduleMath.GetQuarterWeeks("2026-01-10", "2026-03-20")
            .First(w => string.CompareOrdinal(Monday, w.StartISO) >= 0
                        && string.CompareOrdinal(Monday, w.EndISO) <= 0);
        db.WeekAssignments.Add(new WeekAssignment
        {
            ClassId = cls.Id, Quarter = 3, Week = week.Week, TemplateId = tpl.Id,
        });
        await db.SaveChangesAsync();

        return new Seed(cls.Id, math.Id, physics.Id, a.Id, b.Id,
            illness.Id, unexcused.Id, late.Id, actor.Id);
    }

    private static async Task<ExtraClass> AddClassAsync(AppDbContext db, string name, int grade)
    {
        var cls = new SchoolClass { Name = name, Grade = grade, Language = "uz" };
        db.Classes.Add(cls);
        var student = NewStudent($"{name} o'quvchi", name);
        db.Students.Add(student);
        await db.SaveChangesAsync();
        return new ExtraClass(cls.Id, student.Id);
    }

    private static Student NewStudent(string fullName, string className) => new()
    {
        FullName = fullName,
        LastName = "Familiya",
        FirstName = "Ism",
        MiddleName = "Otasi",
        BirthDate = "2015-01-01",
        Address = "Toshkent",
        Gender = "male",
        ParentFullName = "Ota-ona",
        ParentLastName = "Familiya",
        ParentFirstName = "Ism",
        ParentMiddleName = "Otasi",
        ParentPhone = "+998901112233",
        ClassName = className,
        EnrollmentDate = "2025-09-01",
    };
}
