using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <summary>
    /// Several lesson analysis sheets (client, 2026-10-07): a sheets table with the first sheet, every
    /// section and observation on it, and the criterion text frozen on each answer.
    /// </summary>
    public partial class LessonAnalysisSheets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "template_id",
                table: "lesson_observations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "template_id",
                table: "lesson_observation_sections",
                type: "text",
                nullable: false,
                defaultValue: "la-template-default");

            migrationBuilder.AddColumn<string>(
                name: "criterion_text",
                table: "lesson_observation_details",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "section_title",
                table: "lesson_observation_details",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "lesson_observation_templates",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    order_index = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    is_default = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lesson_observation_templates", x => x.id);
                    table.CheckConstraint("ck_lesson_observation_templates_name", "btrim(name) <> ''");
                });

            migrationBuilder.InsertData(
                table: "lesson_observation_templates",
                columns: new[] { "id", "created_at", "description", "is_active", "is_default", "name", "order_index" },
                values: new object[] { "la-template-default", new DateTimeOffset(new DateTime(2026, 10, 7, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null, true, true, "Asosiy tahlil varag'i", 1 });

            // Everything observed before sheets was observed on the first sheet.
            migrationBuilder.Sql("UPDATE lesson_observations SET template_id = 'la-template-default' WHERE template_id IS NULL;");

            // Answers keep the text they were given against: freeze today's texts on the old answers.
            migrationBuilder.Sql("""
                UPDATE lesson_observation_details d
                SET criterion_text = c.text, section_title = s.title
                FROM lesson_observation_criteria c
                JOIN lesson_observation_sections s ON s.id = c.section_id
                WHERE d.criterion_id = c.id AND d.criterion_text IS NULL;
                """);

            migrationBuilder.CreateIndex(
                name: "ix_lesson_observations_template_id",
                table: "lesson_observations",
                column: "template_id");

            migrationBuilder.CreateIndex(
                name: "ix_lesson_observation_sections_template_id_order_index",
                table: "lesson_observation_sections",
                columns: new[] { "template_id", "order_index" });

            migrationBuilder.CreateIndex(
                name: "ix_lesson_observation_templates_is_default",
                table: "lesson_observation_templates",
                column: "is_default",
                unique: true,
                filter: "is_default");

            migrationBuilder.CreateIndex(
                name: "ix_lesson_observation_templates_name",
                table: "lesson_observation_templates",
                column: "name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_lesson_observation_sections_lesson_observation_templates_te",
                table: "lesson_observation_sections",
                column: "template_id",
                principalTable: "lesson_observation_templates",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_lesson_observations_lesson_observation_templates_template_id",
                table: "lesson_observations",
                column: "template_id",
                principalTable: "lesson_observation_templates",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_lesson_observation_sections_lesson_observation_templates_te",
                table: "lesson_observation_sections");

            migrationBuilder.DropForeignKey(
                name: "fk_lesson_observations_lesson_observation_templates_template_id",
                table: "lesson_observations");

            migrationBuilder.DropTable(
                name: "lesson_observation_templates");

            migrationBuilder.DropIndex(
                name: "ix_lesson_observations_template_id",
                table: "lesson_observations");

            migrationBuilder.DropIndex(
                name: "ix_lesson_observation_sections_template_id_order_index",
                table: "lesson_observation_sections");

            migrationBuilder.DropColumn(
                name: "template_id",
                table: "lesson_observations");

            migrationBuilder.DropColumn(
                name: "template_id",
                table: "lesson_observation_sections");

            migrationBuilder.DropColumn(
                name: "criterion_text",
                table: "lesson_observation_details");

            migrationBuilder.DropColumn(
                name: "section_title",
                table: "lesson_observation_details");
        }
    }
}
