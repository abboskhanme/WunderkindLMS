using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <summary>
    /// Bir martalik ma'lumot tuzatishi (mijoz, 2026-09-26: "o'tgan sanaga davomat qilish mumkin
    /// bo'lishi kerak"). Yo'nalish guruhlari tizimga 2026-09-24 da kiritilgan va hamma a'zoning
    /// <c>joined_on</c> sanasi o'sha kun bo'lib qolgan, holbuki o'quvchilar yil boshidan shu
    /// yo'nalishda o'qiydi. Natijada 2–23-sentabrga qo'yilgan davomat profil, portal va sinf
    /// ko'rsatkichlarida hisobga olinmasdi (a'zolik sanasi bo'yicha filtrlanadi).
    ///
    /// <para>
    /// Faqat yo'nalishga BIRINCHI marta qo'shilgan (boshqa yo'nalish tarixi yo'q) FAOL a'zolar
    /// o'quv yili boshiga (1-chorak boshlanishi) tenglashtiriladi. Yo'nalish almashtirgan
    /// o'quvchining sanalari tegilmaydi. Down — ma'lumotni qaytarmaydi (eski sana — kiritilgan kun,
    /// ma'noli qiymat emas).
    /// </para>
    /// </summary>
    public partial class TrackMembersFromYearStart : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE study_group_members m
                SET joined_on = y.start
                FROM (
                    SELECT start_date::date AS start
                    FROM quarters
                    WHERE quarter = 1 AND start_date <> ''
                    ORDER BY start_date DESC
                    LIMIT 1
                ) y
                WHERE m.left_on IS NULL
                  AND m.joined_on > y.start
                  AND EXISTS (SELECT 1 FROM study_groups g WHERE g.id = m.group_id AND g.is_track)
                  AND NOT EXISTS (
                      SELECT 1
                      FROM study_group_members o
                      JOIN study_groups og ON og.id = o.group_id
                      WHERE og.is_track AND o.student_id = m.student_id AND o.id <> m.id);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Ma'lumot tuzatishi — qaytarilmaydi (yuqoridagi izoh).
        }
    }
}
