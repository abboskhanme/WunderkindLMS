using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InvoiceMonthRemoval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "remove_reason",
                table: "invoices",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "removed_at",
                table: "invoices",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "removed_by",
                table: "invoices",
                type: "text",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_invoices_removed_is_void",
                table: "invoices",
                sql: "removed_at is null or status = 'void'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_invoices_removed_is_void",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "remove_reason",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "removed_at",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "removed_by",
                table: "invoices");
        }
    }
}
