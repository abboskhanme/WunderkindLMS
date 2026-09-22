using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    // ===========================================================================
    //  Kechki dars va yotoqxona davomati (mijoz, 2026-09-23). study_groups.is_track —
    //  "yo'nalish guruhi" belgisi (sukut false, mavjud guruhlar o'zgarmaydi) va yangi
    //  boarding_attendance jadvali. Faqat AddColumn/CreateTable; birorta DROP yo'q
    //  (qatorma-qator o'qilgan). Grantlar: boarding_attendance_guards.sql.
    // ===========================================================================
    public partial class BoardingAttendance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_track",
                table: "study_groups",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "boarding_attendance",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    session = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    student_id = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    marked_by = table.Column<string>(type: "text", nullable: false),
                    marked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    notified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_boarding_attendance", x => x.id);
                    table.CheckConstraint("ck_boarding_attendance_session", "session in ('evening','dorm')");
                    table.CheckConstraint("ck_boarding_attendance_status", "status in ('present','absent','excused')");
                    table.ForeignKey(
                        name: "fk_boarding_attendance_students_student_id",
                        column: x => x.student_id,
                        principalTable: "students",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_boarding_attendance_date_session_student_id",
                table: "boarding_attendance",
                columns: new[] { "date", "session", "student_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_boarding_attendance_student_id",
                table: "boarding_attendance",
                column: "student_id");

            migrationBuilder.Sql(MigrationSql.Read("boarding_attendance_guards.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "boarding_attendance");

            migrationBuilder.DropColumn(
                name: "is_track",
                table: "study_groups");
        }
    }
}
