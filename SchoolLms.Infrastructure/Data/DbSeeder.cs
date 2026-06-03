using SchoolLms.Infrastructure.Auth;
using SchoolLms.Domain;
using SchoolLms.Domain.Platform;

namespace SchoolLms.Infrastructure.Data;

/// <summary>
/// Boot'da bo'sh shared-bazaga bitta default Control Plane egasini (loyiha boshlig'i) yuklaydi.
/// Maktablar (tenant) va ularning superadminlari ProvisioningService orqali yaratiladi.
/// </summary>
public static class DbSeeder
{
    /// <summary>
    /// Default platform owner (agar hech biri bo'lmasa) yaratadi. Parol koddan EMAS — chaqiruvchi
    /// bergan <paramref name="configuredPassword"/> (env/config) dan olinadi; berilmasa kuchli
    /// tasodifiy parol generatsiya qilinadi. Yangi yaratilgan bo'lsa — ishlatilgan ochiq parolni
    /// qaytaradi (operator bir marta log'dan oladi); allaqachon mavjud bo'lsa null.
    /// </summary>
    public static string? SeedPlatformOwner(AppDbContext db, string? configuredPassword = null)
    {
        if (db.Owners.Any()) return null;

        var password = string.IsNullOrWhiteSpace(configuredPassword)
            ? AccountFactory.GeneratePassword(16)
            : configuredPassword.Trim();

        db.Owners.Add(new PlatformOwner
        {
            FullName = "Loyiha boshlig'i",
            Email = "owner@schoollms.uz",
            PasswordHash = PasswordHasher.Hash(password),
        });
        db.SaveChanges();
        return password;
    }

    /// <summary>Joriy o'quv yili: sentabrdan boshlab yangi yil.</summary>
    public static string DefaultYear()
    {
        var now = AppClock.Now;
        var start = now.Month >= 9 ? now.Year : now.Year - 1;
        return $"{start}/{start + 1}";
    }
}
