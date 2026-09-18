using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Auth;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Server.Controllers;

// ===========================================================================
//  O'QUVCHINING VASIYLARI — docs/modules/students-parity.md §2.3 (S-8).
// ===========================================================================
//
//  NEGA ALOHIDA CONTROLLER, VA NEGA `AdminGuardiansController` EMAS
//  ---------------------------------------------------------------
//  `/api/admin/guardians` VASIYDAN qaraydi ("bu odamning farzandlari") va
//  `app` ruxsati bilan himoyalangan — u "Ilova" bo'limining ekrani.
//  Bu yerdagisi O'QUVCHIDAN qaraydi ("bu bolaning vasiylari") va o'quvchi
//  formasi ochadi, shuning uchun darvoza ham o'quvchi endpointlariniki:
//  `students`. Ikkalasi bitta jadval ustida ishlaydi, lekin ruxsat
//  darvozasi har bo'limning o'ziniki bo'lib qolgani ma'qul.
//
//  `StudentsController` ga qo'shilmadi: u 800 qatordan oshgan va bu
//  to'lqinda unga boshqa slice ham tegadi. Prefiks bir xil
//  (`api/admin/students`), ya'ni mijoz tarafda farq bilinmaydi — xuddi
//  `StudentSearchController` va `StudentBulkArchiveController` dagidek.
//
//  ESKI IKKI USTUN BILAN QANDAY USHLAB TURILADI
//  --------------------------------------------
//  `students.parent_full_name` / `.parent_phone` o'nlab joydan o'qiladi
//  (ota-ona portali telefon bo'yicha oilani TOPADI). Shu sabab ASOSIY
//  vasiyga tegadigan har amaldan keyin — tahrir, asosiysini almashtirish,
//  uzish — o'quvchi qatori qayta tekislanadi
//  (`GuardianSync.MirrorPrimaryFromDbAsync`). Asosiy vasiy qolmasa ustunlar
//  TEGILMAYDI: bo'shatish ma'lumot yo'qotish bo'lardi.
// ===========================================================================

/// <summary>O'quvchi kartochkasi va uning vasiylari (`/api/admin/students/{id}/guardians`).</summary>
[ApiController]
[Authorize]
[AdminPerm("students")]
[Route("api/admin/students")]
public sealed class StudentGuardiansController(AppDbContext db) : ControllerBase
{
    public const string PhoneRequiredMessage = "Telefon raqami noto'g'ri";
    public const string NameRequiredMessage = "F.I.SH kerak";
    public const string PhoneTakenMessage = "Bu telefon raqami boshqa vasiyga tegishli";
    public const string LastGuardianMessage = "Yagona vasiyni uzib bo'lmaydi";

    /// <summary>
    /// Forma tahrirda yuklaydigan qo'shimcha ma'lumot: ro'yxat ustunlarida
    /// bo'lmagan maydonlar (telefon, til, hujjat nusxasi) va vasiylar.
    /// </summary>
    // Marshrut ATAYLAB `form-card`: `{id}/card` ni profil sahifasining
    // kartochkasi (`StudentProfileController`) egallagan va ikkovi bitta
    // manzilda turgani uchun ASP.NET `AmbiguousMatchException` bergan edi.
    // Ikkisining javobi ham boshqacha: u — sarlavha faktlari, bu — formani
    // to'ldirish uchun (vasiylar bilan).
    [HttpGet("{studentId}/form-card")]
    public async Task<ActionResult<StudentFormCardDto>> Card(string studentId, CancellationToken ct)
    {
        var student = await db.Students.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == studentId, ct);
        if (student is null) return NotFound();

        return new StudentFormCardDto(
            student.Id, student.Phone, student.Language, student.DocumentUrl,
            await RowsAsync(studentId, ct));
    }

    /// <summary>O'quvchining vasiylari — asosiysi birinchi.</summary>
    [HttpGet("{studentId}/guardians")]
    public async Task<ActionResult<IEnumerable<StudentGuardianDto>>> List(
        string studentId, CancellationToken ct) =>
        await RowsAsync(studentId, ct);

    /// <summary>
    /// Vasiy qo'shish. Telefon allaqachon ro'yxatda bo'lsa MAVJUD vasiy
    /// biriktiriladi (dublikat yaratilmaydi) — "bir raqam, bir vasiy"
    /// qoidasi `ux_guardians_phone_key` bilan baza darajasida ham bor.
    /// </summary>
    [HttpPost("{studentId}/guardians")]
    public async Task<ActionResult<StudentGuardianDto>> Attach(
        string studentId, StudentGuardianInput input, CancellationToken ct)
    {
        var student = await db.Students.FirstOrDefaultAsync(s => s.Id == studentId, ct);
        if (student is null) return NotFound();

        if (Invalid(input) is { } message) return BadRequest(new { message });

        await GuardianSync.ApplyAsync(db, student, [input], ct);
        if (input.IsPrimary) await MirrorAsync(student, ct);

        var key = PhoneUtil.Key(input.Phone!);
        var saved = (await RowsAsync(studentId, ct))
            .FirstOrDefault(r => PhoneUtil.Key(r.Phone) == key);
        return saved is null ? NotFound() : saved;
    }

    /// <summary>Vasiy ma'lumotini va bog'lanish turini tahrirlash.</summary>
    [HttpPut("{studentId}/guardians/{guardianId}")]
    public async Task<ActionResult<StudentGuardianDto>> Update(
        string studentId, string guardianId, StudentGuardianInput input, CancellationToken ct)
    {
        var student = await db.Students.FirstOrDefaultAsync(s => s.Id == studentId, ct);
        if (student is null) return NotFound();

        var link = await db.StudentGuardians
            .FirstOrDefaultAsync(l => l.StudentId == studentId && l.GuardianId == guardianId, ct);
        if (link is null) return NotFound();

        var guardian = await db.Guardians.FirstOrDefaultAsync(g => g.Id == guardianId, ct);
        if (guardian is null) return NotFound();

        if (Invalid(input) is { } message) return BadRequest(new { message });

        var phone = input.Phone!.Trim();
        var key = PhoneUtil.Key(phone);
        if (await db.Guardians.AnyAsync(g => g.PhoneKey == key && g.Id != guardianId, ct))
            return BadRequest(new { message = PhoneTakenMessage });

        guardian.FullName = input.FullName!.Trim();
        guardian.Phone = phone;
        if (input.PassportUrl is not null)
            guardian.PassportUrl = string.IsNullOrWhiteSpace(input.PassportUrl)
                ? null
                : input.PassportUrl.Trim();

        link.Relation = GuardianSync.NormalizeRelation(input.Relation);
        link.RelationNote = link.Relation == GuardianRelation.Other
            ? (string.IsNullOrWhiteSpace(input.RelationNote) ? null : input.RelationNote.Trim())
            : null;

        await db.SaveChangesAsync(ct);

        if (input.IsPrimary) await GuardianSync.SetPrimaryAsync(db, studentId, guardianId, ct);
        await MirrorAsync(student, ct);

        var saved = (await RowsAsync(studentId, ct)).FirstOrDefault(r => r.GuardianId == guardianId);
        return saved is null ? NotFound() : saved;
    }

    /// <summary>Asosiy vasiyni almashtirish — chek, shartnoma va xabar shu odamga boradi.</summary>
    [HttpPost("{studentId}/guardians/{guardianId}/primary")]
    public async Task<IActionResult> MakePrimary(
        string studentId, string guardianId, CancellationToken ct)
    {
        var student = await db.Students.FirstOrDefaultAsync(s => s.Id == studentId, ct);
        if (student is null) return NotFound();

        var exists = await db.StudentGuardians
            .AnyAsync(l => l.StudentId == studentId && l.GuardianId == guardianId, ct);
        if (!exists) return NotFound();

        await GuardianSync.SetPrimaryAsync(db, studentId, guardianId, ct);
        await MirrorAsync(student, ct);
        return NoContent();
    }

    /// <summary>
    /// Vasiyni o'quvchidan uzish. Vasiy qatori O'CHIRILMAYDI — u boshqa
    /// farzandga bog'langan bo'lishi mumkin va akkaunti bor bo'lsa
    /// o'chirish uni Telegram'dan uzib qo'yardi.
    ///
    /// <para>
    /// YAGONA vasiyni uzib bo'lmaydi: shundan keyin <c>parent_phone</c>
    /// egasi bo'lmagan raqamga aylanardi va ota-ona portali o'sha oilani
    /// yo'qotardi. Avval yangi vasiy qo'shilsin.
    /// </para>
    /// </summary>
    [HttpDelete("{studentId}/guardians/{guardianId}")]
    public async Task<IActionResult> Detach(string studentId, string guardianId, CancellationToken ct)
    {
        var student = await db.Students.FirstOrDefaultAsync(s => s.Id == studentId, ct);
        if (student is null) return NotFound();

        var links = await db.StudentGuardians.Where(l => l.StudentId == studentId).ToListAsync(ct);
        var link = links.FirstOrDefault(l => l.GuardianId == guardianId);
        if (link is null) return NotFound();
        if (links.Count == 1) return BadRequest(new { message = LastGuardianMessage });

        var wasPrimary = link.IsPrimary;
        db.StudentGuardians.Remove(link);
        await db.SaveChangesAsync(ct);

        if (wasPrimary)
        {
            // Asosiysi ketdi — eng eski qolgani asosiy bo'ladi, aks holda
            // o'quvchi asosiy vasiysiz qolardi (chek kimga chiqadi?).
            var next = links.Where(l => l.GuardianId != guardianId)
                .OrderBy(l => l.CreatedAt).First();
            await GuardianSync.SetPrimaryAsync(db, studentId, next.GuardianId, ct);
            await MirrorAsync(student, ct);
        }

        return NoContent();
    }

    // ------------------------------------------------------------------
    //  Ichki
    // ------------------------------------------------------------------

    /// <summary>Asosiy vasiydan o'quvchi qatorini tekislaydi va saqlaydi.</summary>
    private async Task MirrorAsync(Student student, CancellationToken ct)
    {
        await GuardianSync.MirrorPrimaryFromDbAsync(db, student, ct);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Kirish ma'lumoti to'g'rimi; xato bo'lsa — foydalanuvchiga xabar.</summary>
    private static string? Invalid(StudentGuardianInput input)
    {
        if (string.IsNullOrWhiteSpace(input.FullName)) return NameRequiredMessage;
        if (PhoneUtil.DigitsOnly(input.Phone ?? "").Length < 7) return PhoneRequiredMessage;

        var relation = (input.Relation ?? "").Trim();
        if (relation.Length > 0 && !GuardianRelation.IsStorable(relation.ToLowerInvariant()))
            return $"Vasiylik turi noto'g'ri: \"{relation}\" ({string.Join(" | ", GuardianRelation.Stored)})";

        return null;
    }

    /// <summary>
    /// Bitta o'quvchining vasiylari, asosiysi birinchi. Akkaunt, Telegram
    /// holati va farzandlar soni UCHTA to'plamli so'rov bilan olinadi —
    /// vasiy soniga bog'liq emas (N+1 yo'q).
    /// </summary>
    private async Task<List<StudentGuardianDto>> RowsAsync(string studentId, CancellationToken ct)
    {
        var pairs = await (from l in db.StudentGuardians.AsNoTracking()
                           join g in db.Guardians.AsNoTracking() on l.GuardianId equals g.Id
                           where l.StudentId == studentId
                           select new { Link = l, Guardian = g })
            .ToListAsync(ct);
        if (pairs.Count == 0) return [];

        var guardianIds = pairs.Select(p => p.Guardian.Id).ToList();
        var userIds = pairs.Where(p => p.Guardian.UserId is not null)
            .Select(p => p.Guardian.UserId!).ToList();

        var childCounts = (await db.StudentGuardians.AsNoTracking()
                .Where(l => guardianIds.Contains(l.GuardianId))
                .Select(l => l.GuardianId)
                .ToListAsync(ct))
            .GroupBy(id => id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

        var linked = (await db.TelegramAccounts.AsNoTracking()
                .Where(a => userIds.Contains(a.UserId))
                .Select(a => a.UserId).ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);

        return [.. pairs
            .OrderByDescending(p => p.Link.IsPrimary)
            .ThenBy(p => p.Link.CreatedAt)
            .Select(p => new StudentGuardianDto(
                p.Guardian.Id,
                p.Guardian.FullName,
                p.Guardian.Phone,
                p.Link.Relation,
                p.Link.RelationNote,
                p.Link.IsPrimary,
                p.Guardian.PassportUrl,
                p.Guardian.UserId is not null,
                p.Guardian.UserId is not null && linked.Contains(p.Guardian.UserId),
                childCounts.GetValueOrDefault(p.Guardian.Id)))];
    }
}
