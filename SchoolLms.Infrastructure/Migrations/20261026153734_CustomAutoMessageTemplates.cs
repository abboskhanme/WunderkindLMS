using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class CustomAutoMessageTemplates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Templates staff create (2026-10-26). The built-in rules get empty lists, not NULL (the
            // columns are NOT NULL), and no schedule; ck_auto_message_rules_schedule keeps it that way.
            migrationBuilder.DropCheckConstraint(
                name: "ck_auto_message_rules_key",
                table: "auto_message_rules");

            migrationBuilder.AddColumn<List<string>>(
                name: "class_ids",
                table: "auto_message_rules",
                type: "text[]",
                nullable: false,
                defaultValueSql: "'{}'");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "created_at",
                table: "auto_message_rules",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_archived",
                table: "auto_message_rules",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "schedule",
                table: "auto_message_rules",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "send_date",
                table: "auto_message_rules",
                type: "character varying(10)",
                maxLength: 10,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "title",
                table: "auto_message_rules",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<List<int>>(
                name: "week_days",
                table: "auto_message_rules",
                type: "integer[]",
                nullable: false,
                defaultValueSql: "'{}'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_auto_message_rules_key",
                table: "auto_message_rules",
                sql: "key in ('absence','invoice','debt','birthday','payment') or key ~ '^c_[0-9a-f]{8}$'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_auto_message_rules_schedule",
                table: "auto_message_rules",
                sql: "(key ~ '^c_' and schedule in ('once','daily','weekly','monthly')) or (key !~ '^c_' and schedule is null)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_auto_message_rules_send_date",
                table: "auto_message_rules",
                sql: "send_date is null or send_date ~ '^[0-9]{4}-[0-9]{2}-[0-9]{2}$'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The old key check admits only the built-in rules: the templates go (their runs cascade;
            // their log rows stay, as they would for any rule).
            migrationBuilder.Sql("delete from auto_message_rules where key ~ '^c_';");

            migrationBuilder.DropCheckConstraint(
                name: "ck_auto_message_rules_key",
                table: "auto_message_rules");

            migrationBuilder.DropCheckConstraint(
                name: "ck_auto_message_rules_schedule",
                table: "auto_message_rules");

            migrationBuilder.DropCheckConstraint(
                name: "ck_auto_message_rules_send_date",
                table: "auto_message_rules");

            migrationBuilder.DropColumn(
                name: "class_ids",
                table: "auto_message_rules");

            migrationBuilder.DropColumn(
                name: "created_at",
                table: "auto_message_rules");

            migrationBuilder.DropColumn(
                name: "is_archived",
                table: "auto_message_rules");

            migrationBuilder.DropColumn(
                name: "schedule",
                table: "auto_message_rules");

            migrationBuilder.DropColumn(
                name: "send_date",
                table: "auto_message_rules");

            migrationBuilder.DropColumn(
                name: "title",
                table: "auto_message_rules");

            migrationBuilder.DropColumn(
                name: "week_days",
                table: "auto_message_rules");

            migrationBuilder.AddCheckConstraint(
                name: "ck_auto_message_rules_key",
                table: "auto_message_rules",
                sql: "key in ('absence','invoice','debt','birthday','payment')");
        }
    }
}
