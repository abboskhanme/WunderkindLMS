using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;

namespace SchoolLms.Infrastructure.Data;

/// <summary>Qo'shimcha qo'ng'iroq jadvallari va ularning egalari (<see cref="BellSchedule"/>, 2026-10-11).</summary>
internal static class BellScheduleModel
{
    public static void Apply(ModelBuilder b)
    {
        b.Entity<BellSchedule>(e =>
        {
            e.ToTable("bell_schedules");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.Name).HasMaxLength(120);
            e.HasIndex(x => x.Name).IsUnique().HasDatabaseName("ux_bell_schedules_name");
        });

        b.Entity<BellSchedulePeriod>(e =>
        {
            e.ToTable("bell_schedule_periods", t =>
                t.HasCheckConstraint("ck_bell_schedule_periods_period", "period between 1 and 20"));
            e.HasKey(x => new { x.ScheduleId, x.Period });
            e.Property(x => x.StartTime).HasMaxLength(5);
            e.Property(x => x.EndTime).HasMaxLength(5);
            e.HasOne<BellSchedule>().WithMany().HasForeignKey(x => x.ScheduleId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<BellScheduleOwner>(e =>
        {
            e.ToTable("bell_schedule_owners");
            e.HasKey(x => x.OwnerId);
            e.HasOne<BellSchedule>().WithMany().HasForeignKey(x => x.ScheduleId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.ScheduleId).HasDatabaseName("ix_bell_schedule_owners_schedule");
        });
    }
}
