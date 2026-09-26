using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Infrastructure.Auth;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Application.Dtos;
using SchoolLms.Application.Services;
using SchoolLms.Domain;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Xodimlar — o'qituvchi BO'LMAGAN ishchilar (kassir, administrator, ...). Har biriga admin
/// paneliga kiruvchi tizim akkaunti (role="staff") generatsiya qilinadi. Qaysi bo'limlarni
/// ko'rishi <see cref="AppUser.Permissions"/> bilan boshqariladi — uni FAQAT superadmin
/// ("Xodimlar va rollar" bo'limi / <see cref="SetPermissions"/>) o'zgartiradi.
/// </summary>
[ApiController]
[Authorize]
[AdminPerm("staff")]
[Route("api/admin/staff")]
public class StaffController(AppDbContext db, AuditService audit) : ControllerBase
{
    private const int MinPasswordLength = 8;
    private const string WeakPasswordMessage = "Parol kamida 8 belgidan iborat bo'lsin";
    /// <summary><c>users.salary</c> — <c>numeric(14,2)</c> ning eng katta qiymati.</summary>
    private const decimal MaxSalary = 999_999_999_999.99m;


    /// <summary>
    /// Xodimlar ro'yxati. <see cref="AdminPermAttribute"/> o'qishni HAR QANDAY xodimga ochadi
    /// (ro'yxat boshqa ekranlarda ma'lumotnoma sifatida ishlatiladi), oylik esa pul — uni faqat
    /// admin, moliya rollari va "staff" bo'limi (to'liq yoki faqat ko'rish) bor xodim ko'radi;
    /// boshqalarga <c>salary = 0</c>, <c>salaryStartDate = ""</c> (F0.02 dagi maosh qoidasi).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<StaffDto>>> GetAll()
    {
        var roleNames = await RoleNamesAsync();
        var showSalary = CanSeeSalary();
        return (await db.Users.Where(u => u.Role == Roles.Staff).OrderBy(u => u.FullName).ToListAsync())
            .Select(u => ToDto(u, roleNames, showSalary)).ToList();
    }

    [HttpPost]
    public async Task<ActionResult<StaffDto>> Create(StaffPayload p)
    {
        if (string.IsNullOrWhiteSpace(p.FullName)) return BadRequest(new { message = "F.I.SH kerak" });
        if (ValidateSalary(p) is { } salaryError) return BadRequest(new { message = salaryError });
        var user = AccountFactory.CreateAccountFor(db, Roles.Staff, p.FullName.Trim());
        user.Position = (p.Position ?? "").Trim();
        ApplySalary(user, p);
        if (ApplyAvatar(user, p.AvatarUrl) is { } avatarError) return BadRequest(new { message = avatarError });
        if (!string.IsNullOrWhiteSpace(p.NewPassword))
        {
            if (p.NewPassword.Trim().Length < MinPasswordLength)
                return BadRequest(new { message = WeakPasswordMessage });
            user.SetInitialPassword(p.NewPassword.Trim());
        }
        if (user.Salary != 0 || user.SalaryStartDate.Length > 0)
            audit.Record(AuditService.EntityStaffSalary, user.Id, "create",
                $"Oylik belgilandi: {user.FullName} — {AuditService.Money(user.Salary)} so'm"
                + (user.SalaryStartDate.Length > 0 ? $", {user.SalaryStartDate} dan" : ""),
                after: new { user.Salary, user.SalaryStartDate });
        await db.SaveChangesAsync();
        return ToDto(user, await RoleNamesAsync());
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<StaffDto>> Update(string id, StaffPayload p)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null || user.Role != Roles.Staff) return NotFound();
        if (ValidateSalary(p) is { } salaryError) return BadRequest(new { message = salaryError });
        var before = new { user.Salary, user.SalaryStartDate };
        user.FullName = p.FullName.Trim();
        user.Position = (p.Position ?? "").Trim();
        ApplySalary(user, p);
        if (ApplyAvatar(user, p.AvatarUrl) is { } avatarError) return BadRequest(new { message = avatarError });
        if (!string.IsNullOrWhiteSpace(p.NewPassword))
        {
            if (p.NewPassword.Trim().Length < MinPasswordLength)
                return BadRequest(new { message = WeakPasswordMessage });
            user.SetInitialPassword(p.NewPassword.Trim());
        }
        if (before.Salary != user.Salary || before.SalaryStartDate != user.SalaryStartDate)
            audit.Record(AuditService.EntityStaffSalary, user.Id, "update",
                $"Oylik o'zgardi: {user.FullName} — {AuditService.Money(before.Salary)} → "
                + $"{AuditService.Money(user.Salary)} so'm, boshlanish "
                + $"{(before.SalaryStartDate.Length > 0 ? before.SalaryStartDate : "—")} → "
                + $"{(user.SalaryStartDate.Length > 0 ? user.SalaryStartDate : "—")}",
                before: before, after: new { user.Salary, user.SalaryStartDate });
        await db.SaveChangesAsync();
        return ToDto(user, await RoleNamesAsync());
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null || user.Role != Roles.Staff) return NotFound();
        // `expenses.employee_user_id` is RESTRICT: salary already paid is financial history.
        // Refuse with a readable message instead of a 23503 → 500.
        if (await db.Expenses.AnyAsync(e => e.EmployeeUserId == id))
            return Conflict(new { message = "Bu xodimga maosh berilgan — akkauntni o'chirib bo'lmaydi (moliyaviy tarix saqlanadi)." });
        db.Users.Remove(user);
        await db.SaveChangesAsync();
        return NoContent();
    }

    /// <summary>Xodim akkaunti logini. Parol xavfsizlik uchun saqlanmaydi — bo'sh qaytadi
    /// (ko'rsatish kerak bo'lsa <see cref="ResetPassword"/> orqali yangisini yarating).</summary>
    [HttpGet("{id}/credentials")]
    public async Task<ActionResult<CredentialsDto>> Credentials(string id)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null || user.Role != Roles.Staff) return NotFound();
        return new CredentialsDto(user.Email, user.InitialPassword ?? "", user.Role);
    }

    /// <summary>Xodimga yangi tasodifiy parol generatsiya qiladi va uni BIR MARTA qaytaradi
    /// (DB'da faqat hash saqlanadi).</summary>
    [HttpPost("{id}/reset-password")]
    public async Task<ActionResult<CredentialsDto>> ResetPassword(string id)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null || user.Role != Roles.Staff) return NotFound();
        var pwd = AccountFactory.GeneratePassword();
        user.SetInitialPassword(pwd);
        await db.SaveChangesAsync();
        return new CredentialsDto(user.Email, pwd, user.Role);
    }

    /// <summary>Xodimning admin bo'lim ruxsatlari (Rollar) — FAQAT superadmin.</summary>
    [HttpPut("{id}/permissions")]
    [Authorize(Roles = "superadmin")]
    public async Task<ActionResult<StaffDto>> SetPermissions(string id, SetStaffPermissionsRequest req)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null || user.Role != Roles.Staff) return NotFound();
        user.Permissions = req.Permissions ?? new();
        // Hand-picked permissions no longer follow a role.
        user.AccessRoleId = null;
        await db.SaveChangesAsync();
        return ToDto(user, await RoleNamesAsync());
    }

    /// <summary>
    /// Assigns an access role (Boshqaruv → Rollar) — FAQAT superadmin, like permissions.
    /// The member's permissions become a copy of the role's; removing the role clears them.
    /// </summary>
    [HttpPut("{id}/role")]
    [Authorize(Roles = "superadmin")]
    public async Task<ActionResult<StaffDto>> SetRole(string id, SetStaffRoleRequest req)
    {
        var user = await db.Users.FindAsync(id);
        if (user is null || user.Role != Roles.Staff) return NotFound();

        if (req.AccessRoleId is { } roleId)
        {
            var role = await db.AccessRoles.AsNoTracking().FirstOrDefaultAsync(r => r.Id == roleId);
            if (role is null) return BadRequest(new { message = "Rol topilmadi" });
            user.AccessRoleId = role.Id;
            user.Permissions = [.. role.Permissions];
        }
        else
        {
            user.AccessRoleId = null;
            user.Permissions = new();
        }

        await db.SaveChangesAsync();
        return ToDto(user, await RoleNamesAsync());
    }

    private Task<Dictionary<Guid, string>> RoleNamesAsync() =>
        db.AccessRoles.AsNoTracking().ToDictionaryAsync(r => r.Id, r => r.Name);

    private static StaffDto ToDto(AppUser u, IReadOnlyDictionary<Guid, string> roleNames, bool showSalary = true) =>
        new(u.Id, u.FullName, u.Position, u.Email, u.Permissions, u.AvatarUrl,
            u.AccessRoleId,
            u.AccessRoleId is { } rid ? roleNames.GetValueOrDefault(rid) : null,
            u.LastLoginAt,
            u.Phone, showSalary ? u.Salary : 0m, showSalary ? u.SalaryStartDate : "");

    /// <summary>Oylikni kim ko'radi: admin/superadmin, moliya rollari, "staff" yoki "staff:view" ruxsati.</summary>
    private bool CanSeeSalary() =>
        User.IsInRole(Roles.Admin) || User.IsInRole(Roles.SuperAdmin)
        || User.IsInRole(Roles.FinanceDelegate) || User.IsInRole(Roles.FinanceViewer)
        || User.HasClaim(c => c.Type == AdminPermAttribute.ClaimType
                              && (c.Value == "staff" || c.Value == "staff" + Roles.ViewSuffix));

    /// <summary>Oylik va boshlanish sanasini tekshiradi (hech narsani o'zgartirmaydi). Xato matni yoki null.</summary>
    private static string? ValidateSalary(StaffPayload p)
    {
        if (p.Salary is { } salary)
        {
            if (salary < 0) return "Oylik manfiy bo'lishi mumkin emas";
            if (decimal.Round(salary, 2) != salary || salary > MaxSalary) return "Oylik noto'g'ri";
        }
        var start = p.SalaryStartDate?.Trim();
        if (!string.IsNullOrEmpty(start) && !StaffSalaryCalc.IsIsoDate(start))
            return "Maosh boshlanish sanasi yyyy-MM-dd ko'rinishida bo'lsin";
        return null;
    }

    /// <summary>Telefon, oylik, boshlanish sanasi: null — o'zgarmaydi. Avval <see cref="ValidateSalary"/>.</summary>
    private static void ApplySalary(AppUser user, StaffPayload p)
    {
        if (p.Phone is not null) user.Phone = p.Phone.Trim();
        if (p.Salary is { } salary) user.Salary = salary;
        if (p.SalaryStartDate is not null) user.SalaryStartDate = p.SalaryStartDate.Trim();
    }

    /// <summary>
    /// Rasm manzilini qo'llaydi. Faqat o'zimizning yuklangan fayl (<c>/uploads/…</c>) —
    /// begona URL (kuzatuv pikseli, boshqa sayt) avatar sifatida ko'rsatilmasin.
    /// </summary>
    private static string? ApplyAvatar(AppUser user, string? url)
    {
        if (url is null) return null;
        var v = url.Trim();
        if (v.Length == 0) { user.AvatarUrl = null; return null; }
        if (!v.StartsWith("/uploads/", StringComparison.Ordinal) || v.Contains("..") || v.Length > 300)
            return "Rasm manzili noto'g'ri";
        user.AvatarUrl = v;
        return null;
    }
}
