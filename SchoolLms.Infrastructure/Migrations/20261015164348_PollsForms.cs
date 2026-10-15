using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PollsForms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_polls_audience",
                table: "polls");

            migrationBuilder.DropCheckConstraint(
                name: "ck_poll_questions_kind",
                table: "poll_questions");

            migrationBuilder.AddColumn<bool>(
                name: "collect_contact",
                table: "polls",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "public_link",
                table: "polls",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "public_token",
                table: "polls",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "contact_name",
                table: "poll_responses",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "contact_phone",
                table: "poll_responses",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "via_link",
                table: "poll_responses",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AlterColumn<string>(
                name: "options",
                table: "poll_questions",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2000)",
                oldMaxLength: 2000,
                oldNullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "allow_other",
                table: "poll_questions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "description",
                table: "poll_questions",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "max_choices",
                table: "poll_questions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "max_label",
                table: "poll_questions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "max_value",
                table: "poll_questions",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "min_choices",
                table: "poll_questions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "min_label",
                table: "poll_questions",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "min_value",
                table: "poll_questions",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<List<string>>(
                name: "choices",
                table: "poll_answers",
                type: "text[]",
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "date",
                table: "poll_answers",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "number",
                table: "poll_answers",
                type: "numeric(18,4)",
                precision: 18,
                scale: 4,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_polls_public_token",
                table: "polls",
                column: "public_token",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_polls_audience",
                table: "polls",
                sql: "audience in ('parents','teachers','public')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_polls_contact_not_anonymous",
                table: "polls",
                sql: "not (collect_contact and anonymous)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_poll_questions_kind",
                table: "poll_questions",
                sql: "kind in ('nps','stars','choice','text','multi','dropdown','short','scale','number','date','section')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_polls_public_token",
                table: "polls");

            migrationBuilder.DropCheckConstraint(
                name: "ck_polls_audience",
                table: "polls");

            migrationBuilder.DropCheckConstraint(
                name: "ck_polls_contact_not_anonymous",
                table: "polls");

            migrationBuilder.DropCheckConstraint(
                name: "ck_poll_questions_kind",
                table: "poll_questions");

            migrationBuilder.DropColumn(
                name: "collect_contact",
                table: "polls");

            migrationBuilder.DropColumn(
                name: "public_link",
                table: "polls");

            migrationBuilder.DropColumn(
                name: "public_token",
                table: "polls");

            migrationBuilder.DropColumn(
                name: "contact_name",
                table: "poll_responses");

            migrationBuilder.DropColumn(
                name: "contact_phone",
                table: "poll_responses");

            migrationBuilder.DropColumn(
                name: "via_link",
                table: "poll_responses");

            migrationBuilder.DropColumn(
                name: "allow_other",
                table: "poll_questions");

            migrationBuilder.DropColumn(
                name: "description",
                table: "poll_questions");

            migrationBuilder.DropColumn(
                name: "max_choices",
                table: "poll_questions");

            migrationBuilder.DropColumn(
                name: "max_label",
                table: "poll_questions");

            migrationBuilder.DropColumn(
                name: "max_value",
                table: "poll_questions");

            migrationBuilder.DropColumn(
                name: "min_choices",
                table: "poll_questions");

            migrationBuilder.DropColumn(
                name: "min_label",
                table: "poll_questions");

            migrationBuilder.DropColumn(
                name: "min_value",
                table: "poll_questions");

            migrationBuilder.DropColumn(
                name: "choices",
                table: "poll_answers");

            migrationBuilder.DropColumn(
                name: "date",
                table: "poll_answers");

            migrationBuilder.DropColumn(
                name: "number",
                table: "poll_answers");

            migrationBuilder.AlterColumn<string>(
                name: "options",
                table: "poll_questions",
                type: "character varying(2000)",
                maxLength: 2000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(4000)",
                oldMaxLength: 4000,
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_polls_audience",
                table: "polls",
                sql: "audience in ('parents','teachers')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_poll_questions_kind",
                table: "poll_questions",
                sql: "kind in ('nps','stars','choice','text')");
        }
    }
}
