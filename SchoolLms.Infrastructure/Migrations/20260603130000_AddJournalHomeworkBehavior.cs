using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddJournalHomeworkBehavior : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Behavior",
                table: "JournalEntries",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Homework",
                table: "JournalEntries",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Behavior",
                table: "JournalEntries");

            migrationBuilder.DropColumn(
                name: "Homework",
                table: "JournalEntries");
        }
    }
}
