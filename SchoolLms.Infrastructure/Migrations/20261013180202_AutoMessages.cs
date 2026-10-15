using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AutoMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "auto_message_log",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    rule_key = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    period = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    student_id = table.Column<string>(type: "text", nullable: false),
                    chat_id = table.Column<long>(type: "bigint", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_auto_message_log", x => x.id);
                    table.CheckConstraint("ck_auto_message_log_status", "status in ('sent','failed','no_telegram')");
                    table.ForeignKey(
                        name: "fk_auto_message_log_students_student_id",
                        column: x => x.student_id,
                        principalTable: "students",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "auto_message_rules",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    send_time = table.Column<string>(type: "character varying(5)", maxLength: 5, nullable: false),
                    day_of_month = table.Column<int>(type: "integer", nullable: false),
                    min_amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    last_period = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    last_run_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_auto_message_rules", x => x.key);
                    table.CheckConstraint("ck_auto_message_rules_day", "day_of_month between 1 and 28");
                    table.CheckConstraint("ck_auto_message_rules_key", "key in ('absence','invoice','debt','birthday')");
                    table.CheckConstraint("ck_auto_message_rules_min", "min_amount >= 0");
                    table.CheckConstraint("ck_auto_message_rules_time", "send_time ~ '^([01][0-9]|2[0-3]):[0-5][0-9]$'");
                });

            migrationBuilder.CreateIndex(
                name: "ix_auto_message_log_at",
                table: "auto_message_log",
                column: "at");

            migrationBuilder.CreateIndex(
                name: "ix_auto_message_log_student_id",
                table: "auto_message_log",
                column: "student_id");

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

            // The four rules, all switched OFF: each is turned on once the school approves its text.
            // Texts = AutoMessageService.DefaultText.
            migrationBuilder.Sql("""
                INSERT INTO auto_message_rules (key, enabled, send_time, day_of_month, min_amount, text, updated_at) VALUES
                ('absence', false, '13:00', 1, 0, $t$📋 **Davomat**

                Hurmatli ota-ona, farzandingiz {fish} ({sinf}) {sana} kuni maktabga kelmadi.
                Sabab: {sabab}$t$, now()),
                ('invoice', false, '10:00', 2, 0, $t$🧾 **{oy} oyi uchun hisob**

                Hurmatli ota-ona, farzandingiz {fish} ({sinf}) uchun {oy} oyi to'lovi: {summa} so'm.
                Jami to'lanmagan: {qarzdorlik} so'm.$t$, now()),
                ('debt', false, '10:00', 10, 100000, $t$💳 **To'lov eslatmasi**

                Hurmatli ota-ona, farzandingiz {fish} ({sinf}) bo'yicha {qarzdorlik} so'm qarzdorlik bor ({oylar}).
                Iltimos, to'lovni o'z vaqtida amalga oshiring.$t$, now()),
                ('birthday', false, '08:00', 1, 0, $t$🎉 **Tug'ilgan kuningiz muborak!**

                Hurmatli ota-ona, bugun farzandingiz {ism}ning tug'ilgan kuni. Wunderkind maktabi jamoasi uni chin qalbdan tabriklaydi: sog'lik, baxt va yangi yutuqlar tilaymiz!$t$, now());
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "auto_message_log");

            migrationBuilder.DropTable(
                name: "auto_message_rules");
        }
    }
}
