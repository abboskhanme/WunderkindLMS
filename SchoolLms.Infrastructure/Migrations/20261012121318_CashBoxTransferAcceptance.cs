using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// A cash-box transfer waits for the receiving box's responsible person (client, 2026-10-12): the
    /// accountant accepts what the cashier sends, or returns it. <c>needs_acceptance</c> is set once at
    /// INSERT (existing rows stay <c>false</c> — they took effect long ago); the answer is an insert-only
    /// <c>cash_box_transfer_decisions</c> row, one per transfer. No data changes.
    /// </summary>
    public partial class CashBoxTransferAcceptance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "needs_acceptance",
                table: "cash_box_transactions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "cash_box_transfer_decisions",
                columns: table => new
                {
                    transfer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    decision = table.Column<string>(type: "text", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    decided_by = table.Column<string>(type: "text", nullable: false),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cash_box_transfer_decisions", x => x.transfer_id);
                    table.CheckConstraint("ck_cash_box_transfer_decisions_decision", "decision in ('accepted','returned')");
                    table.CheckConstraint("ck_cash_box_transfer_decisions_return_note", "decision <> 'returned' or btrim(coalesce(note, '')) <> ''");
                    table.ForeignKey(
                        name: "fk_cash_box_transfer_decisions_cash_box_transactions_transfer_",
                        column: x => x.transfer_id,
                        principalTable: "cash_box_transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cash_box_transfer_decisions_users_decided_by",
                        column: x => x.decided_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_cash_box_transactions_acceptance_shape",
                table: "cash_box_transactions",
                sql: "not needs_acceptance or (kind = 'transfer' and reversal_of is null)");

            migrationBuilder.CreateIndex(
                name: "ix_cash_box_transfer_decisions_decided_by",
                table: "cash_box_transfer_decisions",
                column: "decided_by");

            // app_rw: the answer is a money record — insert-only, like the transfer.
            migrationBuilder.Sql(MigrationSql.Read("cash_box_transfer_decisions_guards.sql"));
            // app_ro: the MCP journal tool reads a transfer's status.
            migrationBuilder.Sql(MigrationSql.Read("mcp_guards.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cash_box_transfer_decisions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_cash_box_transactions_acceptance_shape",
                table: "cash_box_transactions");

            migrationBuilder.DropColumn(
                name: "needs_acceptance",
                table: "cash_box_transactions");
        }
    }
}
