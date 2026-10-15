using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ExamAwards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "exam_award_notices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    exam_id = table.Column<string>(type: "text", nullable: false),
                    student_id = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    rank = table.Column<int>(type: "integer", nullable: false),
                    percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    broadcast_id = table.Column<string>(type: "text", nullable: false),
                    delivered = table.Column<int>(type: "integer", nullable: false),
                    with_diploma = table.Column<bool>(type: "boolean", nullable: false),
                    sent_by = table.Column<string>(type: "text", nullable: false),
                    sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exam_award_notices", x => x.id);
                    table.CheckConstraint("ck_exam_award_notices_kind", "kind in ('top','bottom')");
                    table.CheckConstraint("ck_exam_award_notices_rank", "rank >= 1");
                    table.ForeignKey(
                        name: "fk_exam_award_notices_exams_exam_id",
                        column: x => x.exam_id,
                        principalTable: "exams",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_exam_award_notices_students_student_id",
                        column: x => x.student_id,
                        principalTable: "students",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_exam_award_notices_student_id",
                table: "exam_award_notices",
                column: "student_id");

            migrationBuilder.CreateIndex(
                name: "ux_exam_award_notices_once",
                table: "exam_award_notices",
                columns: new[] { "exam_id", "student_id", "kind" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "exam_award_notices");
        }
    }
}
