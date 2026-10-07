using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;

namespace SchoolLms.Infrastructure.Data;

/// <summary>So'rovnomalar va NPS, KPI weights (Polls.cs).</summary>
internal static class PollModel
{
    public static void Apply(ModelBuilder b)
    {
        b.Entity<Poll>(e =>
        {
            e.ToTable("polls");
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(2000);
            e.Property(x => x.Audience).HasMaxLength(20);
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.ClosesOn).HasColumnType("date");
            e.HasMany(x => x.Questions).WithOne().HasForeignKey(x => x.PollId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.Status, x.Audience });
            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_polls_audience", "audience in ('parents','teachers')");
                t.HasCheckConstraint("ck_polls_status", "status in ('draft','open','closed')");
            });
        });

        b.Entity<PollQuestion>(e =>
        {
            e.ToTable("poll_questions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Kind).HasMaxLength(20);
            e.Property(x => x.Text).HasMaxLength(500);
            e.Property(x => x.Options).HasMaxLength(2000);
            e.Property(x => x.Target).HasMaxLength(20);
            e.HasIndex(x => new { x.PollId, x.Position });
            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_poll_questions_kind", "kind in ('nps','stars','choice','text')");
                t.HasCheckConstraint("ck_poll_questions_target", "target in ('school','teachers','management')");
            });
        });

        b.Entity<PollResponse>(e =>
        {
            e.ToTable("poll_responses");
            e.HasKey(x => x.Id);
            e.Property(x => x.RespondentKey).HasMaxLength(100);
            e.Property(x => x.ClassName).HasMaxLength(100);
            e.HasOne<Poll>().WithMany().HasForeignKey(x => x.PollId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Answers).WithOne().HasForeignKey(x => x.ResponseId).OnDelete(DeleteBehavior.Cascade);
            // One answer sheet per respondent (per child for parents).
            e.HasIndex(x => new { x.PollId, x.RespondentKey }).IsUnique();
        });

        b.Entity<PollAnswer>(e =>
        {
            e.ToTable("poll_answers");
            e.HasKey(x => x.Id);
            e.Property(x => x.Choice).HasMaxLength(200);
            e.Property(x => x.Text).HasMaxLength(2000);
            e.HasOne<PollQuestion>().WithMany().HasForeignKey(x => x.QuestionId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.QuestionId, x.SubjectId });
            e.ToTable(t => t.HasCheckConstraint("ck_poll_answers_score", "score is null or (score >= 0 and score <= 10)"));
        });

        b.Entity<KpiSettings>(e =>
        {
            e.ToTable("kpi_settings");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.HasData(new KpiSettings { Id = KpiSettings.SingletonId, UpdatedAt = new DateTimeOffset(2026, 10, 7, 0, 0, 0, TimeSpan.Zero) });
            e.ToTable(t => t.HasCheckConstraint("ck_kpi_settings_single", "id = 1"));
        });
    }
}
