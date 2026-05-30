using SchoolLms.Server.Auth;
using SchoolLms.Server.Models;

namespace SchoolLms.Server.Data;

/// <summary>
/// Bo'sh bazaga FAQAT bitta tizim egasi (superadmin) akkauntini yuklaydi.
/// Qolgan barcha ma'lumotlar (o'quvchi, o'qituvchi, sinf, fan, lead, moliya,
/// jadval, jurnal, sozlamalar) tizim ichidan qo'lda kiritiladi.
/// </summary>
public static class DbSeeder
{
    public static void Seed(AppDbContext db)
    {
        if (!db.Users.Any())
        {
            db.Users.Add(new AppUser
            {
                Id = "a1",
                FullName = "Administrator",
                Role = Roles.SuperAdmin,
                Email = "admin@maktab.uz",
                PasswordHash = PasswordHasher.Hash("admin123"),
            });
        }
        else if (!db.Users.Any(u => u.Role == Roles.SuperAdmin))
        {
            // Eski bazada superadmin yo'q — seed admin (Id="a1") ni yoki shunday topilmasa
            // birinchi "admin" rolli foydalanuvchini superadmin'ga ko'taramiz. Shu bilan tizim
            // egasi tiklanadi va u guruhlash kabi muzlatilgan amallarni override qila oladi.
            var promote = db.Users.FirstOrDefault(u => u.Id == "a1")
                          ?? db.Users.FirstOrDefault(u => u.Role == Roles.Admin);
            if (promote is not null) promote.Role = Roles.SuperAdmin;
        }

        // Joriy o'quv yili (singleton) — mavjud bazaga ham qo'shiladi (idempotent).
        if (!db.SchoolMeta.Any())
        {
            db.SchoolMeta.Add(new SchoolMeta { Id = "current", CurrentYear = DefaultYear() });
        }

        db.SaveChanges();
    }

    /// <summary>Joriy sanaga qarab o'quv yili: sentabrdan boshlab yangi yil.</summary>
    private static string DefaultYear()
    {
        var now = DateTime.Now;
        var start = now.Month >= 9 ? now.Year : now.Year - 1;
        return $"{start}/{start + 1}";
    }
}
