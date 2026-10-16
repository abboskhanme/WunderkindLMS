using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AuditStudentFlowAndShop : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "shop_products",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    price = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    transaction_type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    position = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shop_products", x => x.id);
                    table.CheckConstraint("ck_shop_products_kind", "kind in ('book','uniform','club','other')");
                    table.CheckConstraint("ck_shop_products_name", "btrim(name) <> ''");
                    table.CheckConstraint("ck_shop_products_price", "price >= 0");
                    table.ForeignKey(
                        name: "fk_shop_products_transaction_types_transaction_type_id",
                        column: x => x.transaction_type_id,
                        principalTable: "transaction_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "student_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    student_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    on_date = table.Column<DateOnly>(type: "date", nullable: false),
                    class_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    class_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    reason_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason_text = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    actor_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    actor_name = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_student_events", x => x.id);
                    table.CheckConstraint("ck_student_events_kind", "kind in ('enrolled','left','returned')");
                    table.CheckConstraint("ck_student_events_reason", "kind = 'left' or (reason_id is null and reason_text is null)");
                    table.CheckConstraint("ck_student_events_source", "source in ('manual','bulk','class_archive','graduation','delete','import','lead','backfill')");
                    table.ForeignKey(
                        name: "fk_student_events_student_archive_reasons_reason_id",
                        column: x => x.reason_id,
                        principalTable: "student_archive_reasons",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "shop_sale_lines",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    cash_box_transaction_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    unit_price = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    line_total = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shop_sale_lines", x => x.id);
                    table.CheckConstraint("ck_shop_sale_lines_line_total", "line_total = unit_price * quantity");
                    table.CheckConstraint("ck_shop_sale_lines_product_name", "btrim(product_name) <> ''");
                    table.CheckConstraint("ck_shop_sale_lines_quantity", "quantity > 0");
                    table.CheckConstraint("ck_shop_sale_lines_unit_price", "unit_price >= 0");
                    table.ForeignKey(
                        name: "fk_shop_sale_lines_cash_box_transactions_cash_box_transaction_",
                        column: x => x.cash_box_transaction_id,
                        principalTable: "cash_box_transactions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_shop_sale_lines_shop_products_product_id",
                        column: x => x.product_id,
                        principalTable: "shop_products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_actor_id_timestamp",
                table: "audit_logs",
                columns: new[] { "actor_id", "timestamp" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_entity_type_timestamp",
                table: "audit_logs",
                columns: new[] { "entity_type", "timestamp" });

            migrationBuilder.CreateIndex(
                name: "ix_shop_products_name",
                table: "shop_products",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shop_products_transaction_type_id_is_active_position",
                table: "shop_products",
                columns: new[] { "transaction_type_id", "is_active", "position" });

            migrationBuilder.CreateIndex(
                name: "ix_shop_sale_lines_cash_box_transaction_id_product_id",
                table: "shop_sale_lines",
                columns: new[] { "cash_box_transaction_id", "product_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_shop_sale_lines_product_id",
                table: "shop_sale_lines",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_student_events_kind_on_date",
                table: "student_events",
                columns: new[] { "kind", "on_date" });

            migrationBuilder.CreateIndex(
                name: "ix_student_events_reason_id",
                table: "student_events",
                column: "reason_id");

            migrationBuilder.CreateIndex(
                name: "ix_student_events_student_id_on_date",
                table: "student_events",
                columns: new[] { "student_id", "on_date" });

            migrationBuilder.Sql(MigrationSql.Read("student_events_backfill.sql"));
            // shop_sale_lines is append-only for app_rw (shop.md) — after the table exists.
            migrationBuilder.Sql(MigrationSql.Read("shop_guards.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "shop_sale_lines");

            migrationBuilder.DropTable(
                name: "student_events");

            migrationBuilder.DropTable(
                name: "shop_products");

            migrationBuilder.DropIndex(
                name: "ix_audit_logs_actor_id_timestamp",
                table: "audit_logs");

            migrationBuilder.DropIndex(
                name: "ix_audit_logs_entity_type_timestamp",
                table: "audit_logs");
        }
    }
}
