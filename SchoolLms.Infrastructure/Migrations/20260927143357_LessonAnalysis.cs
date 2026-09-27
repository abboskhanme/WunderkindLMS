using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LessonAnalysis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "lesson_observation_sections",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false),
                    order_index = table.Column<int>(type: "integer", nullable: false),
                    color = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    legacy_id = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lesson_observation_sections", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "lesson_observations",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    teacher_id = table.Column<string>(type: "text", nullable: false),
                    observer_user_id = table.Column<string>(type: "text", nullable: false),
                    lesson_date = table.Column<DateOnly>(type: "date", nullable: false),
                    lesson_time = table.Column<string>(type: "text", nullable: true),
                    class_name = table.Column<string>(type: "text", nullable: true),
                    subject = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    strengths = table.Column<string>(type: "text", nullable: true),
                    growth_areas = table.Column<string>(type: "text", nullable: true),
                    next_lesson_advice = table.Column<string>(type: "text", nullable: true),
                    ai_summary = table.Column<string>(type: "text", nullable: true),
                    ai_error = table.Column<string>(type: "text", nullable: true),
                    score_done = table.Column<int>(type: "integer", nullable: false),
                    score_total = table.Column<int>(type: "integer", nullable: false),
                    score_percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    legacy_id = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lesson_observations", x => x.id);
                    table.CheckConstraint("ck_lesson_observations_status", "status in ('draft','completed')");
                    table.ForeignKey(
                        name: "fk_lesson_observations_teachers_teacher_id",
                        column: x => x.teacher_id,
                        principalTable: "teachers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_lesson_observations_users_observer_user_id",
                        column: x => x.observer_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "lesson_observation_criteria",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    section_id = table.Column<string>(type: "text", nullable: false),
                    text = table.Column<string>(type: "text", nullable: false),
                    order_index = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    legacy_id = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lesson_observation_criteria", x => x.id);
                    table.ForeignKey(
                        name: "fk_lesson_observation_criteria_lesson_observation_sections_sec",
                        column: x => x.section_id,
                        principalTable: "lesson_observation_sections",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "lesson_observation_details",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    observation_id = table.Column<string>(type: "text", nullable: false),
                    criterion_id = table.Column<string>(type: "text", nullable: false),
                    is_done = table.Column<bool>(type: "boolean", nullable: true),
                    comment = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lesson_observation_details", x => x.id);
                    table.ForeignKey(
                        name: "fk_lesson_observation_details_lesson_observation_criteria_crit",
                        column: x => x.criterion_id,
                        principalTable: "lesson_observation_criteria",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_lesson_observation_details_lesson_observations_observation_",
                        column: x => x.observation_id,
                        principalTable: "lesson_observations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_lesson_observation_criteria_legacy_id",
                table: "lesson_observation_criteria",
                column: "legacy_id",
                unique: true,
                filter: "legacy_id is not null");

            migrationBuilder.CreateIndex(
                name: "ix_lesson_observation_criteria_section_id_order_index",
                table: "lesson_observation_criteria",
                columns: new[] { "section_id", "order_index" });

            migrationBuilder.CreateIndex(
                name: "ix_lesson_observation_details_criterion_id",
                table: "lesson_observation_details",
                column: "criterion_id");

            migrationBuilder.CreateIndex(
                name: "ix_lesson_observation_details_observation_id_criterion_id",
                table: "lesson_observation_details",
                columns: new[] { "observation_id", "criterion_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_lesson_observation_sections_legacy_id",
                table: "lesson_observation_sections",
                column: "legacy_id",
                unique: true,
                filter: "legacy_id is not null");

            migrationBuilder.CreateIndex(
                name: "ix_lesson_observation_sections_order_index",
                table: "lesson_observation_sections",
                column: "order_index");

            migrationBuilder.CreateIndex(
                name: "ix_lesson_observations_legacy_id",
                table: "lesson_observations",
                column: "legacy_id",
                unique: true,
                filter: "legacy_id is not null");

            migrationBuilder.CreateIndex(
                name: "ix_lesson_observations_observer_user_id",
                table: "lesson_observations",
                column: "observer_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_lesson_observations_status_lesson_date",
                table: "lesson_observations",
                columns: new[] { "status", "lesson_date" });

            migrationBuilder.CreateIndex(
                name: "ix_lesson_observations_teacher_id_status_lesson_date",
                table: "lesson_observations",
                columns: new[] { "teacher_id", "status", "lesson_date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lesson_observation_details");

            migrationBuilder.DropTable(
                name: "lesson_observation_criteria");

            migrationBuilder.DropTable(
                name: "lesson_observations");

            migrationBuilder.DropTable(
                name: "lesson_observation_sections");
        }
    }
}
