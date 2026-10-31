using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <summary>
    /// Blok test DTM ko'rinishida (mijoz, 2026-10-31): sertifikat turiga `exam_credits` (daraja → foiz)
    /// va natija katagiga `certificate_id` — o'quvchi qaysi sertifikat bilan ozod qilingani.
    /// </summary>
    public partial class ExamDtmCertificateCredits : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "certificate_id",
                table: "exam_section_scores",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "exam_credits",
                table: "certificate_types",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_exam_section_scores_certificate_id",
                table: "exam_section_scores",
                column: "certificate_id");

            migrationBuilder.AddForeignKey(
                name: "fk_exam_section_scores_certificates_certificate_id",
                table: "exam_section_scores",
                column: "certificate_id",
                principalTable: "certificates",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_exam_section_scores_certificates_certificate_id",
                table: "exam_section_scores");

            migrationBuilder.DropIndex(
                name: "ix_exam_section_scores_certificate_id",
                table: "exam_section_scores");

            migrationBuilder.DropColumn(
                name: "certificate_id",
                table: "exam_section_scores");

            migrationBuilder.DropColumn(
                name: "exam_credits",
                table: "certificate_types");
        }
    }
}
