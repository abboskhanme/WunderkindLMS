using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests.Security;

// ===========================================================================
//  X-3 — O'QITUVCHI PORTALIDA "GURUHLARIM" (docs/modules/students-parity.md
//  §2.11, EduSchool getStudentGroups / updatedStudentGroup)
// ===========================================================================
//
//  IKKI DARAJALI RUXSAT:
//    · KO'RISH (reach)   — guruhga BIRIKTIRILGAN (study_group_teachers) YOKI
//      jadvalda unda darsi bor o'qituvchi (TeacherOwnerAccess.ReachesAsync,
//      xuddi /classes va guruh chatidagi kabi — G-12).
//    · TAHRIRLASH (lead) — FAQAT biriktirilgan o'qituvchi. Bu modelda "kim
//      tahrirlay oladi" degan alohida qoida hali yo'q edi (TeacherPermissions
//      orasida "groups" kaliti yo'q); ENG XAVFSIZ o'qish tanlandi va shu yerda
//      qadab qo'yilgan — TeacherPortalController.GroupAccessAsync.
//
//  Har test RUXSAT ETILGAN yo'lni ham, RAD ETILGAN yo'lni ham tekshiradi —
//  darvoza "ochilmoqda", "hammaga ochilmayapti".
// ===========================================================================

/// <summary>O'qituvchi portalidagi guruh ro'yxati sahifasining ruxsat chegarasi (X-3).</summary>
[Collection(SchoolLmsCollection.Name)]
public class TeacherGroupsPortalTests(ApiFixture fixture) : IAsyncLifetime
{
    /// <summary>Shu test yaratgan guruhlar — <see cref="DisposeAsync"/> ularga tegishli
    /// hamma narsani tozalaydi (baza umumiy — GroupTeacherAccessTests'dagi kabi sabab).</summary>
    private readonly List<string> _createdGroupIds = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_createdGroupIds.Count == 0) return;
        var ids = _createdGroupIds;
        await fixture.Api.WithDbAsync(async db =>
        {
            var templates = await db.ScheduleTemplates.Include(t => t.Lessons)
                .Where(t => ids.Contains(t.ClassId)).ToListAsync();
            db.ScheduleTemplates.RemoveRange(templates);
            await db.SaveChangesAsync();
        });
    }

    private const string Switch = "/api/admin/group-lessons";
    private const string Groups = "/api/teacher/groups";

    /* =====================================================================
     *  1. "Guruhlarim" ro'yxati
     * ================================================================== */

    /// <summary>
    /// Biriktirilgan o'qituvchi ro'yxatda o'z guruhini <c>canEditRoster=true</c>
    /// bilan ko'radi, begona guruhni UMUMAN ko'rmaydi. O'chirgichgacha ro'yxat bo'sh.
    /// </summary>
    [Fact]
    public async Task Royxat_faqat_oz_guruhini_beradi_canEditRoster_bilan()
    {
        var w = await SeedAsync();
        using var lead = w.LeadClient;

        Assert.Empty(await ArrayAsync(lead, Groups));

        await WithGroupLessonsAsync(async () =>
        {
            var rows = await ArrayAsync(lead, Groups);
            var row = Assert.Single(rows, x => x.GetProperty("id").GetString() == w.GroupId);
            Assert.True(row.GetProperty("canEditRoster").GetBoolean());
            Assert.Equal(2, row.GetProperty("memberCount").GetInt32());
            Assert.DoesNotContain(rows, x => x.GetProperty("id").GetString() == w.OtherGroupId);
        });

        Assert.Empty(await ArrayAsync(lead, Groups));
    }

    /// <summary>
    /// Faqat jadvalda darsi bor (biriktirilmagan) o'qituvchi ham guruhni ro'yxatda
    /// ko'radi, lekin <c>canEditRoster=false</c>.
    /// </summary>
    [Fact]
    public async Task Faqat_dars_beruvchi_royxatda_koradi_lekin_canEditRoster_false()
    {
        var w = await SeedAsync();
        using var teaching = w.TeachingOnlyClient;

        await WithGroupLessonsAsync(async () =>
        {
            var rows = await ArrayAsync(teaching, Groups);
            var row = Assert.Single(rows, x => x.GetProperty("id").GetString() == w.GroupId);
            Assert.False(row.GetProperty("canEditRoster").GetBoolean());
        });
    }

    /// <summary>Guruhga umuman aloqasi yo'q o'qituvchi — bo'sh ro'yxat.</summary>
    [Fact]
    public async Task Begona_oqituvchi_bosh_royxat_oladi()
    {
        var w = await SeedAsync();
        using var stranger = w.StrangerClient;

        await WithGroupLessonsAsync(async () =>
        {
            Assert.Empty(await ArrayAsync(stranger, Groups));
        });
    }

    /* =====================================================================
     *  2. Ro'yxatni KO'RISH (a'zolar) — reach yetarli
     * ================================================================== */

    /// <summary>Biriktirilgan VA faqat dars beruvchi — ikkalasi ham a'zolar ro'yxatini ko'radi.</summary>
    [Fact]
    public async Task Ikkalasi_ham_azolarni_koradi_lead_ham_teaching_ham()
    {
        var w = await SeedAsync();
        using var lead = w.LeadClient;
        using var teaching = w.TeachingOnlyClient;

        await WithGroupLessonsAsync(async () =>
        {
            foreach (var client in new[] { lead, teaching })
            {
                var members = await ArrayAsync(client, Members(w.GroupId));
                Assert.Equal(2, members.Count);
                Assert.Contains(members, m => m.GetProperty("studentId").GetString() == w.A1);
                Assert.Contains(members, m => m.GetProperty("studentId").GetString() == w.B1);
            }
        });
    }

    /// <summary>Begona o'qituvchi (guruhga umuman yetmaydigan) — a'zolar ro'yxati 403.</summary>
    [Fact]
    public async Task Begona_oqituvchiga_azolar_royxati_403()
    {
        var w = await SeedAsync();
        using var stranger = w.StrangerClient;

        await WithGroupLessonsAsync(async () =>
        {
            Assert.Equal(HttpStatusCode.Forbidden,
                (await stranger.GetAsync(Members(w.GroupId))).StatusCode);
        });
    }

    /// <summary>O'chirgich o'chiq bo'lsa — biriktirilgan o'qituvchiga ham 403 (guruh "yo'q").</summary>
    [Fact]
    public async Task Ochirgich_ochiq_bolsa_biriktirilganga_ham_403()
    {
        var w = await SeedAsync();
        using var lead = w.LeadClient;

        Assert.Equal(HttpStatusCode.Forbidden, (await lead.GetAsync(Members(w.GroupId))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await lead.GetAsync(Candidates(w.GroupId))).StatusCode);
    }

    /* =====================================================================
     *  3. Ro'yxatni TAHRIRLASH — FAQAT biriktirilgan (lead)
     * ================================================================== */

    /// <summary>Nomzodlar ro'yxati — FAQAT biriktirilganga ochiq, dars beruvchiga 403.</summary>
    [Fact]
    public async Task Nomzodlar_faqat_biriktirilganga_ochiq()
    {
        var w = await SeedAsync();
        using var lead = w.LeadClient;
        using var teaching = w.TeachingOnlyClient;

        await WithGroupLessonsAsync(async () =>
        {
            var candidates = await ArrayAsync(lead, Candidates(w.GroupId));
            Assert.Contains(candidates, c => c.GetProperty("studentId").GetString() == w.A2Candidate);

            Assert.Equal(HttpStatusCode.Forbidden,
                (await teaching.GetAsync(Candidates(w.GroupId))).StatusCode);
        });
    }

    /// <summary>
    /// Biriktirilgan o'qituvchi nomzodni qo'shadi — a'zolar soni oshadi. Faqat dars
    /// beruvchi va begona o'qituvchi esa 403 oladi (qo'shish bajarilmaydi).
    /// </summary>
    [Fact]
    public async Task Faqat_biriktirilgan_royxatga_oquvchi_qosha_oladi()
    {
        var w = await SeedAsync();
        using var lead = w.LeadClient;
        using var teaching = w.TeachingOnlyClient;
        using var stranger = w.StrangerClient;

        await WithGroupLessonsAsync(async () =>
        {
            Assert.Equal(HttpStatusCode.Forbidden,
                (await teaching.PostAsJsonAsync(AddMembers(w.GroupId), new { studentIds = new[] { w.A2Candidate } }))
                    .StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await stranger.PostAsJsonAsync(AddMembers(w.GroupId), new { studentIds = new[] { w.A2Candidate } }))
                    .StatusCode);

            var response = await lead.PostAsJsonAsync(
                AddMembers(w.GroupId), new { studentIds = new[] { w.A2Candidate } });
            Assert.True(response.StatusCode == HttpStatusCode.NoContent,
                $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

            var members = await ArrayAsync(lead, Members(w.GroupId));
            Assert.Equal(3, members.Count);
            Assert.Contains(members, m => m.GetProperty("studentId").GetString() == w.A2Candidate);
        });
    }

    /// <summary>
    /// Biriktirilgan o'qituvchi a'zolikni yopadi (sabab bilan) — begona va faqat
    /// dars beruvchi esa 403 oladi.
    /// </summary>
    [Fact]
    public async Task Faqat_biriktirilgan_azolikni_yopa_oladi()
    {
        var w = await SeedAsync();
        using var lead = w.LeadClient;
        using var teaching = w.TeachingOnlyClient;
        using var stranger = w.StrangerClient;

        await WithGroupLessonsAsync(async () =>
        {
            var memberId = await MemberIdAsync(lead, w.GroupId, w.A1);

            Assert.Equal(HttpStatusCode.Forbidden,
                (await teaching.PostAsJsonAsync(RemoveMember(w.GroupId, memberId), new { reason = "yo'q" }))
                    .StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await stranger.PostAsJsonAsync(RemoveMember(w.GroupId, memberId), new { reason = "yo'q" }))
                    .StatusCode);

            var response = await lead.PostAsJsonAsync(
                RemoveMember(w.GroupId, memberId), new { reason = "Ota-onaning iltimosiga ko'ra" });
            Assert.True(response.StatusCode == HttpStatusCode.NoContent,
                $"{(int)response.StatusCode}: {await response.Content.ReadAsStringAsync()}");

            var members = await ArrayAsync(lead, Members(w.GroupId));
            Assert.DoesNotContain(members, m => m.GetProperty("studentId").GetString() == w.A1);
        });
    }

    /// <summary>Begona guruh (ikkalasi ham aloqasi yo'q) — hech kim tegmaydi, 403.</summary>
    [Fact]
    public async Task Begona_guruhga_hech_kim_yetmaydi()
    {
        var w = await SeedAsync();
        using var lead = w.LeadClient;

        await WithGroupLessonsAsync(async () =>
        {
            Assert.Equal(HttpStatusCode.Forbidden,
                (await lead.GetAsync(Members(w.OtherGroupId))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await lead.GetAsync(Candidates(w.OtherGroupId))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden,
                (await lead.PostAsJsonAsync(AddMembers(w.OtherGroupId), new { studentIds = Array.Empty<string>() }))
                    .StatusCode);
        });
    }

    /* =====================================================================
     *  Yordamchilar
     * ================================================================== */

    private sealed record GroupWorld(
        string GroupId, string OtherGroupId, string SubjectId,
        string A1, string B1, string A2Candidate,
        HttpClient LeadClient, HttpClient TeachingOnlyClient, HttpClient StrangerClient);

    /// <summary>
    /// Ikki sinf (A: A1, A2Candidate; B: B1) va ikkala sinfdan yig'ilgan guruh
    /// (a'zo: A1, B1 — A2Candidate hali nomzod). Guruhda ikki o'qituvchi: LEAD —
    /// biriktirilgan (study_group_teachers), TEACHING — faqat jadvalda darsi bor.
    /// Uchinchi o'qituvchi (STRANGER) guruhga umuman aloqasiz. Yana bitta BEGONA
    /// guruh — hech kim unga tegishli emas.
    /// </summary>
    private async Task<GroupWorld> SeedAsync()
    {
        var tag = Guid.NewGuid().ToString("N")[..8];
        var (leadId, leadClient) = await TeacherClientAsync();
        var (teachingId, teachingClient) = await TeacherClientAsync();
        var (_, strangerClient) = await TeacherClientAsync();

        var clsA = new SchoolClass { Name = $"TG-A-{tag}", Grade = 6 };
        var clsB = new SchoolClass { Name = $"TG-B-{tag}", Grade = 6 };
        var subject = new Subject { Name = $"Matematika {tag}", IsGroupable = true };
        var a1 = NewStudent($"TG A1 {tag}", clsA.Name);
        var a2 = NewStudent($"TG A2 {tag}", clsA.Name);
        var b1 = NewStudent($"TG B1 {tag}", clsB.Name);

        var group = new StudyGroup { Name = $"X-3 guruh {tag}", SubjectId = subject.Id };
        var other = new StudyGroup { Name = $"X-3 begona {tag}", SubjectId = subject.Id };

        await fixture.Api.WithDbAsync(async db =>
        {
            db.Classes.AddRange(clsA, clsB);
            db.Subjects.Add(subject);
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
            // LEAD — guruh o'qituvchisi (biriktirilgan, "yetakchi").
            db.StudyGroupTeachers.Add(new StudyGroupTeacher { GroupId = group.Id, TeacherId = leadId });
            db.StudyGroupMembers.AddRange(
                Member(group.Id, subject.Id, a1.Id, creator),
                Member(group.Id, subject.Id, b1.Id, creator));

            // TEACHING — jadvalda guruh darsi bor, lekin study_group_teachers'da YO'Q.
            var tpl = new ScheduleTemplate
            {
                ClassId = group.Id.ToString(),
                Name = "X-3 jadval " + tag,
                OwnerKind = LessonOwnerKind.Group,
            };
            tpl.Lessons.Add(new ScheduleLesson
            {
                TemplateId = tpl.Id, Day = 0, Period = 1,
                SubjectId = subject.Id, TeacherId = teachingId,
            });
            db.ScheduleTemplates.Add(tpl);
            await db.SaveChangesAsync();
        });

        _createdGroupIds.Add(group.Id.ToString());
        _createdGroupIds.Add(other.Id.ToString());

        return new GroupWorld(
            group.Id.ToString(), other.Id.ToString(), subject.Id,
            a1.Id, b1.Id, a2.Id,
            leadClient, teachingClient, strangerClient);
    }

    private static Student NewStudent(string fullName, string className) => new()
    {
        FullName = fullName,
        LastName = fullName.Split(' ')[^1],
        FirstName = fullName.Split(' ')[0],
        BirthDate = "2013-01-01",
        Gender = "male",
        ClassName = className,
        EnrollmentDate = "2026-09-01",
    };

    private static StudyGroupMember Member(Guid groupId, string subjectId, string studentId, string createdBy) =>
        new()
        {
            GroupId = groupId,
            SubjectId = subjectId,
            StudentId = studentId,
            JoinedOn = DateOnly.FromDateTime(AppClock.Today.AddDays(-20).ToDateTime(TimeOnly.MinValue)),
            CreatedBy = createdBy,
            CreatedAt = AppClock.NowInstant,
        };

    private async Task<(string TeacherId, HttpClient Client)> TeacherClientAsync()
    {
        var (user, _) = await fixture.Api.SeedUserAsync(Roles.Teacher);
        var teacherId = "";
        await fixture.Api.WithDbAsync(async db =>
        {
            var t = await db.Teachers.SingleAsync(x => x.UserId == user.Id);
            teacherId = t.Id;
        });
        return (teacherId, fixture.Api.ClientWithToken(
            fixture.Api.TokenFor(Roles.Teacher, user.Id, user.FullName, user.Email)));
    }

    /// <summary>O'chirgichni FAQAT shu blok davomida yoqadi va oxirida qaytaradi.</summary>
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

    private static string Members(string groupId) => $"{Groups}/{groupId}/members";
    private static string Candidates(string groupId) => $"{Groups}/{groupId}/candidates";
    private static string AddMembers(string groupId) => $"{Groups}/{groupId}/members";

    private static string RemoveMember(string groupId, string memberId) =>
        $"{Groups}/{groupId}/members/{memberId}/remove";

    private static async Task<string> MemberIdAsync(HttpClient client, string groupId, string studentId)
    {
        var rows = await ArrayAsync(client, Members(groupId));
        var row = Assert.Single(rows, m => m.GetProperty("studentId").GetString() == studentId);
        return row.GetProperty("id").GetString()!;
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
