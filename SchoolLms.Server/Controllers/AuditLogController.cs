using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

// ===========================================================================
//  Boshqaruv → "O'zgarishlar tarixi" (docs/modules/audit-log.md, 2026-10-15).
//
//    GET /api/admin/audit/log       — the log, newest first, keyset pages of 50 (≤ 200) with the total
//    GET /api/admin/audit/actors    — people who appear in the log (the "Xodim" filter)
//    GET /api/admin/audit/sections  — the "Bo'lim" filter
//    GET /api/admin/audit/export    — the filtered view as .xlsx (≤ 10 000 rows)
//
//  Its own key `audit`, read gated: the whole school's history is not a page any staff member
//  may read just because they have some admin section. Finance and salary rows need the
//  finance right on top (AuditAccess.CanSeeMoney) — without it they are not returned and
//  amounts are stripped from the rest.
// ===========================================================================

[ApiController]
[Authorize]
[AdminPerm(AuditAccess.Permission, GatedRead = true)]
[Route("api/admin/audit")]
public class AuditLogController(AppDbContext db) : ControllerBase
{
    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 200;
    public const int MaxExportRows = 10_000;
    private const string XlsxMime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    [HttpGet("log")]
    public async Task<ActionResult<AuditPageDto>> Log(
        [FromQuery] string? from, [FromQuery] string? to, [FromQuery] string? actorId,
        [FromQuery] string? section, [FromQuery] string? kind, [FromQuery] string? action,
        [FromQuery] string? studentId, [FromQuery] string? teacherId, [FromQuery] string? q,
        [FromQuery] string? cursor, [FromQuery] int? limit, CancellationToken ct = default)
    {
        var money = User.CanSeeMoney();
        var filtered = AuditAccess.Apply(db.AuditLogs.AsNoTracking(),
            new AuditFilter(StudentId: studentId, TeacherId: teacherId, Action: action, From: from, To: to,
                ActorId: actorId, Section: section, Kind: kind, Q: q), money);
        var take = Math.Clamp(limit is > 0 ? limit.Value : DefaultPageSize, 1, MaxPageSize);

        var total = await filtered.CountAsync(ct);

        var page = filtered;
        if (ParseCursor(cursor) is ({ } ts, { } id))
            page = page.Where(a => string.Compare(a.Timestamp, ts) < 0
                                   || (a.Timestamp == ts && string.Compare(a.Id, id) < 0));
        // One extra row tells whether there is a next page.
        var rows = await AuditAccess.Newest(page).Take(take + 1).ToListAsync(ct);
        var hasMore = rows.Count > take;
        if (hasMore) rows.RemoveAt(rows.Count - 1);

        var (students, teachers) = await NamesAsync(rows, ct);
        var items = rows.Select(a => AuditAccess.ToEntryDto(a, money, students, teachers)).ToList();
        var next = hasMore ? $"{rows[^1].Timestamp}|{rows[^1].Id}" : null;
        return new AuditPageDto(items, total, next);
    }

    /// <summary>People who appear in the log, most active first. Only rows the caller may see count.</summary>
    [HttpGet("actors")]
    public async Task<ActionResult<IEnumerable<AuditActorDto>>> Actors(CancellationToken ct = default)
    {
        var q = AuditAccess.Apply(db.AuditLogs.AsNoTracking(), new AuditFilter(), User.CanSeeMoney());
        var groups = await q
            .GroupBy(a => a.ActorId)
            .Select(g => new { g.Key, Name = g.Max(a => a.ActorName), Count = g.Count() })
            .ToListAsync(ct);

        // The current name when the account still exists; the name written in the log otherwise.
        var ids = groups.Where(g => g.Key != null).Select(g => g.Key!).ToList();
        var current = await db.Users.AsNoTracking().Where(u => ids.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FullName, ct);

        return groups
            .Select(g => g.Key is null
                ? new AuditActorDto(AuditAccess.SystemActor, "Tizim", g.Count)
                : new AuditActorDto(g.Key, current.GetValueOrDefault(g.Key) ?? g.Name ?? "Noma'lum", g.Count))
            .OrderByDescending(a => a.Count).ThenBy(a => a.Name, StringComparer.CurrentCulture)
            .ToList();
    }

    [HttpGet("sections")]
    public ActionResult<IEnumerable<AuditSectionDto>> Sections() =>
        AuditCatalog.Sections.Select(s => new AuditSectionDto(s.Key, s.Label)).ToList();

    /// <summary>The filtered view as Excel — newest first, the same columns as the screen.</summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export(
        [FromQuery] string? from, [FromQuery] string? to, [FromQuery] string? actorId,
        [FromQuery] string? section, [FromQuery] string? kind, [FromQuery] string? action,
        [FromQuery] string? studentId, [FromQuery] string? teacherId, [FromQuery] string? q,
        CancellationToken ct = default)
    {
        var money = User.CanSeeMoney();
        var filtered = AuditAccess.Apply(db.AuditLogs.AsNoTracking(),
            new AuditFilter(StudentId: studentId, TeacherId: teacherId, Action: action, From: from, To: to,
                ActorId: actorId, Section: section, Kind: kind, Q: q), money);
        var total = await filtered.CountAsync(ct);
        var rows = await AuditAccess.Newest(filtered).Take(MaxExportRows).ToListAsync(ct);
        var (students, teachers) = await NamesAsync(rows, ct);

        var headers = new[] { "Vaqt", "Xodim", "Bo'lim", "Amal", "Yozuv", "O'quvchi / o'qituvchi", "O'zgarish" };
        var lines = rows.Select(a =>
        {
            var e = AuditAccess.ToEntryDto(a, money, students, teachers);
            var kindText = AuditCatalog.KindLabel(e.Kind) + (e.ActionLabel is { } sub ? $" · {sub}" : "");
            return (IReadOnlyList<ExcelExport.XlsxCell>)new ExcelExport.XlsxCell[]
            {
                e.Timestamp.Replace('T', ' '), e.ActorName, e.SectionLabel, kindText, e.Summary,
                e.StudentName ?? e.TeacherName ?? "", Change(e.Before, e.After),
            };
        });
        var title = $"O'zgarishlar tarixi — {AppClock.Now:dd.MM.yyyy HH:mm}"
                    + (total > rows.Count ? $" (eng so'nggi {rows.Count} ta, jami {total})" : $" ({total} ta)");
        var bytes = ExcelExport.BuildTables(
        [
            new ExcelExport.TableSpec("Tarix", headers, lines, Title: title,
                Widths: [18, 24, 14, 22, 60, 26, 60]),
        ]);
        return File(bytes, XlsxMime, $"ozgarishlar_tarixi_{AppClock.Now:yyyy-MM-dd}.xlsx");
    }

    // ------------------------------------------------------------------

    private static (string Timestamp, string Id)? ParseCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return null;
        var i = cursor.IndexOf('|');
        return i <= 0 || i == cursor.Length - 1 ? null : (cursor[..i], cursor[(i + 1)..]);
    }

    /// <summary>Pupil and teacher names for the rows' links — two queries per page, not one per row.</summary>
    private async Task<(Dictionary<string, string> Students, Dictionary<string, string> Teachers)> NamesAsync(
        List<AuditLog> rows, CancellationToken ct)
    {
        var studentIds = rows.Where(r => r.StudentId != null).Select(r => r.StudentId!).Distinct().ToList();
        var teacherIds = rows.Where(r => r.TeacherId != null).Select(r => r.TeacherId!).Distinct().ToList();
        var students = studentIds.Count == 0 ? new Dictionary<string, string>() : await db.Students.AsNoTracking()
            .Where(s => studentIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.FullName, ct);
        var teachers = teacherIds.Count == 0 ? new Dictionary<string, string>() : await db.Teachers.AsNoTracking()
            .Where(t => teacherIds.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.FullName, ct);
        return (students, teachers);
    }

    /// <summary>"Telefon: 90… → 91…; Manzil: — → Chilonzor" — before/after in one cell.</summary>
    internal static string Change(string? before, string? after)
    {
        var b = Flat(before);
        var a = Flat(after);
        var keys = b.Keys.Union(a.Keys).ToList();
        if (keys.Count == 0) return "";
        return string.Join("; ", keys.Select(k =>
        {
            var hasB = b.TryGetValue(k, out var ov);
            var hasA = a.TryGetValue(k, out var nv);
            return hasB && hasA ? $"{k}: {ov} → {nv}" : $"{k}: {(hasA ? nv : ov)}";
        }));
    }

    private static Dictionary<string, string> Flat(string? json)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrEmpty(json)) return result;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) return result;
            foreach (var p in doc.RootElement.EnumerateObject())
                result[p.Name] = p.Value.ValueKind switch
                {
                    JsonValueKind.Null => "—",
                    JsonValueKind.String => p.Value.GetString() is { Length: > 0 } s ? s : "—",
                    JsonValueKind.True => "Ha",
                    JsonValueKind.False => "Yo'q",
                    _ => p.Value.GetRawText(),
                };
        }
        catch (JsonException)
        {
            // Not an object we can read — the cell stays empty rather than showing raw text.
        }
        return result;
    }
}
