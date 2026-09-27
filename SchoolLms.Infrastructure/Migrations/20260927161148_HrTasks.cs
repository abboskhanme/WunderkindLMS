using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class HrTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "hr_job_runs",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    job_name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    run_date = table.Column<DateOnly>(type: "date", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, defaultValue: "ok"),
                    details = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_job_runs", x => x.id);
                    table.CheckConstraint("ck_hr_job_run_status", "status in ('ok','failed')");
                });

            migrationBuilder.CreateTable(
                name: "hr_positions",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_positions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "hr_settings",
                columns: table => new
                {
                    id = table.Column<short>(type: "smallint", nullable: false),
                    timezone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false, defaultValue: "Asia/Tashkent"),
                    digest_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false, defaultValueSql: "'19:00'"),
                    digest_weekdays = table.Column<short[]>(type: "smallint[]", nullable: false, defaultValueSql: "'{1,2,3,4,5,6}'"),
                    morning_list_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    morning_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false, defaultValueSql: "'07:30'"),
                    generation_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false, defaultValueSql: "'00:10'"),
                    close_overdue_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false, defaultValueSql: "'23:50'"),
                    reminder_enabled = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    reminder_offsets = table.Column<short[]>(type: "smallint[]", nullable: false, defaultValueSql: "'{120,60,30}'"),
                    remind_end_of_day = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_settings", x => x.id);
                    table.CheckConstraint("ck_hr_settings_single", "id = 1");
                    table.CheckConstraint("ck_hr_settings_weekdays", "digest_weekdays <@ ARRAY[1,2,3,4,5,6,7]::smallint[]");
                });

            migrationBuilder.CreateTable(
                name: "hr_employees",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    hr_role = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false, defaultValue: "employee"),
                    full_name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    position_id = table.Column<string>(type: "text", nullable: true),
                    branch = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_employees", x => x.id);
                    table.CheckConstraint("ck_hr_employee_branch", "branch in ('center','school')");
                    table.CheckConstraint("ck_hr_employee_role", "hr_role in ('employee','manager','director')");
                    table.ForeignKey(
                        name: "fk_hr_employees_hr_positions_position_id",
                        column: x => x.position_id,
                        principalTable: "hr_positions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_hr_employees_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "hr_employee_managers",
                columns: table => new
                {
                    employee_id = table.Column<string>(type: "text", nullable: false),
                    manager_id = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_employee_managers", x => new { x.employee_id, x.manager_id });
                    table.CheckConstraint("ck_hr_employee_manager_self", "employee_id <> manager_id");
                    table.ForeignKey(
                        name: "fk_hr_employee_managers_hr_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "hr_employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_hr_employee_managers_hr_employees_manager_id",
                        column: x => x.manager_id,
                        principalTable: "hr_employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "hr_task_templates",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    target_type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    target_employee_id = table.Column<string>(type: "text", nullable: true),
                    target_position_id = table.Column<string>(type: "text", nullable: true),
                    recurrence = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    weekdays = table.Column<short[]>(type: "smallint[]", nullable: false, defaultValueSql: "'{}'"),
                    days_of_month = table.Column<short[]>(type: "smallint[]", nullable: false, defaultValueSql: "'{}'"),
                    due_time = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    target_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    unit = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    proof_required = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    overdue_policy = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_task_templates", x => x.id);
                    table.CheckConstraint("ck_hr_template_amount", "(kind = 'numeric' AND target_amount IS NOT NULL AND target_amount > 0) OR (kind = 'check' AND target_amount IS NULL AND unit IS NULL)");
                    table.CheckConstraint("ck_hr_template_days", "(recurrence IN ('daily','weekly') AND array_length(weekdays, 1) IS NOT NULL) OR (recurrence = 'monthly' AND array_length(days_of_month, 1) IS NOT NULL)");
                    table.CheckConstraint("ck_hr_template_days_of_month", "days_of_month <@ ARRAY[1,2,3,4,5,6,7,8,9,10,11,12,13,14,15,16,17,18,19,20,21,22,23,24,25,26,27,28,29,30,31]::smallint[]");
                    table.CheckConstraint("ck_hr_template_kind", "kind in ('check','numeric')");
                    table.CheckConstraint("ck_hr_template_overdue_policy", "overdue_policy in ('close','keep_open')");
                    table.CheckConstraint("ck_hr_template_recurrence", "recurrence in ('daily','weekly','monthly')");
                    table.CheckConstraint("ck_hr_template_target", "target_type in ('employee','position')");
                    table.CheckConstraint("ck_hr_template_target_ref", "(target_type = 'employee' AND target_employee_id IS NOT NULL AND target_position_id IS NULL) OR (target_type = 'position' AND target_position_id IS NOT NULL AND target_employee_id IS NULL)");
                    table.CheckConstraint("ck_hr_template_weekdays", "weekdays <@ ARRAY[1,2,3,4,5,6,7]::smallint[]");
                    table.ForeignKey(
                        name: "fk_hr_task_templates_hr_employees_target_employee_id",
                        column: x => x.target_employee_id,
                        principalTable: "hr_employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_hr_task_templates_hr_positions_target_position_id",
                        column: x => x.target_position_id,
                        principalTable: "hr_positions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "hr_tasks",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    employee_id = table.Column<string>(type: "text", nullable: false),
                    template_id = table.Column<string>(type: "text", nullable: true),
                    source = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false, defaultValue: "check"),
                    target_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    current_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false, defaultValue: 0m),
                    unit = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    proof_required = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    overdue_policy = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false, defaultValue: "close"),
                    period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false, defaultValue: "open"),
                    is_late = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    employee_comment = table.Column<string>(type: "text", nullable: true),
                    director_comment = table.Column<string>(type: "text", nullable: true),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    returned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_tasks", x => x.id);
                    table.CheckConstraint("ck_hr_task_amount", "(kind = 'numeric' AND target_amount IS NOT NULL AND target_amount > 0) OR (kind = 'check' AND target_amount IS NULL AND unit IS NULL)");
                    table.CheckConstraint("ck_hr_task_kind", "kind in ('check','numeric')");
                    table.CheckConstraint("ck_hr_task_overdue_policy", "overdue_policy in ('close','keep_open')");
                    table.CheckConstraint("ck_hr_task_source", "source in ('director','self','template')");
                    table.CheckConstraint("ck_hr_task_status", "status in ('open','done','returned','missed','cancelled')");
                    table.ForeignKey(
                        name: "fk_hr_tasks_hr_employees_employee_id",
                        column: x => x.employee_id,
                        principalTable: "hr_employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_hr_tasks_hr_task_templates_template_id",
                        column: x => x.template_id,
                        principalTable: "hr_task_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "hr_task_attachments",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    task_id = table.Column<string>(type: "text", nullable: true),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    file_path = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    original_name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    content_type = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    size_bytes = table.Column<int>(type: "integer", nullable: false),
                    duration_seconds = table.Column<int>(type: "integer", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_task_attachments", x => x.id);
                    table.CheckConstraint("ck_hr_attachment_kind", "kind in ('voice','file')");
                    table.ForeignKey(
                        name: "fk_hr_task_attachments_hr_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "hr_tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "hr_task_progress",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    task_id = table.Column<string>(type: "text", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_task_progress", x => x.id);
                    table.ForeignKey(
                        name: "fk_hr_task_progress_hr_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "hr_tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "hr_task_proofs",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    task_id = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    file_path = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    original_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    size_bytes = table.Column<int>(type: "integer", nullable: true),
                    url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    text = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_task_proofs", x => x.id);
                    table.CheckConstraint("ck_hr_proof_kind", "kind in ('photo','file','link','text')");
                    table.ForeignKey(
                        name: "fk_hr_task_proofs_hr_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "hr_tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "hr_task_reminders",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    task_id = table.Column<string>(type: "text", nullable: false),
                    offset_minutes = table.Column<short>(type: "smallint", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_hr_task_reminders", x => x.id);
                    table.ForeignKey(
                        name: "fk_hr_task_reminders_hr_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "hr_tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_hr_employee_managers_manager",
                table: "hr_employee_managers",
                column: "manager_id");

            migrationBuilder.CreateIndex(
                name: "ix_hr_employee_branch",
                table: "hr_employees",
                column: "branch",
                filter: "branch is not null");

            migrationBuilder.CreateIndex(
                name: "ix_hr_employee_position",
                table: "hr_employees",
                column: "position_id");

            migrationBuilder.CreateIndex(
                name: "uq_hr_employee_phone",
                table: "hr_employees",
                column: "phone",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_hr_employee_user",
                table: "hr_employees",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_hr_job_run",
                table: "hr_job_runs",
                columns: new[] { "job_name", "run_date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "uq_hr_position_name",
                table: "hr_positions",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_hr_attachment_task",
                table: "hr_task_attachments",
                column: "task_id");

            migrationBuilder.CreateIndex(
                name: "ix_hr_progress_task",
                table: "hr_task_progress",
                columns: new[] { "task_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_hr_proof_task",
                table: "hr_task_proofs",
                column: "task_id");

            migrationBuilder.CreateIndex(
                name: "uq_hr_task_reminder",
                table: "hr_task_reminders",
                columns: new[] { "task_id", "offset_minutes" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_hr_task_templates_target_employee_id",
                table: "hr_task_templates",
                column: "target_employee_id");

            migrationBuilder.CreateIndex(
                name: "ix_hr_task_templates_target_position_id",
                table: "hr_task_templates",
                column: "target_position_id");

            migrationBuilder.CreateIndex(
                name: "ix_hr_task_due_open",
                table: "hr_tasks",
                column: "due_at",
                filter: "status in ('open','returned')");

            migrationBuilder.CreateIndex(
                name: "ix_hr_task_employee_period",
                table: "hr_tasks",
                columns: new[] { "employee_id", "period_start" });

            migrationBuilder.CreateIndex(
                name: "ix_hr_task_period_start",
                table: "hr_tasks",
                column: "period_start");

            migrationBuilder.CreateIndex(
                name: "ix_hr_task_status_due",
                table: "hr_tasks",
                columns: new[] { "status", "due_at" });

            migrationBuilder.CreateIndex(
                name: "ix_hr_tasks_template_id",
                table: "hr_tasks",
                column: "template_id");

            migrationBuilder.CreateIndex(
                name: "uq_hr_task_period",
                table: "hr_tasks",
                columns: new[] { "template_id", "employee_id", "period_start", "due_at" },
                unique: true,
                filter: "template_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "hr_employee_managers");

            migrationBuilder.DropTable(
                name: "hr_job_runs");

            migrationBuilder.DropTable(
                name: "hr_settings");

            migrationBuilder.DropTable(
                name: "hr_task_attachments");

            migrationBuilder.DropTable(
                name: "hr_task_progress");

            migrationBuilder.DropTable(
                name: "hr_task_proofs");

            migrationBuilder.DropTable(
                name: "hr_task_reminders");

            migrationBuilder.DropTable(
                name: "hr_tasks");

            migrationBuilder.DropTable(
                name: "hr_task_templates");

            migrationBuilder.DropTable(
                name: "hr_employees");

            migrationBuilder.DropTable(
                name: "hr_positions");
        }
    }
}
