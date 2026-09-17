using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

// ===========================================================================
//  G-3 — BO'LINGAN DARS (SubGroup 0/1/2) JURNALDA
//  (docs/modules/students-parity.md §2.1.2, §2.1.3, §2.1.6 G-3)
// ===========================================================================
//
//  Bugun sinf ichidagi bo'linish IKKI joyda yashaydi:
//    · `ScheduleLesson.SubGroup` — darsning O'ZI kimga tegishli (0 = butun sinf);
//    · `Student.SubGroup` — o'quvchi qaysi yarmida.
//  Ular hech qayerda BOG'LANMAGAN: jurnal katagining guruhi so'rovdan EMAS,
//  O'QUVCHIdan ko'chiriladi (`JournalService.SetEntryAsync`), ro'yxat esa
//  `JournalSettingsGuard` da har safar qaytadan hisoblanadi.
//
//  §2.1.4 bo'yicha o'quv guruhi (`study_groups`) BOSHQA narsa: u sinflar
//  ustidan turadi va "SubGroup 0–9" varianti ATAYLAB rad etilgan. Ya'ni
//  quyidagi xatti-harakat cut-over'dan keyin ham AYNAN shunday qolishi kerak —
//  shuning uchun qadab qo'yiladi.
//
//  DIQQAT: bu fayl JURNAL tomonini qadaydi. O'quvchi/ota-ona/Telegram
//  ekranlaridagi bo'lingan dars xatti-harakati — `SplitLessonNoteTests` (G-2).
// ===========================================================================

/// <summary>
/// Bo'lingan darsning bugungi jurnal xatti-harakati: ustunlar, izohlar, katakning
/// guruhi, darsni yopish ro'yxati va davomat maxraji.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class JournalSubGroupTests(ApiFixture fixture)
{
    private const string Journal = "/api/admin/journal";
    private const string Notes = "/api/admin/journal/notes";

    /// <summary>Bo'lingan dars — seshanba, 1-dars (chorakning 1-haftasida 2026-09-01).</summary>
    private const string Date = "2026-09-01";

    private const int SplitPeriod = 1;
    private const int WholePeriod = 2;
    private const int Quarter = 1;

    // =====================================================================
    //  1. Jadval → jurnal ustunlari
    // =====================================================================

    /// <summary>
    /// Bo'lingan dars jurnalda IKKI USTUN beradi: bir xil sana va dars raqami,
    /// farqi faqat guruhda. Butun sinf darsi esa bitta ustun (guruh = 0).
    /// </summary>
    [Fact]
    public async Task Bolingan_dars_jurnalda_ikki_ustun_beradi()
    {
        var w = await SeedAsync(withTemplate: true);
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        await JournalServiceTests.WithQuarterAsync(fixture.Api, async () =>
        {
            var cols = await ColumnsAsync(admin, w);

            Assert.Equal(3, cols.Count);
            // Tartib: sana → dars raqami → guruh.
            Assert.Equal(
                new[] { (Date, SplitPeriod, 1), (Date, SplitPeriod, 2), (Date, WholePeriod, 0) },
                cols.Select(c => (
                    c.GetProperty("date").GetString()!,
                    c.GetProperty("period").GetInt32(),
                    c.GetProperty("subGroup").GetInt32())).ToArray());
        });
    }

    // =====================================================================
    //  2. Izoh (mavzu) — har guruhga o'ziniki
    // =====================================================================

    /// <summary>
    /// Bitta (sana, dars raqami) da har guruhning O'Z mavzusi bo'ladi; birini
    /// o'zgartirish ikkinchisiga tegmaydi.
    /// </summary>
    [Fact]
    public async Task Har_guruhning_mavzusi_alohida_saqlanadi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        await PutNote(admin, w, SplitPeriod, subGroup: 1, topic: "Ingliz — 1-guruh");
        await PutNote(admin, w, SplitPeriod, subGroup: 2, topic: "Ingliz — 2-guruh");

        var bySubGroup = await NotesBySubGroupAsync(admin, w, SplitPeriod);
        Assert.Equal("Ingliz — 1-guruh", bySubGroup[1].GetProperty("topic").GetString());
        Assert.Equal("Ingliz — 2-guruh", bySubGroup[2].GetProperty("topic").GetString());

        // 2-guruhni qayta yozamiz — 1-guruhniki qimirlamaydi.
        await PutNote(admin, w, SplitPeriod, subGroup: 2, topic: "Ingliz — 2-guruh (yangi)");

        bySubGroup = await NotesBySubGroupAsync(admin, w, SplitPeriod);
        Assert.Equal("Ingliz — 1-guruh", bySubGroup[1].GetProperty("topic").GetString());
        Assert.Equal("Ingliz — 2-guruh (yangi)", bySubGroup[2].GetProperty("topic").GetString());
    }

    // =====================================================================
    //  3. Katakning guruhi — so'rovdan EMAS, o'quvchidan
    // =====================================================================

    /// <summary>
    /// Jurnal katagini yozish so'rovida (<c>SetJournalEntryRequest</c>) guruh maydoni
    /// UMUMAN YO'Q: u <c>Student.SubGroup</c> dan ko'chiriladi — ham katakka, ham
    /// avtomatik tug'iladigan izohga.
    /// </summary>
    [Fact]
    public async Task Katakning_guruhi_sorovdan_emas_oquvchidan_olinadi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        await PutEntry(admin, w, w.Group1StudentId, SplitPeriod, grade: 5);
        await PutEntry(admin, w, w.Group2StudentId, SplitPeriod, grade: 4);

        await fixture.Api.WithDbAsync(async db =>
        {
            var byStudent = (await db.JournalEntries.AsNoTracking()
                    .Where(e => e.ClassId == w.ClassId && e.Period == SplitPeriod).ToListAsync())
                .ToDictionary(e => e.StudentId, e => e.SubGroup);

            Assert.Equal(1, byStudent[w.Group1StudentId]);
            Assert.Equal(2, byStudent[w.Group2StudentId]);
        });

        // Avtomatik "Dars o'tildi" ham HAR GURUHGA alohida tug'ildi.
        var bySubGroup = await NotesBySubGroupAsync(admin, w, SplitPeriod);
        Assert.Equal(new[] { 1, 2 }, bySubGroup.Keys.OrderBy(k => k).ToArray());
        Assert.All(bySubGroup.Values, n => Assert.True(n.GetProperty("conducted").GetBoolean()));
    }

    /// <summary>
    /// BUGUNGI XATTI-HARAKAT: guruhsiz (<c>SubGroup = 0</c>) o'quvchiga bo'lingan darsda
    /// baho qo'yilsa, BUTUN SINF izohi (guruh = 0) tug'iladi — ya'ni bitta (sana, dars)
    /// da uchta "o'tilgan dars" qatori paydo bo'ladi: 0, 1 va 2.
    /// </summary>
    [Fact]
    public async Task Guruhsiz_oquvchi_bolingan_darsda_butun_sinf_izohini_tugdiradi_bugungi_xatti_harakat()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        await PutEntry(admin, w, w.Group1StudentId, SplitPeriod, grade: 5);
        await PutEntry(admin, w, w.Group2StudentId, SplitPeriod, grade: 4);
        await PutEntry(admin, w, w.NoGroupStudentId, SplitPeriod, grade: 3);

        var bySubGroup = await NotesBySubGroupAsync(admin, w, SplitPeriod);
        Assert.Equal(new[] { 0, 1, 2 }, bySubGroup.Keys.OrderBy(k => k).ToArray());
    }

    /// <summary>"O'tilgan darslar" ro'yxati guruhni ham qaytaradi (mijoz ustunni shu bilan topadi).</summary>
    [Fact]
    public async Task Otilgan_darslar_royxati_guruhni_ham_qaytaradi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        await PutNote(admin, w, SplitPeriod, subGroup: 1, topic: "1-guruh", conducted: true);
        await PutNote(admin, w, SplitPeriod, subGroup: 2, topic: "2-guruh", conducted: true);

        var rows = (await ArrayAsync(admin, $"{Journal}/conducted?date={Date}"))
            .Where(x => x.GetProperty("classId").GetString() == w.ClassId)
            .Select(x => x.GetProperty("subGroup").GetInt32())
            .OrderBy(x => x).ToArray();

        Assert.Equal(new[] { 1, 2 }, rows);
    }

    // =====================================================================
    //  4. Darsni yopish ro'yxati — faqat o'z yarmi
    // =====================================================================

    /// <summary>
    /// "Dars o'tildi" ni ATAYLAB qo'yishda (baho majburiy bayrog'i yoqiq) ro'yxat
    /// guruh bo'yicha qisiladi: 1-guruh darsini yopish uchun FAQAT 1-guruh
    /// o'quvchilari baholangan bo'lishi kifoya. Butun sinf darsi (guruh = 0) esa
    /// HAMMA o'quvchini talab qiladi.
    /// </summary>
    [Fact]
    public async Task Bolingan_darsni_yopishda_faqat_oz_guruhi_baholanadi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        await SchoolMetaFlags.WithAsync(fixture.Api, m => m.IsStudentGradeRequired = true, async () =>
        {
            // Faqat 1-guruh o'quvchisiga baho.
            Assert.Equal(HttpStatusCode.NoContent,
                (await PutEntry(admin, w, w.Group1StudentId, SplitPeriod, grade: 5)).StatusCode);

            // 1-guruh darsi yopiladi.
            Assert.Equal(HttpStatusCode.NoContent,
                (await PutNote(admin, w, SplitPeriod, subGroup: 1, topic: "1-guruh", conducted: true)).StatusCode);

            // 2-guruh darsi — yo'q: o'sha yarmida baho yo'q.
            var second = await PutNote(admin, w, SplitPeriod, subGroup: 2, topic: "2-guruh", conducted: true);
            Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
            Assert.Equal(JournalSettingsGuard.GradesRequiredMessage, await MessageAsync(second));

            // Butun sinf darsi — ham yo'q: 2-guruh va guruhsiz o'quvchi baholanmagan.
            Assert.Equal(HttpStatusCode.BadRequest,
                (await PutNote(admin, w, WholePeriod, subGroup: 0, topic: "Butun sinf", conducted: true)).StatusCode);
        });
    }

    // =====================================================================
    //  5. Davomat maxraji — har o'quvchi o'z yarmini ko'radi
    // =====================================================================

    /// <summary>
    /// Davomat maxrajiga o'quvchining O'Z guruhi darslari va butun sinf darslari
    /// kiradi, boshqa yarimniki KIRMAYDI. Guruhsiz o'quvchi esa bo'lingan darsni
    /// umuman ko'rmaydi — u hech bir yarmida yo'q.
    /// </summary>
    [Fact]
    public async Task Davomat_maxraji_oquvchining_oz_yarmidan_yigiladi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        // Bir kunda: bo'lingan dars (ikkala yarmi ham o'tilgan) + butun sinf darsi.
        await MarkConductedAsync(w, SplitPeriod, subGroup: 1);
        await MarkConductedAsync(w, SplitPeriod, subGroup: 2);
        await MarkConductedAsync(w, WholePeriod, subGroup: 0);

        // 1-guruh o'quvchisi o'z yarmiga kelmagan.
        await PutEntry(admin, w, w.Group1StudentId, SplitPeriod, reasonId: w.AbsentReasonId);

        var attendance = await AttendanceAsync(admin, w);

        // 1-guruh: maxraj 2 (o'z yarmi + butun sinf), bittasiga kelmagan → 50%.
        Assert.Equal(50d, attendance[w.Group1StudentId]);
        // 2-guruh: maxraj 2, yo'qlik yo'q → 100%.
        Assert.Equal(100d, attendance[w.Group2StudentId]);
        // Guruhsiz: maxraj 1 — faqat butun sinf darsi → 100%.
        Assert.Equal(100d, attendance[w.NoGroupStudentId]);
    }

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    private sealed record SplitWorld(
        string Tag, string ClassId, string SubjectId, string TeacherId,
        string Group1StudentId, string Group2StudentId, string NoGroupStudentId,
        string AbsentReasonId);

    /// <summary>
    /// Sinf, fan, uch o'quvchi (1-guruh, 2-guruh, guruhsiz) va bitta "kelmadi" sababi.
    /// <paramref name="withTemplate"/> — seshanba 1-darsda BO'LINGAN dars (guruh 1 va 2),
    /// 2-darsda butun sinf darsi; shablon chorakning 1-haftasiga biriktiriladi.
    /// </summary>
    private async Task<SplitWorld> SeedAsync(bool withTemplate = false)
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var cls = new SchoolClass { Name = $"SB-{tag}", Grade = 6 };
        var subject = new Subject { Name = $"Ingliz tili {tag}" };
        var teacher = new Teacher { FullName = $"Bo'linma o'qituvchisi {tag}" };
        var absent = new AbsenceReason { Name = $"Kelmadi {tag}", Short = "K", IsLate = false };

        var g1 = GeneralSettingsFlagsTests.NewStudent($"Birinchi yarim {tag}", cls.Name, "+998900000021");
        g1.SubGroup = 1;
        var g2 = GeneralSettingsFlagsTests.NewStudent($"Ikkinchi yarim {tag}", cls.Name, "+998900000022");
        g2.SubGroup = 2;
        var g0 = GeneralSettingsFlagsTests.NewStudent($"Guruhsiz {tag}", cls.Name, "+998900000023");

        await fixture.Api.WithDbAsync(async db =>
        {
            db.Classes.Add(cls);
            db.Subjects.Add(subject);
            db.Teachers.Add(teacher);
            db.AbsenceReasons.Add(absent);
            db.Students.AddRange(g1, g2, g0);

            if (withTemplate)
            {
                var tpl = new ScheduleTemplate { ClassId = cls.Id, Name = "Asosiy " + tag };
                foreach (var subGroup in new[] { 1, 2 })
                    tpl.Lessons.Add(new ScheduleLesson
                    {
                        TemplateId = tpl.Id, Day = 1, Period = SplitPeriod,
                        SubjectId = subject.Id, TeacherId = teacher.Id, SubGroup = subGroup,
                    });
                tpl.Lessons.Add(new ScheduleLesson
                {
                    TemplateId = tpl.Id, Day = 1, Period = WholePeriod,
                    SubjectId = subject.Id, TeacherId = teacher.Id, SubGroup = 0,
                });
                db.ScheduleTemplates.Add(tpl);
                db.WeekAssignments.Add(new WeekAssignment
                {
                    ClassId = cls.Id, Quarter = Quarter, Week = 1, TemplateId = tpl.Id,
                });
            }

            await db.SaveChangesAsync();
        });

        return new SplitWorld(tag, cls.Id, subject.Id, teacher.Id, g1.Id, g2.Id, g0.Id, absent.Id);
    }

    private async Task MarkConductedAsync(SplitWorld w, int period, int subGroup) =>
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
        HttpClient client, SplitWorld w, string studentId, int period,
        int? grade = null, string? reasonId = null) =>
        client.PutAsJsonAsync(Journal, new
        {
            classId = w.ClassId, subjectId = w.SubjectId, quarter = Quarter, studentId,
            date = Date, period, grade, reasonId, homework = 0, behavior = 0, mastery = (int?)null,
        });

    private static Task<HttpResponseMessage> PutNote(
        HttpClient client, SplitWorld w, int period, int subGroup, string topic, bool conducted = true) =>
        client.PutAsJsonAsync(Notes, new
        {
            classId = w.ClassId, subjectId = w.SubjectId, quarter = Quarter,
            date = Date, period, topic, homework = (string?)null, conducted, subGroup,
        });

    private static async Task<Dictionary<int, JsonElement>> NotesBySubGroupAsync(
        HttpClient client, SplitWorld w, int period) =>
        (await ArrayAsync(client, $"{Notes}?classId={w.ClassId}&subjectId={w.SubjectId}&quarter={Quarter}"))
        .Where(n => n.GetProperty("period").GetInt32() == period)
        .ToDictionary(n => n.GetProperty("subGroup").GetInt32());

    private static Task<List<JsonElement>> ColumnsAsync(HttpClient client, SplitWorld w) =>
        ArrayAsync(client, $"{Journal}/columns?classId={w.ClassId}&subjectId={w.SubjectId}&quarter={Quarter}");

    private static async Task<Dictionary<string, double>> AttendanceAsync(HttpClient client, SplitWorld w)
    {
        var url = $"/api/admin/classes/{w.ClassId}/performance";
        var response = await client.GetAsync(url);
        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"{url} → {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("rows").EnumerateArray()
            .Where(r => r.GetProperty("attendance").ValueKind != JsonValueKind.Null)
            .ToDictionary(
                r => r.GetProperty("student").GetProperty("id").GetString()!,
                r => r.GetProperty("attendance").GetDouble());
    }

    private static async Task<List<JsonElement>> ArrayAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"{url} → {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.EnumerateArray().Select(e => e.Clone()).ToList();
    }

    private static async Task<string> MessageAsync(HttpResponseMessage response)
    {
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return json.RootElement.GetProperty("message").GetString() ?? "";
    }
}
