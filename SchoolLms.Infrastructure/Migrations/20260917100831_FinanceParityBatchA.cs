using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class FinanceParityBatchA : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "cash_shift_id",
                table: "expenses",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "cash_handovers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    cash_shift_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    destination = table.Column<string>(type: "text", nullable: false),
                    note = table.Column<string>(type: "text", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    reversal_of = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cash_handovers", x => x.id);
                    table.CheckConstraint("ck_cash_handovers_amount", "amount > 0");
                    table.CheckConstraint("ck_cash_handovers_destination", "destination in ('bank','safe')");
                    table.CheckConstraint("ck_cash_handovers_reversal_not_self", "reversal_of is null or reversal_of <> id");
                    table.ForeignKey(
                        name: "fk_cash_handovers_cash_handovers_reversal_of",
                        column: x => x.reversal_of,
                        principalTable: "cash_handovers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cash_handovers_cash_shifts_cash_shift_id",
                        column: x => x.cash_shift_id,
                        principalTable: "cash_shifts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cash_handovers_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "expense_attachments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    expense_id = table.Column<Guid>(type: "uuid", nullable: false),
                    file_url = table.Column<string>(type: "text", nullable: false),
                    file_name = table.Column<string>(type: "text", nullable: false),
                    content_type = table.Column<string>(type: "text", nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    uploaded_by = table.Column<string>(type: "text", nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_expense_attachments", x => x.id);
                    table.CheckConstraint("ck_expense_attachments_size", "size_bytes > 0");
                    table.ForeignKey(
                        name: "fk_expense_attachments_expenses_expense_id",
                        column: x => x.expense_id,
                        principalTable: "expenses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_expense_attachments_users_uploaded_by",
                        column: x => x.uploaded_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "student_refunds",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    student_id = table.Column<string>(type: "text", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    method = table.Column<string>(type: "text", nullable: false),
                    reason = table.Column<string>(type: "text", nullable: false),
                    requested_by = table.Column<string>(type: "text", nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    approved_by = table.Column<string>(type: "text", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cash_shift_id = table.Column<Guid>(type: "uuid", nullable: true),
                    rejected_reason = table.Column<string>(type: "text", nullable: true),
                    reversal_of = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_student_refunds", x => x.id);
                    table.CheckConstraint("ck_student_refunds_amount", "amount > 0");
                    table.CheckConstraint("ck_student_refunds_approver_differs", "approved_by is null or approved_by <> requested_by");
                    table.CheckConstraint("ck_student_refunds_cash_shift", "approved_by is null or method <> 'cash' or cash_shift_id is not null");
                    table.CheckConstraint("ck_student_refunds_method", "method in ('cash','card','transfer','online')");
                    table.CheckConstraint("ck_student_refunds_reason", "btrim(reason) <> ''");
                    table.ForeignKey(
                        name: "fk_student_refunds_cash_shifts_cash_shift_id",
                        column: x => x.cash_shift_id,
                        principalTable: "cash_shifts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_student_refunds_student_refunds_reversal_of",
                        column: x => x.reversal_of,
                        principalTable: "student_refunds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_student_refunds_students_student_id",
                        column: x => x.student_id,
                        principalTable: "students",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_student_refunds_users_approved_by",
                        column: x => x.approved_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_student_refunds_users_requested_by",
                        column: x => x.requested_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_expenses_cash_shift_id",
                table: "expenses",
                column: "cash_shift_id");

            migrationBuilder.CreateIndex(
                name: "ix_cash_handovers_cash_shift_id",
                table: "cash_handovers",
                column: "cash_shift_id");

            migrationBuilder.CreateIndex(
                name: "ix_cash_handovers_created_by",
                table: "cash_handovers",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_cash_handovers_reversal_of",
                table: "cash_handovers",
                column: "reversal_of",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_expense_attachments_expense_id",
                table: "expense_attachments",
                column: "expense_id");

            migrationBuilder.CreateIndex(
                name: "ix_expense_attachments_uploaded_by",
                table: "expense_attachments",
                column: "uploaded_by");

            migrationBuilder.CreateIndex(
                name: "ix_student_refunds_approved_by",
                table: "student_refunds",
                column: "approved_by");

            migrationBuilder.CreateIndex(
                name: "ix_student_refunds_cash_shift_id",
                table: "student_refunds",
                column: "cash_shift_id");

            migrationBuilder.CreateIndex(
                name: "ix_student_refunds_requested_by",
                table: "student_refunds",
                column: "requested_by");

            migrationBuilder.CreateIndex(
                name: "ix_student_refunds_reversal_of",
                table: "student_refunds",
                column: "reversal_of",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_student_refunds_student_id",
                table: "student_refunds",
                column: "student_id");

            migrationBuilder.AddForeignKey(
                name: "fk_expenses_cash_shifts_cash_shift_id",
                table: "expenses",
                column: "cash_shift_id",
                principalTable: "cash_shifts",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // ---- Baza darajasidagi qulflar ----
            // TARTIB MUHIM: eng oxirida, jadvallar mavjud bo'lgandan keyin —
            // GRANT ham, trigger ham mavjud jadvalni talab qiladi.
            //
            // Ichida nima bor va NEGA — `finance_parity_guards.sql` ning o'zida:
            //   1) `student_refunds_locked` trigger'i — qaror BIR MARTA yoziladi;
            //   2) `cash_handovers` / `student_refunds` / `expense_attachments`
            //      uchun GRANT SELECT, INSERT + REVOKE UPDATE, DELETE, TRUNCATE;
            //   3) `student_refunds` ning to'rtta "qaror" ustuniga ustun
            //      darajasidagi GRANT UPDATE.
            migrationBuilder.Sql(MigrationSql.Read("finance_parity_guards.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_expenses_cash_shifts_cash_shift_id",
                table: "expenses");

            migrationBuilder.DropTable(
                name: "cash_handovers");

            migrationBuilder.DropTable(
                name: "expense_attachments");

            migrationBuilder.DropTable(
                name: "student_refunds");

            migrationBuilder.DropIndex(
                name: "ix_expenses_cash_shift_id",
                table: "expenses");

            migrationBuilder.DropColumn(
                name: "cash_shift_id",
                table: "expenses");

            // ---- Qulf funksiyasi ----
            // Trigger'ning O'ZI jadval bilan birga ketdi (`DROP TABLE
            // student_refunds` yuqorida), FUNKSIYA esa ketmaydi — u sxema
            // darajasidagi alohida obyekt. Shuning uchun u AYNAN SHU YERDA,
            // jadvaldan KEYIN tushiriladi: teskari tartibda trigger hali
            // funksiyaga bog'liq bo'lib turardi va `DROP FUNCTION` 2BP01
            // (`dependent_objects_still_exist`) bilan yiqilardi.
            //
            // `IF EXISTS` — `app_rw` roli yo'q bazada guards blokining GRANT
            // qismi jim o'tib ketadi, lekin funksiya baribir yaratiladi
            // (u roldan mustaqil); shunga qaramay `Down()` hech qanday
            // holatda yiqilmasligi kerak.
            //
            // Grantlar uchun alohida REVOKE KERAK EMAS: uchala jadval ham
            // o'chirildi, ular bilan birga jadval va ustun darajasidagi
            // huquqlar ham yo'qoldi (`anomaly_guards.sql` dagi bilan bir xil
            // qoida). `expenses` ning huquqlari esa UMUMAN o'zgartirilmagan.
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS student_refunds_lock_decided();");
        }
    }
}
