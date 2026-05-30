using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddFeedbackImageAndSender : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ImageUrl",
                table: "Feedbacks",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SenderName",
                table: "Feedbacks",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "SenderRole",
                table: "Feedbacks",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "TeacherId",
                table: "Feedbacks",
                type: "nvarchar(max)",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ImageUrl",
                table: "Feedbacks");

            migrationBuilder.DropColumn(
                name: "SenderName",
                table: "Feedbacks");

            migrationBuilder.DropColumn(
                name: "SenderRole",
                table: "Feedbacks");

            migrationBuilder.DropColumn(
                name: "TeacherId",
                table: "Feedbacks");
        }
    }
}
