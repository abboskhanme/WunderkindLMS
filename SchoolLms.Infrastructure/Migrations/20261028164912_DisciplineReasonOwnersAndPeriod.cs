using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <summary>
    /// Ball sababining mas'ullari va takrorlanish muddati (mijoz, 2026-10-28):
    /// `discipline_reasons.owner_user_ids` (ball faqat mas'ul orqali qo'yiladi) va `period`
    /// (none | week | month — o'quvchiga hafta/oy ichida bir marta).
    /// </summary>
    public partial class DisciplineReasonOwnersAndPeriod : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<List<string>>(
                name: "owner_user_ids",
                table: "discipline_reasons",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'::text[]");

            migrationBuilder.AddColumn<string>(
                name: "period",
                table: "discipline_reasons",
                type: "text",
                nullable: false,
                defaultValue: "none");

            migrationBuilder.AddCheckConstraint(
                name: "ck_discipline_reasons_period",
                table: "discipline_reasons",
                sql: "period in ('none','week','month')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_discipline_reasons_period",
                table: "discipline_reasons");

            migrationBuilder.DropColumn(
                name: "owner_user_ids",
                table: "discipline_reasons");

            migrationBuilder.DropColumn(
                name: "period",
                table: "discipline_reasons");
        }
    }
}
