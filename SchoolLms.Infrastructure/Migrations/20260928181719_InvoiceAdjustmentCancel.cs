using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InvoiceAdjustmentCancel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "group_id",
                table: "invoice_adjustments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // billing-plans.md §9.1 — rows written before groups existed: each is its own group, so
            // cancelling one never takes another along. Runs as the owner; app_rw stays append-only.
            migrationBuilder.Sql(
                "update invoice_adjustments set group_id = id where group_id = '00000000-0000-0000-0000-000000000000'");

            migrationBuilder.AddColumn<Guid>(
                name: "reversal_of",
                table: "invoice_adjustments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_invoice_adjustments_group_id",
                table: "invoice_adjustments",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoice_adjustments_reversal_of",
                table: "invoice_adjustments",
                column: "reversal_of",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_invoice_adjustments_invoice_adjustments_reversal_of",
                table: "invoice_adjustments",
                column: "reversal_of",
                principalTable: "invoice_adjustments",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_invoice_adjustments_invoice_adjustments_reversal_of",
                table: "invoice_adjustments");

            migrationBuilder.DropIndex(
                name: "ix_invoice_adjustments_group_id",
                table: "invoice_adjustments");

            migrationBuilder.DropIndex(
                name: "ix_invoice_adjustments_reversal_of",
                table: "invoice_adjustments");

            migrationBuilder.DropColumn(
                name: "group_id",
                table: "invoice_adjustments");

            migrationBuilder.DropColumn(
                name: "reversal_of",
                table: "invoice_adjustments");
        }
    }
}
