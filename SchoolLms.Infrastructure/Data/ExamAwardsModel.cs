using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;

namespace SchoolLms.Infrastructure.Data;

/// <summary>Thank-you / warning messages after a test (<c>ExamAwards</c> migration, docs/modules/exam-awards.md).</summary>
internal static class ExamAwardsModel
{
    public static void Apply(ModelBuilder b)
    {
        b.Entity<ExamAwardNotice>(e =>
        {
            e.ToTable("exam_award_notices", t =>
            {
                t.HasCheckConstraint("ck_exam_award_notices_kind", "kind in ('top','bottom')");
                t.HasCheckConstraint("ck_exam_award_notices_rank", "rank >= 1");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Kind).HasMaxLength(8);
            e.Property(x => x.Percent).HasPrecision(5, 2);
            // Once per exam, pupil and kind.
            e.HasIndex(x => new { x.ExamId, x.StudentId, x.Kind }).IsUnique()
                .HasDatabaseName("ux_exam_award_notices_once");
            e.HasOne<Exam>().WithMany().HasForeignKey(x => x.ExamId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Student>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
