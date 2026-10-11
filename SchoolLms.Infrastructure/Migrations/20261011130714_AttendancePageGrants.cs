using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// <c>AttendanceMarkPermission</c> ning davomi (2026-10-11). Rollarda bo'lim kalitlaridan tashqari
    /// SAHIFA ruxsatlari ham bor (<c>"/admin/…|edit"</c>, lib/access.ts) — ular menyuni ochadi va rol
    /// qayta saqlanganda bo'lim kalitini qaytadan hosil qiladi. Shuning uchun:
    /// <list type="bullet">
    ///   <item>"Hamshira" (rol va uning xodimlari) — kechki dars va yotoqxona sahifalari olinadi;</item>
    ///   <item>"Davomat belgilash" sahifasi faqat <c>attendanceMark</c> egalarida qoladi — boshqalar
    ///     (rahbarlar) kunduzgi davomatni ko'radi, lekin belgilamaydi.</item>
    /// </list>
    /// Faqat ma'lumot; Down — bo'sh (olingan ruxsatlar qaytarilmaydi).
    /// </summary>
    public partial class AttendancePageGrants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE access_roles
                SET permissions = ARRAY(SELECT p FROM unnest(permissions) WITH ORDINALITY AS x(p, i)
                                        WHERE p NOT LIKE '/admin/attendance/boarding%' ORDER BY i)
                WHERE lower(name) = 'hamshira';

                UPDATE users u
                SET permissions = ARRAY(SELECT p FROM unnest(u.permissions) WITH ORDINALITY AS x(p, i)
                                        WHERE p NOT LIKE '/admin/attendance/boarding%' ORDER BY i)
                FROM access_roles r
                WHERE r.id = u.access_role_id AND lower(r.name) = 'hamshira';

                UPDATE access_roles
                SET permissions = ARRAY(SELECT p FROM unnest(permissions) WITH ORDINALITY AS x(p, i)
                                        WHERE p NOT LIKE '/admin/attendance/mark|%' ORDER BY i)
                WHERE NOT ('attendanceMark' = ANY(permissions))
                  AND EXISTS (SELECT 1 FROM unnest(permissions) p WHERE p LIKE '/admin/attendance/mark|%');

                UPDATE users
                SET permissions = ARRAY(SELECT p FROM unnest(permissions) WITH ORDINALITY AS x(p, i)
                                        WHERE p NOT LIKE '/admin/attendance/mark|%' ORDER BY i)
                WHERE NOT ('attendanceMark' = ANY(permissions))
                  AND EXISTS (SELECT 1 FROM unnest(permissions) p WHERE p LIKE '/admin/attendance/mark|%');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
