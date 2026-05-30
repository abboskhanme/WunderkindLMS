using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Server.Migrations
{
    /// <inheritdoc />
    public partial class AddMessaging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Broadcasts",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ClassName = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SenderUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SenderName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RecipientCount = table.Column<int>(type: "int", nullable: false),
                    SentCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Broadcasts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChatMessages",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ClassName = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    SenderUserId = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SenderName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SenderRole = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Text = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TelegramRegistrations",
                columns: table => new
                {
                    Id = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    StudentId = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    ChatId = table.Column<long>(type: "bigint", nullable: false),
                    ParentName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TelegramRegistrations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Broadcasts_ClassName_CreatedAt",
                table: "Broadcasts",
                columns: new[] { "ClassName", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_ClassName_CreatedAt",
                table: "ChatMessages",
                columns: new[] { "ClassName", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TelegramRegistrations_ChatId",
                table: "TelegramRegistrations",
                column: "ChatId");

            migrationBuilder.CreateIndex(
                name: "IX_TelegramRegistrations_StudentId_ChatId",
                table: "TelegramRegistrations",
                columns: new[] { "StudentId", "ChatId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Broadcasts");

            migrationBuilder.DropTable(
                name: "ChatMessages");

            migrationBuilder.DropTable(
                name: "TelegramRegistrations");
        }
    }
}
