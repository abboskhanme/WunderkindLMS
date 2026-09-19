using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    // ===========================================================================
    //  Davomat belgisi endi DARS bo'yicha (kun bo'yicha emas).
    //  Mijoz, 2026-09-18 (ikkinchi xat): "sinf tanlansa o'sha soatda darsiga
    //  ko'ra sinfni davomat qilish mumkin bo'lsin".
    // ===========================================================================
    //
    //  Ikkita ustun qo'shiladi (`subject_id`, `period`) va unikal kalit
    //  (date, class_id) → (date, class_id, subject_id, period) ga o'zgaradi:
    //  bitta kunda bitta sinfning bir nechta darsi alohida belgilanadi.
    //
    //  ESKI QATORLAR O'CHIRILADI — va bu XAVFSIZ: bu jadval BIR KUN oldin
    //  qo'shilgan (`DailyAttendanceMarks`), ichida faqat "belgilandi" bayrog'i
    //  bor va DAVOMATNING O'ZI unda emas — yo'qliklar `journal_entries` da
    //  qoladi, ularga tegilmaydi. Eski qator qolsa, u `subject_id = ''`
    //  bilan mavjud bo'lmagan "dars" ga tegishli bo'lib, ro'yxatda soxta
    //  "belgilangan" soatni ko'rsatib turardi.
    public partial class AttendanceMarkPerLesson : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_daily_attendance_marks_date_class_id",
                table: "daily_attendance_marks");

            migrationBuilder.AddColumn<int>(
                name: "period",
                table: "daily_attendance_marks",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "subject_id",
                table: "daily_attendance_marks",
                type: "text",
                nullable: false,
                defaultValue: "");

            // Kun bo'yicha yozilgan eski belgilar (darsi yo'q) — yuqoridagi izoh.
            migrationBuilder.Sql("delete from daily_attendance_marks where subject_id = ''");

            migrationBuilder.CreateIndex(
                name: "ix_daily_attendance_marks_date_class_id_subject_id_period",
                table: "daily_attendance_marks",
                columns: new[] { "date", "class_id", "subject_id", "period" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_daily_attendance_marks_date_class_id_subject_id_period",
                table: "daily_attendance_marks");

            migrationBuilder.DropColumn(
                name: "period",
                table: "daily_attendance_marks");

            migrationBuilder.DropColumn(
                name: "subject_id",
                table: "daily_attendance_marks");

            migrationBuilder.CreateIndex(
                name: "ix_daily_attendance_marks_date_class_id",
                table: "daily_attendance_marks",
                columns: new[] { "date", "class_id" },
                unique: true);
        }
    }
}
