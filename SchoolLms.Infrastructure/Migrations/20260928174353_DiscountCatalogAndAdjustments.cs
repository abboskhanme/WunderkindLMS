using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DiscountCatalogAndAdjustments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_discounts_approved_has_approver",
                table: "discounts");

            migrationBuilder.AddColumn<int>(
                name: "days_per_week",
                table: "subscription_plans",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "discount_type_id",
                table: "discounts",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "discount_types",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_discount_types", x => x.id);
                    table.CheckConstraint("ck_discount_types_amount", "amount >= 0");
                    table.CheckConstraint("ck_discount_types_nonzero", "percent > 0 or amount > 0");
                    table.CheckConstraint("ck_discount_types_percent", "percent >= 0 and percent <= 100");
                    table.ForeignKey(
                        name: "fk_discount_types_fee_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "fee_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "invoice_adjustments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    invoice_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    sick_days = table.Column<int>(type: "integer", nullable: true),
                    discount_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_invoice_adjustments", x => x.id);
                    table.CheckConstraint("ck_invoice_adjustments_amount", "amount > 0");
                    table.CheckConstraint("ck_invoice_adjustments_days", "sick_days is null or sick_days > 0");
                    table.CheckConstraint("ck_invoice_adjustments_kind", "kind in ('sick','discount')");
                    table.ForeignKey(
                        name: "fk_invoice_adjustments_discounts_discount_id",
                        column: x => x.discount_id,
                        principalTable: "discounts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_invoice_adjustments_invoices_invoice_id",
                        column: x => x.invoice_id,
                        principalTable: "invoices",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_invoice_adjustments_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_subscription_plans_days_per_week",
                table: "subscription_plans",
                sql: "days_per_week is null or days_per_week between 1 and 7");

            migrationBuilder.CreateIndex(
                name: "ix_discounts_discount_type_id",
                table: "discounts",
                column: "discount_type_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_discounts_approved_has_approver",
                table: "discounts",
                sql: "status <> 'approved' or approved_by is not null or discount_type_id is not null");

            migrationBuilder.CreateIndex(
                name: "ix_discount_types_category_id",
                table: "discount_types",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_discount_types_name",
                table: "discount_types",
                column: "name",
                unique: true,
                filter: "is_active");

            migrationBuilder.CreateIndex(
                name: "ix_invoice_adjustments_created_by",
                table: "invoice_adjustments",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_invoice_adjustments_discount_id",
                table: "invoice_adjustments",
                column: "discount_id");

            migrationBuilder.CreateIndex(
                name: "ix_invoice_adjustments_invoice_id",
                table: "invoice_adjustments",
                column: "invoice_id");

            migrationBuilder.AddForeignKey(
                name: "fk_discounts_discount_types_discount_type_id",
                table: "discounts",
                column: "discount_type_id",
                principalTable: "discount_types",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // billing-plans.md §10 — an adjustment is history: add, never edit or delete (same
            // rule as payments / ledger_entries). deploy/init-roles.sql repeats it on every run.
            migrationBuilder.Sql("""
                DO $guards$
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'app_rw') THEN
                        RETURN;
                    END IF;
                    EXECUTE 'GRANT SELECT, INSERT, UPDATE, DELETE ON public.discount_types, public.subscription_plans TO app_rw';
                    EXECUTE 'GRANT SELECT, INSERT ON public.invoice_adjustments TO app_rw';
                    EXECUTE 'REVOKE UPDATE, DELETE, TRUNCATE ON public.invoice_adjustments FROM app_rw';
                END
                $guards$;
                """);

            // MCP (read-only AI access): the subscription and discount queries its tools reuse now join
            // subscription_plans / discount_types — both joined the app_ro allow-list in
            // mcp_guards.sql; re-running it re-creates the function and re-applies the grants.
            migrationBuilder.Sql(MigrationSql.Read("mcp_guards.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_discounts_discount_types_discount_type_id",
                table: "discounts");

            migrationBuilder.DropTable(
                name: "discount_types");

            migrationBuilder.DropTable(
                name: "invoice_adjustments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_subscription_plans_days_per_week",
                table: "subscription_plans");

            migrationBuilder.DropIndex(
                name: "ix_discounts_discount_type_id",
                table: "discounts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_discounts_approved_has_approver",
                table: "discounts");

            migrationBuilder.DropColumn(
                name: "days_per_week",
                table: "subscription_plans");

            migrationBuilder.DropColumn(
                name: "discount_type_id",
                table: "discounts");

            migrationBuilder.AddCheckConstraint(
                name: "ck_discounts_approved_has_approver",
                table: "discounts",
                sql: "status <> 'approved' or approved_by is not null");
        }
    }
}
