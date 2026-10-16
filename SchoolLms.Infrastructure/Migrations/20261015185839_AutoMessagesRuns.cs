using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AutoMessagesRuns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_auto_message_rules_key",
                table: "auto_message_rules");

            migrationBuilder.DropIndex(
                name: "ux_auto_message_log_chat",
                table: "auto_message_log");

            migrationBuilder.DropIndex(
                name: "ux_auto_message_log_no_chat",
                table: "auto_message_log");

            migrationBuilder.DropCheckConstraint(
                name: "ck_auto_message_log_status",
                table: "auto_message_log");

            migrationBuilder.AddColumn<string>(
                name: "cutoff_time",
                table: "auto_message_rules",
                type: "character varying(5)",
                maxLength: 5,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "requires_approval",
                table: "auto_message_rules",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "attempts",
                table: "auto_message_log",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "error",
                table: "auto_message_log",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "planned_at",
                table: "auto_message_log",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ref_id",
                table: "auto_message_log",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "run_id",
                table: "auto_message_log",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "sent_by",
                table: "auto_message_log",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source",
                table: "auto_message_log",
                type: "character varying(8)",
                maxLength: 8,
                nullable: false,
                defaultValue: "auto");

            migrationBuilder.CreateTable(
                name: "auto_message_runs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_key = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    period = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    planned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    pupils = table.Column<int>(type: "integer", nullable: false),
                    sent = table.Column<int>(type: "integer", nullable: false),
                    failed = table.Column<int>(type: "integer", nullable: false),
                    no_telegram = table.Column<int>(type: "integer", nullable: false),
                    pending = table.Column<int>(type: "integer", nullable: false),
                    cancelled = table.Column<int>(type: "integer", nullable: false),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    approved_by = table.Column<string>(type: "text", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_by = table.Column<string>(type: "text", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_auto_message_runs", x => x.id);
                    table.CheckConstraint("ck_auto_message_runs_status", "status in ('scheduled','pending_approval','sending','done','cancelled')");
                    table.ForeignKey(
                        name: "fk_auto_message_runs_auto_message_rules_rule_key",
                        column: x => x.rule_key,
                        principalTable: "auto_message_rules",
                        principalColumn: "key",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_auto_message_rules_cutoff",
                table: "auto_message_rules",
                sql: "cutoff_time is null or cutoff_time ~ '^([01][0-9]|2[0-3]):[0-5][0-9]$'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_auto_message_rules_key",
                table: "auto_message_rules",
                sql: "key in ('absence','invoice','debt','birthday','payment')");

            migrationBuilder.CreateIndex(
                name: "ix_auto_message_log_run_id",
                table: "auto_message_log",
                column: "run_id");

            migrationBuilder.CreateIndex(
                name: "ux_auto_message_log_chat",
                table: "auto_message_log",
                columns: new[] { "rule_key", "period", "student_id", "chat_id" },
                unique: true,
                filter: "chat_id is not null and rule_key <> 'payment'");

            migrationBuilder.CreateIndex(
                name: "ux_auto_message_log_no_chat",
                table: "auto_message_log",
                columns: new[] { "rule_key", "period", "student_id" },
                unique: true,
                filter: "chat_id is null and rule_key <> 'payment'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_auto_message_log_source",
                table: "auto_message_log",
                sql: "source in ('auto','manual')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_auto_message_log_status",
                table: "auto_message_log",
                sql: "status in ('sent','failed','no_telegram','scheduled','pending_approval','cancelled')");

            migrationBuilder.CreateIndex(
                name: "ix_auto_message_runs_planned_at",
                table: "auto_message_runs",
                column: "planned_at");

            migrationBuilder.CreateIndex(
                name: "ux_auto_message_runs_rule_period",
                table: "auto_message_runs",
                columns: new[] { "rule_key", "period" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_auto_message_log_auto_message_runs_run_id",
                table: "auto_message_log",
                column: "run_id",
                principalTable: "auto_message_runs",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.Sql("UPDATE auto_message_log SET attempts = 1 WHERE status IN ('sent','failed');");

            // The payment rule ships switched OFF, like the others (text = AutoMessageService.DefaultText).
            // The receipt PDF goes either way; when on, this text becomes its caption.
            migrationBuilder.Sql("""
                INSERT INTO auto_message_rules (key, enabled, send_time, day_of_month, min_amount, text, requires_approval, updated_at) VALUES
                ('payment', false, '09:00', 1, 0, $t$✅ **To'lov qabul qilindi**

                Hurmatli ota-ona, farzandingiz {fish} ({sinf}) uchun {summa} so'm to'lov qabul qilindi.
                Chek № {chek} · {sana} {vaqt}
                To'lov usuli: {usul}
                Qolgan qarzdorlik: {qarzdorlik} so'm$t$, false, now())
                ON CONFLICT (key) DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM auto_message_log WHERE rule_key = 'payment' OR status IN ('scheduled','pending_approval','cancelled');
                DELETE FROM auto_message_rules WHERE key = 'payment';
                """);

            migrationBuilder.DropForeignKey(
                name: "fk_auto_message_log_auto_message_runs_run_id",
                table: "auto_message_log");

            migrationBuilder.DropTable(
                name: "auto_message_runs");

            migrationBuilder.DropCheckConstraint(
                name: "ck_auto_message_rules_cutoff",
                table: "auto_message_rules");

            migrationBuilder.DropCheckConstraint(
                name: "ck_auto_message_rules_key",
                table: "auto_message_rules");

            migrationBuilder.DropIndex(
                name: "ix_auto_message_log_run_id",
                table: "auto_message_log");

            migrationBuilder.DropIndex(
                name: "ux_auto_message_log_chat",
                table: "auto_message_log");

            migrationBuilder.DropIndex(
                name: "ux_auto_message_log_no_chat",
                table: "auto_message_log");

            migrationBuilder.DropCheckConstraint(
                name: "ck_auto_message_log_source",
                table: "auto_message_log");

            migrationBuilder.DropCheckConstraint(
                name: "ck_auto_message_log_status",
                table: "auto_message_log");

            migrationBuilder.DropColumn(
                name: "cutoff_time",
                table: "auto_message_rules");

            migrationBuilder.DropColumn(
                name: "requires_approval",
                table: "auto_message_rules");

            migrationBuilder.DropColumn(
                name: "attempts",
                table: "auto_message_log");

            migrationBuilder.DropColumn(
                name: "error",
                table: "auto_message_log");

            migrationBuilder.DropColumn(
                name: "planned_at",
                table: "auto_message_log");

            migrationBuilder.DropColumn(
                name: "ref_id",
                table: "auto_message_log");

            migrationBuilder.DropColumn(
                name: "run_id",
                table: "auto_message_log");

            migrationBuilder.DropColumn(
                name: "sent_by",
                table: "auto_message_log");

            migrationBuilder.DropColumn(
                name: "source",
                table: "auto_message_log");

            migrationBuilder.AddCheckConstraint(
                name: "ck_auto_message_rules_key",
                table: "auto_message_rules",
                sql: "key in ('absence','invoice','debt','birthday')");

            migrationBuilder.CreateIndex(
                name: "ux_auto_message_log_chat",
                table: "auto_message_log",
                columns: new[] { "rule_key", "period", "student_id", "chat_id" },
                unique: true,
                filter: "chat_id is not null");

            migrationBuilder.CreateIndex(
                name: "ux_auto_message_log_no_chat",
                table: "auto_message_log",
                columns: new[] { "rule_key", "period", "student_id" },
                unique: true,
                filter: "chat_id is null");

            migrationBuilder.AddCheckConstraint(
                name: "ck_auto_message_log_status",
                table: "auto_message_log",
                sql: "status in ('sent','failed','no_telegram')");
        }
    }
}
