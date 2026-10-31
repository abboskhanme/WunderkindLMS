using Microsoft.EntityFrameworkCore;
using SchoolLms.Application.Dtos;
using SchoolLms.Domain;
using SchoolLms.Infrastructure.Data;

namespace SchoolLms.Infrastructure.Auth;

/// <summary>
/// Sessiya ochishning YAGONA yo'li: akkaunt holatini tekshiradi, kirishni
/// qayd etadi va JWT beradi.
///
/// <para>
/// <b>Nega alohida klass.</b> Ilgari bu mantiq <c>AuthController</c> ning uchta
/// private metodida turardi. Telegram Mini App (SPEC §6 Faza 3) ham AYNAN shu
/// tokenni berishi kerak — aks holda ikkita "login" bo'lib qolardi va ular
/// albatta bir-biridan uzoqlashardi (masalan biri arxivlangan o'qituvchini
/// bloklab, ikkinchisi bloklamay). Endi ikkala controller ham shu yerni
/// chaqiradi, xatti-harakat esa o'zgarmadi.
/// </para>
/// </summary>
public static class SessionFactory
{
    /// <summary>
    /// Akkaunt bloklanganmi (arxivlangan o'qituvchi/o'quvchi). Bloklangan bo'lsa
    /// token BERILMAYDI — <c>Program.cs</c> dagi token revocation bilan bir xil qoida.
    /// </summary>
    public static async Task<bool> IsBlockedAsync(
        AppDbContext db, AppUser user, CancellationToken ct = default) => user.Role switch
    {
        Roles.Teacher => !await db.Teachers.AnyAsync(t => t.UserId == user.Id && !t.IsArchived, ct),
        Roles.Student => !await db.Students.AnyAsync(s => s.UserId == user.Id && !s.IsArchived, ct),
        _ => false,
    };

    /// <summary>
    /// Ruxsat etilgan bo'limlar: o'qituvchi → <c>Teacher.Permissions</c>; xodim →
    /// <c>AppUser.Permissions</c>; qolganlar → null (cheklov yo'q / kerak emas).
    /// </summary>
    public static async Task<List<string>?> PermissionsAsync(
        AppDbContext db, AppUser user, CancellationToken ct = default) => user.Role switch
    {
        Roles.Teacher => (await db.Teachers.FirstOrDefaultAsync(t => t.UserId == user.Id, ct))?.Permissions,
        Roles.Staff => user.Permissions,
        _ => null,
    };

    /// <summary>
    /// Kirishni qayd etadi va token beradi. Chaqirishdan OLDIN
    /// <see cref="IsBlockedAsync"/> tekshirilgan bo'lishi kerak.
    ///
    /// <para>
    /// Rahbariyatning (admin, superadmin) dastlabki OCHIQ paroli shu yerda o'chiriladi —
    /// Telegram orqali kirganda ham. O'quvchi va xodimlarniki ko'rinib qoladi
    /// (<see cref="AccountExtensions.PasswordStaysVisible"/>).
    /// </para>
    /// </summary>
    /// <param name="telegramUserId">The Telegram account a Mini App session comes from (see <see cref="JwtTokenService.TelegramClaim"/>).</param>
    public static async Task<LoginResponse> IssueAsync(
        AppDbContext db, JwtTokenService jwt, AppUser user, CancellationToken ct = default, long? telegramUserId = null)
    {
        var now = AppClock.Now.ToString("yyyy-MM-ddTHH:mm:ss");
        if (string.IsNullOrEmpty(user.FirstLoginAt)) user.FirstLoginAt = now;
        user.LastLoginAt = now;
        // The admin-set password stays visible to the school after sign-in — pupils since 2026-10-08,
        // staff too since 2026-10-30 (client: "hodimlar va o'quvchilar login paroli kirganda doim ko'rinib
        // tursin"). It goes only when the person sets their own (`SetOwnPassword`) or is blocked.
        // Leadership (admin, superadmin) is the exception: theirs goes at sign-in (2026-10-31).
        if (!user.PasswordStaysVisible()) user.InitialPassword = null;
        await db.SaveChangesAsync(ct);

        return new LoginResponse(
            jwt.CreateToken(user, telegramUserId),
            new UserDto(user.Id, user.FullName, user.Role, user.Email, user.AvatarUrl,
                await PermissionsAsync(db, user, ct)));
    }
}
