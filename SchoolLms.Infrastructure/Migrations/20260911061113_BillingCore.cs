using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <summary>
    /// Moliya (billing) yadrosi — SPEC §3.7 va §4. Vazifa: P1-05.
    ///
    /// <para>
    /// FAQAT QO'SHADI. Eski `finance_transactions` / `monthly_charges` jadvallariga
    /// TEGMAYDI — ular P1-21 gacha tirik qoladi va mavjud moliya sahifasi ishlab turadi.
    /// `Up()` da birorta ham DROP yo'q (qo'lda o'qib tekshirilgan).
    /// </para>
    ///
    /// <para>
    /// EF ifodalay olmaydigan qism xom SQL'da, embedded resource sifatida:
    /// <c>Migrations/Sql/billing_guards.sql</c> (taqsimot trigger'i + `app_rw` GRANT/REVOKE)
    /// va <c>Migrations/Sql/billing_seed.sql</c> (beshta toifa + sozlamalar qatori).
    /// Ikkalasi ham idempotent.
    /// </para>
    /// </summary>
    public partial class BillingCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "billing_settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_due_day = table.Column<int>(type: "integer", nullable: false),
                    overdue_after_day = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_billing_settings", x => x.id);
                    table.CheckConstraint("ck_billing_settings_due_day", "payment_due_day between 1 and 28");
                    table.CheckConstraint("ck_billing_settings_order", "overdue_after_day >= payment_due_day");
                    table.CheckConstraint("ck_billing_settings_overdue_day", "overdue_after_day between 1 and 28");
                    table.ForeignKey(
                        name: "fk_billing_settings_users_updated_by",
                        column: x => x.updated_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "cash_shifts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    cashier_id = table.Column<string>(type: "text", nullable: false),
                    opened_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    opening_float = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    expected_cash = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    counted_cash = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    variance = table.Column<decimal>(type: "numeric(14,2)", nullable: true, computedColumnSql: "counted_cash - expected_cash", stored: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    closed_by = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cash_shifts", x => x.id);
                    table.CheckConstraint("ck_cash_shifts_close_requires_count", "status <> 'closed' or (closed_at is not null and counted_cash is not null and expected_cash is not null)");
                    table.CheckConstraint("ck_cash_shifts_closed_at", "closed_at is null or closed_at >= opened_at");
                    table.CheckConstraint("ck_cash_shifts_opening_float", "opening_float >= 0");
                    table.CheckConstraint("ck_cash_shifts_status", "status in ('open','closed')");
                    table.ForeignKey(
                        name: "fk_cash_shifts_users_cashier_id",
                        column: x => x.cashier_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cash_shifts_users_closed_by",
                        column: x => x.closed_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "expenses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    on_date = table.Column<DateOnly>(type: "date", nullable: false),
                    category = table.Column<string>(type: "text", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    approved_by = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_expenses", x => x.id);
                    table.CheckConstraint("ck_expenses_amount", "amount > 0");
                    table.CheckConstraint("ck_expenses_approver_differs", "approved_by is null or approved_by <> created_by");
                    table.ForeignKey(
                        name: "fk_expenses_users_approved_by",
                        column: x => x.approved_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_expenses_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fee_categories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fee_categories", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ledger_entries",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    entry_date = table.Column<DateOnly>(type: "date", nullable: false),
                    account = table.Column<string>(type: "text", nullable: false),
                    direction = table.Column<string>(type: "text", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    ref_type = table.Column<string>(type: "text", nullable: false),
                    ref_id = table.Column<Guid>(type: "uuid", nullable: true),
                    memo = table.Column<string>(type: "text", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reversal_of = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ledger_entries", x => x.id);
                    table.CheckConstraint("ck_ledger_entries_amount", "amount > 0");
                    table.CheckConstraint("ck_ledger_entries_direction", "direction in ('debit','credit')");
                    table.CheckConstraint("ck_ledger_entries_reversal_not_self", "reversal_of is null or reversal_of <> id");
                    table.ForeignKey(
                        name: "fk_ledger_entries_ledger_entries_reversal_of",
                        column: x => x.reversal_of,
                        principalTable: "ledger_entries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_ledger_entries_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    receipt_no = table.Column<long>(type: "bigint", nullable: false),
                    student_id = table.Column<string>(type: "text", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    method = table.Column<string>(type: "text", nullable: false),
                    cash_shift_id = table.Column<Guid>(type: "uuid", nullable: false),
                    cashier_id = table.Column<string>(type: "text", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reversal_of = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payments", x => x.id);
                    table.CheckConstraint("ck_payments_amount", "amount > 0");
                    table.CheckConstraint("ck_payments_method", "method in ('cash','card','transfer','online')");
                    table.CheckConstraint("ck_payments_receipt_no", "receipt_no > 0");
                    table.CheckConstraint("ck_payments_reversal_not_self", "reversal_of is null or reversal_of <> id");
                    table.ForeignKey(
                        name: "fk_payments_cash_shifts_cash_shift_id",
                        column: x => x.cash_shift_id,
                        principalTable: "cash_shifts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payments_payments_reversal_of",
                        column: x => x.reversal_of,
                        principalTable: "payments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payments_students_student_id",
                        column: x => x.student_id,
                        principalTable: "students",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payments_users_cashier_id",
                        column: x => x.cashier_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "discounts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<string>(type: "text", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false),
                    starts_on = table.Column<DateOnly>(type: "date", nullable: false),
                    ends_on = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    approved_by = table.Column<string>(type: "text", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_discounts", x => x.id);
                    table.CheckConstraint("ck_discounts_amount", "amount >= 0");
                    table.CheckConstraint("ck_discounts_approved_has_approver", "status <> 'approved' or approved_by is not null");
                    table.CheckConstraint("ck_discounts_approver_differs", "approved_by is null or approved_by <> created_by");
                    table.CheckConstraint("ck_discounts_percent", "percent >= 0 and percent <= 100");
                    table.CheckConstraint("ck_discounts_period", "ends_on is null or ends_on >= starts_on");
                    table.CheckConstraint("ck_discounts_status", "status in ('pending','approved','rejected')");
                    table.ForeignKey(
                        name: "fk_discounts_fee_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "fee_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_discounts_students_student_id",
                        column: x => x.student_id,
                        principalTable: "students",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_discounts_users_approved_by",
                        column: x => x.approved_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_discounts_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "invoices",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<string>(type: "text", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_month = table.Column<DateOnly>(type: "date", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    discount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    due_on = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoices", x => x.id);
                    table.CheckConstraint("ck_invoices_amount", "amount >= 0");
                    table.CheckConstraint("ck_invoices_discount", "discount >= 0 and discount <= amount");
                    table.CheckConstraint("ck_invoices_period_first_day", "extract(day from period_month) = 1");
                    table.CheckConstraint("ck_invoices_status", "status in ('open','partial','paid','void')");
                    table.ForeignKey(
                        name: "fk_invoices_fee_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "fee_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_invoices_students_student_id",
                        column: x => x.student_id,
                        principalTable: "students",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "student_subscriptions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<string>(type: "text", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    monthly_amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    detail = table.Column<string>(type: "text", nullable: true),
                    starts_on = table.Column<DateOnly>(type: "date", nullable: false),
                    ends_on = table.Column<DateOnly>(type: "date", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_student_subscriptions", x => x.id);
                    table.CheckConstraint("ck_student_subscriptions_amount", "monthly_amount >= 0");
                    table.CheckConstraint("ck_student_subscriptions_period", "ends_on is null or ends_on >= starts_on");
                    table.ForeignKey(
                        name: "fk_student_subscriptions_fee_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "fee_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_student_subscriptions_students_student_id",
                        column: x => x.student_id,
                        principalTable: "students",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_student_subscriptions_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payment_allocations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_allocations", x => x.id);
                    table.CheckConstraint("ck_payment_allocations_amount", "amount > 0");
                    table.ForeignKey(
                        name: "fk_payment_allocations_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payment_allocations_payments_payment_id",
                        column: x => x.payment_id,
                        principalTable: "payments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_billing_settings_updated_by",
                table: "billing_settings",
                column: "updated_by");

            migrationBuilder.CreateIndex(
                name: "ix_cash_shifts_closed_by",
                table: "cash_shifts",
                column: "closed_by");

            migrationBuilder.CreateIndex(
                name: "ix_cash_shifts_opened_at",
                table: "cash_shifts",
                column: "opened_at");

            migrationBuilder.CreateIndex(
                name: "ux_cash_shifts_one_open_per_cashier",
                table: "cash_shifts",
                column: "cashier_id",
                unique: true,
                filter: "status = 'open'");

            migrationBuilder.CreateIndex(
                name: "ix_discounts_approved_by",
                table: "discounts",
                column: "approved_by");

            migrationBuilder.CreateIndex(
                name: "ix_discounts_category_id",
                table: "discounts",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_discounts_created_by",
                table: "discounts",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_discounts_status",
                table: "discounts",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_discounts_student_id_status",
                table: "discounts",
                columns: new[] { "student_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_expenses_approved_by",
                table: "expenses",
                column: "approved_by");

            migrationBuilder.CreateIndex(
                name: "ix_expenses_category_on_date",
                table: "expenses",
                columns: new[] { "category", "on_date" });

            migrationBuilder.CreateIndex(
                name: "ix_expenses_created_by",
                table: "expenses",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_expenses_on_date",
                table: "expenses",
                column: "on_date");

            migrationBuilder.CreateIndex(
                name: "ix_fee_categories_code",
                table: "fee_categories",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_invoices_category_id",
                table: "invoices",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_period_month",
                table: "invoices",
                column: "period_month");

            migrationBuilder.CreateIndex(
                name: "ix_invoices_status_due_on",
                table: "invoices",
                columns: new[] { "status", "due_on" });

            migrationBuilder.CreateIndex(
                name: "ix_invoices_student_id_category_id_period_month",
                table: "invoices",
                columns: new[] { "student_id", "category_id", "period_month" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_created_by",
                table: "ledger_entries",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_ref_type_ref_id",
                table: "ledger_entries",
                columns: new[] { "ref_type", "ref_id" });

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_reversal_of",
                table: "ledger_entries",
                column: "reversal_of");

            migrationBuilder.CreateIndex(
                name: "ledger_entries_date_account",
                table: "ledger_entries",
                columns: new[] { "entry_date", "account" });

            migrationBuilder.CreateIndex(
                name: "ix_payment_allocations_invoice_id",
                table: "payment_allocations",
                column: "invoice_id");

            migrationBuilder.CreateIndex(
                name: "ix_payment_allocations_payment_id",
                table: "payment_allocations",
                column: "payment_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_cash_shift_id_receipt_no",
                table: "payments",
                columns: new[] { "cash_shift_id", "receipt_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payments_cashier_id_received_at",
                table: "payments",
                columns: new[] { "cashier_id", "received_at" });

            migrationBuilder.CreateIndex(
                name: "ix_payments_reversal_of",
                table: "payments",
                column: "reversal_of",
                unique: true,
                filter: "reversal_of is not null");

            migrationBuilder.CreateIndex(
                name: "ix_payments_student_id_received_at",
                table: "payments",
                columns: new[] { "student_id", "received_at" });

            migrationBuilder.CreateIndex(
                name: "ix_student_subscriptions_category_id",
                table: "student_subscriptions",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_student_subscriptions_created_by",
                table: "student_subscriptions",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_student_subscriptions_student_id_category_id_starts_on",
                table: "student_subscriptions",
                columns: new[] { "student_id", "category_id", "starts_on" });

            // ---- Baza darajasidagi qulflar ----
            // TARTIB MUHIM: avval jadvallar (yuqorida), keyin trigger va grantlar.
            // `billing_guards.sql` dagi GRANT/REVOKE aynan shu migratsiya yaratgan
            // jadvallarga tegishli: `ALTER DEFAULT PRIVILEGES` ularni `app_rw` ga
            // to'liq CRUD bilan topshiradi, biz esa UPDATE/DELETE ni darhol qaytarib
            // olamiz — himoya migratsiya bilan birga keladi, unutib bo'lmaydi.
            migrationBuilder.Sql(MigrationSql.Read("billing_guards.sql"));

            // Ma'lumotnoma: beshta toifa va yagona sozlamalar qatori.
            migrationBuilder.Sql(MigrationSql.Read("billing_seed.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Trigger funksiyasi jadvaldan mustaqil — DropTable uni olib ketmaydi.
            // Qolgan hamma narsa (grantlar, seed qatorlari) jadval bilan birga ketadi.
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS check_allocation_total();");

            migrationBuilder.DropTable(
                name: "billing_settings");

            migrationBuilder.DropTable(
                name: "discounts");

            migrationBuilder.DropTable(
                name: "expenses");

            migrationBuilder.DropTable(
                name: "ledger_entries");

            migrationBuilder.DropTable(
                name: "payment_allocations");

            migrationBuilder.DropTable(
                name: "student_subscriptions");

            migrationBuilder.DropTable(
                name: "invoices");

            migrationBuilder.DropTable(
                name: "payments");

            migrationBuilder.DropTable(
                name: "fee_categories");

            migrationBuilder.DropTable(
                name: "cash_shifts");
        }
    }
}
