using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BehaviourIncidentsAndTeacherAttendanceFlag : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "teachers_mark_attendance",
                table: "school_meta",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "created_by_user_id",
                table: "discipline_points",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "updated_at",
                table: "discipline_points",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "updated_by",
                table: "discipline_points",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "teachers_mark_attendance",
                table: "school_meta");

            migrationBuilder.DropColumn(
                name: "created_by_user_id",
                table: "discipline_points");

            migrationBuilder.DropColumn(
                name: "updated_at",
                table: "discipline_points");

            migrationBuilder.DropColumn(
                name: "updated_by",
                table: "discipline_points");
        }
    }
}
