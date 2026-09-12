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
//  Admin: vasiylar (SPEC §3.2) — ko'p-ko'pga bog'lanishni boshqarish.
// ===========================================================================
//
//  Bu ekran `/api/admin/parents` NING O'RNINI BOSMAYDI. U yerdagi ro'yxat
//  o'quvchi qatoridagi `parent_phone` ni telefon bo'yicha guruhlab ko'rsatadi
//  va shu holicha qoladi (uni o'zgartirish P1-21 dan keyin ikkinchi
//  "retirement" bo'lardi). Bu yerdagisi esa HAQIQIY bog'lanish jadvali
//  ustida ishlaydi: bitta vasiyni ikkinchi farzandga biriktirish, unga
//  akkaunt ochish va Telegram bog'lanishini ko'rish.
//
//  Vasiy qatorining O'ZI odatda qo'lda yaratilmaydi: `GuardianSync` uni
//  o'quvchi yozilganda `parent_phone` dan avtomatik chiqaradi. Bu yerdagi
//  POST/PUT — ikkinchi vasiy (buvi, ishonchli shaxs) va ma'lumotni to'g'rilash
//  uchun.
// ===========================================================================

/// <summary>Admin "Vasiylar" bo'limi (`/api/admin/guardians`).</summary>
[ApiController]
[Authorize]
[AdminPerm("app")]
[Route("api/admin/guardians")]
public sealed class AdminGuardiansController(AppDbContext db) : ControllerBase
{
    /// <summary>Ro'yxat uzunligining yuqori chegarasi — qidiruvsiz butun jadval tortilmasin.</summary>
    private const int PageLimit = 200;

    private const int MinPasswordLength = 8;

    /// <summary>
    /// Vasiylar ro'yxati — har birida farzandlari, akkaunti va Telegram holati.
    /// <paramref name="search"/> — ism yoki telefon bo'yicha (bo'sh bo'lsa birinchi 200 ta).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<GuardianDto>>> List(
        [FromQuery] string? search, CancellationToken ct)
    {
        var query = db.Guardians.AsNoTracking();
        var term = (search ?? "").Trim();
        if (term.Length > 0)
        {
            // Qidiruv kaliti ham OXIRGI 9 RAQAM (`PhoneUtil.Key`) — `phone_key`
            // ustunidagi bilan bir xil. To'liq "+998 90 123 45 67" yozilganda
            // 12 raqamli satrni 9 raqamli ustundan qidirish hech qachon topmasdi.
            var key = PhoneUtil.Key(term);
            query = key.Length >= 3
                ? query.Where(g => g.PhoneKey.Contains(key) || g.FullName.Contains(term))
                : query.Where(g => g.FullName.Contains(term));
        }

        var guardians = await query
            .OrderBy(g => g.FullName)
            .Take(PageLimit)
            .ToListAsync(ct);
        if (guardians.Count == 0) return new List<GuardianDto>();

        return await ToDtosAsync(guardians, ct);
    }

    /// <summary>Bitta o'quvchining vasiylari (o'quvchi kartochkasi uchun).</summary>
    [HttpGet("by-student/{studentId}")]
    public async Task<ActionResult<IEnumerable<GuardianDto>>> ByStudent(string studentId, CancellationToken ct)
    {
        var ids = await db.StudentGuardians.AsNoTracking()
            .Where(l => l.StudentId == studentId)
            .Select(l => l.GuardianId).ToListAsync(ct);
        if (ids.Count == 0) return new List<GuardianDto>();

        var guardians = await db.Guardians.AsNoTracking()
            .Where(g => ids.Contains(g.Id)).OrderBy(g => g.FullName).ToListAsync(ct);
        return await ToDtosAsync(guardians, ct);
    }

    /// <summary>Yangi vasiy. Telefon allaqachon ro'yxatda bo'lsa — mavjudi qaytariladi (dublikat yaratilmaydi).</summary>
    [HttpPost]
    public async Task<ActionResult<GuardianDto>> Create(SaveGuardianRequest req, CancellationToken ct)
    {
        var name = (req.FullName ?? "").Trim();
        var phone = (req.Phone ?? "").Trim();
        if (name.Length == 0) return BadRequest(new { message = "F.I.SH kerak" });
        if (PhoneUtil.DigitsOnly(phone).Length < 7)
            return BadRequest(new { message = "Telefon raqami noto'g'ri" });

        var key = PhoneUtil.Key(phone);
        var existing = await db.Guardians.FirstOrDefaultAsync(g => g.PhoneKey == key, ct);
        if (existing is not null) return (await ToDtosAsync([existing], ct))[0];

        var guardian = new Guardian
        {
            FullName = name,
            Phone = phone,
            PassportUrl = string.IsNullOrWhiteSpace(req.PassportUrl) ? null : req.PassportUrl.Trim(),
            // PhoneKey YOZILMAYDI — u generated stored column.
        };
        db.Guardians.Add(guardian);
        await db.SaveChangesAsync(ct);

        return (await ToDtosAsync([guardian], ct))[0];
    }

    /// <summary>Vasiy ma'lumotini tahrirlash.</summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<GuardianDto>> Update(string id, SaveGuardianRequest req, CancellationToken ct)
    {
        var guardian = await db.Guardians.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (guardian is null) return NotFound();

        var name = (req.FullName ?? "").Trim();
        var phone = (req.Phone ?? "").Trim();
        if (name.Length == 0) return BadRequest(new { message = "F.I.SH kerak" });
        if (PhoneUtil.DigitsOnly(phone).Length < 7)
            return BadRequest(new { message = "Telefon raqami noto'g'ri" });

        var key = PhoneUtil.Key(phone);
        var taken = await db.Guardians.AnyAsync(g => g.PhoneKey == key && g.Id != id, ct);
        if (taken) return BadRequest(new { message = "Bu telefon raqami boshqa vasiyga tegishli" });

        guardian.FullName = name;
        guardian.Phone = phone;
        if (req.PassportUrl is not null)
            guardian.PassportUrl = string.IsNullOrWhiteSpace(req.PassportUrl) ? null : req.PassportUrl.Trim();
        await db.SaveChangesAsync(ct);

        return (await ToDtosAsync([guardian], ct))[0];
    }

    /// <summary>Vasiyga farzand biriktirish (ikkinchi farzand shu yerdan qo'shiladi).</summary>
    [HttpPost("{id}/children")]
    public async Task<ActionResult<GuardianDto>> AttachChild(
        string id, AttachChildRequest req, CancellationToken ct)
    {
        var guardian = await db.Guardians.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (guardian is null) return NotFound();

        var student = await db.Students.FirstOrDefaultAsync(s => s.Id == req.StudentId, ct);
        if (student is null) return NotFound(new { message = "O'quvchi topilmadi" });

        var relation = string.IsNullOrWhiteSpace(req.Relation) ? GuardianRelation.Parent : req.Relation.Trim();
        if (!GuardianRelation.IsValid(relation))
            return BadRequest(new { message = "Vasiylik turi noto'g'ri (parent | grandparent | trustee)" });

        var link = await db.StudentGuardians
            .FirstOrDefaultAsync(l => l.StudentId == student.Id && l.GuardianId == guardian.Id, ct);
        if (link is null)
        {
            link = new StudentGuardian { StudentId = student.Id, GuardianId = guardian.Id };
            db.StudentGuardians.Add(link);
        }
        link.Relation = relation;

        if (req.IsPrimary)
        {
            // Bittadan ortiq asosiy vasiyga baza yo'l qo'ymaydi
            // (`ux_student_guardians_one_primary`), shuning uchun avvalgisini olib tashlaymiz.
            var others = await db.StudentGuardians
                .Where(l => l.StudentId == student.Id && l.GuardianId != guardian.Id && l.IsPrimary)
                .ToListAsync(ct);
            foreach (var other in others) other.IsPrimary = false;
        }
        link.IsPrimary = req.IsPrimary;

        await db.SaveChangesAsync(ct);
        return (await ToDtosAsync([guardian], ct))[0];
    }

    /// <summary>Farzandni vasiydan uzish.</summary>
    [HttpDelete("{id}/children/{studentId}")]
    public async Task<IActionResult> DetachChild(string id, string studentId, CancellationToken ct)
    {
        var link = await db.StudentGuardians
            .FirstOrDefaultAsync(l => l.StudentId == studentId && l.GuardianId == id, ct);
        if (link is null) return NotFound();

        db.StudentGuardians.Remove(link);
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>
    /// Vasiyga tizim akkaunti ochadi (rol = <c>parent</c>).
    ///
    /// <para>
    /// LOGIN — TELEFON RAQAMINING RAQAMLARI. Bu tasodifiy tanlov emas:
    /// mavjud ota-ona yo'llari (<c>StudentPortalController.TargetAsync</c>,
    /// <c>PortalFinanceController.ResolveAsync</c>) ota-onani <c>users.email</c>
    /// dagi raqamni <c>students.parent_phone</c> bilan solishtirib topadi.
    /// Boshqa login berilsa, yangi vasiy akkaunti Mini App'da ishlab, eski
    /// web portalda ishlamasdi.
    /// </para>
    /// </summary>
    [HttpPost("{id}/account")]
    public async Task<ActionResult<CredentialsDto>> CreateAccount(
        string id, CreateGuardianAccountRequest? req, CancellationToken ct)
    {
        var guardian = await db.Guardians.FirstOrDefaultAsync(g => g.Id == id, ct);
        if (guardian is null) return NotFound();

        var login = PhoneUtil.DigitsOnly(guardian.Phone);
        if (login.Length < 7) return BadRequest(new { message = "Vasiyning telefon raqami noto'g'ri" });

        var password = (req?.NewPassword ?? "").Trim();
        if (password.Length > 0 && password.Length < MinPasswordLength)
            return BadRequest(new { message = $"Parol kamida {MinPasswordLength} belgidan iborat bo'lsin" });
        if (password.Length == 0) password = AccountFactory.GeneratePassword();

        var user = guardian.UserId is null
            ? null
            : await db.Users.FirstOrDefaultAsync(u => u.Id == guardian.UserId, ct);

        // Akkaunt allaqachon shu login bilan mavjud bo'lishi mumkin — eski
        // "ota-ona = telefon" yo'lidan qolgan. Uni QAYTA ISHLATAMIZ, ikkinchisini
        // yaratmaymiz: aks holda bitta oilada ikkita parol paydo bo'lardi.
        user ??= await db.Users.FirstOrDefaultAsync(u => u.Email == login, ct);

        if (user is null)
        {
            user = new AppUser { FullName = guardian.FullName, Role = "parent", Email = login };
            db.Users.Add(user);
        }
        else if (user.Role != "parent")
        {
            return BadRequest(new { message = $"'{login}' logini boshqa rol ({user.Role}) tomonidan band" });
        }

        user.FullName = guardian.FullName;
        user.SetInitialPassword(password);
        guardian.UserId = user.Id;
        await db.SaveChangesAsync(ct);

        return new CredentialsDto(user.Email, password, user.Role);
    }

    // ------------------------------------------------------------------
    //  Ichki
    // ------------------------------------------------------------------

    /// <summary>
    /// Vasiylarni DTO ga o'giradi. Farzandlar, akkauntlar va Telegram
    /// bog'lanishlari UCHTA to'plamli so'rov bilan olinadi — vasiy soniga
    /// bog'liq emas (N+1 yo'q).
    /// </summary>
    private async Task<List<GuardianDto>> ToDtosAsync(
        IReadOnlyCollection<Guardian> guardians, CancellationToken ct)
    {
        var ids = guardians.Select(g => g.Id).ToList();
        var userIds = guardians.Where(g => g.UserId is not null).Select(g => g.UserId!).ToList();

        var children = await (from l in db.StudentGuardians.AsNoTracking()
                              join s in db.Students.AsNoTracking() on l.StudentId equals s.Id
                              where ids.Contains(l.GuardianId)
                              select new { l.GuardianId, s.Id, s.FullName, s.ClassName, l.Relation, l.IsPrimary })
            .ToListAsync(ct);

        var logins = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Email, ct);

        var linked = (await db.TelegramAccounts.AsNoTracking()
                .Where(a => userIds.Contains(a.UserId))
                .Select(a => a.UserId).ToListAsync(ct))
            .ToHashSet(StringComparer.Ordinal);

        return [.. guardians.Select(g => new GuardianDto(
            g.Id, g.FullName, g.Phone, g.PassportUrl,
            g.UserId,
            g.UserId is not null ? logins.GetValueOrDefault(g.UserId) : null,
            g.UserId is not null && linked.Contains(g.UserId),
            [.. children
                .Where(c => c.GuardianId == g.Id)
                .OrderByDescending(c => c.IsPrimary).ThenBy(c => c.ClassName).ThenBy(c => c.FullName)
                .Select(c => new GuardianChildDto(c.Id, c.FullName, c.ClassName, c.Relation, c.IsPrimary))]))];
    }
}
