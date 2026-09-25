using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TelegramAccountsManyPerUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_telegram_accounts_user_id",
                table: "telegram_accounts");

            migrationBuilder.CreateIndex(
                name: "ix_telegram_accounts_user_id",
                table: "telegram_accounts",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_telegram_accounts_user_id",
                table: "telegram_accounts");

            migrationBuilder.CreateIndex(
                name: "ix_telegram_accounts_user_id",
                table: "telegram_accounts",
                column: "user_id",
                unique: true);
        }
    }
}
