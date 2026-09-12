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
    /// Dastlabki OCHIQ parol shu yerda o'chiriladi — Telegram orqali kirganda ham.
    /// Foydalanuvchi tizimga kirdi, ya'ni superadmin ekranida uning ochiq parolini
    /// ushlab turishning ma'nosi qolmadi (parolni tiklash yo'li baribir bor).
    /// </para>
    /// </summary>
    public static async Task<LoginResponse> IssueAsync(
        AppDbContext db, JwtTokenService jwt, AppUser user, CancellationToken ct = default)
    {
        var now = AppClock.Now.ToString("yyyy-MM-ddTHH:mm:ss");
        if (string.IsNullOrEmpty(user.FirstLoginAt)) user.FirstLoginAt = now;
        user.LastLoginAt = now;
        user.InitialPassword = null;
        await db.SaveChangesAsync(ct);

        return new LoginResponse(
            jwt.CreateToken(user),
            new UserDto(user.Id, user.FullName, user.Role, user.Email, user.AvatarUrl,
                await PermissionsAsync(db, user, ct)));
    }
}
