using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

// ===========================================================================
//  G-3 — O'QITUVCHI MAOSHIDAGI DARS SONI
//  (docs/modules/students-parity.md §2.1.3 "Salary", §2.1.6 G-3/G-16)
// ===========================================================================
//
//  NEGA BU YERDA RAQAMLAR TEST NOMIGA YOZILGAN
//  -------------------------------------------
//  Guruh darslariga o'tishda (§4.3, C1 slice) `TeacherSalaryCalc.LessonsByWeekdayAsync`
//  qayta yoziladi. U PULNI hisoblaydi, ya'ni jimgina o'zgargan bitta raqam butun
//  maktabning oylik fondini siljitadi. Shuning uchun har test o'zi kutgan dars
//  sonini NOMIDA olib yuradi: qayta yozish natijani o'zgartirsa, test nomi bilan
//  birga o'zgarishni AYTIB ketishga majbur bo'ladi.
//
//  BUGUNGI SANOQNING UCH TUZOG'I (§2.1.5 "Salary counts scheduled lessons")
//  ------------------------------------------------------------------------
//   1. Har sinf uchun FAQAT BITTA shablon sanaladi — eng ko'p darsli. Haftaga
//      HAQIQATAN biriktirilgan (`week_assignments`) shablon E'TIBORGA OLINMAYDI.
//   2. Bo'lingan dars (SubGroup 1 va 2) IKKI dars deb sanaladi — bir xil
//      (kun, dars raqami) bo'lsa ham.
//   3. Sinfi arxivlangan yoki umuman yo'q shablon tushib qoladi — guruh darslari
//      ham AYNAN shu yerdan jimgina tushib qolar edi (G-16).
//
//  ARIFMETIKA (sof funksiyalar) bazasiz tekshiriladi; DARS SONI esa HTTP orqali
//  (`GET /api/admin/salary-rates/{teacherId}`), chunki moliya ekrani aynan shu
//  javobning `weeklyLessons` maydonini ko'rsatadi.
//
//  KALENDAR TAYANCHI (2026-09): 1-sentyabr — seshanba, ya'ni dushanbalar
//  7/14/21/28 (4 ta), chorshanbalar 2/9/16/23/30 (5 ta), 13-sentyabr — yakshanba.
// ===========================================================================

/// <summary>
/// Maoshdagi dars sonining bugungi hisobi: sof arifmetika va shablonlardan
/// yig'iladigan haftalik dars soni. Batafsil — fayl boshidagi izoh.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class TeacherSalaryCalcTests(ApiFixture fixture)
{
    /// <summary>Dushanba 2 dars, chorshanba 1 dars (0=Du … 5=Sha).</summary>
    private static readonly int[] MonTwoWedOne = [2, 0, 1, 0, 0, 0];

    private const string Month = "2026-09";

    // =====================================================================
    //  1. Sof arifmetika — bazasiz
    // =====================================================================

    /// <summary>Oyiga o'rtacha hafta soni — 4. Nominal oylik shu ko'paytuvchiga tayanadi.</summary>
    [Fact]
    public void Oyiga_hafta_soni_tortta()
    {
        Assert.Equal(4, TeacherSalaryCalc.WeeksPerMonth);

        var meta = new SchoolMeta { SalaryRateOliy = 50_000m };
        // 3 dars × 4 hafta × 50 000 = 600 000
        Assert.Equal(600_000m, TeacherSalaryCalc.Monthly(3, "oliy", meta));
    }

    /// <summary>Toifa narxlari: to'rt kalit + noma'lum toifa va qatorsiz maktab — 0.</summary>
    [Fact]
    public void Toifa_narxi_faqat_tort_kalitni_biladi()
    {
        var meta = new SchoolMeta
        {
            SalaryRateOliy = 50_000m, SalaryRate1 = 40_000m,
            SalaryRate2 = 30_000m, SalaryRateMutaxasis = 20_000m,
        };

        Assert.Equal(50_000m, TeacherSalaryCalc.RateFor(meta, "oliy"));
        Assert.Equal(40_000m, TeacherSalaryCalc.RateFor(meta, "1"));
        Assert.Equal(30_000m, TeacherSalaryCalc.RateFor(meta, "2"));
        Assert.Equal(20_000m, TeacherSalaryCalc.RateFor(meta, "mutaxasis"));

        // Toifasi belgilanmagan yoki noma'lum — 0 so'm (rad etilmaydi, jim 0 bo'ladi).
        Assert.Equal(0m, TeacherSalaryCalc.RateFor(meta, ""));
        Assert.Equal(0m, TeacherSalaryCalc.RateFor(meta, "oliy_toifa"));
        Assert.Equal(0m, TeacherSalaryCalc.RateFor(null, "oliy"));
    }

    /// <summary>
    /// Oraliqdagi darslar: 2026-09-01 … 09-30 da 4 dushanba (×2) + 5 chorshanba (×1) = 13.
    /// Yakshanba hech qachon sanalmaydi.
    /// </summary>
    [Fact]
    public void Oraliqdagi_darslar_soni_13()
    {
        var total = TeacherSalaryCalc.LessonsInRange(
            MonTwoWedOne, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30));

        Assert.Equal(13, total);
    }

    /// <summary>
    /// Chorak (dars jadvali davri) oraliqni QISADI: 09-14 dan boshlansa
    /// 3 dushanba (×2) + 3 chorshanba (×1) = 9.
    /// </summary>
    [Fact]
    public void Chorak_tashqarisidagi_kunlar_sanalmaydi_9()
    {
        var quarters = new List<(string Start, string End)> { ("2026-09-14", "2026-09-30") };

        var total = TeacherSalaryCalc.LessonsInRange(
            MonTwoWedOne, new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 30), quarters);

        Assert.Equal(9, total);
    }

    /// <summary>Chorak ro'yxati BO'SH bo'lsa — cheklov yo'q (hamma kun "chorak ichida").</summary>
    [Fact]
    public void Choraklar_bosh_bolsa_cheklov_yoq()
    {
        Assert.True(TeacherSalaryCalc.InQuarter("2026-07-15", null));
        Assert.True(TeacherSalaryCalc.InQuarter("2026-07-15", new List<(string, string)>()));
        Assert.False(TeacherSalaryCalc.InQuarter(
            "2026-07-15", new List<(string, string)> { ("2026-09-01", "2026-10-31") }));
    }

    /// <summary>
    /// Oydagi reja darslar: to'liq oy — 13; oy o'rtasida ishga kirgan bo'lsa (09-15 dan) — 7;
    /// ishga kirishidan OLDINGI oy — 0.
    /// </summary>
    [Fact]
    public void Oydagi_reja_darslar_13_qisman_7_kelmasdan_oldin_0()
    {
        Assert.Equal(13, TeacherSalaryCalc.PlannedLessonsForMonth(MonTwoWedOne, Month, startDate: null));
        Assert.Equal(7, TeacherSalaryCalc.PlannedLessonsForMonth(MonTwoWedOne, Month, "2026-09-15"));
        Assert.Equal(0, TeacherSalaryCalc.PlannedLessonsForMonth(MonTwoWedOne, Month, "2026-10-01"));
    }

    /// <summary>
    /// Kelmagan kundagi darslar: dushanba — 2 dars; YAKSHANBA (13-sentyabr) — 0,
    /// chunki yakshanba jadvalda yo'q. Jami 2.
    /// </summary>
    [Fact]
    public void Kelmagan_kunlardagi_darslar_2_yakshanba_sanalmaydi()
    {
        var missed = TeacherSalaryCalc.MissedLessons(
            MonTwoWedOne, ["2026-09-07", "2026-09-13"]);

        Assert.Equal(2, missed);
    }

    /// <summary>
    /// Bitta oyning yakuniy maoshi: 13 reja − 2 kelmagan = 11 dars × 50 000 = 550 000 so'm.
    /// Chegirma darsdan olinadi, pul summasidan emas.
    /// </summary>
    [Fact]
    public void Oylik_maosh_11_dars_uchun_550_000()
    {
        var meta = new SchoolMeta { SalaryRateOliy = 50_000m };

        var salary = TeacherSalaryCalc.MonthlyForMonth(
            MonTwoWedOne, "oliy", meta, Month, startDate: null,
            absentDatesInMonth: ["2026-09-07"], bonusPct: 0m);

        Assert.Equal(550_000m, salary);
    }

    /// <summary>Ustama — foiz: 550 000 ga +50% = 825 000.</summary>
    [Fact]
    public void Ustama_foizi_qoshiladi()
    {
        Assert.Equal(825_000m, TeacherSalaryCalc.WithBonus(550_000m, 50m));
        Assert.Equal(550_000m, TeacherSalaryCalc.WithBonus(550_000m, 0m));
    }

    /// <summary>Maosh boshlanish sanasi: yangi maydon ustun, bo'lmasa eski oydan, ikkalasi ham bo'lmasa — null.</summary>
    [Fact]
    public void Maosh_boshlanish_sanasi_yangi_maydondan_oqiladi()
    {
        Assert.Equal("2026-09-15", TeacherSalaryCalc.StartDateOf(
            new Teacher { SalaryStartDate = "2026-09-15", SalaryStartMonth = "2026-01" }));
        Assert.Equal("2026-01-01", TeacherSalaryCalc.StartDateOf(
            new Teacher { SalaryStartMonth = "2026-01" }));
        Assert.Null(TeacherSalaryCalc.StartDateOf(new Teacher()));
    }

    // =====================================================================
    //  2. Haftalik dars soni — shablonlardan, HTTP orqali
    // =====================================================================

    /// <summary>Oddiy sinf haftasi: dushanba 2 + chorshanba 1 = haftasiga 3 dars.</summary>
    [Fact]
    public async Task Sinf_haftasi_haftasiga_3_dars()
    {
        var teacherId = await SeedTeacherAsync();
        await SeedClassTemplateAsync("Asosiy", teacherId,
            (Day: 0, Period: 1, SubGroup: 0),
            (Day: 0, Period: 2, SubGroup: 0),
            (Day: 2, Period: 1, SubGroup: 0));

        Assert.Equal(3, await WeeklyLessonsAsync(teacherId));
    }

    /// <summary>
    /// BO'LINGAN DARS IKKI MARTA SANALADI. Bitta (kun, dars raqami) da 1- va 2-guruh
    /// darslari turibdi va ikkalasi ham SHU o'qituvchiniki — bugungi kod ikkitasini
    /// ham qo'shadi: 1 butun sinf darsi + 2 yarim guruh = haftasiga 3 dars.
    /// </summary>
    [Fact]
    public async Task Bolingan_darsning_ikkala_yarmi_bir_oqituvchida_haftasiga_3_dars()
    {
        var teacherId = await SeedTeacherAsync();
        await SeedClassTemplateAsync("Asosiy", teacherId,
            (Day: 0, Period: 1, SubGroup: 0),
            (Day: 1, Period: 3, SubGroup: 1),
            (Day: 1, Period: 3, SubGroup: 2));

        Assert.Equal(3, await WeeklyLessonsAsync(teacherId));
    }

    /// <summary>
    /// Bo'lingan darsni ikki o'qituvchi bo'lishsa — HAR BIRIGA 1 dars (jami 2, ikki barobar emas).
    /// </summary>
    [Fact]
    public async Task Bolingan_darsni_ikki_oqituvchi_bolishsa_har_biriga_1_dars()
    {
        var first = await SeedTeacherAsync();
        var second = await SeedTeacherAsync();
        await SeedClassTemplateAsync("Asosiy", first,
            (Day: 1, Period: 3, SubGroup: 1));
        // Ikkinchi yarmi — o'sha sinf, o'sha shablon emas: alohida shablonda bo'lsa
        // "eng katta shablon" qoidasiga tushib qolardi, shuning uchun ayni shu katakka qo'shamiz.
        await AddLessonAsync("Asosiy", second, Day: 1, Period: 3, SubGroup: 2);

        Assert.Equal(1, await WeeklyLessonsAsync(first));
        Assert.Equal(1, await WeeklyLessonsAsync(second));
    }

    /// <summary>
    /// TUZOQ: sinfning FAQAT eng ko'p darsli shabloni sanaladi. Haftalarga HAQIQATAN
    /// biriktirilgani (bu yerda kichik "Imtihon" shabloni) e'tiborga OLINMAYDI —
    /// o'qituvchi 3 dars uchun pul oladi, jadvalda esa 2 dars turibdi.
    /// </summary>
    [Fact]
    public async Task Faqat_eng_katta_shablon_sanaladi_biriktirilgani_emas_3_dars()
    {
        var teacherId = await SeedTeacherAsync();
        var classId = await SeedClassTemplateAsync("Asosiy", teacherId,
            (Day: 0, Period: 1, SubGroup: 0),
            (Day: 1, Period: 1, SubGroup: 0),
            (Day: 2, Period: 1, SubGroup: 0));

        // Kichikroq shablon — VA aynan u chorakning har haftasiga biriktirilgan.
        var assignedTemplateId = await AddTemplateAsync(classId, "Imtihon", teacherId,
            (Day: 3, Period: 1, SubGroup: 0),
            (Day: 4, Period: 1, SubGroup: 0));
        await fixture.Api.WithDbAsync(async db =>
        {
            for (var week = 1; week <= 4; week++)
                db.WeekAssignments.Add(new WeekAssignment
                {
                    ClassId = classId, Quarter = 1, Week = week, TemplateId = assignedTemplateId,
                });
            await db.SaveChangesAsync();
        });

        Assert.Equal(3, await WeeklyLessonsAsync(teacherId));
    }

    /// <summary>
    /// Sinf ARXIVLANSA — uning darslari maoshdan butunlay chiqib ketadi (3 → 0).
    /// Guruh darslari ham xuddi shu filtrdan jimgina tushib qolar edi (G-16).
    /// </summary>
    [Fact]
    public async Task Arxivlangan_sinf_darslari_sanalmaydi_0_dars()
    {
        var teacherId = await SeedTeacherAsync();
        var classId = await SeedClassTemplateAsync("Asosiy", teacherId,
            (Day: 0, Period: 1, SubGroup: 0),
            (Day: 0, Period: 2, SubGroup: 0),
            (Day: 2, Period: 1, SubGroup: 0));

        Assert.Equal(3, await WeeklyLessonsAsync(teacherId));

        await fixture.Api.WithDbAsync(async db =>
        {
            var cls = await db.Classes.FindAsync(classId);
            cls!.IsArchived = true;
            await db.SaveChangesAsync();
        });

        Assert.Equal(0, await WeeklyLessonsAsync(teacherId));
    }

    /// <summary>
    /// Sinfi umuman mavjud bo'lmagan ("yetim") shablon ham sanalmaydi — o'qituvchida
    /// faqat haqiqiy sinfning 1 darsi qoladi.
    /// </summary>
    [Fact]
    public async Task Yetim_shablon_sanalmaydi_1_dars()
    {
        var teacherId = await SeedTeacherAsync();
        await SeedClassTemplateAsync("Asosiy", teacherId, (Day: 0, Period: 1, SubGroup: 0));

        await fixture.Api.WithDbAsync(async db =>
        {
            var orphan = new ScheduleTemplate { ClassId = "yoq-sinf-" + Guid.NewGuid().ToString("N")[..8], Name = "Yetim" };
            orphan.Lessons.Add(new ScheduleLesson
            {
                TemplateId = orphan.Id, Day = 1, Period = 1, SubjectId = "x", TeacherId = teacherId,
            });
            db.ScheduleTemplates.Add(orphan);
            await db.SaveChangesAsync();
        });

        Assert.Equal(1, await WeeklyLessonsAsync(teacherId));
    }

    /// <summary>
    /// Yakshanba (Day=6) va o'qituvchisi belgilanmagan katak sanalmaydi:
    /// uch katakdan faqat bittasi pulga aylanadi.
    /// </summary>
    [Fact]
    public async Task Yakshanba_va_oqituvchisiz_katak_sanalmaydi_1_dars()
    {
        var teacherId = await SeedTeacherAsync();
        var classId = await SeedClassTemplateAsync("Asosiy", teacherId, (Day: 0, Period: 1, SubGroup: 0));

        await fixture.Api.WithDbAsync(async db =>
        {
            var tpl = await db.ScheduleTemplates.Include(t => t.Lessons)
                .FirstAsync(t => t.ClassId == classId);
            tpl.Lessons.Add(new ScheduleLesson
            {
                TemplateId = tpl.Id, Day = 6, Period = 1, SubjectId = "x", TeacherId = teacherId,
            });
            tpl.Lessons.Add(new ScheduleLesson
            {
                TemplateId = tpl.Id, Day = 3, Period = 1, SubjectId = "x", TeacherId = "",
            });
            await db.SaveChangesAsync();
        });

        Assert.Equal(1, await WeeklyLessonsAsync(teacherId));
    }

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    /// <summary>Moliya ekranidagi haftalik dars soni — HTTP javobidan.</summary>
    private async Task<int> WeeklyLessonsAsync(string teacherId)
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var url = $"/api/admin/salary-rates/{teacherId}?month={Month}";
        var response = await admin.GetAsync(url);
        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"{url} → {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("weeklyLessons").GetInt32();
    }

    private async Task<string> SeedTeacherAsync()
    {
        var (user, _) = await fixture.Api.SeedUserAsync(Roles.Teacher);
        var teacherId = "";
        await fixture.Api.WithDbAsync(async db =>
        {
            var t = db.Teachers.Single(x => x.UserId == user.Id);
            t.Category = "oliy";
            await db.SaveChangesAsync();
            teacherId = t.Id;
        });
        return teacherId;
    }

    /// <summary>Yangi sinf + nomli shablon + berilgan kataklar. Sinf id'sini qaytaradi.</summary>
    private async Task<string> SeedClassTemplateAsync(
        string templateName, string teacherId, params (int Day, int Period, int SubGroup)[] cells)
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var cls = new SchoolClass { Name = $"MA-{tag}", Grade = 7 };
        await fixture.Api.WithDbAsync(async db =>
        {
            db.Classes.Add(cls);
            await db.SaveChangesAsync();
        });
        _lastClassId = cls.Id;
        await AddTemplateAsync(cls.Id, templateName, teacherId, cells);
        return cls.Id;
    }

    private string _lastClassId = "";

    private async Task<string> AddTemplateAsync(
        string classId, string name, string teacherId, params (int Day, int Period, int SubGroup)[] cells)
    {
        var tpl = new ScheduleTemplate { ClassId = classId, Name = name };
        foreach (var (day, period, subGroup) in cells)
            tpl.Lessons.Add(new ScheduleLesson
            {
                TemplateId = tpl.Id, Day = day, Period = period,
                SubjectId = "fan-" + Guid.NewGuid().ToString("N")[..8],
                TeacherId = teacherId, SubGroup = subGroup,
            });

        await fixture.Api.WithDbAsync(async db =>
        {
            db.ScheduleTemplates.Add(tpl);
            await db.SaveChangesAsync();
        });
        return tpl.Id;
    }

    /// <summary>Oxirgi yaratilgan sinfning nomli shabloniga yana bitta katak qo'shadi.</summary>
    private async Task AddLessonAsync(string templateName, string teacherId, int Day, int Period, int SubGroup)
    {
        await fixture.Api.WithDbAsync(async db =>
        {
            var tpl = await db.ScheduleTemplates.Include(t => t.Lessons)
                .SingleAsync(t => t.ClassId == _lastClassId && t.Name == templateName);
            tpl.Lessons.Add(new ScheduleLesson
            {
                TemplateId = tpl.Id, Day = Day, Period = Period,
                SubjectId = "fan-" + Guid.NewGuid().ToString("N")[..8],
                TeacherId = teacherId, SubGroup = SubGroup,
            });
            await db.SaveChangesAsync();
        });
    }
}
