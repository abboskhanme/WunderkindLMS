using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddJournalPeriod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Period",
                table: "LessonNotes",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Period",
                table: "JournalEntries",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Period",
                table: "LessonNotes");

            migrationBuilder.DropColumn(
                name: "Period",
                table: "JournalEntries");
        }
    }
}
