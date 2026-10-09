using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ClassHomeroomTeachers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "class_homeroom_teachers",
                columns: table => new
                {
                    class_id = table.Column<string>(type: "text", nullable: false),
                    teacher_id = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_class_homeroom_teachers", x => new { x.class_id, x.teacher_id });
                    table.ForeignKey(
                        name: "fk_class_homeroom_teachers_classes_class_id",
                        column: x => x.class_id,
                        principalTable: "classes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_class_homeroom_teachers_teachers_teacher_id",
                        column: x => x.teacher_id,
                        principalTable: "teachers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_class_homeroom_teachers_teacher",
                table: "class_homeroom_teachers",
                column: "teacher_id");

            // Mavjud rahbarlar: `teachers.homeroom_class` (sinf NOMI) → sinf id'si. Bir xil nomli
            // faol va arxivdagi sinf bo'lsa — faoliga. Ustun o'chirilmaydi (ilova uni endi o'qimaydi).
            migrationBuilder.Sql("""
                INSERT INTO class_homeroom_teachers (class_id, teacher_id, created_at)
                SELECT c.id, t.id, now()
                FROM teachers t
                JOIN classes c ON c.name = t.homeroom_class
                WHERE t.homeroom_class <> ''
                  AND (NOT c.is_archived OR NOT EXISTS (
                        SELECT 1 FROM classes a WHERE a.name = c.name AND NOT a.is_archived))
                ON CONFLICT DO NOTHING;
                """);

            // MCP (read-only AI access): sinflar va xodimlar vositalari rahbarlarni shu jadvaldan
            // o'qiydi — u mcp_guards.sql allow-list'iga qo'shildi; qayta yurgizish grantlarni yangilaydi.
            migrationBuilder.Sql(MigrationSql.Read("mcp_guards.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "class_homeroom_teachers");
        }
    }
}
