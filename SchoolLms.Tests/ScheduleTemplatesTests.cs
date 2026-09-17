using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

// ===========================================================================
//  G-3 — DARS JADVALI: SHABLON, KATAK, HAFTAGA BIRIKTIRISH, O'QITUVCHI HAFTASI
//  (docs/modules/students-parity.md §2.1.3 "Schedule", §2.1.6 G-3/G-11)
// ===========================================================================
//
//  Bugun jadval zanjiri SINFga tayanadi:
//    `WeekAssignment.ClassId → ScheduleTemplate.ClassId → ScheduleLesson`.
//  §2.1.4 dagi qaror bo'yicha guruh darsi AYNAN SHU ustunlarni ishlatadi,
//  ustiga `owner_kind` qo'shiladi — ya'ni bu controller'lar cut-over'da
//  (§4.3, C1) qayta o'qiladi, lekin SINF darsi uchun javob BIR BAYT HAM
//  o'zgarmasligi kerak. Shu fayl o'sha "bir bayt" ni ushlab turadi.
//
//  QAMROV
//  ------
//   1. Shablon: yaratish, nomlash, o'chirish, begona sinfga tegmaslik
//   2. Katak (`PUT .../cell`): butun sinf, BO'LINGAN (1+2), tozalash, validatsiya
//   3. Eski oqim (`PUT .../{day}/{period}`) — bo'linishni bosib ketadi
//   4. Haftaga biriktirish: chorak bo'yicha butunlay almashtiriladi, bo'sh hafta
//   5. Band soatlar (`occupied-slots`): bo'lingan dars BITTA band soat
//   6. O'qituvchi haftasi: qo'ng'iroqlar jadvali + bo'lingan dars IKKI qator
// ===========================================================================

/// <summary>
/// Dars jadvalining bugungi xatti-harakati, HTTP darajasida. Batafsil — fayl boshidagi izoh.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class ScheduleTemplatesTests(ApiFixture fixture)
{
    private const int Quarter = 1;
    private const int Week = 1;

    // =====================================================================
    //  1. Shablon
    // =====================================================================

    /// <summary>Yangi shablon bo'sh tug'iladi; nomi o'zgaradi; o'chirilganda darslari bilan ketadi.</summary>
    [Fact]
    public async Task Shablon_yaratiladi_nomlanadi_va_darslari_bilan_ochiriladi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var created = await CreateTemplateAsync(admin, w, "Asosiy");
        Assert.Empty(created.GetProperty("lessons").EnumerateArray());
        Assert.Equal(w.ClassId, created.GetProperty("classId").GetString());
        var templateId = created.GetProperty("id").GetString()!;

        await SetCellAsync(admin, w, templateId, day: 0, period: 1, (0, w.SubjectId, w.TeacherId));

        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.PatchAsJsonAsync($"{Base(w)}/{templateId}", new { name = "Qishki" })).StatusCode);
        Assert.Equal("Qishki", (await TemplatesAsync(admin, w)).Single().GetProperty("name").GetString());

        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"{Base(w)}/{templateId}")).StatusCode);
        Assert.Empty(await TemplatesAsync(admin, w));

        // Kaskad: shablon bilan birga darslari ham o'chdi.
        await fixture.Api.WithDbAsync(async db =>
            Assert.Empty(await db.ScheduleTemplates.AsNoTracking()
                .Where(t => t.ClassId == w.ClassId).ToListAsync()));
    }

    /// <summary>Shablon id'si BEGONA sinf yo'li bilan so'ralsa — 404 (id yetarli emas).</summary>
    [Fact]
    public async Task Begona_sinf_yoli_bilan_shablonga_tegib_bolmaydi_404()
    {
        var mine = await SeedAsync();
        var other = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var templateId = (await CreateTemplateAsync(admin, mine, "Asosiy")).GetProperty("id").GetString()!;

        Assert.Equal(HttpStatusCode.NotFound,
            (await admin.PatchAsJsonAsync($"{Base(other)}/{templateId}", new { name = "Yangi" })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await admin.DeleteAsync($"{Base(other)}/{templateId}")).StatusCode);
    }

    // =====================================================================
    //  2. Katak: butun sinf, bo'lingan, tozalash, validatsiya
    // =====================================================================

    /// <summary>Butun sinf darsi — bitta katak, guruh = 0.</summary>
    [Fact]
    public async Task Butun_sinf_darsi_bitta_katak()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var templateId = (await CreateTemplateAsync(admin, w, "Asosiy")).GetProperty("id").GetString()!;

        Assert.Equal(HttpStatusCode.NoContent,
            (await SetCellAsync(admin, w, templateId, day: 2, period: 3, (0, w.SubjectId, w.TeacherId))).StatusCode);

        var lesson = Assert.Single(await LessonsAsync(admin, w, templateId));
        Assert.Equal(2, lesson.GetProperty("day").GetInt32());
        Assert.Equal(3, lesson.GetProperty("period").GetInt32());
        Assert.Equal(0, lesson.GetProperty("subGroup").GetInt32());
        Assert.Equal(w.SubjectId, lesson.GetProperty("subjectId").GetString());
        Assert.Equal(w.TeacherId, lesson.GetProperty("teacherId").GetString());
    }

    /// <summary>
    /// BO'LINGAN DARS: bitta (kun, dars raqami) da IKKI yozuv — guruh 1 va guruh 2,
    /// har birining o'z fani va o'qituvchisi bo'lishi mumkin.
    /// </summary>
    [Fact]
    public async Task Bolingan_dars_ikki_yozuv_boladi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var templateId = (await CreateTemplateAsync(admin, w, "Asosiy")).GetProperty("id").GetString()!;

        Assert.Equal(HttpStatusCode.NoContent, (await SetCellAsync(admin, w, templateId, day: 1, period: 4,
            (1, w.SubjectId, w.TeacherId), (2, w.SecondSubjectId, w.SecondTeacherId))).StatusCode);

        var lessons = await LessonsAsync(admin, w, templateId);
        Assert.Equal(2, lessons.Count);
        // Tartib: kun → dars raqami → guruh.
        Assert.Equal(new[] { 1, 2 }, lessons.Select(l => l.GetProperty("subGroup").GetInt32()).ToArray());
        Assert.Equal(w.SubjectId, lessons[0].GetProperty("subjectId").GetString());
        Assert.Equal(w.SecondSubjectId, lessons[1].GetProperty("subjectId").GetString());
        Assert.Equal(w.SecondTeacherId, lessons[1].GetProperty("teacherId").GetString());
    }

    /// <summary>Bo'sh <c>lessons</c> — katak tozalanadi.</summary>
    [Fact]
    public async Task Bosh_katak_jadvaldan_ochiriladi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var templateId = (await CreateTemplateAsync(admin, w, "Asosiy")).GetProperty("id").GetString()!;

        await SetCellAsync(admin, w, templateId, day: 1, period: 4, (1, w.SubjectId, w.TeacherId), (2, w.SubjectId, w.SecondTeacherId));
        Assert.Equal(HttpStatusCode.NoContent, (await SetCellAsync(admin, w, templateId, day: 1, period: 4)).StatusCode);

        Assert.Empty(await LessonsAsync(admin, w, templateId));
    }

    /// <summary>Katak validatsiyasi — to'rtta qoida, har biri 400 bilan.</summary>
    [Fact]
    public async Task Katak_validatsiyasi_400_qaytaradi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var templateId = (await CreateTemplateAsync(admin, w, "Asosiy")).GetProperty("id").GetString()!;
        var url = $"{Base(w)}/{templateId}/cell";

        // Guruh 0, 1 yoki 2 dan boshqa bo'lishi mumkin emas.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(url, new
        {
            day = 1, period = 4,
            lessons = new[] { new { day = 1, period = 4, subjectId = w.SubjectId, teacherId = w.TeacherId, subGroup = 3 } },
        })).StatusCode);

        // Butun sinf (0) boshqa guruh bilan birga tura olmaydi.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(url, new
        {
            day = 1, period = 4,
            lessons = new[]
            {
                new { day = 1, period = 4, subjectId = w.SubjectId, teacherId = w.TeacherId, subGroup = 0 },
                new { day = 1, period = 4, subjectId = w.SubjectId, teacherId = w.TeacherId, subGroup = 1 },
            },
        })).StatusCode);

        // Bitta guruhda ikkita dars bo'lmaydi.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(url, new
        {
            day = 1, period = 4,
            lessons = new[]
            {
                new { day = 1, period = 4, subjectId = w.SubjectId, teacherId = w.TeacherId, subGroup = 1 },
                new { day = 1, period = 4, subjectId = w.SecondSubjectId, teacherId = w.TeacherId, subGroup = 1 },
            },
        })).StatusCode);

        // Dars ichidagi kun/raqam so'rovdagisiga mos kelishi shart.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync(url, new
        {
            day = 1, period = 4,
            lessons = new[] { new { day = 2, period = 4, subjectId = w.SubjectId, teacherId = w.TeacherId, subGroup = 0 } },
        })).StatusCode);

        // Hech biri yozilmagan.
        Assert.Empty(await LessonsAsync(admin, w, templateId));
    }

    /// <summary>
    /// ESKI OQIM (<c>PUT .../{day}/{period}</c>) — orqaga moslik uchun: katakdagi
    /// BARCHA guruh yozuvlarini o'chirib, bitta butun-sinf darsini qo'yadi.
    /// Ya'ni bo'lingan darsni jimgina bosib ketadi. Tozalash ham har ikki guruhni oladi.
    /// </summary>
    [Fact]
    public async Task Eski_oqim_bolinishni_bosib_ketadi_va_tozalaydi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var templateId = (await CreateTemplateAsync(admin, w, "Asosiy")).GetProperty("id").GetString()!;

        await SetCellAsync(admin, w, templateId, day: 1, period: 4,
            (1, w.SubjectId, w.TeacherId), (2, w.SecondSubjectId, w.SecondTeacherId));

        var slot = $"{Base(w)}/{templateId}/1/4";
        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync(slot, new
        {
            day = 1, period = 4, subjectId = w.SubjectId, teacherId = w.TeacherId, subGroup = 0,
        })).StatusCode);

        var lesson = Assert.Single(await LessonsAsync(admin, w, templateId));
        Assert.Equal(0, lesson.GetProperty("subGroup").GetInt32());

        // Qayta bo'lamiz va eski oqim bilan tozalaymiz — ikkala yarim ham ketadi.
        await SetCellAsync(admin, w, templateId, day: 1, period: 4,
            (1, w.SubjectId, w.TeacherId), (2, w.SecondSubjectId, w.SecondTeacherId));
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync(slot)).StatusCode);
        Assert.Empty(await LessonsAsync(admin, w, templateId));
    }

    // =====================================================================
    //  3. Haftaga biriktirish
    // =====================================================================

    /// <summary>
    /// Biriktirish chorak bo'yicha BUTUNLAY ALMASHTIRILADI (qo'shilmaydi): ikkinchi
    /// saqlash birinchisining haftalarini o'chiradi. Bo'sh hafta (<c>templateId = null</c>)
    /// ham saqlanadi va shu holida qaytadi.
    /// </summary>
    [Fact]
    public async Task Haftaga_biriktirish_chorak_boyicha_butunlay_almashtiriladi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var first = (await CreateTemplateAsync(admin, w, "Asosiy")).GetProperty("id").GetString()!;
        var second = (await CreateTemplateAsync(admin, w, "Qishki")).GetProperty("id").GetString()!;
        var url = $"/api/admin/classes/{w.ClassId}/week-assignments";

        Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync(url, new
        {
            quarter = Quarter,
            assignments = new object[]
            {
                new { week = 1, templateId = first },
                new { week = 2, templateId = (string?)null },
                new { week = 3, templateId = second },
            },
        })).StatusCode);

        var rows = await ArrayAsync(admin, $"{url}?quarter={Quarter}");
        Assert.Equal(new[] { 1, 2, 3 }, rows.Select(r => r.GetProperty("week").GetInt32()).ToArray());
        Assert.Equal(first, rows[0].GetProperty("templateId").GetString());
        Assert.Equal(JsonValueKind.Null, rows[1].GetProperty("templateId").ValueKind);
        Assert.Equal(second, rows[2].GetProperty("templateId").GetString());

        // Ikkinchi saqlash — eski uchta qator o'chadi, faqat yangisi qoladi.
        await admin.PutAsJsonAsync(url, new
        {
            quarter = Quarter,
            assignments = new object[] { new { week = 5, templateId = second } },
        });

        var after = await ArrayAsync(admin, $"{url}?quarter={Quarter}");
        Assert.Equal(new[] { 5 }, after.Select(r => r.GetProperty("week").GetInt32()).ToArray());
    }

    /// <summary>Biriktirish CHORAKKA tegishli: 2-chorak 1-chorakni ko'rmaydi.</summary>
    [Fact]
    public async Task Biriktirish_choraklar_orasida_aralashmaydi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var templateId = (await CreateTemplateAsync(admin, w, "Asosiy")).GetProperty("id").GetString()!;
        var url = $"/api/admin/classes/{w.ClassId}/week-assignments";

        await admin.PutAsJsonAsync(url, new
        {
            quarter = 1,
            assignments = new object[] { new { week = 1, templateId } },
        });

        Assert.Single(await ArrayAsync(admin, $"{url}?quarter=1"));
        Assert.Empty(await ArrayAsync(admin, $"{url}?quarter=2"));
    }

    // =====================================================================
    //  4. Band soatlar (o'qituvchi ziddiyati)
    // =====================================================================

    /// <summary>
    /// Band soatlar (kun, dars raqami) bo'yicha guruhlanadi: bo'lingan darsning
    /// ikkala yarmini BIR o'qituvchi olsa ham — BITTA band soat.
    /// <c>excludeTemplateId</c> tahrirlanayotgan shablonni o'zi bilan ziddiyatga kiritmaydi.
    /// </summary>
    [Fact]
    public async Task Bolingan_dars_bitta_band_soat_beradi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var templateId = (await CreateTemplateAsync(admin, w, "Asosiy")).GetProperty("id").GetString()!;

        await SetCellAsync(admin, w, templateId, day: 1, period: 4,
            (1, w.SubjectId, w.TeacherId), (2, w.SecondSubjectId, w.TeacherId));
        await SetCellAsync(admin, w, templateId, day: 2, period: 5, (0, w.SubjectId, w.TeacherId));

        var slots = await OccupiedAsync(admin, w.TeacherId, excludeTemplateId: null);
        Assert.Equal(
            new[] { (1, 4), (2, 5) },
            slots.Select(s => (s.GetProperty("day").GetInt32(), s.GetProperty("period").GetInt32()))
                .OrderBy(x => x.Item1).ToArray());
        Assert.Equal(w.ClassName, slots[0].GetProperty("className").GetString());
        Assert.Equal("Asosiy", slots[0].GetProperty("templateName").GetString());

        // O'z shabloni chiqarib tashlansa — band soat qolmaydi.
        Assert.Empty(await OccupiedAsync(admin, w.TeacherId, templateId));
    }

    /// <summary>
    /// TUZOQ: sinfi ARXIVLANGAN shablon band soatlar xaritasidan butunlay tushib qoladi —
    /// ziddiyat ogohlantirishi jimgina yo'qoladi.
    /// </summary>
    [Fact]
    public async Task Arxivlangan_sinf_band_soat_bermaydi_bugungi_xatti_harakat()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var templateId = (await CreateTemplateAsync(admin, w, "Asosiy")).GetProperty("id").GetString()!;
        await SetCellAsync(admin, w, templateId, day: 3, period: 2, (0, w.SubjectId, w.TeacherId));

        Assert.Single(await OccupiedAsync(admin, w.TeacherId, excludeTemplateId: null));

        await fixture.Api.WithDbAsync(async db =>
        {
            var cls = await db.Classes.FindAsync(w.ClassId);
            cls!.IsArchived = true;
            await db.SaveChangesAsync();
        });

        Assert.Empty(await OccupiedAsync(admin, w.TeacherId, excludeTemplateId: null));
    }

    // =====================================================================
    //  5. O'qituvchining haftasi
    // =====================================================================

    /// <summary>
    /// O'qituvchi haftasi biriktirilgan shablondan yig'iladi: qo'ng'iroqlar jadvalidagi
    /// vaqtlar qo'shiladi, bo'lingan dars IKKI QATOR bo'lib chiqadi (har guruhga bittadan).
    ///
    /// <para>
    /// BUGUNGI XATTI-HARAKAT: tartib faqat <b>kun → dars raqami</b> bo'yicha
    /// (<c>PortalSchedule.TeacherWeekAsync</c>). Bitta katak ichidagi ikki yarimning
    /// o'zaro tartibi ANIQLANMAGAN — shuning uchun bu yerda ketma-ketlik emas,
    /// TO'PLAM tekshiriladi. (Jadval ustunlari, aksincha, guruh bo'yicha ham
    /// tartiblanadi — <c>JournalSubGroupTests</c>.)
    /// </para>
    /// </summary>
    [Fact]
    public async Task Oqituvchi_haftasi_qongiroq_vaqtlari_bilan_va_bolingan_dars_ikki_qator()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var templateId = (await CreateTemplateAsync(admin, w, "Asosiy")).GetProperty("id").GetString()!;

        await SetCellAsync(admin, w, templateId, day: 0, period: 1, (0, w.SubjectId, w.TeacherId));
        await SetCellAsync(admin, w, templateId, day: 1, period: 2,
            (1, w.SubjectId, w.TeacherId), (2, w.SecondSubjectId, w.TeacherId));
        await admin.PutAsJsonAsync($"/api/admin/classes/{w.ClassId}/week-assignments", new
        {
            quarter = Quarter,
            assignments = new object[] { new { week = Week, templateId } },
        });

        var (teacherId, teacher) = await TeacherClientAsync(TeacherPermissions.Schedule);
        using var _ = teacher;
        await AttachTeacherAsync(templateId, teacherId);

        await WithLessonTimesAsync(async () =>
        {
            var lessons = await ArrayAsync(teacher, $"/api/teacher/schedule?quarter={Quarter}&week={Week}");

            Assert.Equal(3, lessons.Count);

            // Kun → dars raqami tartibi kafolatlangan: butun sinf darsi birinchi.
            Assert.Equal(new[] { 0, 1, 1 }, lessons.Select(l => l.GetProperty("day").GetInt32()).ToArray());
            Assert.Equal(new[] { 1, 2, 2 }, lessons.Select(l => l.GetProperty("period").GetInt32()).ToArray());

            // Bitta katakdagi ikki yarim — to'plam sifatida (ichki tartib aniqlanmagan).
            Assert.Equal(new[] { 0, 1, 2 },
                lessons.Select(l => l.GetProperty("subGroup").GetInt32()).OrderBy(x => x).ToArray());

            // Qo'ng'iroqlar jadvali — dars raqami bo'yicha.
            Assert.Equal("08:30", lessons[0].GetProperty("startTime").GetString());
            Assert.Equal("09:15", lessons[0].GetProperty("endTime").GetString());
            Assert.Equal("09:25", lessons[1].GetProperty("startTime").GetString());
            Assert.Equal("10:10", lessons[2].GetProperty("endTime").GetString());

            // Sinf va fan nomlari ham shu javobdan keladi; 2-guruh — ikkinchi fan.
            Assert.Equal(w.ClassName, lessons[0].GetProperty("className").GetString());
            var secondHalf = lessons.Single(l => l.GetProperty("subGroup").GetInt32() == 2);
            Assert.Equal(w.SecondSubjectName, secondHalf.GetProperty("subjectName").GetString());
        });
    }

    /// <summary>
    /// Qo'ng'iroqlar jadvalida shu dars raqami YO'Q bo'lsa — vaqtlar <c>null</c> keladi
    /// (dars baribir ko'rsatiladi).
    /// </summary>
    [Fact]
    public async Task Qongiroq_jadvalida_yoq_dars_raqami_vaqtsiz_keladi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var templateId = (await CreateTemplateAsync(admin, w, "Asosiy")).GetProperty("id").GetString()!;

        await SetCellAsync(admin, w, templateId, day: 0, period: 9, (0, w.SubjectId, w.TeacherId));
        await admin.PutAsJsonAsync($"/api/admin/classes/{w.ClassId}/week-assignments", new
        {
            quarter = Quarter,
            assignments = new object[] { new { week = Week, templateId } },
        });

        var (teacherId, teacher) = await TeacherClientAsync(TeacherPermissions.Schedule);
        using var _ = teacher;
        await AttachTeacherAsync(templateId, teacherId);

        await WithLessonTimesAsync(async () =>
        {
            var lesson = Assert.Single(await ArrayAsync(teacher, $"/api/teacher/schedule?quarter={Quarter}&week={Week}"));
            Assert.Equal(JsonValueKind.Null, lesson.GetProperty("startTime").ValueKind);
            Assert.Equal(JsonValueKind.Null, lesson.GetProperty("endTime").ValueKind);
        });
    }

    /// <summary>Haftaga hech narsa biriktirilmagan bo'lsa — o'qituvchi haftasi bo'sh.</summary>
    [Fact]
    public async Task Biriktirilmagan_hafta_bosh_jadval_beradi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var templateId = (await CreateTemplateAsync(admin, w, "Asosiy")).GetProperty("id").GetString()!;
        await SetCellAsync(admin, w, templateId, day: 0, period: 1, (0, w.SubjectId, w.TeacherId));

        var (teacherId, teacher) = await TeacherClientAsync(TeacherPermissions.Schedule);
        using var _ = teacher;
        await AttachTeacherAsync(templateId, teacherId);

        // 9-hafta hech qayerda biriktirilmagan.
        Assert.Empty(await ArrayAsync(teacher, $"/api/teacher/schedule?quarter={Quarter}&week=9"));
    }

    /// <summary>Jadval ruxsati yo'q o'qituvchi o'z haftasini ham ko'ra olmaydi — 403.</summary>
    [Fact]
    public async Task Jadval_ruxsatisiz_oqituvchi_403()
    {
        var (_, teacher) = await TeacherClientAsync(TeacherPermissions.Journal);
        using var _client = teacher;

        Assert.Equal(HttpStatusCode.Forbidden,
            (await teacher.GetAsync($"/api/teacher/schedule?quarter={Quarter}&week={Week}")).StatusCode);
    }

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    private sealed record ScheduleWorld(
        string Tag, string ClassId, string ClassName,
        string SubjectId, string SecondSubjectId, string SecondSubjectName,
        string TeacherId, string SecondTeacherId);

    private static string Base(ScheduleWorld w) => $"/api/admin/classes/{w.ClassId}/schedule-templates";

    private async Task<ScheduleWorld> SeedAsync()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var cls = new SchoolClass { Name = $"JD-{tag}", Grade = 8 };
        var subject = new Subject { Name = $"Fizika {tag}" };
        var second = new Subject { Name = $"Chizmachilik {tag}" };
        var teacher = new Teacher { FullName = $"Jadval o'qituvchisi {tag}" };
        var secondTeacher = new Teacher { FullName = $"Ikkinchi o'qituvchi {tag}" };

        await fixture.Api.WithDbAsync(async db =>
        {
            db.Classes.Add(cls);
            db.Subjects.AddRange(subject, second);
            db.Teachers.AddRange(teacher, secondTeacher);
            await db.SaveChangesAsync();
        });

        return new ScheduleWorld(
            tag, cls.Id, cls.Name, subject.Id, second.Id, second.Name, teacher.Id, secondTeacher.Id);
    }

    /// <summary>Tizim akkaunti bor o'qituvchi (portal so'rovlari uchun) va uning klienti.</summary>
    private async Task<(string TeacherId, HttpClient Client)> TeacherClientAsync(params string[] permissions)
    {
        var (user, _) = await fixture.Api.SeedUserAsync(Roles.Teacher);
        var teacherId = "";
        await fixture.Api.WithDbAsync(async db =>
        {
            var t = await db.Teachers.SingleAsync(x => x.UserId == user.Id);
            t.Permissions = [.. permissions];
            await db.SaveChangesAsync();
            teacherId = t.Id;
        });
        return (teacherId, fixture.Api.ClientWithToken(
            fixture.Api.TokenFor(Roles.Teacher, user.Id, user.FullName, user.Email)));
    }

    /// <summary>Shablondagi barcha darslarni shu o'qituvchiga o'tkazadi.</summary>
    private async Task AttachTeacherAsync(string templateId, string teacherId) =>
        await fixture.Api.WithDbAsync(async db =>
        {
            var tpl = await db.ScheduleTemplates.Include(t => t.Lessons)
                .SingleAsync(t => t.Id == templateId);
            foreach (var lesson in tpl.Lessons) lesson.TeacherId = teacherId;
            await db.SaveChangesAsync();
        });

    /// <summary>
    /// Qo'ng'iroqlar jadvali UMUMIY (butun maktabga bitta) va <c>PortalSchedule</c>
    /// uni dars raqami bo'yicha lug'atga aylantiradi — takror raqam butun so'rovni
    /// yiqitadi. Shuning uchun test davriga qo'yiladi va asl holati qaytariladi.
    /// </summary>
    private async Task WithLessonTimesAsync(Func<Task> body)
    {
        List<LessonTime> saved = [];
        await fixture.Api.WithDbAsync(async db =>
        {
            saved = await db.LessonTimes.AsNoTracking().ToListAsync();
            await db.LessonTimes.ExecuteDeleteAsync();
            db.LessonTimes.AddRange(
                new LessonTime { Period = 1, StartTime = "08:30", EndTime = "09:15" },
                new LessonTime { Period = 2, StartTime = "09:25", EndTime = "10:10" },
                new LessonTime { Period = 3, StartTime = "10:20", EndTime = "11:05" });
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
                await db.LessonTimes.ExecuteDeleteAsync();
                db.LessonTimes.AddRange(saved);
                await db.SaveChangesAsync();
            });
        }
    }

    private static async Task<JsonElement> CreateTemplateAsync(HttpClient client, ScheduleWorld w, string name)
    {
        var response = await client.PostAsJsonAsync(Base(w), new { name });
        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"{Base(w)} → {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.Clone();
    }

    private static Task<HttpResponseMessage> SetCellAsync(
        HttpClient client, ScheduleWorld w, string templateId, int day, int period,
        params (int SubGroup, string SubjectId, string TeacherId)[] lessons) =>
        client.PutAsJsonAsync($"{Base(w)}/{templateId}/cell", new
        {
            day, period,
            lessons = lessons.Select(l => new
            {
                day, period, subjectId = l.SubjectId, teacherId = l.TeacherId, subGroup = l.SubGroup,
            }).ToArray(),
        });

    private static Task<List<JsonElement>> TemplatesAsync(HttpClient client, ScheduleWorld w) =>
        ArrayAsync(client, Base(w));

    private static async Task<List<JsonElement>> LessonsAsync(HttpClient client, ScheduleWorld w, string templateId) =>
        (await TemplatesAsync(client, w))
        .Single(t => t.GetProperty("id").GetString() == templateId)
        .GetProperty("lessons").EnumerateArray().Select(l => l.Clone()).ToList();

    private static async Task<List<JsonElement>> OccupiedAsync(
        HttpClient client, string teacherId, string? excludeTemplateId)
    {
        var url = "/api/admin/schedule/occupied-slots"
                  + (excludeTemplateId is null ? "" : $"?excludeTemplateId={excludeTemplateId}");
        var response = await client.GetAsync(url);
        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"{url} → {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.TryGetProperty(teacherId, out var slots)
            ? slots.EnumerateArray().Select(s => s.Clone()).ToList()
            : [];
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
