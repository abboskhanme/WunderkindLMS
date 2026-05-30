using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LmsSubjects",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ClassNames = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    UnlockMode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    BatchSize = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LmsSubjects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LmsTopics",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    SubjectId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    VideoUrl = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TextContent = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Order = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LmsTopics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LmsTopics_LmsSubjects_SubjectId",
                        column: x => x.SubjectId,
                        principalTable: "LmsSubjects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LmsMaterials",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    TopicId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Url = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LmsMaterials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LmsMaterials_LmsTopics_TopicId",
                        column: x => x.TopicId,
                        principalTable: "LmsTopics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LmsProgresses",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    StudentId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    TopicId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LmsProgresses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LmsProgresses_LmsTopics_TopicId",
                        column: x => x.TopicId,
                        principalTable: "LmsTopics",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LmsMaterials_TopicId",
                table: "LmsMaterials",
                column: "TopicId");

            migrationBuilder.CreateIndex(
                name: "IX_LmsProgresses_StudentId",
                table: "LmsProgresses",
                column: "StudentId");

            migrationBuilder.CreateIndex(
                name: "IX_LmsProgresses_StudentId_TopicId",
                table: "LmsProgresses",
                columns: new[] { "StudentId", "TopicId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LmsProgresses_TopicId",
                table: "LmsProgresses",
                column: "TopicId");

            migrationBuilder.CreateIndex(
                name: "IX_LmsTopics_SubjectId_Order",
                table: "LmsTopics",
                columns: new[] { "SubjectId", "Order" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LmsMaterials");

            migrationBuilder.DropTable(
                name: "LmsProgresses");

            migrationBuilder.DropTable(
                name: "LmsTopics");

            migrationBuilder.DropTable(
                name: "LmsSubjects");
        }
    }
}
