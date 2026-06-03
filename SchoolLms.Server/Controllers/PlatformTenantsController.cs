using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;
using SchoolLms.Domain.Platform;
using SchoolLms.Infrastructure.Auth;
using SchoolLms.Infrastructure.Data;
using SchoolLms.Infrastructure.Tenancy;

namespace SchoolLms.Server.Controllers;

public record TenantDto(string Id, string Name, string Slug, string Status, string SuperAdminEmail, string CreatedAt,
    List<string> EnabledModules, string? SubscriptionStartsAt, string? SubscriptionEndsAt, decimal SubscriptionPrice);
public record CreateTenantRequest(string Name, string Slug, string SuperAdminFullName, string SuperAdminEmail, string SuperAdminPassword,
    List<string>? EnabledModules = null, DateTime? SubscriptionStartsAt = null, DateTime? SubscriptionEndsAt = null,
    decimal? SubscriptionPrice = null);
public record UpdateTenantStatusRequest(string Status);
/// <summary>Maktab obunasini tahrirlash: bo'limlar, narx va muddat (boshlanish/tugash sanasi; null = chegarasiz).</summary>
public record UpdateSubscriptionRequest(List<string>? EnabledModules = null, decimal? SubscriptionPrice = null,
    DateTime? SubscriptionStartsAt = null, DateTime? SubscriptionEndsAt = null);
public record ModuleDto(string Key, string Label);
/// <summary>Maktab ma'lumotlarini tahrirlash. Email/parol berilsa maktab superadmini ham yangilanadi.</summary>
public record UpdateTenantRequest(string? Name, string? SuperAdminEmail, string? SuperAdminPassword);
public record PlatformDashboardDto(int Total, int Active, int Provisioning, int Suspended);
/// <summary>Maktab ichidagi statistika (shared-bazadan TenantId bo'yicha sanaladi).</summary>
public record TenantStatsDto(int Teachers, int Staff, int Students, int Classes, int AppActivated, int AppDevices);

/// <summary>
/// Control Plane — maktablar (tenant) reestri. Faqat loyiha boshlig'i (platformowner).
/// Shared-baza: Tenants/Owners filtrlanmaydi; maktab ichidagi qatorlar IgnoreQueryFilters + TenantId
/// bilan o'qiladi (platform so'rovida joriy tenant yo'q).
/// </summary>
[ApiController]
[Route("api/platform/tenants")]
[Authorize(Roles = Roles.PlatformOwner)]
public class PlatformTenantsController(AppDbContext db, ProvisioningService provisioning) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<TenantDto>>> List()
    {
        var items = await db.Tenants.AsNoTracking().OrderByDescending(t => t.CreatedAt).ToListAsync();
        return items.Select(Map).ToList();
    }

    [HttpGet("dashboard")]
    public async Task<ActionResult<PlatformDashboardDto>> Dashboard()
    {
        var all = await db.Tenants.AsNoTracking().Select(t => t.Status).ToListAsync();
        return new PlatformDashboardDto(
            all.Count,
            all.Count(s => s == TenantStatus.Active),
            all.Count(s => s == TenantStatus.Provisioning),
            all.Count(s => s == TenantStatus.Suspended));
    }

    [HttpPost]
    public async Task<ActionResult<TenantDto>> Create(CreateTenantRequest req)
    {
        try
        {
            var tenant = await provisioning.CreateAsync(
                req.Name, req.Slug, req.SuperAdminEmail, req.SuperAdminPassword, req.SuperAdminFullName,
                req.EnabledModules, DayStart(req.SubscriptionStartsAt), DayEnd(req.SubscriptionEndsAt),
                req.SubscriptionPrice ?? 0,
                HttpContext.RequestAborted);
            return Map(tenant);
        }
        catch (ProvisioningException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPatch("{id}")]
    public async Task<ActionResult<TenantDto>> UpdateStatus(string id, UpdateTenantStatusRequest req)
    {
        var tenant = await db.Tenants.FindAsync(id);
        if (tenant is null) return NotFound();

        if (req.Status is not (TenantStatus.Active or TenantStatus.Suspended))
            return BadRequest(new { message = "Holat faqat 'active' yoki 'suspended' bo'lishi mumkin" });

        tenant.Status = req.Status;
        await db.SaveChangesAsync();
        return Map(tenant);
    }

    /// <summary>
    /// Maktab ma'lumotlarini tahrirlaydi: nomi va ixtiyoriy ravishda superadmin login/parolini
    /// (shared-bazadagi o'sha maktabning superadmin AppUser'i). Subdomen (slug) O'ZGARTIRILMAYDI.
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult<TenantDto>> Update(string id, UpdateTenantRequest req)
    {
        var tenant = await db.Tenants.FindAsync(id);
        if (tenant is null) return NotFound();

        if (!string.IsNullOrWhiteSpace(req.Name))
            tenant.Name = req.Name.Trim();

        var newEmail = req.SuperAdminEmail?.Trim();
        var changeEmail = !string.IsNullOrWhiteSpace(newEmail) && newEmail != tenant.SuperAdminEmail;
        var changePass = !string.IsNullOrEmpty(req.SuperAdminPassword);

        if (changePass && req.SuperAdminPassword!.Length < 8)
            return BadRequest(new { message = "Parol kamida 8 belgidan iborat bo'lsin" });

        if (changeEmail || changePass)
        {
            // Platform so'rovida joriy tenant yo'q → filtrlarni chetlab, TenantId bo'yicha topamiz.
            var su = await db.Users.IgnoreQueryFilters()
                .FirstOrDefaultAsync(u => EF.Property<string>(u, "TenantId") == id && u.Role == Roles.SuperAdmin);
            if (su is null) return BadRequest(new { message = "Maktab superadmini topilmadi" });

            if (changeEmail)
            {
                // Login BUTUN baza bo'ylab unikal (maktab kodisiz login uchun).
                var taken = await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == newEmail && u.Id != su.Id);
                if (taken) return BadRequest(new { message = "Bu login allaqachon band (boshqa maktabda ham)" });
                su.Email = newEmail!;
                tenant.SuperAdminEmail = newEmail!;
            }
            if (changePass)
                su.PasswordHash = PasswordHasher.Hash(req.SuperAdminPassword!);
        }

        await db.SaveChangesAsync();
        return Map(tenant);
    }

    /// <summary>Mavjud admin bo'limlari (modullar) katalogi — obuna belgilash uchun.</summary>
    [HttpGet("modules")]
    public ActionResult<List<ModuleDto>> Modules() =>
        AdminModules.All.Select(m => new ModuleDto(m.Key, m.Label)).ToList();

    /// <summary>
    /// Maktab obunasini tahrirlaydi: ochiq bo'limlar, narx va muddat (boshlanish/tugash sanasi).
    /// Modal joriy holatni to'liq yuboradi — sanalar har doim qo'llanadi (bo'sh sana = chegarasiz/muddatsiz).
    /// </summary>
    [HttpPut("{id}/subscription")]
    public async Task<ActionResult<TenantDto>> UpdateSubscription(string id, UpdateSubscriptionRequest req)
    {
        var tenant = await db.Tenants.FindAsync(id);
        if (tenant is null) return NotFound();

        if (req.EnabledModules is not null)
            tenant.EnabledModules = AdminModules.Sanitize(req.EnabledModules);
        if (req.SubscriptionPrice is decimal price)
            tenant.SubscriptionPrice = price < 0 ? 0 : price;

        tenant.SubscriptionStartsAt = DayStart(req.SubscriptionStartsAt);
        tenant.SubscriptionEndsAt = DayEnd(req.SubscriptionEndsAt);

        await db.SaveChangesAsync();
        return Map(tenant);
    }

    /// <summary>Sanani kun boshiga (00:00) keltiradi — obuna boshlanishi shu kundan.</summary>
    private static DateTime? DayStart(DateTime? d) => d?.Date;
    /// <summary>Sanani kun oxiriga keltiradi — tugash kuni to'liq qamrab olinishi uchun (inclusive).</summary>
    private static DateTime? DayEnd(DateTime? d) => d?.Date.AddDays(1).AddTicks(-1);

    /// <summary>Maktab ichidagi statistika — shared-bazadan TenantId bo'yicha.</summary>
    [HttpGet("{id}/stats")]
    public async Task<ActionResult<TenantStatsDto>> Stats(string id)
    {
        var tenant = await db.Tenants.FindAsync(id);
        if (tenant is null) return NotFound();

        return new TenantStatsDto(
            Teachers: await db.Teachers.IgnoreQueryFilters()
                .CountAsync(t => EF.Property<string>(t, "TenantId") == id && !t.IsArchived),
            Staff: await db.Users.IgnoreQueryFilters()
                .CountAsync(u => EF.Property<string>(u, "TenantId") == id && u.Role == Roles.Staff),
            Students: await db.Students.IgnoreQueryFilters()
                .CountAsync(s => EF.Property<string>(s, "TenantId") == id && !s.IsArchived),
            Classes: await db.Classes.IgnoreQueryFilters()
                .CountAsync(c => EF.Property<string>(c, "TenantId") == id),
            AppActivated: await db.Users.IgnoreQueryFilters()
                .CountAsync(u => EF.Property<string>(u, "TenantId") == id && u.FirstLoginAt != null),
            AppDevices: await db.DeviceTokens.IgnoreQueryFilters()
                .Where(d => EF.Property<string>(d, "TenantId") == id)
                .Select(d => d.UserId).Distinct().CountAsync());
    }

    private static TenantDto Map(Tenant t) =>
        new(t.Id, t.Name, t.Slug, t.Status, t.SuperAdminEmail,
            t.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            t.EnabledModules,
            t.SubscriptionStartsAt?.ToString("yyyy-MM-dd"),
            t.SubscriptionEndsAt?.ToString("yyyy-MM-dd"),
            t.SubscriptionPrice);
}
