using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEvaluationGradeMonth : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Month",
                table: "EvaluationGrades",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Week",
                table: "EvaluationGrades",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Month",
                table: "EvaluationGrades");

            migrationBuilder.DropColumn(
                name: "Week",
                table: "EvaluationGrades");
        }
    }
}
