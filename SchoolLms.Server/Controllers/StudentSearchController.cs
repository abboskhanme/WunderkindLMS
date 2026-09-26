using System.Globalization;
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
/// O'quvchilar ro'yxati — server tarafdagi filtr, tartib, sahifa, xlsx eksport
/// va ommaviy butunlay o'chirish (docs/modules/students-parity.md §2.3 S-1..S-4,
/// S-7, K-4; §2.4 A-1, A-3).
///
/// <para>
/// <b>Nega alohida controller.</b> <c>StudentsController</c> 700 qatordan
/// oshgan va shu to'lqinda boshqa slice unga tegadi (S-8 payload). Yangi
/// marshrutlar qo'shni faylga chiqarildi; prefiks bir xil bo'lgani uchun
/// mijoz tarafda hech narsa o'zgarmaydi. Xuddi shu naqsh
/// <see cref="StudentBulkArchiveController"/> da ham ishlatilgan.
/// </para>
/// <para>
/// <b>Eski <c>GET /api/admin/students</c> TEGILMAGAN.</b> U hamon butun
/// ro'yxatni qaytaradi va uni o'qiydigan boshqa ekranlar (portal, hisobotlar)
/// o'zgarishsiz ishlaydi. Bu yerdagi <c>search</c> — QO'SHIMCHA yo'l.
/// </para>
/// <para>
/// <b>Balans.</b> Qator bugungi ro'yxat ustunidagi qoldiqni olib keladi —
/// ya'ni ruxsat doirasi KENGAYTIRILMAGAN. Kim to'lov DAFTARINI ko'rishi
/// mumkinligi alohida qoida bo'lib qoladi
/// (<c>StudentsController.Ledger</c>, moliya roli).
/// </para>
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("students")]
[Route("api/admin/students")]
public class StudentSearchController(AppDbContext db, AuditService audit) : ControllerBase
{
    private const string XlsxMime =
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";

    /// <summary>Bir so'rovda o'chirish mumkin bo'lgan eng katta son.</summary>
    private const int MaxDeleteBatch = 200;

    /// <summary>
    /// Filtrlangan, tartiblangan va sahifalangan ro'yxat.
    ///
    /// <para>
    /// Birorta parametrsiz chaqirilsa natija bugungi ekran ko'rsatadigan
    /// narsaning aynan o'zi: faqat arxivlanmaganlar, F.I.SH bo'yicha.
    /// </para>
    /// </summary>
    [HttpGet("search")]
    public async Task<ActionResult<StudentListPageDto>> Search(
        [FromQuery] StudentListFilter filter, CancellationToken ct = default) =>
        await new StudentListQuery(db).RunAsync(filter, ct);

    /// <summary>
    /// S-2 — FILTRLANGAN ro'yxatning .xlsx eksporti (tanlangan qatorlarniki
    /// emas, login/parol eksporti ham emas).
    ///
    /// <para>
    /// <b>Ustunlar import bilan mos.</b> 1-varaqning birinchi ustunlari
    /// import shablonining ustunlari bilan AYNAN bir xil, ulardan keyin esa
    /// import o'qimaydigan ma'lumot ustunlari turadi. Shu sabab eksport
    /// qilingan faylni import'ga qaytarish mumkin (round-trip): hamma qator
    /// "yangilanadi" bo'lib o'tadi, hech narsa dublikat bo'lmaydi.
    /// </para>
    /// </summary>
    [HttpGet("search/export")]
    public async Task<IActionResult> Export(
        [FromQuery] StudentListFilter filter, CancellationToken ct = default)
    {
        var rows = await new StudentListQuery(db).AllAsync(filter, ct);

        var headers = StudentImportSheet.Headers
            .Concat(StudentImportSheet.ExportExtraHeaders).ToList();

        var statusNames = await db.StudentStatuses.AsNoTracking()
            .OrderBy(s => s.Position).ThenBy(s => s.Name).Select(s => s.Name).ToListAsync(ct);

        // G-19: har o'quvchining FAOL guruhlari — "Fan: Guruh" juftliklari,
        // import ustuni bilan AYNAN bir xil formatda (round-trip).
        var studentIds = rows.Select(r => r.Id).ToList();
        var subjectNameById = await db.Subjects.AsNoTracking()
            .ToDictionaryAsync(s => s.Id, s => s.Name, ct);
        var groupsByStudent = (await db.StudyGroupMembers.AsNoTracking()
                .Where(m => m.LeftOn == null && studentIds.Contains(m.StudentId))
                .Join(db.StudyGroups.AsNoTracking(), m => m.GroupId, g => g.Id,
                    (m, g) => new { m.StudentId, g.Name, g.SubjectId })
                .ToListAsync(ct))
            .GroupBy(x => x.StudentId, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => string.Join("; ", g.Select(x =>
                    $"{SubjectLabel(subjectNameById, x.SubjectId)}: {x.Name}")),
                StringComparer.Ordinal);

        var data = rows.Select(r => (IReadOnlyList<string>)new[]
        {
            r.FullName,
            r.ClassName,
            r.BirthDate,
            r.Gender == "female" ? "qiz" : "o'g'il",
            r.Address,
            r.ParentFullName,
            r.ParentPhone,
            r.EnrollmentDate,
            r.Phone ?? "",
            r.Language ?? "",
            r.StatusName ?? "",
            groupsByStudent.GetValueOrDefault(r.Id, ""),
            r.Balance.ToString("0.##", CultureInfo.InvariantCulture),
            r.ContractNumber ?? "",
            r.ArchivedAt ?? "",
            r.ArchiveReason ?? "",
        });

        var groupLabels = await db.StudyGroups.AsNoTracking().Where(g => !g.IsArchived)
            .Select(g => new { g.SubjectId, g.Name })
            .OrderBy(g => g.SubjectId).ThenBy(g => g.Name)
            .ToListAsync(ct);

        var bytes = ExcelExport.Build(new[]
        {
            new ExcelExport.SheetSpec(StudentImportSheet.SheetName, headers, data),
            new ExcelExport.SheetSpec("Holatlar",
                new[] { "Holat" },
                statusNames.Select(n => (IReadOnlyList<string>)new[] { n })),
            new ExcelExport.SheetSpec("Guruhlar",
                new[] { "Guruh (Guruhlar katagiga shu matnni yozing)" },
                groupLabels.Select(g => (IReadOnlyList<string>)new[]
                    { $"{SubjectLabel(subjectNameById, g.SubjectId)}: {g.Name}" })),
        });

        return File(bytes, XlsxMime, $"oquvchilar_{AppClock.Now:yyyy-MM-dd}.xlsx");
    }

    /// <summary>
    /// S-7 — ommaviy BUTUNLAY o'chirish (arxiv tab'idan).
    ///
    /// <para>
    /// <b>Hammasi yoki hech nima.</b> Moliyaviy yozuvi (hisob-faktura yoki
    /// to'lov) bor birorta o'quvchi topilsa, amal butunlay rad etiladi va
    /// ro'yxat qaytadi — bitta arxivlash bilan bir xil qoida
    /// (<see cref="StudentBulkArchiveController"/>). "O'n beshtadan uchtasi
    /// qoldi" — administrator uchun eng yomon natija.
    /// </para>
    /// <para>
    /// Qoida <c>StudentsController.Delete</c> dagining o'zi: pul yozuvi hech
    /// qachon o'chmaydi (SPEC §4.1), bunday o'quvchi arxivda qoladi.
    /// </para>
    /// </summary>
    [HttpPost("delete-many")]
    public async Task<ActionResult<StudentDeleteResultDto>> DeleteMany(
        BulkDeleteStudentsRequest req, CancellationToken ct = default)
    {
        var ids = (req.StudentIds ?? []).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct().ToList();
        if (ids.Count == 0) return BadRequest(new { message = "O'quvchi tanlanmadi" });
        if (ids.Count > MaxDeleteBatch)
            return BadRequest(new { message = $"Bir martada eng ko'pi {MaxDeleteBatch} ta o'quvchi o'chiriladi" });

        var students = await db.Students.Where(s => ids.Contains(s.Id)).ToListAsync(ct);
        if (students.Count == 0)
            return BadRequest(new { message = "O'chiriladigan o'quvchi topilmadi" });

        // Moliyaviy yozuvi borlar — bitta partiyada (sikl ichida so'rov yo'q).
        var withInvoice = await db.Invoices.AsNoTracking()
            .Where(i => ids.Contains(i.StudentId)).Select(i => i.StudentId).Distinct().ToListAsync(ct);
        var withPayment = await db.Payments.AsNoTracking()
            .Where(p => ids.Contains(p.StudentId)).Select(p => p.StudentId).Distinct().ToListAsync(ct);
        var moneyed = withInvoice.Concat(withPayment).ToHashSet(StringComparer.Ordinal);

        var blocked = students
            .Where(s => moneyed.Contains(s.Id))
            .OrderBy(s => s.FullName, StringComparer.OrdinalIgnoreCase)
            .Select(s => new StudentDeleteBlockedDto(s.Id, s.FullName,
                "Moliyaviy yozuvi (hisob-faktura yoki to'lov) bor"))
            .ToList();

        if (blocked.Count > 0)
            return BadRequest(new StudentDeleteResultDto(0, blocked,
                "Moliyaviy yozuvi bor o'quvchini o'chirib bo'lmaydi — uni arxivda qoldiring. "
                + "Hech kim o'chirilmadi."));

        foreach (var student in students)
        {
            // Audit qatori o'quvchidan OLDIN yoziladi: `audit_logs.student_id`
            // da FK yo'q (AppDbContext.cs — faqat indeks), ya'ni iz o'quvchi
            // o'chgandan keyin ham qoladi. Butunlay o'chirishning yagona izi
            // shu.
            audit.Record(AuditEntityStudentDelete, student.Id, "delete",
                $"O'quvchi butunlay o'chirildi ({student.FullName}, {student.ClassName})",
                before: new { student.FullName, student.ClassName, student.IsArchived },
                studentId: student.Id);

            if (student.UserId is not null)
            {
                var user = await db.Users.FirstOrDefaultAsync(u => u.Id == student.UserId, ct);
                if (user is not null) db.Users.Remove(user);
            }
            db.Students.Remove(student);
        }

        await db.SaveChangesAsync(ct);
        return new StudentDeleteResultDto(students.Count, []);
    }

    /// <summary>
    /// Audit yozuvidagi entity turi. <c>AuditService.cs</c> shu to'lqinda
    /// bir nechta slice uchun MUZLATILGAN fayl, shuning uchun konstanta shu
    /// yerda turibdi; wiring bosqichida u <c>AuditService</c> ga ko'chiriladi.
    /// </summary>
    public const string AuditEntityStudentDelete = AuditService.EntityStudentDelete;

    /// <summary>Guruh yorlig'idagi fan nomi; yo'nalish guruhi fansiz — "Yo'nalish".</summary>
    private static string SubjectLabel(Dictionary<string, string> names, string? subjectId) =>
        subjectId is null ? "Yo'nalish" : names.GetValueOrDefault(subjectId, "?");
}
