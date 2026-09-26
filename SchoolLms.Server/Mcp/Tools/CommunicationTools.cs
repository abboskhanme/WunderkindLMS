using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using ModelContextProtocol.Server;
using SchoolLms.Application.Services;

namespace SchoolLms.Server.Mcp.Tools;

/// <summary>Messages history (read only — nothing is ever sent), certificates, contracts.
/// Sections: <c>messages</c>, <c>students</c>, <c>contracts</c>.</summary>
[McpServerToolType]
public sealed class CommunicationTools(McpToolContext t)
{
    [McpServerTool(Name = "messages_history", Title = "Yuborilgan xabarlar tarixi",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Ilgari yuborilgan e'lon/xabarlar TARIXI (faqat o'qish — bu vosita hech narsa yubormaydi): sana, kimga (sinf yoki auditoriya), "
        + "matn, yuboruvchi, qabul qiluvchilar va yetkazilganlar soni. Davr ko'pi bilan 366 kun. Broadcast history.")]
    public async Task<string> MessagesHistory(
        [Description("Davr boshi YYYY-MM-DD (sukut — 30 kun oldin)")] string? from = null,
        [Description("Davr oxiri YYYY-MM-DD (sukut — bugun)")] string? to = null,
        [Description("Nechta qator (1..200, sukut 50)")] int? limit = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.Messages);
        var db = t.Db;
        var (f, tt) = McpToolContext.Range(from, to, 30);
        var start = f.ToDateTime(TimeOnly.MinValue);
        var end = tt.AddDays(1).ToDateTime(TimeOnly.MinValue);
        var take = McpToolContext.PageSize(limit);
        var broadcasts = await db.Broadcasts.Where(b => b.CreatedAt >= start && b.CreatedAt < end)
            .OrderByDescending(b => b.CreatedAt).Take(take)
            .Select(b => new { b.CreatedAt, channel = "telegram", audience = b.ClassName, title = (string?)null, text = b.Text, sender = b.SenderName, b.RecipientCount, b.SentCount })
            .ToListAsync(ct);
        var pushes = await db.PushMessages.Where(b => b.CreatedAt >= start && b.CreatedAt < end)
            .OrderByDescending(b => b.CreatedAt).Take(take)
            .Select(b => new { b.CreatedAt, channel = "push", audience = b.Audience, title = (string?)b.Title, text = b.Body, sender = b.SenderName, b.RecipientCount, b.SentCount })
            .ToListAsync(ct);
        var rows = broadcasts.Concat(pushes).OrderByDescending(r => r.CreatedAt).Take(take).ToList();
        return t.Json(new { from = f, to = tt, rows }, rows.Count);
    }

    [McpServerTool(Name = "certificates_list", Title = "Sertifikatlar",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("O'quvchilarning sertifikatlari (IELTS, olimpiada va h.k.): turi, fani, ball, berilgan va tugash sanasi, muddati o'tganmi. "
        + "Filtrlar: sinf, qidiruv, yaqin kunlarda muddati tugaydiganlar. Pupils' certificates.")]
    public async Task<string> CertificatesList(
        [Description("Sinf nomi (ixtiyoriy)")] string? className = null,
        [Description("O'quvchi ismi yoki hujjat raqami")] string? search = null,
        [Description("Shuncha kun ichida muddati tugaydiganlar (ixtiyoriy)")] int? expiringInDays = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.Certificates);
        var list = await CertificateService.ListAsync(t.Db, className: className, search: search,
            expiringInDays: expiringInDays is { } d ? Math.Clamp(d, 0, 3650) : null, ct: ct);
        var rows = list.Take(McpToolContext.MaxPageSize).Select(c => new
        {
            c.StudentName, c.ClassName, type = c.TypeName, subject = c.SubjectName, teacher = c.TeacherName,
            c.Number, c.Score, c.IssuedOn, c.ExpiresOn, c.IsExpired,
        }).ToList();
        return t.Json(new { total = list.Count, rows, truncated = list.Count > rows.Count }, rows.Count);
    }

    [McpServerTool(Name = "contracts_list", Title = "Shartnomalar",
        ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("O'quvchi shartnomalari: raqami, o'quvchi, sinf, imzolangan va tugash sanasi, manba. "
        + "Filtrlar: sinf, qidiruv, faqat muddati o'tganlar. Pupil contracts.")]
    public async Task<string> ContractsList(
        [Description("Sinf nomi (ixtiyoriy)")] string? className = null,
        [Description("O'quvchi ismi yoki shartnoma raqami")] string? search = null,
        [Description("Faqat muddati o'tganlar")] bool expiredOnly = false,
        [Description("Sahifa")] int? page = null,
        [Description("Sahifa hajmi (1..200)")] int? pageSize = null,
        CancellationToken ct = default)
    {
        t.Require(McpAreas.Contracts);
        var db = t.Db;
        var today = McpToolContext.Today;
        var q = from c in db.StudentContracts
                join s in db.Students on c.StudentId equals s.Id
                select new { c.Number, pupil = s.FullName, s.ClassName, c.SignedOn, c.EndsOn, c.Source, c.Comment, c.CreatedAt };
        if (!string.IsNullOrWhiteSpace(className)) q = q.Where(x => x.ClassName == className.Trim());
        if (!string.IsNullOrWhiteSpace(search))
        {
            var n = search.Trim().ToLower();
            q = q.Where(x => x.pupil.ToLower().Contains(n) || (x.Number != null && x.Number.ToLower().Contains(n)));
        }
        if (expiredOnly) q = q.Where(x => x.EndsOn != null && x.EndsOn < today);
        var total = await q.CountAsync(ct);
        var p = McpToolContext.Page(page);
        var size = McpToolContext.PageSize(pageSize);
        var rows = await q.OrderByDescending(x => x.CreatedAt).Skip((p - 1) * size).Take(size).ToListAsync(ct);
        return t.Json(new { total, page = p, pageSize = size, rows }, rows.Count);
    }
}
