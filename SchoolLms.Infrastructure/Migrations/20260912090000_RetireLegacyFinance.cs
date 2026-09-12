using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <summary>
    /// Eski moliya yo'lini olib tashlaydi — SPEC §3.7, docs/TASKS.md §4. Vazifa: P1-21.
    ///
    /// <para>
    /// <b>BU MIGRATSIYA BUZG'UNCHI (destructive) VA QAYTARIB BO'LMAYDIGAN.</b>
    /// <c>Down()</c> jadval va ustunlarni QAYTA YARATADI, lekin ULARDAGI
    /// MA'LUMOTNI EMAS. Bu ataylab: yo'qolgan pul qatorini bo'sh jadval bilan
    /// almashtirish "rollback ishladi" degan yolg'on tuyg'u berardi. Haqiqiy
    /// orqaga qaytish — <c>pg_dump</c> dan tiklash (docs/TASKS.md §4.4), va
    /// nusxa migratsiyadan OLDIN olinishi shart.
    /// </para>
    ///
    /// <para>
    /// <b>Nega ma'lumot ko'chirilmaydi.</b> docs/TASKS.md §4: bu jadvallarda
    /// HAQIQIY ma'lumot yo'q — faqat <c>tools/seed_demo.py</c> yaratgan demo
    /// qatorlar. Mijoz javobi ham shuni tasdiqlaydi (SPEC §8.1 Q9/Q17: "noldan
    /// boshlanadi, ochilish qoldiqlari ko'chirilmaydi"). Shuning uchun ma'lumot
    /// migratsiyasi YOZILMAGAN va yozilmasligi kerak.
    /// </para>
    ///
    /// <para>
    /// <b>Nima o'chadi va o'rniga nima keladi</b> (docs/TASKS.md §4.2):
    /// </para>
    /// <list type="bullet">
    ///   <item><c>monthly_charges</c> → <c>invoices</c> (o'quvchi × TOIFA × oy)</item>
    ///   <item><c>finance_transactions</c> → <c>payments</c> +
    ///         <c>payment_allocations</c> + <c>expenses</c> + <c>ledger_entries</c></item>
    ///   <item><c>students.balance</c> → hisoblanadi: <c>StudentBalanceQuery</c></item>
    ///   <item><c>students.discount_*</c> → <c>discounts</c> (direktor tasdig'i bilan, SPEC §8.1 Q5)</item>
    /// </list>
    ///
    /// <para>
    /// <b>Bitta QO'SHIMCHA ham bor:</b> <c>expenses.teacher_id</c>. Maosh endi
    /// <c>expenses</c> ga yoziladi va "falonchi falon oyda qancha oldi" degan
    /// savolga javob beradigan yagona bog'lanish shu ustun —
    /// <c>finance_transactions.teacher_id</c> ning o'rnini bosadi
    /// (<c>SalaryPaymentQuery</c>). U <c>nullable</c>: boshqa toifadagi chiqimda
    /// o'qituvchi bo'lmaydi, buni <c>ck_expenses_teacher_only_salary</c> kafolatlaydi.
    /// </para>
    ///
    /// <para>
    /// <b>GRANT holati.</b> Yangi JADVAL yaratilmayapti, shuning uchun
    /// <c>Migrations/Sql/billing_guards.sql</c> ni qayta ishga tushirish SHART EMAS:
    /// yangi ustun jadvalning mavjud huquqlarini meros qiladi va <c>expenses</c>
    /// allaqachon <c>app_rw</c> ga to'liq CRUD bilan berilgan. O'chirilayotgan
    /// jadvallarning huquqlari ular bilan birga ketadi.
    /// </para>
    /// </summary>
    public partial class RetireLegacyFinance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---- 1. Maosh chiqimini o'qituvchiga bog'lash (QO'SHISH) ----
            migrationBuilder.AddColumn<string>(
                name: "teacher_id",
                table: "expenses",
                type: "text",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_expenses_teacher_only_salary",
                table: "expenses",
                sql: "teacher_id is null or category = 'salary'");

            migrationBuilder.CreateIndex(
                name: "ix_expenses_teacher_id_on_date",
                table: "expenses",
                columns: new[] { "teacher_id", "on_date" });

            // To'langan maosh moliyaviy tarix — o'qituvchi qatori bilan birga
            // o'chib ketmasin (RESTRICT). O'qituvchi arxivlanadi, o'chirilmaydi.
            migrationBuilder.AddForeignKey(
                name: "fk_expenses_teachers_teacher_id",
                table: "expenses",
                column: "teacher_id",
                principalTable: "teachers",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // ---- 2. Eski yassi moliya jadvallari (O'CHIRISH) ----
            migrationBuilder.DropTable(
                name: "finance_transactions");

            migrationBuilder.DropTable(
                name: "monthly_charges");

            // ---- 3. O'quvchi qatoridagi pul ustunlari (O'CHIRISH) ----
            migrationBuilder.DropColumn(
                name: "balance",
                table: "students");

            migrationBuilder.DropColumn(
                name: "discount_amount",
                table: "students");

            migrationBuilder.DropColumn(
                name: "discount_note",
                table: "students");

            migrationBuilder.DropColumn(
                name: "discount_pct",
                table: "students");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // DIQQAT: bu faqat SXEMANI qaytaradi, MA'LUMOTNI emas — sinf
            // izohidagi ogohlantirishga qarang.
            migrationBuilder.DropForeignKey(
                name: "fk_expenses_teachers_teacher_id",
                table: "expenses");

            migrationBuilder.DropIndex(
                name: "ix_expenses_teacher_id_on_date",
                table: "expenses");

            migrationBuilder.DropCheckConstraint(
                name: "ck_expenses_teacher_only_salary",
                table: "expenses");

            migrationBuilder.DropColumn(
                name: "teacher_id",
                table: "expenses");

            migrationBuilder.AddColumn<decimal>(
                name: "balance",
                table: "students",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "discount_amount",
                table: "students",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "discount_note",
                table: "students",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "discount_pct",
                table: "students",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateTable(
                name: "finance_transactions",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    category = table.Column<string>(type: "text", nullable: false),
                    date = table.Column<string>(type: "text", nullable: false),
                    direction = table.Column<string>(type: "text", nullable: false),
                    month = table.Column<string>(type: "text", nullable: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    student_id = table.Column<string>(type: "text", nullable: true),
                    teacher_id = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_finance_transactions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "monthly_charges",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    date = table.Column<string>(type: "text", nullable: false),
                    discount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    month = table.Column<string>(type: "text", nullable: false),
                    student_id = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_monthly_charges", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_finance_transactions_date",
                table: "finance_transactions",
                column: "date");

            migrationBuilder.CreateIndex(
                name: "ix_monthly_charges_student_id_month",
                table: "monthly_charges",
                columns: new[] { "student_id", "month" },
                unique: true);
        }
    }
}
