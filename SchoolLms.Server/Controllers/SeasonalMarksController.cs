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
/// <b>Mavsumiy baholash</b> — the admin surface of seasonal assessment
/// (docs/modules/admission-and-testing.md §3.3 screens 10–13, contract §6.6).
///
/// <para>
/// <b>Permission — <c>seasonalMarks</c></b> (§4.4). <see cref="AdminPermAttribute"/>
/// without <c>GatedRead</c>: admin and superadmin always; any staff account may
/// READ (the list, the pivot, the coverage report and their exports hold no
/// secret), and only staff holding the key may write — bulk, inline edit,
/// delete. Teachers use <see cref="TeacherSeasonalMarksController"/>; here they
/// are refused. An admin has no "teaches that class" restriction (§8.6).
/// </para>
///
/// <para>
/// <b>No history endpoint.</b> The history dialog reads the general audit log
/// (<c>GET /api/admin/audit?entityType=SeasonalMark&amp;entityId=…</c>, §5.12),
/// which is admin/superadmin only; this unit does not widen it.
/// </para>
///
/// <para>
/// <b>DI:</b> none added. The service and both reports are stateless over the
/// context and are built per request, as <see cref="SurveySubmissionsController"/>
/// builds its query; the audit writer comes from DI.
/// </para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("seasonalMarks")]
[Route("api/admin/seasonal-marks")]
public sealed class SeasonalMarksController(AppDbContext db, AuditService audit) : ControllerBase
{
    private const string XlsxMime =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>§13 Q5: the scale is named wherever a score column appears.</summary>
    private const string ScoreHeader = "Ball (0–100)";

    private readonly SeasonalMarkService _marks = new(db, audit);
    private readonly SeasonalPivotQuery _pivot = new(db);
    private readonly SeasonalCoverageReport _coverage = new(db);

    private string? Uid => User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

    // ------------------------------------------------------------------
    //  Screen 10 — the list
    // ------------------------------------------------------------------

    /// <summary><c>GET /api/admin/seasonal-marks</c> — one page of stored marks, newest change first.</summary>
    [HttpGet]
    public Task<ActionResult<SeasonalPageDto<SeasonalMarkRowDto>>> List(
        [FromQuery] SeasonalMarkFilter filter, CancellationToken ct = default) =>
        SeasonalMarkHttp.RunAsync(() => _marks.PageAsync(filter, ct));

    /// <summary><c>PUT /api/admin/seasonal-marks/{id}</c> — inline edit; the body is the full new state.</summary>
    [HttpPut("{id}")]
    public Task<ActionResult<SeasonalMarkRowDto>> Update(
        string id, SeasonalMarkUpdateRequest req, CancellationToken ct = default) =>
        SeasonalMarkHttp.RunAsync(() => _marks.UpdateAsync(id, req, ct));

    /// <summary><c>DELETE /api/admin/seasonal-marks/{id}</c> — 204; the audit row keeps what was deleted.</summary>
    [HttpDelete("{id}")]
    public Task<IActionResult> Delete(string id, CancellationToken ct = default) =>
        SeasonalMarkHttp.RunAsync(() => _marks.DeleteAsync(id, ct));

    /// <summary>
    /// <c>GET /api/admin/seasonal-marks/export</c> — the WHOLE filter as .xlsx,
    /// capped at <see cref="SeasonalMarkService.MaxExportRows"/>; when the cap
    /// bites the file says so on its last line.
    /// </summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] SeasonalMarkFilter filter, CancellationToken ct = default)
    {
        SeasonalMarkExport result;
        try
        {
            result = await _marks.ExportAsync(filter, ct);
        }
        catch (SeasonalMarkException ex)
        {
            return SeasonalMarkHttp.Fail(ex);
        }

        string[] headers =
            ["O'quvchi", "Sinf", "Tur", "Davr", "Fan", ScoreHeader, "Izoh", "O'zgartirilgan", "Kiritgan"];

        var rows = result.Rows.Select(MarkCells);

        // The cap bit. Say so IN the file: a truncated export that looks whole
        // is a report someone will trust.
        if (result.Rows.Count < result.Total)
        {
            IReadOnlyList<ExcelExport.XlsxCell> note =
            [
                $"Ro'yxat {SeasonalMarkService.MaxExportRows} qator bilan cheklandi "
                    + $"(filtrga mos jami {result.Total} ta). Davr yoki sinf bo'yicha toraytiring.",
            ];
            rows = rows.Append(note);
        }

        var bytes = ExcelExport.BuildTable("Mavsumiy baholash", headers, rows);
        return File(bytes, XlsxMime, $"mavsumiy_baholash_{AppClock.Now:yyyy-MM-dd}.xlsx");
    }

    // ------------------------------------------------------------------
    //  Screen 11 — bulk entry
    // ------------------------------------------------------------------

    /// <summary>
    /// <c>GET /api/admin/seasonal-marks/scope?classId=</c> — live classes and the
    /// (class, subject, teacher) triples of the current schedule.
    /// </summary>
    [HttpGet("scope")]
    public Task<ActionResult<SeasonalScopeDto>> Scope([FromQuery] string? classId, CancellationToken ct = default) =>
        SeasonalMarkHttp.RunAsync(() => _marks.ScopeAsync(classId, teacherId: null, ct));

    /// <summary><c>GET /api/admin/seasonal-marks/students</c> — the entry grid, stored marks prefilled.</summary>
    [HttpGet("students")]
    public Task<ActionResult<IReadOnlyList<SeasonalEntryRowDto>>> Students(
        [FromQuery] SeasonalStudentsQuery query, CancellationToken ct = default) =>
        SeasonalMarkHttp.RunAsync(() => _marks.StudentsAsync(query, ct));

    /// <summary><c>POST /api/admin/seasonal-marks/bulk</c> — upsert the grid; an empty row deletes.</summary>
    [HttpPost("bulk")]
    public Task<ActionResult<SeasonalBulkResultDto>> Bulk(SeasonalBulkRequest req, CancellationToken ct = default) =>
        SeasonalMarkHttp.RunAsync(() => _marks.BulkAsync(req, Uid, ct));

    // ------------------------------------------------------------------
    //  Screen 12 — by subjects
    // ------------------------------------------------------------------

    /// <summary>
    /// <c>GET /api/admin/seasonal-marks/by-subjects</c> — pupils × chosen subjects.
    /// <c>classIds</c> / <c>subjectIds</c> are repeated query keys.
    /// </summary>
    [HttpGet("by-subjects")]
    public Task<ActionResult<SeasonalPivotPageDto>> BySubjects(
        [FromQuery] SeasonalPivotFilter filter, CancellationToken ct = default) =>
        SeasonalMarkHttp.RunAsync(() => _pivot.PageAsync(filter, ct));

    /// <summary><c>GET /api/admin/seasonal-marks/by-subjects/export</c> — the whole selection as .xlsx.</summary>
    [HttpGet("by-subjects/export")]
    public async Task<IActionResult> BySubjectsExport(
        [FromQuery] SeasonalPivotFilter filter, CancellationToken ct = default)
    {
        IReadOnlyList<SeasonalPivotColumnDto> columns;
        IReadOnlyList<SeasonalPivotRowDto> pivot;
        try
        {
            (columns, pivot) = await _pivot.AllAsync(filter, ct);
        }
        catch (SeasonalMarkException ex)
        {
            return SeasonalMarkHttp.Fail(ex);
        }

        string[] headers = ["O'quvchi", "Sinf", .. columns.Select(c => $"{c.Name} — {ScoreHeader}")];
        var rows = pivot.Select(r => PivotCells(r, columns));

        var bytes = ExcelExport.BuildTable("Fanlar kesimida", headers, rows);
        return File(bytes, XlsxMime, $"mavsumiy_baholash_fanlar_{AppClock.Now:yyyy-MM-dd}.xlsx");
    }

    // ------------------------------------------------------------------
    //  Screen 13 — coverage report
    // ------------------------------------------------------------------

    /// <summary>
    /// <c>GET /api/admin/seasonal-marks/coverage</c> — per-teacher coverage;
    /// <c>teacherIds</c> is a repeated key, absent = every teacher.
    /// </summary>
    [HttpGet("coverage")]
    public Task<ActionResult<SeasonalPageDto<SeasonalCoverageRowDto>>> Coverage(
        [FromQuery] SeasonalCoverageFilter filter, CancellationToken ct = default) =>
        SeasonalMarkHttp.RunAsync(() => _coverage.PageAsync(filter, ct));

    /// <summary><c>GET /api/admin/seasonal-marks/coverage/detail</c> — the pupils behind one teacher's numbers.</summary>
    [HttpGet("coverage/detail")]
    public Task<ActionResult<SeasonalPageDto<SeasonalCoverageDetailRowDto>>> CoverageDetail(
        [FromQuery] SeasonalCoverageDetailFilter filter, CancellationToken ct = default) =>
        SeasonalMarkHttp.RunAsync(() => _coverage.DetailAsync(filter, ct));

    /// <summary><c>GET /api/admin/seasonal-marks/coverage/export</c> — every teacher row as .xlsx.</summary>
    [HttpGet("coverage/export")]
    public async Task<IActionResult> CoverageExport(
        [FromQuery] SeasonalCoverageFilter filter, CancellationToken ct = default)
    {
        IReadOnlyList<SeasonalCoverageRowDto> result;
        try
        {
            result = await _coverage.AllAsync(filter, ct);
        }
        catch (SeasonalMarkException ex)
        {
            return SeasonalMarkHttp.Fail(ex);
        }

        // Counts as text, like the other non-money registers: the only numeric
        // style ExcelExport has is the money format, which would print "30.00".
        string[] headers = ["F.I.SH", "Jami o'quvchi", "Baholangan", "Baholanmagan", "Foiz"];
        var bytes = ExcelExport.Build("Hisobot", headers, result.Select(CoverageCells));
        return File(bytes, XlsxMime, $"mavsumiy_baholash_hisoboti_{AppClock.Now:yyyy-MM-dd}.xlsx");
    }

    // ------------------------------------------------------------------
    //  Export rows
    // ------------------------------------------------------------------

    /// <summary>
    /// One list row. The score is a real number cell, so the school can sort and
    /// average the column in Excel; everything else is text.
    /// </summary>
    private static IReadOnlyList<ExcelExport.XlsxCell> MarkCells(SeasonalMarkRowDto r) =>
    [
        r.Student.FullName,
        r.Class.Name,
        SeasonalLabels.Kind(r.PeriodKind),
        r.PeriodLabel,
        r.Subject.Name,
        ExcelExport.XlsxCell.Num(r.Score),
        r.Comment,
        ScreenStamp(r.UpdatedAt),
        r.CreatedByName,
    ];

    /// <summary>One pivot row. A subject without a score is an EMPTY cell, never 0.</summary>
    private static IReadOnlyList<ExcelExport.XlsxCell> PivotCells(
        SeasonalPivotRowDto r, IReadOnlyList<SeasonalPivotColumnDto> columns) =>
    [
        r.FullName,
        r.ClassName,
        .. columns.Select(c => r.Scores.TryGetValue(c.SubjectId, out var score)
            ? ExcelExport.XlsxCell.Num(score)
            : ExcelExport.XlsxCell.Of(null)),
    ];

    private static IReadOnlyList<string> CoverageCells(SeasonalCoverageRowDto r) =>
    [
        r.FullName,
        Count(r.TotalStudents),
        Count(r.Marked),
        Count(r.Unmarked),
        $"{Count(r.Percent)}%",
    ];

    private static string Count(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>"2026-09-22T10:15:30" → "22.09.2026 10:15", the way the screen prints it.</summary>
    private static string ScreenStamp(string iso) =>
        DateTime.TryParseExact(iso, "yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var at)
            ? at.ToString("dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture)
            : iso;
}

/// <summary>
/// Maps <see cref="SeasonalMarkException"/> to the house error shape
/// (<c>{ message }</c>, §6 "Errors") — shared by the admin and teacher
/// controllers so both answer a refused rule with the same status.
/// </summary>
internal static class SeasonalMarkHttp
{
    public static ActionResult Fail(SeasonalMarkException ex) => ex.Error switch
    {
        SeasonalMarkError.NotFound => new NotFoundObjectResult(new { message = ex.Message }),
        SeasonalMarkError.Conflict => new ConflictObjectResult(new { message = ex.Message }),
        _ => new BadRequestObjectResult(new { message = ex.Message }),
    };

    /// <summary>Runs a query or command and returns its value, or the mapped refusal.</summary>
    public static async Task<ActionResult<T>> RunAsync<T>(Func<Task<T>> action)
    {
        try
        {
            // The constructor, not the implicit conversion: C# has no user-defined
            // conversion from an interface type (IReadOnlyList<…>).
            return new ActionResult<T>(await action());
        }
        catch (SeasonalMarkException ex)
        {
            return Fail(ex);
        }
    }

    /// <summary>Runs a command with no result: 204, or the mapped refusal.</summary>
    public static async Task<IActionResult> RunAsync(Func<Task> action)
    {
        try
        {
            await action();
            return new NoContentResult();
        }
        catch (SeasonalMarkException ex)
        {
            return Fail(ex);
        }
    }
}
