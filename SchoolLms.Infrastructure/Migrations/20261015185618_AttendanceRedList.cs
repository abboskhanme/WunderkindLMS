using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AttendanceRedList : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "attendance_cases",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<string>(type: "text", nullable: false),
                    opened_on = table.Column<DateOnly>(type: "date", nullable: false),
                    trigger = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    streak = table.Column<int>(type: "integer", nullable: false),
                    window_count = table.Column<int>(type: "integer", nullable: false),
                    last_absent_on = table.Column<DateOnly>(type: "date", nullable: true),
                    last_present_on = table.Column<DateOnly>(type: "date", nullable: true),
                    absent_dates = table.Column<List<DateOnly>>(type: "date[]", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    callback_on = table.Column<DateOnly>(type: "date", nullable: true),
                    last_action_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_by = table.Column<string>(type: "text", nullable: true),
                    notified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attendance_cases", x => x.id);
                    table.CheckConstraint("ck_attendance_cases_closed", "(status = 'hal_boldi') = (closed_at is not null)");
                    table.CheckConstraint("ck_attendance_cases_counts", "streak >= 0 and window_count >= 0");
                    table.CheckConstraint("ck_attendance_cases_status", "status in ('aloqa_kerak','qayta_qongiroq','boglanib_bolmadi','hal_boldi')");
                    table.CheckConstraint("ck_attendance_cases_trigger", "trigger in ('consecutive','window')");
                    table.ForeignKey(
                        name: "fk_attendance_cases_students_student_id",
                        column: x => x.student_id,
                        principalTable: "students",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "attendance_red_list_settings",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    consecutive_days = table.Column<int>(type: "integer", nullable: false),
                    window_days = table.Column<int>(type: "integer", nullable: false),
                    window_unexcused = table.Column<int>(type: "integer", nullable: false),
                    count_excused = table.Column<bool>(type: "boolean", nullable: false),
                    escalate_after_days = table.Column<int>(type: "integer", nullable: false),
                    notify_teachers = table.Column<bool>(type: "boolean", nullable: false),
                    notify_management = table.Column<bool>(type: "boolean", nullable: false),
                    last_scan_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_scan_slot = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    last_digest_on = table.Column<DateOnly>(type: "date", nullable: true),
                    updated_by = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attendance_red_list_settings", x => x.id);
                    table.CheckConstraint("ck_attendance_red_list_settings_consecutive", "consecutive_days between 2 and 10");
                    table.CheckConstraint("ck_attendance_red_list_settings_escalate", "escalate_after_days between 1 and 14");
                    table.CheckConstraint("ck_attendance_red_list_settings_single", "id = 1");
                    table.CheckConstraint("ck_attendance_red_list_settings_window", "window_days between 3 and 60 and window_unexcused between 2 and 30 and window_unexcused <= window_days");
                });

            migrationBuilder.CreateTable(
                name: "attendance_case_actions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    case_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    callback_on = table.Column<DateOnly>(type: "date", nullable: true),
                    actor_id = table.Column<string>(type: "text", nullable: false),
                    actor_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    actor_type = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attendance_case_actions", x => x.id);
                    table.CheckConstraint("ck_attendance_case_actions_actor_type", "actor_type in ('teacher','staff')");
                    table.CheckConstraint("ck_attendance_case_actions_comment", "length(btrim(comment)) > 0");
                    table.CheckConstraint("ck_attendance_case_actions_status", "status in ('aloqa_kerak','qayta_qongiroq','boglanib_bolmadi','hal_boldi')");
                    table.ForeignKey(
                        name: "fk_attendance_case_actions_attendance_cases_case_id",
                        column: x => x.case_id,
                        principalTable: "attendance_cases",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "attendance_red_list_settings",
                columns: new[] { "id", "consecutive_days", "count_excused", "escalate_after_days", "last_digest_on", "last_scan_at", "last_scan_slot", "notify_management", "notify_teachers", "updated_at", "updated_by", "window_days", "window_unexcused" },
                values: new object[] { 1, 3, false, 2, null, null, null, false, false, new DateTimeOffset(new DateTime(2026, 10, 15, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 14, 5 });

            migrationBuilder.CreateIndex(
                name: "ix_attendance_case_actions_case",
                table: "attendance_case_actions",
                columns: new[] { "case_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_attendance_cases_closed_at",
                table: "attendance_cases",
                column: "closed_at");

            migrationBuilder.CreateIndex(
                name: "ix_attendance_cases_student",
                table: "attendance_cases",
                columns: new[] { "student_id", "opened_on" });

            migrationBuilder.CreateIndex(
                name: "ux_attendance_cases_open",
                table: "attendance_cases",
                column: "student_id",
                unique: true,
                filter: "closed_at is null");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "attendance_case_actions");

            migrationBuilder.DropTable(
                name: "attendance_red_list_settings");

            migrationBuilder.DropTable(
                name: "attendance_cases");
        }
    }
}
