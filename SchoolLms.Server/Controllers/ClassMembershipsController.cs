using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Billing;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Sinf ro'yxati — <c>docs/modules/students-parity.md</c> §2.2, C-1.
///
/// <para>
/// <b>Nima o'zgaradi va nima o'zgarmaydi.</b> O'quvchini sinfga bog'laydigan
/// haqiqat manbai bugungiday <c>students.class_name</c> bo'lib QOLADI —
/// jurnal, davomat, chat va hisobotlarning hammasi shu ustunni o'qiydi.
/// Yangilik: har bir o'zgarish endi <c>class_memberships</c> da SANALI yozuv
/// ham qoldiradi (kim, qachon, nega ketdi) va ikkalasi
/// <see cref="ClassMembershipService"/> orqali BIRGA yoziladi.
/// </para>
/// <para>
/// <b>Ruxsat: <c>classes</c></b> — <see cref="ClassesController"/> bilan bir
/// xil darvoza. Qoldiq (balans) faqat moliyani ko'ra oladiganlarga qaytadi
/// (SPEC §4.3): xodim uchun GET ochiq, lekin pul raqami emas.
/// </para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("classes")]
[Route("api/admin/class-roster")]
public class ClassMembershipsController(AppDbContext db, AuditService audit) : ControllerBase
{
    private ClassMembershipService Service => new(db);

    private string? UserId =>
        User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;

    /// <summary>
    /// Qoldiqni KIM ko'radi (§2.2.1 <c>canSeeStudentBalance</c>). Xodimga pul
    /// raqami faqat <c>finance</c> kaliti bilan ochiladi — usiz ro'yxatda 0
    /// turadi va ekran ustunni yashiradi.
    /// </summary>
    private bool CanSeeBalance =>
        User.IsInRole(Roles.Admin) || User.IsInRole(Roles.SuperAdmin)
        || User.Claims.Any(c => c.Type == AdminPermAttribute.ClaimType && c.Value == "finance");

    /* ===================================================================
     *  1. O'qish
     * ================================================================ */

    /// <summary>
    /// Sinf ro'yxati: FAOL a'zolar, alifbo bo'yicha.
    ///
    /// <para>
    /// Ro'yxat <c>students.class_name</c> dan quriladi (haqiqat manbai), a'zolik
    /// yozuvi esa unga QO'SHILADI. Backfill qamramagan o'quvchida
    /// <c>membershipId</c> bo'sh <see cref="Guid"/> bo'lib qaytadi va ekran
    /// unga "chiqarish/o'tkazish" tugmalarini ko'rsatmaydi — bunday bolani
    /// avval formadan qayta saqlash kerak.
    /// </para>
    /// </summary>
    [HttpGet("{classId}")]
    public async Task<ActionResult<ClassRosterDto>> GetRoster(
        string classId, [FromQuery] string? search = null, CancellationToken ct = default)
    {
        var cls = await db.Classes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == classId, ct);
        if (cls is null) return NotFound();

        var q = db.Students.AsNoTracking().Where(s => !s.IsArchived && s.ClassName == cls.Name);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            q = q.Where(s => EF.Functions.ILike(s.FullName, $"%{term}%"));
        }
        var students = await q.OrderBy(s => s.FullName).ToListAsync(ct);

        var studentIds = students.Select(s => s.Id).ToList();
        var memberships = await db.ClassMemberships.AsNoTracking()
            .Where(m => m.ClassId == cls.Id && m.LeftOn == null && studentIds.Contains(m.StudentId))
            .ToDictionaryAsync(m => m.StudentId, ct);

        IReadOnlyDictionary<string, decimal> balances = CanSeeBalance && studentIds.Count > 0
            ? await new StudentBalanceQuery(db).ForManyAsync(studentIds, ct)
            : new Dictionary<string, decimal>();

        var rows = students.Select(s =>
        {
            var m = memberships.GetValueOrDefault(s.Id);
            return new ClassRosterRowDto(
                m?.Id ?? Guid.Empty, s.Id, s.FullName, s.Gender,
                balances.GetValueOrDefault(s.Id),
                m?.JoinedOn ?? AppClock.Today);
        }).ToList();

        return new ClassRosterDto(cls.Id, cls.Name, cls.Grade, rows);
    }

    /// <summary>
    /// Sinfga qo'shish uchun nomzodlar — FAQAT sinfsiz, arxivlanmagan
    /// o'quvchilar (§2.2.1: <c>students/pagin?withNoClass=true&amp;noArchive=true</c>).
    /// </summary>
    [HttpGet("{classId}/candidates")]
    public async Task<ActionResult<IEnumerable<ClassCandidateDto>>> Candidates(
        string classId, [FromQuery] string? search = null, CancellationToken ct = default)
    {
        if (!await db.Classes.AnyAsync(c => c.Id == classId, ct)) return NotFound();

        var activeStudentIds = db.ClassMemberships.Where(m => m.LeftOn == null).Select(m => m.StudentId);
        var q = db.Students.AsNoTracking()
            .Where(s => !s.IsArchived
                        && (s.ClassName == null || s.ClassName == "")
                        && !activeStudentIds.Contains(s.Id));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            q = q.Where(s => EF.Functions.ILike(s.FullName, $"%{term}%"));
        }

        return await q.OrderBy(s => s.FullName)
            .Select(s => new ClassCandidateDto(s.Id, s.FullName, s.Gender))
            .Take(200)
            .ToListAsync(ct);
    }

    /* ===================================================================
     *  2. Yozish
     * ================================================================ */

    [HttpPost("{classId}/members")]
    public async Task<IActionResult> AddMember(
        string classId, AddClassMemberRequest req, CancellationToken ct = default)
    {
        var cls = await db.Classes.FirstOrDefaultAsync(c => c.Id == classId, ct);
        if (cls is null) return NotFound();
        var student = await db.Students.FirstOrDefaultAsync(s => s.Id == req.StudentId, ct);
        if (student is null) return NotFound();

        string? error;
        try
        {
            error = await Service.AddAsync(student, cls, UserId, ct);
        }
        catch (DbUpdateException ex) when (ClassMembershipService.IsOneActiveViolation(ex))
        {
            return Conflict(new { message = ClassMembershipService.AlreadyInClassMessage });
        }
        if (error is not null) return BadRequest(new { message = error });

        audit.Record(AuditService.EntityStudentClass, cls.Id, "update",
            $"O'quvchi sinfga qo'shildi: {student.FullName} → {cls.Name}",
            after: new { Class = cls.Name }, studentId: student.Id);
        await db.SaveChangesAsync(ct);

        // C-4: sig'im OGOHLANTIRISHI — taqiq emas, amal baribir bajarildi.
        var warning = await Service.CapacityWarningAsync(cls, ct);
        return warning is null ? NoContent() : Ok(new ClassCapacityWarningDto(warning));
    }

    /// <summary>
    /// Sinfdan chiqarish — a'zolik SABAB bilan yopiladi, <c>class_name</c>
    /// bo'shaydi. Sabab MAJBURIY (§2.2.1).
    /// </summary>
    [HttpPost("members/{membershipId:guid}/remove")]
    public async Task<IActionResult> RemoveMember(
        Guid membershipId, RemoveClassMemberRequest req, CancellationToken ct = default)
    {
        var membership = await db.ClassMemberships.FirstOrDefaultAsync(m => m.Id == membershipId, ct);
        if (membership is null) return NotFound();
        if (membership.LeftOn is not null)
            return BadRequest(new { message = "Bu a'zolik allaqachon yopilgan" });

        var student = await db.Students.FirstOrDefaultAsync(s => s.Id == membership.StudentId, ct);
        if (student is null) return NotFound();
        var cls = await db.Classes.AsNoTracking().FirstOrDefaultAsync(c => c.Id == membership.ClassId, ct);

        var error = await Service.RemoveAsync(student, membership, req?.Reason ?? "", ct);
        if (error is not null) return BadRequest(new { message = error });

        audit.Record(AuditService.EntityStudentClass, membership.ClassId, "update",
            $"O'quvchi sinfdan chiqarildi: {student.FullName} ({cls?.Name}) — sabab: {req!.Reason.Trim()}",
            before: new { Class = cls?.Name }, after: new { Class = "" }, studentId: student.Id);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Boshqa sinfga o'tkazish — AYNI DARAJADA (§2.2.1: <c>grades=[same grade]</c>).
    /// "Guruhlarda qolsin" sukut bo'yicha yoqiq.
    /// </summary>
    [HttpPost("members/{membershipId:guid}/transfer")]
    public async Task<IActionResult> TransferMember(
        Guid membershipId, TransferClassMemberRequest req, CancellationToken ct = default)
    {
        var membership = await db.ClassMemberships.FirstOrDefaultAsync(m => m.Id == membershipId, ct);
        if (membership is null) return NotFound();
        if (membership.LeftOn is not null)
            return BadRequest(new { message = "Yopilgan a'zolikni o'tkazib bo'lmaydi" });

        var student = await db.Students.FirstOrDefaultAsync(s => s.Id == membership.StudentId, ct);
        var from = await db.Classes.FirstOrDefaultAsync(c => c.Id == membership.ClassId, ct);
        var to = await db.Classes.FirstOrDefaultAsync(c => c.Id == req.ToClassId, ct);
        if (student is null || from is null || to is null) return NotFound();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        string? error;
        try
        {
            error = await Service.TransferAsync(
                student, membership, from, to, req.KeepGroups, req.Reason, UserId, ct);
        }
        catch (DbUpdateException ex) when (ClassMembershipService.IsOneActiveViolation(ex))
        {
            return Conflict(new { message = ClassMembershipService.AlreadyInClassMessage });
        }
        if (error is not null) return BadRequest(new { message = error });

        audit.Record(AuditService.EntityStudentClass, to.Id, "update",
            $"O'quvchi sinfdan sinfga o'tkazildi: {student.FullName} — {from.Name} → {to.Name}"
            + (req.KeepGroups ? " (guruhlarda qoldi)" : " (guruhlardan chiqarildi)"),
            before: new { Class = from.Name }, after: new { Class = to.Name },
            studentId: student.Id);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        // C-4: sig'im OGOHLANTIRISHI — nishon (to) sinf bo'yicha, taqiq emas.
        var warning = await Service.CapacityWarningAsync(to, ct);
        return warning is null ? NoContent() : Ok(new ClassCapacityWarningDto(warning));
    }
}
