using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AttendanceMarkPermission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Mijoz, 2026-10-11: "Kunduzgi davomat faqat hamshirada, kechki yotoq komendantlarda".
            // Davomatni BELGILASH alohida ruxsat bo'ldi (`attendanceMark`): uni faqat "Hamshira" roli
            // oladi; kechki va yotoqxona ruxsatlari undan olinadi. Rahbarlarning ko'rish ruxsatlariga
            // (`attendance`, kechki/yotoqxona) tegilmaydi. Rol nomi bo'yicha — bunday rol yo'q bazada
            // (testlar) hech narsa o'zgarmaydi.
            migrationBuilder.Sql("""
                UPDATE access_roles
                SET permissions = array_remove(array_remove(
                        CASE WHEN 'attendanceMark' = ANY(permissions) THEN permissions
                             ELSE array_append(permissions, 'attendanceMark') END,
                        'attendanceEvening'), 'attendanceDorm')
                WHERE lower(name) = 'hamshira';

                UPDATE users u
                SET permissions = array_remove(array_remove(
                        CASE WHEN 'attendanceMark' = ANY(u.permissions) THEN u.permissions
                             ELSE array_append(u.permissions, 'attendanceMark') END,
                        'attendanceEvening'), 'attendanceDorm')
                FROM access_roles r
                WHERE r.id = u.access_role_id AND lower(r.name) = 'hamshira';

                INSERT INTO access_roles (id, name, description, permissions, created_at)
                SELECT gen_random_uuid(), 'Komendant', 'Kechki dars va yotoqxona davomati',
                       ARRAY['attendanceEvening', 'attendanceDorm'], now()
                WHERE NOT EXISTS (SELECT 1 FROM access_roles WHERE lower(name) = 'komendant');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
