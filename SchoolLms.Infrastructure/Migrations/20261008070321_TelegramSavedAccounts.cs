using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TelegramSavedAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "telegram_saved_accounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    telegram_user_id = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    added_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    last_used_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_telegram_saved_accounts", x => x.id);
                    table.ForeignKey(
                        name: "fk_telegram_saved_accounts_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_telegram_saved_accounts_telegram_user_id_user_id",
                table: "telegram_saved_accounts",
                columns: new[] { "telegram_user_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_telegram_saved_accounts_user_id",
                table: "telegram_saved_accounts",
                column: "user_id");

            // Every Telegram already linked keeps its account in the list — nobody has to add it again.
            migrationBuilder.Sql("""
                INSERT INTO telegram_saved_accounts (id, telegram_user_id, user_id, added_at, last_used_at)
                SELECT gen_random_uuid(), a.telegram_user_id, a.user_id, a.linked_at, a.last_seen_at
                FROM telegram_accounts a
                ON CONFLICT (telegram_user_id, user_id) DO NOTHING;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "telegram_saved_accounts");
        }
    }
}
