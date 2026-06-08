using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;

namespace SchoolLms.Server.Controllers;

[ApiController]
[Authorize]
[AdminPerm("journal")]
[Route("api/admin/journal")]
public class JournalController(AppDbContext db, FcmService fcm) : ControllerBase
{
    /// <summary>Fanning chorakdagi darslari (sana + dars raqami). Bir kunda bir fan bir necha marta bo'lishi mumkin.</summary>
    [HttpGet("columns")]
    public async Task<ActionResult<IEnumerable<JournalColumnDto>>> GetColumns(
        [FromQuery] string classId, [FromQuery] string subjectId, [FromQuery] int quarter)
        => await JournalService.ComputeColumnsAsync(db, classId, subjectId, quarter);

    /// <summary>Berilgan sanada o'tilgan darslar (sinf+fan+dars raqami): ptichka yoki baho/davomat bo'lganlar.</summary>
    [HttpGet("conducted")]
    public async Task<ActionResult<IEnumerable<ConductedLessonDto>>> Conducted([FromQuery] string date)
    {
        var fromNotes = await db.LessonNotes
            .Where(n => n.Date == date && n.Conducted)
            .Select(n => new ConductedLessonDto(n.ClassId, n.SubjectId, n.Period, n.SubGroup))
            .ToListAsync();
        var fromEntries = await db.JournalEntries
            .Where(e => e.Date == date && (e.Grade != null || e.ReasonId != null))
            .Select(e => new ConductedLessonDto(e.ClassId, e.SubjectId, e.Period, e.SubGroup))
            .ToListAsync();
        return fromNotes.Concat(fromEntries).Distinct().ToList();
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<JournalEntryDto>>> GetEntries(
        [FromQuery] string classId, [FromQuery] string subjectId, [FromQuery] int quarter)
        => await JournalService.GetEntriesAsync(db, classId, subjectId, quarter);

    /// <summary>Bitta katakni belgilash — baho yoki davomat sababi (mavjud bo'lsa ustiga yoziladi).</summary>
    [HttpPut]
    public async Task<IActionResult> SetEntry(SetJournalEntryRequest req)
    {
        await JournalService.SetEntryAsync(db, req, fcm);
        return NoContent();
    }

    [HttpDelete]
    public async Task<IActionResult> ClearEntry(
        [FromQuery] string classId, [FromQuery] string subjectId, [FromQuery] int quarter,
        [FromQuery] string studentId, [FromQuery] string date, [FromQuery] int period)
    {
        await JournalService.ClearEntryAsync(db, classId, subjectId, quarter, studentId, date, period);
        return NoContent();
    }

    /* ---------- Mavzu va uyga vazifa ---------- */

    [HttpGet("notes")]
    public async Task<ActionResult<IEnumerable<JournalTopicDto>>> GetNotes(
        [FromQuery] string classId, [FromQuery] string subjectId, [FromQuery] int quarter)
        => await JournalService.GetNotesAsync(db, classId, subjectId, quarter);

    [HttpPut("notes")]
    public async Task<IActionResult> SetNote(SetLessonNoteRequest req)
    {
        await JournalService.SetNoteAsync(db, req);
        return NoContent();
    }

    /* ---------- Mavzularni Excel'dan ommaviy yuklash (mavzu + uy vazifa) ---------- */

    private const string XlsxMime = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>Tanlangan sinf+fan+chorak uchun mavzular shabloni (.xlsx) — jadval kunlari oldindan to'ldirilgan.</summary>
    [HttpGet("topics-template")]
    public async Task<IActionResult> TopicsTemplate(
        [FromQuery] string classId, [FromQuery] string subjectId, [FromQuery] int quarter)
    {
        var bytes = await JournalService.TopicTemplateXlsxAsync(db, classId, subjectId, quarter);
        return File(bytes, XlsxMime, "mavzular_shablon.xlsx");
    }

    /// <summary>To'ldirilgan Excel'dan mavzu+uy vazifani import qiladi (darsni "o'tilgan" qilmaydi).</summary>
    [HttpPost("topics-import")]
    [RequestSizeLimit(10 * 1024 * 1024)]
    public async Task<ActionResult<TopicImportResultDto>> TopicsImport(
        [FromForm] string classId, [FromForm] string subjectId, [FromForm] int quarter, IFormFile? file)
    {
        if (string.IsNullOrWhiteSpace(classId) || string.IsNullOrWhiteSpace(subjectId))
            return BadRequest(new { message = "Sinf va fan ko'rsatilishi shart" });
        if (file is null || file.Length == 0)
            return BadRequest(new { message = "Fayl tanlanmagan" });
        if (!file.FileName.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "Faqat .xlsx (Excel) fayl qabul qilinadi" });

        List<string[]> rows;
        try
        {
            await using var stream = file.OpenReadStream();
            rows = ExcelImport.ReadRows(stream, JournalService.TopicHeaders.Length);
        }
        catch
        {
            return BadRequest(new { message = "Faylni o'qib bo'lmadi — buzilmagan .xlsx ekanini tekshiring" });
        }

        return await JournalService.ImportTopicsAsync(db, classId, subjectId, quarter, rows);
    }

    /* ---------- Chorak (yakuniy) bahosi ---------- */

    /// <summary>Fan+chorak bo'yicha o'quvchilarning chorak bahosi + tavsiya (kunlik o'rtacha).</summary>
    [HttpGet("quarter-grades")]
    public async Task<ActionResult<IEnumerable<QuarterGradeRowDto>>> GetQuarterGrades(
        [FromQuery] string classId, [FromQuery] string subjectId, [FromQuery] int quarter)
        => await JournalService.GetQuarterGradesAsync(db, classId, subjectId, quarter);

    [HttpPut("quarter-grades")]
    public async Task<IActionResult> SetQuarterGrade(SetQuarterGradeRequest req)
    {
        await JournalService.SetQuarterGradeAsync(db, req);
        return NoContent();
    }
}
