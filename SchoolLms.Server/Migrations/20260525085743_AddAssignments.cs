using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Assignments",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ClassId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    SubjectId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Quarter = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DueDate = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Assignments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Assignments_ClassId_SubjectId_Quarter",
                table: "Assignments",
                columns: new[] { "ClassId", "SubjectId", "Quarter" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Assignments");
        }
    }
}
