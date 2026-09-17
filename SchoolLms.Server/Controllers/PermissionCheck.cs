using System.Security.Claims;
using SchoolLms.Domain;

namespace SchoolLms.Server.Controllers;

/// <summary>
/// Bitta amal ichida ruxsatni QAYTA so'rash.
///
/// <para>
/// <see cref="AdminPermAttribute"/> sinf darajasida ishlaydi va xodimning (staff)
/// HAR QANDAY o'qish so'rovini ataylab ochiq qoldiradi — bo'limlararo o'qish
/// buzilmasligi uchun. Ya'ni `messages` ruxsatiga ega xodim `GET` bilan istalgan
/// bo'lim ma'lumotini o'qiy oladi.
/// </para>
/// <para>
/// PUL uchun bu yetarli emas: qoldiq, hisob-faktura, to'lov va chiqim bitta
/// darvozadan o'tmasligi kerak. Shunday joyda amal ruxsatni O'ZI so'raydi va
/// yo qatorni bermaydi, yo pul ustunini bo'sh qaytaradi.
/// </para>
/// <para>
/// Bu <c>X-2</c> (amal darajasidagi ruxsat kalitlari) ning o'rnini BOSMAYDI —
/// u kelguncha faqat pul o'qiladigan joylarni yopadi.
/// </para>
/// </summary>
public static class PermissionCheck
{
    /// <summary>Moliya ma'lumotini o'qish ruxsati.</summary>
    public const string Finance = "finance";

    /// <summary>
    /// Shu foydalanuvchida <paramref name="perm"/> ruxsati bormi.
    /// admin va superadmin — har doim bor (ular ruxsat ro'yxati bilan cheklanmaydi).
    /// </summary>
    public static bool HasPerm(this ClaimsPrincipal user, string perm) =>
        user.IsInRole(Roles.Admin)
        || user.IsInRole(Roles.SuperAdmin)
        || user.Claims.Any(c => c.Type == AdminPermAttribute.ClaimType && c.Value == perm);
}
