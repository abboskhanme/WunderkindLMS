using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddStudentArchive : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ArchiveReason",
                table: "Students",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ArchivedAt",
                table: "Students",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsArchived",
                table: "Students",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ArchiveReason",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "ArchivedAt",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "IsArchived",
                table: "Students");
        }
    }
}
