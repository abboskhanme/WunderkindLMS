using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// Sinf ro'yxati (docs/modules/students-parity.md §2.2, C-1/C-2).
///
/// <para>
/// <b>Bu testlarning yagona mavzusi — IKKI USTUN BIRGA YURADI.</b>
/// Bugun o'quvchini sinfga bog'laydigan narsa <c>students.class_name</c>
/// (NOM), va butun tizim — jurnal, davomat, chat, hisobotlar, maosh — aynan
/// shu ustunni o'qiydi. Yangi <c>class_memberships</c> esa sanali tarix.
/// Agar bittasi o'zgarib, ikkinchisi qolib ketsa, sinf ro'yxati bilan jurnal
/// ro'yxati boshqa-boshqa bola ko'rsata boshlaydi va qaysi biri to'g'riligini
/// hech kim ayta olmaydi. Shuning uchun HAR bir amaldan keyin ikkalasi ham
/// tekshiriladi.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class ClassMembershipTests(ApiFixture fixture)
{
    private const string Roster = "/api/admin/class-roster";
    private const string Students = "/api/admin/students";
    private const string Groups = "/api/admin/study-groups";

    /* =====================================================================
     *  1. RUXSAT (RBAC)
     * ================================================================== */

    [Fact]
    public async Task Tokensiz_401()
    {
        using var client = fixture.Api.AnonymousClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"{Roster}/x")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await client.PostAsJsonAsync($"{Roster}/x/members", new { studentId = "y" })).StatusCode);
    }

    [Theory]
    [InlineData(Roles.Teacher)]
    [InlineData(Roles.Cashier)]
    public async Task Oqituvchi_va_kassir_403(string role)
    {
        var w = await SeedAsync();
        using var client = await fixture.Api.ClientAsAsync(role, "classes", "students");

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"{Roster}/{w.ClassA}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.PostAsJsonAsync($"{Roster}/{w.ClassA}/members",
                new { studentId = w.Loose })).StatusCode);
    }

    /// <summary>
    /// Xodim: ro'yxatni O'QIYDI, lekin <c>classes</c> kalitisiz bola qo'sha
    /// olmaydi, chiqara olmaydi va o'tkaza olmaydi.
    /// </summary>
    [Fact]
    public async Task Xodim_oqiydi_lekin_classes_ruxsatisiz_yozmaydi()
    {
        var w = await SeedAsync();
        using var noPerm = await fixture.Api.ClientAsAsync(Roles.Staff);

        Assert.Equal(HttpStatusCode.OK, (await noPerm.GetAsync($"{Roster}/{w.ClassA}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await noPerm.PostAsJsonAsync($"{Roster}/{w.ClassA}/members",
                new { studentId = w.Loose })).StatusCode);

        var membershipId = await MembershipIdAsync(w.StudentA);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await noPerm.PostAsJsonAsync($"{Roster}/members/{membershipId}/remove",
                new { reason = "test" })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await noPerm.PostAsJsonAsync($"{Roster}/members/{membershipId}/transfer",
                new { toClassId = w.ClassB })).StatusCode);

        using var withPerm = await fixture.Api.ClientAsAsync(Roles.Staff, "classes");
        Assert.Equal(HttpStatusCode.NoContent,
            (await withPerm.PostAsJsonAsync($"{Roster}/{w.ClassA}/members",
                new { studentId = w.Loose })).StatusCode);
    }

    /* =====================================================================
     *  2. QO'SHISH
     * ================================================================== */

    /// <summary>
    /// Sinfga qo'shish <c>class_name</c> ni VA a'zolik yozuvini birga yozadi.
    /// Sinfli o'quvchi nomzodlar ro'yxatida ko'rinmaydi (§2.2.1: faqat sinfsiz).
    /// </summary>
    [Fact]
    public async Task Qoshish_class_name_va_azolikni_birga_yozadi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var candidates = await admin.GetFromJsonAsync<List<JsonElement>>($"{Roster}/{w.ClassA}/candidates");
        Assert.Contains(candidates!, c => c.GetProperty("studentId").GetString() == w.Loose);
        Assert.DoesNotContain(candidates!, c => c.GetProperty("studentId").GetString() == w.StudentA);

        var res = await admin.PostAsJsonAsync($"{Roster}/{w.ClassA}/members", new { studentId = w.Loose });
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);

        await fixture.Api.WithDbAsync(async db =>
        {
            var student = await db.Students.AsNoTracking().SingleAsync(s => s.Id == w.Loose);
            Assert.Equal(w.ClassAName, student.ClassName);

            var membership = await db.ClassMemberships.AsNoTracking()
                .SingleAsync(m => m.StudentId == w.Loose && m.LeftOn == null);
            Assert.Equal(w.ClassA, membership.ClassId);
        });

        // Endi u nomzod emas va ro'yxatda ko'rinadi.
        var roster = await admin.GetFromJsonAsync<JsonElement>($"{Roster}/{w.ClassA}");
        Assert.Contains(roster.GetProperty("students").EnumerateArray(),
            r => r.GetProperty("studentId").GetString() == w.Loose);
    }

    /// <summary>Sinfi bor o'quvchini ikkinchi sinfga qo'shib bo'lmaydi — bu o'tkazish amali.</summary>
    [Fact]
    public async Task Sinfli_oquvchi_ikkinchi_sinfga_qoshilmaydi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var res = await admin.PostAsJsonAsync($"{Roster}/{w.ClassB}/members", new { studentId = w.StudentA });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("boshqa sinfda", await MessageAsync(res), StringComparison.Ordinal);
        Assert.Equal(1, await ActiveClassMembershipsAsync(w.StudentA));
    }

    /* =====================================================================
     *  3. CHIQARISH
     * ================================================================== */

    /// <summary>
    /// Chiqarish: sabab MAJBURIY, a'zolik yopiladi (o'chirilmaydi),
    /// <c>class_name</c> bo'shaydi va tarix qoladi.
    /// </summary>
    [Fact]
    public async Task Chiqarish_sababni_talab_qiladi_va_tarixni_saqlaydi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var membershipId = await MembershipIdAsync(w.StudentA);

        var noReason = await admin.PostAsJsonAsync($"{Roster}/members/{membershipId}/remove",
            new { reason = "   " });
        Assert.Equal(HttpStatusCode.BadRequest, noReason.StatusCode);

        var ok = await admin.PostAsJsonAsync($"{Roster}/members/{membershipId}/remove",
            new { reason = "Ko'chib ketdi" });
        Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);

        await fixture.Api.WithDbAsync(async db =>
        {
            var student = await db.Students.AsNoTracking().SingleAsync(s => s.Id == w.StudentA);
            Assert.Equal("", student.ClassName);

            var row = await db.ClassMemberships.AsNoTracking().SingleAsync(m => m.Id == membershipId);
            Assert.NotNull(row.LeftOn);
            Assert.Equal("Ko'chib ketdi", row.LeaveReason);
        });

        // Bo'shagan bola yana nomzod bo'ladi.
        var candidates = await admin.GetFromJsonAsync<List<JsonElement>>($"{Roster}/{w.ClassB}/candidates");
        Assert.Contains(candidates!, c => c.GetProperty("studentId").GetString() == w.StudentA);
    }

    /* =====================================================================
     *  4. O'TKAZISH
     * ================================================================== */

    /// <summary>
    /// O'tkazish: AYNAN BITTA faol a'zolik qoladi, <c>class_name</c> yangi
    /// sinfga o'tadi, eskisi esa sabab bilan tarixda turadi.
    /// </summary>
    [Fact]
    public async Task Otkazish_class_name_va_azolikni_birga_kochiradi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var membershipId = await MembershipIdAsync(w.StudentA);

        var res = await admin.PostAsJsonAsync($"{Roster}/members/{membershipId}/transfer",
            new { toClassId = w.ClassB });
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);

        Assert.Equal(1, await ActiveClassMembershipsAsync(w.StudentA));
        await fixture.Api.WithDbAsync(async db =>
        {
            var student = await db.Students.AsNoTracking().SingleAsync(s => s.Id == w.StudentA);
            Assert.Equal(w.ClassBName, student.ClassName);

            var active = await db.ClassMemberships.AsNoTracking()
                .SingleAsync(m => m.StudentId == w.StudentA && m.LeftOn == null);
            Assert.Equal(w.ClassB, active.ClassId);

            var closed = await db.ClassMemberships.AsNoTracking()
                .SingleAsync(m => m.Id == membershipId);
            Assert.NotNull(closed.LeftOn);
            Assert.False(string.IsNullOrWhiteSpace(closed.LeaveReason));
        });
    }

    /// <summary>
    /// Boshqa DARAJADAGI sinfga o'tkazib bo'lmaydi (§2.2.1:
    /// <c>class/pagin?grades=[same grade]</c>). 5-sinfdan 7-sinfga ko'chirish —
    /// o'quv yilini yakunlash amali, ro'yxat tugmasi emas.
    /// </summary>
    [Fact]
    public async Task Boshqa_darajaga_otkazib_bolmaydi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var membershipId = await MembershipIdAsync(w.StudentA);

        var res = await admin.PostAsJsonAsync($"{Roster}/members/{membershipId}/transfer",
            new { toClassId = w.ClassOtherGrade });

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
        Assert.Contains("DARAJADAGI", await MessageAsync(res), StringComparison.Ordinal);
        await fixture.Api.WithDbAsync(async db =>
        {
            var student = await db.Students.AsNoTracking().SingleAsync(s => s.Id == w.StudentA);
            Assert.Equal(w.ClassAName, student.ClassName);
        });
    }

    /// <summary>
    /// "Guruhlarda qolsin" (sukut bo'yicha YOQIQ) — o'tkazishda guruh
    /// a'zoligiga tegilmaydi. O'chirilsa, YANGI sinf boqmaydigan guruhlardagi
    /// a'zolik yopiladi, boqadigani esa qoladi.
    /// </summary>
    [Fact]
    public async Task Guruhlarda_qolsin_bayrogi_ishlaydi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        // Faqat A sinfni boqadigan guruh + ikkala sinfni boqadigan guruh.
        var onlyA = await CreateGroupAsync(admin, w, "Faqat A " + w.Tag, w.Subject, [w.ClassA], [w.StudentA]);
        var both = await CreateGroupAsync(admin, w, "A va B " + w.Tag, w.Subject2,
            [w.ClassA, w.ClassB], [w.StudentA]);

        // 1) Sukut (keepGroups=true) — B sinfga o'tkazamiz, ikkala a'zolik ham qoladi,
        //    garchi "Faqat A" guruhini yangi sinf boqmasa ham.
        var membershipId = await MembershipIdAsync(w.StudentA);
        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.PostAsJsonAsync($"{Roster}/members/{membershipId}/transfer",
                new { toClassId = w.ClassB })).StatusCode);
        Assert.Equal(2, await ActiveGroupMembershipsAsync(w.StudentA));

        // 2) A ga qaytaramiz (bayroq baribir sukutda) — hech narsa o'zgarmaydi.
        var backId = await MembershipIdAsync(w.StudentA);
        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.PostAsJsonAsync($"{Roster}/members/{backId}/transfer",
                new { toClassId = w.ClassA })).StatusCode);
        Assert.Equal(2, await ActiveGroupMembershipsAsync(w.StudentA));

        // 3) Endi keepGroups=false bilan B ga — B "Faqat A" ni BOQMAYDI, shuning
        //    uchun o'sha a'zolik yopiladi; ikkala sinfni boqadigani qoladi.
        var offId = await MembershipIdAsync(w.StudentA);
        Assert.Equal(HttpStatusCode.NoContent,
            (await admin.PostAsJsonAsync($"{Roster}/members/{offId}/transfer",
                new { toClassId = w.ClassB, keepGroups = false })).StatusCode);

        await fixture.Api.WithDbAsync(async db =>
        {
            var rows = await db.StudyGroupMembers.AsNoTracking()
                .Where(m => m.StudentId == w.StudentA && m.LeftOn == null).ToListAsync();
            var open = Assert.Single(rows);
            Assert.Equal(both, open.GroupId);
            Assert.NotEqual(onlyA, open.GroupId);
        });
    }

    /* =====================================================================
     *  5. O'QUVCHI FORMASIDAN KELGAN SINF O'ZGARISHI
     * ================================================================== */

    /// <summary>
    /// <c>PUT /students/{id}</c> dagi sinf o'zgarishi ham a'zolikda qoladi
    /// (C-1: "sinf o'zgarishi shu xizmatdan o'tadi"). Forma bugungiday
    /// ishlashda davom etadi — yagona yangilik shu.
    /// </summary>
    [Fact]
    public async Task Oquvchi_formasidagi_sinf_ozgarishi_azolikda_qoladi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var res = await admin.PutAsJsonAsync($"{Students}/{w.StudentA}", new
        {
            fullName = "Ali " + w.Tag,
            birthDate = "2012-01-01",
            address = "Toshkent",
            gender = "male",
            parentFullName = "Ota-ona",
            parentPhone = "+99890" + Random.Shared.Next(1_000_000, 9_999_999),
            className = w.ClassBName,
            enrollmentDate = "2025-09-01",
        });
        Assert.Equal(HttpStatusCode.NoContent, res.StatusCode);

        Assert.Equal(1, await ActiveClassMembershipsAsync(w.StudentA));
        await fixture.Api.WithDbAsync(async db =>
        {
            var student = await db.Students.AsNoTracking().SingleAsync(s => s.Id == w.StudentA);
            Assert.Equal(w.ClassBName, student.ClassName);

            var active = await db.ClassMemberships.AsNoTracking()
                .SingleAsync(m => m.StudentId == w.StudentA && m.LeftOn == null);
            Assert.Equal(w.ClassB, active.ClassId);

            var closed = await db.ClassMemberships.AsNoTracking()
                .SingleAsync(m => m.StudentId == w.StudentA && m.LeftOn != null);
            Assert.Equal(w.ClassA, closed.ClassId);
        });
    }

    /* =====================================================================
     *  6. O'QUVCHI KARTOCHKASI — "Sinf va guruhlar" (G-10)
     * ================================================================== */

    /// <summary>
    /// Kartochka tab'i sinf va guruh a'zoliklarini TARIXI bilan qaytaradi:
    /// faol qatorlar birinchi, har birida kunlar soni va chiqish sababi.
    /// </summary>
    [Fact]
    public async Task Kartochka_sinf_va_guruh_tarixini_qaytaradi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        await CreateGroupAsync(admin, w, "Kartochka " + w.Tag, w.Subject, [w.ClassA], [w.StudentA]);

        var membershipId = await MembershipIdAsync(w.StudentA);
        await admin.PostAsJsonAsync($"{Roster}/members/{membershipId}/transfer",
            new { toClassId = w.ClassB });

        var card = await admin.GetFromJsonAsync<JsonElement>($"{Students}/{w.StudentA}/memberships");

        Assert.Equal(w.ClassBName, card.GetProperty("className").GetString());

        var classes = card.GetProperty("classes").EnumerateArray().ToList();
        Assert.Equal(2, classes.Count);
        Assert.Equal(JsonValueKind.Null, classes[0].GetProperty("leftOn").ValueKind);  // faol birinchi
        Assert.True(classes[0].GetProperty("days").GetInt32() >= 1);
        Assert.NotEqual(JsonValueKind.Null, classes[1].GetProperty("leftOn").ValueKind);

        var groups = card.GetProperty("groups").EnumerateArray().ToList();
        var group = Assert.Single(groups);
        Assert.Equal("Kartochka " + w.Tag, group.GetProperty("groupName").GetString());
        Assert.False(group.GetProperty("groupIsArchived").GetBoolean());
    }

    /* =====================================================================
     *  Yordamchilar
     * ================================================================== */

    /// <summary>
    /// Bir darajadagi ikki sinf, boshqa darajadagi uchinchisi, ikkita guruhli
    /// fan, o'qituvchi, sinfli o'quvchi (a'zolik yozuvi bilan) va SINFSIZ
    /// o'quvchi.
    /// </summary>
    private async Task<World> SeedAsync()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];

        var classA = new SchoolClass { Name = $"C{tag}-A", Grade = 5 };
        var classB = new SchoolClass { Name = $"C{tag}-B", Grade = 5 };
        var classC = new SchoolClass { Name = $"C{tag}-C", Grade = 7 };
        var subject = new Subject { Name = $"Ingliz {tag}", IsGroupable = true };
        var subject2 = new Subject { Name = $"Matem {tag}", IsGroupable = true };
        var teacher = new Teacher { FullName = $"Ustoz {tag}" };

        var a = GeneralSettingsFlagsTests.NewStudent($"Ali {tag}", classA.Name, Phone());
        var loose = GeneralSettingsFlagsTests.NewStudent($"Sinfsiz {tag}", "", Phone());

        await fixture.Api.WithDbAsync(async db =>
        {
            db.Classes.AddRange(classA, classB, classC);
            db.Subjects.AddRange(subject, subject2);
            db.Teachers.Add(teacher);
            db.Students.AddRange(a, loose);
            // Migratsiya backfill'i qiladigan narsa: sinfli o'quvchining ochiq
            // a'zolik qatori. Testda uni qo'lda qo'yamiz, chunki o'quvchilar
            // migratsiyadan KEYIN yaratilgan.
            db.ClassMemberships.Add(new ClassMembership
            {
                StudentId = a.Id, ClassId = classA.Id, JoinedOn = AppClock.Today,
            });
            await db.SaveChangesAsync();
        });

        return new World(tag, classA.Id, classA.Name, classB.Id, classB.Name, classC.Id,
            subject.Id, subject2.Id, teacher.Id, a.Id, loose.Id);
    }

    private static async Task<Guid> CreateGroupAsync(
        HttpClient client, World w, string name, string subjectId,
        IReadOnlyList<string> classIds, IReadOnlyList<string> studentIds)
    {
        var res = await client.PostAsJsonAsync(Groups, new
        {
            name,
            subjectId,
            classIds,
            teacherIds = new[] { w.Teacher },
            studentIds,
        });
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        return JsonDocument.Parse(await res.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetGuid();
    }

    private async Task<Guid> MembershipIdAsync(string studentId)
    {
        var id = Guid.Empty;
        await fixture.Api.WithDbAsync(async db =>
            id = (await db.ClassMemberships.AsNoTracking()
                .SingleAsync(m => m.StudentId == studentId && m.LeftOn == null)).Id);
        return id;
    }

    private async Task<int> ActiveClassMembershipsAsync(string studentId)
    {
        var count = 0;
        await fixture.Api.WithDbAsync(async db =>
            count = await db.ClassMemberships.CountAsync(m => m.StudentId == studentId && m.LeftOn == null));
        return count;
    }

    private async Task<int> ActiveGroupMembershipsAsync(string studentId)
    {
        var count = 0;
        await fixture.Api.WithDbAsync(async db =>
            count = await db.StudyGroupMembers.CountAsync(m => m.StudentId == studentId && m.LeftOn == null));
        return count;
    }

    private static async Task<string> MessageAsync(HttpResponseMessage res)
    {
        var body = await res.Content.ReadAsStringAsync();
        return JsonDocument.Parse(body).RootElement.TryGetProperty("message", out var m)
            ? m.GetString() ?? body
            : body;
    }

    private static string Phone() => "+99890" + Random.Shared.Next(1_000_000, 9_999_999);

    private sealed record World(
        string Tag,
        string ClassA, string ClassAName,
        string ClassB, string ClassBName,
        string ClassOtherGrade,
        string Subject, string Subject2,
        string Teacher, string StudentA, string Loose);
}
