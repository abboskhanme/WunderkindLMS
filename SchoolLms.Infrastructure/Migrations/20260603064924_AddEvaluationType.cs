using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEvaluationType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EvaluationTypes",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TenantId = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false, defaultValue: "")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EvaluationTypes", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EvaluationTypes_TenantId",
                table: "EvaluationTypes",
                column: "TenantId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EvaluationTypes");
        }
    }
}
