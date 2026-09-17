using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

// ===========================================================================
//  G-3 — JURNALNING BUGUNGI XATTI-HARAKATI
//  (docs/modules/students-parity.md §2.1.3 "Journal & teacher access", §2.1.6 G-3)
// ===========================================================================
//
//  Jurnal — bugun HECH BIR TEST BILAN QOPLANMAGAN. Guruh darslariga o'tishda
//  (§4.3, C2 slice) `JournalService`, `JournalSettingsGuard`, `JournalController`
//  va ular bilan bir qatorda davomat foizi qayta yoziladi. Bu fayl o'sha
//  qayta yozishdan OLDINGI holatni qadab qo'yadi: oddiy sinf darsi uchun
//  natija bir bayt ham siljimasligi kerak.
//
//  BU YERDA "XATO" TUZATILMAYDI — QADAB QO'YILADI
//  ----------------------------------------------
//  G-3 ning vazifasi bugungi javobni yozib olish. Shubhali xatti-harakat
//  (masalan: belgilanmagan o'quvchi "kelgan" deb sanaladi, katakni o'chirish
//  "Dars o'tildi" belgisini qaytarmaydi) TUZATILMAYDI — nomida "bugungi
//  xatti-harakat" deb belgilangan test bilan qadab qo'yiladi.
//
//  QAMROV
//  ------
//   1. Katak yozish/o'qish/o'chirish (`PUT|GET|DELETE /api/admin/journal`)
//   2. Davomat: kechikkan DARSDA, kelmagan — yo'q (davomat foizi orqali)
//   3. Belgilanmagan jurnal: dars "o'tilgan" bo'lmaydi, maxrajga kirmaydi
//   4. "Dars o'tildi" (`Conducted`) — nima uni qo'yadi va nima qo'ymaydi
//   5. Jurnal ustunlari — jadval + haftaga biriktirish + bayram
//
//  Bo'lingan darslar (SubGroup 0/1/2) — alohida faylda: `JournalSubGroupTests`.
// ===========================================================================

/// <summary>
/// Admin jurnalining bugungi xatti-harakati, HTTP darajasida. Batafsil — fayl boshidagi izoh.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class JournalServiceTests(ApiFixture fixture)
{
    private const string Journal = "/api/admin/journal";
    private const string Notes = "/api/admin/journal/notes";

    /// <summary>Dars sanasi — o'tmishda (o'qituvchi yo'li kelajak sanani rad etadi).</summary>
    private const string Date = "2026-09-16";

    private const int Quarter = 1;

    // =====================================================================
    //  1. Katak: yozish, o'qish, ustiga yozish, o'chirish
    // =====================================================================

    /// <summary>
    /// Yozilgan katak AYNAN o'sha ko'rinishda qaytib o'qiladi: baho, uyga vazifa
    /// belgisi, xulq va o'zlashtirish foizi.
    /// </summary>
    [Fact]
    public async Task Katak_yozilgan_holida_qaytib_oqiladi()
    {
        var w = await SeedAsync(students: 1);
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        Assert.Equal(HttpStatusCode.NoContent,
            (await PutEntry(admin, w, w.StudentIds[0], period: 1, grade: 5, homework: 1, behavior: 2, mastery: 80)).StatusCode);

        var row = (await EntriesAsync(admin, w)).Single();
        Assert.Equal(w.StudentIds[0], row.GetProperty("studentId").GetString());
        Assert.Equal(Date, row.GetProperty("date").GetString());
        Assert.Equal(1, row.GetProperty("period").GetInt32());
        Assert.Equal(5, row.GetProperty("grade").GetInt32());
        Assert.Equal(1, row.GetProperty("homework").GetInt32());
        Assert.Equal(2, row.GetProperty("behavior").GetInt32());
        Assert.Equal(80, row.GetProperty("mastery").GetInt32());
        Assert.Equal(JsonValueKind.Null, row.GetProperty("reasonId").ValueKind);
    }

    /// <summary>
    /// Ikkinchi yozuv YANGI qator yaratmaydi — katak USTIGA yoziladi. Kalit:
    /// (sinf, fan, chorak, o'quvchi, sana, dars raqami); <b>guruh kalitda YO'Q</b>.
    /// </summary>
    [Fact]
    public async Task Katak_ustiga_yoziladi_ikkinchi_qator_paydo_bolmaydi()
    {
        var w = await SeedAsync(students: 1);
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var s = w.StudentIds[0];

        await PutEntry(admin, w, s, period: 1, grade: 5);
        await PutEntry(admin, w, s, period: 1, grade: 3);

        var row = Assert.Single(await EntriesAsync(admin, w));
        Assert.Equal(3, row.GetProperty("grade").GetInt32());

        await fixture.Api.WithDbAsync(async db =>
            Assert.Single(await db.JournalEntries.AsNoTracking()
                .Where(e => e.ClassId == w.ClassId && e.StudentId == s).ToListAsync()));
    }

    /// <summary>Baho o'rniga davomat sababi yozilsa — baho bo'shaydi, sabab qoladi.</summary>
    [Fact]
    public async Task Baho_ornini_davomat_sababi_egallaydi()
    {
        var w = await SeedAsync(students: 1);
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var s = w.StudentIds[0];

        await PutEntry(admin, w, s, period: 1, grade: 5);
        await PutEntry(admin, w, s, period: 1, reasonId: w.AbsentReasonId);

        var row = Assert.Single(await EntriesAsync(admin, w));
        Assert.Equal(JsonValueKind.Null, row.GetProperty("grade").ValueKind);
        Assert.Equal(w.AbsentReasonId, row.GetProperty("reasonId").GetString());
    }

    /// <summary>O'chirish — katak butunlay yo'q bo'ladi (bo'shatilmaydi, O'CHIRILADI).</summary>
    [Fact]
    public async Task Katakni_ochirish_yozuvni_yoq_qiladi()
    {
        var w = await SeedAsync(students: 1);
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var s = w.StudentIds[0];

        await PutEntry(admin, w, s, period: 1, grade: 5);

        var url = $"{Journal}?classId={w.ClassId}&subjectId={w.SubjectId}&quarter={Quarter}"
                  + $"&studentId={s}&date={Date}&period=1";
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync(url)).StatusCode);

        Assert.Empty(await EntriesAsync(admin, w));
    }

    /// <summary>
    /// BUGUNGI XATTI-HARAKAT: jurnal katagida BAZA DARAJASIDA yagonalik cheklovi YO'Q
    /// (`QuarterGrade` da bor, `JournalEntry` da yo'q — §2.1.5). Bir xil kalitli ikki
    /// qator yozib qo'yilsa, ikkalasi ham jurnalga qaytadi.
    /// </summary>
    [Fact]
    public async Task Jurnal_katagida_yagonalik_cheklovi_yoq_bugungi_xatti_harakat()
    {
        var w = await SeedAsync(students: 1);
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var s = w.StudentIds[0];

        await fixture.Api.WithDbAsync(async db =>
        {
            foreach (var grade in new[] { 4, 5 })
                db.JournalEntries.Add(new JournalEntry
                {
                    ClassId = w.ClassId, SubjectId = w.SubjectId, Quarter = Quarter,
                    StudentId = s, Date = Date, Period = 1, Grade = grade,
                });
            await db.SaveChangesAsync();
        });

        Assert.Equal(2, (await EntriesAsync(admin, w)).Count);
    }

    // =====================================================================
    //  2. Davomat: kechikkan DARSDA, kelmagan — yo'q
    // =====================================================================

    /// <summary>
    /// KECHIKKAN O'QUVCHI DARSDA QATNASHGAN: davomati 100%. Kelmagan o'quvchiniki —
    /// 0%. Ikkalasi ham bir xil "sabab" ustunida yozilgan, farqi faqat
    /// <c>AbsenceReason.IsLate</c> da.
    /// </summary>
    [Fact]
    public async Task Kechikkan_oquvchi_darsda_hisoblanadi_kelmagan_yoq()
    {
        var w = await SeedAsync(students: 2);
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var (late, absent) = (w.StudentIds[0], w.StudentIds[1]);

        await MarkConductedAsync(w, period: 1, subGroup: 0);
        await PutEntry(admin, w, late, period: 1, reasonId: w.LateReasonId);
        await PutEntry(admin, w, absent, period: 1, reasonId: w.AbsentReasonId);

        var attendance = await AttendanceAsync(admin, w);
        Assert.Equal(100d, attendance[late]);
        Assert.Equal(0d, attendance[absent]);
    }

    /// <summary>
    /// BUGUNGI XATTI-HARAKAT: o'tilgan darsda umuman belgilanmagan o'quvchi
    /// "kelgan" deb sanaladi (davomati 100%). Ya'ni bo'sh katak bilan
    /// "keldi" katagi bugun bir xil ko'rinadi.
    /// </summary>
    [Fact]
    public async Task Belgilanmagan_oquvchi_otilgan_darsda_kelgan_deb_sanaladi_bugungi_xatti_harakat()
    {
        var w = await SeedAsync(students: 2);
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var (marked, untouched) = (w.StudentIds[0], w.StudentIds[1]);

        await MarkConductedAsync(w, period: 1, subGroup: 0);
        await PutEntry(admin, w, marked, period: 1, reasonId: w.AbsentReasonId);

        var attendance = await AttendanceAsync(admin, w);
        Assert.Equal(0d, attendance[marked]);
        Assert.Equal(100d, attendance[untouched]);
    }

    /// <summary>
    /// BELGILANMAGAN JURNAL BELGILANMAGANICHA QOLADI: dars "o'tildi" bo'lmasa, u
    /// maxrajga umuman kirmaydi va davomat <c>null</c> bo'ladi — 100% EMAS.
    /// Ya'ni hech kim jimgina "kelgan" deb yozilmaydi.
    /// </summary>
    [Fact]
    public async Task Belgilanmagan_dars_maxrajga_kirmaydi_davomat_null()
    {
        var w = await SeedAsync(students: 1);
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var attendance = await AttendanceRawAsync(admin, w);
        Assert.Equal(JsonValueKind.Null, attendance[w.StudentIds[0]].ValueKind);

        // "O'tilgan darslar" ro'yxatida ham yo'q.
        Assert.Empty(await ConductedAsync(admin, w));
    }

    // =====================================================================
    //  3. "Dars o'tildi" (Conducted) — nima qo'yadi, nima qo'ymaydi
    // =====================================================================

    /// <summary>
    /// Birinchi baho darsni AVTOMATIK "o'tildi" qiladi: izoh qatori o'zi tug'iladi
    /// va <c>conducted = true</c> bo'ladi (mavzu bo'sh bo'lsa ham).
    /// </summary>
    [Fact]
    public async Task Birinchi_baho_darsni_otildi_deb_belgilaydi()
    {
        var w = await SeedAsync(students: 1);
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        await PutEntry(admin, w, w.StudentIds[0], period: 1, grade: 5);

        var note = Assert.Single(await NotesAsync(admin, w));
        Assert.True(note.GetProperty("conducted").GetBoolean());
        Assert.Equal(1, note.GetProperty("period").GetInt32());
        Assert.Equal(0, note.GetProperty("subGroup").GetInt32());
        Assert.Equal("", note.GetProperty("topic").GetString());
    }

    /// <summary>
    /// Davomat sababi ham, uyga vazifa/xulq/o'zlashtirish belgisi ham darsni
    /// "o'tildi" qiladi — baho shart emas.
    /// </summary>
    [Theory]
    [InlineData("reason")]
    [InlineData("homework")]
    [InlineData("behavior")]
    [InlineData("mastery")]
    public async Task Baho_bolmasa_ham_belgilangan_katak_darsni_yopadi(string kind)
    {
        var w = await SeedAsync(students: 1);
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        await PutEntry(admin, w, w.StudentIds[0], period: 1,
            reasonId: kind == "reason" ? w.AbsentReasonId : null,
            homework: kind == "homework" ? 1 : 0,
            behavior: kind == "behavior" ? 1 : 0,
            mastery: kind == "mastery" ? 50 : null);

        Assert.True((await NotesAsync(admin, w)).Single().GetProperty("conducted").GetBoolean());
    }

    /// <summary>
    /// BO'M-BO'SH katak (baho yo'q, sabab yo'q, belgilar 0) darsni "o'tildi"
    /// QILMAYDI — lekin jurnal qatorining O'ZI baribir yoziladi.
    /// </summary>
    [Fact]
    public async Task Bosh_katak_darsni_otildi_qilmaydi_lekin_qator_yoziladi()
    {
        var w = await SeedAsync(students: 1);
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        await PutEntry(admin, w, w.StudentIds[0], period: 1);

        Assert.Single(await EntriesAsync(admin, w));
        Assert.Empty(await NotesAsync(admin, w));
    }

    /// <summary>
    /// BUGUNGI XATTI-HARAKAT: katakni O'CHIRISH "Dars o'tildi" belgisini
    /// QAYTARMAYDI — avtomatik tug'ilgan izoh qatori joyida qoladi va dars
    /// hisobotlarda o'tilgan bo'lib ko'rinaveradi.
    /// </summary>
    [Fact]
    public async Task Katakni_ochirish_dars_otildi_belgisini_qaytarmaydi_bugungi_xatti_harakat()
    {
        var w = await SeedAsync(students: 1);
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var s = w.StudentIds[0];

        await PutEntry(admin, w, s, period: 1, grade: 5);
        Assert.True((await NotesAsync(admin, w)).Single().GetProperty("conducted").GetBoolean());

        await admin.DeleteAsync($"{Journal}?classId={w.ClassId}&subjectId={w.SubjectId}"
                                + $"&quarter={Quarter}&studentId={s}&date={Date}&period=1");

        Assert.Empty(await EntriesAsync(admin, w));
        Assert.True((await NotesAsync(admin, w)).Single().GetProperty("conducted").GetBoolean());
    }

    /// <summary>
    /// Mavzu, uyga vazifa va "o'tildi" — uchchovi ham bo'sh bo'lsa izoh qatori
    /// O'CHIRILADI (bo'sh qator saqlanmaydi).
    /// </summary>
    [Fact]
    public async Task Butunlay_bosh_izoh_yozuvni_ochiradi()
    {
        var w = await SeedAsync(students: 1);
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        Assert.Equal(HttpStatusCode.NoContent,
            (await PutNote(admin, w, period: 1, topic: "Kasrlar", homework: "12-mashq", conducted: true)).StatusCode);
        var saved = Assert.Single(await NotesAsync(admin, w));
        Assert.Equal("Kasrlar", saved.GetProperty("topic").GetString());
        Assert.Equal("12-mashq", saved.GetProperty("homework").GetString());

        Assert.Equal(HttpStatusCode.NoContent,
            (await PutNote(admin, w, period: 1, topic: "", homework: null, conducted: false)).StatusCode);
        Assert.Empty(await NotesAsync(admin, w));
    }

    /// <summary>
    /// Mavzu bo'lsa-yu "o'tildi" belgilanmagan bo'lsa — dars o'tilmagan bo'lib qoladi.
    /// Mavzu yozish darsni yopmaydi.
    /// </summary>
    [Fact]
    public async Task Mavzu_yozish_ozi_darsni_yopmaydi()
    {
        var w = await SeedAsync(students: 1);
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        await PutNote(admin, w, period: 1, topic: "Kasrlar", homework: null, conducted: false);

        Assert.False((await NotesAsync(admin, w)).Single().GetProperty("conducted").GetBoolean());
        Assert.Empty(await ConductedAsync(admin, w));
    }

    /// <summary>
    /// Mavzular IMPORTI darsni "o'tilgan" QILMAYDI: yangi qatorda <c>conducted = false</c>,
    /// mavjud qatorda esa belgi O'ZGARMAYDI. (HTTP qatlami faqat .xlsx ni o'qiydi,
    /// qoidaning o'zi shu yerda — shuning uchun xizmat darajasida qadab qo'yilgan.)
    /// </summary>
    [Fact]
    public async Task Mavzular_importi_darsni_otilgan_qilmaydi()
    {
        var w = await SeedAsync(students: 1, withTemplate: true);
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        await WithQuarterAsync(async () =>
        {
            // Jadvaldagi 1-dars uchun mavzu — import yo'li bilan.
            await fixture.Api.WithDbAsync(async db =>
            {
                var result = await JournalService.ImportTopicsAsync(db, w.ClassId, w.SubjectId, Quarter,
                [
                    JournalService.TopicHeaders,
                    ["1", "Import mavzusi", "13-mashq"],
                ]);
                Assert.Equal(1, result.Imported);
                Assert.Equal(0, result.Errors);
            });

            var note = Assert.Single(await NotesAsync(admin, w));
            Assert.Equal("Import mavzusi", note.GetProperty("topic").GetString());
            Assert.False(note.GetProperty("conducted").GetBoolean());
        });
    }

    /// <summary>
    /// "O'tilgan darslar" ro'yxati (<c>GET /journal/conducted</c>) ikki manbadan yig'iladi:
    /// ptichka qo'yilgan izohlar VA baho/sabab yozilgan kataklar.
    /// </summary>
    [Fact]
    public async Task Otilgan_darslar_royxati_izoh_va_katakdan_yigiladi()
    {
        var w = await SeedAsync(students: 1);
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        // 1-dars: faqat ptichka. 2-dars: faqat baho (izohni o'zi tug'diradi).
        await PutNote(admin, w, period: 1, topic: "", homework: null, conducted: true);
        await PutEntry(admin, w, w.StudentIds[0], period: 2, grade: 4);

        var periods = (await ConductedAsync(admin, w))
            .Select(x => x.GetProperty("period").GetInt32()).OrderBy(p => p).ToList();
        Assert.Equal([1, 2], periods);
    }

    // =====================================================================
    //  4. Jurnal ustunlari — jadval + haftaga biriktirish + bayram
    // =====================================================================

    /// <summary>
    /// Ustunlar jadvaldan hisoblanadi: haftaga biriktirilgan shablonning shu fandagi
    /// har bir katagi uchun (sana, dars raqami). Biriktirilmagan hafta ustun BERMAYDI.
    /// </summary>
    [Fact]
    public async Task Ustunlar_faqat_biriktirilgan_haftalardan_chiqadi()
    {
        var w = await SeedAsync(students: 1, withTemplate: true);
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        await WithQuarterAsync(async () =>
        {
            // 1-hafta biriktirilgan (seed), 2-hafta — yo'q.
            var cols = await ColumnsAsync(admin, w);
            var dates = cols.Select(c => c.GetProperty("date").GetString()).ToList();

            // Shablonda: seshanba (Day=1) 1-dars va payshanba (Day=3) 2-dars.
            Assert.Equal(new[] { "2026-09-01", "2026-09-03" }, dates);
            Assert.Equal(new[] { 1, 2 }, cols.Select(c => c.GetProperty("period").GetInt32()).ToList());
        });
    }

    /// <summary>
    /// Chorakdan tashqaridagi dars kuni (1-haftaning dushanbasi — 31-avgust) va
    /// BAYRAM kuni ustun bermaydi.
    /// </summary>
    [Fact]
    public async Task Chorakdan_tashqari_va_bayram_kuni_ustun_bermaydi()
    {
        var w = await SeedAsync(students: 1, withTemplate: true);
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        await fixture.Api.WithDbAsync(async db =>
        {
            // 1-haftaning DUSHANBASI — 2026-08-31, chorak esa 09-01 da boshlanadi.
            var tpl = await db.ScheduleTemplates.Include(t => t.Lessons)
                .FirstAsync(t => t.ClassId == w.ClassId);
            tpl.Lessons.Add(new ScheduleLesson
            {
                TemplateId = tpl.Id, Day = 0, Period = 1, SubjectId = w.SubjectId, TeacherId = w.TeacherId,
            });
            await db.SaveChangesAsync();
        });

        await WithQuarterAsync(async () =>
        {
            var before = (await ColumnsAsync(admin, w))
                .Select(c => c.GetProperty("date").GetString()).ToList();
            Assert.DoesNotContain("2026-08-31", before);
            Assert.Contains("2026-09-03", before);

            // Payshanbani bayram qilamiz — ustun yo'qoladi.
            var holidayName = "Test bayrami " + w.Tag;
            await fixture.Api.WithDbAsync(async db =>
            {
                db.Holidays.Add(new Holiday { Date = "2026-09-03", Name = holidayName });
                await db.SaveChangesAsync();
            });
            try
            {
                var after = (await ColumnsAsync(admin, w))
                    .Select(c => c.GetProperty("date").GetString()).ToList();
                Assert.DoesNotContain("2026-09-03", after);
                Assert.Contains("2026-09-01", after);
            }
            finally
            {
                // Bayramlar jadvali UMUMIY — faqat o'zimiznikini olib tashlaymiz.
                await fixture.Api.WithDbAsync(async db =>
                    await db.Holidays.Where(h => h.Name == holidayName).ExecuteDeleteAsync());
            }
        });
    }

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    internal sealed record JournalWorld(
        string Tag, string ClassId, string ClassName, string SubjectId, string TeacherId,
        List<string> StudentIds, string AbsentReasonId, string LateReasonId);

    /// <summary>Chorak 1 = 2026-09-01 … 2026-10-31; 1-hafta 09-01 … 09-05.</summary>
    private const string QuarterStart = "2026-09-01";
    private const string QuarterEnd = "2026-10-31";

    /// <summary>
    /// Sinf, fan, o'qituvchi, o'quvchilar va ikkita davomat sababi (kelmadi/kechikdi).
    /// <paramref name="withTemplate"/> — jadval shabloni (seshanba 1-dars, payshanba 2-dars)
    /// va 1-haftaga biriktirish.
    /// </summary>
    private async Task<JournalWorld> SeedAsync(int students, bool withTemplate = false)
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var cls = new SchoolClass { Name = $"JR-{tag}", Grade = 5 };
        var subject = new Subject { Name = $"Matematika {tag}" };
        var teacher = new Teacher { FullName = $"O'qituvchi {tag}" };
        var absent = new AbsenceReason { Name = $"Kelmadi {tag}", Short = "K", IsLate = false };
        var late = new AbsenceReason { Name = $"Kechikdi {tag}", Short = "Kch", IsLate = true };
        var ids = new List<string>();

        await fixture.Api.WithDbAsync(async db =>
        {
            db.Classes.Add(cls);
            db.Subjects.Add(subject);
            db.Teachers.Add(teacher);
            db.AbsenceReasons.AddRange(absent, late);
            for (var i = 0; i < students; i++)
            {
                var s = GeneralSettingsFlagsTests.NewStudent($"Jurnal {i} {tag}", cls.Name, "+998900000001");
                db.Students.Add(s);
                ids.Add(s.Id);
            }

            if (withTemplate)
            {
                var tpl = new ScheduleTemplate { ClassId = cls.Id, Name = "Asosiy " + tag };
                tpl.Lessons.Add(new ScheduleLesson
                {
                    TemplateId = tpl.Id, Day = 1, Period = 1, SubjectId = subject.Id, TeacherId = teacher.Id,
                });
                tpl.Lessons.Add(new ScheduleLesson
                {
                    TemplateId = tpl.Id, Day = 3, Period = 2, SubjectId = subject.Id, TeacherId = teacher.Id,
                });
                db.ScheduleTemplates.Add(tpl);
                db.WeekAssignments.Add(new WeekAssignment
                {
                    ClassId = cls.Id, Quarter = Quarter, Week = 1, TemplateId = tpl.Id,
                });
            }

            await db.SaveChangesAsync();
        });

        return new JournalWorld(tag, cls.Id, cls.Name, subject.Id, teacher.Id, ids, absent.Id, late.Id);
    }

    /// <summary>
    /// Chorak qatori UMUMIY (butun maktabga bitta) — shuning uchun test davrigagina
    /// qo'yiladi va keyin ASL holati qaytariladi, test yiqilsa ham.
    /// </summary>
    private Task WithQuarterAsync(Func<Task> body) => WithQuarterAsync(fixture.Api, body);

    internal static async Task WithQuarterAsync(ApiFactory api, Func<Task> body)
    {
        List<QuarterPeriod> saved = [];
        await api.WithDbAsync(async db =>
        {
            saved = await db.Quarters.AsNoTracking().ToListAsync();
            await db.Quarters.ExecuteDeleteAsync();
            db.Quarters.Add(new QuarterPeriod
            {
                Quarter = Quarter, StartDate = QuarterStart, EndDate = QuarterEnd, GradesOpen = true,
            });
            await db.SaveChangesAsync();
        });

        try
        {
            await body();
        }
        finally
        {
            await api.WithDbAsync(async db =>
            {
                await db.Quarters.ExecuteDeleteAsync();
                db.Quarters.AddRange(saved);
                await db.SaveChangesAsync();
            });
        }
    }

    /// <summary>Darsni "o'tildi" deb belgilaydi (ptichka), jurnal katagisiz.</summary>
    private async Task MarkConductedAsync(JournalWorld w, int period, int subGroup) =>
        await fixture.Api.WithDbAsync(async db =>
        {
            db.LessonNotes.Add(new LessonNote
            {
                ClassId = w.ClassId, SubjectId = w.SubjectId, Quarter = Quarter,
                Date = Date, Period = period, SubGroup = subGroup, Conducted = true,
            });
            await db.SaveChangesAsync();
        });

    private static Task<HttpResponseMessage> PutEntry(
        HttpClient client, JournalWorld w, string studentId, int period,
        int? grade = null, string? reasonId = null, int homework = 0, int behavior = 0, int? mastery = null) =>
        client.PutAsJsonAsync(Journal, new
        {
            classId = w.ClassId, subjectId = w.SubjectId, quarter = Quarter, studentId,
            date = Date, period, grade, reasonId, homework, behavior, mastery,
        });

    private static Task<HttpResponseMessage> PutNote(
        HttpClient client, JournalWorld w, int period, string topic, string? homework, bool conducted,
        int subGroup = 0) =>
        client.PutAsJsonAsync(Notes, new
        {
            classId = w.ClassId, subjectId = w.SubjectId, quarter = Quarter,
            date = Date, period, topic, homework, conducted, subGroup,
        });

    private static Task<List<JsonElement>> EntriesAsync(HttpClient client, JournalWorld w) =>
        ArrayAsync(client, $"{Journal}?classId={w.ClassId}&subjectId={w.SubjectId}&quarter={Quarter}");

    private static Task<List<JsonElement>> NotesAsync(HttpClient client, JournalWorld w) =>
        ArrayAsync(client, $"{Notes}?classId={w.ClassId}&subjectId={w.SubjectId}&quarter={Quarter}");

    private static Task<List<JsonElement>> ColumnsAsync(HttpClient client, JournalWorld w) =>
        ArrayAsync(client, $"{Journal}/columns?classId={w.ClassId}&subjectId={w.SubjectId}&quarter={Quarter}");

    /// <summary>Shu sinf+fanga tegishli "o'tilgan darslar" (butun maktabniki filtrlanadi).</summary>
    private static async Task<List<JsonElement>> ConductedAsync(HttpClient client, JournalWorld w) =>
        (await ArrayAsync(client, $"{Journal}/conducted?date={Date}"))
        .Where(x => x.GetProperty("classId").GetString() == w.ClassId
                    && x.GetProperty("subjectId").GetString() == w.SubjectId)
        .ToList();

    /// <summary>O'quvchi → davomat foizi (null bo'lganlari tushib qoladi).</summary>
    private static async Task<Dictionary<string, double>> AttendanceAsync(HttpClient client, JournalWorld w)
    {
        var raw = await AttendanceRawAsync(client, w);
        return raw.Where(kv => kv.Value.ValueKind != JsonValueKind.Null)
            .ToDictionary(kv => kv.Key, kv => kv.Value.GetDouble());
    }

    private static async Task<Dictionary<string, JsonElement>> AttendanceRawAsync(HttpClient client, JournalWorld w)
    {
        var url = $"/api/admin/classes/{w.ClassId}/performance";
        var response = await client.GetAsync(url);
        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"{url} → {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("rows").EnumerateArray()
            .ToDictionary(
                r => r.GetProperty("student").GetProperty("id").GetString()!,
                r => r.GetProperty("attendance").Clone());
    }

    private static async Task<List<JsonElement>> ArrayAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"{url} → {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.EnumerateArray().Select(e => e.Clone()).ToList();
    }
}
