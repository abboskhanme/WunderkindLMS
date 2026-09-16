using System.Net;
using System.Text.Json;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using Npgsql;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// Turniket hisobotlari (docs/modules/existing-module-gaps.md §4 — #11, #12, #13).
///
/// <para>
/// <b>Testlar ikki guruhga bo'lingan, <see cref="FinanceReportsTests"/> dagi kabi va
/// xuddi shu sabab bilan.</b>
/// </para>
/// <list type="number">
///   <item><b>RUXSAT (HTTP)</b> — umumiy bazada: bu yerda javob SHAKLI emas, KIM kira
///     olishi tekshiriladi.</item>
///   <item><b>ARIFMETIKA</b> — har biri O'ZINING toza bazasida. Hisobot butun maktabni
///     yig'adi (barcha o'quvchilar, barcha hodisalar), ya'ni umumiy bazada qo'shni
///     testning bitta o'quvchisi ham "kelmaganlar" sonini o'zgartirardi. Bunday test
///     bugun yashil, ertaga qizil bo'lardi.</item>
/// </list>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class TurnstileAnalyticsTests(ApiFixture fixture) : IAsyncLifetime
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

    private const string Base = "/api/admin/turnstile-analytics";

    private static readonly string[] AllEndpoints =
    [
        $"{Base}/attendance",
        $"{Base}/violations",
        $"{Base}/late-early/summary",
        $"{Base}/today-late-count",
        $"{Base}/flow",
        $"{Base}/daily-report",
    ];

    // Dushanba, o'tgan kun — hisobotdagi "kelmadi" faqat o'tgan kunlar uchun yoziladi.
    private const string Monday = "2026-09-07";
    private const string ClassName = "9-TURNIKET";

    // =====================================================================
    //  1. RUXSAT
    // =====================================================================

    /// <summary>
    /// O'qituvchi va kassir — 403. Turniket hisoboti butun maktabning kim qachon
    /// kelib-ketgani; o'qituvchi uni ko'rishi uchun ish sababi yo'q, kassirniki esa
    /// umuman boshqa bo'lim. (O'quvchi/ota-ona bu yerda emas: ular uchun token
    /// tekshiruvining o'zi 401 beradi — <c>Program.cs</c> `OnTokenValidated`.)
    /// </summary>
    [Theory]
    [InlineData(Roles.Teacher)]
    [InlineData(Roles.Cashier)]
    public async Task Oqituvchi_va_kassir_kira_olmaydi_403(string role)
    {
        using var client = await fixture.Api.ClientAsAsync(role, "students");

        foreach (var url in AllEndpoints)
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(url)).StatusCode);
    }

    /// <summary>Token'siz so'rov — 401 (403 emas): kim so'rayotgani umuman noma'lum.</summary>
    [Fact]
    public async Task Tokensiz_sorov_401()
    {
        using var client = fixture.Api.AnonymousClient();

        foreach (var url in AllEndpoints)
            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(url)).StatusCode);
    }

    /// <summary>
    /// Admin, direktor va xodim (staff) — ha. Xodim uchun O'QISH ochiq
    /// (<c>AdminPermAttribute</c>: GET har doim, yozish esa ruxsat kalitiga bog'liq) —
    /// bu yerdagi hamma endpoint GET.
    /// </summary>
    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.SuperAdmin)]
    [InlineData(Roles.Staff)]
    public async Task Admin_direktor_va_xodim_kira_oladi_200(string role)
    {
        using var client = await fixture.Api.ClientAsAsync(role);

        foreach (var url in AllEndpoints)
        {
            var response = await client.GetAsync(url);
            Assert.True(response.IsSuccessStatusCode,
                $"{url} → {(int)response.StatusCode} {response.StatusCode}");
        }
    }

    /// <summary>
    /// Javob SHAKLI (JSON nomlari) — frontend aynan shularga bog'lanadi.
    /// Nomi o'zgarsa sahifa jimgina bo'sh qolardi.
    /// </summary>
    [Fact]
    public async Task Javob_shakli_frontend_kutgan_maydonlarni_beradi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);

        using var daily = JsonDocument.Parse(
            await (await client.GetAsync($"{Base}/daily-report")).Content.ReadAsStringAsync());
        foreach (var field in new[] { "expected", "turnstileEntered", "journalPresent", "unchecked", "gap", "classes", "mismatches" })
            Assert.True(daily.RootElement.TryGetProperty(field, out _), $"`{field}` yo'q");

        using var flow = JsonDocument.Parse(
            await (await client.GetAsync($"{Base}/flow")).Content.ReadAsStringAsync());
        foreach (var field in new[] { "buckets", "classes", "peakLabel", "afterPeak", "avgCheckIn" })
            Assert.True(flow.RootElement.TryGetProperty(field, out _), $"`{field}` yo'q");

        using var late = JsonDocument.Parse(
            await (await client.GetAsync($"{Base}/today-late-count")).Content.ReadAsStringAsync());
        Assert.True(late.RootElement.TryGetProperty("late", out _));
    }

    /// <summary>
    /// Juda uzun oraliq — 400. <c>turnstile_events</c> da <c>event_at</c> indeksi yo'q,
    /// ya'ni bir yillik so'rov butun jadvalni skanerlardi.
    /// </summary>
    [Fact]
    public async Task Juda_uzun_oraliq_400_qaytaradi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);

        var response = await client.GetAsync($"{Base}/attendance?from=2025-01-01&to=2026-12-31");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("juda uzun", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    // =====================================================================
    //  2. #11 — davomat, kechikish, erta ketish (toza bazada)
    // =====================================================================

    /// <summary>
    /// Bir kun, beshta o'quvchi, har biri boshqa holatda. Kutilgan raqamlar testda
    /// MUSTAQIL sanaladi — hisobot mantig'i bu yerda qayta yozilmagan.
    /// </summary>
    [Fact]
    public async Task Davomat_kelgan_kelmagan_kechikkan_va_erta_ketganni_ajratadi()
    {
        await using var db = await NewDbAsync("att");
        await SeedDayAsync(db);
        var reports = new TurnstileAnalyticsQueries(db);

        var report = await reports.AttendanceAsync(
            DateOnly.Parse(Monday), DateOnly.Parse(Monday), ClassName, null);

        var s = report.Summary;
        Assert.Equal(5, s.Students);
        Assert.Equal(4, s.Linked);
        // Qurilma ID'siz o'quvchi "kelmagan" EMAS — turniket uni ko'ra olmaydi.
        Assert.Equal(1, s.Unlinked);
        Assert.Equal(3, s.Entered);
        Assert.Equal(1, s.NeverEntered);
        Assert.Equal(1, s.LateStudents);
        Assert.Equal(1, s.LateDays);
        Assert.Equal(1, s.EarlyStudents);
        Assert.Equal(1, s.EarlyDays);
        // 4 tadan 3 tasi kirdi — foiz SERVERDA hisoblanadi.
        Assert.Equal(75.0, s.AttendanceRate);

        // Qatorlarda faqat biriktirilganlar.
        Assert.Equal(4, report.Rows.Count);

        // Bekzod 09:05 da kirdi, birinchi dars 08:30 da — 35 daqiqa kechikish.
        var late = report.Rows.Single(r => r.FullName.StartsWith("Bekzod", StringComparison.Ordinal));
        Assert.Equal(1, late.LateDays);
        Assert.Equal(35, late.LateMinutes);
        Assert.Equal("09:05", late.AvgCheckIn);

        // Dilnoza umuman o'tmagan: o'tgan kun bo'lgani uchun "kelmadi" deb yoziladi.
        var missing = report.Rows.Single(r => r.FullName.StartsWith("Dilnoza", StringComparison.Ordinal));
        Assert.Equal(0, missing.DaysEntered);
        Assert.Equal(1, missing.DaysMissed);

        // Aziz o'z vaqtida (08:25, grace 10 daq.) — kechikish ham, erta ketish ham yo'q.
        var onTime = report.Rows.Single(r => r.FullName.StartsWith("Aziz", StringComparison.Ordinal));
        Assert.Equal(0, onTime.LateDays);
        Assert.Equal(0, onTime.EarlyDays);
    }

    /// <summary>
    /// <c>status=unlinked</c> — qurilma ID biriktirilmaganlar ro'yxati. Ular hisobotdan
    /// tashqarida qoladi, lekin KO'RINMAY qolmasligi kerak: direktor kimni biriktirish
    /// kerakligini shu ro'yxatdan biladi.
    /// </summary>
    [Fact]
    public async Task Biriktirilmaganlar_alohida_royxatda_chiqadi()
    {
        await using var db = await NewDbAsync("unlinked");
        await SeedDayAsync(db);

        var report = await new TurnstileAnalyticsQueries(db).AttendanceAsync(
            DateOnly.Parse(Monday), DateOnly.Parse(Monday), ClassName, "unlinked");

        var row = Assert.Single(report.Rows);
        Assert.StartsWith("Elbek", row.FullName, StringComparison.Ordinal);
        Assert.Equal("", row.DeviceUserId);
        Assert.Equal(0, row.DaysMissed);
    }

    /// <summary>
    /// Buzilishlar ro'yxati: bitta kechikish + bitta erta ketish, sahifalanadi.
    /// Kechikish "o'qituvchilar davomati" bilan BIR XIL formula bo'yicha aniqlanadi.
    /// </summary>
    [Fact]
    public async Task Buzilishlar_sahifalanadi_va_turi_boyicha_filtrlanadi()
    {
        await using var db = await NewDbAsync("viol");
        await SeedDayAsync(db);
        var reports = new TurnstileAnalyticsQueries(db);
        var day = DateOnly.Parse(Monday);

        var all = await reports.ViolationsAsync(day, day, ClassName, null, 1, 50);
        Assert.Equal(2, all.Total);
        Assert.Equal(1, all.LateTotal);
        Assert.Equal(1, all.EarlyTotal);

        var lateOnly = await reports.ViolationsAsync(day, day, ClassName, "late", 1, 50);
        var item = Assert.Single(lateOnly.Items);
        Assert.Equal("late", item.Type);
        Assert.Equal("09:05", item.CheckIn);
        Assert.Equal("08:30", item.Expected);
        Assert.Equal(35, item.Minutes);

        // Sahifalash: bittadan → ikki sahifa.
        var page2 = await reports.ViolationsAsync(day, day, ClassName, null, 2, 1);
        Assert.Equal(2, page2.Pages);
        Assert.Single(page2.Items);
    }

    // =====================================================================
    //  3. #12 — kirib-chiqish statistikasi
    // =====================================================================

    /// <summary>
    /// Kun davomidagi taqsimot: ertalabki to'lqin va undan KEYIN kirganlar.
    /// "Keyin kirganlar" — hisobotning butun sababi.
    /// </summary>
    [Fact]
    public async Task Kirish_chiqish_taqsimoti_tolqinni_va_undan_keyingilarni_korsatadi()
    {
        await using var db = await NewDbAsync("flow");
        await SeedDayAsync(db);
        var day = DateOnly.Parse(Monday);

        var flow = await new TurnstileAnalyticsQueries(db).FlowAsync(day, day, ClassName, "hour");

        Assert.Equal("hour", flow.GroupBy);
        Assert.Equal(3, flow.Entered);            // Aziz, Bekzod, Chori
        Assert.Equal(3, flow.Exited);             // uchalasining ham chiqishi qayd etilgan
        Assert.Equal(6, flow.Passes);             // 3 × (kirish + chiqish)

        // Eng gavjum soat — 08:00 (Aziz 08:25, Chori 08:20).
        Assert.Equal("08:00", flow.PeakLabel);
        Assert.Equal(2, flow.PeakEntered);
        // To'lqindan keyin bitta bola kirgan (Bekzod, 09:05).
        Assert.Equal(1, flow.AfterPeak);

        var eight = flow.Buckets.Single(b => b.Label == "08:00");
        Assert.Equal(2, eight.Entered);
        Assert.Equal(0, eight.Exited);
        var ten = flow.Buckets.Single(b => b.Label == "10:00");
        Assert.Equal(1, ten.Exited);              // Chori 10:00 da chiqib ketgan
        var eleven = flow.Buckets.Single(b => b.Label == "11:00");
        Assert.Equal(2, eleven.Exited);

        // Soatlar orasida bo'shliq qolmaydi (grafik uzilib qolmasin).
        Assert.Equal(
            new[] { "08:00", "09:00", "10:00", "11:00" },
            flow.Buckets.Select(b => b.Label).ToArray());
    }

    /// <summary>Dars vaqti kesimi: qo'ng'iroqlar jadvali bo'yicha oraliqlar.</summary>
    [Fact]
    public async Task Dars_kesimida_kirishlar_oz_darsiga_tushadi()
    {
        await using var db = await NewDbAsync("flowper");
        await SeedDayAsync(db);
        var day = DateOnly.Parse(Monday);

        var flow = await new TurnstileAnalyticsQueries(db).FlowAsync(day, day, ClassName, "period");

        Assert.Equal("period", flow.GroupBy);
        // 08:25 va 08:20 — birinchi dars (08:30) BOSHLANMASDAN oldin.
        var before = flow.Buckets.Single(b => b.Label == "Darslardan oldin");
        Assert.Equal(2, before.Entered);
        // 09:05 — birinchi dars vaqti ichida (08:30-09:25 oralig'i).
        Assert.Equal(1, flow.Buckets.Single(b => b.Label == "1-dars").Entered);
        // 11:10 — oxirgi dars (11:05) tugagandan keyin.
        Assert.Equal(2, flow.Buckets.Single(b => b.Label == "Darslardan keyin").Exited);
    }

    // =====================================================================
    //  4. #13 — kunlik davomat hisoboti (turniket ↔ jurnal)
    // =====================================================================

    /// <summary>
    /// <b>Hisobotning butun mag'zi.</b> Jamlanma farq (<c>gap</c>) NOL bo'lishi mumkin,
    /// ayni paytda ikkita haqiqiy nomuvofiqlik turgan bo'ladi: turniket ko'rgan, jurnal
    /// "yo'q" degan bola va aksincha. Shuning uchun hisobot faqat raqam emas, RO'YXAT
    /// ham qaytaradi — raqamning o'zi bu ikki bolani yashirardi.
    /// </summary>
    [Fact]
    public async Task Kunlik_hisobot_turniket_va_jurnal_farqini_royxat_bilan_korsatadi()
    {
        await using var db = await NewDbAsync("daily");
        await SeedDayAsync(db, withJournal: true);

        var report = await new TurnstileAnalyticsQueries(db).DailyReportAsync(DateOnly.Parse(Monday));

        Assert.Equal(5, report.Expected);
        Assert.Equal(4, report.Linked);
        Assert.Equal(3, report.TurnstileEntered);   // Aziz, Bekzod, Chori
        Assert.Equal(3, report.JournalPresent);     // Aziz, Bekzod (kech keldi), Dilnoza
        Assert.Equal(1, report.JournalAbsent);      // Chori
        Assert.Equal(1, report.Unchecked);          // Elbek — davomati umuman olinmagan

        // Jamlanma farq NOL, lekin ikkita haqiqiy nomuvofiqlik bor.
        Assert.Equal(0, report.Gap);
        Assert.Equal(1, report.TurnstileOnly);
        Assert.Equal(1, report.JournalOnly);
        Assert.Equal(2, report.MismatchTotal);

        var turnstileOnly = report.Mismatches.Single(m => m.Kind == "turnstile-only");
        Assert.StartsWith("Chori", turnstileOnly.FullName, StringComparison.Ordinal);
        Assert.Equal("absent", turnstileOnly.JournalStatus);
        Assert.Equal("Kasal", turnstileOnly.Reason);
        Assert.Equal("08:20", turnstileOnly.CheckIn);

        var journalOnly = report.Mismatches.Single(m => m.Kind == "journal-only");
        Assert.StartsWith("Dilnoza", journalOnly.FullName, StringComparison.Ordinal);

        var row = Assert.Single(report.Classes);
        Assert.Equal(ClassName, row.ClassName);
        Assert.Equal(5, row.Expected);
        Assert.Equal(60.0, row.JournalPct);        // 3/5
    }

    /// <summary>
    /// <b>"Tekshirilmagan" HECH QACHON "bor" ga qo'shilmaydi.</b> Bosh sahifadagi davomat
    /// blokidagi qoidaning aynan o'zi: davomati olinmagan o'quvchini "keldi" deb hisoblash —
    /// eng oson va eng zararli xato, chunki direktor 100% ko'rib, aslida hech kim davomat
    /// qo'ymaganini bilmay qolardi.
    /// </summary>
    [Fact]
    public async Task Tekshirilmagan_hech_qachon_bor_ga_qoshilmaydi()
    {
        await using var db = await NewDbAsync("unchecked");
        await SeedDayAsync(db); // jurnalga BITTA ham yozuv yo'q

        var report = await new TurnstileAnalyticsQueries(db).DailyReportAsync(DateOnly.Parse(Monday));

        Assert.Equal(5, report.Expected);
        Assert.Equal(0, report.JournalPresent);
        Assert.Equal(0, report.JournalAbsent);
        Assert.Equal(5, report.Unchecked);
        Assert.Equal(0.0, report.JournalPct);
        // Turniket uchtasini ko'rdi — jurnal esa hech kimni belgilamagan.
        Assert.Equal(3, report.TurnstileEntered);
        Assert.Equal(3, report.Gap);
        // Belgilanmagan kun — bu "farq" emas, davomat OLINMAGAN. Nomuvofiqlik ro'yxatiga
        // tushmaydi, aks holda butun maktab har kuni "nomuvofiq" bo'lib chiqardi.
        Assert.Equal(0, report.TurnstileOnly);
        Assert.Equal(0, report.MismatchTotal);
    }

    /// <summary>
    /// Yakshanba — o'quv kuni emas: hech kim "kelmadi" deb belgilanmaydi va sinf qatori
    /// umuman chiqmaydi (jadvalda o'sha kuni dars yo'q).
    /// </summary>
    [Fact]
    public async Task Yakshanba_oquv_kuni_emas()
    {
        await using var db = await NewDbAsync("sunday");
        await SeedDayAsync(db);
        var sunday = DateOnly.Parse(Monday).AddDays(-1); // 2026-09-06, yakshanba

        var report = await new TurnstileAnalyticsQueries(db).AttendanceAsync(sunday, sunday, ClassName, null);
        Assert.Equal(0, report.SchoolDays);
        Assert.All(report.Rows, r => Assert.Equal(0, r.DaysMissed));

        var daily = await new TurnstileAnalyticsQueries(db).DailyReportAsync(sunday);
        Assert.False(daily.SchoolDay);
        Assert.Empty(daily.Classes);
    }

    /// <summary>Bayram kuni ham o'quv kuni emas — "kelmadi" yozilmaydi.</summary>
    [Fact]
    public async Task Bayram_kuni_kelmadi_deb_belgilanmaydi()
    {
        await using var db = await NewDbAsync("holiday");
        await SeedDayAsync(db);
        db.Holidays.Add(new Holiday { Date = Monday, Name = "Bayram" });
        await db.SaveChangesAsync();

        var report = await new TurnstileAnalyticsQueries(db).AttendanceAsync(
            DateOnly.Parse(Monday), DateOnly.Parse(Monday), ClassName, null);

        Assert.Equal(0, report.SchoolDays);
        Assert.Equal(0, report.Summary.LateDays);
        Assert.All(report.Rows, r => Assert.Equal(0, r.DaysMissed));
    }

    // =====================================================================
    //  Ma'lumot tayyorlash
    // =====================================================================

    private async Task<AppDbContext> NewDbAsync(string prefix)
    {
        var database = await fixture.Postgres.CreateDatabaseAsync("turnstile_" + prefix);
        _connectionStrings.Add(database.OwnerConnectionString);
        return PostgresFixture.NewContext(database.OwnerConnectionString);
    }

    /// <summary>
    /// Bitta dushanba kuni: 3 ta dars (08:30-09:15, 09:25-10:10, 10:20-11:05), grace 10 daqiqa
    /// va beshta o'quvchi — har biri boshqa holatda:
    /// <list type="bullet">
    ///   <item>Aziz — 08:25 kirdi, 11:10 chiqdi (o'z vaqtida);</item>
    ///   <item>Bekzod — 09:05 kirdi (35 daqiqa kechikdi), 11:10 chiqdi;</item>
    ///   <item>Chori — 08:20 kirdi, 10:00 chiqdi (oxirgi darsdan 65 daqiqa oldin);</item>
    ///   <item>Dilnoza — umuman o'tmagan;</item>
    ///   <item>Elbek — qurilma ID biriktirilmagan (turniket uchun ko'rinmas).</item>
    /// </list>
    /// </summary>
    private static async Task SeedDayAsync(AppDbContext db, bool withJournal = false)
    {
        db.SchoolMeta.Add(new SchoolMeta
        {
            CurrentYear = "2026/2027",
            Name = "Test maktab",
            TurnstileEnabled = true,
            WorkStartTime = "08:30",
            LateGraceMinutes = 10,
        });
        db.Quarters.Add(new QuarterPeriod { Quarter = 1, StartDate = "2026-09-01", EndDate = "2026-12-31" });
        db.LessonTimes.AddRange(
            new LessonTime { Period = 1, StartTime = "08:30", EndTime = "09:15" },
            new LessonTime { Period = 2, StartTime = "09:25", EndTime = "10:10" },
            new LessonTime { Period = 3, StartTime = "10:20", EndTime = "11:05" });

        var cls = new SchoolClass { Name = ClassName, Grade = 9 };
        db.Classes.Add(cls);

        var subject = new Subject { Name = "Matematika" };
        db.Subjects.Add(subject);

        // Dars jadvali — o'quvchi uchun KUTILGAN kelish vaqti shu yerdan chiqadi
        // (sinfning o'sha kungi birinchi darsi), maktabning ish vaqtidan emas.
        var template = new ScheduleTemplate
        {
            ClassId = cls.Id,
            Name = "Asosiy",
            Lessons =
            [
                new ScheduleLesson { Day = 0, Period = 1, SubjectId = subject.Id },
                new ScheduleLesson { Day = 0, Period = 2, SubjectId = subject.Id },
                new ScheduleLesson { Day = 0, Period = 3, SubjectId = subject.Id },
            ],
        };
        db.ScheduleTemplates.Add(template);

        var aziz = NewStudent("Aziz Azizov", "1001");
        var bekzod = NewStudent("Bekzod Bekzodov", "1002");
        var chori = NewStudent("Chori Choriyev", "1003");
        var dilnoza = NewStudent("Dilnoza Dilnozayeva", "1004");
        var elbek = NewStudent("Elbek Elbekov", "");
        db.Students.AddRange(aziz, bekzod, chori, dilnoza, elbek);

        db.TurnstileEvents.AddRange(
            Pass("1001", "08:25"), Pass("1001", "11:10"),
            Pass("1002", "09:05"), Pass("1002", "11:10"),
            Pass("1003", "08:20"), Pass("1003", "10:00"));

        if (withJournal)
        {
            var absent = new AbsenceReason { Name = "Kasal", Short = "K", IsLate = false };
            var lateReason = new AbsenceReason { Name = "Kech keldi", Short = "KK", IsLate = true };
            db.AbsenceReasons.AddRange(absent, lateReason);

            // Aziz — belgisiz (darsda bor). Bekzod — "kech keldi" (DARSDA EDI, yo'qlik emas).
            // Chori — kasal (jurnal "yo'q" dedi, turniket esa uni ko'rdi).
            // Dilnoza — jurnalda bor, turniket ko'rmagan. Elbek — umuman belgilanmagan.
            db.JournalEntries.AddRange(
                Mark(cls.Id, subject.Id, aziz.Id, null),
                Mark(cls.Id, subject.Id, bekzod.Id, lateReason.Id),
                Mark(cls.Id, subject.Id, chori.Id, absent.Id),
                Mark(cls.Id, subject.Id, dilnoza.Id, null));
        }

        await db.SaveChangesAsync();
    }

    private static Student NewStudent(string fullName, string deviceUserId) => new()
    {
        FullName = fullName,
        LastName = fullName.Split(' ')[^1],
        FirstName = fullName.Split(' ')[0],
        BirthDate = "2012-01-01",
        Gender = "male",
        ClassName = ClassName,
        DeviceUserId = deviceUserId,
        EnrollmentDate = "2026-09-01",
    };

    private static TurnstileEvent Pass(string deviceUserId, string hhmm) => new()
    {
        DeviceUserId = deviceUserId,
        EventAt = $"{Monday}T{hhmm}:00",
        Direction = "in",
        DeviceName = "Test turniket",
        CreatedAt = $"{Monday}T{hhmm}:05",
    };

    private static JournalEntry Mark(string classId, string subjectId, string studentId, string? reasonId) => new()
    {
        ClassId = classId,
        SubjectId = subjectId,
        Quarter = 1,
        StudentId = studentId,
        Date = Monday,
        Period = 1,
        ReasonId = reasonId,
    };
}
