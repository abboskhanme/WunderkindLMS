using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// Who a news item's Telegram message reached (client, 2026-10-12: "Telegram: 120/150 bunda aynan
    /// kimlarga borgan bormaganini bilsa bo'ladimi"): one row per chat (sent / failed) and per person with
    /// no Telegram, written when the news is sent. New table only; older news keeps just the counts.
    /// </summary>
    public partial class NewsTelegramRecipients : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "news_telegram_recipients",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    news_id = table.Column<Guid>(type: "uuid", nullable: false),
                    chat_id = table.Column<long>(type: "bigint", nullable: true),
                    kind = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    detail = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_news_telegram_recipients", x => x.id);
                    table.CheckConstraint("ck_news_telegram_recipients_chat", "(status = 'no_telegram') = (chat_id is null)");
                    table.CheckConstraint("ck_news_telegram_recipients_kind", "kind in ('parent','employee','student')");
                    table.CheckConstraint("ck_news_telegram_recipients_status", "status in ('sent','failed','no_telegram')");
                    table.ForeignKey(
                        name: "fk_news_telegram_recipients_news_news_id",
                        column: x => x.news_id,
                        principalTable: "news",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_news_telegram_recipients_news_id",
                table: "news_telegram_recipients",
                column: "news_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "news_telegram_recipients");
        }
    }
}
