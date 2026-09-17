using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <summary>
    /// O'quv bo'limi pariteti, P2 — <c>docs/modules/students-parity.md</c> §3.3
    /// (Batch C): sinf sig'imi (C-4), fan rangi va faolligi (F-3), mo'ljaldagi
    /// sinf darajasi (S-9), sertifikatning bir nechta fani (Z-3), turlangan
    /// joylashuvlar (L-2), jadval ko'rinishi sozlamalari (X-1), shartnoma
    /// raqamlash rejimi (K-6) va topshiriq egasi (G-20). Ustiga §3.3 da
    /// YO'Q bitta ustun — <c>rooms.is_active</c> (R-1 slice'ining talabi,
    /// <c>RoomTests.Sinf_korsatgan_xona_ochirilmaydi</c> dagi izoh).
    ///
    /// <para>
    /// <b>FAQAT QO'SHADI.</b> <c>Up()</c> qator-baqator o'qib tekshirilgan:
    /// 7 ta <c>AddColumn</c>, 3 ta <c>CreateTable</c>, 5 ta
    /// <c>AddCheckConstraint</c>, 2 ta <c>CreateIndex</c> va ikkita xom SQL
    /// (backfill, grant). Birorta <c>DROP</c>, <c>ALTER COLUMN</c> yoki
    /// <c>RENAME</c> yo'q. <c>assignments</c> va <c>subjects</c> — jonli,
    /// ma'lumot bilan to'lgan jadvallar; ularda aynan shu narsa maktabning
    /// topshiriqlari va fan katalogini yo'qotardi.
    /// </para>
    ///
    /// <para>
    /// <b>BUGUNGI XATTI-HARAKAT O'ZGARMAYDI.</b> Mavjud jadvalga qo'shilgan
    /// har bir NOT NULL ustunning DEFAULT'i bugungi ma'noni saqlaydi:
    /// <c>assignments.owner_kind = 'class'</c> (har bir mavjud topshiriq —
    /// sinf topshirig'i), <c>subjects.is_active = true</c>,
    /// <c>rooms.is_active = true</c>, <c>school_meta.contract_number_mode =
    /// 'auto'</c>. Qolgan uchtasi — <c>classes.capacity</c>,
    /// <c>subjects.color</c>, <c>students.target_grade</c> — <c>null</c>,
    /// ya'ni "ko'rsatilmagan".
    /// </para>
    ///
    /// <para>
    /// <b>KATTA JADVALLAR.</b> <c>ADD COLUMN ... NOT NULL DEFAULT ...</c> —
    /// PostgreSQL 11+ da faqat katalog yozuvi
    /// (<c>pg_attribute.atthasmissing</c>), ya'ni <c>assignments</c> qayta
    /// YOZILMAYDI va bir dona qator ham yangilanmaydi. CHECK constraint
    /// jadvalni bir marta O'QIB tekshiradi (yozmaydi) — "avval nullable,
    /// keyin UPDATE, keyin NOT NULL" yo'li ATAYLAB ishlatilmagan (§3.1 dagi
    /// bir xil qaror).
    /// </para>
    ///
    /// <para>
    /// <b>BACKFILL.</b> <c>certificate_subjects</c> mavjud
    /// <c>certificates.subject_id</c> dan to'ldiriladi. Eski ustun JOYIDA
    /// qoladi va uni o'qiydigan kod o'zgarmaydi — yangi jadval uning
    /// o'rniga emas, yoniga
    /// (<c>Migrations/Sql/students_parity_p2_backfill.sql</c>).
    /// </para>
    ///
    /// <para>
    /// <b>GRANT BOR, <c>REVOKE</c> YO'Q.</b> Uchala yangi jadval ham
    /// moliyaviy emas — ichida summa ham, ledger yozuvi ham, kassa
    /// smenasiga havola ham yo'q — va uchalasida ham TUZATISH funksiyaning
    /// o'zi. Sabab to'liq
    /// <c>Migrations/Sql/students_parity_p2_guards.sql</c> da.
    /// </para>
    ///
    /// <para>
    /// <b><c>cash_handovers</c> GA USTUN QO'SHILMADI — ATAYLAB.</b> Vazifada
    /// <c>approved_by</c> va <c>approved_at</c> "xavfsiz deb hisoblasang"
    /// sharti bilan so'ralgandi. Xavfsiz emas, uch sabab bilan:
    /// <list type="number">
    ///   <item><c>finance-parity.md</c> §3.1 (A2) bu jadvalni ATAYLAB
    ///     tasdiqlash oqimisiz loyihalagan: kassadan pul chiqishi INSERT
    ///     lahzasida allaqachon sodir bo'lgan FAKT, "kutayotgan so'rov"
    ///     emas. Xato topshiriq <c>reversal_of</c> bilan qarshi qator
    ///     qo'shib tuzatiladi. Ya'ni ustunlar hech qanday mavjud yoki
    ///     rejalashtirilgan talabga javob bermaydi.</item>
    ///   <item>Ular ISHLASHI uchun <c>GRANT UPDATE (approved_by,
    ///     approved_at) ON cash_handovers</c> kerak bo'lardi. Bugun bu
    ///     jadvalda <c>REVOKE UPDATE, DELETE, TRUNCATE</c> turibdi
    ///     (<c>finance_parity_guards.sql</c>) va ustun darajasidagi grant
    ///     uni teshadi: <c>student_refunds</c> dagi o'sha naqsh faqat
    ///     <c>student_refunds_locked</c> trigger'i bilan BIRGA xavfsiz
    ///     ("bir marta yoziladi"), grantning o'zi "faqat bir marta"
    ///     demaydi. Bunday trigger va uning `BEFORE UPDATE` mantig'i —
    ///     moliya migratsiyasining ishi, bu yerdagi qo'shimcha emas.</item>
    ///   <item>Grantsiz qo'shilsa, ustunlar ilova uchun O'LIK bo'lardi
    ///     (har yozish 42501), va keyingi agent 42501 ni ko'rib jadval
    ///     darajasida <c>GRANT UPDATE</c> qo'yishga JALB qilinardi — bu esa
    ///     <c>amount</c>, <c>destination</c>, <c>cash_shift_id</c> va
    ///     <c>reversal_of</c> ni ham tahrirlanadigan qilardi. Jimgina
    ///     yo'qoladigan himoya — <c>finance_parity_guards.sql</c> ogohlantirgan
    ///     aynan shu xato.</item>
    /// </list>
    /// Kerak bo'lsa — ALOHIDA moliya migratsiyasi: ustunlar + qulf trigger'i
    /// + ustun grantı + <c>deploy/init-roles.sql</c> §5, bitta ko'rib
    /// chiqishda (<c>finance-parity.md</c> §3.4: "grant review required").
    /// </para>
    /// </summary>
    public partial class StudentsParityP2 : Migration
    {
        /// <summary>
        /// Qat'iy tartib: avval MAVJUD jadvallarga ustun, keyin yangi
        /// jadvallar, keyin cheklovlar va indekslar, keyin backfill (jadval
        /// va uning kaliti tayyor bo'lgach), eng oxirida grant (jadvallar
        /// mavjud bo'lgandan keyin).
        /// </summary>
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "color",
                table: "subjects",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                table: "subjects",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<short>(
                name: "target_grade",
                table: "students",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "contract_number_mode",
                table: "school_meta",
                type: "text",
                nullable: false,
                defaultValue: "auto");

            migrationBuilder.AddColumn<bool>(
                name: "is_active",
                table: "rooms",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<short>(
                name: "capacity",
                table: "classes",
                type: "smallint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "owner_kind",
                table: "assignments",
                type: "text",
                nullable: false,
                defaultValue: "class");

            migrationBuilder.CreateTable(
                name: "certificate_subjects",
                columns: table => new
                {
                    certificate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_id = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_certificate_subjects", x => new { x.certificate_id, x.subject_id });
                    table.ForeignKey(
                        name: "fk_certificate_subjects_certificates_certificate_id",
                        column: x => x.certificate_id,
                        principalTable: "certificates",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_certificate_subjects_subjects_subject_id",
                        column: x => x.subject_id,
                        principalTable: "subjects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "student_locations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    student_id = table.Column<string>(type: "text", nullable: false),
                    kind = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: true),
                    lat = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    lng = table.Column<decimal>(type: "numeric(9,6)", precision: 9, scale: 6, nullable: false),
                    pickup_from = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    pickup_to = table.Column<TimeOnly>(type: "time without time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_student_locations", x => x.id);
                    table.CheckConstraint("ck_student_locations_kind", "kind in ('home','school','pickup')");
                    table.CheckConstraint("ck_student_locations_lat", "lat between -90 and 90");
                    table.CheckConstraint("ck_student_locations_lng", "lng between -180 and 180");
                    table.CheckConstraint("ck_student_locations_pickup_window", "pickup_from is null or pickup_to is null or pickup_to >= pickup_from");
                    table.ForeignKey(
                        name: "fk_student_locations_students_student_id",
                        column: x => x.student_id,
                        principalTable: "students",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_table_settings",
                columns: table => new
                {
                    user_id = table.Column<string>(type: "text", nullable: false),
                    page = table.Column<string>(type: "text", nullable: false),
                    settings = table.Column<string>(type: "jsonb", nullable: false, defaultValueSql: "'{}'::jsonb"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_table_settings", x => new { x.user_id, x.page });
                    table.CheckConstraint("ck_user_table_settings_page", "btrim(page) <> ''");
                    table.ForeignKey(
                        name: "fk_user_table_settings_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_subjects_color",
                table: "subjects",
                sql: "color is null or color ~ '^#[0-9a-fA-F]{6}$'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_students_target_grade",
                table: "students",
                sql: "target_grade between 0 and 11");

            migrationBuilder.AddCheckConstraint(
                name: "ck_school_meta_contract_number_mode",
                table: "school_meta",
                sql: "contract_number_mode in ('auto','manual')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_classes_capacity",
                table: "classes",
                sql: "capacity is null or capacity > 0");

            migrationBuilder.AddCheckConstraint(
                name: "ck_assignments_owner_kind",
                table: "assignments",
                sql: "owner_kind in ('class','group')");

            migrationBuilder.CreateIndex(
                name: "ix_certificate_subjects_subject",
                table: "certificate_subjects",
                column: "subject_id");

            migrationBuilder.CreateIndex(
                name: "ux_student_locations_student_kind",
                table: "student_locations",
                columns: new[] { "student_id", "kind" },
                unique: true);

            // ---- Z-3: sertifikat fanlarini eski ustundan to'ldirish ----
            // TARTIB MUHIM: jadval va uning kompozit kaliti YARATILGANDAN
            // KEYIN — backfill `ON CONFLICT (certificate_id, subject_id)` ga
            // tayanadi. `certificates.subject_id` O'CHIRILMAYDI.
            migrationBuilder.Sql(MigrationSql.Read("students_parity_p2_backfill.sql"));

            // ---- Baza darajasidagi grantlar ----
            // Eng oxirida: jadvallar mavjud bo'lgandan keyin. Izoh va sabab
            // (jumladan nega birorta `REVOKE` yo'q) `students_parity_p2_guards.sql`
            // ning o'zida.
            migrationBuilder.Sql(MigrationSql.Read("students_parity_p2_guards.sql"));
        }

        /// <summary>
        /// Uchta jadval o'chadi, beshta check constraint va yettita ustun
        /// olib tashlanadi — <c>Up()</c> yaratganning AYNAN o'zi. Yangi
        /// jadvallarning FK, indeks va cheklovlari o'sha jadvallar ichida
        /// edi, backfill qatorlari ham (<c>certificate_subjects</c>), ya'ni
        /// alohida qadam talab qilmaydi. Grant ham jadval bilan birga ketadi.
        ///
        /// <para>
        /// <b>Orqaga qaytish MA'LUMOT YO'QOTADIMI?</b> Faqat SHU migratsiya
        /// keltirganini: joylashuvlar, jadval sozlamalari, sertifikat-fan
        /// bog'lanishlari va yettita yangi ustunning qiymatlari. Mavjud
        /// birorta ustun yoki qator tegilmaydi — <c>certificates.subject_id</c>
        /// hech qachon ko'chirilmagani uchun sertifikatning fani ham
        /// yo'qolmaydi.
        /// </para>
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "certificate_subjects");

            migrationBuilder.DropTable(
                name: "student_locations");

            migrationBuilder.DropTable(
                name: "user_table_settings");

            migrationBuilder.DropCheckConstraint(
                name: "ck_subjects_color",
                table: "subjects");

            migrationBuilder.DropCheckConstraint(
                name: "ck_students_target_grade",
                table: "students");

            migrationBuilder.DropCheckConstraint(
                name: "ck_school_meta_contract_number_mode",
                table: "school_meta");

            migrationBuilder.DropCheckConstraint(
                name: "ck_classes_capacity",
                table: "classes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_assignments_owner_kind",
                table: "assignments");

            migrationBuilder.DropColumn(
                name: "color",
                table: "subjects");

            migrationBuilder.DropColumn(
                name: "is_active",
                table: "subjects");

            migrationBuilder.DropColumn(
                name: "target_grade",
                table: "students");

            migrationBuilder.DropColumn(
                name: "contract_number_mode",
                table: "school_meta");

            migrationBuilder.DropColumn(
                name: "is_active",
                table: "rooms");

            migrationBuilder.DropColumn(
                name: "capacity",
                table: "classes");

            migrationBuilder.DropColumn(
                name: "owner_kind",
                table: "assignments");
        }
    }
}
