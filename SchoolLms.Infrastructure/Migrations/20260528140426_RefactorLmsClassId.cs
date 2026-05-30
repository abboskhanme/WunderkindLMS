using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RefactorLmsClassId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ClassNames",
                table: "LmsSubjects");

            migrationBuilder.AddColumn<string>(
                name: "ClassId",
                table: "LmsSubjects",
                type: "nvarchar(450)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_LmsSubjects_ClassId",
                table: "LmsSubjects",
                column: "ClassId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LmsSubjects_ClassId",
                table: "LmsSubjects");

            migrationBuilder.DropColumn(
                name: "ClassId",
                table: "LmsSubjects");

            migrationBuilder.AddColumn<string>(
                name: "ClassNames",
                table: "LmsSubjects",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }
    }
}
