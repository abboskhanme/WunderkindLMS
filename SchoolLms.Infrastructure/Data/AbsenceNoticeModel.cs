using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;

namespace SchoolLms.Infrastructure.Data;

/// <summary><c>absence_notices</c> — kelmaganlik haqida ota-onaga yuborilgan xabarlar (<see cref="AbsenceNotice"/>).</summary>
internal static class AbsenceNoticeModel
{
    public static void Apply(ModelBuilder b)
    {
        b.Entity<AbsenceNotice>(e =>
        {
            e.ToTable("absence_notices");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.HasOne<Student>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
            // Bitta o'quvchiga bir kunda bitta xabar — takror bosish bazada ham to'xtaydi.
            e.HasIndex(x => new { x.StudentId, x.Date }).IsUnique().HasDatabaseName("ux_absence_notices_student_date");
            e.HasIndex(x => x.Date).HasDatabaseName("ix_absence_notices_date");
        });
    }
}
