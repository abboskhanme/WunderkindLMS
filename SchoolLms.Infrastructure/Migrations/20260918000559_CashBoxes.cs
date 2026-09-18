using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    // ===========================================================================
    //  KASSALAR (cash boxes) — "smena" tushunchasini almashtiradi.
    //  Mijoz javobi (2026-09): "bizni tizimda smena degan tushuncha umuman
    //  bo'lmasin butunlay olib tashla, shunchaki kassa degan narsa bo'lsin
    //  xolos, bizda bir nechta kassa bo'lishi mumkin, ular har bir alohida
    //  pul kirim chiqim qilishi va o'zaro o'tkazma qilishi mumkin."
    // ===========================================================================
    //
    //  `cash_shifts` VA UNING USTUNLARI O'CHIRILMAYDI. Bu migratsiya faqat
    //  `payments.cash_shift_id` ni NOT NULL'dan NULLABLE'ga o'tkazadi — SMENA
    //  ENDI MAJBURIY EMAS, lekin jadval, ustunlar va mavjud qatorlar (haqiqiy
    //  tarix) JOYIDA QOLADI. `CashShiftService`/`CashHandoverService` hamon
    //  kompilyatsiya qilinadi va ishlaydi — ular faqat `PaymentService`/
    //  `ExpenseService` yo'lidan uzilgan (o'sha ikki fayldagi o'zgarish, bu
    //  migratsiyaning ishi emas).
    //
    //  BU YERDA "DROP" SO'ZI IKKI MARTA BOR (`AlterColumn ... DROP NOT
    //  NULL`, so'ng xom SQL `... DROP DEFAULT`) — IKKALASI HAM XAVFSIZ.
    //  Loyihaning "Up() da DROP bo'lmasin" odati MA'LUMOT yo'qotadigan
    //  spurious DROP TABLE/COLUMN haqida (docs/ASSUMPTIONS.md, 2026-09-11:
    //  P1-05 mezoni). Bu yerdagi ikkala operatsiya ham CHEKLOVNI
    //  KENGAYTIRADI — biri majburiy ustunni ixtiyoriy qiladi (xuddi
    //  `StudentsParityP1` migratsiyasidagi `DropCheckConstraint` + kengroq
    //  `AddCheckConstraint` juftligi kabi — o'sha migratsiya ham Up()'da
    //  DROP operatsiyasi bilan boshlanadi, bu loyihada AVVALDAN qabul
    //  qilingan naqsh), ikkinchisi esa Down() EF avtomatik qo'shgan
    //  xavfsizlik <c>DEFAULT</c>'ini (pastga qarang) tozalaydi — ustunda
    //  hech qachon default bo'lmagan bo'lsa ham NO-OP. Hech qanday QATOR
    //  yo'qolmaydi: ustun TURI o'zgarmaydi, faqat cheklovlar bo'shashadi.
    public partial class CashBoxes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "cash_box_id",
                table: "student_refunds",
                type: "uuid",
                nullable: true);

            // XAVFSIZ: faqat NOT NULL cheklovi olib tashlanadi, ustun turi
            // (`uuid`) o'zgarmaydi va mavjud qiymatlar TEGILMAYDI — yuqoridagi
            // fayl sarlavhasi izohiga qarang. Yangi to'lovlar bu ustunni
            // HECH QACHON to'ldirmaydi (`PaymentService` — "smena" endi yo'q);
            // eski qatorlar esa tarix sifatida qoladi.
            migrationBuilder.AlterColumn<Guid>(
                name: "cash_shift_id",
                table: "payments",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            // Down() (pastda) shu ustunni QAYTA "NOT NULL" qilishda XAVFSIZLIK
            // UCHUN `DEFAULT '00000000-...-0000'` beradi (EF'ning o'zi
            // qo'shgan — mavjud NULL qatorlarni yopish uchun, real revert
            // stsenariysida SHART). Agar bu migratsiya avval Down() qilinib,
            // SO'NG qayta Up() qilinsa (aynan CashBoxesMigrationTests shuni
            // sinaydi), o'sha DEFAULT ustunda QOLIB KETARDI va yangi
            // to'lovlar `cash_shift_id` ni tashlab ketganda jimgina
            // Guid.Empty bilan to'lardi — bu esa FK buzilishi (23503) yoki,
            // undan ham yomoni, "smena" bo'lmagan holda soxta FK qiymati
            // degani. `DROP DEFAULT` bu holatni istisno qiladi; ustunda
            // hech qachon default bo'lmagan bo'lsa ham xavfsiz (no-op).
            migrationBuilder.Sql("ALTER TABLE payments ALTER COLUMN cash_shift_id DROP DEFAULT;");

            migrationBuilder.AddColumn<Guid>(
                name: "cash_box_id",
                table: "payments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "cash_box_id",
                table: "expenses",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "cash_boxes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    name = table.Column<string>(type: "text", nullable: false),
                    responsible_user_id = table.Column<string>(type: "text", nullable: true),
                    is_default = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cash_boxes", x => x.id);
                    table.CheckConstraint("ck_cash_boxes_name", "btrim(name) <> ''");
                    table.ForeignKey(
                        name: "fk_cash_boxes_users_responsible_user_id",
                        column: x => x.responsible_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "cash_box_transactions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    cash_box_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    method = table.Column<string>(type: "text", nullable: false),
                    to_method = table.Column<string>(type: "text", nullable: true),
                    student_id = table.Column<string>(type: "text", nullable: true),
                    note = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    transfer_to_box_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reversal_of = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cash_box_transactions", x => x.id);
                    table.CheckConstraint("ck_cash_box_transactions_amount", "amount > 0");
                    table.CheckConstraint("ck_cash_box_transactions_exchange_methods_differ", "to_method is null or to_method <> method");
                    table.CheckConstraint("ck_cash_box_transactions_exchange_shape", "(kind = 'exchange') = (to_method is not null)");
                    table.CheckConstraint("ck_cash_box_transactions_kind", "kind in ('pay_in','pay_out','transfer','exchange')");
                    table.CheckConstraint("ck_cash_box_transactions_method", "method in ('cash','card','transfer','online')");
                    table.CheckConstraint("ck_cash_box_transactions_reversal_not_self", "reversal_of is null or reversal_of <> id");
                    table.CheckConstraint("ck_cash_box_transactions_status", "status in ('posted','reversal')");
                    table.CheckConstraint("ck_cash_box_transactions_to_method", "to_method is null or to_method in ('cash','card','transfer','online')");
                    table.CheckConstraint("ck_cash_box_transactions_transfer_not_self", "transfer_to_box_id is null or transfer_to_box_id <> cash_box_id");
                    table.CheckConstraint("ck_cash_box_transactions_transfer_shape", "(kind = 'transfer') = (transfer_to_box_id is not null)");
                    table.ForeignKey(
                        name: "fk_cash_box_transactions_cash_box_transactions_reversal_of",
                        column: x => x.reversal_of,
                        principalTable: "cash_box_transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cash_box_transactions_cash_boxes_cash_box_id",
                        column: x => x.cash_box_id,
                        principalTable: "cash_boxes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cash_box_transactions_cash_boxes_transfer_to_box_id",
                        column: x => x.transfer_to_box_id,
                        principalTable: "cash_boxes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cash_box_transactions_students_student_id",
                        column: x => x.student_id,
                        principalTable: "students",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_cash_box_transactions_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_student_refunds_cash_box_id",
                table: "student_refunds",
                column: "cash_box_id");

            migrationBuilder.CreateIndex(
                name: "ix_payments_cash_box_id_receipt_no",
                table: "payments",
                columns: new[] { "cash_box_id", "receipt_no" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_expenses_cash_box_id",
                table: "expenses",
                column: "cash_box_id");

            migrationBuilder.CreateIndex(
                name: "ix_cash_box_transactions_cash_box_id",
                table: "cash_box_transactions",
                column: "cash_box_id");

            migrationBuilder.CreateIndex(
                name: "ix_cash_box_transactions_created_at",
                table: "cash_box_transactions",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_cash_box_transactions_created_by",
                table: "cash_box_transactions",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_cash_box_transactions_reversal_of",
                table: "cash_box_transactions",
                column: "reversal_of",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cash_box_transactions_student_id",
                table: "cash_box_transactions",
                column: "student_id");

            migrationBuilder.CreateIndex(
                name: "ix_cash_box_transactions_transfer_to_box_id",
                table: "cash_box_transactions",
                column: "transfer_to_box_id");

            migrationBuilder.CreateIndex(
                name: "ix_cash_boxes_is_active",
                table: "cash_boxes",
                column: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_cash_boxes_responsible_user_id",
                table: "cash_boxes",
                column: "responsible_user_id");

            migrationBuilder.CreateIndex(
                name: "ux_cash_boxes_one_default",
                table: "cash_boxes",
                column: "is_default",
                unique: true,
                filter: "is_default");

            migrationBuilder.AddForeignKey(
                name: "fk_expenses_cash_boxes_cash_box_id",
                table: "expenses",
                column: "cash_box_id",
                principalTable: "cash_boxes",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_payments_cash_boxes_cash_box_id",
                table: "payments",
                column: "cash_box_id",
                principalTable: "cash_boxes",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_student_refunds_cash_boxes_cash_box_id",
                table: "student_refunds",
                column: "cash_box_id",
                principalTable: "cash_boxes",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // ---- Sukut (default) kassa — barqaror id, boshqa seed'lar
            // (billing_seed.sql dagi FeeCategory'lar, BillingSettings.SingletonId)
            // bilan bir xil naqsh. `PaymentService`/`ExpenseService` cashBoxId
            // ko'rsatilmagan har bir amalda AYNAN shu qatorga tayanadi
            // (`CashBoxService.DefaultBoxIdAsync` — `is_default = true` bo'yicha
            // qidiradi, id'ning o'zi bilan emas, lekin qator albatta mavjud
            // bo'lishi SHART — bo'sh bazada "sukut kassa yo'q" degan xato
            // BIRINCHI to'lovdayoq chiqardi).
            migrationBuilder.Sql(
                "INSERT INTO cash_boxes (id, name, responsible_user_id, is_default, is_active, created_at) "
                + "VALUES ('00000000-0000-0000-0000-0000000000cb', 'Asosiy kassa', NULL, true, true, now());");

            // ---- Baza darajasidagi qulflar ----
            // TARTIB MUHIM: eng oxirida, jadvallar mavjud bo'lgandan keyin —
            // GRANT ham, REVOKE ham mavjud jadvalni talab qiladi.
            //
            // Ichida nima bor va NEGA — `cash_boxes_guards.sql` ning o'zida:
            //   1) `cash_boxes` — DELETE yopiladi, UPDATE ochiq qoladi
            //      (rename/deactivate/mas'ul/sukut o'zgartirish uchun);
            //   2) `cash_box_transactions` — FAQAT SELECT+INSERT (SPEC §4.1,
            //      `payments`/`ledger_entries` bilan bir xil qoida).
            migrationBuilder.Sql(MigrationSql.Read("cash_boxes_guards.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_expenses_cash_boxes_cash_box_id",
                table: "expenses");

            migrationBuilder.DropForeignKey(
                name: "fk_payments_cash_boxes_cash_box_id",
                table: "payments");

            migrationBuilder.DropForeignKey(
                name: "fk_student_refunds_cash_boxes_cash_box_id",
                table: "student_refunds");

            migrationBuilder.DropTable(
                name: "cash_box_transactions");

            migrationBuilder.DropTable(
                name: "cash_boxes");

            migrationBuilder.DropIndex(
                name: "ix_student_refunds_cash_box_id",
                table: "student_refunds");

            migrationBuilder.DropIndex(
                name: "ix_payments_cash_box_id_receipt_no",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "ix_expenses_cash_box_id",
                table: "expenses");

            migrationBuilder.DropColumn(
                name: "cash_box_id",
                table: "student_refunds");

            migrationBuilder.DropColumn(
                name: "cash_box_id",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "cash_box_id",
                table: "expenses");

            // DIQQAT — bu ALTER faqat BO'SH yoki hali kassa bilan
            // ISHLATILMAGAN bazada muvaffaqiyatli bo'ladi. Agar bu migratsiya
            // qo'llangandan keyin haqiqiy to'lov yozilgan bo'lsa (yangi
            // to'lovlarda `cash_shift_id` HAR DOIM null — Up() dagi izoh),
            // quyidagi `SET NOT NULL` PostgreSQL tomonidan 23502 bilan rad
            // etiladi. Bu KUTILGAN: "smena" bilan ishlagan tarixni orqaga
            // qaytarib bo'lmaydi — qaysi to'lov qaysi smenaga tegishli
            // bo'lishi kerakligini hech kim bila olmaydi. Migratsiya
            // testlari (`CashBoxesMigrationTests`) Down()'ni FAQAT bo'sh
            // bazada tekshiradi — bu boshqa migratsiyalar bilan bir xil naqsh.
            migrationBuilder.AlterColumn<Guid>(
                name: "cash_shift_id",
                table: "payments",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
