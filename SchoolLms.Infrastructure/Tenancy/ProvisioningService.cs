using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;
using SchoolLms.Domain.Platform;
using SchoolLms.Infrastructure.Auth;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Infrastructure.Tenancy;

/// <summary>Yangi maktab ochish natijasi xatosi (foydalanuvchiga ko'rsatiladigan xabar bilan).</summary>
public class ProvisioningException(string message) : Exception(message);

/// <summary>
/// Yangi maktab (tenant) ochadi: shared-bazaga Tenant yozuvini, uning superadmin AppUser'ini va
/// SchoolMeta yozuvini qo'shadi (har biri TenantId bilan belgilanadi). Alohida DB yaratilmaydi.
/// </summary>
public class ProvisioningService(AppDbContext db)
{
    public async Task<Tenant> CreateAsync(
        string name, string rawSlug,
        string superAdminEmail, string superAdminPassword, string superAdminFullName,
        IEnumerable<string>? enabledModules = null,
        DateTime? subscriptionStartsAt = null,
        DateTime? subscriptionEndsAt = null,
        decimal subscriptionPrice = 0,
        CancellationToken ct = default)
    {
        var slug = Slug.Normalize(rawSlug);
        if (!Slug.IsValid(slug))
            throw new ProvisioningException("Subdomen noto'g'ri yoki band (faqat a-z, 0-9, '-'; 2-32 belgi).");
        if (string.IsNullOrWhiteSpace(name))
            throw new ProvisioningException("Maktab nomi kiritilmadi.");
        if (string.IsNullOrWhiteSpace(superAdminEmail) || string.IsNullOrWhiteSpace(superAdminPassword))
            throw new ProvisioningException("Superadmin login va parol kiritilishi shart.");
        if (superAdminPassword.Length < 8)
            throw new ProvisioningException("Superadmin paroli kamida 8 belgidan iborat bo'lsin.");

        if (await db.Tenants.AnyAsync(t => t.Slug == slug, ct))
            throw new ProvisioningException($"'{slug}' subdomeni allaqachon band.");

        // Login BUTUN baza bo'ylab unikal bo'lishi shart (maktab kodisiz login uchun).
        var email = superAdminEmail.Trim();
        if (await db.Users.IgnoreQueryFilters().AnyAsync(u => u.Email == email, ct))
            throw new ProvisioningException($"'{email}' logini allaqachon band (boshqa maktabda).");

        var tenant = new Tenant
        {
            Name = name.Trim(),
            Slug = slug,
            Status = TenantStatus.Active,
            SuperAdminEmail = superAdminEmail.Trim(),
            EnabledModules = AdminModules.Sanitize(enabledModules),
            SubscriptionStartsAt = subscriptionStartsAt,
            SubscriptionEndsAt = subscriptionEndsAt,
            SubscriptionPrice = subscriptionPrice < 0 ? 0 : subscriptionPrice,
        };
        db.Tenants.Add(tenant);

        var superAdmin = new AppUser
        {
            FullName = string.IsNullOrWhiteSpace(superAdminFullName) ? "Administrator" : superAdminFullName.Trim(),
            Role = Roles.SuperAdmin,
            Email = superAdminEmail.Trim(),
            PasswordHash = PasswordHasher.Hash(superAdminPassword),
        };
        db.Users.Add(superAdmin);

        var meta = new SchoolMeta { CurrentYear = DbSeeder.DefaultYear() };
        db.SchoolMeta.Add(meta);

        // Bu so'rov platform kontekstida (TenantId null) — shuning uchun yangi maktab qatorlariga
        // TenantId'ni QO'LDA qo'yamiz (SaveChanges bo'shligini ko'rib o'zgartirmaydi).
        db.Entry(superAdmin).Property("TenantId").CurrentValue = tenant.Id;
        db.Entry(meta).Property("TenantId").CurrentValue = tenant.Id;

        await db.SaveChangesAsync(ct);
        return tenant;
    }
}
