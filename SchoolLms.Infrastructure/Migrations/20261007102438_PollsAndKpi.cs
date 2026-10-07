using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PollsAndKpi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "kpi_settings",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false),
                    weight_tasks = table.Column<int>(type: "integer", nullable: false),
                    weight_lesson_analysis = table.Column<int>(type: "integer", nullable: false),
                    weight_journal = table.Column<int>(type: "integer", nullable: false),
                    weight_parent_nps = table.Column<int>(type: "integer", nullable: false),
                    weight_staff_nps = table.Column<int>(type: "integer", nullable: false),
                    weight_penalties = table.Column<int>(type: "integer", nullable: false),
                    weight_attendance = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_kpi_settings", x => x.id);
                    table.CheckConstraint("ck_kpi_settings_single", "id = 1");
                });

            migrationBuilder.CreateTable(
                name: "polls",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    audience = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    anonymous = table.Column<bool>(type: "boolean", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    closes_on = table.Column<DateOnly>(type: "date", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    notified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_polls", x => x.id);
                    table.CheckConstraint("ck_polls_audience", "audience in ('parents','teachers')");
                    table.CheckConstraint("ck_polls_status", "status in ('draft','open','closed')");
                });

            migrationBuilder.CreateTable(
                name: "poll_questions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    poll_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    options = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    required = table.Column<bool>(type: "boolean", nullable: false),
                    target = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_poll_questions", x => x.id);
                    table.CheckConstraint("ck_poll_questions_kind", "kind in ('nps','stars','choice','text')");
                    table.CheckConstraint("ck_poll_questions_target", "target in ('school','teachers','management')");
                    table.ForeignKey(
                        name: "fk_poll_questions_polls_poll_id",
                        column: x => x.poll_id,
                        principalTable: "polls",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "poll_responses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    poll_id = table.Column<Guid>(type: "uuid", nullable: false),
                    respondent_key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    student_id = table.Column<string>(type: "text", nullable: true),
                    teacher_id = table.Column<string>(type: "text", nullable: true),
                    class_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_poll_responses", x => x.id);
                    table.ForeignKey(
                        name: "fk_poll_responses_polls_poll_id",
                        column: x => x.poll_id,
                        principalTable: "polls",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "poll_answers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    response_id = table.Column<Guid>(type: "uuid", nullable: false),
                    question_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_id = table.Column<string>(type: "text", nullable: true),
                    score = table.Column<int>(type: "integer", nullable: true),
                    choice = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    text = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_poll_answers", x => x.id);
                    table.CheckConstraint("ck_poll_answers_score", "score is null or (score >= 0 and score <= 10)");
                    table.ForeignKey(
                        name: "fk_poll_answers_poll_questions_question_id",
                        column: x => x.question_id,
                        principalTable: "poll_questions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_poll_answers_poll_responses_response_id",
                        column: x => x.response_id,
                        principalTable: "poll_responses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "kpi_settings",
                columns: new[] { "id", "updated_at", "updated_by", "weight_attendance", "weight_journal", "weight_lesson_analysis", "weight_parent_nps", "weight_penalties", "weight_staff_nps", "weight_tasks" },
                values: new object[] { 1, new DateTimeOffset(new DateTime(2026, 10, 7, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, 5, 10, 25, 15, 10, 10, 25 });

            migrationBuilder.CreateIndex(
                name: "ix_poll_answers_question_id_subject_id",
                table: "poll_answers",
                columns: new[] { "question_id", "subject_id" });

            migrationBuilder.CreateIndex(
                name: "ix_poll_answers_response_id",
                table: "poll_answers",
                column: "response_id");

            migrationBuilder.CreateIndex(
                name: "ix_poll_questions_poll_id_position",
                table: "poll_questions",
                columns: new[] { "poll_id", "position" });

            migrationBuilder.CreateIndex(
                name: "ix_poll_responses_poll_id_respondent_key",
                table: "poll_responses",
                columns: new[] { "poll_id", "respondent_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_polls_status_audience",
                table: "polls",
                columns: new[] { "status", "audience" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "kpi_settings");

            migrationBuilder.DropTable(
                name: "poll_answers");

            migrationBuilder.DropTable(
                name: "poll_questions");

            migrationBuilder.DropTable(
                name: "poll_responses");

            migrationBuilder.DropTable(
                name: "polls");
        }
    }
}
