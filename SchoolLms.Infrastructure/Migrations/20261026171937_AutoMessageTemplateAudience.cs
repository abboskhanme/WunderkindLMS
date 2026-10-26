using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AutoMessageTemplateAudience : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "audience",
                table: "auto_message_rules",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "group_id",
                table: "auto_message_rules",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "only_debtors",
                table: "auto_message_rules",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Existing rules: an empty list, not NULL (the column is NOT NULL).
            migrationBuilder.AddColumn<List<string>>(
                name: "student_ids",
                table: "auto_message_rules",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");

            // Templates made before the announcement-like audience (2026-10-26) keep reaching who they did:
            // their chosen classes, or everyone. The built-in rules have no audience.
            migrationBuilder.Sql(
                "update auto_message_rules set audience = case when cardinality(class_ids) > 0 then 'class' else 'all' end where key ~ '^c_';");

            migrationBuilder.AddCheckConstraint(
                name: "ck_auto_message_rules_audience",
                table: "auto_message_rules",
                sql: "audience is null or audience in ('all','class','group','selected')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_auto_message_rules_audience",
                table: "auto_message_rules");

            migrationBuilder.DropColumn(
                name: "audience",
                table: "auto_message_rules");

            migrationBuilder.DropColumn(
                name: "group_id",
                table: "auto_message_rules");

            migrationBuilder.DropColumn(
                name: "only_debtors",
                table: "auto_message_rules");

            migrationBuilder.DropColumn(
                name: "student_ids",
                table: "auto_message_rules");
        }
    }
}
