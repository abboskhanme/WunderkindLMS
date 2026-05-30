using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddSubmissionContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AnswerText",
                table: "AssignmentSubmissions",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FileUrl",
                table: "AssignmentSubmissions",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AnswerText",
                table: "AssignmentSubmissions");

            migrationBuilder.DropColumn(
                name: "FileUrl",
                table: "AssignmentSubmissions");
        }
    }
}
