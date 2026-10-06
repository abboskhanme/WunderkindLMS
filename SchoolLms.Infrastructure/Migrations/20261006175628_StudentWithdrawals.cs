using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class StudentWithdrawals : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_payment_allocations_amount",
                table: "payment_allocations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_invoice_adjustments_kind",
                table: "invoice_adjustments");

            migrationBuilder.AddColumn<string>(
                name: "kind",
                table: "payment_allocations",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "normal");

            migrationBuilder.AddColumn<Guid>(
                name: "withdrawal_id",
                table: "payment_allocations",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "student_withdrawals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<string>(type: "text", nullable: false),
                    last_day = table.Column<DateOnly>(type: "date", nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    written_off = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    released = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    details = table.Column<string>(type: "jsonb", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_student_withdrawals", x => x.id);
                    table.CheckConstraint("ck_student_withdrawals_amounts", "written_off >= 0 and released >= 0");
                    table.CheckConstraint("ck_student_withdrawals_cancel", "(cancelled_at is null) = (cancelled_by is null)");
                    table.ForeignKey(
                        name: "fk_student_withdrawals_students_student_id",
                        column: x => x.student_id,
                        principalTable: "students",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_student_withdrawals_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_payment_allocations_withdrawal_id",
                table: "payment_allocations",
                column: "withdrawal_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payment_allocations_amount",
                table: "payment_allocations",
                sql: "(kind = 'normal' and amount > 0) or (kind = 'release' and amount < 0 and withdrawal_id is not null)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_invoice_adjustments_kind",
                table: "invoice_adjustments",
                sql: "kind in ('sick','discount','withdrawal')");

            migrationBuilder.CreateIndex(
                name: "ix_student_withdrawals_created_by",
                table: "student_withdrawals",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_student_withdrawals_student_id",
                table: "student_withdrawals",
                column: "student_id");

            migrationBuilder.AddForeignKey(
                name: "fk_payment_allocations_student_withdrawals_withdrawal_id",
                table: "payment_allocations",
                column: "withdrawal_id",
                principalTable: "student_withdrawals",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_payment_allocations_student_withdrawals_withdrawal_id",
                table: "payment_allocations");

            migrationBuilder.DropTable(
                name: "student_withdrawals");

            migrationBuilder.DropIndex(
                name: "ix_payment_allocations_withdrawal_id",
                table: "payment_allocations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payment_allocations_amount",
                table: "payment_allocations");

            migrationBuilder.DropCheckConstraint(
                name: "ck_invoice_adjustments_kind",
                table: "invoice_adjustments");

            migrationBuilder.DropColumn(
                name: "kind",
                table: "payment_allocations");

            migrationBuilder.DropColumn(
                name: "withdrawal_id",
                table: "payment_allocations");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payment_allocations_amount",
                table: "payment_allocations",
                sql: "amount > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_invoice_adjustments_kind",
                table: "invoice_adjustments",
                sql: "kind in ('sick','discount')");
        }
    }
}
