using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;

namespace SchoolLms.Infrastructure.Data;

/// <summary>
/// AI task drafts (docs/modules/hr-ai-tasks.md §4.5, migration <c>HrAiDrafts</c>). Not financial —
/// full CRUD for <c>app_rw</c> (default privileges, no guards file).
/// </summary>
internal static class HrAiModel
{
    public static void Apply(ModelBuilder b)
    {
        b.Entity<HrAiDraft>(e =>
        {
            e.ToTable("hr_ai_drafts", t =>
            {
                t.HasCheckConstraint("ck_hr_ai_draft_source", $"source in ('{HrAiSources.Text}','{HrAiSources.Voice}')");
                t.HasCheckConstraint("ck_hr_ai_draft_status",
                    $"status in ({string.Join(",", HrAiDraftStatuses.All.Select(s => $"'{s}'"))})");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasMaxLength(36);
            e.Property(x => x.UserId).HasMaxLength(36);
            e.Property(x => x.Source).HasMaxLength(8);
            e.Property(x => x.Status).HasMaxLength(12);
            e.Property(x => x.Proposal).HasColumnType("jsonb");
            e.Property(x => x.Result).HasColumnType("jsonb");
            e.Property(x => x.Error).HasMaxLength(500);
            e.HasIndex(x => new { x.UserId, x.CreatedAt }).HasDatabaseName("ix_hr_ai_drafts_user_created");
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
