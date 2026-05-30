using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRichAssignments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoGrade",
                table: "Assignments",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ClassIds",
                table: "Assignments",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.AddColumn<string>(
                name: "CreatedByUserId",
                table: "Assignments",
                type: "nvarchar(450)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Format",
                table: "Assignments",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<bool>(
                name: "LateAccept",
                table: "Assignments",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "LatePenaltyPct",
                table: "Assignments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "MaxScore",
                table: "Assignments",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "StartDate",
                table: "Assignments",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AssignmentMaterials",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    AssignmentId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Url = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssignmentMaterials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssignmentMaterials_Assignments_AssignmentId",
                        column: x => x.AssignmentId,
                        principalTable: "Assignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TestQuestions",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    AssignmentId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Options = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CorrectIndex = table.Column<int>(type: "int", nullable: false),
                    Order = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TestQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TestQuestions_Assignments_AssignmentId",
                        column: x => x.AssignmentId,
                        principalTable: "Assignments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Assignments_CreatedByUserId",
                table: "Assignments",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_AssignmentMaterials_AssignmentId",
                table: "AssignmentMaterials",
                column: "AssignmentId");

            migrationBuilder.CreateIndex(
                name: "IX_TestQuestions_AssignmentId",
                table: "TestQuestions",
                column: "AssignmentId");

            // Eski (oddiy) topshiriqlar ClassId'sini yangi ClassIds ro'yxatiga ko'chiramiz.
            migrationBuilder.Sql(
                "UPDATE [Assignments] SET [ClassIds] = '[\"' + [ClassId] + '\"]' " +
                "WHERE [ClassId] IS NOT NULL AND [ClassId] <> '' AND ([ClassIds] = '[]' OR [ClassIds] IS NULL)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssignmentMaterials");

            migrationBuilder.DropTable(
                name: "TestQuestions");

            migrationBuilder.DropIndex(
                name: "IX_Assignments_CreatedByUserId",
                table: "Assignments");

            migrationBuilder.DropColumn(
                name: "AutoGrade",
                table: "Assignments");

            migrationBuilder.DropColumn(
                name: "ClassIds",
                table: "Assignments");

            migrationBuilder.DropColumn(
                name: "CreatedByUserId",
                table: "Assignments");

            migrationBuilder.DropColumn(
                name: "Format",
                table: "Assignments");

            migrationBuilder.DropColumn(
                name: "LateAccept",
                table: "Assignments");

            migrationBuilder.DropColumn(
                name: "LatePenaltyPct",
                table: "Assignments");

            migrationBuilder.DropColumn(
                name: "MaxScore",
                table: "Assignments");

            migrationBuilder.DropColumn(
                name: "StartDate",
                table: "Assignments");
        }
    }
}
