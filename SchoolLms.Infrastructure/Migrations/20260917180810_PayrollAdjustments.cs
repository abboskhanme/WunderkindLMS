using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PayrollAdjustments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "adjustment_reasons",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    kind = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    position = table.Column<int>(type: "integer", nullable: false, defaultValue: 0)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_adjustment_reasons", x => x.id);
                    table.UniqueConstraint("ak_adjustment_reasons_id_kind", x => new { x.id, x.kind });
                    table.CheckConstraint("ck_adjustment_reasons_kind", "kind in ('bonus','penalty')");
                });

            migrationBuilder.CreateTable(
                name: "payroll_adjustments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    teacher_id = table.Column<string>(type: "text", nullable: true),
                    user_id = table.Column<string>(type: "text", nullable: true),
                    kind = table.Column<string>(type: "text", nullable: false),
                    reason_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    period_year = table.Column<short>(type: "smallint", nullable: false),
                    period_month = table.Column<short>(type: "smallint", nullable: false),
                    comment = table.Column<string>(type: "text", nullable: true),
                    image_url = table.Column<string>(type: "text", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    reversal_of = table.Column<Guid>(type: "uuid", nullable: true),
                    reversal_reason = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payroll_adjustments", x => x.id);
                    table.CheckConstraint("ck_payroll_adjustments_amount", "amount > 0");
                    table.CheckConstraint("ck_payroll_adjustments_identity", "num_nonnulls(teacher_id, user_id) = 1");
                    table.CheckConstraint("ck_payroll_adjustments_kind", "kind in ('bonus','penalty')");
                    table.CheckConstraint("ck_payroll_adjustments_period_month", "period_month between 1 and 12");
                    table.CheckConstraint("ck_payroll_adjustments_period_year", "period_year between 2000 and 2100");
                    table.CheckConstraint("ck_payroll_adjustments_reversal", "(reversal_of is null) = (reversal_reason is null)");
                    table.CheckConstraint("ck_payroll_adjustments_reversal_not_self", "reversal_of is null or reversal_of <> id");
                    table.ForeignKey(
                        name: "fk_payroll_adjustments_adjustment_reasons_reason_id_kind",
                        columns: x => new { x.reason_id, x.kind },
                        principalTable: "adjustment_reasons",
                        principalColumns: new[] { "id", "kind" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payroll_adjustments_payroll_adjustments_reversal_of",
                        column: x => x.reversal_of,
                        principalTable: "payroll_adjustments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payroll_adjustments_teachers_teacher_id",
                        column: x => x.teacher_id,
                        principalTable: "teachers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payroll_adjustments_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payroll_adjustments_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_adjustment_reasons_kind_name",
                table: "adjustment_reasons",
                columns: new[] { "kind", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payroll_adjustments_created_by",
                table: "payroll_adjustments",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_payroll_adjustments_reason_id_kind",
                table: "payroll_adjustments",
                columns: new[] { "reason_id", "kind" });

            migrationBuilder.CreateIndex(
                name: "ix_payroll_adjustments_reversal_of",
                table: "payroll_adjustments",
                column: "reversal_of",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payroll_adjustments_teacher_id_period_year_period_month",
                table: "payroll_adjustments",
                columns: new[] { "teacher_id", "period_year", "period_month" });

            migrationBuilder.CreateIndex(
                name: "ix_payroll_adjustments_user_id_period_year_period_month",
                table: "payroll_adjustments",
                columns: new[] { "user_id", "period_year", "period_month" });

            // ---- Baza darajasidagi qulflar ----
            // TARTIB MUHIM: eng oxirida, jadvallar mavjud bo'lgandan keyin —
            // GRANT mavjud jadvalni talab qiladi.
            //
            // Ichida nima bor va NEGA — `payroll_adjustments_guards.sql` ning o'zida:
            //   1) `adjustment_reasons` — to'liq CRUD (katalog, moliyaviy emas);
            //   2) `payroll_adjustments` — GRANT SELECT, INSERT + REVOKE UPDATE,
            //      DELETE, TRUNCATE (SPEC §4.1: faqat qo'shiladi).
            migrationBuilder.Sql(MigrationSql.Read("payroll_adjustments_guards.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "payroll_adjustments");

            migrationBuilder.DropTable(
                name: "adjustment_reasons");
        }
    }
}
