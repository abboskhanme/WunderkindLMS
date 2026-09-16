using System.Net;
using Npgsql;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// Ikki analitik hisobot (docs/modules/existing-module-gaps.md §4): <b>#9 buyurtmalar
/// voronkasi</b> va <b>#6 davomat intizomi</b>.
///
/// <para>
/// <b>Nega har test O'Z bazasida yuradi.</b> Ikkala hisobot ham BUTUN baza bo'yicha yig'indi
/// chiqaradi (barcha lidlar, barcha o'quvchilar). Umumiy bazada boshqa test klassining bitta
/// lidi yoki o'quvchisi bu yerdagi har bir raqamni siljitib yuborardi. Shuning uchun
/// <c>CreateDatabaseAsync</c> — va shu sababli <see cref="DisposeAsync"/> da hovuz
/// tozalanadi (<c>AllocationTests</c> dagi izoh: tozalanmasa keyingi klasslar
/// <c>53300 remaining connection slots</c> bilan yiqiladi).
/// </para>
///
/// <para>
/// RBAC testlari esa umumiy (ilova ulangan) bazada, HAQIQIY HTTP so'rovlari bilan yuradi —
/// ular ma'lumotga emas, darvozaga qaraydi.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class AnalyticsReportsTests(ApiFixture fixture) : IAsyncLifetime
{
    private const string From = "2026-09-01";
    private const string To = "2026-09-30";

    private TestDatabase _database = default!;

    public async Task InitializeAsync() => _database = await fixture.Postgres.CreateDatabaseAsync("reports");

    /// <summary>
    /// Shu bazaning ulanish hovuzi yopiladi — ilovaning (ApiFixture) hovuziga tegilmaydi.
    /// Batafsil: <c>Billing/AllocationTests.cs</c>.
    /// </summary>
    public Task DisposeAsync()
    {
        NpgsqlConnection.ClearPool(new NpgsqlConnection(_database.OwnerConnectionString));
        return Task.CompletedTask;
    }

    private AppDbContext NewDb() => PostgresFixture.NewContext(_database.OwnerConnectionString);

    // =====================================================================
    //  #9 — BUYURTMALAR VORONKASI
    // =====================================================================

    /// <summary>
    /// Bosqichlar ro'yxati ham, tartibi ham <c>LeadStage</c> dan olinadi (kodda qattiq
    /// yozilgan ro'yxat yo'q), "yetib kelgan" esa keyingi bosqichlarni ham qo'shib sanaydi.
    ///
    /// <para>
    /// Chizma: Yangi(3) → Aloqada(2) → Shartnoma(1). Ya'ni birinchi bosqichga 6 lid yetgan,
    /// ikkinchisiga 3, uchinchisiga 1. Bosqichdan-bosqichga: 50% va 33.3%; boshdan-oxir: 16.7%.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Voronka_bosqichlarni_LeadStage_tartibida_va_yigindi_bilan_quradi()
    {
        await using var db = NewDb();
        var (s1, s2, s3) = await ThreeStagesAsync(db);
        await AddLeadsAsync(db, s1, 3, grade: 1);
        await AddLeadsAsync(db, s2, 2, grade: 1);
        await AddLeadsAsync(db, s3, 1, grade: 1);

        var funnel = await LeadFunnelQuery.BuildAsync(db);

        Assert.Equal(6, funnel.TotalLeads);
        Assert.Equal(6, funnel.FunnelLeads);
        Assert.Equal(0, funnel.OrphanCount);
        Assert.Equal(["Yangi", "Aloqada", "Shartnoma"], funnel.Stages.Select(s => s.Title));

        Assert.Equal(6, funnel.Stages[0].ReachedCount);
        Assert.Equal(3, funnel.Stages[1].ReachedCount);
        Assert.Equal(1, funnel.Stages[2].ReachedCount);

        Assert.Equal(3, funnel.Stages[0].MovedOnCount);
        Assert.Equal(50, funnel.Stages[0].StepConversionPercent);
        Assert.Equal(50, funnel.Stages[0].DropOffPercent);
        Assert.Equal(33.3, funnel.Stages[1].StepConversionPercent);

        // Oxirgi bosqichdan keyin bosqich yo'q — foiz ham yo'q (0 emas, null).
        Assert.Null(funnel.Stages[2].StepConversionPercent);
        Assert.Equal(16.7, funnel.OverallConversionPercent);
    }

    /// <summary>
    /// "Yo'qotildi" ustunlari voronkadan chiqariladi va sabab kesimi bo'lib qaytadi — sabab
    /// matni ustun nomining o'zi (bazada alohida "yo'qotish sababi" maydoni yo'q).
    /// Muhimi: yo'qotilganlar konversiya MAXRAJIGA kirmaydi, aks holda voronka o'zini
    /// yaxshiroq ko'rsatardi.
    /// </summary>
    [Fact]
    public async Task Yoqotilgan_ustun_voronkadan_chiqib_sabab_bolib_qaytadi()
    {
        await using var db = NewDb();
        var (s1, s2, _) = await ThreeStagesAsync(db);
        var lost = new LeadStage { Title = "Rad etdi", Color = "rose", Order = 3 };
        db.LeadStages.Add(lost);
        await db.SaveChangesAsync();

        await AddLeadsAsync(db, s1, 2, grade: 1);
        await AddLeadsAsync(db, s2, 2, grade: 1);
        await AddLeadsAsync(db, lost.Id, 4, grade: 1);

        var funnel = await LeadFunnelQuery.BuildAsync(db, [lost.Id]);

        Assert.Equal(8, funnel.TotalLeads);
        Assert.Equal(4, funnel.FunnelLeads);
        Assert.Equal(4, funnel.LostCount);
        Assert.DoesNotContain(funnel.Stages, s => s.Title == "Rad etdi");

        var loss = Assert.Single(funnel.Losses);
        Assert.Equal("Rad etdi", loss.Title);
        Assert.Equal(4, loss.Count);
        Assert.Equal(100, loss.SharePercent);

        // Voronkada 4 lid: 2 tasi birinchi, 2 tasi ikkinchi bosqichda → 50%.
        Assert.Equal(4, funnel.Stages[0].ReachedCount);
        Assert.Equal(50, funnel.Stages[0].StepConversionPercent);
    }

    /// <summary>
    /// Ustuni o'chirilgan lid jim yo'qolib ketmaydi — <c>OrphanCount</c> da ko'rinadi.
    /// Aks holda "jami 10 lid" deganda jadval 8 tasini ko'rsatib turardi va farqni hech kim
    /// tushuntira olmasdi.
    /// </summary>
    [Fact]
    public async Task Bosqichi_yoq_lid_orphan_bolib_korinadi()
    {
        await using var db = NewDb();
        var (s1, _, _) = await ThreeStagesAsync(db);
        await AddLeadsAsync(db, s1, 2, grade: 1);
        await AddLeadsAsync(db, "ochirilgan-ustun", 1, grade: 1);

        var funnel = await LeadFunnelQuery.BuildAsync(db);

        Assert.Equal(3, funnel.TotalLeads);
        Assert.Equal(2, funnel.FunnelLeads);
        Assert.Equal(1, funnel.OrphanCount);
    }

    /// <summary>
    /// Manba (source) maydoni bazada yo'q, shuning uchun kesim maqsadli sinf bo'yicha.
    /// </summary>
    [Fact]
    public async Task Sinf_kesimi_har_daraja_uchun_alohida_konversiya_beradi()
    {
        await using var db = NewDb();
        var (s1, _, s3) = await ThreeStagesAsync(db);
        await AddLeadsAsync(db, s1, 2, grade: 1);
        await AddLeadsAsync(db, s3, 2, grade: 1);
        await AddLeadsAsync(db, s1, 3, grade: 5);

        var funnel = await LeadFunnelQuery.BuildAsync(db);

        var first = funnel.Grades.Single(g => g.TargetGrade == 1);
        Assert.Equal(4, first.Total);
        Assert.Equal(2, first.ReachedFinalCount);
        Assert.Equal(50, first.ConversionPercent);

        var fifth = funnel.Grades.Single(g => g.TargetGrade == 5);
        Assert.Equal(3, fifth.Total);
        Assert.Equal(0, fifth.ReachedFinalCount);
        Assert.Equal(0, fifth.ConversionPercent);
    }

    // =====================================================================
    //  #6 — DAVOMAT INTIZOMI
    // =====================================================================

    /// <summary>
    /// Hisobotning butun mag'zi bitta testda, chunki uchta ta'rif bir-biriga tegib turadi:
    ///
    /// <list type="bullet">
    ///   <item><b>Belgilanmagan katak "keldi" EMAS</b> — u <c>Unchecked</c> ustuniga tushadi
    ///     (bosh sahifadagi kabi).</item>
    ///   <item><b>"Kech keldi" yo'qlik EMAS</b>, lekin balli bor — ball yeydi.</item>
    ///   <item><b>O'tilmagan dars davomatga kirmaydi</b> (na maxrajga, na yo'qlikka), AMMO
    ///     uning balli <c>DisciplineController.GetScores</c> dagidek hisoblanadi — aks holda
    ///     bu hisobot va Ballar nazorati bir o'quvchi uchun ikki xil raqam ko'rsatardi.</item>
    /// </list>
    /// </summary>
    [Fact]
    public async Task Yoqlik_kechikish_va_belgilanmagan_katak_ozicha_sanaladi()
    {
        await using var db = NewDb();
        var world = await SchoolAsync(db);

        // 1-dars: sababsiz (yo'qlik, −10). 2-dars: kech keldi (yo'qlik emas, −5).
        // 3-dars: hech kim belgilamagan → tekshirilmagan. 4-dars: O'TILMAGAN, lekin belgi bor.
        await MarkAsync(db, world, world.StudentA, period: 1, reasonId: world.Unexcused);
        await MarkAsync(db, world, world.StudentA, period: 2, reasonId: world.Late);
        await MarkAsync(db, world, world.StudentA, period: 4, reasonId: world.Unexcused);

        var report = await AttendanceDisciplineReport.BuildAsync(db, From, To);

        var row = report.Students.Single(r => r.StudentId == world.StudentA);
        Assert.Equal(3, row.Opportunities);   // o'tilgan darslar: 1, 2, 3
        Assert.Equal(1, row.Absences);        // faqat 1-dars
        Assert.Equal(1, row.Lates);           // 2-dars
        Assert.Equal(1, row.Unchecked);       // 3-dars
        Assert.Equal(66.7, row.AttendancePercent);

        // Ball: −10 (1-dars) −5 (2-dars) −10 (o'tilmagan 4-dars ham) = −25.
        Assert.Equal(-25, row.AttendancePoints);
        Assert.Equal(100 - 25, row.Remaining);
    }

    /// <summary>
    /// Sinf qatori BARCHA o'quvchilar bo'yicha yig'iladi — belgisi yo'q o'quvchining
    /// tekshirilmagan kataklari ham. Aks holda "sinfda 4 ta belgilanmagan katak bor"
    /// degan xabar hech qachon chiqmasdi.
    /// </summary>
    [Fact]
    public async Task Sinf_qatori_belgisi_yoq_oquvchini_ham_hisobga_oladi()
    {
        await using var db = NewDb();
        var world = await SchoolAsync(db);
        await MarkAsync(db, world, world.StudentA, period: 1, reasonId: world.Unexcused);

        var report = await AttendanceDisciplineReport.BuildAsync(db, From, To);

        var cls = Assert.Single(report.Classes);
        Assert.Equal(2, cls.Students);
        Assert.Equal(6, cls.Opportunities);   // 3 o'tilgan dars × 2 o'quvchi
        Assert.Equal(1, cls.Absences);
        Assert.Equal(5, cls.Unchecked);       // A: 2 ta, B: 3 ta
        Assert.Equal(-10, cls.AttendancePoints);

        // Jadvalda esa faqat belgisi borlar — toza o'quvchi qatorni ko'mib tashlamaydi.
        Assert.Single(report.Students);
        Assert.Equal(2, report.Totals.Students);
    }

    /// <summary>
    /// Davr filtri: chegaradan tashqaridagi belgi hisobotga kirmaydi, lekin
    /// <c>Remaining</c> (qoldi) BUTUN tarix bo'yicha qoladi — qoldiq davrga bo'linmaydi.
    /// </summary>
    [Fact]
    public async Task Davr_tashqarisidagi_belgi_kirmaydi_lekin_qoldiqqa_tasir_qiladi()
    {
        await using var db = NewDb();
        var world = await SchoolAsync(db);
        await MarkAsync(db, world, world.StudentA, period: 1, reasonId: world.Unexcused);
        // Avgust — so'ralgan davrdan tashqarida.
        await MarkAsync(db, world, world.StudentA, period: 1, reasonId: world.Unexcused, date: "2026-08-15");

        var report = await AttendanceDisciplineReport.BuildAsync(db, From, To);

        var row = report.Students.Single(r => r.StudentId == world.StudentA);
        Assert.Equal(1, row.Absences);
        Assert.Equal(-10, row.AttendancePoints);
        Assert.Equal(100 - 20, row.Remaining);
    }

    /// <summary>
    /// Sabablar kesimi ikki manbani ajratib beradi: jurnal davomati (<c>attendance</c>) va
    /// qo'lda kiritilgan ball (<c>manual</c>) — <c>DisciplineController</c> dagi nomlar bilan.
    /// </summary>
    [Fact]
    public async Task Sabablar_kesimi_davomat_va_qolda_kiritilganni_ajratadi()
    {
        await using var db = NewDb();
        var world = await SchoolAsync(db);
        await MarkAsync(db, world, world.StudentA, period: 1, reasonId: world.Unexcused);
        await MarkAsync(db, world, world.StudentA, period: 2, reasonId: world.Late);
        db.DisciplinePoints.Add(new DisciplinePoint
        {
            StudentId = world.StudentA,
            ReasonId = "qwl-1",
            ReasonName = "Darsni buzdi",
            Points = -7,
            CreatedAt = "2026-09-10T10:00:00.0000000+05:00",
            CreatedBy = "Administrator",
        });
        await db.SaveChangesAsync();

        var report = await AttendanceDisciplineReport.BuildAsync(db, From, To);

        var unexcused = report.Reasons.Single(r => r.Kind == "attendance" && r.Name == "Sababsiz");
        Assert.Equal(1, unexcused.Count);
        Assert.Equal(-10, unexcused.TotalPoints);

        var manual = report.Reasons.Single(r => r.Kind == "manual");
        Assert.Equal("Darsni buzdi", manual.Name);
        Assert.Equal(-7, manual.TotalPoints);

        var row = report.Students.Single(r => r.StudentId == world.StudentA);
        Assert.Equal(-7, row.ManualPoints);
        Assert.Equal(-7, report.Totals.ManualPoints);
        Assert.Equal(100 - 15 - 7, row.Remaining);
    }

    // =====================================================================
    //  RBAC — har ikki endpoint uchun
    // =====================================================================

    /// <summary>
    /// Voronka <c>LeadsController</c> ning darvozasida (<c>AdminPerm("leads")</c>): o'qituvchi
    /// admin bo'limiga umuman kira olmaydi, token'siz so'rov esa 401.
    /// </summary>
    [Theory]
    [InlineData("api/admin/leads/funnel")]
    [InlineData("api/admin/attendance-discipline-report")]
    public async Task Hisobot_tokensiz_401_va_oqituvchiga_403(string path)
    {
        using var anonymous = fixture.Api.AnonymousClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/" + path)).StatusCode);

        using var teacher = await fixture.Api.ClientAsAsync(Roles.Teacher);
        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync("/" + path)).StatusCode);
    }

    /// <summary>
    /// Admin — 200. Hisobot bo'sh bazada ham yiqilmasligi kerak (umumiy bazada lid ham,
    /// davomat ham bo'lmasligi mumkin).
    /// </summary>
    [Theory]
    [InlineData("api/admin/leads/funnel")]
    [InlineData("api/admin/attendance-discipline-report")]
    public async Task Hisobot_adminga_ochiq(string path)
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var response = await admin.GetAsync("/" + path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// Ota-ona roli ham rad etiladi — <c>AdminPermAttribute</c> faqat admin/superadmin/staff
    /// ni o'tkazadi. (O'quvchi roli bu yerda sinalmaydi: uning tokeni
    /// <c>Program.cs OnTokenValidated</c> da <c>Student</c> qatorini talab qiladi va javob
    /// darvozagacha yetmay 401 bo'lardi — ya'ni test boshqa narsani tekshirgan bo'lardi.)
    /// </summary>
    [Fact]
    public async Task Intizom_hisobotiga_ota_ona_kira_olmaydi()
    {
        using var client = await fixture.Api.ClientAsAsync("parent");
        var response = await client.GetAsync("/api/admin/attendance-discipline-report");
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>
    /// <b>Ataylab yozilgan "ruxsat berilgan" kataklar.</b> <c>AdminPermAttribute</c> bo'yicha
    /// xodim (staff) uchun O'QISH har doim ochiq — ruxsat kaliti faqat YOZISHni cheklaydi
    /// (izoh atributning o'zida). Ya'ni <c>leads</c> ruxsatli xodim intizom hisobotini ham
    /// o'qiy oladi. Bu butun tizimga tegishli qoida, shu ikki endpoint uchun o'zgartirilmadi;
    /// test uni KO'RINADIGAN qilib qo'yadi — kimdir bu qoidani qattiqlashtirsa, shu yerda
    /// qizil bo'ladi va qaroni ongli ravishda qayta ko'radi.
    /// </summary>
    [Theory]
    [InlineData("api/admin/leads/funnel", "discipline")]
    [InlineData("api/admin/attendance-discipline-report", "leads")]
    public async Task Xodim_boshqa_bolim_ruxsati_bilan_ham_oqiy_oladi(string path, string otherPerm)
    {
        using var staff = await fixture.Api.ClientAsAsync(Roles.Staff, otherPerm);
        var response = await staff.GetAsync("/" + path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    private static async Task<(string S1, string S2, string S3)> ThreeStagesAsync(AppDbContext db)
    {
        var s1 = new LeadStage { Title = "Yangi", Color = "slate", Order = 0 };
        var s2 = new LeadStage { Title = "Aloqada", Color = "blue", Order = 1 };
        var s3 = new LeadStage { Title = "Shartnoma", Color = "emerald", Order = 2 };
        db.LeadStages.AddRange(s1, s2, s3);
        await db.SaveChangesAsync();
        return (s1.Id, s2.Id, s3.Id);
    }

    private static async Task AddLeadsAsync(AppDbContext db, string stageId, int count, int grade)
    {
        for (var i = 0; i < count; i++)
            db.Leads.Add(new Lead
            {
                FullName = $"Lid {stageId[..4]}-{grade}-{i}",
                ParentFullName = "Ota-ona",
                ParentPhone = "+998900000000",
                TargetGrade = grade,
                Stage = stageId,
            });
        await db.SaveChangesAsync();
    }

    /// <summary>Bitta sinf, ikki o'quvchi, uchta o'tilgan va bitta o'tilmagan dars.</summary>
    private sealed record World(
        string ClassId, string SubjectId, string StudentA, string StudentB,
        string Unexcused, string Late, string Ill);

    private static async Task<World> SchoolAsync(AppDbContext db)
    {
        var cls = new SchoolClass { Name = "5-A", Grade = 5 };
        var subject = new Subject { Name = "Matematika" };
        db.Classes.Add(cls);
        db.Subjects.Add(subject);

        var a = new Student { FullName = "Aliyev Ali", ClassName = cls.Name };
        var b = new Student { FullName = "Valiyev Vali", ClassName = cls.Name };
        db.Students.AddRange(a, b);

        var unexcused = new AbsenceReason { Name = "Sababsiz", Short = "S", IsLate = false, Points = -10 };
        var late = new AbsenceReason { Name = "Kech keldi", Short = "K", IsLate = true, Points = -5 };
        var ill = new AbsenceReason { Name = "Kasal", Short = "Ks", IsLate = false, Points = 0 };
        db.AbsenceReasons.AddRange(unexcused, late, ill);

        for (var period = 1; period <= 4; period++)
            db.LessonNotes.Add(new LessonNote
            {
                ClassId = cls.Id,
                SubjectId = subject.Id,
                Quarter = 1,
                Date = "2026-09-10",
                Period = period,
                Topic = "Mavzu",
                // 4-dars ATAYLAB o'tilmagan.
                Conducted = period <= 3,
            });

        await db.SaveChangesAsync();
        return new World(cls.Id, subject.Id, a.Id, b.Id, unexcused.Id, late.Id, ill.Id);
    }

    private static async Task MarkAsync(
        AppDbContext db, World world, string studentId, int period, string reasonId,
        string date = "2026-09-10")
    {
        db.JournalEntries.Add(new JournalEntry
        {
            ClassId = world.ClassId,
            SubjectId = world.SubjectId,
            Quarter = 1,
            StudentId = studentId,
            Date = date,
            Period = period,
            ReasonId = reasonId,
        });
        await db.SaveChangesAsync();
    }
}
