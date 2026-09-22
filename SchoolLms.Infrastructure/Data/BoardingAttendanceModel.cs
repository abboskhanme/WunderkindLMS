using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;

namespace SchoolLms.Infrastructure.Data;

/// <summary>
/// Kechki dars va yotoqxona davomati (<c>BoardingAttendance</c> migratsiyasi). Bir kun ×
/// bir sessiya × bir o'quvchi = bitta qator.
/// </summary>
internal static class BoardingAttendanceModel
{
    public static void Apply(ModelBuilder b)
    {
        b.Entity<BoardingAttendance>(e =>
        {
            e.ToTable("boarding_attendance", t =>
            {
                t.HasCheckConstraint("ck_boarding_attendance_session", "session in ('evening','dorm')");
                t.HasCheckConstraint("ck_boarding_attendance_status", "status in ('present','absent','excused')");
            });
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.Date, x.Session, x.StudentId }).IsUnique();
            e.HasIndex(x => x.StudentId);
            e.Property(x => x.Session).HasMaxLength(16);
            e.Property(x => x.Status).HasMaxLength(16);
            // O'quvchi o'chirilsa — uning kechki/yotoqxona qatorlari ham (tarix — arxiv orqali saqlanadi).
            e.HasOne<Student>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
