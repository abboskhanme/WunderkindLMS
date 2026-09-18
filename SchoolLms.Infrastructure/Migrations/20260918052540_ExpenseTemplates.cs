using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ExpenseTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "expense_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    name = table.Column<string>(type: "text", nullable: false),
                    category = table.Column<string>(type: "text", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    day_of_month = table.Column<short>(type: "smallint", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_expense_templates", x => x.id);
                    table.CheckConstraint("ck_expense_templates_amount", "amount > 0");
                    table.CheckConstraint("ck_expense_templates_day_of_month", "day_of_month between 1 and 28");
                    table.CheckConstraint("ck_expense_templates_name", "btrim(name) <> ''");
                });

            migrationBuilder.CreateIndex(
                name: "ix_expense_templates_is_active_day_of_month",
                table: "expense_templates",
                columns: new[] { "is_active", "day_of_month" });

            // ---- `app_rw` grantlari ----
            // ENG OXIRIDA — jadval mavjud bo'lgandan keyin. Ichida nima bor va
            // NEGA — `expense_templates_guards.sql` ning o'zida: to'liq CRUD
            // (SPEC §4.1 emas — bu jadval moliyaviy emas, ExpenseTemplates.cs
            // boshidagi izoh).
            migrationBuilder.Sql(MigrationSql.Read("expense_templates_guards.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Grantlar uchun alohida REVOKE KERAK EMAS: jadvalning o'zi
            // o'chirildi, u bilan birga jadval darajasidagi huquqlar ham
            // yo'qoladi (`finance_parity_guards.sql` / `FinanceParityBatchA.Down()`
            // dagi bilan bir xil qoida).
            migrationBuilder.DropTable(
                name: "expense_templates");
        }
    }
}
