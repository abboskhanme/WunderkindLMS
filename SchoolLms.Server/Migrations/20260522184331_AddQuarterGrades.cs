using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddQuarterGrades : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "QuarterGrades",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ClassId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    SubjectId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Quarter = table.Column<int>(type: "int", nullable: false),
                    StudentId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Grade = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QuarterGrades", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_QuarterGrades_ClassId_SubjectId_Quarter_StudentId",
                table: "QuarterGrades",
                columns: new[] { "ClassId", "SubjectId", "Quarter", "StudentId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "QuarterGrades");
        }
    }
}
