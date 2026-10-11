using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;

namespace SchoolLms.Infrastructure.Data;

/// <summary>
/// <c>absence_notices</c> — kelmaganlik haqida ota-onaga yuborilgan xabarlar (<see cref="AbsenceNotice"/>)
/// va <c>broadcast_recipients</c> — e'lonning har bir oluvchisi (<see cref="BroadcastRecipient"/>).
/// </summary>
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

        // E'lon oluvchilari (2026-10-11) — e'lon o'chsa birga o'chadi.
        b.Entity<BroadcastRecipient>(e =>
        {
            e.ToTable("broadcast_recipients");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.HasOne<Broadcast>().WithMany().HasForeignKey(x => x.BroadcastId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.BroadcastId).HasDatabaseName("ix_broadcast_recipients_broadcast");
            e.ToTable(t => t.HasCheckConstraint("ck_broadcast_recipients_status", "status in ('sent','failed','no_telegram')"));
        });
    }
}
