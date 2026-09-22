using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Tests.Fixtures;

namespace SchoolLms.Tests;

/// <summary>
/// Lid → o'quvchi, to'g'ridan-to'g'ri sinfga: <c>POST /api/admin/leads/{id}/enrol</c>
/// (admission-and-testing.md §6.1). O'quvchi oddiy yaratish yo'lidan yaratiladi, lid
/// O'CHIRILADI, umumiy son esa <c>lead_conversions</c> da qoladi (mijoz qarori,
/// 2026-09-22) — hammasi bitta saqlashda.
/// </summary>
[Collection(SchoolLmsCollection.Name)]
public class LeadEnrolTests(ApiFixture fixture)
{
    private AppDbContext NewDb() => PostgresFixture.NewContext(fixture.Database.OwnerConnectionString);

    private static string Enrol(string leadId) => $"/api/admin/leads/{leadId}/enrol";

    private static object Body(string fullName, string? className = "5-A") => new
    {
        student = new
        {
            fullName,
            birthDate = "2016-03-14",
            address = "",
            gender = "male",
            parentFullName = "Karimov Aziz",
            parentPhone = "+998901234567",
            className,
            enrollmentDate = (string?)null,
        },
    };

    /// <summary>Ustun va lid — test o'z qatorlarini yaratadi, umumiy bazadagi boshqalarga tegmaydi.</summary>
    private async Task<(string StageId, string LeadId)> SeedLeadAsync(string name)
    {
        await using var db = NewDb();
        var stage = new LeadStage { Title = $"Enrol {Guid.NewGuid():N}"[..20], Color = "blue", Order = 900 };
        db.LeadStages.Add(stage);
        var lead = new Lead
        {
            FullName = name, Gender = "male", BirthDate = "2016-03-14",
            ParentFullName = "Karimov Aziz", ParentPhone = "+998901234567",
            TargetGrade = 5, Stage = stage.Id,
        };
        db.Leads.Add(lead);
        await db.SaveChangesAsync();
        return (stage.Id, lead.Id);
    }

    [Fact]
    public async Task Lid_oquvchiga_aylantiriladi_lid_ochadi_son_qoladi()
    {
        var name = $"Enrol Ali {Guid.NewGuid():N}"[..24];
        var (_, leadId) = await SeedLeadAsync(name);
        await using (var before = NewDb())
            Assert.True(await before.Leads.AnyAsync(l => l.Id == leadId));
        int conversionsBefore;
        await using (var before = NewDb())
            conversionsBefore = await before.LeadConversions.CountAsync();
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var response = await admin.PostAsJsonAsync(Enrol(leadId), Body(name));
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{(int)response.StatusCode}: {text}");

        using var doc = JsonDocument.Parse(text);
        var studentId = doc.RootElement.GetProperty("student").GetProperty("id").GetString()!;
        Assert.Equal("5-A", doc.RootElement.GetProperty("student").GetProperty("className").GetString());

        await using var db = NewDb();
        var student = await db.Students.AsNoTracking().SingleAsync(s => s.Id == studentId);
        Assert.Equal(name, student.FullName);
        Assert.NotNull(student.UserId); // tizim akkaunti — oddiy yaratishdagidek

        // Lid bazadan ketdi, son esa qoldi.
        Assert.False(await db.Leads.AnyAsync(l => l.Id == leadId));
        Assert.Equal(conversionsBefore + 1, await db.LeadConversions.CountAsync());
        Assert.True(await db.AuditLogs.AnyAsync(a => a.EntityType == "Lead" && a.EntityId == leadId && a.Action == "enrol"));
    }

    /// <summary>Ariza formasidan kelgan lidning manbasi statistikaga o'tadi — voronka "qaysi forma o'quvchi olib keldi" deb sanaydi.</summary>
    [Fact]
    public async Task Arizadan_kelgan_lid_manbasi_statistikada_qoladi()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var survey = await SalesMarketingKit.CreateSurveyAsync(admin, $"enrol-{SalesMarketingKit.Tag()}");
        using var anon = fixture.Api.AnonymousClient();
        var posted = await SalesMarketingKit.PublicPostAsync(anon, survey.Slug,
            SalesMarketingKit.Form(studentFirst: "Enrol", studentLast: "Arizadan"), SalesMarketingKit.NextIp());
        await SalesMarketingKit.AssertAcceptedAsync(posted, survey.ThankYou);

        string leadId;
        await using (var db = NewDb())
            leadId = (await db.Leads.AsNoTracking().SingleAsync(l => l.SurveyId == survey.Id)).Id;

        var response = await admin.PostAsJsonAsync(Enrol(leadId), Body("Enrol Arizadan"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var check = NewDb();
        var conversion = await check.LeadConversions.AsNoTracking().SingleAsync(c => c.SurveyId == survey.Id);
        Assert.Equal(LeadSource.Survey, conversion.Source);
        // Ariza topshirig'i (ota-ona yozgan asl qator) qoladi — u lid emas, ariza registri.
        Assert.True(await check.SurveySubmissions.AnyAsync(x => x.SurveyId == survey.Id && x.LeadId == null));
    }

    /// <summary>Ikkinchi bosish ikkinchi o'quvchini yaratmaydi — lid allaqachon yo'q, 404.</summary>
    [Fact]
    public async Task Ikkinchi_marta_aylantirish_404_va_ikkinchi_oquvchi_yoq()
    {
        var name = $"Enrol Twice {Guid.NewGuid():N}"[..26];
        var (_, leadId) = await SeedLeadAsync(name);
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsJsonAsync(Enrol(leadId), Body(name))).StatusCode);
        var again = await admin.PostAsJsonAsync(Enrol(leadId), Body(name));

        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        await using var db = NewDb();
        Assert.Equal(1, await db.Students.CountAsync(s => s.FullName == name));
    }

    /// <summary>Tekshiruvdan o'tmagan so'rov hech narsa yozmaydi: o'quvchi ham yo'q, lid ham o'chmagan.</summary>
    [Fact]
    public async Task Sinf_ham_mojal_ham_yoq_bolsa_400_va_hech_narsa_yozilmaydi()
    {
        var name = $"Enrol Bad {Guid.NewGuid():N}"[..24];
        var (_, leadId) = await SeedLeadAsync(name);
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);

        var response = await admin.PostAsJsonAsync(Enrol(leadId), Body(name, className: ""));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var db = NewDb();
        Assert.False(await db.Students.AnyAsync(s => s.FullName == name));
        Assert.True(await db.Leads.AnyAsync(l => l.Id == leadId));
    }

    [Fact]
    public async Task Yoq_lid_404()
    {
        using var admin = await fixture.Api.ClientAsAsync(Roles.Admin);
        var response = await admin.PostAsJsonAsync(Enrol(Guid.NewGuid().ToString()), Body("Yoq"));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    /// <summary>
    /// O'quvchi yaratiladi (<c>students</c>) VA lid o'zgaradi (<c>leads</c>) — xodimga
    /// ikkalasi ham kerak. Rad etilgan so'rov lidni o'chirmaydi.
    /// </summary>
    [Theory]
    [InlineData("admin", "", HttpStatusCode.OK)]
    [InlineData("staff", "students,leads", HttpStatusCode.OK)]
    [InlineData("staff", "students", HttpStatusCode.Forbidden)]
    [InlineData("staff", "leads", HttpStatusCode.Forbidden)]
    [InlineData("teacher", "", HttpStatusCode.Forbidden)]
    [InlineData("cashier", "", HttpStatusCode.Forbidden)]
    [InlineData(null, "", HttpStatusCode.Unauthorized)]
    public async Task Ruxsat_students_va_leads_ikkalasi_kerak(string? role, string perms, HttpStatusCode expected)
    {
        var name = $"Enrol Rbac {Guid.NewGuid():N}"[..25];
        var (_, leadId) = await SeedLeadAsync(name);
        using var client = role is null
            ? fixture.Api.AnonymousClient()
            : await fixture.Api.ClientAsAsync(role, perms.Split(',', StringSplitOptions.RemoveEmptyEntries));

        var response = await client.PostAsJsonAsync(Enrol(leadId), Body(name));

        Assert.Equal(expected, response.StatusCode);
        await using var db = NewDb();
        var gone = !await db.Leads.AnyAsync(l => l.Id == leadId);
        Assert.Equal(expected == HttpStatusCode.OK, gone);
    }
}
