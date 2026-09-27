using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;

namespace SchoolLms.Infrastructure.Data;

/// <summary>
/// Dars tahlili (docs/modules/lesson-analysis.md §3, migration <c>LessonAnalysis</c>):
/// checklist sections and criteria, observations and their per-criterion answers.
/// Not financial — full CRUD for <c>app_rw</c> (default privileges, no guards file).
/// </summary>
internal static class LessonAnalysisModel
{
    public static void Apply(ModelBuilder b)
    {
        b.Entity<LessonObservationSection>(e =>
        {
            e.ToTable("lesson_observation_sections");
            e.HasKey(x => x.Id);
            e.Property(x => x.Color).HasMaxLength(20);
            e.HasIndex(x => x.OrderIndex);
            // The one-time import re-runs idempotently by this key.
            e.HasIndex(x => x.LegacyId).IsUnique().HasFilter("legacy_id is not null");
        });

        b.Entity<LessonObservationCriterion>(e =>
        {
            e.ToTable("lesson_observation_criteria");
            e.HasKey(x => x.Id);
            // RESTRICT: a section with criteria is never deleted (criteria are deactivated, not removed).
            e.HasOne(x => x.Section).WithMany(s => s.Criteria)
                .HasForeignKey(x => x.SectionId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.SectionId, x.OrderIndex });
            e.HasIndex(x => x.LegacyId).IsUnique().HasFilter("legacy_id is not null");
        });

        b.Entity<LessonObservation>(e =>
        {
            e.ToTable("lesson_observations", t =>
                t.HasCheckConstraint("ck_lesson_observations_status", "status in ('draft','completed')"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasMaxLength(16);
            e.Property(x => x.ScorePercent).HasPrecision(5, 2);
            e.Property(x => x.LessonDate).HasColumnType("date");

            // RESTRICT on both: an observation is a record about a teacher, written by an
            // observer — deleting either account must not silently erase that history.
            e.HasOne<Teacher>().WithMany().HasForeignKey(x => x.TeacherId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.ObserverUserId).OnDelete(DeleteBehavior.Restrict);

            e.HasMany(x => x.Details).WithOne()
                .HasForeignKey(d => d.ObservationId).OnDelete(DeleteBehavior.Cascade);

            // Rating board / teacher card: completed rows of one teacher by date.
            e.HasIndex(x => new { x.TeacherId, x.Status, x.LessonDate });
            // List page: newest first, optionally by status.
            e.HasIndex(x => new { x.Status, x.LessonDate });
            e.HasIndex(x => x.ObserverUserId);
            e.HasIndex(x => x.LegacyId).IsUnique().HasFilter("legacy_id is not null");
        });

        b.Entity<LessonObservationDetail>(e =>
        {
            e.ToTable("lesson_observation_details");
            e.HasKey(x => x.Id);
            e.HasOne<LessonObservationCriterion>().WithMany()
                .HasForeignKey(x => x.CriterionId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.ObservationId, x.CriterionId }).IsUnique();
            e.HasIndex(x => x.CriterionId);
        });
    }
}
