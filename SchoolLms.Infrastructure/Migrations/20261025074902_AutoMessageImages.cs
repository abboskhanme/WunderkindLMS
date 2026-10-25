using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AutoMessageImages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Existing rules and runs are text-only: an empty array, not NULL (the columns are NOT NULL).
            migrationBuilder.AddColumn<List<string>>(
                name: "image_urls",
                table: "auto_message_runs",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");

            migrationBuilder.AddColumn<List<string>>(
                name: "image_urls",
                table: "auto_message_rules",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "image_urls",
                table: "auto_message_runs");

            migrationBuilder.DropColumn(
                name: "image_urls",
                table: "auto_message_rules");
        }
    }
}
