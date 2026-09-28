using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TelegramPanelKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "panel_key_hash",
                table: "telegram_accounts",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "panel_key_issued_at",
                table: "telegram_accounts",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_telegram_accounts_panel_key_hash",
                table: "telegram_accounts",
                column: "panel_key_hash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_telegram_accounts_panel_key_hash",
                table: "telegram_accounts");

            migrationBuilder.DropColumn(
                name: "panel_key_hash",
                table: "telegram_accounts");

            migrationBuilder.DropColumn(
                name: "panel_key_issued_at",
                table: "telegram_accounts");
        }
    }
}
