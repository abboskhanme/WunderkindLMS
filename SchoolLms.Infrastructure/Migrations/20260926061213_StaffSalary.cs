using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class StaffSalary : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "phone",
                table: "users",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "salary",
                table: "users",
                type: "numeric(14,2)",
                precision: 14,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "salary_start_date",
                table: "users",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "employee_user_id",
                table: "expenses",
                type: "text",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_users_salary",
                table: "users",
                sql: "salary >= 0");

            migrationBuilder.CreateIndex(
                name: "ix_expenses_employee_user_id_on_date",
                table: "expenses",
                columns: new[] { "employee_user_id", "on_date" });

            migrationBuilder.AddCheckConstraint(
                name: "ck_expenses_employee_only_salary",
                table: "expenses",
                sql: "employee_user_id is null or category = 'salary'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_expenses_one_salary_recipient",
                table: "expenses",
                sql: "num_nonnulls(teacher_id, employee_user_id) <= 1");

            migrationBuilder.AddForeignKey(
                name: "fk_expenses_users_employee_user_id",
                table: "expenses",
                column: "employee_user_id",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_expenses_users_employee_user_id",
                table: "expenses");

            migrationBuilder.DropCheckConstraint(
                name: "ck_users_salary",
                table: "users");

            migrationBuilder.DropIndex(
                name: "ix_expenses_employee_user_id_on_date",
                table: "expenses");

            migrationBuilder.DropCheckConstraint(
                name: "ck_expenses_employee_only_salary",
                table: "expenses");

            migrationBuilder.DropCheckConstraint(
                name: "ck_expenses_one_salary_recipient",
                table: "expenses");

            migrationBuilder.DropColumn(
                name: "phone",
                table: "users");

            migrationBuilder.DropColumn(
                name: "salary",
                table: "users");

            migrationBuilder.DropColumn(
                name: "salary_start_date",
                table: "users");

            migrationBuilder.DropColumn(
                name: "employee_user_id",
                table: "expenses");
        }
    }
}
