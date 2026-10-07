using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LessonSubstitutions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "lesson_substitutions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    period = table.Column<int>(type: "integer", nullable: false),
                    owner_kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    owner_id = table.Column<string>(type: "text", nullable: false),
                    subject_id = table.Column<string>(type: "text", nullable: false),
                    sub_group = table.Column<int>(type: "integer", nullable: false),
                    absent_teacher_id = table.Column<string>(type: "text", nullable: false),
                    substitute_teacher_id = table.Column<string>(type: "text", nullable: false),
                    reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lesson_substitutions", x => x.id);
                    table.CheckConstraint("ck_lesson_substitutions_cancel", "(cancelled_at is null) = (cancelled_by is null)");
                    table.CheckConstraint("ck_lesson_substitutions_people", "absent_teacher_id <> substitute_teacher_id");
                    table.ForeignKey(
                        name: "fk_lesson_substitutions_teachers_absent_teacher_id",
                        column: x => x.absent_teacher_id,
                        principalTable: "teachers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_lesson_substitutions_teachers_substitute_teacher_id",
                        column: x => x.substitute_teacher_id,
                        principalTable: "teachers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_lesson_substitutions_absent_teacher_id_date",
                table: "lesson_substitutions",
                columns: new[] { "absent_teacher_id", "date" });

            migrationBuilder.CreateIndex(
                name: "ix_lesson_substitutions_date_period_owner_id_subject_id_sub_gr",
                table: "lesson_substitutions",
                columns: new[] { "date", "period", "owner_id", "subject_id", "sub_group" },
                unique: true,
                filter: "cancelled_at is null");

            migrationBuilder.CreateIndex(
                name: "ix_lesson_substitutions_substitute_teacher_id_date",
                table: "lesson_substitutions",
                columns: new[] { "substitute_teacher_id", "date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lesson_substitutions");
        }
    }
}
