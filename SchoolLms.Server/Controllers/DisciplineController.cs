using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using System.Security.Claims;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Intizomiy ball — har o'quvchi 100 balldan boshlaydi. Sabablar IKKI manbadan:
/// "other" — mustaqil intizomiy sabablar (shu yerda CRUD); "attendance" — davomat sabablari
/// (<see cref="AbsenceReason"/>, jurnalda ishlatiladi) — ularning balli shu yerda belgilanadi va
/// jurnalda shu sabab bilan davomat qo'yilsa qoldiga avtomatik ta'sir qiladi. Faqat admin.
/// </summary>
[ApiController]
[Authorize(Roles = "admin,superadmin")]
[Route("api/admin/discipline")]
public class DisciplineController(AppDbContext db) : ControllerBase
{
    private const int BaseScore = 100;
    private string Uid => User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";

    // ---------- Ball sabablar (birlashgan) ----------

    /// <summary>Barcha sabablar: mustaqil intizomiy ("other") + davomat sabablari ("attendance").</summary>
    [HttpGet("reasons")]
    public async Task<ActionResult<IEnumerable<DisciplineReasonDto>>> GetReasons()
    {
        var other = await db.DisciplineReasons.OrderBy(r => r.Name)
            .Select(r => new DisciplineReasonDto(r.Id, r.Name, r.Points, "other")).ToListAsync();
        var attendance = await db.AbsenceReasons.OrderBy(r => r.Name)
            .Select(r => new DisciplineReasonDto(r.Id, r.Name, r.Points, "attendance")).ToListAsync();
        return other.Concat(attendance).ToList();
    }

    [HttpPost("reasons")]
    public async Task<ActionResult<DisciplineReasonDto>> CreateReason(SaveDisciplineReasonRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name)) return BadRequest(new { message = "Sabab nomi kerak" });
        var r = new DisciplineReason { Name = req.Name.Trim(), Points = req.Points };
        db.DisciplineReasons.Add(r);
        await db.SaveChangesAsync();
        return new DisciplineReasonDto(r.Id, r.Name, r.Points, "other");
    }

    [HttpPut("reasons/{id}")]
    public async Task<ActionResult<DisciplineReasonDto>> UpdateReason(string id, SaveDisciplineReasonRequest req)
    {
        var r = await db.DisciplineReasons.FindAsync(id);
        if (r is null) return NotFound();
        r.Name = (req.Name ?? "").Trim();
        r.Points = req.Points;
        await db.SaveChangesAsync();
        return new DisciplineReasonDto(r.Id, r.Name, r.Points, "other");
    }

    [HttpDelete("reasons/{id}")]
    public async Task<IActionResult> DeleteReason(string id)
    {
        var r = await db.DisciplineReasons.FindAsync(id);
        if (r is not null) { db.DisciplineReasons.Remove(r); await db.SaveChangesAsync(); }
        return NoContent();
    }

    /// <summary>Davomat sababiga ball belgilash (nomi Sozlamalar/jurnal tarafida boshqariladi).</summary>
    [HttpPut("reasons/attendance/{id}")]
    public async Task<ActionResult<DisciplineReasonDto>> SetAttendancePoints(string id, SetReasonPointsRequest req)
    {
        var r = await db.AbsenceReasons.FindAsync(id);
        if (r is null) return NotFound();
        r.Points = req.Points;
        await db.SaveChangesAsync();
        return new DisciplineReasonDto(r.Id, r.Name, r.Points, "attendance");
    }

    // ---------- Ballar nazorati ----------

    /// <summary>
    /// Faol o'quvchilar jamlamasi: plus/minus/qoldi. Qoldi = 100 + qo'lda kiritilgan ballar
    /// + jurnal davomati ballari (sabab balli != 0 bo'lganlar).
    /// </summary>
    [HttpGet("scores")]
    public async Task<ActionResult<IEnumerable<DisciplineScoreRowDto>>> GetScores()
    {
        var students = await db.Students.Where(s => !s.IsArchived)
            .Select(s => new { s.Id, s.FullName, s.ClassName }).ToListAsync();
        var manual = await db.DisciplinePoints.Select(p => new { p.StudentId, p.Points }).ToListAsync();
        var absPts = await db.AbsenceReasons.ToDictionaryAsync(r => r.Id, r => r.Points);
        var journal = await db.JournalEntries.Where(e => e.ReasonId != null)
            .Select(e => new { e.StudentId, e.ReasonId }).ToListAsync();

        var plusBy = new Dictionary<string, int>();
        var minusBy = new Dictionary<string, int>();
        void Apply(string sid, int pts)
        {
            if (pts > 0) plusBy[sid] = plusBy.GetValueOrDefault(sid) + pts;
            else if (pts < 0) minusBy[sid] = minusBy.GetValueOrDefault(sid) + (-pts);
        }
        foreach (var m in manual) Apply(m.StudentId, m.Points);
        foreach (var j in journal)
            if (j.ReasonId is not null && absPts.TryGetValue(j.ReasonId, out var p)) Apply(j.StudentId, p);

        return students.Select(s =>
        {
            var plus = plusBy.GetValueOrDefault(s.Id);
            var minus = minusBy.GetValueOrDefault(s.Id);
            return new DisciplineScoreRowDto(s.Id, s.FullName, s.ClassName, plus, minus, BaseScore + plus - minus);
        })
        .OrderBy(r => r.ClassName, StringComparer.OrdinalIgnoreCase)
        .ThenBy(r => r.FullName, StringComparer.OrdinalIgnoreCase)
        .ToList();
    }

    /// <summary>O'quvchiga qo'lda ball kiritadi (sabab "other" yoki "attendance" bo'lishi mumkin).</summary>
    [HttpPost("points")]
    public async Task<ActionResult<DisciplinePointDto>> AddPoint(AddDisciplinePointRequest req)
    {
        var student = await db.Students.FindAsync(req.StudentId);
        if (student is null) return NotFound(new { message = "O'quvchi topilmadi" });

        string name;
        int pts;
        var dr = await db.DisciplineReasons.FindAsync(req.ReasonId);
        if (dr is not null) { name = dr.Name; pts = dr.Points; }
        else
        {
            var ar = await db.AbsenceReasons.FindAsync(req.ReasonId);
            if (ar is null) return BadRequest(new { message = "Sabab tanlanmadi" });
            name = ar.Name; pts = ar.Points;
        }

        var user = await db.Users.FindAsync(Uid);
        var p = new DisciplinePoint
        {
            StudentId = student.Id,
            ReasonId = req.ReasonId,
            ReasonName = name,
            Points = pts,
            Note = (req.Note ?? "").Trim(),
            CreatedAt = AppClock.Now.ToString("o"),
            CreatedBy = user?.FullName ?? "Administrator",
        };
        db.DisciplinePoints.Add(p);
        await db.SaveChangesAsync();
        return new DisciplinePointDto(p.Id, p.StudentId, name, pts, p.Note, p.CreatedAt, p.CreatedBy, "manual");
    }

    /// <summary>O'quvchining ball tarixi: qo'lda kiritilgan (o'chirsa bo'ladi) + jurnal davomati (faqat ko'rish).</summary>
    [HttpGet("points")]
    public async Task<ActionResult<IEnumerable<DisciplinePointDto>>> GetPoints([FromQuery] string studentId)
    {
        var manual = await db.DisciplinePoints.Where(p => p.StudentId == studentId).ToListAsync();
        var drNames = await db.DisciplineReasons.ToDictionaryAsync(r => r.Id, r => r.Name);
        var absReasons = await db.AbsenceReasons.ToDictionaryAsync(r => r.Id, r => new { r.Name, r.Points });

        var result = manual.Select(p => new DisciplinePointDto(
            p.Id, p.StudentId,
            string.IsNullOrEmpty(p.ReasonName) ? drNames.GetValueOrDefault(p.ReasonId, "—") : p.ReasonName,
            p.Points, p.Note, p.CreatedAt, p.CreatedBy, "manual")).ToList();

        // Jurnal davomati — sabab balli != 0 bo'lganlari (qoldiga ta'sir qilgani uchun ko'rsatamiz).
        var journal = await db.JournalEntries
            .Where(e => e.StudentId == studentId && e.ReasonId != null).ToListAsync();
        foreach (var e in journal)
        {
            if (e.ReasonId is null || !absReasons.TryGetValue(e.ReasonId, out var r) || r.Points == 0) continue;
            result.Add(new DisciplinePointDto(
                e.Id, studentId, r.Name, r.Points, "Jurnal davomati", e.Date, "", "attendance"));
        }

        return result.OrderByDescending(p => p.CreatedAt, StringComparer.Ordinal).ToList();
    }

    /// <summary>Qo'lda kiritilgan ball yozuvini o'chiradi (jurnal davomatini emas).</summary>
    [HttpDelete("points/{id}")]
    public async Task<IActionResult> DeletePoint(string id)
    {
        var p = await db.DisciplinePoints.FindAsync(id);
        if (p is not null) { db.DisciplinePoints.Remove(p); await db.SaveChangesAsync(); }
        return NoContent();
    }
}
