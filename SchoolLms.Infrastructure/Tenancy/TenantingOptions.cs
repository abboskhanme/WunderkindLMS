namespace SchoolLms.Infrastructure.Tenancy;

/// <summary>Multi-tenant sozlamalari (appsettings "Tenancy" bo'limi).</summary>
public class TenantingOptions
{
    /// <summary>Asosiy domen — subdomenni ajratish uchun (masalan "intellectschool.uz" yoki dev'da "lvh.me").
    /// Vergul bilan bir nechta berilishi mumkin.</summary>
    public string RootDomain { get; set; } = "lvh.me";

    /// <summary>Asosiy domen sifatida qaraladigan subdomenlar (tenant emas → Control Plane).</summary>
    public string[] PlatformSubdomains { get; set; } = ["www", "app", "admin"];

    public string[] Roots() => RootDomain
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
