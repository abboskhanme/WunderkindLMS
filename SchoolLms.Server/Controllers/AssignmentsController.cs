using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using System.Security.Claims;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Admin "Topshiriqlar" bo'limi. Admin o'qituvchilar kabi topshiriq YARATADI/tahrirlaydi/o'chiradi
/// (istalgan sinf+fan uchun) va BARCHA topshiriqlarni — o'qituvchilar yaratganini ham — boshqaradi.
/// Egalik (CreatedByUserId) yangilashda saqlanadi (o'qituvchi baribir o'zinikini ko'rib turadi).
/// Yaratish/tahrirlash o'qituvchida ham bor (`/api/teacher/assignments`).
/// </summary>
[ApiController]
[Authorize(Roles = "admin,superadmin")]
[Route("api/admin/assignments")]
public class AssignmentsController(AppDbContext db, IWebHostEnvironment env) : ControllerBase
{
    private string Uid => User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";

    /// <summary>Barcha topshiriqlar (yoki sinf bo'yicha).</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<AssignmentDto>>> List([FromQuery] string? classId)
        => string.IsNullOrEmpty(classId)
            ? await AssignmentService.ListAllAsync(db)
            : await AssignmentService.ListForClassAsync(db, classId);

    /// <summary>Topshiriq natijalari — kim bajardi/bajarmadi + ball + yuborgan javobi.</summary>
    [HttpGet("{id}/results")]
    public async Task<ActionResult<AssignmentResultDto>> Results(string id)
    {
        var res = await AssignmentService.GetResultsAsync(db, id);
        return res is null ? NotFound() : res;
    }

    /// <summary>"Topshiriqlar bali" — sinf bo'yicha ball jadvali (o'quvchilar × topshiriqlar).</summary>
    [HttpGet("scoreboard")]
    public async Task<ActionResult<AssignmentScoreboardDto>> Scoreboard([FromQuery] string classId)
    {
        if (string.IsNullOrWhiteSpace(classId))
            return BadRequest(new { message = "classId kerak" });
        var res = await AssignmentService.GetScoreboardAsync(db, classId);
        return res is null ? NotFound() : res;
    }

    /// <summary>Yangi topshiriq yaratish (admin egasi bo'ladi).</summary>
    [HttpPost]
    public async Task<ActionResult<AssignmentDto>> Create(SaveAssignmentRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Title)) return BadRequest(new { message = "Topshiriq nomi kerak" });
        if (req.ClassIds is null || req.ClassIds.Count == 0)
            return BadRequest(new { message = "Kamida bitta sinf tanlang" });
        return await AssignmentService.CreateAsync(db, Uid, req);
    }

    /// <summary>Topshiriqni tahrirlash (istalganini — admin barcha topshiriqni boshqaradi).</summary>
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, SaveAssignmentRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Title)) return BadRequest(new { message = "Topshiriq nomi kerak" });
        return await AssignmentService.UpdateAsync(db, id, req) ? NoContent() : NotFound();
    }

    /// <summary>Topshiriqni o'chirish (istalganini).</summary>
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
        => await AssignmentService.DeleteAsync(db, id) ? NoContent() : NotFound();

    /// <summary>O'quvchining bajarish holatini va ballini belgilash (admin baholaydi).</summary>
    [HttpPut("{id}/submissions/{studentId}")]
    public async Task<IActionResult> SetSubmission(string id, string studentId, SetSubmissionRequest req)
    {
        if (await db.Assignments.FindAsync(id) is null) return NotFound();
        await AssignmentService.SetSubmissionAsync(db, id, studentId, req.Completed, req.Score);
        return NoContent();
    }

    /// <summary>Topshiriq materiali sifatida fayl yuklash (PDF/rasm/doc, maks ~20MB).</summary>
    [HttpPost("uploads")]
    [RequestSizeLimit(20_000_000)]
    public async Task<ActionResult<UploadedFileDto>> Upload(IFormFile file)
    {
        if (file is null || file.Length == 0) return BadRequest(new { message = "Fayl bo'sh" });
        if (file.Length > 20_000_000) return BadRequest(new { message = "Fayl 20 MB dan katta" });

        var dir = System.IO.Path.Combine(env.ContentRootPath, "uploads");
        System.IO.Directory.CreateDirectory(dir);
        var ext = System.IO.Path.GetExtension(file.FileName);
        var stored = $"{Guid.NewGuid():N}{ext}";
        await using (var fs = System.IO.File.Create(System.IO.Path.Combine(dir, stored)))
            await file.CopyToAsync(fs);

        return new UploadedFileDto(file.FileName, $"/uploads/{stored}", file.Length, file.ContentType ?? "");
    }
}
