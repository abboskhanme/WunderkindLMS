using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    // ===========================================================================
    //  Tranzaksiya turi katalogi (Kirim/Chiqim) — mijoz yuborgan EduSchool kassa
    //  kirim shakli ("Tranzaksiya turi *" — majburiy dropdown) va moliya
    //  sozlamalari ekrani (pill-tab, jadval "№ / Nomi / Amallar"), 2026-09-18.
    // ===========================================================================
    //
    //  IKKITA KIND, TO'RTTA EMAS. Mijoz skrinshotida to'rtta pill (Kirim,
    //  Chiqim, Bonus, Jarima) bor. Bonus/Jarima uchun bu katalog ALLAQACHON
    //  mavjud — `adjustment_reasons` (F11.02, `PayrollAdjustments` migratsiyasi),
    //  aynan shu shaklda (kind, name, is_active, position) va aynan shu
    //  vazifada. Uni bu yerda takrorlash ikkita mustaqil, bir-biridan
    //  uzoqlashadigan katalog degani bo'lardi. Batafsil: `TransactionTypes.cs`
    //  (Domain) boshidagi izoh.
    //
    //  `Accounts.cs` GA TEGMAYDI. `docs/modules/existing-module-gaps.md` §3.4:
    //  "Editable transaction-type tree — declined — Accounts.cs is closed on
    //  purpose". Bu yerdagi katalog o'sha qarorni buzmaydi: u faqat kassa
    //  tranzaksiyasiga (`cash_box_transactions.transaction_type_id`)
    //  yopishtiriladigan YORLIQ, hisobot hamon AKKAUNT bo'yicha yig'iladi.
    //
    //  MOLIYAVIY EMAS — TO'LIQ CRUD. Ichida summa yo'q, faqat nom/tartib/
    //  faollik — `expense_templates`/`adjustment_reasons` bilan bir xil naqsh
    //  (SPEC §4.1 bu yerga tegishli emas).
    //
    //  `cash_box_transactions.transaction_type_id` — NULLABLE, YANGI USTUN.
    //  Faqat Kirim shaklida (`CashBoxService.PayInAsync`) ixtiyoriy ravishda
    //  so'raladi; eski qatorlar va chiqim/ko'chirish/ayirboshlash `null` qoladi.
    //  `cash_box_transactions` jadvalining o'zi allaqachon FAQAT SELECT+INSERT
    //  (`cash_boxes_guards.sql`, SPEC §4.1) — bu migratsiya o'sha huquqni
    //  o'zgartirmaydi, yangi ustun avtomatik o'sha huquq ostiga tushadi.
    public partial class TransactionTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "transaction_types",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    kind = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    is_seeded = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    position = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transaction_types", x => x.id);
                    table.CheckConstraint("ck_transaction_types_kind", "kind in ('in','out')");
                    table.CheckConstraint("ck_transaction_types_name", "btrim(name) <> ''");
                });

            migrationBuilder.CreateIndex(
                name: "ix_transaction_types_kind_name",
                table: "transaction_types",
                columns: new[] { "kind", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_transaction_types_kind_is_active_position",
                table: "transaction_types",
                columns: new[] { "kind", "is_active", "position" });

            // XAVFSIZ ADD: nullable ustun, mavjud qatorlarga TA'SIR QILMAYDI —
            // eski tranzaksiyalar (va hozircha chiqim/ko'chirish/ayirboshlash)
            // `null` bilan qoladi (fayl boshidagi izoh).
            migrationBuilder.AddColumn<Guid>(
                name: "transaction_type_id",
                table: "cash_box_transactions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_cash_box_transactions_transaction_type_id",
                table: "cash_box_transactions",
                column: "transaction_type_id");

            migrationBuilder.AddForeignKey(
                name: "fk_cash_box_transactions_transaction_types_transaction_type_id",
                table: "cash_box_transactions",
                column: "transaction_type_id",
                principalTable: "transaction_types",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // ---- Boshlang'ich ma'lumot: mijoz dropdown'i (Kirim) + kichik
            // kassa chiqimlari (Chiqim) — `transaction_types_seed.sql` ning
            // o'zida ID'lar va sabab. ----
            migrationBuilder.Sql(MigrationSql.Read("transaction_types_seed.sql"));

            // ---- `app_rw` grantlari ----
            // ENG OXIRIDA — jadval mavjud bo'lgandan keyin. Ichida nima bor va
            // NEGA — `transaction_types_guards.sql` ning o'zida: to'liq CRUD
            // (bu jadval moliyaviy emas).
            migrationBuilder.Sql(MigrationSql.Read("transaction_types_guards.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_cash_box_transactions_transaction_types_transaction_type_id",
                table: "cash_box_transactions");

            migrationBuilder.DropIndex(
                name: "ix_cash_box_transactions_transaction_type_id",
                table: "cash_box_transactions");

            migrationBuilder.DropColumn(
                name: "transaction_type_id",
                table: "cash_box_transactions");

            // Grantlar uchun alohida REVOKE kerak emas: jadvalning o'zi
            // o'chirilganda uning ustidagi huquqlar ham yo'qoladi
            // (`ExpenseTemplates.Down()` dagi bilan bir xil qoida).
            migrationBuilder.DropTable(
                name: "transaction_types");
        }
    }
}
