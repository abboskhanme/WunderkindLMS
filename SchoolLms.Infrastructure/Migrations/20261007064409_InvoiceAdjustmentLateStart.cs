using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InvoiceAdjustmentLateStart : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_invoice_adjustments_kind",
                table: "invoice_adjustments");

            migrationBuilder.AddCheckConstraint(
                name: "ck_invoice_adjustments_kind",
                table: "invoice_adjustments",
                sql: "kind in ('sick','discount','withdrawal','late_start')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_invoice_adjustments_kind",
                table: "invoice_adjustments");

            migrationBuilder.AddCheckConstraint(
                name: "ck_invoice_adjustments_kind",
                table: "invoice_adjustments",
                sql: "kind in ('sick','discount','withdrawal')");
        }
    }
}
