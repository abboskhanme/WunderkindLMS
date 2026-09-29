using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <summary>
    /// Arxivdagi o'quvchilar sinfda ham, guruhda ham qolmaydi (mijoz, 2026-09-29: "arxivdagi barcha
    /// o'quvchilar sinfdan chiqarilsin ... hamma ishlar faqat active o'quvchilar bilan"). Bundan
    /// keyin buni arxivlashning o'zi qiladi (StudentArchiveService.LeaveClassAndGroupsAsync); bu
    /// migratsiya shu kungacha arxivlanganlarni bir marta tozalaydi.
    ///
    /// Hech narsa o'chirilmaydi: a'zoliklar arxiv sanasi bilan YOPILADI (tarix qoladi), sinf darajasi
    /// target_grade ga o'tadi, keyin class_name bo'shaydi.
    /// </summary>
    public partial class ArchivedStudentsLeaveClasses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                -- Arxiv sanasi (matn "yyyy-MM-dd"); yaroqsiz bo'lsa — bugun.
                CREATE TEMP TABLE _archived ON COMMIT DROP AS
                SELECT s.id,
                       CASE WHEN s.archived_at ~ '^\d{4}-\d{2}-\d{2}$' THEN s.archived_at::date ELSE CURRENT_DATE END AS left_on
                FROM students s
                WHERE s.is_archived;

                UPDATE students s
                SET target_grade = c.grade::smallint
                FROM classes c
                WHERE s.is_archived AND s.class_name <> '' AND c.name = s.class_name
                  AND s.target_grade IS NULL AND c.grade BETWEEN 0 AND 11;

                UPDATE class_memberships m
                SET left_on = GREATEST(m.joined_on, a.left_on), leave_reason = 'Arxivlangan'
                FROM _archived a
                WHERE a.id = m.student_id AND m.left_on IS NULL;

                UPDATE study_group_members m
                SET left_on = GREATEST(m.joined_on, a.left_on), leave_reason = 'Arxivlangan'
                FROM _archived a
                WHERE a.id = m.student_id AND m.left_on IS NULL;

                UPDATE students SET class_name = '' WHERE is_archived AND class_name <> '';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Ma'lumot tozalash — orqaga qaytarilmaydi (yopilgan a'zoliklar tarix sifatida qoladi).
        }
    }
}
