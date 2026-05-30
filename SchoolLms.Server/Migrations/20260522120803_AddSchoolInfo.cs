using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddSchoolInfo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Address",
                table: "SchoolMeta",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Director",
                table: "SchoolMeta",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "District",
                table: "SchoolMeta",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "SchoolMeta",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Name",
                table: "SchoolMeta",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Phone",
                table: "SchoolMeta",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Region",
                table: "SchoolMeta",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Address",
                table: "SchoolMeta");

            migrationBuilder.DropColumn(
                name: "Director",
                table: "SchoolMeta");

            migrationBuilder.DropColumn(
                name: "District",
                table: "SchoolMeta");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "SchoolMeta");

            migrationBuilder.DropColumn(
                name: "Name",
                table: "SchoolMeta");

            migrationBuilder.DropColumn(
                name: "Phone",
                table: "SchoolMeta");

            migrationBuilder.DropColumn(
                name: "Region",
                table: "SchoolMeta");
        }
    }
}
