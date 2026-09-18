using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// G-20 — topshiriq (uyga vazifa/test) o'quv GURUHGA ham berilishi mumkin, sinfga bergandek
/// (docs/modules/students-parity.md §2.1.6, §2.1.4).
///
/// <para>
/// <b>Naqsh ikkalanmaydi.</b> <c>Assignment.OwnerKind</c> — jurnal va jadval allaqachon
/// ishlatgan <c>LessonOwnerKind</c>; guruh id'si xuddi shu <c>ClassIds</c> ustunida
/// saqlanadi. Shu testlar aynan shu naqshni mahkamlaydi: admin panel guruhga topshiriq
/// beradi, guruh a'zosi uni portalda ko'radi, guruhga kirmagan sinfdoshi ko'rmaydi.
/// </para>
/// <para>
/// <b>O'chirgichga bog'liq emas.</b> <c>school_meta.group_lessons_enabled</c> bu yerda
/// TEKSHIRILMAYDI — <see cref="AssignmentService"/> izohida yozilganidek, topshiriq
/// rejalashtirilgan darsga bog'lanmaydi. Shuning uchun testlar o'chirgichni yoqmaydi.
/// </para>
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class AssignmentGroupTests(ApiFixture fixture)
{
    private const string Admin = "/api/admin/assignments";

    // =====================================================================
    //  1. Yaratish — validatsiya va saqlash
    // =====================================================================

    [Fact]
    public async Task Guruhga_bosh_royxat_bilan_yaratish_guruh_sozini_ishlatadi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var w = await SeedAsync();

        var response = await client.PostAsJsonAsync(Admin, new
        {
            subjectId = w.SubjectId,
            title = $"Uy vazifa {w.Tag}",
            format = "written",
            classIds = Array.Empty<string>(),
            ownerKind = "group",
            lateAccept = false,
            latePenaltyPct = 0,
            maxScore = 100,
            autoGrade = false,
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Contains("guruh", body.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Guruhga_topshiriq_yaratiladi_va_guruh_nomi_bilan_qaytadi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var w = await SeedAsync();

        var created = await CreateAsync(client, w, $"Insho {w.Tag}");

        Assert.Equal(LessonOwnerKind.Group, created.RootElement.GetProperty("ownerKind").GetString());
        Assert.Equal([w.GroupId], created.RootElement.GetProperty("classIds").EnumerateArray()
            .Select(x => x.GetString() ?? "").ToArray());
        Assert.Equal([w.GroupName], created.RootElement.GetProperty("classNames").EnumerateArray()
            .Select(x => x.GetString() ?? "").ToArray());
    }

    // =====================================================================
    //  2. Ro'yxat va natijalar — ega ro'yxati LessonRoster orqali
    // =====================================================================

    [Fact]
    public async Task Royxat_filtri_guruh_idsi_bilan_ham_ishlaydi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var w = await SeedAsync();
        var created = await CreateAsync(client, w, $"Filtr {w.Tag}");
        var id = created.RootElement.GetProperty("id").GetString();

        var byGroup = await JsonAsync(client, $"{Admin}?classId={w.GroupId}");
        Assert.Contains(byGroup.RootElement.EnumerateArray(), a => a.GetProperty("id").GetString() == id);

        // Sinf ustunidan qidirilsa (guruhning o'zi emas) chiqmasligi kerak.
        var byClass = await JsonAsync(client, $"{Admin}?classId={w.ClassId}");
        Assert.DoesNotContain(byClass.RootElement.EnumerateArray(), a => a.GetProperty("id").GetString() == id);
    }

    /// <summary>
    /// Natijalar (kim bajardi) — guruh a'zosi bor, guruhga kirmagan sinfdosh yo'q.
    /// <see cref="AssignmentService.RosterAsync"/> ni LessonRoster orqali mahkamlaydi.
    /// </summary>
    [Fact]
    public async Task Natijalar_royxati_guruh_azosini_korsatadi_chetdagini_emas()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var w = await SeedAsync();
        var created = await CreateAsync(client, w, $"Natija {w.Tag}");
        var id = created.RootElement.GetProperty("id").GetString();

        var results = await JsonAsync(client, $"{Admin}/{id}/results");
        var rows = results.RootElement.GetProperty("rows").EnumerateArray()
            .Select(r => r.GetProperty("studentId").GetString()).ToList();

        Assert.Contains(w.MemberId, rows);
        Assert.DoesNotContain(w.OutsiderId, rows);
    }

    /// <summary>"Topshiriqlar bali" — guruh id bilan chaqirilsa guruh nomi va faqat uning a'zolari.</summary>
    [Fact]
    public async Task Scoreboard_guruh_idsi_bilan_ishlaydi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var w = await SeedAsync();
        await CreateAsync(client, w, $"Ball {w.Tag}");

        var board = await JsonAsync(client, $"{Admin}/scoreboard?classId={w.GroupId}");
        Assert.Equal(w.GroupId, board.RootElement.GetProperty("classId").GetString());
        Assert.Equal(w.GroupName, board.RootElement.GetProperty("className").GetString());

        var studentIds = board.RootElement.GetProperty("students").EnumerateArray()
            .Select(s => s.GetProperty("studentId").GetString()).ToList();
        Assert.Contains(w.MemberId, studentIds);
        Assert.DoesNotContain(w.OutsiderId, studentIds);
    }

    // =====================================================================
    //  3. O'quvchi portali — guruh a'zosi ko'radi, chetdagi ko'rmaydi
    // =====================================================================

    [Fact]
    public async Task Oquvchi_portali_guruh_topshirigini_azosiga_korsatadi_chetdagiga_yashiradi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var w = await SeedAsync();
        var created = await CreateAsync(client, w, $"Portal {w.Tag}");
        var id = created.RootElement.GetProperty("id").GetString();

        var memberList = await JsonAsync(client, $"/api/student/assignments?studentId={w.MemberId}");
        Assert.Contains(memberList.RootElement.EnumerateArray(), a => a.GetProperty("id").GetString() == id);

        var outsiderList = await JsonAsync(client, $"/api/student/assignments?studentId={w.OutsiderId}");
        Assert.DoesNotContain(outsiderList.RootElement.EnumerateArray(), a => a.GetProperty("id").GetString() == id);

        // Bitta topshiriq tafsiloti ham xuddi shunday: a'zoga 200, chetdagiga 404.
        var memberDetail = await client.GetAsync($"/api/student/assignments/{id}?studentId={w.MemberId}");
        Assert.Equal(HttpStatusCode.OK, memberDetail.StatusCode);
        var outsiderDetail = await client.GetAsync($"/api/student/assignments/{id}?studentId={w.OutsiderId}");
        Assert.Equal(HttpStatusCode.NotFound, outsiderDetail.StatusCode);
    }

    /// <summary>
    /// Sinfga berilgan (eski, ownerKind="class") topshiriq — REGRESSIYA: guruh
    /// mantig'i sinf yo'liga tegmagan bo'lishi kerak.
    /// </summary>
    [Fact]
    public async Task Sinfga_berilgan_topshiriq_ikkala_oquvchiga_ham_korinadi()
    {
        using var client = await fixture.Api.ClientAsAsync(Roles.Admin);
        var w = await SeedAsync();

        var response = await client.PostAsJsonAsync(Admin, new
        {
            subjectId = w.SubjectId,
            title = $"Sinf vazifasi {w.Tag}",
            format = "written",
            classIds = new[] { w.ClassId },
            lateAccept = false,
            latePenaltyPct = 0,
            maxScore = 100,
            autoGrade = false,
        });
        response.EnsureSuccessStatusCode();
        using var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var id = created.RootElement.GetProperty("id").GetString();
        Assert.Equal(LessonOwnerKind.Class, created.RootElement.GetProperty("ownerKind").GetString());

        var memberList = await JsonAsync(client, $"/api/student/assignments?studentId={w.MemberId}");
        var outsiderList = await JsonAsync(client, $"/api/student/assignments?studentId={w.OutsiderId}");
        Assert.Contains(memberList.RootElement.EnumerateArray(), a => a.GetProperty("id").GetString() == id);
        Assert.Contains(outsiderList.RootElement.EnumerateArray(), a => a.GetProperty("id").GetString() == id);
    }

    // =====================================================================
    //  4. RBAC — bir xil darvoza, guruhga ham amal qiladi
    // =====================================================================

    [Theory]
    [InlineData(Roles.Teacher)]
    [InlineData(Roles.Cashier)]
    public async Task Oqituvchi_va_kassir_guruhga_topshiriq_yarata_olmaydi(string role)
    {
        var w = await SeedAsync();
        using var client = await fixture.Api.ClientAsAsync(role, "students");

        var response = await client.PostAsJsonAsync(Admin, new
        {
            subjectId = w.SubjectId,
            title = $"Ruxsatsiz {w.Tag}",
            format = "written",
            classIds = new[] { w.GroupId },
            ownerKind = "group",
            lateAccept = false,
            latePenaltyPct = 0,
            maxScore = 100,
            autoGrade = false,
        });
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Tokensiz_401()
    {
        using var client = fixture.Api.AnonymousClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(Admin)).StatusCode);
    }

    // =====================================================================
    //  Yordamchilar
    // =====================================================================

    private static async Task<JsonDocument> JsonAsync(HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"{url} → {(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private static async Task<JsonDocument> CreateAsync(HttpClient client, World w, string title)
    {
        var response = await client.PostAsJsonAsync(Admin, new
        {
            subjectId = w.SubjectId,
            title,
            format = "written",
            classIds = new[] { w.GroupId },
            ownerKind = "group",
            lateAccept = false,
            latePenaltyPct = 0,
            maxScore = 100,
            autoGrade = false,
        });
        response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
    }

    private sealed record World(
        string Tag, string ClassId, string SubjectId,
        string GroupId, string GroupName, string MemberId, string OutsiderId);

    /// <summary>
    /// Bitta sinf, ichida groupable fan, shu fandan bitta guruh (sinfning bir qismi
    /// boqadi), guruh a'zosi va guruhga kirmagan sinfdosh.
    /// </summary>
    private async Task<World> SeedAsync()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var cls = new SchoolClass { Name = $"7-AG-{tag}", Grade = 7 };
        var subject = new Subject { Name = $"Ingliz tili {tag}", IsGroupable = true };
        var member = GeneralSettingsFlagsTests.NewStudent($"Guruh azosi {tag}", cls.Name, "+99891" + Rnd());
        var outsider = GeneralSettingsFlagsTests.NewStudent($"Guruhsiz sinfdosh {tag}", cls.Name, "+99891" + Rnd());

        var (creator, _) = await fixture.Api.SeedUserAsync(Roles.Admin);

        var group = new StudyGroup
        {
            Name = $"Kuchli guruh {tag}",
            SubjectId = subject.Id,
            CreatedBy = creator.Id,
            CreatedAt = AppClock.NowInstant,
        };

        await fixture.Api.WithDbAsync(async db =>
        {
            db.Classes.Add(cls);
            db.Subjects.Add(subject);
            db.Students.AddRange(member, outsider);
            db.StudyGroups.Add(group);
            db.StudyGroupClasses.Add(new StudyGroupClass { GroupId = group.Id, ClassId = cls.Id });
            db.StudyGroupMembers.Add(new StudyGroupMember
            {
                GroupId = group.Id,
                SubjectId = subject.Id,
                StudentId = member.Id,
                JoinedOn = new DateOnly(2026, 9, 1),
                CreatedBy = creator.Id,
                CreatedAt = AppClock.NowInstant,
            });
            await db.SaveChangesAsync();
        });

        return new World(tag, cls.Id, subject.Id, group.Id.ToString(), group.Name, member.Id, outsider.Id);
    }

    private static string Rnd() => Random.Shared.Next(1000000, 9999999).ToString();
}
