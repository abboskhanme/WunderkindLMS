using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;

namespace SchoolLms.Server.Mcp.Tools;

/// <summary>Leads, application-form submissions, admission candidates.
/// Sections: <c>leads</c>, <c>marketing</c>, <c>admission</c>.</summary>
[McpServerToolType]
public sealed class SalesTools(McpToolContext t)
{
    [McpServerTool(Name = "leads_list", Title = "Lidlar",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Qabul lidlari (bo'lajak o'quvchilar): ism, ota-ona, telefon, mo'ljaldagi sinf, bosqich, manba, yaratilgan sana. "
        + "Filtrlar: bosqich nomi, sinf, qidiruv. Leads list.")]
    public async Task<string> LeadsList(
        [Description("Bosqich nomi (ixtiyoriy)")] string? stage = null,
        [Description("Mo'ljaldagi sinf raqami (ixtiyoriy)")] int? targetGrade = null,
        [Description("Ism yoki telefon bo'lagi")] string? search = null,
        [Description("Sahifa")] int? page = null,
        [Description("Sahifa hajmi (1..200)")] int? pageSize = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.Leads);
        var db = t.Db;
        var stages = await db.LeadStages.OrderBy(s => s.Order).ToListAsync(ct);
        var titles = stages.ToDictionary(s => s.Id, s => s.Title);
        var q = db.Leads.AsQueryable();
        if (!string.IsNullOrWhiteSpace(stage))
        {
            var ids = stages.Where(s => s.Title.Contains(stage.Trim(), StringComparison.OrdinalIgnoreCase)).Select(s => s.Id).ToList();
            q = q.Where(l => ids.Contains(l.Stage));
        }
        if (targetGrade is { } g) q = q.Where(l => l.TargetGrade == g);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var n = search.Trim().ToLower();
            q = q.Where(l => l.FullName.ToLower().Contains(n) || l.ParentFullName.ToLower().Contains(n) || l.ParentPhone.Contains(n));
        }
        var total = await q.CountAsync(ct);
        var p = McpToolContext.Page(page);
        var size = McpToolContext.PageSize(pageSize);
        var rows = (await q.OrderByDescending(l => l.CreatedAt).Skip((p - 1) * size).Take(size).ToListAsync(ct))
            .Select(l => new
            {
                l.Id, l.FullName, l.Gender, l.BirthDate, l.ParentFullName, l.ParentPhone, l.TargetGrade,
                stage = titles.GetValueOrDefault(l.Stage, l.Stage), l.Source, l.AdmissionStatus, l.Note, l.CreatedAt,
            }).ToList();
        return t.Json(new { total, page = p, pageSize = size, stages = stages.Select(s => s.Title), rows }, rows.Count);
    }

    [McpServerTool(Name = "leads_funnel", Title = "Lidlar voronkasi",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Qabul voronkasi: bosqichlar bo'yicha lidlar soni va konversiya, manbalar, sinflar kesimi, o'quvchiga aylanganlar. "
        + "Leads funnel and conversion.")]
    public async Task<string> LeadsFunnel(CancellationToken ct = default)
    {
        t.Require(McpAreas.Leads);
        var f = await LeadFunnelQuery.BuildAsync(t.Db);
        return t.Json(f, f.Stages.Count);
    }

    [McpServerTool(Name = "survey_submissions", Title = "Ariza formasi topshiriqlari",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Ommaviy ariza formasi orqali kelgan arizalar: sana, forma, holat, ota-ona va farzand ma'lumotlari. "
        + "Davr (ko'pi bilan 366 kun), holat va qidiruv bo'yicha. Public application-form submissions.")]
    public async Task<string> SurveySubmissions(
        [Description("Davr boshi YYYY-MM-DD (sukut — 30 kun oldin)")] string? from = null,
        [Description("Davr oxiri YYYY-MM-DD (sukut — bugun)")] string? to = null,
        [Description("Holat (ixtiyoriy)")] string? status = null,
        [Description("Qidiruv (ism/telefon)")] string? search = null,
        [Description("Sahifa")] int? page = null,
        [Description("Sahifa hajmi (1..200)")] int? pageSize = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.Marketing);
        var (f, tt) = McpToolContext.Range(from, to, 30);
        var res = await new SurveySubmissionQuery(t.Db).PageAsync(new SurveySubmissionFilter
        {
            From = f, To = tt, Status = status, Q = search,
            Page = McpToolContext.Page(page), PageSize = McpToolContext.PageSize(pageSize),
        }, ct);
        var rows = res.Rows.Select(r => new
        {
            r.CreatedAt, survey = r.SurveyName, r.Status, r.ParentFullName, r.ParentPhone,
            r.StudentFullName, r.StudentGrade, r.StudentGender, r.StudentPhone, convertedToLead = r.LeadId != null,
        }).ToList();
        return t.Json(new { total = res.Total, rows }, rows.Count);
    }

    [McpServerTool(Name = "admission_candidates", Title = "Qabul nomzodlari va natijalari",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Qabul imtihoni nomzodlari: ism, mo'ljaldagi sinf, qabul holati, imtihon, ball va foiz. "
        + "Filtrlar: holat, sinf, qidiruv. Admission candidates and entrance-test results.")]
    public async Task<string> AdmissionCandidates(
        [Description("Qabul holati (ixtiyoriy)")] string? admissionStatus = null,
        [Description("Mo'ljaldagi sinf (ixtiyoriy)")] int? grade = null,
        [Description("Ism yoki telefon bo'lagi")] string? search = null,
        [Description("Sahifa")] int? page = null,
        [Description("Sahifa hajmi (1..200)")] int? pageSize = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.Admission);
        var res = await CandidateQuery.ListAsync(t.Db, new CandidateListQuery
        {
            AdmissionStatus = admissionStatus, Grade = grade, Search = search,
            Page = McpToolContext.Page(page), Limit = McpToolContext.PageSize(pageSize),
        }, ct);
        if (res.Error is { } err) throw new McpException("Filtr noto'g'ri: " + err);
        var pageDto = res.Value!;
        var rows = pageDto.Items.Select(c => new
        {
            c.FullName, c.ParentPhone, c.TargetGrade, c.AdmissionStatus, exam = c.ExamTitle,
            c.ParticipantStatus, c.TotalPoints, c.MaxPoints, c.Percent, enrolled = c.StudentId != null,
        }).ToList();
        return t.Json(new { total = pageDto.Total, page = pageDto.Page, pageSize = pageDto.Limit, rows }, rows.Count);
    }
}
