using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests.Security;

// ===========================================================================
//  G-3 — O'QITUVCHI JURNALGA QAYERGACHA YETADI (UCH YUZA)
//  (docs/modules/students-parity.md §2.1.3 "Journal & teacher access", §2.1.6 G-3/G-12)
// ===========================================================================
//
//  Bugun "bu o'qituvchi shu sinfda shu fanni o'qitadimi" degan savolga
//  UCHTA joyda javob beriladi va uchalasi ham JADVAL SHABLONLARIdan
//  hisoblanadi (o'qituvchi↔sinf↔fan jadvali YO'Q):
//
//    · admin jurnali   `AdminPerm("journal")`            — o'qituvchini UMUMAN kiritmaydi
//    · o'qituvchi web  `TeacherPortalController.Authorized`
//    · Telegram Mini App `TelegramTeacherController.TeachesAsync`
//
//  §4.3 (C2 slice) da uchalasi ham qayta yoziladi: guruh darsi beradigan
//  o'qituvchi bugun 403 oladi. Qayta yozish "ochish" bo'lishi kerak, "hammaga
//  ochish" emas — shuning uchun har darvoza IKKI TOMONDAN sinaladi: ruxsat
//  etilgan yo'l HAQIQATAN ishlaydi, qolgan hammasi rad etiladi.
//
//  ESLATMA: ruxsat BO'LIM darajasida ham bor (`Teacher.Permissions`) — u ham
//  shu yerda, chunki jurnalga yetishning birinchi sharti o'sha.
// ===========================================================================

/// <summary>
/// Jurnalga kirish chegarasi: admin paneli, o'qituvchi portali va Telegram Mini App.
/// Batafsil — fayl boshidagi izoh.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class TeacherAccessTests(ApiFixture fixture)
{
    private const string AdminJournal = "/api/admin/journal";
    private const string TeacherJournal = "/api/teacher/journal";
    private const string TgRoster = "/api/tg/teacher/roster";

    private static string Past => AppClock.Today.AddDays(-1).ToString("yyyy-MM-dd");
    private static string Future => AppClock.Today.AddDays(7).ToString("yyyy-MM-dd");

    // =====================================================================
    //  0. Tokensiz — hech qayerga
    // =====================================================================

    [Theory]
    [InlineData(AdminJournal)]
    [InlineData(TeacherJournal)]
    [InlineData(TgRoster)]
    public async Task Tokensiz_401(string url)
    {
        using var client = fixture.Api.AnonymousClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(url)).StatusCode);
    }

    // =====================================================================
    //  1. Admin jurnali — o'qituvchiga umuman yopiq
    // =====================================================================

    /// <summary>
    /// O'qituvchi ADMIN jurnaliga umuman kira olmaydi — o'zi dars beradigan sinf+fan
    /// bo'lsa ham. Uning yo'li faqat <c>/api/teacher/journal</c>.
    /// </summary>
    [Fact]
    public async Task Admin_jurnali_oqituvchiga_yopiq_403()
    {
        var w = await SeedAsync();
        using var teacher = w.FirstClient;

        var read = await teacher.GetAsync(Query(AdminJournal, w.ClassId, w.SubjectId));
        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PutAdminEntry(teacher, w)).StatusCode);
    }

    /// <summary>
    /// Boshqa tomoni: admin jurnali ADMINga ochiq, "journal" kaliti bo'lgan XODIMga ham.
    /// Kaliti yo'q xodim esa O'QIY oladi, lekin YOZA olmaydi (<c>AdminPermAttribute</c> qoidasi).
    /// </summary>
    [Fact]
    public async Task Admin_jurnali_adminga_va_kalitli_xodimga_ochiq()
    {
        var w = await SeedAsync();

        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        Assert.Equal(HttpStatusCode.NoContent, (await PutAdminEntry(admin, w)).StatusCode);

        using var keyed = await fixture.Api.ClientAsAsync(Roles.Staff, "journal");
        Assert.Equal(HttpStatusCode.NoContent, (await PutAdminEntry(keyed, w)).StatusCode);

        using var plain = await fixture.Api.ClientAsAsync(Roles.Staff, "students");
        Assert.Equal(HttpStatusCode.OK,
            (await plain.GetAsync(Query(AdminJournal, w.ClassId, w.SubjectId))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PutAdminEntry(plain, w)).StatusCode);
    }

    // =====================================================================
    //  2. O'qituvchi portali — faqat o'z sinfi va o'z fani
    // =====================================================================

    /// <summary>
    /// O'z sinfi + o'z fani: o'qiydi VA yozadi. (Darvozaning "ochiq" tomoni —
    /// usiz quyidagi 403 lar hech narsani isbotlamasdi.)
    /// </summary>
    [Fact]
    public async Task Oz_sinfi_va_oz_fani_oqiladi_va_yoziladi()
    {
        var w = await SeedAsync();
        using var teacher = w.FirstClient;

        Assert.Equal(HttpStatusCode.OK,
            (await teacher.GetAsync(Query(TeacherJournal, w.ClassId, w.SubjectId))).StatusCode);
        Assert.Equal(HttpStatusCode.OK,
            (await teacher.GetAsync(Query($"{TeacherJournal}/notes", w.ClassId, w.SubjectId))).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await PutTeacherEntry(teacher, w, Past)).StatusCode);
    }

    /// <summary>
    /// Uch xil begona yo'l — uchalasi ham 403: o'z sinfining BEGONA fani, BEGONA sinfdagi
    /// o'z fani va butunlay boshqa o'qituvchining sinf+fani.
    /// </summary>
    [Fact]
    public async Task Begona_sinf_yoki_begona_fan_403()
    {
        var w = await SeedAsync();
        using var teacher = w.FirstClient;

        foreach (var (classId, subjectId, why) in new[]
                 {
                     (w.ClassId, w.OtherSubjectId, "o'z sinfi, begona fan"),
                     (w.SecondClassId, w.SubjectId, "begona sinf, o'z fani"),
                     (w.SecondClassId, w.SecondSubjectId, "butunlay begona"),
                 })
        {
            Assert.True(HttpStatusCode.Forbidden ==
                (await teacher.GetAsync(Query(TeacherJournal, classId, subjectId))).StatusCode, why);
            Assert.True(HttpStatusCode.Forbidden ==
                (await PutTeacherEntry(teacher, w, Past, classId, subjectId)).StatusCode, why);
            Assert.True(HttpStatusCode.Forbidden ==
                (await teacher.GetAsync(Query($"{TeacherJournal}/columns", classId, subjectId))).StatusCode, why);
        }
    }

    /// <summary>
    /// "Jurnal" bo'lim ruxsati yo'q o'qituvchi O'Z sinf+fanida ham 403 oladi —
    /// dars berish o'zi yetarli emas.
    /// </summary>
    [Fact]
    public async Task Jurnal_ruxsati_yoq_oqituvchi_oz_sinfida_ham_403()
    {
        var w = await SeedAsync(firstTeacherPermissions: [TeacherPermissions.Schedule]);
        using var teacher = w.FirstClient;

        Assert.Equal(HttpStatusCode.Forbidden,
            (await teacher.GetAsync(Query(TeacherJournal, w.ClassId, w.SubjectId))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PutTeacherEntry(teacher, w, Past)).StatusCode);
    }

    /// <summary>
    /// BUGUNGI XATTI-HARAKAT (nomutanosiblik): SINF RAHBARI o'z sinfining
    /// o'quvchilar RO'YXATINI ko'radi (unda dars bermasa ham), lekin o'sha
    /// sinfning jurnal KATAKLARIGA yetolmaydi — ro'yxat <c>TeachesClass</c> ga,
    /// jurnal esa <c>Teaches(classId, subjectId)</c> ga tayanadi.
    /// </summary>
    [Fact]
    public async Task Sinf_rahbari_royxatni_koradi_jurnalni_esa_yoq_bugungi_xatti_harakat()
    {
        var w = await SeedAsync();
        var (homeroomId, homeroom) = await TeacherClientAsync(TeacherPermissions.Journal);
        using var _ = homeroom;

        await fixture.Api.WithDbAsync(async db =>
        {
            var t = await db.Teachers.SingleAsync(x => x.Id == homeroomId);
            t.HomeroomClass = w.ClassName;
            await db.SaveChangesAsync();
        });

        Assert.Equal(HttpStatusCode.OK,
            (await homeroom.GetAsync($"{TeacherJournal}/students?classId={w.ClassId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await homeroom.GetAsync(Query(TeacherJournal, w.ClassId, w.SubjectId))).StatusCode);
    }

    /// <summary>
    /// O'qituvchi yo'lidagi QO'SHIMCHA qoida (adminda yo'q): hali o'tilmagan,
    /// kelajakdagi sanaga baho qo'yib bo'lmaydi — 400.
    /// </summary>
    [Fact]
    public async Task Kelajakdagi_darsga_oqituvchi_baho_qoya_olmaydi_400()
    {
        var w = await SeedAsync();
        using var teacher = w.FirstClient;

        var response = await PutTeacherEntry(teacher, w, Future);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        // Admin yo'lida esa o'sha sana qabul qilinadi.
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        Assert.Equal(HttpStatusCode.NoContent, (await PutAdminEntry(admin, w, Future)).StatusCode);
    }

    /// <summary>O'qituvchi portali faqat <c>teacher</c> roliga — admin ham, xodim ham 403.</summary>
    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.SuperAdmin)]
    [InlineData(Roles.Staff)]
    public async Task Oqituvchi_portali_boshqa_rollarga_yopiq_403(string role)
    {
        var w = await SeedAsync();
        using var client = await fixture.Api.ClientAsAsync(role, "journal");

        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync(Query(TeacherJournal, w.ClassId, w.SubjectId))).StatusCode);
    }

    // =====================================================================
    //  3. Telegram Mini App — o'sha qoida, uchinchi yuzada
    // =====================================================================

    /// <summary>
    /// Mini App yo'qlamasi o'z sinf+fanida ochiladi va ro'yxatni beradi;
    /// begona sinf+fanda esa 403.
    /// </summary>
    [Fact]
    public async Task Mini_app_yoqlamasi_oz_sinfida_ochiladi_begonasida_403()
    {
        var w = await SeedAsync();
        using var teacher = w.FirstClient;

        var mine = await teacher.GetAsync(Roster(w.ClassId, w.SubjectId));
        Assert.True(mine.StatusCode == HttpStatusCode.OK,
            $"{(int)mine.StatusCode}: {await mine.Content.ReadAsStringAsync()}");
        using var body = JsonDocument.Parse(await mine.Content.ReadAsStringAsync());
        Assert.Equal(w.ClassName, body.RootElement.GetProperty("className").GetString());
        Assert.Contains(body.RootElement.GetProperty("students").EnumerateArray(),
            s => s.GetProperty("studentId").GetString() == w.StudentId);

        Assert.Equal(HttpStatusCode.Forbidden,
            (await teacher.GetAsync(Roster(w.SecondClassId, w.SecondSubjectId))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await teacher.GetAsync(Roster(w.ClassId, w.OtherSubjectId))).StatusCode);
    }

    /// <summary>Mini App'da ham "jurnal" bo'lim ruxsati shart.</summary>
    [Fact]
    public async Task Mini_app_yoqlamasi_jurnal_ruxsatisiz_403()
    {
        var w = await SeedAsync(firstTeacherPermissions: [TeacherPermissions.Messages]);
        using var teacher = w.FirstClient;

        Assert.Equal(HttpStatusCode.Forbidden,
            (await teacher.GetAsync(Roster(w.ClassId, w.SubjectId))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await teacher.GetAsync("/api/tg/teacher/journal/recent")).StatusCode);
    }

    /// <summary>Mini App o'qituvchi yuzasi boshqa rollarga umuman ochilmaydi.</summary>
    [Theory]
    [InlineData(Roles.Admin)]
    [InlineData(Roles.Staff)]
    [InlineData("parent")]
    public async Task Mini_app_oqituvchi_yuzasi_boshqa_rollarga_403(string role)
    {
        var w = await SeedAsync();
        using var client = await fixture.Api.ClientAsAsync(role, "journal");

        Assert.Equal(HttpStatusCode.Forbidden,
            (await client.GetAsync(Roster(w.ClassId, w.SubjectId))).StatusCode);
    }

    /// <summary>
    /// "So'nggi yozuvlar" ro'yxati faqat o'qituvchining O'Z (sinf, fan) juftliklaridan
    /// yig'iladi — qo'shni o'qituvchining jurnali sizib chiqmaydi.
    /// </summary>
    [Fact]
    public async Task Songgi_yozuvlar_faqat_oz_sinf_fanidan()
    {
        var w = await SeedAsync();
        using var first = w.FirstClient;
        using var second = w.SecondClient;

        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        await PutAdminEntry(admin, w);                                        // 1-o'qituvchining sinf+fani
        await PutAdminEntry(admin, w, classId: w.SecondClassId, subjectId: w.SecondSubjectId,
            studentId: w.SecondStudentId);                                     // 2-o'qituvchiniki

        var mine = await RecentPairsAsync(first);
        Assert.Contains((w.ClassId, w.SubjectId), mine);
        Assert.DoesNotContain((w.SecondClassId, w.SecondSubjectId), mine);

        var theirs = await RecentPairsAsync(second);
        Assert.Contains((w.SecondClassId, w.SecondSubjectId), theirs);
        Assert.DoesNotContain((w.ClassId, w.SubjectId), theirs);
    }

    /// <summary>
    /// Hech qayerda dars bermaydigan o'qituvchi uchun "so'nggi yozuvlar" — bo'sh ro'yxat
    /// (403 emas: ekran ochiladi, ichi bo'sh).
    /// </summary>
    [Fact]
    public async Task Dars_bermaydigan_oqituvchining_songgi_yozuvlari_bosh()
    {
        var (_, teacher) = await TeacherClientAsync(TeacherPermissions.Journal);
        using var _client = teacher;

        var response = await teacher.GetAsync("/api/tg/teacher/journal/recent");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Empty(body.RootElement.EnumerateArray());
    }

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    private sealed record AccessWorld(
        string Tag,
        string ClassId, string ClassName, string SubjectId, string OtherSubjectId, string StudentId,
        string SecondClassId, string SecondSubjectId, string SecondStudentId,
        HttpClient FirstClient, HttpClient SecondClient);

    /// <summary>
    /// Ikki mustaqil olam: 1-o'qituvchi 1-sinfda 1-fanni, 2-o'qituvchi 2-sinfda 2-fanni
    /// o'qitadi. 1-sinfda hech kim o'qitmaydigan "begona fan" ham bor.
    /// </summary>
    private async Task<AccessWorld> SeedAsync(string[]? firstTeacherPermissions = null)
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var (firstId, firstClient) = await TeacherClientAsync(
            firstTeacherPermissions ?? [TeacherPermissions.Journal]);
        var (secondId, secondClient) = await TeacherClientAsync(TeacherPermissions.Journal);

        var clsA = new SchoolClass { Name = $"RX-A-{tag}", Grade = 9 };
        var clsB = new SchoolClass { Name = $"RX-B-{tag}", Grade = 9 };
        var subjA = new Subject { Name = $"Kimyo {tag}" };
        var subjOther = new Subject { Name = $"Biologiya {tag}" };
        var subjB = new Subject { Name = $"Tarix {tag}" };
        var studentA = GeneralSettingsFlagsTests.NewStudent($"Ruxsat A {tag}", clsA.Name, "+998900000031");
        var studentB = GeneralSettingsFlagsTests.NewStudent($"Ruxsat B {tag}", clsB.Name, "+998900000032");

        await fixture.Api.WithDbAsync(async db =>
        {
            db.Classes.AddRange(clsA, clsB);
            db.Subjects.AddRange(subjA, subjOther, subjB);
            db.Students.AddRange(studentA, studentB);

            db.ScheduleTemplates.Add(Template(clsA.Id, "A " + tag, subjA.Id, firstId));
            db.ScheduleTemplates.Add(Template(clsB.Id, "B " + tag, subjB.Id, secondId));
            await db.SaveChangesAsync();
        });

        return new AccessWorld(
            tag, clsA.Id, clsA.Name, subjA.Id, subjOther.Id, studentA.Id,
            clsB.Id, subjB.Id, studentB.Id, firstClient, secondClient);
    }

    private static ScheduleTemplate Template(string classId, string name, string subjectId, string teacherId)
    {
        var tpl = new ScheduleTemplate { ClassId = classId, Name = name };
        tpl.Lessons.Add(new ScheduleLesson
        {
            TemplateId = tpl.Id, Day = 0, Period = 1, SubjectId = subjectId, TeacherId = teacherId,
        });
        return tpl;
    }

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

    private static string Query(string url, string classId, string subjectId) =>
        $"{url}?classId={classId}&subjectId={subjectId}&quarter=1";

    private static string Roster(string classId, string subjectId) =>
        $"{TgRoster}?classId={classId}&subjectId={subjectId}&quarter=1&period=1";

    private static Task<HttpResponseMessage> PutAdminEntry(
        HttpClient client, AccessWorld w, string? date = null,
        string? classId = null, string? subjectId = null, string? studentId = null) =>
        client.PutAsJsonAsync(AdminJournal, new
        {
            classId = classId ?? w.ClassId, subjectId = subjectId ?? w.SubjectId, quarter = 1,
            studentId = studentId ?? w.StudentId, date = date ?? Past, period = 1,
            grade = 5, reasonId = (string?)null, homework = 0, behavior = 0, mastery = (int?)null,
        });

    private static Task<HttpResponseMessage> PutTeacherEntry(
        HttpClient client, AccessWorld w, string date, string? classId = null, string? subjectId = null) =>
        client.PutAsJsonAsync(TeacherJournal, new
        {
            classId = classId ?? w.ClassId, subjectId = subjectId ?? w.SubjectId, quarter = 1,
            studentId = w.StudentId, date, period = 1,
            grade = 4, reasonId = (string?)null, homework = 0, behavior = 0, mastery = (int?)null,
        });

    private static async Task<List<(string ClassId, string SubjectId)>> RecentPairsAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/tg/teacher/journal/recent?limit=100");
        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.EnumerateArray()
            .Select(e => (e.GetProperty("classId").GetString()!, e.GetProperty("subjectId").GetString()!))
            .ToList();
    }
}
