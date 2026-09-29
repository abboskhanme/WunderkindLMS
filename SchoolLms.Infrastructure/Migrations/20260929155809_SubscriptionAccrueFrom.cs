using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <summary>
    /// <c>student_subscriptions.accrue_from</c>: obuna bir sanadan turadi, oylik hisob esa keyinroq
    /// boshlanadi. Mijoz, 2026-09-29: "abonimentlar sentabrdan boshlanishi kerak, eduschool bilan bir
    /// xil bo'lsin". EduSchool'dan ko'chirilgan avtobus/yotoqxona/kechki dars obunalari 1-oktabrdan
    /// ochilgan edi, chunki sentyabr puli EduSchool'dan kelgan sentyabr ("O'qish") fakturasida bor
    /// (o'qish + avtobus/yotoqxona bitta summa). Endi ular 1-sentabrdan turadi, hisob esa
    /// 1-oktabrdan — sentyabr ikki marta yozilmaydi.
    ///
    /// Faqat sentyabr fakturasi bor o'quvchilar ko'chiriladi (qolganlarida sentyabr puli hech qayerda
    /// yo'q — ular 1-oktabrdan qoladi). Shu toifada sentyabrga tushadigan boshqa obuna bo'lsa — tegmaydi.
    /// </summary>
    public partial class SubscriptionAccrueFrom : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "accrue_from",
                table: "student_subscriptions",
                type: "date",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_student_subscriptions_accrue_from",
                table: "student_subscriptions",
                sql: "accrue_from is null or accrue_from >= starts_on");

            migrationBuilder.Sql("""
                CREATE TEMP TABLE _moved ON COMMIT DROP AS
                SELECT x.id, x.student_id, c.name AS category_name
                FROM student_subscriptions x
                JOIN fee_categories c ON c.id = x.category_id
                WHERE c.code IN ('bus', 'dormitory', 'evening')
                  AND x.starts_on = DATE '2026-10-01'
                  AND x.accrue_from IS NULL
                  AND x.detail LIKE 'EduSchool: %'
                  AND EXISTS (SELECT 1 FROM invoices i
                              WHERE i.student_id = x.student_id AND i.period_month = DATE '2026-09-01'
                                AND i.status <> 'void')
                  AND NOT EXISTS (SELECT 1 FROM student_subscriptions y
                                  WHERE y.student_id = x.student_id AND y.category_id = x.category_id
                                    AND y.id <> x.id AND y.starts_on <= DATE '2026-09-30'
                                    AND (y.ends_on IS NULL OR y.ends_on >= DATE '2026-09-01'));

                UPDATE student_subscriptions x
                SET starts_on = DATE '2026-09-01', accrue_from = DATE '2026-10-01'
                FROM _moved m
                WHERE x.id = m.id;

                INSERT INTO audit_logs (id, entity_type, entity_id, action, timestamp, actor_id, actor_name, summary, before, after, student_id)
                SELECT gen_random_uuid()::text, 'StudentSubscription', m.id::text, 'update',
                       to_char(now() AT TIME ZONE 'Asia/Tashkent', 'YYYY-MM-DD"T"HH24:MI:SS'),
                       NULL, 'Tizim',
                       m.category_name || ' obunasi 2026-09-01 dan (EduSchool bilan bir xil); oylik hisob 2026-10-01 dan — '
                         || 'sentyabr puli EduSchool''dan kelgan sentyabr fakturasida',
                       jsonb_build_object('StartsOn', '2026-10-01', 'AccrueFrom', NULL),
                       jsonb_build_object('StartsOn', '2026-09-01', 'AccrueFrom', '2026-10-01'),
                       m.student_id
                FROM _moved m;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Ko'chirilganlar yana 1-oktabrdan (aks holda ustun ketgach sentyabr qayta hisoblanardi).
            migrationBuilder.Sql("""
                UPDATE student_subscriptions SET starts_on = accrue_from
                WHERE accrue_from IS NOT NULL AND (ends_on IS NULL OR ends_on >= accrue_from);
                """);

            migrationBuilder.DropCheckConstraint(
                name: "ck_student_subscriptions_accrue_from",
                table: "student_subscriptions");

            migrationBuilder.DropColumn(
                name: "accrue_from",
                table: "student_subscriptions");
        }
    }
}
