namespace SchoolLms.Server.Models;

/// <summary>
/// Tizim rollari (AppUser.Role qiymatlari). Yangi rol qo'shilsa shu joyga qo'shiladi.
///
/// <para>
/// <see cref="SuperAdmin"/> — tizim egasi. Admin'ga teng huquqlar ortida QO'SHIMCHA imtiyozlar:
/// guruhlash kabi o'quv yili boshida bloklanadigan amallarni istalgan vaqtda o'zgartira oladi
/// (kelajakda boshqa "lock"lar ham shu rolga override bo'ladi).
/// </para>
/// <para>
/// <see cref="Admin"/> — oddiy administrator. Hamma admin endpoint'lardan foydalanadi, lekin
/// muzlatilgan ma'lumotlarni (masalan, o'quv yili boshlangach guruhlarni) o'zgartira olmaydi.
/// </para>
/// </summary>
public static class Roles
{
    public const string SuperAdmin = "superadmin";
    public const string Admin = "admin";
    public const string Teacher = "teacher";
    public const string Student = "student";
    /// <summary>O'qituvchi bo'lmagan xodim (kassir, administrator, ...). Admin paneliga
    /// kiradi, lekin faqat <see cref="AppUser.Permissions"/> dagi bo'limlarni ko'radi.</summary>
    public const string Staff = "staff";

    /// <summary>
    /// Admin endpoint'larida ishlatish uchun: ikkala rol ham ruxsat etiladi.
    /// <c>[Authorize(Roles = Roles.AdminOrSuper)]</c> ko'rinishida foydalaniladi.
    /// </summary>
    public const string AdminOrSuper = Admin + "," + SuperAdmin;
}
