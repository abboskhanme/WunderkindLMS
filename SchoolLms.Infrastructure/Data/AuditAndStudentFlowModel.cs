using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;

namespace SchoolLms.Infrastructure.Data;

/// <summary>
/// <c>AuditStudentFlowAndShop</c> migration: the audit screen's two indexes
/// (docs/modules/audit-log.md) and the <c>student_events</c> table (docs/modules/student-flow.md).
/// </summary>
internal static class AuditAndStudentFlowModel
{
    public static void Apply(ModelBuilder b)
    {
        // The audit screen filters by person or by kind of record and always reads newest first.
        b.Entity<AuditLog>().HasIndex(a => new { a.ActorId, a.Timestamp }).HasDatabaseName("ix_audit_logs_actor_id_timestamp");
        b.Entity<AuditLog>().HasIndex(a => new { a.EntityType, a.Timestamp }).HasDatabaseName("ix_audit_logs_entity_type_timestamp");

        b.Entity<StudentEvent>(e =>
        {
            e.ToTable("student_events", t =>
            {
                t.HasCheckConstraint("ck_student_events_kind", "kind in ('enrolled','left','returned')");
                t.HasCheckConstraint("ck_student_events_source",
                    "source in ('manual','bulk','class_archive','graduation','delete','import','lead','backfill')");
                // A reason belongs to a departure only.
                t.HasCheckConstraint("ck_student_events_reason", "kind = 'left' or (reason_id is null and reason_text is null)");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.StudentId).HasMaxLength(64);
            e.Property(x => x.StudentName).HasMaxLength(300);
            e.Property(x => x.Kind).HasMaxLength(16);
            e.Property(x => x.ClassId).HasMaxLength(64);
            e.Property(x => x.ClassName).HasMaxLength(100);
            e.Property(x => x.ReasonText).HasMaxLength(500);
            e.Property(x => x.Source).HasMaxLength(16);
            e.Property(x => x.ActorId).HasMaxLength(64);
            e.Property(x => x.ActorName).HasMaxLength(300);

            e.HasIndex(x => new { x.Kind, x.OnDate }).HasDatabaseName("ix_student_events_kind_on_date");
            e.HasIndex(x => new { x.StudentId, x.OnDate }).HasDatabaseName("ix_student_events_student_id_on_date");

            // RESTRICT, like `students.archive_reason_id`: a reason that explains a departure cannot
            // be deleted from the catalog (ArchiveReasonsController counts these rows too); it is
            // retired with is_active = false instead.
            e.HasOne<StudentArchiveReason>().WithMany().HasForeignKey(x => x.ReasonId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
