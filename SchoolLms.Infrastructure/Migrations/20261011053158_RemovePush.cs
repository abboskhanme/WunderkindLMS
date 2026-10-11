using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class RemovePush : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "device_tokens");

            migrationBuilder.DropTable(
                name: "push_messages");

            migrationBuilder.DropColumn(
                name: "fcm_service_account_json",
                table: "school_meta");

            migrationBuilder.DropColumn(
                name: "fcm_vapid_key",
                table: "school_meta");

            migrationBuilder.DropColumn(
                name: "fcm_web_config_json",
                table: "school_meta");

            // Push butunlay olib tashlandi (mijoz, 2026-10-11: "bizda app yo'q, hammasi telegram mini app
            // bilan"). push_messages app_ro allow-list'idan ham chiqdi — grantlarni qayta qo'llaymiz.
            migrationBuilder.Sql(MigrationSql.Read("mcp_guards.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "fcm_service_account_json",
                table: "school_meta",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "fcm_vapid_key",
                table: "school_meta",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "fcm_web_config_json",
                table: "school_meta",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "device_tokens",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    app_id = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    device_name = table.Column<string>(type: "text", nullable: false),
                    last_seen_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    platform = table.Column<string>(type: "text", nullable: false),
                    token = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_device_tokens", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "push_messages",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    audience = table.Column<string>(type: "text", nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    recipient_count = table.Column<int>(type: "integer", nullable: false),
                    sender_name = table.Column<string>(type: "text", nullable: false),
                    sender_user_id = table.Column<string>(type: "text", nullable: false),
                    sent_count = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_push_messages", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_device_tokens_token",
                table: "device_tokens",
                column: "token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_device_tokens_user_id",
                table: "device_tokens",
                column: "user_id");
        }
    }
}
