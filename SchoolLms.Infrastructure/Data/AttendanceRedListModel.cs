using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;

namespace SchoolLms.Infrastructure.Data;

/// <summary>Attendance red list: cases, their actions and the criteria row (<c>AttendanceRedList</c> migration,
/// docs/modules/attendance-red-list.md).</summary>
internal static class AttendanceRedListModel
{
    private const string Statuses = "('aloqa_kerak','qayta_qongiroq','boglanib_bolmadi','hal_boldi')";

    public static void Apply(ModelBuilder b)
    {
        b.Entity<AttendanceCase>(e =>
        {
            e.ToTable("attendance_cases", t =>
            {
                t.HasCheckConstraint("ck_attendance_cases_status", $"status in {Statuses}");
                t.HasCheckConstraint("ck_attendance_cases_trigger", "trigger in ('consecutive','window')");
                // Closed exactly when resolved — a closed case in another status would never show anywhere.
                t.HasCheckConstraint("ck_attendance_cases_closed", "(status = 'hal_boldi') = (closed_at is not null)");
                t.HasCheckConstraint("ck_attendance_cases_counts", "streak >= 0 and window_count >= 0");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Trigger).HasMaxLength(16);
            e.Property(x => x.Status).HasMaxLength(20);
            // One open case per pupil; the scan relies on it to stay idempotent.
            e.HasIndex(x => x.StudentId).IsUnique().HasFilter("closed_at is null")
                .HasDatabaseName("ux_attendance_cases_open");
            e.HasIndex(x => new { x.StudentId, x.OpenedOn }).HasDatabaseName("ix_attendance_cases_student");
            e.HasIndex(x => x.ClosedAt).HasDatabaseName("ix_attendance_cases_closed_at");
            e.HasOne<Student>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<AttendanceCaseAction>(e =>
        {
            e.ToTable("attendance_case_actions", t =>
            {
                t.HasCheckConstraint("ck_attendance_case_actions_status", $"status in {Statuses}");
                t.HasCheckConstraint("ck_attendance_case_actions_comment", "length(btrim(comment)) > 0");
                t.HasCheckConstraint("ck_attendance_case_actions_actor_type", "actor_type in ('teacher','staff')");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasMaxLength(20);
            e.Property(x => x.Comment).HasMaxLength(1000);
            e.Property(x => x.ActorName).HasMaxLength(200);
            e.Property(x => x.ActorType).HasMaxLength(8);
            e.HasIndex(x => new { x.CaseId, x.CreatedAt }).HasDatabaseName("ix_attendance_case_actions_case");
            e.HasOne<AttendanceCase>().WithMany().HasForeignKey(x => x.CaseId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<AttendanceRedListSettings>(e =>
        {
            e.ToTable("attendance_red_list_settings", t =>
            {
                t.HasCheckConstraint("ck_attendance_red_list_settings_single", "id = 1");
                t.HasCheckConstraint("ck_attendance_red_list_settings_consecutive", "consecutive_days between 2 and 10");
                t.HasCheckConstraint("ck_attendance_red_list_settings_window",
                    "window_days between 3 and 60 and window_unexcused between 2 and 30 and window_unexcused <= window_days");
                t.HasCheckConstraint("ck_attendance_red_list_settings_escalate", "escalate_after_days between 1 and 14");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.LastScanSlot).HasMaxLength(16);
            // The defaults of AttendanceRedListSettings (3 days in a row, 5 in 14 days, excused not counted,
            // overdue after 2 days, both Telegram notices off until the school approves the texts).
            e.HasData(new AttendanceRedListSettings
            {
                Id = AttendanceRedListSettings.SingletonId,
                UpdatedAt = new DateTimeOffset(2026, 10, 15, 0, 0, 0, TimeSpan.Zero),
            });
        });
    }
}
