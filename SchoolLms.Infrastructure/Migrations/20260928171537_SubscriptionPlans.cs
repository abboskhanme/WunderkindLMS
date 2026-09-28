using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SubscriptionPlans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "plan_id",
                table: "student_subscriptions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "subscription_plans",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    monthly_amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_subscription_plans", x => x.id);
                    table.CheckConstraint("ck_subscription_plans_amount", "monthly_amount >= 0");
                    table.ForeignKey(
                        name: "fk_subscription_plans_fee_categories_category_id",
                        column: x => x.category_id,
                        principalTable: "fee_categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_student_subscriptions_plan_id",
                table: "student_subscriptions",
                column: "plan_id");

            migrationBuilder.CreateIndex(
                name: "ix_subscription_plans_category_id",
                table: "subscription_plans",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_subscription_plans_name",
                table: "subscription_plans",
                column: "name",
                unique: true,
                filter: "is_active");

            migrationBuilder.AddForeignKey(
                name: "fk_student_subscriptions_subscription_plans_plan_id",
                table: "student_subscriptions",
                column: "plan_id",
                principalTable: "subscription_plans",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // Backfill (docs/modules/billing-plans.md §4): the EduSchool import wrote each
            // abonement's name into `detail` as "EduSchool: <name>". One plan per distinct
            // (category, name, amount); a name met with a second category/amount gets " (2)", " (3)"
            // so the active-name index holds. Generic — no school-specific names here.
            migrationBuilder.Sql("""
                create temporary table tmp_plan_seed on commit drop as
                select gen_random_uuid() as id,
                       x.category_id, x.base_name, x.monthly_amount,
                       case when x.rn = 1 then x.base_name
                            else left(x.base_name, 150) || ' (' || x.rn || ')' end as name
                from (
                    select category_id, base_name, monthly_amount,
                           row_number() over (partition by lower(base_name) order by count(*) desc, monthly_amount) as rn
                    from (
                        select category_id, left(btrim(substring(detail from 12)), 160) as base_name, monthly_amount
                        from student_subscriptions
                        where detail like 'EduSchool: %' and btrim(substring(detail from 12)) <> ''
                    ) d
                    group by category_id, base_name, monthly_amount
                ) x;

                insert into subscription_plans (id, name, category_id, monthly_amount, is_active, created_at)
                select id, name, category_id, monthly_amount, true, now() from tmp_plan_seed;

                update student_subscriptions s
                set plan_id = t.id
                from tmp_plan_seed t
                where s.detail like 'EduSchool: %'
                  and left(btrim(substring(s.detail from 12)), 160) = t.base_name
                  and s.category_id = t.category_id
                  and s.monthly_amount = t.monthly_amount;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_student_subscriptions_subscription_plans_plan_id",
                table: "student_subscriptions");

            migrationBuilder.DropTable(
                name: "subscription_plans");

            migrationBuilder.DropIndex(
                name: "ix_student_subscriptions_plan_id",
                table: "student_subscriptions");

            migrationBuilder.DropColumn(
                name: "plan_id",
                table: "student_subscriptions");
        }
    }
}
