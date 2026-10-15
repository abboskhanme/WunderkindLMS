using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class TeacherAllowances : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "teacher_allowances",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    teacher_id = table.Column<string>(type: "text", nullable: false),
                    source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    amount_kind = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    from_month = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: false),
                    to_month = table.Column<string>(type: "character varying(7)", maxLength: 7, nullable: true),
                    level = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    number = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    issued_on = table.Column<DateOnly>(type: "date", nullable: true),
                    expires_on = table.Column<DateOnly>(type: "date", nullable: true),
                    file_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_teacher_allowances", x => x.id);
                    table.CheckConstraint("ck_teacher_allowances_amount", "amount >= 0 and (amount_kind <> 'percent' or amount <= 1000)");
                    table.CheckConstraint("ck_teacher_allowances_from", "from_month ~ '^[0-9]{4}-(0[1-9]|1[0-2])$'");
                    table.CheckConstraint("ck_teacher_allowances_kind", "amount_kind in ('percent','fixed')");
                    table.CheckConstraint("ck_teacher_allowances_source", "source in ('certificate','ustama')");
                    table.CheckConstraint("ck_teacher_allowances_to", "to_month is null or (to_month ~ '^[0-9]{4}-(0[1-9]|1[0-2])$' and to_month >= from_month)");
                    table.ForeignKey(
                        name: "fk_teacher_allowances_teachers_teacher_id",
                        column: x => x.teacher_id,
                        principalTable: "teachers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_teacher_allowances_teacher",
                table: "teacher_allowances",
                column: "teacher_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "teacher_allowances");
        }
    }
}
