using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEvaluationGrade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EvaluationGrades",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    StudentId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EvaluationTypeId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Score = table.Column<int>(type: "int", nullable: false),
                    UpdatedAt = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, defaultValue: "")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvaluationGrades", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationGrades_TenantId",
                table: "EvaluationGrades",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EvaluationGrades");
        }
    }
}
