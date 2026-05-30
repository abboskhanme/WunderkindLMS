using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Server.Data;
using SchoolLms.Server.Dtos;
using SchoolLms.Server.Services;

namespace SchoolLms.Server.Controllers;

[ApiController]
[Authorize(Roles = "admin,superadmin")]
[Route("api/admin/journal")]
public class JournalController(AppDbContext db) : ControllerBase
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
        await JournalService.SetEntryAsync(db, req);
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
