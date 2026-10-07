using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <summary>
    /// A voided month can be billed again by a subscription opened or edited after the void
    /// (billing-plans.md §10.3): the invoice unique index skips void rows, <c>invoices.voided_at</c>
    /// tells accrual when the void happened, <c>student_subscriptions.updated_at</c> when the terms changed.
    /// </summary>
    public partial class InvoiceVoidRebill : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_invoices_student_id_category_id_period_month",
                table: "invoices");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "updated_at",
                table: "student_subscriptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "voided_at",
                table: "invoices",
                type: "timestamp with time zone",
                nullable: true);

            // Existing voids: the moment their ledger pair was reversed (reversal rows carry the invoice id).
            // A void with no ledger reversal (nothing was payable) stays NULL — accrual reads NULL as
            // "voided for good", i.e. the old behaviour.
            migrationBuilder.Sql("""
                UPDATE invoices i
                SET voided_at = r.reversed_at
                FROM (SELECT ref_id, MIN(created_at) AS reversed_at
                      FROM ledger_entries
                      WHERE reversal_of IS NOT NULL AND ref_id IS NOT NULL
                      GROUP BY ref_id) r
                WHERE i.status = 'void' AND r.ref_id = i.id;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_invoices_student_id_category_id_period_month",
                table: "invoices",
                columns: new[] { "student_id", "category_id", "period_month" },
                unique: true,
                filter: "status <> 'void'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Fails while a month holds both a void and a live invoice — merge them by hand first.

            migrationBuilder.DropIndex(
                name: "ix_invoices_student_id_category_id_period_month",
                table: "invoices");

            migrationBuilder.DropColumn(
                name: "updated_at",
                table: "student_subscriptions");

            migrationBuilder.DropColumn(
                name: "voided_at",
                table: "invoices");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_student_id_category_id_period_month",
                table: "invoices",
                columns: new[] { "student_id", "category_id", "period_month" },
                unique: true);
        }
    }
}
