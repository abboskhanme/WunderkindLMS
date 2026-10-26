using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;

namespace SchoolLms.Infrastructure.Data;

/// <summary>
/// Automatic Telegram messages (<c>AutoMessages</c> and <c>AutoMessagesRuns</c> migrations,
/// docs/modules/auto-messages.md).
/// </summary>
internal static class AutoMessagesModel
{
    private const string TimePattern = "'^([01][0-9]|2[0-3]):[0-5][0-9]$'";

    public static void Apply(ModelBuilder b)
    {
        b.Entity<AutoMessageRule>(e =>
        {
            e.ToTable("auto_message_rules", t =>
            {
                // Templates staff create (2026-10-26) are "c_" and 8 hex digits, with a schedule;
                // the built-in rules have none.
                t.HasCheckConstraint("ck_auto_message_rules_key",
                    "key in ('absence','invoice','debt','birthday','payment') or key ~ '^c_[0-9a-f]{8}$'");
                t.HasCheckConstraint("ck_auto_message_rules_schedule",
                    "(key ~ '^c_' and schedule in ('once','daily','weekly','monthly')) or (key !~ '^c_' and schedule is null)");
                t.HasCheckConstraint("ck_auto_message_rules_audience",
                    "audience is null or audience in ('all','class','group','selected')");
                t.HasCheckConstraint("ck_auto_message_rules_send_date",
                    "send_date is null or send_date ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$'");
                t.HasCheckConstraint("ck_auto_message_rules_day", "day_of_month between 1 and 28");
                t.HasCheckConstraint("ck_auto_message_rules_time", $"send_time ~ {TimePattern}");
                t.HasCheckConstraint("ck_auto_message_rules_cutoff", $"cutoff_time is null or cutoff_time ~ {TimePattern}");
                t.HasCheckConstraint("ck_auto_message_rules_min", "min_amount >= 0");
            });
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(16);
            e.Property(x => x.SendTime).HasMaxLength(5);
            e.Property(x => x.CutoffTime).HasMaxLength(5);
            e.Property(x => x.MinAmount).HasPrecision(14, 2);
            e.Property(x => x.LastPeriod).HasMaxLength(10);
            e.Property(x => x.Title).HasMaxLength(100);
            e.Property(x => x.Schedule).HasMaxLength(8);
            e.Property(x => x.SendDate).HasMaxLength(10);
            e.Property(x => x.Audience).HasMaxLength(8);
        });

        b.Entity<AutoMessageRun>(e =>
        {
            e.ToTable("auto_message_runs", t => t.HasCheckConstraint("ck_auto_message_runs_status",
                "status in ('scheduled','pending_approval','sending','done','cancelled')"));
            e.HasKey(x => x.Id);
            e.Property(x => x.RuleKey).HasMaxLength(16);
            e.Property(x => x.Period).HasMaxLength(10);
            e.Property(x => x.Status).HasMaxLength(16);
            e.Property(x => x.Note).HasMaxLength(1000);
            // One run per rule and period.
            e.HasIndex(x => new { x.RuleKey, x.Period }).IsUnique().HasDatabaseName("ux_auto_message_runs_rule_period");
            e.HasIndex(x => x.PlannedAt).HasDatabaseName("ix_auto_message_runs_planned_at");
            e.HasOne<AutoMessageRule>().WithMany().HasForeignKey(x => x.RuleKey).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<AutoMessageLogEntry>(e =>
        {
            e.ToTable("auto_message_log", t =>
            {
                t.HasCheckConstraint("ck_auto_message_log_status",
                    "status in ('sent','failed','no_telegram','scheduled','pending_approval','cancelled')");
                t.HasCheckConstraint("ck_auto_message_log_source", "source in ('auto','manual')");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.RuleKey).HasMaxLength(16);
            e.Property(x => x.Period).HasMaxLength(10);
            e.Property(x => x.Status).HasMaxLength(16);
            e.Property(x => x.RefId).HasMaxLength(32);
            e.Property(x => x.Source).HasMaxLength(8).HasDefaultValue(AutoMessageSource.Auto);
            e.Property(x => x.Error).HasMaxLength(500);
            // Once per pupil and chat in a period; the "no parent on Telegram" row once per pupil.
            // Payment rows are one per receipt send, so they are outside both.
            e.HasIndex(x => new { x.RuleKey, x.Period, x.StudentId, x.ChatId }).IsUnique()
                .HasFilter("chat_id is not null and rule_key <> 'payment'").HasDatabaseName("ux_auto_message_log_chat");
            e.HasIndex(x => new { x.RuleKey, x.Period, x.StudentId }).IsUnique()
                .HasFilter("chat_id is null and rule_key <> 'payment'").HasDatabaseName("ux_auto_message_log_no_chat");
            e.HasIndex(x => x.At).HasDatabaseName("ix_auto_message_log_at");
            e.HasIndex(x => x.RunId).HasDatabaseName("ix_auto_message_log_run_id");
            e.HasOne<Student>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<AutoMessageRun>().WithMany().HasForeignKey(x => x.RunId).OnDelete(DeleteBehavior.SetNull);
        });
    }
}
