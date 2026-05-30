using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAssignmentTypeAndLesson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Date",
                table: "Assignments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Period",
                table: "Assignments",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TypeId",
                table: "Assignments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AssignmentTypes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssignmentTypes", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssignmentTypes");

            migrationBuilder.DropColumn(
                name: "Date",
                table: "Assignments");

            migrationBuilder.DropColumn(
                name: "Period",
                table: "Assignments");

            migrationBuilder.DropColumn(
                name: "TypeId",
                table: "Assignments");
        }
    }
}
