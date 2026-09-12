using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <summary>
    /// Tungi tekshiruv bayroqlari va <c>audit_log</c> ning <c>jsonb</c> ustunlari —
    /// SPEC §4.6. Vazifa: P1-14.
    ///
    /// <para>
    /// FAQAT QO'SHADI. <c>Up()</c> da birorta ham <c>DROP</c> yo'q (qo'lda o'qib
    /// tekshirilgan). Eski <c>finance_transactions</c> / <c>monthly_charges</c>
    /// yo'liga TEGILMAGAN — u P1-21 niki.
    /// </para>
    ///
    /// <para>
    /// <b>Autogenerate chiqargan ikkita <c>AlterColumn</c> QO'LDA almashtirildi.</b>
    /// EF <c>ALTER TABLE audit_logs ALTER COLUMN before TYPE jsonb;</c> yozadi,
    /// PostgreSQL esa uni rad etadi: <c>text</c> dan <c>jsonb</c> ga avtomatik
    /// kast yo'q, <c>USING before::jsonb</c> kerak — buni <c>AlterColumn</c>
    /// orqali berib bo'lmaydi. Ya'ni avtomatik variant ishlab turgan bazada
    /// YIQILARDI. O'rniga <c>Sql/audit_log_jsonb.sql</c>: u avval eski
    /// qiymatlarni xavfsiz holatga keltiradi (bo'sh satr → NULL, JSON bo'lmagan
    /// matn → JSON satri), keyin turni o'zgartiradi. Model va snapshot esa
    /// o'zgarmaydi — ustun ikkalasida ham <c>jsonb</c>.
    /// </para>
    ///
    /// <para>
    /// EF ifodalay olmaydigan ikkinchi qism — <c>app_rw</c> uchun GRANT/REVOKE:
    /// <c>Sql/anomaly_guards.sql</c> (jadval darajasida UPDATE/DELETE/TRUNCATE
    /// yo'q, faqat uchta "yopish" ustuniga ustun darajasidagi UPDATE).
    /// Ikkala fayl ham EMBEDDED RESOURCE — chop etilgan konteynerda manba
    /// papkasi yo'q.
    /// </para>
    /// </summary>
    public partial class FinanceAnomalyFlags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ---- 1. audit_log.before / .after -> jsonb (SPEC §4.6) ----
            // Autogenerate'ning `AlterColumn` lari o'rniga — klass izohiga qarang.
            migrationBuilder.Sql(MigrationSql.Read("audit_log_jsonb.sql"));

            // ---- 2. Bayroqlar jadvali ----
            migrationBuilder.CreateTable(
                name: "finance_anomaly_flags",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    ref_type = table.Column<string>(type: "text", nullable: false),
                    ref_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    detected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    summary = table.Column<string>(type: "text", nullable: false),
                    details = table.Column<string>(type: "jsonb", nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolved_by = table.Column<string>(type: "text", nullable: true),
                    resolved_reason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_finance_anomaly_flags", x => x.id);
                    table.CheckConstraint("ck_finance_anomaly_flags_kind", "kind in ('shift_variance','fast_reversal','off_hours_payment','paid_without_allocation')");
                    table.CheckConstraint("ck_finance_anomaly_flags_ref_type", "ref_type in ('cash_shift','payment','invoice')");
                    table.CheckConstraint("ck_finance_anomaly_flags_resolution", "(resolved_at is null) = (resolved_by is null) and (resolved_at is null) = (resolved_reason is null) and (resolved_reason is null or btrim(resolved_reason) <> '')");
                    table.ForeignKey(
                        name: "fk_finance_anomaly_flags_users_resolved_by",
                        column: x => x.resolved_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_finance_anomaly_flags_resolved_by",
                table: "finance_anomaly_flags",
                column: "resolved_by");

            migrationBuilder.CreateIndex(
                name: "ix_finance_anomaly_flags_unresolved",
                table: "finance_anomaly_flags",
                column: "occurred_at",
                filter: "resolved_at is null");

            // TEKSHIRUVNING IDEMPOTENTLIGI SHU INDEKSGA TAYANADI: bitta hodisa =
            // bitta bayroq. Busiz har tungi yurish dublikat yozardi va direktor
            // panelidagi hisoblagich bir hafta ichida ma'nosiz bo'lardi.
            migrationBuilder.CreateIndex(
                name: "ux_finance_anomaly_flags_kind_ref",
                table: "finance_anomaly_flags",
                columns: new[] { "kind", "ref_id" },
                unique: true);

            // ---- 3. Baza darajasidagi qulflar ----
            // TARTIB MUHIM: avval jadval (yuqorida), keyin grantlar.
            migrationBuilder.Sql(MigrationSql.Read("anomaly_guards.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Grantlar jadval bilan birga ketadi — alohida REVOKE kerak emas.
            migrationBuilder.DropTable(
                name: "finance_anomaly_flags");

            // jsonb -> text. `USING` bu yerda ham majburiy: teskari yo'nalishda
            // ham avtomatik kast yo'q. Ma'lumot o'zgarmaydi — jsonb'ning matn
            // ko'rinishi yoziladi.
            migrationBuilder.Sql(
                "ALTER TABLE audit_logs ALTER COLUMN before TYPE text USING before::text;");
            migrationBuilder.Sql(
                "ALTER TABLE audit_logs ALTER COLUMN after TYPE text USING after::text;");
        }
    }
}
