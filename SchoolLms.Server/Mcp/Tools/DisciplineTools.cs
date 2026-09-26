using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;

namespace SchoolLms.Server.Mcp.Tools;

/// <summary>Discipline incidents and scores. Section: <c>discipline</c>.</summary>
[McpServerToolType]
public sealed class DisciplineTools(McpToolContext t)
{
    /// <summary>Every pupil starts at 100 — same rule as DisciplineController.BuildScoresAsync.</summary>
    private const int BaseScore = 100;

    [McpServerTool(Name = "discipline_incidents", Title = "Intizomiy harakatlar",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Qo'lda kiritilgan intizomiy ballar (rag'bat yoki jazo): sana, o'quvchi, sinf, sabab, ball, izoh, kim yozgan. "
        + "Davr (ko'pi bilan 366 kun), sinf va faqat manfiy/musbat filtri. Discipline incidents.")]
    public async Task<string> Incidents(
        [Description("Davr boshi YYYY-MM-DD (sukut — 30 kun oldin)")] string? from = null,
        [Description("Davr oxiri YYYY-MM-DD (sukut — bugun)")] string? to = null,
        [Description("Sinf nomi (ixtiyoriy)")] string? className = null,
        [Description("negative | positive (ixtiyoriy)")] string? sign = null,
        [Description("Sahifa")] int? page = null,
        [Description("Sahifa hajmi (1..200)")] int? pageSize = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.Discipline);
        var db = t.Db;
        var (f, tt) = McpToolContext.Range(from, to, 30);
        var fromIso = f.ToString("yyyy-MM-dd");
        var toIso = tt.AddDays(1).ToString("yyyy-MM-dd");
        var q = from p in db.DisciplinePoints
                join s in db.Students on p.StudentId equals s.Id
                where string.Compare(p.CreatedAt, fromIso) >= 0 && string.Compare(p.CreatedAt, toIso) < 0
                select new { p.CreatedAt, pupil = s.FullName, s.ClassName, reason = p.ReasonName, p.Points, p.Note, p.CreatedBy };
        if (!string.IsNullOrWhiteSpace(className)) q = q.Where(x => x.ClassName == className.Trim());
        if (sign == "negative") q = q.Where(x => x.Points < 0);
        else if (sign == "positive") q = q.Where(x => x.Points > 0);

        var total = await q.CountAsync(ct);
        var p0 = McpToolContext.Page(page);
        var size = McpToolContext.PageSize(pageSize);
        var rows = await q.OrderByDescending(x => x.CreatedAt).Skip((p0 - 1) * size).Take(size).ToListAsync(ct);
        var byReason = await q.GroupBy(x => x.reason).Select(g => new { reason = g.Key, count = g.Count(), points = g.Sum(x => x.Points) })
            .OrderByDescending(x => x.count).ToListAsync(ct);
        return t.Json(new { from = f, to = tt, total, page = p0, pageSize = size, byReason, rows }, rows.Count);
    }

    [McpServerTool(Name = "discipline_scores", Title = "Intizom ballari reytingi",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Faol o'quvchilarning JORIY o'quv yilidagi intizom ballari (100 dan boshlanadi): plus, minus, qoldi. Sinf bo'yicha filtr; "
        + "sort=lowest (sukut, eng past birinchi) yoki highest. Discipline scores per pupil.")]
    public async Task<string> Scores(
        [Description("Sinf nomi (ixtiyoriy)")] string? className = null,
        [Description("lowest | highest")] string sort = "lowest",
        [Description("Nechta qator (1..200, sukut 50)")] int? limit = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.Discipline);
        var db = t.Db;
        // Current academic year only (the web screen counts all history; an AI call should not
        // scan every journal row ever written). Year start = quarter 1 start, else 1 September.
        var yearStart = await AcademicYearStartAsync(ct);
        var students = await db.Students.Where(s => !s.IsArchived)
            .Where(s => className == null || s.ClassName == className.Trim())
            .Select(s => new { s.Id, s.FullName, s.ClassName }).ToListAsync(ct);
        var ids = students.Select(s => s.Id).ToList();
        var manual = await db.DisciplinePoints.Where(p => ids.Contains(p.StudentId) && string.Compare(p.CreatedAt, yearStart) >= 0)
            .Select(p => new { p.StudentId, p.Points }).ToListAsync(ct);
        var absPts = await db.AbsenceReasons.ToDictionaryAsync(r => r.Id, r => r.Points, ct);
        var journal = await db.JournalEntries.Where(e => e.ReasonId != null && ids.Contains(e.StudentId) && string.Compare(e.Date, yearStart) >= 0)
            .Select(e => new { e.StudentId, e.ReasonId }).ToListAsync(ct);

        var plus = new Dictionary<string, int>();
        var minus = new Dictionary<string, int>();
        void Apply(string sid, int pts)
        {
            if (pts > 0) plus[sid] = plus.GetValueOrDefault(sid) + pts;
            else if (pts < 0) minus[sid] = minus.GetValueOrDefault(sid) - pts;
        }
        foreach (var m in manual) Apply(m.StudentId, m.Points);
        foreach (var j in journal)
            if (absPts.TryGetValue(j.ReasonId!, out var pt)) Apply(j.StudentId, pt);

        var rows = students.Select(s => new
        {
            s.Id, s.FullName, s.ClassName, plus = plus.GetValueOrDefault(s.Id), minus = minus.GetValueOrDefault(s.Id),
            remaining = BaseScore + plus.GetValueOrDefault(s.Id) - minus.GetValueOrDefault(s.Id),
        });
        rows = sort == "highest" ? rows.OrderByDescending(r => r.remaining) : rows.OrderBy(r => r.remaining);
        var list = rows.Take(McpToolContext.PageSize(limit)).ToList();
        return t.Json(new { academicYearFrom = yearStart, pupils = students.Count, rows = list }, list.Count);
    }

    /// <summary>ISO date the current academic year started: quarter-1 start if configured and
    /// not in the future, otherwise the last 1 September.</summary>
    private async Task<string> AcademicYearStartAsync(CancellationToken ct)
    {
        var today = McpToolContext.Today;
        var fallback = new DateOnly(today.Month >= 9 ? today.Year : today.Year - 1, 9, 1);
        var q1 = await t.Db.Quarters.Where(q => q.Quarter == 1).Select(q => q.StartDate).FirstOrDefaultAsync(ct);
        return DateOnly.TryParseExact(q1, "yyyy-MM-dd", out var d) && d <= today && d > today.AddYears(-1)
            ? d.ToString("yyyy-MM-dd")
            : fallback.ToString("yyyy-MM-dd");
    }
}
