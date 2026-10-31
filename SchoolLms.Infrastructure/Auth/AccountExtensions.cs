using SchoolLms.Domain;

namespace SchoolLms.Infrastructure.Auth;

public static class AccountExtensions
{
    /// <summary>
    /// Admin tomonidan parol o'rnatish/tiklash: hash + dastlabki ochiq parolni saqlaydi
    /// (admin ko'rishi/eksport uchun — foydalanuvchi o'zi o'zgartirmaguncha).
    /// </summary>
    public static void SetInitialPassword(this AppUser user, string plain)
    {
        user.PasswordHash = PasswordHasher.Hash(plain);
        user.InitialPassword = plain;
    }

    /// <summary>Foydalanuvchi o'zi o'zgartirgan parol (yoki login qilingach): faqat hash, ochiq parol tozalanadi.</summary>
    public static void SetOwnPassword(this AppUser user, string plain)
    {
        user.PasswordHash = PasswordHasher.Hash(plain);
        user.InitialPassword = null;
    }

    /// <summary>
    /// Admin bergan parol kirgandan keyin ham ko'rinib turadimi: o'quvchi va xodimlarda — ha,
    /// rahbariyatda (admin, superadmin) — yo'q (mijoz, 2026-10-31: "rahbariyatdan tashqari hodimlar va
    /// o'quvchilar uchun ishlasin. admin va shunga o'xshaganlarniki ko'rinmasin").
    /// </summary>
    public static bool PasswordStaysVisible(this AppUser user) =>
        user.Role is not (Roles.Admin or Roles.SuperAdmin);

    /// <summary>Login bloklash (arxiv): parolni bo'shatadi va ko'rsatiladigan parolni o'chiradi.</summary>
    public static void BlockLogin(this AppUser user)
    {
        user.PasswordHash = "";
        user.InitialPassword = null;
    }
}
