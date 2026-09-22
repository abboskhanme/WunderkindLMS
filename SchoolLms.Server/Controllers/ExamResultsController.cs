using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// <b>Natijalar</b> — manual result entry and the cross-exam results register
/// (<c>docs/modules/admission-and-testing.md</c> §3.2 screens 8–9, §6.3, §8.4).
/// Unit B2.
///
/// <para>
/// <b>Permission — <c>exams</c></b>, reads not gated (§4.4): the same gate as
/// <see cref="ExamsController"/>. Writing a score, importing a sheet and
/// nothing else here needs the claim.
/// </para>
///
/// <para>
/// <b>One write path.</b> The grid's save and the Excel import both end in
/// <see cref="ExamService.SaveEntryAsync"/>, which scores through
/// <see cref="ExamScoringService"/> — the engine unit B3 grades online papers
/// with (§2.1). The per-attempt review drawer's endpoint
/// (<c>/api/admin/exams/participants/{pid}/review</c>) is B3's.
/// </para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("exams")]
[Route("api/admin/exams")]
public sealed class ExamResultsController(AppDbContext db) : ControllerBase
{
    private const string XlsxMime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>Same ceiling as every other import in the repository.</summary>
    private const int MaxUpload = 10 * 1024 * 1024;

    public const string NoFileMessage = "Fayl tanlanmagan";
    public const string NotXlsxMessage = "Faqat .xlsx (Excel) fayl qabul qilinadi";

    // =====================================================================
    //  Entry grid (screen 9)
    // =====================================================================

    /// <summary>
    /// <c>GET /api/admin/exams/{id}/entry-table</c> — bare <see cref="ExamEntryTableDto"/>;
    /// 409 above 500 participants (split the exam), never a truncated grid.
    /// </summary>
    [HttpGet("{id:guid}/entry-table")]
    public async Task<ActionResult<ExamEntryTableDto>> EntryTable(string id, CancellationToken ct = default)
    {
        var result = await ExamService.GetEntryTableAsync(db, id, ct);
        return result.Error is { } e ? Fail(e) : result.Value!;
    }

    /// <summary>
    /// <c>POST /api/admin/exams/{id}/entry-table</c> <c>{ rows }</c> → <c>{ saved }</c>.
    /// All or nothing: 400 names the person and the subject that is out of range.
    /// </summary>
    [HttpPost("{id:guid}/entry-table")]
    public async Task<ActionResult<ExamEntrySaveResultDto>> SaveEntry(
        string id, ExamEntrySaveRequest request, CancellationToken ct = default)
    {
        var result = await ExamService.SaveEntryAsync(db, Actor, id, request.Rows, ct);
        return result.Error is { } e ? Fail(e) : result.Value!;
    }

    /// <summary>
    /// <c>GET /api/admin/exams/{id}/entry-table/template</c> — <c>natijalar_shablon.xlsx</c>,
    /// pre-filled with the participants and what is stored for them.
    /// </summary>
    [HttpGet("{id:guid}/entry-table/template")]
    public async Task<IActionResult> Template(string id, CancellationToken ct = default)
    {
        var result = await ResultImportService.TemplateAsync(db, id, ct);
        if (result.Error is { } e) return Fail(e);
        return File(result.Value!, XlsxMime, ResultImportService.TemplateFileName);
    }

    /// <summary>
    /// <c>POST /api/admin/exams/{id}/entry-table/import</c> — multipart <c>file</c>,
    /// <c>dryRun</c>. The browser posts the same file twice (§6.2): first
    /// <c>dryRun=true</c> (nothing is written), then <c>dryRun=false</c>.
    ///
    /// <para>
    /// A request without <c>dryRun</c> is treated as a DRY RUN. Of the two
    /// possible mistakes — a forgotten flag that writes a class's results, or a
    /// forgotten flag that only checks them — only the second is harmless.
    /// </para>
    /// </summary>
    [HttpPost("{id:guid}/entry-table/import")]
    [RequestSizeLimit(MaxUpload)]
    public async Task<ActionResult<ExamResultImportResultDto>> Import(
        string id, IFormFile? file, [FromForm] bool? dryRun, CancellationToken ct = default)
    {
        if (file is null || file.Length == 0) return BadRequest(new { code = "no_file", message = NoFileMessage });
        if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { code = "not_xlsx", message = NotXlsxMessage });

        await using var stream = file.OpenReadStream();
        var result = await ResultImportService.ImportAsync(
            db, Actor, id, Path.GetFileName(file.FileName), stream, dryRun ?? true, ct);
        return result.Error is { } e ? Fail(e) : result.Value!;
    }

    // =====================================================================
    //  Results register (screen 8)
    // =====================================================================

    /// <summary>
    /// <c>GET /api/admin/exams/results</c> — paged (§6 envelope), newest exam
    /// first, each row with its per-subject scores. No default status on the
    /// server; the screen sends <c>status=finished</c> (§3.2).
    /// </summary>
    [HttpGet("results")]
    public async Task<ActionResult<ExamPageDto<ExamResultRowDto>>> Results(
        [FromQuery] ExamResultListQuery query, CancellationToken ct = default)
    {
        var result = await ExamService.ListResultsAsync(db, query, ct);
        return result.Error is { } e ? Fail(e) : result.Value!;
    }

    /// <summary>
    /// <c>GET /api/admin/exams/results/export</c> — the WHOLE filter as
    /// <c>.xlsx</c>, not the page on screen. Points are real numbers in Excel
    /// (<see cref="ExcelExport.BuildTable"/>), so the sheet can be summed and
    /// sorted. With an <c>examId</c> every subject gets its own column; across
    /// exams the subjects differ, so they share one "Fanlar" column.
    /// Name: <c>natijalar_&lt;examId-short&gt;_&lt;yyyy-MM-dd&gt;.xlsx</c> (§6.3),
    /// <c>hammasi</c> in place of the id when no exam is chosen.
    /// </summary>
    [HttpGet("results/export")]
    public async Task<IActionResult> Export([FromQuery] ExamResultListQuery query, CancellationToken ct = default)
    {
        var result = await ExamService.ExportResultsAsync(db, query, ct);
        if (result.Error is { } e) return Fail(e);
        var (rows, total) = result.Value;

        var examId = ExamService.Clean(query.ExamId);
        var columns = examId is null ? null : await ExamService.ColumnsAsync(db, examId, ct);

        var headers = new List<string> { "Imtihon", "Sana", "F.I.SH", "Sinf", "Holati" };
        if (columns is null) headers.Add("Fanlar");
        else headers.AddRange(columns.Select(c => ResultImportService.SectionHeader(c.Name, c.MaxScore)));
        headers.AddRange(["Jami ball", "Maks. ball", "Foiz"]);

        var data = rows.Select(r =>
        {
            var cells = new List<ExcelExport.XlsxCell>
            {
                r.ExamTitle, Day(r.ExamDate), r.FullName, r.ClassName ?? "", ExamService.ParticipantStatusLabel(r.Status),
            };
            if (columns is null)
            {
                cells.Add(string.Join("; ", r.Scores.Where(s => s.Points is not null)
                    .Select(s => $"{s.Name}: {ExamScoringService.Format(s.Points!.Value)}/{ExamScoringService.Format(s.MaxPoints)}")));
            }
            else
            {
                cells.AddRange(columns.Select(c =>
                    ExcelExport.XlsxCell.Num(r.Scores.FirstOrDefault(s => s.SectionId == c.SectionId)?.Points)));
            }
            cells.Add(ExcelExport.XlsxCell.Num(r.TotalPoints));
            cells.Add(ExcelExport.XlsxCell.Num(r.MaxPoints));
            cells.Add(ExcelExport.XlsxCell.Num(r.Percent));
            return (IReadOnlyList<ExcelExport.XlsxCell>)cells;
        }).ToList();

        // The cap bit: say so IN the file — a short list that looks whole is the worse failure.
        if (rows.Count < total)
        {
            data.Add(
            [
                $"Ro'yxat {ExamService.MaxExportRows} qator bilan cheklandi (filtrga mos jami {total} ta). "
                    + "Imtihon yoki sinf bo'yicha toraytiring.",
            ]);
        }

        var bytes = ExcelExport.BuildTable("Natijalar", headers, data);
        var shortId = examId is null ? "hammasi" : examId[..Math.Min(8, examId.Length)];
        return File(bytes, XlsxMime, $"natijalar_{shortId}_{AppClock.Now:yyyy-MM-dd}.xlsx");
    }

    // ------------------------------------------------------------------

    /// <summary>"YYYY-MM-DD" → "DD.MM.YYYY" — the screen's format, without a Date round trip.</summary>
    private static string Day(string? iso) =>
        DateOnly.TryParseExact(iso, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)
            : iso ?? "";

    private ExamActor Actor => new(
        User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value,
        User.FindFirst(ClaimTypes.Name)?.Value ?? "Tizim");

    private ObjectResult Fail(ExamError error) => StatusCode(error.Status, error.ToBody());
}
