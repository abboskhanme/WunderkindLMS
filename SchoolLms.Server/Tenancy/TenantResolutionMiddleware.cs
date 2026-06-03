using SchoolLms.Application.Abstractions;
using SchoolLms.Domain;
using SchoolLms.Domain.Platform;
using SchoolLms.Infrastructure.Tenancy;

namespace SchoolLms.Server.Tenancy;

/// <summary>
/// Har so'rovda joriy maktab (tenant)ni aniqlaydi va <see cref="ITenantContext"/>'ni to'ldiradi —
/// shared-DB global query filter shu asosda ishlaydi. Autentifikatsiyadan KEYIN ishlaydi:
///  - kirgan maktab foydalanuvchisi uchun tokendagi <c>tenant</c> claim ASOSIY (ishonchli) manba;
///    so'rovdagi subdomen/X-Tenant unga mos kelmasa — 403 (tenantlararo kirish bloklanadi).
///  - anonim so'rov (login) uchun X-Tenant sarlavhasi yoki Host subdomeni ishlatiladi.
///  - bo'sh / "www","app","admin" → asosiy domen (Control Plane).
/// </summary>
public class TenantResolutionMiddleware(RequestDelegate next, TenantingOptions options)
{
    public async Task Invoke(HttpContext ctx, ITenantContext tenant, ITenantStore store)
    {
        var isApi = IsApiPath(ctx.Request.Path);
        var requestSlug = RequestSlug(ctx);
        var claimSlug = ctx.User.Identity?.IsAuthenticated == true
            ? ctx.User.FindFirst("tenant")?.Value
            : null;

        string? slug;
        if (!string.IsNullOrEmpty(claimSlug))
        {
            // Kirgan maktab foydalanuvchisi — token ASOSIY. So'rovdagi boshqa maktab → rad etiladi.
            if (!string.IsNullOrEmpty(requestSlug) &&
                !requestSlug.Equals(claimSlug, StringComparison.OrdinalIgnoreCase))
            {
                if (isApi) { await Reject(ctx, 403, "Token boshqa maktabga tegishli"); return; }
            }
            slug = claimSlug;
        }
        else
        {
            slug = requestSlug; // anonim (login) yoki platform tokeni
        }

        if (string.IsNullOrEmpty(slug) || IsPlatformLabel(slug))
        {
            tenant.SetPlatform();
            await next(ctx);
            return;
        }

        var info = await store.FindBySlugAsync(slug, ctx.RequestAborted);
        if (info is null)
        {
            if (isApi) { await Reject(ctx, 404, "Maktab topilmadi"); return; }
            tenant.SetPlatform();
        }
        else if (info.Status == TenantStatus.Suspended)
        {
            if (isApi) { await Reject(ctx, 403, "Maktab vaqtincha to'xtatilgan"); return; }
            tenant.SetPlatform();
        }
        else if (info.SubscriptionStartsAt is { } start && AppClock.Now < start)
        {
            // Obuna hali boshlanmagan (boshlanish sanasidan oldin) — bloklanadi.
            if (isApi) { await Reject(ctx, 403, "Maktab obunasi hali boshlanmagan"); return; }
            tenant.SetPlatform();
        }
        else if (info.SubscriptionEndsAt is { } end && AppClock.Now > end)
        {
            // Obuna muddati o'tib ketgan — maktab to'xtatilgan kabi bloklanadi (loyiha boshlig'i uzaytirsin).
            if (isApi) { await Reject(ctx, 403, "Maktab obunasi muddati tugagan"); return; }
            tenant.SetPlatform();
        }
        else
        {
            tenant.SetTenant(info.Id, info.Slug);

            // Modul litsenziyasi: bo'sh ro'yxat = cheklovsiz. Aks holda ochilmagan bo'lim API'si 403.
            // FAQAT izolyatsiyalangan (leaf) admin yo'llari bloklanadi — pastdagi ModuleForPath'ga qarang.
            if (isApi && info.EnabledModules.Count > 0)
            {
                var module = ModuleForPath(ctx.Request.Path);
                if (module is not null && !info.EnabledModules.Contains(module))
                {
                    await Reject(ctx, 403, "Bu bo'lim maktab obunasiga kirmaydi");
                    return;
                }
            }
        }

        await next(ctx);
    }

    /// <summary>
    /// API yo'lini admin moduliga moslaydi (litsenziya tekshiruvi uchun). FAQAT boshqa bo'limga
    /// bog'liq bo'lmagan, izolyatsiyalangan (leaf) yo'llar — noto'g'ri bloklab qo'ymaslik uchun.
    /// O'quvchilar/o'qituvchilar/sinflar/jadval/jurnal/davomat/sozlama kabi ASOSIY/ulashilgan yo'llar
    /// bu yerda yo'q (boshqa bo'limlarga ham kerak); ular faqat frontend nav'da yashiriladi.
    /// </summary>
    private static string? ModuleForPath(PathString path)
    {
        if (path.StartsWithSegments("/api/admin/finance")) return "finance";
        if (path.StartsWithSegments("/api/admin/contracts")) return "contracts";
        if (path.StartsWithSegments("/api/admin/discipline")) return "discipline";
        if (path.StartsWithSegments("/api/admin/teacher-reports")) return "teacherReports";
        if (path.StartsWithSegments("/api/admin/leads") || path.StartsWithSegments("/api/admin/lead-stages")) return "leads";
        if (path.StartsWithSegments("/api/admin/feedback")) return "feedback";
        if (path.StartsWithSegments("/api/admin/academic-year")) return "academicYear";
        if (path.StartsWithSegments("/api/admin/staff")) return "staff";
        return null;
    }

    /// <summary>So'rovdan kelgan slug: X-Tenant sarlavhasi (ustun) yoki Host subdomeni.</summary>
    private string? RequestSlug(HttpContext ctx)
    {
        var header = ctx.Request.Headers["X-Tenant"].ToString();
        if (!string.IsNullOrWhiteSpace(header)) return header.Trim().ToLowerInvariant();

        var host = ctx.Request.Host.Host;
        if (string.IsNullOrEmpty(host) || host == "localhost" || System.Net.IPAddress.TryParse(host, out _))
            return null;

        foreach (var root in options.Roots())
        {
            if (host.Equals(root, StringComparison.OrdinalIgnoreCase)) return null;
            if (host.EndsWith("." + root, StringComparison.OrdinalIgnoreCase))
            {
                var prefix = host[..^(root.Length + 1)];
                return prefix.Contains('.') ? null : prefix.ToLowerInvariant();
            }
        }
        if (host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase))
        {
            var prefix = host[..^".localhost".Length];
            return prefix.Contains('.') ? null : prefix.ToLowerInvariant();
        }
        return null;
    }

    private bool IsPlatformLabel(string slug) =>
        options.PlatformSubdomains.Contains(slug, StringComparer.OrdinalIgnoreCase);

    private static bool IsApiPath(PathString path) =>
        path.StartsWithSegments("/api") || path.StartsWithSegments("/hubs");

    private static async Task Reject(HttpContext ctx, int status, string message)
    {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json";
        await ctx.Response.WriteAsJsonAsync(new { message });
    }
}
