using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using SchoolLms.Infrastructure.Data;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <summary>
    /// Chegirmani butunlay o'chirish (mijoz, 2026-10-27): `discounts.status = 'deleted'` va
    /// accrual yozgan chegirmani qaytaruvchi `invoice_adjustments.kind = 'discount_undo'`.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20261027120000_DiscountDelete")]
    public partial class DiscountDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_discounts_status",
                table: "discounts");

            migrationBuilder.AddCheckConstraint(
                name: "ck_discounts_status",
                table: "discounts",
                sql: "status in ('pending','approved','rejected','deleted')");

            migrationBuilder.DropCheckConstraint(
                name: "ck_invoice_adjustments_kind",
                table: "invoice_adjustments");

            migrationBuilder.AddCheckConstraint(
                name: "ck_invoice_adjustments_kind",
                table: "invoice_adjustments",
                sql: "kind in ('sick','discount','withdrawal','late_start','discount_undo')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_discounts_status",
                table: "discounts");

            migrationBuilder.AddCheckConstraint(
                name: "ck_discounts_status",
                table: "discounts",
                sql: "status in ('pending','approved','rejected')");

            migrationBuilder.DropCheckConstraint(
                name: "ck_invoice_adjustments_kind",
                table: "invoice_adjustments");

            migrationBuilder.AddCheckConstraint(
                name: "ck_invoice_adjustments_kind",
                table: "invoice_adjustments",
                sql: "kind in ('sick','discount','withdrawal','late_start')");
        }
    }
}
