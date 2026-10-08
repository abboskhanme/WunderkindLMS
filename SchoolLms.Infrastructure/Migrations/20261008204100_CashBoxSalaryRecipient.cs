using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CashBoxSalaryRecipient : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "employee_user_id",
                table: "cash_box_transactions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "teacher_id",
                table: "cash_box_transactions",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_cash_box_transactions_employee_user_id",
                table: "cash_box_transactions",
                column: "employee_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_cash_box_transactions_teacher_id",
                table: "cash_box_transactions",
                column: "teacher_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_cash_box_transactions_one_recipient",
                table: "cash_box_transactions",
                sql: "num_nonnulls(teacher_id, employee_user_id) <= 1");

            migrationBuilder.AddCheckConstraint(
                name: "ck_cash_box_transactions_recipient_pay_out",
                table: "cash_box_transactions",
                sql: "(teacher_id is null and employee_user_id is null) or kind = 'pay_out'");

            migrationBuilder.AddForeignKey(
                name: "fk_cash_box_transactions_teachers_teacher_id",
                table: "cash_box_transactions",
                column: "teacher_id",
                principalTable: "teachers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_cash_box_transactions_users_employee_user_id",
                table: "cash_box_transactions",
                column: "employee_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_cash_box_transactions_teachers_teacher_id",
                table: "cash_box_transactions");

            migrationBuilder.DropForeignKey(
                name: "fk_cash_box_transactions_users_employee_user_id",
                table: "cash_box_transactions");

            migrationBuilder.DropIndex(
                name: "ix_cash_box_transactions_employee_user_id",
                table: "cash_box_transactions");

            migrationBuilder.DropIndex(
                name: "ix_cash_box_transactions_teacher_id",
                table: "cash_box_transactions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_cash_box_transactions_one_recipient",
                table: "cash_box_transactions");

            migrationBuilder.DropCheckConstraint(
                name: "ck_cash_box_transactions_recipient_pay_out",
                table: "cash_box_transactions");

            migrationBuilder.DropColumn(
                name: "employee_user_id",
                table: "cash_box_transactions");

            migrationBuilder.DropColumn(
                name: "teacher_id",
                table: "cash_box_transactions");
        }
    }
}
