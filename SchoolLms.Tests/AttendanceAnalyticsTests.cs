using System.Net;
using System.Text.Json;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using Npgsql;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// Analitika: davomat roll-up'i (#5) va fanlar bo'yicha o'zlashtirish pivoti (#3).
///
/// <para>
/// <b>Testlar ikki guruhga bo'lingan</b> — <c>FinanceReportsTests</c> dagi kabi va shu sabab bilan.
/// </para>
/// <list type="number">
///   <item><b>RUXSAT (HTTP)</b> — umumiy bazada: kim bu ekranni ocha oladi.</item>
///   <item><b>ARIFMETIKA</b> — har biri O'ZINING toza bazasida
///     (<c>CreateDatabaseAsync</c>). Roll-up butun maktabni yig'adi, ya'ni qo'shni
///     testning bitta jurnal yozuvi ham raqamni o'zgartirardi.</item>
/// </list>
///
/// <para>
/// <b>Eng muhim mezon — "tekshirilmagan" ustuni.</b> Davomati belgilanmagan o'quvchini
/// "keldi"ga qo'shish eng oson va eng zararli xato bo'lardi: zavuch 100% ko'rib, aslida
/// jurnal ochilmaganini bilmay qolardi. Shuning uchun quyida
/// <c>Present + Absent + Unchecked = Opportunities</c> aniq tekshiriladi.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class AttendanceAnalyticsTests(ApiFixture fixture) : IAsyncLifetime
{
    /// <summary>
    /// Shu klass yaratgan bazalarning ulanish satrlari.
    ///
    /// <para>
    /// <b>Nega kerak.</b> Har test O'Z bazasini oladi, ya'ni O'Z ulanish
    /// hovuzini ham. Hovuz tozalanmasa tugagan testning ulanishlari ochiq
    /// qolib, konteynerdagi <c>max_connections</c> ni yeb qo'yadi va KEYINGI
    /// test klasslari <c>53300</c> bilan yiqiladi — o'z aybi bilan emas.
    /// <c>AllocationTests</c> da xuddi shu izoh bor; bu yerda bazalar test
    /// ichida yaratilgani uchun ro'yxat yuritiladi.
    /// </para>
    /// </summary>
    private readonly List<string> _connectionStrings = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync()
    {
        foreach (var connectionString in _connectionStrings)
            NpgsqlConnection.ClearPool(new NpgsqlConnection(connectionString));

        return Task.CompletedTask;
    }

    private const string Analytics = "/api/admin/attendance/analytics";
    private const string SubjectPivot = "/api/admin/grades-report/subjects";

    private static readonly string[] BothReports = [Analytics, SubjectPivot];

    // =====================================================================
    //  1. RUXSAT
    // =====================================================================

    /// <summary>Token'siz so'rov — 401 (403 emas): kim so'rayotgani umuman noma'lum.</summary>
    [Theory]
    [InlineData(Analytics)]
    [InlineData(SubjectPivot)]
    public async Task Tokensiz_sorov_401(string url)
    {
        using var client = fixture.Api.AnonymousClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(url)).StatusCode);
    }

    /// <summary>
    /// O'qituvchi, kassir va ota-ona — yopiq. Bu maktab BO'YLAB hisobot: bitta sinfning
    /// o'qituvchisi ham, kassir ham butun maktabning davomati va baholarini ko'rmaydi.
    /// Ruxsat mavjud endpoint'lar bilan bir xil darvozadan (<c>AdminPerm</c> /
    /// <c>Authorize(Roles=...)</c>) o'tadi — yangi, alohida qoida kiritilmadi.
    ///
    /// <para><c>student</c> roli bu yerda YO'Q va bu ataylab: <c>Program.cs</c> ning
    /// <c>OnTokenValidated</c> hodisasi o'quvchi tokeni uchun <c>students.user_id</c> qatorini
    /// talab qiladi, ya'ni bunday token 403 emas, 401 qaytaradi — boshqa qatlamning testi.</para>
    /// </summary>
    [Theory]
    [InlineData(Roles.Teacher)]
    [InlineData(Roles.Cashier)]
    [InlineData("parent")]
    public async Task Oqituvchi_kassir_va_ota_ona_kira_olmaydi_403(string role)
    {
        using var client = await fixture.Api.ClientAsAsync(role, "attendance", "gradesReport");

        foreach (var url in BothReports)
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(url)).StatusCode);
    }

    /// <summary>
    /// Admin, direktor va xodim — ha. Xodim uchun O'QISH mavjud
    /// <c>AdminPermAttribute</c> qoidasi bo'yicha ochiq (bo'limlararo bog'liqliklar uchun) —
    /// yangi endpoint eski qoidaga AYNAN ergashadi, o'zining qoidasini o'ylab topmaydi.
    /// </summary>
    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.SuperAdmin)]
    [InlineData(Roles.Staff)]
    public async Task Admin_direktor_va_xodim_kira_oladi_200(string role)
    {
        using var client = await fixture.Api.ClientAsAsync(role, "attendance", "gradesReport");

        foreach (var url in BothReports)
        {
            var response = await client.GetAsync(url);
            Assert.True(response.IsSuccessStatusCode,
                $"{url} → {(int)response.StatusCode} {response.StatusCode}");
        }
    }

    /// <summary>
    /// Holat kodi yetarli emas: javob SHAKLI ham kutilganidek bo'lsin — frontend aynan shu
    /// JSON nomlariga bog'lanadi. Foizlar SERVERDAN keladi (brauzerda hisoblanmaydi).
    /// </summary>
    [Fact]
    public async Task Javob_shakli_frontend_kutgan_nomlar_bilan_keladi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);

        using var att = JsonDocument.Parse(
            await (await client.GetAsync(Analytics)).Content.ReadAsStringAsync());
        foreach (var name in new[]
                 { "from", "to", "day", "studentsTotal", "total", "classes", "periods", "trend", "reasons" })
            Assert.True(att.RootElement.TryGetProperty(name, out _), $"'{name}' yo'q");

        var total = att.RootElement.GetProperty("total");
        foreach (var name in new[]
                 {
                     "lessons", "opportunities", "present", "absent", "excused", "unexcused",
                     "late", "unchecked", "presentPct", "absentPct", "uncheckedPct",
                 })
            Assert.True(total.TryGetProperty(name, out _), $"total.{name} yo'q");

        using var pivot = JsonDocument.Parse(
            await (await client.GetAsync(SubjectPivot)).Content.ReadAsStringAsync());
        foreach (var name in new[] { "quarters", "subjects", "rows", "school" })
            Assert.True(pivot.RootElement.TryGetProperty(name, out _), $"'{name}' yo'q");
    }

    // =====================================================================
    //  2. DAVOMAT ARIFMETIKASI (toza bazada)
    // =====================================================================

    /// <summary>
    /// Bitta sinf, bitta kun, ikkita dars. Har bir holat ATAYLAB bor: keldi, sababli,
    /// sababsiz, kech keldi va BELGILANMAGAN.
    ///
    /// <para>Tekshiriladigan uchta narsa:</para>
    /// <list type="number">
    ///   <item><c>Present + Absent + Unchecked = Opportunities</c> — "tekshirilmagan" o'z
    ///     ustunida qoladi va hech qaerga qo'shilmaydi;</item>
    ///   <item>"kech keldi" yo'qlik EMAS — u <c>Present</c> ichida, lekin <c>Late</c> ham;</item>
    ///   <item>o'tilMAGAN dars maxrajga umuman kirmaydi.</item>
    /// </list>
    /// </summary>
    [Fact]
    public async Task Tekshirilmagan_keldiga_qoshilmaydi_va_kech_keldi_yoqlik_emas()
    {
        await using var db = await NewDbAsync("att");
        var s = await SeedSchoolAsync(db);

        // 1-dars (Matematika, 2026-03-02): 5 o'quvchidan 4 tasi belgilangan.
        //   A — belgilangan, sababsiz yo'qlik
        //   B — belgilangan, sababli (kasal)
        //   C — belgilangan, kech keldi  → KELDI
        //   D — belgilangan, sabab yo'q  → KELDI
        //   E — UMUMAN BELGILANMAGAN     → tekshirilmagan
        Conducted(db, s, s.Math, "2026-03-02", 1);
        Mark(db, s, s.Math, "2026-03-02", 1, s.A, s.Unexcused);
        Mark(db, s, s.Math, "2026-03-02", 1, s.B, s.Illness);
        Mark(db, s, s.Math, "2026-03-02", 1, s.C, s.Late);
        Mark(db, s, s.Math, "2026-03-02", 1, s.D, null);

        // 2-dars (Fizika, o'sha kun): hamma belgilangan va hamma kelgan.
        Conducted(db, s, s.Physics, "2026-03-02", 2);
        foreach (var id in s.All) Mark(db, s, s.Physics, "2026-03-02", 2, id, null);

        // 3-dars: jadvalda bor, lekin O'TILMAGAN — maxrajga kirmaydi.
        db.LessonNotes.Add(new LessonNote
        {
            ClassId = s.ClassId, SubjectId = s.Math, Quarter = 3,
            Date = "2026-03-02", Period = 3, Topic = "O'tilmadi", Conducted = false,
        });

        await db.SaveChangesAsync();

        var report = await AttendanceAnalytics.BuildAsync(db, s.ClassId, "2026-03-01", "2026-03-07", "2026-03-02");

        var t = report.Total;
        Assert.Equal(2, t.Lessons);              // o'tilMAGAN dars sanalmaydi
        Assert.Equal(10, t.Opportunities);       // 2 dars × 5 o'quvchi
        Assert.Equal(t.Opportunities, t.Present + t.Absent + t.Unchecked);
        Assert.Equal(7, t.Present);              // 1-darsda C va D, 2-darsda 5 ta
        Assert.Equal(2, t.Absent);               // A (sababsiz) + B (kasal)
        Assert.Equal(1, t.Excused);
        Assert.Equal(1, t.Unexcused);
        Assert.Equal(1, t.Late);                 // C — Present ICHIDA
        Assert.Equal(1, t.Unchecked);            // E
        Assert.Equal(70.0, t.PresentPct);
        Assert.Equal(20.0, t.AbsentPct);
        Assert.Equal(10.0, t.UncheckedPct);

        // Dars soatlari kesimi — tanlangan kun bo'yicha, 1- va 2-dars alohida.
        Assert.Equal(2, report.Periods.Count);
        var first = report.Periods.Single(p => p.Period == 1);
        Assert.Equal(5, first.Tally.Opportunities);
        Assert.Equal(1, first.Tally.Unchecked);
        Assert.Equal("08:30", first.StartTime);
        Assert.Equal(0, report.Periods.Single(p => p.Period == 2).Tally.Unchecked);

        // Sabablar taqsimoti — xom haqiqat, sababli/sababsiz ta'rifidan mustaqil.
        Assert.Equal(1, report.Reasons.Single(r => r.ReasonId == s.Unexcused).Count);
        Assert.True(report.Reasons.Single(r => r.ReasonId == s.Unexcused).Unexcused);
        Assert.False(report.Reasons.Single(r => r.ReasonId == s.Illness).Unexcused);
        Assert.Equal(1, report.Reasons.Single(r => r.ReasonId == s.Late).Count);
    }

    /// <summary>
    /// Bo'lingan darsda (SubGroup) BOSHQA guruhning o'quvchisi maxrajga kirmasligi kerak —
    /// aks holda 2-guruh bolasi 1-guruh darsiga "kelmagan" bo'lib chiqardi va butun
    /// sinfning davomati soxta pasayardi. Ta'rif <c>Analytics.BuildClass</c> dagi bilan bir xil.
    /// </summary>
    [Fact]
    public async Task Bolingan_darsda_faqat_oz_guruhi_hisoblanadi()
    {
        await using var db = await NewDbAsync("sub");
        var s = await SeedSchoolAsync(db);

        // A va B — 1-guruh, C va D — 2-guruh, E — guruhsiz (SubGroup = 0).
        await SetSubGroupAsync(db, s.A, 1);
        await SetSubGroupAsync(db, s.B, 1);
        await SetSubGroupAsync(db, s.C, 2);
        await SetSubGroupAsync(db, s.D, 2);

        // Faqat 1-guruh darsi o'tildi. Hech kim belgilanmagan.
        db.LessonNotes.Add(new LessonNote
        {
            ClassId = s.ClassId, SubjectId = s.Math, Quarter = 3,
            Date = "2026-03-03", Period = 1, Topic = "Guruh darsi",
            Conducted = true, SubGroup = 1,
        });
        await db.SaveChangesAsync();

        var report = await AttendanceAnalytics.BuildAsync(db, s.ClassId, "2026-03-01", "2026-03-07", "2026-03-03");

        // Maxraj — faqat A va B. E (guruhsiz, SubGroup = 0) bo'lingan darsga kirmaydi.
        Assert.Equal(2, report.Total.Opportunities);
        Assert.Equal(2, report.Total.Unchecked);
        Assert.Equal(0, report.Total.Present);
    }

    /// <summary>
    /// Davr, trend va sinflar kesimi. Ikki sinf, ikki kun: har kesim o'z raqamini beradi va
    /// sinflar yig'indisi umumiy yig'indiga TENG bo'ladi (jadval o'zi bilan ziddiyatga tushmasin).
    /// </summary>
    [Fact]
    public async Task Trend_va_sinflar_kesimi_umumiy_yigindiga_mos()
    {
        await using var db = await NewDbAsync("trend");
        var s = await SeedSchoolAsync(db);
        var second = await AddClassAsync(db, "6-B", 6, 3);

        Conducted(db, s, s.Math, "2026-03-02", 1);
        foreach (var id in s.All) Mark(db, s, s.Math, "2026-03-02", 1, id, null);

        Conducted(db, s, s.Math, "2026-03-03", 1);
        Mark(db, s, s.Math, "2026-03-03", 1, s.A, s.Unexcused);

        db.LessonNotes.Add(new LessonNote
        {
            ClassId = second.ClassId, SubjectId = s.Math, Quarter = 3,
            Date = "2026-03-03", Period = 1, Topic = "Dars", Conducted = true,
        });
        await db.SaveChangesAsync();

        var report = await AttendanceAnalytics.BuildAsync(db, null, "2026-03-01", "2026-03-07", "2026-03-03");

        Assert.Equal(8, report.StudentsTotal);                       // 5 + 3
        Assert.Equal(13, report.Total.Opportunities);                // 5 + 5 + 3
        Assert.Equal(report.Total.Opportunities, report.Classes.Sum(c => c.Tally.Opportunities));
        Assert.Equal(report.Total.Unchecked, report.Classes.Sum(c => c.Tally.Unchecked));

        var trendDates = report.Trend.Select(p => p.Date).ToList();
        Assert.Equal(new[] { "2026-03-02", "2026-03-03" }, trendDates);
        Assert.Equal(5, report.Trend[0].Tally.Opportunities);
        Assert.Equal(8, report.Trend[1].Tally.Opportunities);
        // Birinchi kuni hamma kelgan — 100%, ikkinchi kuni jurnal deyarli ochilmagan.
        Assert.Equal(100.0, report.Trend[0].Tally.PresentPct);
        Assert.Equal(7, report.Trend[1].Tally.Unchecked);
    }

    /// <summary>
    /// Davr chegarasi qat'iy: chegaradan tashqaridagi kun hisobotga TUSHMAYDI. Sanalar
    /// "yyyy-MM-dd" satr sifatida taqqoslanadi (loyihaning mavjud modeli) — shuning uchun
    /// bu alohida tekshiriladi.
    /// </summary>
    [Fact]
    public async Task Davr_chegarasidan_tashqaridagi_kun_hisobga_olinmaydi()
    {
        await using var db = await NewDbAsync("range");
        var s = await SeedSchoolAsync(db);

        Conducted(db, s, s.Math, "2026-02-28", 1);   // davrdan OLDIN
        Conducted(db, s, s.Math, "2026-03-02", 1);   // ichida
        Conducted(db, s, s.Math, "2026-03-08", 1);   // davrdan KEYIN
        await db.SaveChangesAsync();

        var report = await AttendanceAnalytics.BuildAsync(db, s.ClassId, "2026-03-01", "2026-03-07", null);

        Assert.Equal(1, report.Total.Lessons);
        Assert.Equal("2026-03-02", Assert.Single(report.Trend).Date);
        // `day` berilmadi — davrning oxirgi kuni olinadi, u kuni dars yo'q.
        Assert.Equal("2026-03-07", report.Day);
        Assert.Empty(report.Periods);
    }

    // =====================================================================
    //  3. O'ZLASHTIRISH (FANLAR BO'YICHA) ARIFMETIKASI
    // =====================================================================

    /// <summary>
    /// Pivotning asosiy qoidasi: RASMIY chorak bahosi kunlik baholar o'rtachasining
    /// o'rnini bosadi — <c>grades-report/class</c> dagi bilan aynan bir xil. Agar bu qoida
    /// buzilsa, ikki hisobot bir xil sinf uchun ikki xil raqam ko'rsatardi.
    /// </summary>
    [Fact]
    public async Task Rasmiy_chorak_bahosi_kunlik_ortachani_almashtiradi()
    {
        await using var db = await NewDbAsync("pivot");
        var s = await SeedSchoolAsync(db);

        // A: kunlik 3 va 3 (o'rtacha 3), lekin rasmiy chorak bahosi — 5.
        Grade(db, s, s.Math, 3, s.A, 3);
        Grade(db, s, s.Math, 3, s.A, 3);
        db.QuarterGrades.Add(new QuarterGrade
        {
            ClassId = s.ClassId, SubjectId = s.Math, Quarter = 3, StudentId = s.A, Grade = 5,
        });
        // B: rasmiy bahosi yo'q — kunlik o'rtacha (4 va 4 → 4).
        Grade(db, s, s.Math, 3, s.B, 4);
        Grade(db, s, s.Math, 3, s.B, 4);
        // C: bu fandan umuman bahosi yo'q — katakka KIRMAYDI (nol bo'lib tortmaydi).
        await db.SaveChangesAsync();

        var report = await SubjectAttainmentReport.BuildAsync(db, [s.ClassId], [3]);

        var row = Assert.Single(report.Rows);
        var cell = row.Cells.Single(c => c.SubjectId == s.Math);
        Assert.Equal(2, cell.Values);               // faqat A va B
        Assert.Equal(4.5, cell.Average);            // (5 + 4) / 2 — kunlik 3 emas
        Assert.Equal(100.0, cell.QualityPct);       // 5 va 4 — ikkalasi ham "sifat"
        Assert.Equal(4.5, cell.ByQuarter[3]);

        // Bahosi yo'q fan katagi BO'SH (null), nol emas.
        var physics = row.Cells.Single(c => c.SubjectId == s.Physics);
        Assert.Equal(0, physics.Values);
        Assert.Null(physics.Average);

        // Maktab qatori mavjud va shu bitta sinfdan kelib chiqadi.
        Assert.Equal("school", report.School.Kind);
        Assert.Equal(4.5, report.School.Average);
        Assert.Equal(row.Average, report.School.Average);
    }

    /// <summary>
    /// Ikki sinf, ikki chorak: kataklar ustunlar ro'yxati bilan bir xil tartibda keladi,
    /// chorak yoyilmasi to'g'ri, maktab o'rtachasi esa HAMMA qiymatlardan hisoblanadi
    /// (sinf o'rtachalarining o'rtachasi emas — kichik sinf katta sinfni tortib yubormasin).
    /// </summary>
    [Fact]
    public async Task Ikki_sinf_ikki_chorak_maktab_ortachasi_hamma_qiymatdan()
    {
        await using var db = await NewDbAsync("pivot2");
        var s = await SeedSchoolAsync(db);
        var second = await AddClassAsync(db, "6-B", 6, 1);

        // 5-A: A → 3-chorak 5, 4-chorak 3. B → 3-chorak 4.
        Quarter(db, s.ClassId, s.Math, 3, s.A, 5);
        Quarter(db, s.ClassId, s.Math, 4, s.A, 3);
        Quarter(db, s.ClassId, s.Math, 3, s.B, 4);
        // 6-B: bitta o'quvchi, 3-chorak 2.
        Quarter(db, second.ClassId, s.Math, 3, second.StudentIds[0], 2);
        await db.SaveChangesAsync();

        var report = await SubjectAttainmentReport.BuildAsync(db, [s.ClassId, second.ClassId], [3, 4]);

        Assert.Equal(new[] { 3, 4 }, report.Quarters);
        Assert.All(report.Rows, r => Assert.Equal(report.Subjects.Count, r.Cells.Count));
        Assert.All(report.Rows, r => Assert.Equal(
            report.Subjects.Select(x => x.Id), r.Cells.Select(c => c.SubjectId)));

        var first = report.Rows.Single(r => r.ClassId == s.ClassId);
        var cell = first.Cells.Single(c => c.SubjectId == s.Math);
        Assert.Equal(4.0, cell.Average);          // (5 + 3 + 4) / 3
        Assert.Equal(4.5, cell.ByQuarter[3]);     // (5 + 4) / 2
        Assert.Equal(3.0, cell.ByQuarter[4]);
        Assert.Equal(66.7, cell.QualityPct);      // 5 va 4 — sifat; 3 — yo'q

        // Maktab: (5 + 3 + 4 + 2) / 4 = 3.5. Sinf o'rtachalarining o'rtachasi 3.0 bo'lardi.
        Assert.Equal(3.5, report.School.Average);
        Assert.Equal(6, report.School.Students);
    }

    /// <summary>Sinf yoki chorak tanlanmasa — bo'sh hisobot, xato emas (ekran endi ochildi).</summary>
    [Fact]
    public async Task Tanlov_bosh_bolsa_bosh_hisobot()
    {
        await using var db = await NewDbAsync("pivot3");
        var s = await SeedSchoolAsync(db);

        Assert.Empty((await SubjectAttainmentReport.BuildAsync(db, [], [3])).Rows);
        Assert.Empty((await SubjectAttainmentReport.BuildAsync(db, [s.ClassId], [])).Rows);
    }

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    /// <summary>Test uchun tayyorlangan maktab: bitta sinf, ikki fan, besh o'quvchi, sabablar.</summary>
    private sealed record Seed(
        string ClassId, string Math, string Physics,
        string A, string B, string C, string D, string E,
        string Illness, string Unexcused, string Late)
    {
        public List<string> All => [A, B, C, D, E];
    }

    private async Task<AppDbContext> NewDbAsync(string prefix)
    {
        var database = await fixture.Postgres.CreateDatabaseAsync("analytics_" + prefix);
        _connectionStrings.Add(database.OwnerConnectionString);
        return PostgresFixture.NewContext(database.OwnerConnectionString);
    }

    private static async Task<Seed> SeedSchoolAsync(AppDbContext db)
    {
        var cls = new SchoolClass { Name = "5-A", Grade = 5, Language = "uz" };
        var math = new Subject { Name = "Matematika" };
        var physics = new Subject { Name = "Fizika" };
        db.Classes.Add(cls);
        db.Subjects.AddRange(math, physics);

        var students = Enumerable.Range(0, 5)
            .Select(i => NewStudent($"O'quvchi {(char)('A' + i)}", "5-A"))
            .ToList();
        db.Students.AddRange(students);

        // Nomlar ataylab mijoznikiga o'xshash: "sababsiz" ajratish nomdan olinadi.
        var illness = new AbsenceReason { Name = "Kasal", Short = "K" };
        var unexcused = new AbsenceReason { Name = "Sababsiz", Short = "SS" };
        var late = new AbsenceReason { Name = "Kech keldi", Short = "Kch", IsLate = true };
        db.AbsenceReasons.AddRange(illness, unexcused, late);

        db.LessonTimes.Add(new LessonTime { Period = 1, StartTime = "08:30", EndTime = "09:15" });
        db.LessonTimes.Add(new LessonTime { Period = 2, StartTime = "09:25", EndTime = "10:10" });
        db.Quarters.Add(new QuarterPeriod { Quarter = 3, StartDate = "2026-01-10", EndDate = "2026-03-20" });

        // Jadval — pivotning USTUNLARI shundan olinadi: sinf o'tadigan fan, hatto bahosi
        // hali yo'q bo'lsa ham, ustun bo'lib turishi kerak (bo'sh katak ham ma'lumot).
        db.ScheduleTemplates.Add(new ScheduleTemplate
        {
            ClassId = cls.Id,
            Name = "Asosiy",
            Lessons =
            [
                new ScheduleLesson { Day = 0, Period = 1, SubjectId = math.Id },
                new ScheduleLesson { Day = 0, Period = 2, SubjectId = physics.Id },
            ],
        });

        await db.SaveChangesAsync();

        return new Seed(
            cls.Id, math.Id, physics.Id,
            students[0].Id, students[1].Id, students[2].Id, students[3].Id, students[4].Id,
            illness.Id, unexcused.Id, late.Id);
    }

    /// <summary>Qo'shimcha sinf (o'z o'quvchilari bilan) — sinflar kesimi uchun.</summary>
    private sealed record ExtraClass(string ClassId, List<string> StudentIds);

    private static async Task<ExtraClass> AddClassAsync(
        AppDbContext db, string name, int grade, int studentCount)
    {
        var cls = new SchoolClass { Name = name, Grade = grade, Language = "uz" };
        db.Classes.Add(cls);
        var students = Enumerable.Range(0, studentCount)
            .Select(i => NewStudent($"{name} o'quvchi {i + 1}", name))
            .ToList();
        db.Students.AddRange(students);
        await db.SaveChangesAsync();
        return new ExtraClass(cls.Id, [.. students.Select(x => x.Id)]);
    }

    private static async Task SetSubGroupAsync(AppDbContext db, string studentId, int subGroup)
    {
        var st = await db.Students.FindAsync(studentId);
        st!.SubGroup = subGroup;
        await db.SaveChangesAsync();
    }

    private static void Conducted(AppDbContext db, Seed s, string subjectId, string date, int period) =>
        db.LessonNotes.Add(new LessonNote
        {
            ClassId = s.ClassId, SubjectId = subjectId, Quarter = 3,
            Date = date, Period = period, Topic = "Mavzu", Conducted = true,
        });

    private static void Mark(
        AppDbContext db, Seed s, string subjectId, string date, int period,
        string studentId, string? reasonId) =>
        db.JournalEntries.Add(new JournalEntry
        {
            ClassId = s.ClassId, SubjectId = subjectId, Quarter = 3,
            StudentId = studentId, Date = date, Period = period, ReasonId = reasonId,
        });

    private static void Grade(AppDbContext db, Seed s, string subjectId, int quarter, string studentId, int grade) =>
        db.JournalEntries.Add(new JournalEntry
        {
            ClassId = s.ClassId, SubjectId = subjectId, Quarter = quarter,
            StudentId = studentId, Date = "2026-03-02", Period = 1, Grade = grade,
        });

    private static void Quarter(
        AppDbContext db, string classId, string subjectId, int quarter, string studentId, int grade) =>
        db.QuarterGrades.Add(new QuarterGrade
        {
            ClassId = classId, SubjectId = subjectId, Quarter = quarter,
            StudentId = studentId, Grade = grade,
        });

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
