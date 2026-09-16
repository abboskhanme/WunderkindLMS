using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <summary>
    /// `docs/modules/existing-module-gaps.md` bo'yicha ikkinchi to'lqin sxemasi —
    /// qarzdorlar ish oqimi (§3.5), sertifikatlar (§2.3), arxivlash sabablari
    /// katalogi (§2.2), intizom sababining uchta yangi ustuni (§6.3, 4–5-qadam),
    /// `school_meta` ning to'rtta bayrog'i (§5.5) va turniket hodisalaridagi
    /// yetishmayotgan indeks.
    ///
    /// <para>
    /// <b>FAQAT QO'SHADI.</b> <c>Up()</c> da birorta ham <c>DROP</c> yo'q — qo'lda,
    /// qator-baqator o'qib tekshirilgan. Birorta mavjud ustun turini
    /// o'zgartirmaydi, nomini almashtirmaydi va constraint'ini yo'qotmaydi.
    /// Xususan <c>students.archive_reason</c> (erkin matn) JOYIDA qoladi: yangi
    /// <c>archive_reason_id</c> uning O'RNIGA emas, YONIGA qo'shiladi (§2.2
    /// "Boshqa" tanlovi ikkalasini ham talab qiladi).
    /// </para>
    ///
    /// <para>
    /// <b>DEFAULT'lar mavjud qatorlarni to'ldiradi</b> va aynan shu narsa bu
    /// migratsiyaning eng nozik joyi:
    /// </para>
    /// <list type="bullet">
    ///   <item><c>discipline_reasons.notify_parent</c> → <b>false</b>. §6.3 ning
    ///     o'z ogohlantirishi: ota-onaga xabar yuboradigan intizomiy ball
    ///     butunlay boshqa ijtimoiy hodisa, va maktab uni har sabab uchun
    ///     ALOHIDA, ataylab yoqishi kerak. Ya'ni bu "sozlamaning sukut
    ///     qiymati" emas, mavjud HAR BIR sababning qiymati.</item>
    ///   <item><c>discipline_reasons.is_active</c> → <b>true</b>: mavjud
    ///     sabablarning hammasi faol bo'lib qoladi.</item>
    ///   <item><c>school_meta.show_learning_progress_in_parent_dashboard</c> →
    ///     <b>true</b>: bugun ota-onalar o'zlashtirishni ko'rishadi va
    ///     migratsiya mavjud xatti-harakatni o'zgartirmaydi.</item>
    ///   <item><c>school_meta.archive_only_non_debtor_students</c> → <b>true</b>:
    ///     mijoz savoli Q4 ning "javob bo'lmasa" qarori (§9). Qarzdorni
    ///     arxivlash — qarzning yo'qolishining eng oson yo'li.</item>
    ///   <item>Qolgan ikki bayroq (<c>make_attendance_reason_required</c>,
    ///     <c>is_student_grade_required</c>) → <b>false</b>: ular o'qituvchining
    ///     kunlik ishini o'zgartiradi, ya'ni ularni maktab yoqadi, migratsiya
    ///     emas.</item>
    /// </list>
    ///
    /// <para>
    /// EF modelidan (<c>Data/ParityModel.cs</c>) keladigan narsalar —
    /// check constraint'lar, qisman indeks (<c>ix_debtor_actions_open_promises</c>),
    /// teskari tartibli indekslar va DEFAULT'lar — snapshot'da ham ko'rinadi,
    /// ya'ni keyingi <c>--autogenerate</c> ularni "ortiqcha" deb DROP qilmaydi.
    /// Xom SQL'da faqat EF ifodalay olmaydigan ikki narsa qoldi:
    /// </para>
    /// <list type="number">
    ///   <item><c>Sql/parity_wave2_seed.sql</c> — ikkita katalogning boshlang'ich
    ///     qatorlari (barqaror id, <c>ON CONFLICT DO NOTHING</c>);</item>
    ///   <item><c>Sql/parity_wave2_guards.sql</c> — <c>app_rw</c> uchun GRANT.
    ///     Busiz ilova birinchi so'rovda SQLSTATE 42501 bilan yiqiladi.
    ///     <b>REVOKE YO'Q</b>: beshta jadvalning hech biri moliyaviy emas va
    ///     §3.5 <c>debtor_actions</c> ni <c>deploy/init-roles.sql</c> §5
    ///     ro'yxatiga qo'shishni ATAYLAB rad etadi — tarix <c>deleted_at</c>
    ///     bilan himoyalanadi, DELETE ni tortib olish bilan emas.</item>
    /// </list>
    ///
    /// <para>
    /// Ikkala fayl ham EMBEDDED RESOURCE — chop etilgan konteynerda manba
    /// papkasi yo'q (csproj: <c>Migrations\Sql\*.sql</c>).
    /// </para>
    /// </summary>
    public partial class ParityWave2Schema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "archive_reason_id",
                table: "students",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "archive_only_non_debtor_students",
                table: "school_meta",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_student_grade_required",
                table: "school_meta",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "make_attendance_reason_required",
                table: "school_meta",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "show_learning_progress_in_parent_dashboard",
                table: "school_meta",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "description",
                table: "discipline_reasons",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                table: "discipline_reasons",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "notify_parent",
                table: "discipline_reasons",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "certificate_types",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    is_scored = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_certificate_types", x => x.id);
                    table.CheckConstraint("ck_certificate_types_name", "btrim(name) <> ''");
                });

            migrationBuilder.CreateTable(
                name: "debtor_statuses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    color = table.Column<string>(type: "text", nullable: false),
                    hint = table.Column<string>(type: "text", nullable: true),
                    position = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_debtor_statuses", x => x.id);
                    table.CheckConstraint("ck_debtor_statuses_name", "btrim(name) <> ''");
                    table.CheckConstraint("ck_debtor_statuses_position", "position >= 0");
                });

            migrationBuilder.CreateTable(
                name: "student_archive_reasons",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_student_archive_reasons", x => x.id);
                    table.CheckConstraint("ck_student_archive_reasons_name", "btrim(name) <> ''");
                    table.CheckConstraint("ck_student_archive_reasons_position", "position >= 0");
                });

            migrationBuilder.CreateTable(
                name: "certificates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<string>(type: "text", nullable: false),
                    type_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_id = table.Column<string>(type: "text", nullable: true),
                    teacher_id = table.Column<string>(type: "text", nullable: true),
                    number = table.Column<string>(type: "text", nullable: true),
                    score = table.Column<decimal>(type: "numeric(6,2)", precision: 6, scale: 2, nullable: true),
                    issued_on = table.Column<DateOnly>(type: "date", nullable: false),
                    expires_on = table.Column<DateOnly>(type: "date", nullable: true),
                    file_url = table.Column<string>(type: "text", nullable: true),
                    comment = table.Column<string>(type: "text", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_certificates", x => x.id);
                    table.CheckConstraint("ck_certificates_period", "expires_on is null or expires_on >= issued_on");
                    table.CheckConstraint("ck_certificates_score", "score is null or score >= 0");
                    table.ForeignKey(
                        name: "fk_certificates_certificate_types_type_id",
                        column: x => x.type_id,
                        principalTable: "certificate_types",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_certificates_students_student_id",
                        column: x => x.student_id,
                        principalTable: "students",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_certificates_subjects_subject_id",
                        column: x => x.subject_id,
                        principalTable: "subjects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_certificates_teachers_teacher_id",
                        column: x => x.teacher_id,
                        principalTable: "teachers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_certificates_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "debtor_actions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_id = table.Column<string>(type: "text", nullable: false),
                    status_id = table.Column<Guid>(type: "uuid", nullable: true),
                    comment = table.Column<string>(type: "text", nullable: false),
                    promised_on = table.Column<DateOnly>(type: "date", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_debtor_actions", x => x.id);
                    table.CheckConstraint("ck_debtor_actions_comment", "btrim(comment) <> ''");
                    table.ForeignKey(
                        name: "fk_debtor_actions_debtor_statuses_status_id",
                        column: x => x.status_id,
                        principalTable: "debtor_statuses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_debtor_actions_students_student_id",
                        column: x => x.student_id,
                        principalTable: "students",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_debtor_actions_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_turnstile_events_device_user_id_event_at",
                table: "turnstile_events",
                columns: new[] { "device_user_id", "event_at" });

            migrationBuilder.CreateIndex(
                name: "ix_students_archive_reason_id",
                table: "students",
                column: "archive_reason_id");

            migrationBuilder.CreateIndex(
                name: "ix_certificate_types_name",
                table: "certificate_types",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_certificates_created_by",
                table: "certificates",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_certificates_student_id_issued_on",
                table: "certificates",
                columns: new[] { "student_id", "issued_on" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_certificates_subject_id",
                table: "certificates",
                column: "subject_id");

            migrationBuilder.CreateIndex(
                name: "ix_certificates_teacher_id",
                table: "certificates",
                column: "teacher_id");

            migrationBuilder.CreateIndex(
                name: "ix_certificates_type_id",
                table: "certificates",
                column: "type_id");

            migrationBuilder.CreateIndex(
                name: "ix_debtor_actions_created_by",
                table: "debtor_actions",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_debtor_actions_open_promises",
                table: "debtor_actions",
                column: "promised_on",
                filter: "promised_on is not null and deleted_at is null");

            migrationBuilder.CreateIndex(
                name: "ix_debtor_actions_status_id",
                table: "debtor_actions",
                column: "status_id");

            migrationBuilder.CreateIndex(
                name: "ix_debtor_actions_student_id_created_at",
                table: "debtor_actions",
                columns: new[] { "student_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_debtor_statuses_name",
                table: "debtor_statuses",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_student_archive_reasons_name",
                table: "student_archive_reasons",
                column: "name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_students_student_archive_reasons_archive_reason_id",
                table: "students",
                column: "archive_reason_id",
                principalTable: "student_archive_reasons",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // ---- Kataloglarning boshlang'ich qatorlari (§2.2, §3.5) ----
            // TARTIB MUHIM: unikal indekslardan KEYIN. Seed `ON CONFLICT (name)`
            // ga tayanadi va u indekssiz ishlamaydi.
            migrationBuilder.Sql(MigrationSql.Read("parity_wave2_seed.sql"));

            // ---- Baza darajasidagi grantlar ----
            // Eng oxirida: jadvallar mavjud bo'lgandan keyin.
            migrationBuilder.Sql(MigrationSql.Read("parity_wave2_guards.sql"));
        }

        /// <summary>
        /// Beshta jadval o'chadi, sakkizta ustun olib tashlanadi, ikkita indeks
        /// ketadi — <c>Up()</c> yaratgan narsalarning AYNAN o'zi va boshqa hech
        /// nima. Grantlar jadvallar bilan birga ketadi (alohida REVOKE kerak
        /// emas), seed qatorlari ham shu jadvallarning ichida edi.
        ///
        /// <para>
        /// <b>Orqaga qaytish MA'LUMOT YO'QOTADIMI?</b> Ha — lekin faqat SHU
        /// migratsiya keltirgan ma'lumotni: sertifikatlar, qarzdor amallari va
        /// katalog qatorlari. Migratsiyadan OLDIN mavjud bo'lgan birorta qiymat
        /// yo'qolmaydi: <c>students.archive_reason</c> (erkin matn) hech qachon
        /// o'zgartirilmagan, intizom sabablarining nomi va bali ham shunday.
        /// </para>
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_students_student_archive_reasons_archive_reason_id",
                table: "students");

            migrationBuilder.DropTable(
                name: "certificates");

            migrationBuilder.DropTable(
                name: "debtor_actions");

            migrationBuilder.DropTable(
                name: "student_archive_reasons");

            migrationBuilder.DropTable(
                name: "certificate_types");

            migrationBuilder.DropTable(
                name: "debtor_statuses");

            migrationBuilder.DropIndex(
                name: "ix_turnstile_events_device_user_id_event_at",
                table: "turnstile_events");

            migrationBuilder.DropIndex(
                name: "ix_students_archive_reason_id",
                table: "students");

            migrationBuilder.DropColumn(
                name: "archive_reason_id",
                table: "students");

            migrationBuilder.DropColumn(
                name: "archive_only_non_debtor_students",
                table: "school_meta");

            migrationBuilder.DropColumn(
                name: "is_student_grade_required",
                table: "school_meta");

            migrationBuilder.DropColumn(
                name: "make_attendance_reason_required",
                table: "school_meta");

            migrationBuilder.DropColumn(
                name: "show_learning_progress_in_parent_dashboard",
                table: "school_meta");

            migrationBuilder.DropColumn(
                name: "description",
                table: "discipline_reasons");

            migrationBuilder.DropColumn(
                name: "is_active",
                table: "discipline_reasons");

            migrationBuilder.DropColumn(
                name: "notify_parent",
                table: "discipline_reasons");
        }
    }
}
