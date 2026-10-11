using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BoardingRosters : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "boarding_rosters",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    session = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    student_id = table.Column<string>(type: "text", nullable: false),
                    added_on = table.Column<DateOnly>(type: "date", nullable: false),
                    removed_on = table.Column<DateOnly>(type: "date", nullable: true),
                    added_by = table.Column<string>(type: "text", nullable: false),
                    removed_by = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_boarding_rosters", x => x.id);
                    table.CheckConstraint("ck_boarding_rosters_dates", "removed_on is null or removed_on >= added_on");
                    table.CheckConstraint("ck_boarding_rosters_session", "session in ('evening','dorm')");
                    table.ForeignKey(
                        name: "fk_boarding_rosters_students_student_id",
                        column: x => x.student_id,
                        principalTable: "students",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_boarding_rosters_student",
                table: "boarding_rosters",
                column: "student_id");

            migrationBuilder.CreateIndex(
                name: "ux_boarding_rosters_active",
                table: "boarding_rosters",
                columns: new[] { "session", "student_id" },
                unique: true,
                filter: "removed_on is null");

            // Bugungi yotoqxona abonementchilari — ikkala ro'yxatga (ilgari ro'yxat shulardan olinardi,
            // hech kim kesilib qolmasin). Keyin ro'yxat qo'lda yuritiladi (mijoz, 2026-10-11).
            migrationBuilder.Sql("""
                INSERT INTO boarding_rosters (session, student_id, added_on, added_by, created_at)
                SELECT v.session, b.student_id, b.added_on, '', now()
                FROM (
                    SELECT ss.student_id, MIN(ss.starts_on) AS added_on
                    FROM student_subscriptions ss
                    JOIN fee_categories fc ON fc.id = ss.category_id AND fc.code = 'dormitory'
                    JOIN students st ON st.id = ss.student_id AND NOT st.is_archived
                    WHERE ss.starts_on <= current_date AND (ss.ends_on IS NULL OR ss.ends_on >= current_date)
                    GROUP BY ss.student_id
                ) b
                CROSS JOIN (VALUES ('evening'), ('dorm')) AS v(session);
                """);

            // Endi yotoqxonada bo'lmaganlarning o'tgan kunlardagi davomati ko'rinib tursin: belgi qo'yilgan
            // davr — ro'yxatdagi davr.
            migrationBuilder.Sql("""
                INSERT INTO boarding_rosters (session, student_id, added_on, removed_on, added_by, created_at)
                SELECT a.session, a.student_id, MIN(a.date), MAX(a.date) + 1, '', now()
                FROM boarding_attendance a
                WHERE NOT EXISTS (
                    SELECT 1 FROM boarding_rosters r WHERE r.session = a.session AND r.student_id = a.student_id)
                GROUP BY a.session, a.student_id;
                """);

            // MCP boarding_attendance vositasi ro'yxatni shu jadvaldan o'qiydi — app_ro allow-list'iga qo'shildi.
            migrationBuilder.Sql(MigrationSql.Read("mcp_guards.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "boarding_rosters");
        }
    }
}
