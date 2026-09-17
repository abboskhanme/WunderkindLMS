using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Auth;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Ommaviy arxivlash (§2.2) — bitiruv tabiatan ommaviy amal: yakuniy sinf bittalab
/// emas, bir bosishda arxivga ketadi.
///
/// <para>
/// <b>Nega alohida fayl.</b> <c>StudentsController</c> 600 qatordan oshgan va shu
/// to'lqinda bir nechta agent unga tegadi. Yangi amal qo'shni faylga chiqarilsa
/// birlashtirish (merge) arzon bo'ladi; marshrut prefiksi bir xil bo'lgani uchun
/// mijoz tarafda hech narsa o'zgarmaydi (<c>archive-many</c> — bitta segment,
/// mavjud <c>{id}/archive</c> bilan to'qnashmaydi).
/// </para>
/// <para>
/// <b>Hammasi yoki hech nima.</b> Qarzdor topilsa amal BUTUNLAY rad etiladi va
/// ro'yxat qaytadi. "O'n beshtadan uchtasi qoldi" — administrator uchun eng yomon
/// natija: qaysi bola qolganini u qo'lda qidirishi kerak bo'lardi.
/// </para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("students")]
[Route("api/admin/students")]
public class StudentBulkArchiveController(AppDbContext db, AuditService audit) : ControllerBase
{
    /// <summary>Bir so'rovda arxivlash mumkin bo'lgan eng katta son — tasodifiy "hammasi"dan himoya.</summary>
    private const int MaxBatch = 500;

    [HttpPost("archive-many")]
    public async Task<ActionResult<BulkArchiveResultDto>> ArchiveMany(
        BulkArchiveRequest req, CancellationToken ct = default)
    {
        var ids = (req.StudentIds ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
        if (ids.Count == 0) return BadRequest(new { message = "O'quvchi tanlanmadi" });
        if (ids.Count > MaxBatch)
            return BadRequest(new { message = $"Bir martada eng ko'pi {MaxBatch} ta o'quvchi arxivlanadi" });

        var reason = (req.Reason ?? "").Trim();
        if (reason.Length == 0)
            return BadRequest(new { message = StudentArchiveService.ReasonRequiredMessage });

        var archive = new StudentArchiveService(db);
        if (!await archive.ReasonIsUsableAsync(req.ArchiveReasonId, ct))
            return BadRequest(new { message = StudentArchiveService.ReasonNotFoundMessage });

        var isSuperAdmin = User.IsInRole(Roles.SuperAdmin);

        // Allaqachon arxivdagilar jimgina tushib qoladi — ular xato emas, shunchaki ish yo'q.
        var students = await db.Students
            .Where(s => ids.Contains(s.Id) && !s.IsArchived)
            .ToListAsync(ct);
        if (students.Count == 0)
            return BadRequest(new { message = "Arxivlanadigan o'quvchi topilmadi (tanlanganlar allaqachon arxivda)" });

        if (await archive.DebtorGuardAppliesAsync(isSuperAdmin, req.Force, ct))
        {
            var blocked = await archive.DebtorsAmongAsync(students, ct);
            if (blocked.Count > 0)
                return BadRequest(new BulkArchiveResultDto(
                    0, blocked, isSuperAdmin,
                    isSuperAdmin
                        ? StudentArchiveService.DebtorOverrideHintMessage
                        : StudentArchiveService.DebtorBlockedMessage));
        }

        foreach (var student in students)
        {
            // Login bloklash — bitta arxivlash bilan bir xil (StudentsController.Archive).
            (await archive.ApplyAsync(student, reason, req.ArchiveReasonId, ct))?.BlockLogin();
            // Bitta o'quvchi ham shu endpoint orqali kelishi mumkin (ekran xato javobini
            // bitta shaklda olishi uchun) — audit matni shunga qarab o'zgaradi.
            var what = students.Count > 1 ? "ommaviy arxivlandi" : "arxivga ko'chirildi";
            audit.Record(AuditService.EntityStudentDiscount, student.Id, "update",
                $"O'quvchi {what} ({student.FullName}): \"{reason}\""
                    + (isSuperAdmin && req.Force ? " — qarzdorlik to'sig'i chetlab o'tildi" : ""),
                studentId: student.Id);
        }

        await db.SaveChangesAsync(ct);
        return new BulkArchiveResultDto(students.Count, [], isSuperAdmin);
    }
}
