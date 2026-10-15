using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;

namespace SchoolLms.Infrastructure.Data;

/// <summary>Automatic Telegram messages (<c>AutoMessages</c> migration, docs/modules/auto-messages.md).</summary>
internal static class AutoMessagesModel
{
    public static void Apply(ModelBuilder b)
    {
        b.Entity<AutoMessageRule>(e =>
        {
            e.ToTable("auto_message_rules", t =>
            {
                t.HasCheckConstraint("ck_auto_message_rules_key", "key in ('absence','invoice','debt','birthday')");
                t.HasCheckConstraint("ck_auto_message_rules_day", "day_of_month between 1 and 28");
                t.HasCheckConstraint("ck_auto_message_rules_time", "send_time ~ '^([01][0-9]|2[0-3]):[0-5][0-9]$'");
                t.HasCheckConstraint("ck_auto_message_rules_min", "min_amount >= 0");
            });
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(16);
            e.Property(x => x.SendTime).HasMaxLength(5);
            e.Property(x => x.MinAmount).HasPrecision(14, 2);
            e.Property(x => x.LastPeriod).HasMaxLength(10);
        });

        b.Entity<AutoMessageLogEntry>(e =>
        {
            e.ToTable("auto_message_log", t =>
                t.HasCheckConstraint("ck_auto_message_log_status", "status in ('sent','failed','no_telegram')"));
            e.HasKey(x => x.Id);
            e.Property(x => x.RuleKey).HasMaxLength(16);
            e.Property(x => x.Period).HasMaxLength(10);
            e.Property(x => x.Status).HasMaxLength(16);
            // Once per pupil and chat in a period; the "no parent on Telegram" row once per pupil.
            e.HasIndex(x => new { x.RuleKey, x.Period, x.StudentId, x.ChatId }).IsUnique()
                .HasFilter("chat_id is not null").HasDatabaseName("ux_auto_message_log_chat");
            e.HasIndex(x => new { x.RuleKey, x.Period, x.StudentId }).IsUnique()
                .HasFilter("chat_id is null").HasDatabaseName("ux_auto_message_log_no_chat");
            e.HasIndex(x => x.At).HasDatabaseName("ix_auto_message_log_at");
            e.HasOne<Student>().WithMany().HasForeignKey(x => x.StudentId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
