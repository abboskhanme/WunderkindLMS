using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    // ===========================================================================
    //  Kunlik davomat belgisi — "bu sinfning bu kuni belgilab bo'lindi".
    //  Mijoz, 2026-09-18: "bitta mas'ul xodimga shu davomat menusini bersak
    //  barcha sinflarni eng qulay usulda davomatini qilolsin."
    // ===========================================================================
    //
    //  DAVOMATNING O'ZI BU YERDA EMAS. Yo'qlik hamon `journal_entries.reason_id`
    //  ga yoziladi — o'qituvchi qo'li bilan yozgani bilan AYNAN bir xil qatorga
    //  (batafsil: `SchoolLms.Domain/DailyAttendance.cs` boshidagi izoh). Bu
    //  jadval faqat ISH JARAYONINI kuzatadi: usiz "hammasi keldi" bilan "hali
    //  belgilanmagan" ni farqlab bo'lmaydi, chunki ikkalasida ham jurnalda
    //  bitta ham qator yo'q.
    //
    //  YANGI JADVAL, MAVJUDLARIGA TEGILMAYDI: bu migratsiya birorta ustun
    //  qo'shmaydi, o'zgartirmaydi va o'chirmaydi — eski ma'lumot bir bayt ham
    //  qimirlamaydi.
    public partial class DailyAttendanceMarks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "daily_attendance_marks",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    class_id = table.Column<string>(type: "text", nullable: false),
                    date = table.Column<string>(type: "text", nullable: false),
                    marked_by = table.Column<string>(type: "text", nullable: false),
                    marked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    absent_count = table.Column<int>(type: "integer", nullable: false),
                    late_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_daily_attendance_marks", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_daily_attendance_marks_date_class_id",
                table: "daily_attendance_marks",
                columns: new[] { "date", "class_id" },
                unique: true);

            // ---- `app_rw` grantlari ----
            // ENG OXIRIDA — jadval mavjud bo'lgandan keyin. Ichida nima va NEGA:
            // `daily_attendance_marks_guards.sql` ning o'zida.
            migrationBuilder.Sql(MigrationSql.Read("daily_attendance_marks_guards.sql"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Grant uchun alohida REVOKE kerak emas: jadval o'chirilganda uning
            // ustidagi huquqlar ham yo'qoladi (`TransactionTypes.Down()` qoidasi).
            migrationBuilder.DropTable(
                name: "daily_attendance_marks");
        }
    }
}
