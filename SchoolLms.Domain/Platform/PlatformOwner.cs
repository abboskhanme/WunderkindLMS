namespace SchoolLms.Domain.Platform;

/// <summary>
/// Loyiha boshlig'i ("createadmin") akkaunti — Control Plane'ga (asosiy domen) kiradi.
/// Maktab foydalanuvchilaridan (AppUser) butunlay ALOHIDA bazada (Platform DB) saqlanadi.
/// Bu rol faqat maktablarni (tenant) yaratish/boshqarish uchun; hech bir maktab DB'siga
/// to'g'ridan-to'g'ri kirmaydi.
/// </summary>
public class PlatformOwner
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string FullName { get; set; } = string.Empty;
    /// <summary>Login (email/username).</summary>
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string? LastLoginAt { get; set; }
    public DateTime CreatedAt { get; set; } = AppClock.Now;
}
