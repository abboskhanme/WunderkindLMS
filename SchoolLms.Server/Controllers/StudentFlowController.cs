using System.Globalization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

// ===========================================================================
//  O'quv bo'limi → "O'quvchilar oqimi" (docs/modules/student-flow.md, 2026-10-15).
//
//    GET /api/admin/reports/student-flow?from&to[&classId]          — totals, reasons, classes, lists
//    GET /api/admin/reports/student-flow/export?from&to[&classId]   — the same as .xlsx
//    PUT /api/admin/reports/student-flow/events/{id}/reason         — a departure's reason, later
//
//  The `students` key, read gated: the lists name pupils and why they left — the same people
//  who may open the pupil list (students or students:view). Changing a reason needs the full key.
// ===========================================================================

[ApiController]
[Authorize]
[AdminPerm("students", GatedRead = true)]
[Route("api/admin/reports/student-flow")]
public class StudentFlowController(AppDbContext db, AuditService audit) : ControllerBase
{
    private const string XlsxMime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    public const string BadPeriodMessage =
        "Davr noto'g'ri: from va to (yyyy-MM-dd) kerak, from ≤ to, ko'pi bilan 1100 kun.";

    [HttpGet]
    public async Task<ActionResult<StudentFlowReportDto>> Get(
        [FromQuery] string? from, [FromQuery] string? to, [FromQuery] string? classId, CancellationToken ct = default)
    {
        if (Period(from, to) is not ({ } start, { } end)) return BadRequest(new { message = BadPeriodMessage });
        return await StudentFlowReport.BuildAsync(db, start, end, classId, ct);
    }

    [HttpGet("export")]
    public async Task<IActionResult> Export(
        [FromQuery] string? from, [FromQuery] string? to, [FromQuery] string? classId, CancellationToken ct = default)
    {
        if (Period(from, to) is not ({ } start, { } end)) return BadRequest(new { message = BadPeriodMessage });
        var r = await StudentFlowReport.BuildAsync(db, start, end, classId, ct);
        var title = $"O'quvchilar oqimi — {Show(r.From)} – {Show(r.To)}" + (r.ClassName is null ? "" : $" · {r.ClassName}");

        static IReadOnlyList<ExcelExport.XlsxCell> Cells(params ExcelExport.XlsxCell[] cells) => cells;
        IEnumerable<IReadOnlyList<ExcelExport.XlsxCell>> List(IEnumerable<StudentFlowRowDto> rows, bool withReason) =>
            rows.Select(x => withReason
                ? Cells(x.StudentName, x.ClassName ?? "", Show(x.Date), x.Reason ?? StudentFlowReport.OtherReason, x.ReasonText ?? "", x.ActorName ?? "")
                : Cells(x.StudentName, x.ClassName ?? "", Show(x.Date), x.ActorName ?? ""));
        string[] plain = ["O'quvchi", "Sinf", "Sana", "Kim"];

        var t = r.Totals;
        var bytes = ExcelExport.BuildTables(
        [
            new ExcelExport.TableSpec("Umumiy", ["Ko'rsatkich", "Soni"],
            [
                Cells("Davr boshida", ExcelExport.XlsxCell.Num(t.HeadcountStart)),
                Cells("Yangi kelganlar", ExcelExport.XlsxCell.Num(t.New)),
                Cells("Faollashganlar (birinchi to'lov)", ExcelExport.XlsxCell.Num(t.Activated)),
                Cells("Qaytganlar", ExcelExport.XlsxCell.Num(t.Returned)),
                Cells("Ketganlar", ExcelExport.XlsxCell.Num(t.Left)),
                Cells("Sof o'zgarish", ExcelExport.XlsxCell.Num(t.Net)),
                Cells("Davr oxirida", ExcelExport.XlsxCell.Num(t.HeadcountEnd)),
            ], Title: title, Widths: [34, 12]),
            new ExcelExport.TableSpec("Sabablar", ["Sabab", "Soni", "Ulush, %"],
                r.ByReason.Select(x => Cells(x.Name, ExcelExport.XlsxCell.Num(x.Count), ExcelExport.XlsxCell.Num((decimal)x.Percent))),
                Title: title, Widths: [34, 10, 10]),
            new ExcelExport.TableSpec("Sinflar", ["Sinf", "Yangi", "Qaytgan", "Ketgan", "Sof"],
                r.ByClass.Select(x => Cells(x.ClassName, ExcelExport.XlsxCell.Num(x.New), ExcelExport.XlsxCell.Num(x.Returned),
                    ExcelExport.XlsxCell.Num(x.Left), ExcelExport.XlsxCell.Num(x.Net))),
                Title: title, Widths: [16, 10, 10, 10, 10]),
            new ExcelExport.TableSpec("Yangi kelganlar", plain, List(r.NewList, false), Title: title, Widths: [34, 12, 12, 26]),
            new ExcelExport.TableSpec("Faollashganlar", plain, List(r.ActivatedList, false), Title: title, Widths: [34, 12, 12, 26]),
            new ExcelExport.TableSpec("Ketganlar", ["O'quvchi", "Sinf", "Sana", "Sabab", "Izoh", "Kim"],
                List(r.LeftList, true), Title: title, Widths: [34, 12, 12, 26, 40, 26]),
            new ExcelExport.TableSpec("Qaytganlar", plain, List(r.ReturnedList, false), Title: title, Widths: [34, 12, 12, 26]),
        ]);
        return File(bytes, XlsxMime, $"oquvchilar_oqimi_{r.From}_{r.To}.xlsx");
    }

    /// <summary>
    /// A departure's reason, set or corrected later. While the pupil is still archived and this is
    /// their latest departure, the card's archive reason follows (the archive list and the report
    /// must not disagree). Audited.
    /// </summary>
    [HttpPut("events/{id:guid}/reason")]
    public async Task<ActionResult<StudentFlowRowDto>> UpdateReason(
        Guid id, UpdateStudentEventReasonRequest req, CancellationToken ct = default)
    {
        var ev = await db.StudentEvents.FirstOrDefaultAsync(e => e.Id == id, ct);
        if (ev is null) return NotFound(new { message = "Yozuv topilmadi" });
        if (ev.Kind != StudentEventKind.Left)
            return BadRequest(new { message = "Sabab faqat ketgan o'quvchi yozuviga qo'yiladi" });
        if (!await new StudentArchiveService(db).ReasonIsUsableAsync(req.ReasonId, ct))
            return BadRequest(new { message = StudentArchiveService.ReasonNotFoundMessage });

        var reasons = await db.StudentArchiveReasons.AsNoTracking().ToDictionaryAsync(r => r.Id, r => r.Name, ct);
        string NameOf(Guid? rid) => rid is { } r ? reasons.GetValueOrDefault(r, StudentFlowReport.OtherReason) : StudentFlowReport.OtherReason;

        var before = new Dictionary<string, object?> { ["Sabab"] = NameOf(ev.ReasonId), ["Izoh"] = ev.ReasonText };
        ev.ReasonId = req.ReasonId;
        if (req.ReasonText is not null)
        {
            var text = req.ReasonText.Trim();
            ev.ReasonText = text.Length == 0 ? null : text.Length > 500 ? text[..500] : text;
        }
        var after = new Dictionary<string, object?> { ["Sabab"] = NameOf(ev.ReasonId), ["Izoh"] = ev.ReasonText };

        var student = await db.Students.FirstOrDefaultAsync(s => s.Id == ev.StudentId, ct);
        if (student is { IsArchived: true })
        {
            var latest = await db.StudentEvents.AsNoTracking()
                .Where(e => e.StudentId == ev.StudentId && e.Kind == StudentEventKind.Left)
                .OrderByDescending(e => e.OnDate).ThenByDescending(e => e.CreatedAt)
                .Select(e => e.Id).FirstOrDefaultAsync(ct);
            if (latest == ev.Id)
            {
                student.ArchiveReasonId = ev.ReasonId;
                if (ev.ReasonText is not null) student.ArchiveReason = ev.ReasonText;
            }
        }

        audit.RecordChanges(AuditService.EntityStudentEvent, ev.Id.ToString(),
            $"Ketish sababi o'zgardi ({ev.StudentName}, {ev.OnDate:dd.MM.yyyy})", before, after, studentId: ev.StudentId);
        await db.SaveChangesAsync(ct);

        return new StudentFlowRowDto(
            ev.Id, ev.StudentId, ev.StudentName, ev.ClassName, ev.OnDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ev.ReasonId, ev.ReasonId is { } rid2 ? reasons.GetValueOrDefault(rid2) : null, ev.ReasonText, ev.Source, ev.ActorName,
            student is not null);
    }

    private static (DateOnly From, DateOnly To)? Period(string? from, string? to)
    {
        if (!DateOnly.TryParseExact(from ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var start)
            || !DateOnly.TryParseExact(to ?? "", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var end)
            || start > end || end.DayNumber - start.DayNumber + 1 > StudentFlowReport.MaxDays)
            return null;
        return (start, end);
    }

    /// <summary>"2026-10-01" → "01.10.2026".</summary>
    private static string Show(string iso) =>
        DateOnly.TryParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture) : iso;
}
