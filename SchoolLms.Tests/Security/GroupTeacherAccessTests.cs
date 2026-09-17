using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Auth;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests.Security;

// ===========================================================================
//  G-12 / G-17 — GURUH O'QITUVCHISI QAYERGACHA YETADI, VA GURUH CHATI
//  (docs/modules/students-parity.md §2.1.6, §4.3)
// ===========================================================================
//
//  `Security/TeacherAccessTests` bugungi chegarani qadab qo'ygan: FAQAT
//  guruhda dars beradigan o'qituvchi hamma joyda 403 oladi. Bu fayl o'sha
//  chegaraning YANGI yarmini yozadi:
//
//    · o'chirgich (`group_lessons_enabled`) O'CHIQ ekan — javob BUGUNGIDEK
//      403 (guruh darsi hali yo'q);
//    · yoqilgach — o'qituvchi FAQAT O'Z guruhiga yetadi, begonasiga baribir
//      403.
//
//  Darvoza "ochilmoqda", "hammaga ochilmayapti" — shuning uchun har test
//  ruxsat etilgan yo'lni ham, rad etilgan yo'lni ham tekshiradi.
// ===========================================================================

/// <summary>
/// Guruh o'qituvchisining jurnal, ro'yxat va chat chegarasi — uch yuzada
/// (admin, o'qituvchi web, Telegram Mini App). Batafsil — fayl boshidagi izoh.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class GroupTeacherAccessTests(ApiFixture fixture) : IAsyncLifetime
{
    /// <summary>
    /// Shu test yaratgan o'quv guruhlarining id'lari — <see cref="DisposeAsync"/>
    /// ularga tegishli HAMMA narsani tozalaydi.
    ///
    /// <para>
    /// <b>Nega tozalash SHART.</b> Baza umumiy (jarayonda bir vaqtda faqat
    /// BITTA <c>ApiFactory</c> bo'la oladi), bu testlar esa guruh jadvalini va
    /// guruh jurnal qatorini yozadi. Qo'shni test klassi —
    /// <c>StudyGroupTests.Guruh_yaratish_darslarga_tegmaydi</c> — butun bazada
    /// <c>owner_kind='group'</c> qatori YO'Qligini talab qiladi, ya'ni ortimizdan
    /// qolgan qator uni yiqitardi. Faqat O'ZIMIZ yaratgan egalar o'chiriladi:
    /// boshqa klassning qatoriga tegmaymiz.
    /// </para>
    /// </summary>
    private readonly List<string> _createdGroupIds = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_createdGroupIds.Count == 0) return;
        var ids = _createdGroupIds;

        await fixture.Api.WithDbAsync(async db =>
        {
            // Shablonlar — darslari bilan (kuzatuv orqali, kaskad ishlashi uchun).
            var templates = await db.ScheduleTemplates.Include(t => t.Lessons)
                .Where(t => ids.Contains(t.ClassId)).ToListAsync();
            db.ScheduleTemplates.RemoveRange(templates);

            db.WeekAssignments.RemoveRange(
                await db.WeekAssignments.Where(x => ids.Contains(x.ClassId)).ToListAsync());
            db.JournalEntries.RemoveRange(
                await db.JournalEntries.Where(x => ids.Contains(x.ClassId)).ToListAsync());
            db.LessonNotes.RemoveRange(
                await db.LessonNotes.Where(x => ids.Contains(x.ClassId)).ToListAsync());
            db.QuarterGrades.RemoveRange(
                await db.QuarterGrades.Where(x => ids.Contains(x.ClassId)).ToListAsync());
            await db.SaveChangesAsync();
        });
    }

    private const string Switch = "/api/admin/group-lessons";
    private const string TeacherJournal = "/api/teacher/journal";
    private const string TgRoster = "/api/tg/teacher/roster";
    private const string AdminOwners = "/api/admin/journal/owners";

    private static string Past => AppClock.Today.AddDays(-1).ToString("yyyy-MM-dd");

    /* =====================================================================
     *  1. Jurnal — o'chirgichdan oldin va keyin
     * ================================================================== */

    /// <summary>
    /// <b>ESKI: 403. YANGI: 200.</b>
    ///
    /// <para>
    /// Faqat guruhda dars beradigan o'qituvchi bugun o'z jurnaliga kira
    /// olmaydi (<c>TeachesClass</c> <c>db.Classes</c> dan qidiradi va guruhni
    /// topmaydi). O'chirgich yoqilgach u o'z guruhining jurnalini ochadi va
    /// katak yoza oladi.
    /// </para>
    /// </summary>
    [Fact]
    public async Task Guruh_oqituvchisi_ochirgichgacha_403_keyin_jurnalni_ochadi()
    {
        var w = await SeedAsync();
        using var teacher = w.GroupTeacherClient;

        Assert.Equal(HttpStatusCode.Forbidden,
            (await teacher.GetAsync(Query(TeacherJournal, w.GroupId, w.EnglishId))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await teacher.GetAsync($"{TeacherJournal}/students?classId={w.GroupId}")).StatusCode);

        await WithGroupLessonsAsync(async () =>
        {
            Assert.Equal(HttpStatusCode.OK,
                (await teacher.GetAsync(Query(TeacherJournal, w.GroupId, w.EnglishId))).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent,
                (await PutTeacherEntry(teacher, w)).StatusCode);

            // Ro'yxat — guruhning FAOL a'zolari, ikkala boquvchi sinfdan.
            var ids = await StudentIdsAsync(teacher, $"{TeacherJournal}/students?classId={w.GroupId}");
            Assert.Contains(w.A1, ids);
            Assert.Contains(w.B1, ids);
            Assert.DoesNotContain(w.A2, ids); // 5-A da, lekin guruhda emas
        });

        // O'chirgich qaytarilgach — yana bugungi javob.
        Assert.Equal(HttpStatusCode.Forbidden,
            (await teacher.GetAsync(Query(TeacherJournal, w.GroupId, w.EnglishId))).StatusCode);
    }

    /// <summary>
    /// Begona guruh — o'chirgich yoqiq bo'lsa ham 403. Darvoza "ochildi",
    /// "hammaga ochildi" emas.
    /// </summary>
    [Fact]
    public async Task Begona_guruh_ochirgich_yoqilganda_ham_403()
    {
        var w = await SeedAsync();
        using var teacher = w.GroupTeacherClient;

        await WithGroupLessonsAsync(async () =>
        {
            Assert.Equal(HttpStatusCode.Forbidden,
                (await teacher.GetAsync(Query(TeacherJournal, w.OtherGroupId, w.EnglishId))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await teacher.GetAsync($"{TeacherJournal}/students?classId={w.OtherGroupId}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await teacher.GetAsync(Roster(w.OtherGroupId, w.EnglishId))).StatusCode);
        });
    }

    /// <summary>
    /// Guruhga BIRIKTIRILGAN (lekin jadvalda darsi yo'q) o'qituvchi ro'yxatni
    /// ko'radi, jurnal KATAKLARIGA esa yetmaydi — bu SINF RAHBARI uchun bugun
    /// amal qilayotgan nomutanosiblikning aynan o'zi
    /// (<c>TeacherAccessTests.Sinf_rahbari_royxatni_koradi_jurnalni_esa_yoq…</c>).
    /// </summary>
    [Fact]
    public async Task Biriktirilgan_guruh_oqituvchisi_royxatni_koradi_jurnalni_esa_yoq()
    {
        var w = await SeedAsync();
        using var attached = w.AttachedTeacherClient;

        await WithGroupLessonsAsync(async () =>
        {
            Assert.Equal(HttpStatusCode.OK,
                (await attached.GetAsync($"{TeacherJournal}/students?classId={w.GroupId}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await attached.GetAsync(Query(TeacherJournal, w.GroupId, w.EnglishId))).StatusCode);
        });
    }

    /// <summary>
    /// "Jurnal" bo'lim ruxsati guruhda ham SHART — dars berish o'zi yetarli emas.
    /// </summary>
    [Fact]
    public async Task Jurnal_ruxsatisiz_guruh_oqituvchisi_ham_403()
    {
        var w = await SeedAsync(groupTeacherPermissions: [TeacherPermissions.Schedule]);
        using var teacher = w.GroupTeacherClient;

        await WithGroupLessonsAsync(async () =>
        {
            Assert.Equal(HttpStatusCode.Forbidden,
                (await teacher.GetAsync(Query(TeacherJournal, w.GroupId, w.EnglishId))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await teacher.GetAsync(Roster(w.GroupId, w.EnglishId))).StatusCode);
        });
    }

    /// <summary>
    /// O'qituvchining egalar ro'yxati: o'chirgichgacha guruh YO'Q, keyin
    /// <c>ownerKind="group"</c> bilan paydo bo'ladi.
    /// </summary>
    [Fact]
    public async Task Oqituvchining_egalar_royxatiga_guruh_faqat_yoqilganda_qoshiladi()
    {
        var w = await SeedAsync();
        using var teacher = w.GroupTeacherClient;

        Assert.DoesNotContain(w.GroupId, await OwnerIdsAsync(teacher, "/api/teacher/classes"));

        await WithGroupLessonsAsync(async () =>
        {
            var body = await ArrayAsync(teacher, "/api/teacher/classes");
            var row = Assert.Single(body, x => x.GetProperty("classId").GetString() == w.GroupId);
            Assert.Equal(LessonOwnerKind.Group, row.GetProperty("ownerKind").GetString());
            Assert.Equal(w.GroupName, row.GetProperty("className").GetString());
            Assert.Equal(0, row.GetProperty("grade").GetInt32());
        });
    }

    /* =====================================================================
     *  2. Telegram Mini App yo'qlamasi
     * ================================================================== */

    /// <summary>
    /// Mini App yo'qlamasi guruh uchun ham ishlaydi — lekin FAQAT o'chirgich
    /// yoqilganda. Javobda ega turi va guruh nomi qaytadi.
    /// </summary>
    [Fact]
    public async Task Mini_app_yoqlamasi_guruhda_ochirgichdan_keyin_ishlaydi()
    {
        var w = await SeedAsync();
        using var teacher = w.GroupTeacherClient;

        Assert.Equal(HttpStatusCode.Forbidden,
            (await teacher.GetAsync(Roster(w.GroupId, w.EnglishId))).StatusCode);

        await WithGroupLessonsAsync(async () =>
        {
            var response = await teacher.GetAsync(Roster(w.GroupId, w.EnglishId));
            Assert.True(response.StatusCode == HttpStatusCode.OK,
                $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(LessonOwnerKind.Group, body.RootElement.GetProperty("ownerKind").GetString());
            Assert.Equal(w.GroupName, body.RootElement.GetProperty("className").GetString());

            var ids = body.RootElement.GetProperty("students").EnumerateArray()
                .Select(s => s.GetProperty("studentId").GetString()).ToList();
            Assert.Contains(w.A1, ids);
            Assert.Contains(w.B1, ids);
            Assert.DoesNotContain(w.A2, ids);
        });
    }

    /// <summary>
    /// Guruh darsida sinf ichidagi 1/2-bo'linish YO'Q — server <c>subGroup=1</c>
    /// ni 400 bilan rad etadi (§2.1.4).
    /// </summary>
    [Fact]
    public async Task Mini_app_guruh_yoqlamasida_subgroup_rad_etiladi()
    {
        var w = await SeedAsync();
        using var teacher = w.GroupTeacherClient;

        await WithGroupLessonsAsync(async () =>
        {
            var response = await teacher.GetAsync($"{Roster(w.GroupId, w.EnglishId)}&subGroup=1");
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        });
    }

    /* =====================================================================
     *  3. Admin jurnali — egalar va ro'yxat
     * ================================================================== */

    /// <summary>
    /// Admin jurnalining tanlagichi: o'chirgichgacha faqat sinflar, keyin
    /// guruhlar ham. Ro'yxat esa serverdan — guruhda BIR NECHA sinfdan
    /// yig'ilgan bolalar.
    /// </summary>
    [Fact]
    public async Task Admin_jurnalining_egalari_va_guruh_royxati()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        Assert.DoesNotContain(w.GroupId, await OwnerIdsAsync(admin, AdminOwners, "id"));
        Assert.Empty(await StudentIdsAsync(admin, $"/api/admin/journal/students?classId={w.GroupId}"));

        await WithGroupLessonsAsync(async () =>
        {
            var owners = await ArrayAsync(admin, AdminOwners);
            var row = Assert.Single(owners, x => x.GetProperty("id").GetString() == w.GroupId);
            Assert.Equal(LessonOwnerKind.Group, row.GetProperty("kind").GetString());
            Assert.Equal(w.EnglishId, row.GetProperty("subjectId").GetString());
            Assert.Equal(2, row.GetProperty("studentCount").GetInt32());

            var ids = await StudentIdsAsync(admin, $"/api/admin/journal/students?classId={w.GroupId}");
            Assert.Equal(2, ids.Count);
            Assert.Contains(w.A1, ids);
            Assert.Contains(w.B1, ids);

            // Sinf ro'yxati bugungidek — sinf nomi bo'yicha.
            var classIds = await StudentIdsAsync(admin, $"/api/admin/journal/students?classId={w.ClassAId}");
            Assert.Contains(w.A1, classIds);
            Assert.Contains(w.A2, classIds);
            Assert.DoesNotContain(w.B1, classIds);
        });
    }

    /// <summary>O'qituvchi admin jurnalining egalar ro'yxatiga ham yeta olmaydi.</summary>
    [Fact]
    public async Task Admin_egalar_royxati_oqituvchiga_yopiq()
    {
        var w = await SeedAsync();
        using var teacher = w.GroupTeacherClient;
        using var anonymous = fixture.Api.AnonymousClient();

        Assert.Equal(HttpStatusCode.Forbidden, (await teacher.GetAsync(AdminOwners)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(AdminOwners)).StatusCode);
    }

    /* =====================================================================
     *  4. G-17 — guruh chati
     * ================================================================== */

    /// <summary>
    /// Guruh kanali: kalit <c>grp:&lt;id&gt;</c> (sinf nomi bilan
    /// TO'QNASHMAYDI), nom — guruhning nomi. O'chirgichgacha kanal umuman
    /// yo'q, yoqilgach o'qituvchi unga yozadi.
    /// </summary>
    [Fact]
    public async Task Guruh_chati_kanali_grp_kaliti_bilan_paydo_boladi()
    {
        var w = await SeedAsync();
        using var teacher = w.GroupTeacherClient;
        var key = ChatService.GroupChannel(Guid.Parse(w.GroupId));

        Assert.DoesNotContain(key, await ChannelKeysAsync(teacher));
        Assert.Equal(HttpStatusCode.Forbidden,
            (await teacher.GetAsync($"/api/teacher/chat/{Uri.EscapeDataString(key)}")).StatusCode);

        await WithGroupLessonsAsync(async () =>
        {
            var channels = await ArrayAsync(teacher, "/api/teacher/chat/channels");
            var row = Assert.Single(channels, x => x.GetProperty("key").GetString() == key);
            Assert.Equal(w.GroupName, row.GetProperty("label").GetString());
            Assert.Equal(LessonOwnerKind.Group, row.GetProperty("kind").GetString());

            // Kalit sinf nomi bo'la olmaydi — prefiks ularni butunlay ajratadi.
            Assert.StartsWith(ChatService.GroupChannelPrefix, key, StringComparison.Ordinal);
            Assert.DoesNotContain(channels, x =>
                x.GetProperty("kind").GetString() == LessonOwnerKind.Class
                && x.GetProperty("key").GetString() == key);

            var send = await teacher.PostAsJsonAsync(
                $"/api/teacher/chat/{Uri.EscapeDataString(key)}", new { text = "Guruhga salom" });
            Assert.Equal(HttpStatusCode.OK, send.StatusCode);

            var messages = await ArrayAsync(teacher, $"/api/teacher/chat/{Uri.EscapeDataString(key)}");
            Assert.Contains(messages, m => m.GetProperty("text").GetString() == "Guruhga salom");
        });
    }

    /// <summary>
    /// Begona guruhning kanali — yoqilgan holatda ham 403, va u kanallar
    /// ro'yxatida umuman ko'rinmaydi.
    /// </summary>
    [Fact]
    public async Task Begona_guruh_kanali_403()
    {
        var w = await SeedAsync();
        using var teacher = w.GroupTeacherClient;
        var otherKey = ChatService.GroupChannel(Guid.Parse(w.OtherGroupId));

        await WithGroupLessonsAsync(async () =>
        {
            Assert.DoesNotContain(otherKey, await ChannelKeysAsync(teacher));
            Assert.Equal(HttpStatusCode.Forbidden,
                (await teacher.GetAsync($"/api/teacher/chat/{Uri.EscapeDataString(otherKey)}")).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await teacher.PostAsJsonAsync(
                    $"/api/teacher/chat/{Uri.EscapeDataString(otherKey)}", new { text = "yo'q" })).StatusCode);
        });
    }

    /// <summary>Admin kanallar ro'yxatida guruh ham nomi bilan chiqadi.</summary>
    [Fact]
    public async Task Admin_kanallari_guruhni_ham_nomi_bilan_beradi()
    {
        var w = await SeedAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var key = ChatService.GroupChannel(Guid.Parse(w.GroupId));

        var off = await ArrayAsync(admin, "/api/admin/messages/channels");
        Assert.DoesNotContain(off, x => x.GetProperty("key").GetString() == key);

        await WithGroupLessonsAsync(async () =>
        {
            var on = await ArrayAsync(admin, "/api/admin/messages/channels");
            var row = Assert.Single(on, x => x.GetProperty("key").GetString() == key);
            Assert.Equal(w.GroupName, row.GetProperty("label").GetString());
        });
    }

    /* =====================================================================
     *  Yordamchilar
     * ================================================================== */

    private sealed record GroupWorld(
        string Tag,
        string ClassAId, string ClassAName, string ClassBId,
        string GroupId, string GroupName, string OtherGroupId,
        string EnglishId,
        string A1, string A2, string B1,
        HttpClient GroupTeacherClient, HttpClient AttachedTeacherClient);

    /// <summary>
    /// Ikki sinf (A: A1, A2; B: B1) va IKKALA sinfdan yig'ilgan guruh (A1, B1).
    /// Guruhda ikki o'qituvchi: biri jadvalda dars beradi, ikkinchisi faqat
    /// BIRIKTIRILGAN. Yana bitta BEGONA guruh ham bor.
    /// </summary>
    private async Task<GroupWorld> SeedAsync(string[]? groupTeacherPermissions = null)
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var (teachingId, teachingClient) = await TeacherClientAsync(
            groupTeacherPermissions ?? [TeacherPermissions.Journal, TeacherPermissions.Messages]);
        var (attachedId, attachedClient) = await TeacherClientAsync(
            TeacherPermissions.Journal, TeacherPermissions.Messages);

        var clsA = new SchoolClass { Name = $"GA-{tag}", Grade = 7 };
        var clsB = new SchoolClass { Name = $"GB-{tag}", Grade = 7 };
        var english = new Subject { Name = $"Ingliz {tag}", IsGroupable = true };
        var a1 = GeneralSettingsFlagsTests.NewStudent($"Guruh A1 {tag}", clsA.Name, "+998900000051");
        var a2 = GeneralSettingsFlagsTests.NewStudent($"Guruh A2 {tag}", clsA.Name, "+998900000052");
        var b1 = GeneralSettingsFlagsTests.NewStudent($"Guruh B1 {tag}", clsB.Name, "+998900000053");

        var group = new StudyGroup { Name = $"Kuchli ingliz {tag}", SubjectId = english.Id };
        var other = new StudyGroup { Name = $"Begona guruh {tag}", SubjectId = english.Id };

        await fixture.Api.WithDbAsync(async db =>
        {
            db.Classes.AddRange(clsA, clsB);
            db.Subjects.Add(english);
            db.Students.AddRange(a1, a2, b1);
            await db.SaveChangesAsync();

            var creator = await db.Users.Select(u => u.Id).FirstAsync();
            foreach (var g in new[] { group, other })
            {
                g.CreatedBy = creator;
                g.CreatedAt = AppClock.NowInstant;
            }
            db.StudyGroups.AddRange(group, other);
            db.StudyGroupClasses.AddRange(
                new StudyGroupClass { GroupId = group.Id, ClassId = clsA.Id },
                new StudyGroupClass { GroupId = group.Id, ClassId = clsB.Id },
                new StudyGroupClass { GroupId = other.Id, ClassId = clsA.Id });
            db.StudyGroupTeachers.Add(new StudyGroupTeacher { GroupId = group.Id, TeacherId = attachedId });
            db.StudyGroupMembers.AddRange(
                Member(group.Id, english.Id, a1.Id, creator),
                Member(group.Id, english.Id, b1.Id, creator),
                Member(other.Id, english.Id, a2.Id, creator));

            // Guruhning jadvali — dars beruvchi o'qituvchi shundan aniqlanadi.
            var tpl = new ScheduleTemplate
            {
                ClassId = group.Id.ToString(),
                Name = "Guruh " + tag,
                OwnerKind = LessonOwnerKind.Group,
            };
            tpl.Lessons.Add(new ScheduleLesson
            {
                TemplateId = tpl.Id, Day = 0, Period = 1,
                SubjectId = english.Id, TeacherId = teachingId,
            });
            db.ScheduleTemplates.Add(tpl);
            await db.SaveChangesAsync();
        });

        _createdGroupIds.Add(group.Id.ToString());
        _createdGroupIds.Add(other.Id.ToString());

        return new GroupWorld(
            tag, clsA.Id, clsA.Name, clsB.Id,
            group.Id.ToString(), group.Name, other.Id.ToString(),
            english.Id, a1.Id, a2.Id, b1.Id,
            teachingClient, attachedClient);
    }

    private static StudyGroupMember Member(Guid groupId, string subjectId, string studentId, string createdBy) =>
        new()
        {
            GroupId = groupId,
            SubjectId = subjectId,
            StudentId = studentId,
            JoinedOn = DateOnly.FromDateTime(AppClock.Today.AddDays(-30).ToDateTime(TimeOnly.MinValue)),
            CreatedBy = createdBy,
            CreatedAt = AppClock.NowInstant,
        };

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

    /// <summary>
    /// O'chirgichni FAQAT shu blok davomida yoqadi va oxirida qaytaradi (test
    /// yiqilsa ham) — u butun maktabga bitta qator.
    /// </summary>
    private async Task WithGroupLessonsAsync(Func<Task> body)
    {
        using var super = await fixture.Api.ClientAsAsync(Roles.SuperAdmin);
        Assert.Equal(HttpStatusCode.OK,
            (await super.PutAsJsonAsync(Switch, new { enabled = true })).StatusCode);
        try
        {
            await body();
        }
        finally
        {
            Assert.Equal(HttpStatusCode.OK,
                (await super.PutAsJsonAsync(Switch, new { enabled = false })).StatusCode);
        }
    }

    private static string Query(string url, string ownerId, string subjectId) =>
        $"{url}?classId={ownerId}&subjectId={subjectId}&quarter=1";

    private static string Roster(string ownerId, string subjectId) =>
        $"{TgRoster}?classId={ownerId}&subjectId={subjectId}&quarter=1&period=1";

    private static Task<HttpResponseMessage> PutTeacherEntry(HttpClient client, GroupWorld w) =>
        client.PutAsJsonAsync(TeacherJournal, new
        {
            classId = w.GroupId, subjectId = w.EnglishId, quarter = 1,
            studentId = w.A1, date = Past, period = 1,
            grade = 5, reasonId = (string?)null, homework = 0, behavior = 0, mastery = (int?)null,
        });

    private async Task<List<string>> ChannelKeysAsync(HttpClient client) =>
        [.. (await ArrayAsync(client, "/api/teacher/chat/channels"))
            .Select(x => x.GetProperty("key").GetString()!)];

    private async Task<List<string>> OwnerIdsAsync(HttpClient client, string url, string field = "classId") =>
        [.. (await ArrayAsync(client, url)).Select(x => x.GetProperty(field).GetString()!)];

    private static async Task<List<string>> StudentIdsAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"{url} → {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return [.. body.RootElement.EnumerateArray().Select(x => x.GetProperty("id").GetString()!)];
    }

    private static async Task<List<JsonElement>> ArrayAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"{url} → {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return [.. body.RootElement.EnumerateArray().Select(e => e.Clone())];
    }
}
