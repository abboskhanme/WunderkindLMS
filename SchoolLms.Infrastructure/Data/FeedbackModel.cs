using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;

namespace SchoolLms.Infrastructure.Data;

/// <summary>Taklif va shikoyatlar workflow tables (FeedbackWorkflow.cs).</summary>
internal static class FeedbackModel
{
    /// <summary>The departments the school starts with — renamed or switched off on the settings screen.</summary>
    private static readonly (Guid Id, string Name)[] Seed =
    [
        (new Guid("0f3d1a00-0000-4000-8000-000000000001"), "O'quv bo'limi"),
        (new Guid("0f3d1a00-0000-4000-8000-000000000002"), "Moliya"),
        (new Guid("0f3d1a00-0000-4000-8000-000000000003"), "Ovqatlanish"),
        (new Guid("0f3d1a00-0000-4000-8000-000000000004"), "Transport"),
        (new Guid("0f3d1a00-0000-4000-8000-000000000005"), "Yotoqxona"),
        (new Guid("0f3d1a00-0000-4000-8000-000000000006"), "HR"),
        (new Guid("0f3d1a00-0000-4000-8000-000000000007"), "Rahbariyat"),
    ];

    public static void Apply(ModelBuilder b)
    {
        b.Entity<FeedbackDepartment>(e =>
        {
            e.ToTable("feedback_departments");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(100);
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
            e.ToTable(t => t.HasCheckConstraint("ck_feedback_departments_name", "btrim(name) <> ''"));
            var created = new DateTimeOffset(2026, 10, 7, 0, 0, 0, TimeSpan.Zero);
            e.HasData(Seed.Select((d, i) => new FeedbackDepartment
            {
                Id = d.Id, Name = d.Name, Position = i + 1, IsActive = true, CreatedAt = created,
            }));
        });

        b.Entity<FeedbackDepartmentMember>(e =>
        {
            e.ToTable("feedback_department_members");
            e.HasKey(x => new { x.DepartmentId, x.UserId });
            e.HasOne<FeedbackDepartment>().WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.UserId);
        });

        b.Entity<FeedbackEvent>(e =>
        {
            e.ToTable("feedback_events");
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.Note).HasMaxLength(2000);
            e.Property(x => x.ActorName).HasMaxLength(200);
            e.HasOne<Feedback>().WithMany().HasForeignKey(x => x.FeedbackId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.FeedbackId, x.CreatedAt });
        });

        b.Entity<Feedback>(e =>
        {
            e.Property(x => x.Answer).HasMaxLength(2000);
            e.HasOne<FeedbackDepartment>().WithMany().HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => x.SenderUserId);
            e.HasIndex(x => new { x.DepartmentId, x.Status });
        });
    }
}
