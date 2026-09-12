using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <summary>
    /// Chiqim uchun ikki qavatli nazorat chegarasi — SPEC §4.5.
    ///
    /// <para>
    /// `billing_settings` ga BITTA ustun qo'shadi:
    /// <c>expense_approval_threshold numeric(14,2) not null default 5000000</c>.
    /// Shu summadan KATTA chiqim ikkinchi, boshqa shaxsning tasdig'isiz jurnalga
    /// tushmaydi. Qiymat mijoz javobidan (docs/TASKS.md §8 Q16: 5 000 000 so'm)
    /// va sozlama bo'lib qoladi — keyingi o'zgarish <c>UPDATE</c>, migratsiya emas.
    /// </para>
    ///
    /// <para>
    /// FAQAT QO'SHADI. <c>Up()</c> da birorta ham DROP yo'q (qo'lda o'qib
    /// tekshirilgan). <c>default</c> berilgani uchun mavjud yagona sozlama
    /// qatori ham darhol to'g'ri chegarani oladi — alohida UPDATE kerak emas.
    /// </para>
    ///
    /// <para>
    /// <b>YANGI JADVAL YO'Q</b>, ya'ni <c>deploy/init-roles.sql</c> ni qayta
    /// ishga tushirish SHART EMAS: ustun jadvalning mavjud GRANT'larini meros
    /// qilib oladi va <c>billing_settings</c> allaqachon <c>app_rw</c> ga to'liq
    /// CRUD bilan berilgan (Migrations/Sql/billing_guards.sql).
    /// </para>
    ///
    /// <para>
    /// "Tasdiqlovchi yaratuvchidan boshqa shaxs" qoidasi bu yerda YO'Q, chunki u
    /// allaqachon bor: <c>ck_expenses_approver_differs</c> (P1-05, BillingCore).
    /// </para>
    /// </summary>
    public partial class ExpenseApprovalThreshold : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "expense_approval_threshold",
                table: "billing_settings",
                type: "numeric(14,2)",
                precision: 14,
                scale: 2,
                nullable: false,
                defaultValue: 5000000m);

            migrationBuilder.AddCheckConstraint(
                name: "ck_billing_settings_expense_threshold",
                table: "billing_settings",
                sql: "expense_approval_threshold >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_billing_settings_expense_threshold",
                table: "billing_settings");

            migrationBuilder.DropColumn(
                name: "expense_approval_threshold",
                table: "billing_settings");
        }
    }
}
