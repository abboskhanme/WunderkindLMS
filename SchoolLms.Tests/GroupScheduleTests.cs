using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// G-11 — cut-over: guruh jadvali, o'chirgich va o'quvchi ziddiyati
/// (docs/modules/students-parity.md §2.1.4, §2.1.6, §4.3).
///
/// <para>
/// <b>Uchta da'vo tekshiriladi.</b>
/// </para>
/// <list type="number">
///   <item><b>O'chirgich yopiq ekan hech narsa o'zgarmaydi.</b> Guruh jadvali
///     yaratilishi mumkin (u qoralama), lekin HAFTAGA BIRIKTIRIB bo'lmaydi —
///     ya'ni birorta dars, jurnal, hisobot yoki maosh uni ko'rmaydi.</item>
///   <item><b>Bola ikki joyda bo'la olmaydi.</b> Sinf darsi va guruh darsi
///     bitta (kun, dars) ga tushsa — 409, va xabarda TO'QNASHGAN BOLALARNING
///     ISMLARI bo'ladi. Auditning "eng qiyin qismi" deb atagani shu.</item>
///   <item><b>Darvozalar joyida.</b> O'chirgichni faqat tizim egasi buradi;
///     jadval endpointlari esa o'z bo'lim ruxsatini talab qiladi.</item>
/// </list>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class GroupScheduleTests(ApiFixture fixture)
{
    private const string Switch = "/api/admin/group-lessons";
    private const string Groups = "/api/admin/study-groups";
    private static string Templates(string ownerId) => $"/api/admin/classes/{ownerId}/schedule-templates";
    private static string Weeks(string ownerId) => $"/api/admin/classes/{ownerId}/week-assignments";

    /* =====================================================================
     *  1. RUXSAT (RBAC)
     * ================================================================== */

    [Fact]
    public async Task Tokensiz_401()
    {
        using var client = fixture.Api.AnonymousClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Switch)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PutAsJsonAsync(Switch, new { enabled = true })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.GetAsync("/api/admin/schedule/occupied-slots")).StatusCode);
    }

    /// <summary>Jadval — o'quv bo'limining ichki ma'lumoti: o'qituvchi ham, kassir ham kira olmaydi.</summary>
    [Theory]
    [InlineData(Roles.Teacher)]
    [InlineData(Roles.Cashier)]
    public async Task Oqituvchi_va_kassir_403(string role)
    {
        using var client = await fixture.Api.ClientAsAsync(role, "schedule", "classes");

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Switch)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PutAsJsonAsync(Switch, new { enabled = true })).StatusCode);
    }

    /// <summary>
    /// Xodim (staff): o'qish ochiq, YOZISH esa faqat <c>schedule</c> kaliti bilan.
    /// Kalitsiz xodim jadval shabloni ham yarata olmaydi.
    /// </summary>
    [Fact]
    public async Task Xodim_oqiydi_lekin_schedule_ruxsatisiz_yozmaydi()
    {
        var w = await SeedAsync();
        using var noPerm = await fixture.Api.ClientAsAsync(Roles.Staff);

        Assert.Equal(HttpStatusCode.OK, (await noPerm.GetAsync(Templates(w.ClassAId))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await noPerm.PostAsJsonAsync(Templates(w.ClassAId), new { name = "Yangi" })).StatusCode);

        using var withPerm = await fixture.Api.ClientAsAsync(Roles.Staff, "schedule");
        Assert.Equal(HttpStatusCode.OK,
            (await withPerm.PostAsJsonAsync(Templates(w.ClassAId), new { name = "Yangi " + w.Tag })).StatusCode);
    }

    /// <summary>
    /// O'chirgich — CUT-OVER, oddiy sozlama emas. Uni oddiy admin ham bura
    /// olmaydi: faqat tizim egasi (§4.3, 3-qadam).
    /// </summary>
    [Fact]
    public async Task Ochirgichni_faqat_tizim_egasi_buradi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync(Switch)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await admin.PutAsJsonAsync(Switch, new { enabled = true })).StatusCode);

        await WithGroupLessonsAsync(async () =>
        {
            using var super = await fixture.Api.ClientAsAsync(Roles.SuperAdmin);
            var body = await super.GetFromJsonAsync<JsonElement>(Switch);
            Assert.True(body.GetProperty("enabled").GetBoolean());
        });
    }

    /* =====================================================================
     *  2. Cut-over darvozasi
     * ================================================================== */

    /// <summary>
    /// O'chirgich o'chiq: guruh jadvali YARATILADI (qoralama), lekin HAFTAGA
    /// BIRIKTIRILMAYDI. Aynan shu chegara tufayli o'chirgich burilmaguncha
    /// birorta raqam o'zgarmaydi.
    /// </summary>
    [Fact]
    public async Task Ochirgich_ochiq_bolsa_guruh_jadvali_haftaga_biriktirilmaydi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var groupId = await CreateGroupAsync(admin, w, [w.StudentA]);
        var templateId = await CreateTemplateAsync(admin, groupId, "Guruh jadvali " + w.Tag);

        // Shablon yaratildi va u GURUHniki deb belgilandi.
        await fixture.Api.WithDbAsync(async db =>
        {
            var tpl = await db.ScheduleTemplates.AsNoTracking().SingleAsync(t => t.Id == templateId);
            Assert.Equal(LessonOwnerKind.Group, tpl.OwnerKind);
        });

        var refused = await admin.PutAsJsonAsync(Weeks(groupId), new
        {
            quarter = 1,
            assignments = new[] { new { week = 1, templateId } },
        });
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("hali yoqilmagan", await MessageAsync(refused), StringComparison.Ordinal);

        // Bazada birorta guruh biriktirishi paydo bo'lmadi.
        await fixture.Api.WithDbAsync(async db =>
            Assert.Equal(0, await db.WeekAssignments.CountAsync(
                a => a.ClassId == groupId && a.OwnerKind == LessonOwnerKind.Group)));

        // O'chirgich yoqilganda o'sha so'rov o'tadi.
        await WithGroupLessonsAsync(async () =>
        {
            var ok = await admin.PutAsJsonAsync(Weeks(groupId), new
            {
                quarter = 1,
                assignments = new[] { new { week = 1, templateId } },
            });
            Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);
        });
    }

    /// <summary>
    /// Sinf jadvalini haftaga biriktirish o'chirgichdan QAT'I NAZAR ishlaydi —
    /// bugungi oqim tegilmaganini qo'riqlaydi.
    /// </summary>
    [Fact]
    public async Task Sinf_biriktirishi_ochirgichsiz_ham_ishlaydi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var templateId = await CreateTemplateAsync(admin, w.ClassAId, "Sinf jadvali " + w.Tag);
        var res = await admin.PutAsJsonAsync(Weeks(w.ClassAId), new
        {
            quarter = 1,
            assignments = new[] { new { week = 1, templateId } },
        });
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);

        await fixture.Api.WithDbAsync(async db =>
        {
            var a = await db.WeekAssignments.AsNoTracking()
                .SingleAsync(x => x.ClassId == w.ClassAId && x.Quarter == 1 && x.Week == 1);
            Assert.Equal(LessonOwnerKind.Class, a.OwnerKind);
            Assert.Equal(templateId, a.TemplateId);
        });
    }

    /* =====================================================================
     *  3. O'quvchi ziddiyati — G-11 ning eng qiyin qismi
     * ================================================================== */

    /// <summary>
    /// Bola sinf darsida VA guruh darsida bir vaqtda bo'lib qolsa — saqlash
    /// rad etiladi va xabarda uning ISMI bo'ladi.
    ///
    /// <para>
    /// Ikkala yo'l ham tekshiriladi: haftaga biriktirishda ham, allaqachon
    /// biriktirilgan shablonning katagini tahrirlashda ham.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Ikki_joyda_qolgan_bola_nomi_bilan_rad_etiladi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        // Sinf: dushanba 1-dars, 1-chorak 1-hafta.
        var classTpl = await CreateTemplateAsync(admin, w.ClassAId, "Sinf " + w.Tag);
        await SetCellAsync(admin, w.ClassAId, classTpl, day: 0, period: 1, w.SubjectId, w.TeacherId);
        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.PutAsJsonAsync(Weeks(w.ClassAId), new
            {
                quarter = 1,
                assignments = new[] { new { week = 1, templateId = classTpl } },
            })).StatusCode);

        var groupId = await CreateGroupAsync(admin, w, [w.StudentA]);
        var groupTpl = await CreateTemplateAsync(admin, groupId, "Guruh " + w.Tag);
        // Guruh shabloni hali biriktirilmagan — katakni to'ldirish MUMKIN.
        await SetCellAsync(admin, groupId, groupTpl, day: 0, period: 1, w.SubjectId, w.TeacherId);

        await WithGroupLessonsAsync(async () =>
        {
            // (a) biriktirish yo'li.
            var refused = await admin.PutAsJsonAsync(Weeks(groupId), new
            {
                quarter = 1,
                assignments = new[] { new { week = 1, templateId = groupTpl } },
            });
            Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
            var message = await MessageAsync(refused);
            Assert.Contains(w.StudentAName, message, StringComparison.Ordinal);
            Assert.Contains("Dushanba", message, StringComparison.Ordinal);

            // (b) katak yo'li: guruhni BO'SH soatga biriktiramiz, keyin band
            // soatga ko'chirmoqchi bo'lamiz.
            await ClearCellAsync(admin, groupId, groupTpl, day: 0, period: 1);
            await SetCellAsync(admin, groupId, groupTpl, day: 0, period: 5, w.SubjectId, w.TeacherId);
            Assert.Equal(HttpStatusCode.NoContent,
                (await admin.PutAsJsonAsync(Weeks(groupId), new
                {
                    quarter = 1,
                    assignments = new[] { new { week = 1, templateId = groupTpl } },
                })).StatusCode);

            var cellRefused = await admin.PutAsJsonAsync($"{Templates(groupId)}/{groupTpl}/cell", new
            {
                day = 0,
                period = 1,
                lessons = new[] { new { day = 0, period = 1, subjectId = w.SubjectId, teacherId = w.TeacherId, subGroup = 0 } },
            });
            Assert.Equal(HttpStatusCode.Conflict, cellRefused.StatusCode);
            Assert.Contains(w.StudentAName, await MessageAsync(cellRefused), StringComparison.Ordinal);
        });
    }

    /// <summary>
    /// Guruhda bo'lmagan bola hech narsani bloklamaydi: guruh boshqa sinfning
    /// bolasidan yig'ilgan bo'lsa, 5-A ning darsi bemalol saqlanadi.
    /// </summary>
    [Fact]
    public async Task Umumiy_bolasi_yoq_ega_ziddiyat_yaratmaydi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        // Guruhda faqat 5-B ning bolasi bor.
        var groupId = await CreateGroupAsync(admin, w, [w.StudentB]);
        var groupTpl = await CreateTemplateAsync(admin, groupId, "B guruh " + w.Tag);
        await SetCellAsync(admin, groupId, groupTpl, day: 0, period: 1, w.SubjectId, w.TeacherId);

        var classTpl = await CreateTemplateAsync(admin, w.ClassAId, "A sinf " + w.Tag);
        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.PutAsJsonAsync(Weeks(w.ClassAId), new
            {
                quarter = 1,
                assignments = new[] { new { week = 1, templateId = classTpl } },
            })).StatusCode);

        await WithGroupLessonsAsync(async () =>
        {
            Assert.Equal(HttpStatusCode.NoContent,
                (await admin.PutAsJsonAsync(Weeks(groupId), new
                {
                    quarter = 1,
                    assignments = new[] { new { week = 1, templateId = groupTpl } },
                })).StatusCode);

            // 5-A ning o'sha soatdagi darsi — 5-A da guruh a'zosi yo'q, ziddiyat yo'q.
            await SetCellAsync(admin, w.ClassAId, classTpl, day: 0, period: 1, w.SubjectId, w.TeacherId);
        });
    }

    /// <summary>
    /// Guruh darsida sinf ichidagi 1/2-bo'linish bo'lmaydi — guruhning o'zi
    /// allaqachon tanlangan bolalar ro'yxati.
    /// </summary>
    [Fact]
    public async Task Guruh_darsini_ikki_qismga_bolib_bolmaydi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var groupId = await CreateGroupAsync(admin, w, [w.StudentA]);
        var groupTpl = await CreateTemplateAsync(admin, groupId, "Bo'linmas " + w.Tag);

        var res = await admin.PutAsJsonAsync($"{Templates(groupId)}/{groupTpl}/cell", new
        {
            day = 1,
            period = 1,
            lessons = new[]
            {
                new { day = 1, period = 1, subjectId = w.SubjectId, teacherId = w.TeacherId, subGroup = 1 },
                new { day = 1, period = 1, subjectId = w.SubjectId, teacherId = w.TeacherId, subGroup = 2 },
            },
        });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("bo'linish", await MessageAsync(res), StringComparison.OrdinalIgnoreCase);
    }

    /* =====================================================================
     *  4. Band soatlar va sinfni o'chirish
     * ================================================================== */

    /// <summary>
    /// O'qituvchining band soatlari xaritasi GURUH shablonlarini ham ko'radi
    /// (o'chirgich yoqilganda) — aks holda u ikki joyga birdan yozilardi.
    /// O'chiq holatda esa xarita bugungidek, guruhsiz.
    /// </summary>
    [Fact]
    public async Task Band_soatlar_guruhni_ochirgich_yoqilganda_koradi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var groupId = await CreateGroupAsync(admin, w, [w.StudentA]);
        var groupTpl = await CreateTemplateAsync(admin, groupId, "Band " + w.Tag);
        await SetCellAsync(admin, groupId, groupTpl, day: 2, period: 7, w.SubjectId, w.TeacherId);

        Assert.Empty(await OccupiedForTeacherAsync(admin, w.TeacherId));

        await WithGroupLessonsAsync(async () =>
        {
            var slots = await OccupiedForTeacherAsync(admin, w.TeacherId);
            var slot = Assert.Single(slots, s => s.GetProperty("period").GetInt32() == 7);
            Assert.Equal(LessonOwnerKind.Group, slot.GetProperty("ownerKind").GetString());
        });
    }

    /// <summary>
    /// Sinfni o'chirish — u biror guruhni boqayotgan bo'lsa RAD ETILADI va
    /// xabarda guruh nomi bo'ladi. (Bazada FK ham RESTRICT; bu yerda uni
    /// o'zbekcha tushuntirib to'xtatamiz, 23503 o'rniga.)
    /// </summary>
    [Fact]
    public async Task Guruhni_boqayotgan_sinfni_ochirib_bolmaydi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var groupName = "Boquvchi " + w.Tag;
        await CreateGroupAsync(admin, w, [w.StudentA], groupName);

        // O'quvchilar to'sig'iga urilmaslik uchun ularni sinfdan chiqaramiz.
        await fixture.Api.WithDbAsync(async db =>
        {
            foreach (var s in await db.Students.Where(s => s.ClassName == w.ClassAName).ToListAsync())
                s.ClassName = "";
            await db.SaveChangesAsync();
        });

        var res = await admin.DeleteAsync($"/api/admin/classes/{w.ClassAId}");
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        var message = await MessageAsync(res);
        Assert.Contains("guruh", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(groupName, message, StringComparison.Ordinal);

        await fixture.Api.WithDbAsync(async db =>
            Assert.True(await db.Classes.AnyAsync(c => c.Id == w.ClassAId)));
    }

    /* =====================================================================
     *  Yordamchilar
     * ================================================================== */

    /// <summary>
    /// O'chirgichni YOQIB berilgan ishni bajaradi va HAR HOLDA o'chirib
    /// qo'yadi. Umumiy bazada bu majburiy: qo'shni klasslar
    /// (<c>StudyGroupTests</c>) uning o'chiq ekanini talab qiladi.
    /// </summary>
    private async Task WithGroupLessonsAsync(Func<Task> body)
    {
        using var super = await fixture.Api.ClientAsAsync(Roles.SuperAdmin);
        var on = await super.PutAsJsonAsync(Switch, new { enabled = true });
        Assert.Equal(HttpStatusCode.OK, on.StatusCode);
        try
        {
            await body();
        }
        finally
        {
            var off = await super.PutAsJsonAsync(Switch, new { enabled = false });
            Assert.Equal(HttpStatusCode.OK, off.StatusCode);
        }
    }

    private static async Task<List<JsonElement>> OccupiedForTeacherAsync(HttpClient client, string teacherId)
    {
        var body = await client.GetFromJsonAsync<JsonElement>("/api/admin/schedule/occupied-slots");
        return body.TryGetProperty(teacherId, out var slots)
            ? [.. slots.EnumerateArray()]
            : [];
    }

    private static async Task<string> CreateTemplateAsync(HttpClient client, string ownerId, string name)
    {
        var res = await client.PostAsJsonAsync(Templates(ownerId), new { name });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return JsonDocument.Parse(await res.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetString()!;
    }

    private static async Task SetCellAsync(
        HttpClient client, string ownerId, string templateId,
        int day, int period, string subjectId, string teacherId)
    {
        var res = await client.PutAsJsonAsync($"{Templates(ownerId)}/{templateId}/cell", new
        {
            day,
            period,
            lessons = new[] { new { day, period, subjectId, teacherId, subGroup = 0 } },
        });
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
    }

    private static async Task ClearCellAsync(
        HttpClient client, string ownerId, string templateId, int day, int period)
    {
        var res = await client.PutAsJsonAsync($"{Templates(ownerId)}/{templateId}/cell", new
        {
            day,
            period,
            lessons = Array.Empty<object>(),
        });
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);
    }

    private static async Task<string> CreateGroupAsync(
        HttpClient client, World w, IReadOnlyList<string> studentIds, string? name = null)
    {
        var res = await client.PostAsJsonAsync(Groups, new
        {
            name = name ?? "Guruh " + w.Tag,
            subjectId = w.SubjectId,
            classIds = new[] { w.ClassAId, w.ClassBId },
            teacherIds = new[] { w.TeacherId },
            gender = (string?)null,
            studentIds,
        });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return JsonDocument.Parse(await res.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetString()!;
    }

    private static async Task<string> MessageAsync(HttpResponseMessage res)
    {
        var body = await res.Content.ReadAsStringAsync();
        return JsonDocument.Parse(body).RootElement.TryGetProperty("message", out var m)
            ? m.GetString() ?? body
            : body;
    }

    /// <summary>
    /// Ikki sinf, guruhli fan, bitta o'qituvchi va har sinfda bittadan
    /// o'quvchi. Har test o'z dunyosini yaratadi (umumiy baza, takrorlanmas teg).
    /// </summary>
    private async Task<World> SeedAsync()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];

        var classA = new SchoolClass { Name = $"S{tag}-A", Grade = 5 };
        var classB = new SchoolClass { Name = $"S{tag}-B", Grade = 5 };
        var subject = new Subject { Name = $"Ingliz tili {tag}", IsGroupable = true };
        var teacher = new Teacher { FullName = $"Ustoz {tag}", SubjectIds = [] };

        var a = GeneralSettingsFlagsTests.NewStudent($"Ali {tag}", classA.Name, "+99890" + Rnd());
        var b = GeneralSettingsFlagsTests.NewStudent($"Bobur {tag}", classB.Name, "+99890" + Rnd());

        await fixture.Api.WithDbAsync(async db =>
        {
            db.Classes.AddRange(classA, classB);
            db.Subjects.Add(subject);
            teacher.SubjectIds = [subject.Id];
            db.Teachers.Add(teacher);
            db.Students.AddRange(a, b);
            await db.SaveChangesAsync();
        });

        return new World(tag, classA.Id, classA.Name, classB.Id, subject.Id, teacher.Id,
            a.Id, a.FullName, b.Id);
    }

    private static string Rnd() => Random.Shared.Next(1_000_000, 9_999_999).ToString();

    private sealed record World(
        string Tag,
        string ClassAId,
        string ClassAName,
        string ClassBId,
        string SubjectId,
        string TeacherId,
        string StudentA,
        string StudentAName,
        string StudentB);
}
