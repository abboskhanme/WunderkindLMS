using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;

namespace SchoolLms.Infrastructure.Data;

/// <summary>
/// Bildirishnoma holati (<c>NotificationStates</c> migratsiyasi) — foydalanuvchi × bildirishnoma
/// id'si bo'yicha bitta qator. Vaqt ustunlari <c>user_settings.notifications_read_at</c> bilan
/// bir xil turda (mahalliy vaqt, <c>timestamp without time zone</c>).
/// </summary>
internal static class NotificationStateModel
{
    public static void Apply(ModelBuilder b)
    {
        b.Entity<NotificationState>(e =>
        {
            e.ToTable("notification_states");
            e.HasKey(x => new { x.UserId, x.NotificationId });
            e.Property(x => x.NotificationId).HasMaxLength(200);
            e.Property(x => x.ReadAt).HasColumnType("timestamp without time zone");
            e.Property(x => x.DismissedAt).HasColumnType("timestamp without time zone");

            // Foydalanuvchi o'chirilsa — uning holatlari ham.
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
