using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class StudentsParityP1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_student_guardians_relation",
                table: "student_guardians");

            migrationBuilder.AddColumn<string>(
                name: "document_url",
                table: "students",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "language",
                table: "students",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "phone",
                table: "students",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "status_id",
                table: "students",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "relation_note",
                table: "student_guardians",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "rooms",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    name = table.Column<string>(type: "text", nullable: false),
                    building = table.Column<string>(type: "text", nullable: true),
                    floor = table.Column<short>(type: "smallint", nullable: true),
                    capacity = table.Column<short>(type: "smallint", nullable: false, defaultValue: (short)30),
                    kind = table.Column<string>(type: "text", nullable: false, defaultValue: "classroom")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_rooms", x => x.id);
                    table.CheckConstraint("ck_rooms_name", "btrim(name) <> ''");
                });

            migrationBuilder.CreateTable(
                name: "student_comments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    student_id = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    image_url = table.Column<string>(type: "text", nullable: true),
                    file_url = table.Column<string>(type: "text", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_student_comments", x => x.id);
                    table.CheckConstraint("ck_student_comments_body", "btrim(body) <> ''");
                    table.CheckConstraint("ck_student_comments_kind", "kind in ('positive','negative')");
                    table.ForeignKey(
                        name: "fk_student_comments_students_student_id",
                        column: x => x.student_id,
                        principalTable: "students",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_student_comments_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "student_contracts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    student_id = table.Column<string>(type: "text", nullable: false),
                    template_id = table.Column<string>(type: "text", nullable: true),
                    number = table.Column<string>(type: "text", nullable: true),
                    signed_on = table.Column<DateOnly>(type: "date", nullable: true),
                    ends_on = table.Column<DateOnly>(type: "date", nullable: true),
                    file_url = table.Column<string>(type: "text", nullable: true),
                    source = table.Column<string>(type: "text", nullable: false),
                    comment = table.Column<string>(type: "text", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_student_contracts", x => x.id);
                    table.CheckConstraint("ck_student_contracts_number", "number is null or btrim(number) <> ''");
                    table.CheckConstraint("ck_student_contracts_period", "ends_on is null or signed_on is null or ends_on >= signed_on");
                    table.CheckConstraint("ck_student_contracts_source", "source in ('generated','uploaded')");
                    table.ForeignKey(
                        name: "fk_student_contracts_contract_templates_template_id",
                        column: x => x.template_id,
                        principalTable: "contract_templates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_student_contracts_students_student_id",
                        column: x => x.student_id,
                        principalTable: "students",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_student_contracts_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "student_statuses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    name = table.Column<string>(type: "text", nullable: false),
                    color = table.Column<string>(type: "text", nullable: true),
                    position = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    is_default = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_student_statuses", x => x.id);
                    table.CheckConstraint("ck_student_statuses_color", "color is null or color ~ '^#[0-9a-fA-F]{6}$'");
                    table.CheckConstraint("ck_student_statuses_name", "btrim(name) <> ''");
                    table.CheckConstraint("ck_student_statuses_position", "position >= 0");
                });

            migrationBuilder.CreateIndex(
                name: "ix_students_status",
                table: "students",
                column: "status_id");

            migrationBuilder.AddCheckConstraint(
                name: "ck_students_language",
                table: "students",
                sql: "language in ('uz','ru','en','kaa')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_student_guardians_relation",
                table: "student_guardians",
                sql: "relation in ('parent','father','mother','grandparent','trustee','other')");

            migrationBuilder.CreateIndex(
                name: "ix_rooms_name",
                table: "rooms",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_student_comments_created_by",
                table: "student_comments",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_student_comments_student",
                table: "student_comments",
                columns: new[] { "student_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_student_contracts_created_by",
                table: "student_contracts",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_student_contracts_student",
                table: "student_contracts",
                columns: new[] { "student_id", "signed_on" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_student_contracts_template_id",
                table: "student_contracts",
                column: "template_id");

            migrationBuilder.CreateIndex(
                name: "ux_student_contracts_number",
                table: "student_contracts",
                column: "number",
                unique: true,
                filter: "number is not null");

            migrationBuilder.CreateIndex(
                name: "ix_student_statuses_name",
                table: "student_statuses",
                column: "name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_students_student_statuses_status_id",
                table: "students",
                column: "status_id",
                principalTable: "student_statuses",
                principalColumn: "id",
                onDelete: ReferentialAction.SetNull);
        
            // ---- Baza darajasidagi grantlar ----
            // Eng oxirida: jadvallar mavjud bo'lgandan keyin. Izoh va sabab
            // `students_parity_p1_guards.sql` ning o'zida.
            migrationBuilder.Sql(MigrationSql.Read("students_parity_p1_guards.sql"));

}

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_students_student_statuses_status_id",
                table: "students");

            migrationBuilder.DropTable(
                name: "rooms");

            migrationBuilder.DropTable(
                name: "student_comments");

            migrationBuilder.DropTable(
                name: "student_contracts");

            migrationBuilder.DropTable(
                name: "student_statuses");

            migrationBuilder.DropIndex(
                name: "ix_students_status",
                table: "students");

            migrationBuilder.DropCheckConstraint(
                name: "ck_students_language",
                table: "students");

            migrationBuilder.DropCheckConstraint(
                name: "ck_student_guardians_relation",
                table: "student_guardians");

            migrationBuilder.DropColumn(
                name: "document_url",
                table: "students");

            migrationBuilder.DropColumn(
                name: "language",
                table: "students");

            migrationBuilder.DropColumn(
                name: "phone",
                table: "students");

            migrationBuilder.DropColumn(
                name: "status_id",
                table: "students");

            migrationBuilder.DropColumn(
                name: "relation_note",
                table: "student_guardians");

            migrationBuilder.AddCheckConstraint(
                name: "ck_student_guardians_relation",
                table: "student_guardians",
                sql: "relation in ('parent','grandparent','trustee')");
        }
    }
}
