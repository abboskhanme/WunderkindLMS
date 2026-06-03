using SchoolLms.Domain;

namespace SchoolLms.Infrastructure.Auth;

public static class AccountExtensions
{
    /// <summary>
    /// Admin tomonidan parol o'rnatish/tiklash: hash + dastlabki ochiq parolni saqlaydi
    /// (superadmin ko'rishi/eksport uchun, birinchi login'gacha).
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

    /// <summary>Login bloklash (arxiv): parolni bo'shatadi va ko'rsatiladigan parolni o'chiradi.</summary>
    public static void BlockLogin(this AppUser user)
    {
        user.PasswordHash = "";
        user.InitialPassword = null;
    }
}
