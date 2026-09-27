using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;

namespace SchoolLms.Infrastructure.Data;

/// <summary>
/// HR topshiriqlari (docs/modules/hr-tasks.md §4, migration <c>HrTasks</c>): the old WunderkindHR
/// tables one-to-one with the <c>hr_</c> prefix in <c>public</c> — columns, CHECKs, unique keys and
/// index names as in its <c>models.py</c>. Enum columns are varchar + CHECK (no native PG enums).
/// Not financial — full CRUD for <c>app_rw</c> (default privileges, no guards file).
/// </summary>
internal static class HrTasksModel
{
    private static string In(string column, params string[] values) =>
        $"{column} in ({string.Join(",", values.Select(v => $"'{v}'"))})";

    private const string WeekdaySet = "ARRAY[1,2,3,4,5,6,7]::smallint[]";
    private const string MonthDaySet =
        "ARRAY[1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25,26,27,28,29,30,31]::smallint[]";

    // Every DB default below is also `.ValueGeneratedNever()`: the column keeps the old
    // server default (for raw inserts / the one-time import), but EF always sends the CLR
    // value. Without it EF skips a value equal to the CLR default — `is_active = false` or a
    // 00:00 time would silently become the database default (true / 19:00).
    public static void Apply(ModelBuilder b)
    {
        b.Entity<HrPosition>(e =>
        {
            e.ToTable("hr_positions");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(120);
            e.Property(x => x.IsActive).HasDefaultValue(true).ValueGeneratedNever();
            e.HasIndex(x => x.Name).IsUnique().HasDatabaseName("uq_hr_position_name");
        });

        b.Entity<HrEmployee>(e =>
        {
            e.ToTable("hr_employees", t =>
            {
                t.HasCheckConstraint("ck_hr_employee_branch", In("branch", HrBranches.Center, HrBranches.School));
                t.HasCheckConstraint("ck_hr_employee_role", In("hr_role", HrRoles.All));
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.HrRole).HasMaxLength(16).HasDefaultValue(HrRoles.Employee).ValueGeneratedNever();
            e.Property(x => x.FullName).HasMaxLength(160);
            e.Property(x => x.Phone).HasMaxLength(20);
            e.Property(x => x.Branch).HasMaxLength(16);
            e.Property(x => x.IsActive).HasDefaultValue(true).ValueGeneratedNever();

            // 1:1 with our users. RESTRICT: an HR row carries task history — deleting the
            // account must not silently erase it (the HR row is deactivated instead).
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.UserId).IsUnique().HasDatabaseName("uq_hr_employee_user");
            e.HasIndex(x => x.Phone).IsUnique().HasDatabaseName("uq_hr_employee_phone");

            e.HasOne<HrPosition>().WithMany().HasForeignKey(x => x.PositionId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => x.PositionId).HasDatabaseName("ix_hr_employee_position");
            e.HasIndex(x => x.Branch).HasDatabaseName("ix_hr_employee_branch").HasFilter("branch is not null");
        });

        b.Entity<HrEmployeeManager>(e =>
        {
            // The shortest cycle (self) is a CHECK; longer ones are refused on write
            // (409 hr_manager_cycle) and the scope walk is cycle-safe on read.
            e.ToTable("hr_employee_managers", t =>
                t.HasCheckConstraint("ck_hr_employee_manager_self", "employee_id <> manager_id"));
            e.HasKey(x => new { x.EmployeeId, x.ManagerId });
            e.HasOne<HrEmployee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<HrEmployee>().WithMany().HasForeignKey(x => x.ManagerId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.ManagerId).HasDatabaseName("ix_hr_employee_managers_manager");
        });

        b.Entity<HrTaskTemplate>(e =>
        {
            e.ToTable("hr_task_templates", t =>
            {
                t.HasCheckConstraint("ck_hr_template_target", In("target_type", HrTargetTypes.Employee, HrTargetTypes.Position));
                t.HasCheckConstraint("ck_hr_template_recurrence",
                    In("recurrence", HrRecurrences.Daily, HrRecurrences.Weekly, HrRecurrences.Monthly));
                t.HasCheckConstraint("ck_hr_template_kind", In("kind", HrTaskKinds.Check, HrTaskKinds.Numeric));
                t.HasCheckConstraint("ck_hr_template_overdue_policy",
                    In("overdue_policy", HrOverduePolicies.Close, HrOverduePolicies.KeepOpen));
                t.HasCheckConstraint("ck_hr_template_target_ref",
                    "(target_type = 'employee' AND target_employee_id IS NOT NULL AND target_position_id IS NULL)"
                    + " OR (target_type = 'position' AND target_position_id IS NOT NULL AND target_employee_id IS NULL)");
                t.HasCheckConstraint("ck_hr_template_days",
                    "(recurrence IN ('daily','weekly') AND array_length(weekdays, 1) IS NOT NULL)"
                    + " OR (recurrence = 'monthly' AND array_length(days_of_month, 1) IS NOT NULL)");
                t.HasCheckConstraint("ck_hr_template_weekdays", $"weekdays <@ {WeekdaySet}");
                t.HasCheckConstraint("ck_hr_template_days_of_month", $"days_of_month <@ {MonthDaySet}");
                t.HasCheckConstraint("ck_hr_template_amount",
                    "(kind = 'numeric' AND target_amount IS NOT NULL AND target_amount > 0)"
                    + " OR (kind = 'check' AND target_amount IS NULL AND unit IS NULL)");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.TargetType).HasMaxLength(10);
            e.Property(x => x.Recurrence).HasMaxLength(10);
            e.Property(x => x.Weekdays).HasColumnType("smallint[]").HasDefaultValueSql("'{}'").ValueGeneratedNever();
            e.Property(x => x.DaysOfMonth).HasColumnType("smallint[]").HasDefaultValueSql("'{}'").ValueGeneratedNever();
            e.Property(x => x.Kind).HasMaxLength(10);
            e.Property(x => x.TargetAmount).HasPrecision(12, 2);
            e.Property(x => x.Unit).HasMaxLength(32);
            e.Property(x => x.ProofRequired).HasDefaultValue(false).ValueGeneratedNever();
            e.Property(x => x.OverduePolicy).HasMaxLength(12);
            e.Property(x => x.IsActive).HasDefaultValue(true).ValueGeneratedNever();
            e.Ignore(x => x.WeekdayList);
            e.Ignore(x => x.DayOfMonthList);
            // RESTRICT: employees and positions are deactivated, never deleted — a delete
            // must fail loudly rather than silently drop the rules.
            e.HasOne<HrEmployee>().WithMany().HasForeignKey(x => x.TargetEmployeeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<HrPosition>().WithMany().HasForeignKey(x => x.TargetPositionId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.TargetEmployeeId);
            e.HasIndex(x => x.TargetPositionId);
        });

        b.Entity<HrTask>(e =>
        {
            e.ToTable("hr_tasks", t =>
            {
                t.HasCheckConstraint("ck_hr_task_status", In("status", HrTaskStatuses.All));
                t.HasCheckConstraint("ck_hr_task_source", In("source", HrTaskSources.All));
                t.HasCheckConstraint("ck_hr_task_kind", In("kind", HrTaskKinds.Check, HrTaskKinds.Numeric));
                t.HasCheckConstraint("ck_hr_task_overdue_policy",
                    In("overdue_policy", HrOverduePolicies.Close, HrOverduePolicies.KeepOpen));
                t.HasCheckConstraint("ck_hr_task_amount",
                    "(kind = 'numeric' AND target_amount IS NOT NULL AND target_amount > 0)"
                    + " OR (kind = 'check' AND target_amount IS NULL AND unit IS NULL)");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Source).HasMaxLength(10);
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Kind).HasMaxLength(10).HasDefaultValue(HrTaskKinds.Check).ValueGeneratedNever();
            e.Property(x => x.TargetAmount).HasPrecision(12, 2);
            e.Property(x => x.CurrentAmount).HasPrecision(12, 2).HasDefaultValue(0m).ValueGeneratedNever();
            e.Property(x => x.Unit).HasMaxLength(32);
            e.Property(x => x.ProofRequired).HasDefaultValue(false).ValueGeneratedNever();
            e.Property(x => x.OverduePolicy).HasMaxLength(12).HasDefaultValue(HrOverduePolicies.Close).ValueGeneratedNever();
            e.Property(x => x.PeriodStart).HasColumnType("date");
            e.Property(x => x.Status).HasMaxLength(12).HasDefaultValue(HrTaskStatuses.Open).ValueGeneratedNever();
            e.Property(x => x.IsLate).HasDefaultValue(false).ValueGeneratedNever();
            e.Ignore(x => x.IsOpen);
            e.Ignore(x => x.IsFinal);
            e.Ignore(x => x.NeedsAcceptance);

            e.HasOne<HrEmployee>().WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
            // SET NULL: deleting a template keeps the tasks it produced (old §15.7).
            e.HasOne<HrTaskTemplate>().WithMany().HasForeignKey(x => x.TemplateId).OnDelete(DeleteBehavior.SetNull);

            // IDEMPOTENCY of generation (old §5.2): a re-run never makes a second instance.
            // due_at is in the key — a {14, 28} monthly template has two rows per month_start.
            e.HasIndex(x => new { x.TemplateId, x.EmployeeId, x.PeriodStart, x.DueAt })
                .IsUnique().HasDatabaseName("uq_hr_task_period").HasFilter("template_id IS NOT NULL");
            e.HasIndex(x => new { x.EmployeeId, x.PeriodStart }).HasDatabaseName("ix_hr_task_employee_period");
            e.HasIndex(x => new { x.Status, x.DueAt }).HasDatabaseName("ix_hr_task_status_due");
            e.HasIndex(x => x.PeriodStart).HasDatabaseName("ix_hr_task_period_start");
            e.HasIndex(x => x.DueAt).HasDatabaseName("ix_hr_task_due_open").HasFilter("status in ('open','returned')");
            e.HasIndex(x => x.TemplateId);
        });

        b.Entity<HrTaskProof>(e =>
        {
            e.ToTable("hr_task_proofs", t =>
                t.HasCheckConstraint("ck_hr_proof_kind",
                    In("kind", HrProofKinds.Photo, HrProofKinds.File, HrProofKinds.Link, HrProofKinds.Text)));
            e.HasKey(x => x.Id);
            e.Property(x => x.Kind).HasMaxLength(10);
            e.Property(x => x.FilePath).HasMaxLength(400);
            e.Property(x => x.OriginalName).HasMaxLength(200);
            e.Property(x => x.ContentType).HasMaxLength(100);
            e.Property(x => x.Url).HasMaxLength(500);
            e.HasOne<HrTask>().WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.TaskId).HasDatabaseName("ix_hr_proof_task");
        });

        b.Entity<HrTaskProgress>(e =>
        {
            e.ToTable("hr_task_progress");
            e.HasKey(x => x.Id);
            e.Property(x => x.Amount).HasPrecision(12, 2);
            e.Property(x => x.Note).HasMaxLength(200);
            e.HasOne<HrTask>().WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.TaskId, x.CreatedAt }).HasDatabaseName("ix_hr_progress_task");
        });

        b.Entity<HrTaskAttachment>(e =>
        {
            e.ToTable("hr_task_attachments", t =>
                t.HasCheckConstraint("ck_hr_attachment_kind", In("kind", HrAttachmentKinds.Voice, HrAttachmentKinds.File)));
            e.HasKey(x => x.Id);
            e.Property(x => x.Kind).HasMaxLength(16);
            e.Property(x => x.FilePath).HasMaxLength(500);
            e.Property(x => x.OriginalName).HasMaxLength(255);
            e.Property(x => x.ContentType).HasMaxLength(120);
            e.HasOne<HrTask>().WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.TaskId).HasDatabaseName("ix_hr_attachment_task");
        });

        b.Entity<HrTaskReminder>(e =>
        {
            e.ToTable("hr_task_reminders");
            e.HasKey(x => x.Id);
            e.HasOne<HrTask>().WithMany().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.TaskId, x.OffsetMinutes }).IsUnique().HasDatabaseName("uq_hr_task_reminder");
        });

        b.Entity<HrSettings>(e =>
        {
            e.ToTable("hr_settings", t =>
            {
                t.HasCheckConstraint("ck_hr_settings_single", "id = 1");
                t.HasCheckConstraint("ck_hr_settings_weekdays", $"digest_weekdays <@ {WeekdaySet}");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Timezone).HasMaxLength(64).HasDefaultValue("Asia/Tashkent").ValueGeneratedNever();
            e.Property(x => x.DigestTime).HasDefaultValueSql("'19:00'").ValueGeneratedNever();
            e.Property(x => x.DigestWeekdays).HasColumnType("smallint[]").HasDefaultValueSql("'{1,2,3,4,5,6}'").ValueGeneratedNever();
            e.Property(x => x.MorningListEnabled).HasDefaultValue(true).ValueGeneratedNever();
            e.Property(x => x.MorningTime).HasDefaultValueSql("'07:30'").ValueGeneratedNever();
            e.Property(x => x.GenerationTime).HasDefaultValueSql("'00:10'").ValueGeneratedNever();
            e.Property(x => x.CloseOverdueTime).HasDefaultValueSql("'23:50'").ValueGeneratedNever();
            e.Property(x => x.ReminderEnabled).HasDefaultValue(true).ValueGeneratedNever();
            e.Property(x => x.ReminderOffsets).HasColumnType("smallint[]").HasDefaultValueSql("'{120,60,30}'").ValueGeneratedNever();
            e.Property(x => x.RemindEndOfDay).HasDefaultValue(false).ValueGeneratedNever();
            e.Ignore(x => x.Zone);
        });

        b.Entity<HrJobRun>(e =>
        {
            e.ToTable("hr_job_runs", t =>
                t.HasCheckConstraint("ck_hr_job_run_status", In("status", HrJobs.StatusOk, HrJobs.StatusFailed)));
            e.HasKey(x => x.Id);
            e.Property(x => x.JobName).HasMaxLength(60);
            e.Property(x => x.RunDate).HasColumnType("date");
            e.Property(x => x.Status).HasMaxLength(10).HasDefaultValue(HrJobs.StatusOk).ValueGeneratedNever();
            e.Property(x => x.Details).HasColumnType("jsonb");
            e.HasIndex(x => new { x.JobName, x.RunDate }).IsUnique().HasDatabaseName("uq_hr_job_run");
        });
    }
}
