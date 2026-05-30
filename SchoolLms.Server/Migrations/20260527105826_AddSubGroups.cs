using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddSubGroups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SubGroup",
                table: "Students",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SubGroup",
                table: "ScheduleLesson",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SubGroup",
                table: "LessonNotes",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "SubGroup",
                table: "JournalEntries",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SubGroup",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "SubGroup",
                table: "ScheduleLesson");

            migrationBuilder.DropColumn(
                name: "SubGroup",
                table: "LessonNotes");

            migrationBuilder.DropColumn(
                name: "SubGroup",
                table: "JournalEntries");
        }
    }
}
