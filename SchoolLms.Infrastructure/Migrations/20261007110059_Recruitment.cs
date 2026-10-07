using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Recruitment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "onboarding_step_templates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    due_day = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_onboarding_step_templates", x => x.id);
                    table.CheckConstraint("ck_onboarding_step_templates_day", "due_day >= 1 and due_day <= 30");
                });

            migrationBuilder.CreateTable(
                name: "vacancies",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    department = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    requirements = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    slug = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vacancies", x => x.id);
                    table.CheckConstraint("ck_vacancies_status", "status in ('open','closed')");
                });

            migrationBuilder.CreateTable(
                name: "job_applicants",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    vacancy_id = table.Column<Guid>(type: "uuid", nullable: true),
                    full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    position = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    experience = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    about = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    cv_path = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    cv_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    stage = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    stage_changed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reject_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    rejected_at_stage = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    hired_on = table.Column<DateOnly>(type: "date", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_job_applicants", x => x.id);
                    table.CheckConstraint("ck_job_applicants_stage", "stage in ('new','cv','test','demo','hr_interview','director_interview','onboarding','adapted','rejected')");
                    table.ForeignKey(
                        name: "fk_job_applicants_vacancies_vacancy_id",
                        column: x => x.vacancy_id,
                        principalTable: "vacancies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "applicant_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    applicant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_stage = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    to_stage = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    by = table.Column<string>(type: "text", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_applicant_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_applicant_events_job_applicants_applicant_id",
                        column: x => x.applicant_id,
                        principalTable: "job_applicants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "applicant_onboarding_steps",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    applicant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    due_on = table.Column<DateOnly>(type: "date", nullable: false),
                    done_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    done_by = table.Column<string>(type: "text", nullable: true),
                    comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_applicant_onboarding_steps", x => x.id);
                    table.ForeignKey(
                        name: "fk_applicant_onboarding_steps_job_applicants_applicant_id",
                        column: x => x.applicant_id,
                        principalTable: "job_applicants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "applicant_stage_results",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    applicant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stage = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    score = table.Column<int>(type: "integer", nullable: true),
                    comment = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    by = table.Column<string>(type: "text", nullable: false),
                    at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_applicant_stage_results", x => x.id);
                    table.CheckConstraint("ck_applicant_stage_results_score", "score is null or (score >= 0 and score <= 100)");
                    table.ForeignKey(
                        name: "fk_applicant_stage_results_job_applicants_applicant_id",
                        column: x => x.applicant_id,
                        principalTable: "job_applicants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "onboarding_step_templates",
                columns: new[] { "id", "description", "due_day", "is_active", "position", "title" },
                values: new object[,]
                {
                    { new Guid("7b1e0c00-0000-4000-8000-000000000001"), "Shaxsiy hujjatlar topshiriladi, mehnat shartnomasi imzolanadi.", 1, true, 1, "Hujjatlar va mehnat shartnomasi" },
                    { new Guid("7b1e0c00-0000-4000-8000-000000000002"), "Ish joyi tayyorlanadi, LMS login beriladi, Telegram botga ulanadi.", 1, true, 2, "Ish joyi va tizimga kirish" },
                    { new Guid("7b1e0c00-0000-4000-8000-000000000003"), "Binolar, xavfsizlik qoidalari, ichki tartib va kiyinish qoidalari.", 2, true, 3, "Maktab bilan tanishuv" },
                    { new Guid("7b1e0c00-0000-4000-8000-000000000004"), "Tajribali hamkasb murabbiy (mentor) etib tayinlanadi.", 2, true, 4, "Mentor biriktirish" },
                    { new Guid("7b1e0c00-0000-4000-8000-000000000005"), "Direktor, o'rinbosarlar, metodbirlashma va bo'lim bilan tanishuv.", 3, true, 5, "Rahbariyat va jamoa bilan tanishuv" },
                    { new Guid("7b1e0c00-0000-4000-8000-000000000006"), "O'quv dasturi, dars jadvali, jurnal va baholash tartibi.", 5, true, 6, "O'quv dasturi va LMS treningi" },
                    { new Guid("7b1e0c00-0000-4000-8000-000000000007"), "Kamida ikkita mentor darsini kuzatib, xulosa yoziladi.", 7, true, 7, "Mentor darslarini kuzatish" },
                    { new Guid("7b1e0c00-0000-4000-8000-000000000008"), "Birinchi mustaqil darslar mentor ishtirokida o'tiladi.", 10, true, 8, "Mentor kuzatuvida birinchi darslar" },
                    { new Guid("7b1e0c00-0000-4000-8000-000000000009"), "Ikki haftalik natijalar, qiyinchiliklar va yordam ehtiyoji muhokamasi.", 14, true, 9, "Oraliq suhbat (HR)" },
                    { new Guid("7b1e0c00-0000-4000-8000-000000000010"), "Metodist darsni kuzatadi va dars tahlili varag'ini to'ldiradi.", 21, true, 10, "Dars tahlili (metodist)" },
                    { new Guid("7b1e0c00-0000-4000-8000-000000000011"), "Adaptatsiya natijalari va doimiy ishga o'tkazish qarori.", 30, true, 11, "Yakuniy baholash (direktor)" }
                });

            migrationBuilder.CreateIndex(
                name: "ix_applicant_events_applicant_id_at",
                table: "applicant_events",
                columns: new[] { "applicant_id", "at" });

            migrationBuilder.CreateIndex(
                name: "ix_applicant_onboarding_steps_applicant_id_position",
                table: "applicant_onboarding_steps",
                columns: new[] { "applicant_id", "position" });

            migrationBuilder.CreateIndex(
                name: "ix_applicant_stage_results_applicant_id_stage",
                table: "applicant_stage_results",
                columns: new[] { "applicant_id", "stage" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_job_applicants_phone",
                table: "job_applicants",
                column: "phone");

            migrationBuilder.CreateIndex(
                name: "ix_job_applicants_stage_stage_changed_at",
                table: "job_applicants",
                columns: new[] { "stage", "stage_changed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_job_applicants_vacancy_id",
                table: "job_applicants",
                column: "vacancy_id");

            migrationBuilder.CreateIndex(
                name: "ix_vacancies_slug",
                table: "vacancies",
                column: "slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "applicant_events");

            migrationBuilder.DropTable(
                name: "applicant_onboarding_steps");

            migrationBuilder.DropTable(
                name: "applicant_stage_results");

            migrationBuilder.DropTable(
                name: "onboarding_step_templates");

            migrationBuilder.DropTable(
                name: "job_applicants");

            migrationBuilder.DropTable(
                name: "vacancies");
        }
    }
}
