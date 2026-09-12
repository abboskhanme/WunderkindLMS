using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;
using SchoolLms.Application.Services;

namespace SchoolLms.Server.Controllers;

[ApiController]
[Authorize]
[AdminPerm("classes")]
[Route("api/admin/classes")]
public class ClassesController(AppDbContext db, AuditService audit) : ControllerBase
{
    /// <summary>Faol (arxivlanmagan) sinflar. <paramref name="includeArchived"/>=true bo'lsa hammasi.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<SchoolClass>>> GetAll([FromQuery] bool includeArchived = false)
    {
        var q = db.Classes.AsQueryable();
        if (!includeArchived) q = q.Where(c => !c.IsArchived);
        return await q.OrderBy(c => c.Grade).ThenBy(c => c.Name).ToListAsync();
    }

    /// <summary>Arxivlangan sinflar ro'yxati.</summary>
    [HttpGet("archived")]
    public async Task<ActionResult<IEnumerable<SchoolClass>>> GetArchived() =>
        await db.Classes.Where(c => c.IsArchived)
            .OrderByDescending(c => c.ArchivedAt).ThenBy(c => c.Name).ToListAsync();

    [HttpPost]
    public async Task<ActionResult<SchoolClass>> Create(ClassPayload p)
    {
        var cls = new SchoolClass
        {
            Name = p.Name,
            Grade = p.Grade,
            Language = p.Language,
            MonthlyFee = p.MonthlyFee,
            Room = p.Room,
        };
        db.Classes.Add(cls);

        if (cls.MonthlyFee > 0)
            audit.Record(AuditService.EntityClassFee, cls.Id, "create",
                $"Oylik to'lov belgilandi: {AuditService.Money(cls.MonthlyFee)} so'm ({cls.Name})",
                after: new { cls.MonthlyFee, cls.Name });

        await db.SaveChangesAsync();
        return cls;
    }

    /// <summary>
    /// Sinfni tahrirlash.
    ///
    /// <para>
    /// <b>P1-21: oylik to'lovni o'zgartirish endi PULGA TEGMAYDI</b> va shu bilan
    /// <c>?applyFee=</c> parametri ham olib tashlandi. Narxni <c>SchoolClass.MonthlyFee</c>
    /// emas, <c>student_subscriptions.monthly_amount</c> belgilaydi (SPEC §3.7);
    /// sinf narxi endi faqat yangi obuna ochilayotganda TAKLIF qilinadigan
    /// standart qiymat. Ilgari bu tugma bir bosishda butun sinfning joriy oyini
    /// va qoldiqlarini qayta yozardi — yangi modelda bu obunadagi kelishilgan
    /// summani jimgina bekor qilardi.
    /// </para>
    /// <para>
    /// Mavjud obunalarning narxini o'zgartirish — "Moliya → Obunalar" ekranida,
    /// har o'quvchi uchun alohida va audit bilan.
    /// </para>
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<SchoolClass>> Update(string id, ClassPayload p)
    {
        var cls = await db.Classes.FindAsync(id);
        if (cls is null) return NotFound();

        var oldFee = cls.MonthlyFee;
        cls.Name = p.Name;
        cls.Grade = p.Grade;
        cls.Language = p.Language;
        cls.MonthlyFee = p.MonthlyFee;
        cls.Room = p.Room;

        if (oldFee != cls.MonthlyFee)
        {
            var summary =
                $"Oylik to'lov o'zgartirildi: {AuditService.Money(oldFee)} → "
                + $"{AuditService.Money(cls.MonthlyFee)} so'm ({cls.Name})"
                + " — mavjud obunalarga TA'SIR QILMAYDI, faqat yangi obuna uchun standart qiymat";
            audit.Record(AuditService.EntityClassFee, cls.Id, "update", summary,
                before: new { MonthlyFee = oldFee, cls.Name }, after: new { cls.MonthlyFee, cls.Name });
        }

        await db.SaveChangesAsync();
        return cls;
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var cls = await db.Classes.FindAsync(id);
        if (cls is null) return NotFound();
        // Sinfga bog'langan (faol) o'quvchi bo'lsa — bittasi ham bo'lsa — o'chirib bo'lmaydi.
        var studentCount = await db.Students.CountAsync(s => s.ClassName == cls.Name && !s.IsArchived);
        if (studentCount > 0)
            return BadRequest(new
            {
                message = $"Bu sinfda {studentCount} ta o'quvchi bor — sinfni o'chirib bo'lmaydi. " +
                          "Avval o'quvchilarni boshqa sinfga o'tkazing yoki arxivlang.",
            });
        // Sinf jadval shablonlari va hafta biriktirishlarini ham o'chiramiz — "yetim" jadval
        // qolmasin (aks holda maosh/hisoblar o'chirilgan sinf darslarini sanayverardi).
        db.ScheduleTemplates.RemoveRange(
            await db.ScheduleTemplates.Where(t => t.ClassId == cls.Id).ToListAsync());
        db.WeekAssignments.RemoveRange(
            await db.WeekAssignments.Where(w => w.ClassId == cls.Id).ToListAsync());
        db.Classes.Remove(cls);
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// Sinfni arxivlash — <c>IsArchived=true</c>. Unga bog'langan FAOL o'quvchilar ham arxivlanadi
    /// (login bloklanadi, lekin parol saqlanadi — chiqarganda tiklanadi) va <c>ArchivedWithClass=true</c>
    /// bilan belgilanadi. Avval alohida arxivlangan o'quvchilar tegilmaydi.
    /// </summary>
    [HttpPost("{id}/archive")]
    public async Task<IActionResult> Archive(string id)
    {
        var cls = await db.Classes.FindAsync(id);
        if (cls is null) return NotFound();
        if (cls.IsArchived) return BadRequest(new { message = "Sinf allaqachon arxivda" });

        var today = AppClock.Today.ToString("yyyy-MM-dd");
        cls.IsArchived = true;
        cls.ArchivedAt = today;

        var students = await db.Students.Where(s => s.ClassName == cls.Name && !s.IsArchived).ToListAsync();
        foreach (var s in students)
        {
            s.IsArchived = true;
            s.ArchivedAt = today;
            s.ArchiveReason = $"Sinf arxivlandi ({cls.Name})";
            s.ArchivedWithClass = true;
        }

        audit.Record(AuditService.EntityClassFee, cls.Id, "update",
            $"Sinf arxivlandi ({cls.Name}) — {students.Count} ta o'quvchi bilan");
        await db.SaveChangesAsync();
        return Ok(new { archivedStudents = students.Count });
    }

    /// <summary>
    /// Sinfni arxivdan chiqarish — <c>IsArchived=false</c>. Faqat shu sinf bilan arxivlangan
    /// (<c>ArchivedWithClass=true</c>) o'quvchilar qaytariladi; alohida arxivlanganlar arxivda qoladi.
    /// </summary>
    [HttpPost("{id}/unarchive")]
    public async Task<IActionResult> Unarchive(string id)
    {
        var cls = await db.Classes.FindAsync(id);
        if (cls is null) return NotFound();
        if (!cls.IsArchived) return BadRequest(new { message = "Sinf arxivda emas" });

        cls.IsArchived = false;
        cls.ArchivedAt = null;

        var students = await db.Students
            .Where(s => s.ClassName == cls.Name && s.IsArchived && s.ArchivedWithClass).ToListAsync();
        foreach (var s in students)
        {
            s.IsArchived = false;
            s.ArchivedAt = null;
            s.ArchiveReason = null;
            s.ArchivedWithClass = false;
        }

        audit.Record(AuditService.EntityClassFee, cls.Id, "update",
            $"Sinf arxivdan chiqarildi ({cls.Name}) — {students.Count} ta o'quvchi bilan");
        await db.SaveChangesAsync();
        return Ok(new { restoredStudents = students.Count });
    }

    /* ---------- Sinf ichidagi guruhlar ---------- */

    /// <summary>
    /// Sinfdagi o'quvchilarning ikki guruhga taqsimoti. Locked=true bo'lsa — o'quv yili
    /// boshlangan (jurnalda yozuv bor). CanEdit = !Locked YOKI joriy foydalanuvchi superadmin
    /// (tizim egasi muzlatishni o'tib o'zgartira oladi).
    /// </summary>
    [HttpGet("{id}/groups")]
    public async Task<ActionResult<ClassGroupsDto>> GetGroups(string id)
    {
        var cls = await db.Classes.FindAsync(id);
        if (cls is null) return NotFound();

        var students = await db.Students.Where(s => s.ClassName == cls.Name)
            .OrderBy(s => s.FullName).ToListAsync();

        var hasJournal = await db.JournalEntries.AnyAsync(e => e.ClassId == cls.Id);
        var locked = hasJournal;
        var isSuper = User.IsInRole(Roles.SuperAdmin);
        var canEdit = !locked || isSuper;
        var lockReason = hasJournal
            ? (isSuper
                ? "O'quv yili boshlangan (jurnalda yozuv bor) — siz superadmin sifatida o'zgartira olasiz"
                : "Bu sinf uchun jurnalga yozuv kiritilgan — o'quv yili boshlangan, faqat superadmin o'zgartira oladi")
            : null;

        return new ClassGroupsDto(
            cls.Id, cls.Name, locked, lockReason, canEdit,
            students.Count(s => s.SubGroup == 0),
            students.Count(s => s.SubGroup == 1),
            students.Count(s => s.SubGroup == 2),
            students.Select(s => new GroupStudentDto(s.Id, s.FullName, s.SubGroup)).ToList());
    }

    /// <summary>
    /// O'quvchilarni guruhga belgilash (1, 2 yoki 0 = guruhsiz). Yopiq bo'lsa va foydalanuvchi
    /// superadmin BO'LMASA 400 qaytadi. Superadmin har doim o'zgartira oladi.
    /// </summary>
    [HttpPut("{id}/groups")]
    public async Task<IActionResult> SaveGroups(string id, SaveGroupsRequest req)
    {
        var cls = await db.Classes.FindAsync(id);
        if (cls is null) return NotFound();

        var hasJournal = await db.JournalEntries.AnyAsync(e => e.ClassId == cls.Id);
        if (hasJournal && !User.IsInRole(Roles.SuperAdmin))
            return BadRequest(new { message = "Guruhni o'zgartirib bo'lmaydi: o'quv yili allaqachon boshlangan. Faqat superadmin o'zgartira oladi." });

        var assignments = req.Assignments ?? new();
        foreach (var a in assignments)
        {
            if (a.SubGroup is < 0 or > 2)
                return BadRequest(new { message = $"SubGroup 0, 1 yoki 2 bo'lishi kerak ({a.StudentId})" });
        }

        var ids = assignments.Select(a => a.StudentId).ToList();
        var students = await db.Students.Where(s => ids.Contains(s.Id) && s.ClassName == cls.Name).ToListAsync();
        foreach (var s in students)
        {
            var a = assignments.First(x => x.StudentId == s.Id);
            s.SubGroup = a.SubGroup;
        }
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>
    /// Sinf o'quvchilarini ikki guruhga avtomatik teng bo'lish (alifbo bo'yicha bir-biriga
    /// almashtirib). Yopiq bo'lsa va foydalanuvchi superadmin bo'lmasa 400.
    /// Joriy guruhlash ustiga yoziladi.
    /// </summary>
    [HttpPost("{id}/groups/auto-split")]
    public async Task<ActionResult<ClassGroupsDto>> AutoSplit(string id)
    {
        var cls = await db.Classes.FindAsync(id);
        if (cls is null) return NotFound();

        var hasJournal = await db.JournalEntries.AnyAsync(e => e.ClassId == cls.Id);
        if (hasJournal && !User.IsInRole(Roles.SuperAdmin))
            return BadRequest(new { message = "Avtomatik bo'lishni qo'llab bo'lmaydi: o'quv yili allaqachon boshlangan. Faqat superadmin override qila oladi." });

        var students = await db.Students.Where(s => s.ClassName == cls.Name)
            .OrderBy(s => s.FullName).ToListAsync();
        for (var i = 0; i < students.Count; i++)
            students[i].SubGroup = i % 2 == 0 ? 1 : 2;
        await db.SaveChangesAsync();

        return await GetGroups(id);
    }
}
