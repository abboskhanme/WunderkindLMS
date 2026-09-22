using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// O'quv guruhlari (docs/modules/students-parity.md §2.1, G-6/G-7/G-9).
///
/// <para>
/// <b>Nima isbotlanadi.</b> Guruh — sinfning yonidagi ikkinchi "o'quvchi
/// to'plami", va uning butun ma'nosi bitta qoidada: <b>bitta o'quvchi bitta
/// fandan ko'pi bilan bitta faol guruhda</b>. Qoida bazada qisman unikal
/// indeks bilan yozilgan, lekin foydalanuvchiga 23505 chiqarish — qoidani
/// tushuntirmaslik demak. Shuning uchun quyida qoidaning O'ZI ham, uning
/// O'ZBEKCHA xabari ham tekshiriladi.
/// </para>
/// <para>
/// <b>Va nima O'ZGARMASLIGI isbotlanadi.</b> Bu slice'da guruh darslari YO'Q:
/// <c>school_meta.group_lessons_enabled</c> o'chiq turadi, guruh yaratish
/// hech qanday jadval shabloni, hafta biriktirishi yoki jurnal qatorini
/// yozmaydi va sinfning bugungi ro'yxati (<c>students.class_name</c>,
/// <c>sub_group</c>) tegilmaydi. Buni <see cref="Guruh_yaratish_darslarga_tegmaydi"/>
/// qo'riqlaydi — keyingi to'lqin (G-11..G-18) uni buzsa, darhol ko'rinadi.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class StudyGroupTests(ApiFixture fixture)
{
    private const string Groups = "/api/admin/study-groups";
    private const string Subjects = "/api/admin/subjects";

    /* =====================================================================
     *  1. RUXSAT (RBAC)
     * ================================================================== */

    [Fact]
    public async Task Tokensiz_401()
    {
        using var client = fixture.Api.AnonymousClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Groups)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync(Groups, new { name = "X" })).StatusCode);
    }

    /// <summary>
    /// Guruh — o'quv bo'limining ichki ma'lumoti: o'qituvchi ham, kassir ham
    /// unga umuman kira olmaydi (<c>AdminPermAttribute</c> ularni darvozadan
    /// o'tkazmaydi).
    /// </summary>
    [Theory]
    [InlineData(Roles.Teacher)]
    [InlineData(Roles.Cashier)]
    public async Task Oqituvchi_va_kassir_403(string role)
    {
        using var client = await fixture.Api.ClientAsAsync(role, "classes", "students");

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(Groups)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync(Groups, new { name = "X" })).StatusCode);
    }

    /// <summary>
    /// Xodim (staff): O'QISH har doim ochiq (bo'limlararo bog'liqlik uchun),
    /// YOZISH esa faqat <c>classes</c> kaliti bilan. Kalit yo'q xodim guruh
    /// yarata olmaydi, ro'yxatga bola qo'sha olmaydi va arxivlay olmaydi.
    /// </summary>
    [Fact]
    public async Task Xodim_oqiydi_lekin_classes_ruxsatisiz_yozmaydi()
    {
        var w = await SeedAsync();
        using var noPerm = await fixture.Api.ClientAsAsync(Roles.Staff);

        Assert.Equal(HttpStatusCode.OK, (await noPerm.GetAsync(Groups)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await noPerm.PostAsJsonAsync(Groups, Payload(w, "RBAC " + w.Tag))).StatusCode);

        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var group = await CreateAsync(admin, w, "RBAC-OK " + w.Tag, [w.StudentA]);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await noPerm.PostAsJsonAsync($"{Groups}/{group}/members",
                new { studentIds = new[] { w.StudentB } })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await noPerm.PostAsJsonAsync($"{Groups}/{group}/archive", new { })).StatusCode);

        // Kalit berilgan xodim — yoza oladi.
        using var withPerm = await fixture.Api.ClientAsAsync(Roles.Staff, "classes");
        Assert.Equal(HttpStatusCode.NoContent,
            (await withPerm.PostAsJsonAsync($"{Groups}/{group}/members",
                new { studentIds = new[] { w.StudentB } })).StatusCode);
    }

    /* =====================================================================
     *  2. FAN "GURUHLARGA BO'LINADI" (G-9)
     * ================================================================== */

    /// <summary>
    /// Mijoz, 2026-09-23: "istalgan fanni guruhga biriktirish mumkin". Ilgari guruh faqat
    /// <c>is_groupable</c> fanga ochilardi (G-9) — endi belgisiz fanga ham ochiladi.
    /// </summary>
    [Fact]
    public async Task Istalgan_fanga_guruh_ochiladi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var res = await admin.PostAsJsonAsync(Groups, new
        {
            name = "Oddiy fan " + w.Tag,
            subjectId = w.PlainSubject,
            classIds = new[] { w.ClassA },
            teacherIds = new[] { w.Teacher },
        });

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    /// <summary>
    /// Belgi endi qulf emas: faol guruhi bor fanning belgisini olib tashlash ham mumkin
    /// va guruh ishlashda davom etadi.
    /// </summary>
    [Fact]
    public async Task Fan_belgisi_guruhni_qulflamaydi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        await CreateAsync(admin, w, "Qulf " + w.Tag, [w.StudentA]);

        var off = await admin.PutAsJsonAsync($"{Subjects}/{w.Subject}",
            new { name = "Ingliz tili " + w.Tag, isGroupable = false });
        Assert.Equal(HttpStatusCode.OK, off.StatusCode);
    }

    /// <summary>
    /// F-1: ishlatilayotgan fanni o'chirib bo'lmaydi. Ilgari bu jim o'tardi va
    /// jurnal qatori "fansiz" qolardi.
    /// </summary>
    [Fact]
    public async Task Ishlatilayotgan_fan_ochirilmaydi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        await CreateAsync(admin, w, "F1 " + w.Tag, [w.StudentA]);

        var used = await admin.DeleteAsync($"{Subjects}/{w.Subject}");
        Assert.Equal(HttpStatusCode.Conflict, used.StatusCode);
        Assert.Contains("ishlatilmoqda", await MessageAsync(used), StringComparison.Ordinal);

        // Hech qayerda ishlatilmagan fan — o'chadi.
        var free = new Subject { Name = "Bo'sh fan " + w.Tag };
        await fixture.Api.WithDbAsync(async db => { db.Subjects.Add(free); await db.SaveChangesAsync(); });
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"{Subjects}/{free.Id}")).StatusCode);
    }

    /* =====================================================================
     *  3. QOIDA: BITTA FANDAN BITTA GURUH
     * ================================================================== */

    /// <summary>
    /// Ikkinchi guruhga qo'shish urinishi — 400 va O'ZBEKCHA xabar, 500 emas.
    /// Baza indeksi ham joyida turibdi (poyga uchun), lekin foydalanuvchi uni
    /// ko'rmaydi.
    /// </summary>
    [Fact]
    public async Task Bitta_fandan_ikkinchi_guruhga_qoshib_bolmaydi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        await CreateAsync(admin, w, "Kuchli " + w.Tag, [w.StudentA]);
        var second = await CreateAsync(admin, w, "Zaif " + w.Tag, []);

        var res = await admin.PostAsJsonAsync($"{Groups}/{second}/members",
            new { studentIds = new[] { w.StudentA } });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("bitta guruhda", await MessageAsync(res), StringComparison.Ordinal);

        // Baza tomonda ham AYNAN bitta faol a'zolik qoldi.
        Assert.Equal(1, await ActiveMembershipsAsync(w.StudentA, w.Subject));
    }

    /// <summary>
    /// Boshqa FAN bo'yicha ikkinchi guruh — mumkin. Qoida "bitta fandan bitta",
    /// "umuman bitta" emas.
    /// </summary>
    [Fact]
    public async Task Boshqa_fandan_ikkinchi_guruh_mumkin()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        await CreateAsync(admin, w, "Ingliz " + w.Tag, [w.StudentA]);
        var other = await CreateAsync(admin, w, "Matem " + w.Tag, [w.StudentA], subjectId: w.Subject2);

        Assert.Equal(1, await ActiveMembershipsAsync(w.StudentA, w.Subject));
        Assert.Equal(1, await ActiveMembershipsAsync(w.StudentA, w.Subject2));
        Assert.NotEqual(Guid.Empty, other);
    }

    /// <summary>
    /// Nomzodlar ro'yxati (chap panel): band o'quvchi <c>currentGroupId</c> bilan
    /// qaytadi — ekran uni KULRANG qiladi va tanlab bo'lmaydi.
    /// </summary>
    [Fact]
    public async Task Nomzodlar_royxati_band_oquvchini_belgilaydi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var first = await CreateAsync(admin, w, "Band " + w.Tag, [w.StudentA]);

        var rows = await admin.GetFromJsonAsync<List<JsonElement>>(
            $"{Groups}/candidates?classIds={w.ClassA},{w.ClassB}&subjectId={w.Subject}");

        var busy = Assert.Single(rows!, r => r.GetProperty("studentId").GetString() == w.StudentA);
        Assert.Equal(first, busy.GetProperty("currentGroupId").GetGuid());

        var free = Assert.Single(rows!, r => r.GetProperty("studentId").GetString() == w.StudentB);
        Assert.Equal(JsonValueKind.Null, free.GetProperty("currentGroupId").ValueKind);

        // Tahrirlanayotgan guruhning O'Z a'zosi band emas — u o'ng panelda turadi.
        var editing = await admin.GetFromJsonAsync<List<JsonElement>>(
            $"{Groups}/candidates?classIds={w.ClassA}&subjectId={w.Subject}&excludeGroupId={first}");
        var own = Assert.Single(editing!, r => r.GetProperty("studentId").GetString() == w.StudentA);
        Assert.Equal(JsonValueKind.Null, own.GetProperty("currentGroupId").ValueKind);
    }

    /// <summary>
    /// Guruhni boqmaydigan sinfning o'quvchisi ro'yxatga tushmaydi. EduSchool
    /// buni faqat tanlash oynasida ushlaydi — bizda server ham ushlaydi.
    /// </summary>
    [Fact]
    public async Task Guruhni_boqmaydigan_sinf_oquvchisi_qoshilmaydi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        // Guruhni faqat A sinf boqadi; StudentB esa B sinfda.
        var group = await CreateAsync(admin, w, "Faqat A " + w.Tag, [], classIds: [w.ClassA]);
        var res = await admin.PostAsJsonAsync($"{Groups}/{group}/members",
            new { studentIds = new[] { w.StudentB } });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("boqmaydi", await MessageAsync(res), StringComparison.Ordinal);
    }

    /// <summary>Jins bo'yicha cheklangan guruhga mos kelmaydigan bola qo'shilmaydi.</summary>
    [Fact]
    public async Task Jins_mos_kelmasa_qoshilmaydi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var group = await CreateAsync(admin, w, "Qizlar " + w.Tag, [], gender: "female");
        var res = await admin.PostAsJsonAsync($"{Groups}/{group}/members",
            new { studentIds = new[] { w.StudentA } });   // StudentA — male

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("jins", (await MessageAsync(res)).ToLowerInvariant(), StringComparison.Ordinal);
    }

    /* =====================================================================
     *  4. RO'YXAT AMALLARI
     * ================================================================== */

    /// <summary>
    /// Chiqarish — O'CHIRISH EMAS: qator sana va sabab bilan yopiladi, tarix
    /// qoladi. Shundan keyin bola boshqa guruhga bemalol qo'shiladi.
    /// </summary>
    [Fact]
    public async Task Chiqarish_sababni_yozadi_va_tarixni_saqlaydi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var group = await CreateAsync(admin, w, "Tarix " + w.Tag, [w.StudentA]);

        var members = await admin.GetFromJsonAsync<List<JsonElement>>($"{Groups}/{group}/members");
        var memberId = Assert.Single(members!).GetProperty("id").GetGuid();

        var res = await admin.PostAsJsonAsync($"{Groups}/{group}/members/{memberId}/remove",
            new { reason = "Boshqa maktabga ketdi" });
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);

        // Faol ro'yxat bo'sh, tarix esa saqlangan.
        var active = await admin.GetFromJsonAsync<List<JsonElement>>($"{Groups}/{group}/members");
        Assert.Empty(active!);

        var history = await admin.GetFromJsonAsync<List<JsonElement>>(
            $"{Groups}/{group}/members?includeHistory=true");
        var row = Assert.Single(history!);
        Assert.Equal("Boshqa maktabga ketdi", row.GetProperty("leaveReason").GetString());
        Assert.NotEqual(JsonValueKind.Null, row.GetProperty("leftOn").ValueKind);

        // Qoida bo'shadi — ikkinchi guruhga qo'shiladi.
        var second = await CreateAsync(admin, w, "Ikkinchi " + w.Tag, []);
        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.PostAsJsonAsync($"{Groups}/{second}/members",
                new { studentIds = new[] { w.StudentA } })).StatusCode);
    }

    /// <summary>
    /// O'tkazish: natijada shu fan bo'yicha AYNAN BITTA faol a'zolik qoladi va
    /// eski a'zolik yopilgan holda tarixda turadi.
    /// </summary>
    [Fact]
    public async Task Otkazish_bitta_faol_azolik_qoldiradi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var from = await CreateAsync(admin, w, "Manba " + w.Tag, [w.StudentA]);
        var to = await CreateAsync(admin, w, "Nishon " + w.Tag, []);

        var members = await admin.GetFromJsonAsync<List<JsonElement>>($"{Groups}/{from}/members");
        var memberId = Assert.Single(members!).GetProperty("id").GetGuid();

        var res = await admin.PostAsJsonAsync($"{Groups}/members/{memberId}/transfer",
            new { toGroupId = to });
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);

        Assert.Equal(1, await ActiveMembershipsAsync(w.StudentA, w.Subject));
        Assert.Empty((await admin.GetFromJsonAsync<List<JsonElement>>($"{Groups}/{from}/members"))!);
        Assert.Single((await admin.GetFromJsonAsync<List<JsonElement>>($"{Groups}/{to}/members"))!);

        await fixture.Api.WithDbAsync(async db =>
        {
            var closed = await db.StudyGroupMembers
                .SingleAsync(m => m.StudentId == w.StudentA && m.GroupId == from);
            Assert.NotNull(closed.LeftOn);
            Assert.False(string.IsNullOrWhiteSpace(closed.LeaveReason));
        });
    }

    /// <summary>Boshqa FANDAGI guruhga o'tkazish rad etiladi (§2.1.1 "Transfer").</summary>
    [Fact]
    public async Task Boshqa_fandagi_guruhga_otkazib_bolmaydi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var from = await CreateAsync(admin, w, "Ingliz manba " + w.Tag, [w.StudentA]);
        var other = await CreateAsync(admin, w, "Matem nishon " + w.Tag, [], subjectId: w.Subject2);

        var members = await admin.GetFromJsonAsync<List<JsonElement>>($"{Groups}/{from}/members");
        var memberId = Assert.Single(members!).GetProperty("id").GetGuid();

        var res = await admin.PostAsJsonAsync($"{Groups}/members/{memberId}/transfer",
            new { toGroupId = other });
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("SHU FANDAGI", await MessageAsync(res), StringComparison.Ordinal);
    }

    /* =====================================================================
     *  5. ARXIV VA NUSXA
     * ================================================================== */

    /// <summary>
    /// Arxivlash faol a'zoliklarni yopadi — aks holda bola arxivdagi guruh
    /// tufayli yangi guruhga qo'shila olmay qolardi.
    /// </summary>
    [Fact]
    public async Task Arxivlash_azoliklarni_yopadi_va_yangi_guruh_ochiladi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var group = await CreateAsync(admin, w, "Arxiv " + w.Tag, [w.StudentA]);

        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.PostAsJsonAsync($"{Groups}/{group}/archive", new { })).StatusCode);
        Assert.Equal(0, await ActiveMembershipsAsync(w.StudentA, w.Subject));

        // Arxiv ro'yxatida ko'rinadi, faol ro'yxatda yo'q.
        var active = await admin.GetFromJsonAsync<List<JsonElement>>(Groups);
        Assert.DoesNotContain(active!, g => g.GetProperty("id").GetGuid() == group);
        var archived = await admin.GetFromJsonAsync<List<JsonElement>>($"{Groups}?archived=true");
        Assert.Contains(archived!, g => g.GetProperty("id").GetGuid() == group);

        var fresh = await CreateAsync(admin, w, "Yangi yil " + w.Tag, [w.StudentA]);
        Assert.NotEqual(Guid.Empty, fresh);
    }

    /// <summary>
    /// Nusxalash: fan, sinflar va jins ko'chadi. FAOL guruhning ro'yxatini
    /// ko'chirish rad etiladi — aks holda bola bir vaqtda ikki guruhda
    /// bo'lardi. Arxivlangan guruhdan ko'chirish esa mumkin (yangi o'quv yili).
    /// </summary>
    [Fact]
    public async Task Nusxalash_fan_va_sinflarni_kochiradi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var source = await CreateAsync(admin, w, "Manba nusxa " + w.Tag, [w.StudentA]);

        var refused = await admin.PostAsJsonAsync($"{Groups}/{source}/duplicate",
            new { name = "Nusxa 1 " + w.Tag, copyMembers = true });
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);

        var empty = await admin.PostAsJsonAsync($"{Groups}/{source}/duplicate",
            new { name = "Nusxa 2 " + w.Tag });
        Assert.Equal(HttpStatusCode.OK, empty.StatusCode);
        var copy = JsonDocument.Parse(await empty.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(w.Subject, copy.GetProperty("subjectId").GetString());
        Assert.Equal(2, copy.GetProperty("classes").GetArrayLength());
        Assert.Empty(copy.GetProperty("members").EnumerateArray());

        // Arxivlangach — ro'yxati bilan nusxalanadi.
        await admin.PostAsJsonAsync($"{Groups}/{source}/archive", new { });
        var withMembers = await admin.PostAsJsonAsync($"{Groups}/{source}/duplicate",
            new { name = "Nusxa 3 " + w.Tag, copyMembers = true });
        Assert.Equal(HttpStatusCode.OK, withMembers.StatusCode);
        var third = JsonDocument.Parse(await withMembers.Content.ReadAsStringAsync()).RootElement;
        Assert.Single(third.GetProperty("members").EnumerateArray());
    }

    /// <summary>
    /// Tahrirlash: nom, sinf va o'qituvchi ro'yxati yangilanadi, ro'yxatdan
    /// olib tashlangan bola esa YOPILADI (o'chirilmaydi).
    ///
    /// <para>
    /// Alohida qo'riqlaydigan narsa: saqlashda O'ZGARMAGAN sinf qatoriga
    /// tegilmasligi kerak. "Hammasini o'chirib qaytadan qo'shish" bitta
    /// SaveChanges ichida birlamchi kalit to'qnashuvini berishi mumkin edi.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Tahrirlash_royxatni_moslaydi_va_ozgarmagan_sinfga_tegmaydi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var group = await CreateAsync(admin, w, "Tahrir " + w.Tag, [w.StudentA, w.StudentB]);

        // A sinf QOLADI (o'zgarmaydi), B sinf olib tashlanadi; ro'yxatda faqat A sinf bolasi.
        var res = await admin.PutAsJsonAsync($"{Groups}/{group}", new
        {
            name = "Tahrir 2 " + w.Tag,
            subjectId = w.Subject,
            classIds = new[] { w.ClassA },
            teacherIds = new[] { w.Teacher },
            studentIds = new[] { w.StudentA },
        });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var detail = JsonDocument.Parse(await res.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("Tahrir 2 " + w.Tag, detail.GetProperty("name").GetString());
        Assert.Single(detail.GetProperty("classes").EnumerateArray());
        var member = Assert.Single(detail.GetProperty("members").EnumerateArray());
        Assert.Equal(w.StudentA, member.GetProperty("studentId").GetString());

        // Chiqarilgan bola YOPILDI, o'chirilmadi — va endi boshqa guruhga ochiq.
        Assert.Equal(0, await ActiveMembershipsAsync(w.StudentB, w.Subject));
        await fixture.Api.WithDbAsync(async db =>
        {
            var closed = await db.StudyGroupMembers.AsNoTracking()
                .SingleAsync(m => m.StudentId == w.StudentB && m.GroupId == group);
            Assert.NotNull(closed.LeftOn);
        });
    }

    /// <summary>Bitta fan ichida bir xil nomli ikkita FAOL guruh bo'lmaydi.</summary>
    [Fact]
    public async Task Bir_fan_ichida_nom_takrorlanmaydi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var name = "Takror " + w.Tag;
        await CreateAsync(admin, w, name, []);

        var dup = await admin.PostAsJsonAsync(Groups, Payload(w, name.ToUpperInvariant()));
        Assert.Equal(HttpStatusCode.BadRequest, dup.StatusCode);
        Assert.Contains("boshqa nom", await MessageAsync(dup), StringComparison.Ordinal);
    }

    /* =====================================================================
     *  6. HECH NARSA O'ZGARMAYDI
     * ================================================================== */

    /// <summary>
    /// <b>Bu slice'ning eng muhim testi.</b> Guruh yaratish va ro'yxatini
    /// to'ldirish:
    /// <list type="bullet">
    ///   <item><c>group_lessons_enabled</c> ni YOQMAYDI;</item>
    ///   <item>bitta ham jadval shabloni, hafta biriktirishi yoki jurnal
    ///     qatorini yozmaydi;</item>
    ///   <item>beshta dars jadvalidagi <c>owner_kind</c> ni <c>'group'</c> ga
    ///     o'zgartirmaydi;</item>
    ///   <item>o'quvchining <c>class_name</c> va <c>sub_group</c> ustunlariga
    ///     TEGMAYDI — ya'ni jurnal, davomat va maosh bugungi ro'yxatni
    ///     o'zgarishsiz ko'radi.</item>
    /// </list>
    /// </summary>
    [Fact]
    public async Task Guruh_yaratish_darslarga_tegmaydi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var beforeTemplates = await CountAsync(db => db.ScheduleTemplates.CountAsync());
        var beforeWeeks = await CountAsync(db => db.WeekAssignments.CountAsync());
        var beforeJournal = await CountAsync(db => db.JournalEntries.CountAsync());

        await CreateAsync(admin, w, "Ta'sirsiz " + w.Tag, [w.StudentA, w.StudentB]);

        Assert.Equal(beforeTemplates, await CountAsync(db => db.ScheduleTemplates.CountAsync()));
        Assert.Equal(beforeWeeks, await CountAsync(db => db.WeekAssignments.CountAsync()));
        Assert.Equal(beforeJournal, await CountAsync(db => db.JournalEntries.CountAsync()));

        await fixture.Api.WithDbAsync(async db =>
        {
            var meta = await db.SchoolMeta.AsNoTracking().FirstOrDefaultAsync();
            Assert.False(meta?.GroupLessonsEnabled ?? false);

            Assert.Equal(0, await db.ScheduleTemplates.CountAsync(t => t.OwnerKind == LessonOwnerKind.Group));
            Assert.Equal(0, await db.WeekAssignments.CountAsync(x => x.OwnerKind == LessonOwnerKind.Group));
            Assert.Equal(0, await db.JournalEntries.CountAsync(e => e.OwnerKind == LessonOwnerKind.Group));
            Assert.Equal(0, await db.LessonNotes.CountAsync(n => n.OwnerKind == LessonOwnerKind.Group));
            Assert.Equal(0, await db.QuarterGrades.CountAsync(g => g.OwnerKind == LessonOwnerKind.Group));

            var a = await db.Students.AsNoTracking().SingleAsync(s => s.Id == w.StudentA);
            Assert.Equal(w.ClassAName, a.ClassName);
            Assert.Equal(0, a.SubGroup);
        });
    }

    /* =====================================================================
     *  Yordamchilar
     * ================================================================== */

    /// <summary>
    /// Ikki sinf, ikkita guruhli fan, bitta oddiy fan, bitta o'qituvchi va
    /// har sinfda bittadan o'quvchi. Har test o'z dunyosini yaratadi (umumiy
    /// baza, takrorlanmas teg) — testlar bir-birining qatorini ko'rmaydi.
    /// </summary>
    private async Task<World> SeedAsync()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];

        var classA = new SchoolClass { Name = $"G{tag}-A", Grade = 5 };
        var classB = new SchoolClass { Name = $"G{tag}-B", Grade = 5 };
        var subject = new Subject { Name = $"Ingliz tili {tag}", IsGroupable = true };
        var subject2 = new Subject { Name = $"Matematika {tag}", IsGroupable = true };
        var plain = new Subject { Name = $"Tarix {tag}", IsGroupable = false };
        var teacher = new Teacher { FullName = $"Guruh ustozi {tag}" };

        var a = GeneralSettingsFlagsTests.NewStudent($"Ali {tag}", classA.Name, "+99890" + Rnd());
        var b = GeneralSettingsFlagsTests.NewStudent($"Bobur {tag}", classB.Name, "+99890" + Rnd());

        await fixture.Api.WithDbAsync(async db =>
        {
            db.Classes.AddRange(classA, classB);
            db.Subjects.AddRange(subject, subject2, plain);
            db.Teachers.Add(teacher);
            db.Students.AddRange(a, b);
            await db.SaveChangesAsync();
        });

        return new World(tag, classA.Id, classA.Name, classB.Id, subject.Id, subject2.Id,
            plain.Id, teacher.Id, a.Id, b.Id);
    }

    private static object Payload(
        World w, string name, string? subjectId = null,
        IReadOnlyList<string>? classIds = null, string? gender = null,
        IReadOnlyList<string>? studentIds = null) => new
        {
            name,
            subjectId = subjectId ?? w.Subject,
            classIds = classIds ?? new[] { w.ClassA, w.ClassB },
            teacherIds = new[] { w.Teacher },
            gender,
            studentIds,
        };

    private static async Task<Guid> CreateAsync(
        HttpClient client, World w, string name, IReadOnlyList<string> studentIds,
        string? subjectId = null, IReadOnlyList<string>? classIds = null, string? gender = null)
    {
        var res = await client.PostAsJsonAsync(Groups,
            Payload(w, name, subjectId, classIds, gender, studentIds));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return JsonDocument.Parse(await res.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetGuid();
    }

    private Task<int> ActiveMembershipsAsync(string studentId, string subjectId) =>
        CountAsync(db => db.StudyGroupMembers.CountAsync(
            m => m.StudentId == studentId && m.SubjectId == subjectId && m.LeftOn == null));

    private async Task<int> CountAsync(Func<SchoolLms.Infrastructure.Data.AppDbContext, Task<int>> query)
    {
        var count = 0;
        await fixture.Api.WithDbAsync(async db => count = await query(db));
        return count;
    }

    private static async Task<string> MessageAsync(HttpResponseMessage res)
    {
        var body = await res.Content.ReadAsStringAsync();
        return JsonDocument.Parse(body).RootElement.TryGetProperty("message", out var m)
            ? m.GetString() ?? body
            : body;
    }

    private static string Rnd() => Random.Shared.Next(1_000_000, 9_999_999).ToString();

    private sealed record World(
        string Tag, string ClassA, string ClassAName, string ClassB,
        string Subject, string Subject2, string PlainSubject,
        string Teacher, string StudentA, string StudentB);
}
