using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddStudentLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "Latitude",
                table: "Students",
                type: "float",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LocationAddress",
                table: "Students",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LocationUpdatedAt",
                table: "Students",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "Longitude",
                table: "Students",
                type: "float",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Latitude",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "LocationAddress",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "LocationUpdatedAt",
                table: "Students");

            migrationBuilder.DropColumn(
                name: "Longitude",
                table: "Students");
        }
    }
}
