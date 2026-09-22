using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    // ===========================================================================
    //  Lid → o'quvchi STATISTIKASI (mijoz qarori, 2026-09-22).
    //  O'quvchiga aylangan lid `leads` dan O'CHIRILADI; umumiy son esa shu yangi
    //  `lead_conversions` jadvalida qoladi — faqat vaqt va manba, shaxsiy
    //  ma'lumot ham, bosqich ham yo'q. Faqat CreateTable/CreateIndex; birorta
    //  DROP yo'q (qatorma-qator o'qilgan). Grantlar: lead_conversions_guards.sql.
    // ===========================================================================
    public partial class LeadConversions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "lead_conversions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    source = table.Column<string>(type: "text", nullable: false, defaultValue: "manual"),
                    survey_id = table.Column<Guid>(type: "uuid", nullable: true),
                    converted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_lead_conversions", x => x.id);
                    table.CheckConstraint("ck_lead_conversions_source", "source in ('manual','survey')");
                    table.ForeignKey(
                        name: "fk_lead_conversions_surveys_survey_id",
                        column: x => x.survey_id,
                        principalTable: "surveys",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "ix_lead_conversions_source",
                table: "lead_conversions",
                columns: new[] { "source", "survey_id" });

            migrationBuilder.CreateIndex(
                name: "ix_lead_conversions_survey_id",
                table: "lead_conversions",
                column: "survey_id");

            migrationBuilder.Sql(MigrationSql.Read("lead_conversions_guards.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "lead_conversions");
        }
    }
}
