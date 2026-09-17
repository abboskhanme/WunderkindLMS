using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <summary>
    /// O'quv guruhlari va sinf a'zoligi — <c>docs/modules/students-parity.md</c>
    /// §3.1 (Batch A). Mijoz 2026-09-17 da bir nechta sinfdan yig'iladigan
    /// guruhlar borligini tasdiqladi, ya'ni <c>Guruh ≠ Sinf</c>.
    ///
    /// <para>
    /// <b>FAQAT QO'SHADI.</b> <c>Up()</c> qator-baqator o'qib tekshirilgan:
    /// 7 ta <c>AddColumn</c>, 5 ta <c>CreateTable</c>, 5 ta
    /// <c>AddCheckConstraint</c>, 13 ta <c>CreateIndex</c> va uchta xom SQL
    /// (ifoda indeksi, backfill, grant). Birorta <c>DROP</c>,
    /// <c>ALTER COLUMN</c> yoki <c>RENAME</c> yo'q — beshta yadro jadvaliga
    /// (jurnal, dars mavzulari, chorak baholari, jadval shablonlari, hafta
    /// biriktirishlari) tegadigan migratsiyada aynan shu narsa jonli maktabning
    /// akademik tarixini yo'qotardi.
    /// </para>
    ///
    /// <para>
    /// <b>BUGUNGI XATTI-HARAKAT O'ZGARMAYDI.</b> Mavjud har bir qator
    /// <c>owner_kind = 'class'</c> oladi (DEFAULT), <c>subjects.is_groupable</c>
    /// va <c>school_meta.group_lessons_enabled</c> — <c>false</c>.
    /// <c>students.class_name</c> JOYIDA qoladi va uni o'qiydigan ~14 ta so'rov
    /// o'zgarmaydi; <c>class_memberships</c> shu ustundan BIR MARTA
    /// to'ldiriladi (backfill) va a'zolik xizmati (1-slice) chiqmaguncha NUSXA
    /// bo'lib turadi.
    /// </para>
    ///
    /// <para>
    /// <b>KATTA JADVALLAR.</b> <c>ADD COLUMN ... NOT NULL DEFAULT 'class'</c> —
    /// PostgreSQL 11+ da faqat katalog yozuvi
    /// (<c>pg_attribute.atthasmissing</c>), ya'ni <c>journal_entries</c> qayta
    /// YOZILMAYDI va bir dona qator ham yangilanmaydi. CHECK constraint jadvalni
    /// bir marta O'QIB tekshiradi (yozmaydi) — "avval nullable, keyin UPDATE,
    /// keyin NOT NULL" yo'li ATAYLAB ishlatilmagan.
    /// </para>
    ///
    /// <para>
    /// <b>MIJOZ SAVOLI Q1 (§5) ning "javob bo'lmasa" qarori BAZADA:</b>
    /// <c>ux_group_members_one_group_per_subject</c> — bitta o'quvchi bitta
    /// fandan ko'pi bilan bitta faol guruhda. Mijoz "ikkita bo'lishi mumkin"
    /// desa SHU BITTA indeks olib tashlanadi, boshqa hech narsa o'zgarmaydi.
    /// </para>
    ///
    /// <para>
    /// <b>Uchta narsa xom SQL'da</b>, chunki EF ularni ifodalay olmaydi:
    /// (1) <c>ux_study_groups_name_active</c> — <c>lower(name)</c> ustidagi
    /// ifoda indeksi; (2) a'zolar jadvalidagi kompozit FK'ning
    /// <c>ON UPDATE CASCADE</c> qismi (quyida qo'lda qo'yilgan); (3) backfill va
    /// grant fayllari (EMBEDDED RESOURCE — prod konteynerida manba papkasi
    /// yo'q).
    /// </para>
    /// </summary>
    public partial class StudyGroupsAndMemberships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "owner_kind",
                table: "week_assignments",
                type: "text",
                nullable: false,
                defaultValue: "class");

            migrationBuilder.AddColumn<bool>(
                name: "is_groupable",
                table: "subjects",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "group_lessons_enabled",
                table: "school_meta",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "owner_kind",
                table: "schedule_templates",
                type: "text",
                nullable: false,
                defaultValue: "class");

            migrationBuilder.AddColumn<string>(
                name: "owner_kind",
                table: "quarter_grades",
                type: "text",
                nullable: false,
                defaultValue: "class");

            migrationBuilder.AddColumn<string>(
                name: "owner_kind",
                table: "lesson_notes",
                type: "text",
                nullable: false,
                defaultValue: "class");

            migrationBuilder.AddColumn<string>(
                name: "owner_kind",
                table: "journal_entries",
                type: "text",
                nullable: false,
                defaultValue: "class");

            migrationBuilder.CreateTable(
                name: "class_memberships",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    student_id = table.Column<string>(type: "text", nullable: false),
                    class_id = table.Column<string>(type: "text", nullable: false),
                    joined_on = table.Column<DateOnly>(type: "date", nullable: false),
                    left_on = table.Column<DateOnly>(type: "date", nullable: true),
                    leave_reason = table.Column<string>(type: "text", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_class_memberships", x => x.id);
                    table.CheckConstraint("ck_class_memberships_period", "left_on is null or left_on >= joined_on");
                    table.ForeignKey(
                        name: "fk_class_memberships_classes_class_id",
                        column: x => x.class_id,
                        principalTable: "classes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_class_memberships_students_student_id",
                        column: x => x.student_id,
                        principalTable: "students",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_class_memberships_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "study_groups",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    name = table.Column<string>(type: "text", nullable: false),
                    subject_id = table.Column<string>(type: "text", nullable: false),
                    gender = table.Column<string>(type: "text", nullable: true),
                    is_archived = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    archived_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_study_groups", x => x.id);
                    table.UniqueConstraint("ak_study_groups_id_subject_id", x => new { x.id, x.subject_id });
                    table.CheckConstraint("ck_study_groups_gender", "gender in ('male','female')");
                    table.CheckConstraint("ck_study_groups_name", "btrim(name) <> ''");
                    table.ForeignKey(
                        name: "fk_study_groups_subjects_subject_id",
                        column: x => x.subject_id,
                        principalTable: "subjects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_study_groups_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "study_group_classes",
                columns: table => new
                {
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    class_id = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_study_group_classes", x => new { x.group_id, x.class_id });
                    table.ForeignKey(
                        name: "fk_study_group_classes_classes_class_id",
                        column: x => x.class_id,
                        principalTable: "classes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_study_group_classes_study_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "study_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "study_group_members",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_id = table.Column<string>(type: "text", nullable: false),
                    student_id = table.Column<string>(type: "text", nullable: false),
                    joined_on = table.Column<DateOnly>(type: "date", nullable: false),
                    left_on = table.Column<DateOnly>(type: "date", nullable: true),
                    leave_reason = table.Column<string>(type: "text", nullable: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_study_group_members", x => x.id);
                    table.CheckConstraint("ck_study_group_members_period", "left_on is null or left_on >= joined_on");
                    table.ForeignKey(
                        name: "fk_study_group_members_students_student_id",
                        column: x => x.student_id,
                        principalTable: "students",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    // QO'LDA QO'SHILGAN: `onUpdate: Cascade`. EF modelida "update"
                    // harakati yo'q, migratsiya operatsiyasida esa bor. Guruhning
                    // fani o'zgarsa a'zolardagi nusxa ham o'zgaradi va
                    // `ux_group_members_one_group_per_subject` YANGI fan bo'yicha
                    // tekshiradi — ya'ni fanni almashtirish "bitta fandan bitta
                    // guruh" qoidasini jimgina buza olmaydi (§3.1).
                    table.ForeignKey(
                        name: "fk_study_group_members_study_groups_group_id_subject_id",
                        columns: x => new { x.group_id, x.subject_id },
                        principalTable: "study_groups",
                        principalColumns: new[] { "id", "subject_id" },
                        onUpdate: ReferentialAction.Cascade,
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_study_group_members_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "study_group_teachers",
                columns: table => new
                {
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    teacher_id = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_study_group_teachers", x => new { x.group_id, x.teacher_id });
                    table.ForeignKey(
                        name: "fk_study_group_teachers_study_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "study_groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_study_group_teachers_teachers_teacher_id",
                        column: x => x.teacher_id,
                        principalTable: "teachers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_week_assignments_owner_kind",
                table: "week_assignments",
                sql: "owner_kind in ('class','group')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_schedule_templates_owner_kind",
                table: "schedule_templates",
                sql: "owner_kind in ('class','group')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_quarter_grades_owner_kind",
                table: "quarter_grades",
                sql: "owner_kind in ('class','group')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_lesson_notes_owner_kind",
                table: "lesson_notes",
                sql: "owner_kind in ('class','group')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_journal_entries_owner_kind",
                table: "journal_entries",
                sql: "owner_kind in ('class','group')");

            migrationBuilder.CreateIndex(
                name: "ix_class_memberships_class",
                table: "class_memberships",
                column: "class_id",
                filter: "left_on is null");

            migrationBuilder.CreateIndex(
                name: "ix_class_memberships_created_by",
                table: "class_memberships",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_class_memberships_student",
                table: "class_memberships",
                column: "student_id");

            migrationBuilder.CreateIndex(
                name: "ux_class_memberships_one_active",
                table: "class_memberships",
                column: "student_id",
                unique: true,
                filter: "left_on is null");

            migrationBuilder.CreateIndex(
                name: "ix_study_group_classes_class",
                table: "study_group_classes",
                column: "class_id");

            migrationBuilder.CreateIndex(
                name: "ix_group_members_student",
                table: "study_group_members",
                column: "student_id");

            migrationBuilder.CreateIndex(
                name: "ix_study_group_members_created_by",
                table: "study_group_members",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_study_group_members_group_id_subject_id",
                table: "study_group_members",
                columns: new[] { "group_id", "subject_id" });

            migrationBuilder.CreateIndex(
                name: "ux_group_members_one_active",
                table: "study_group_members",
                columns: new[] { "group_id", "student_id" },
                unique: true,
                filter: "left_on is null");

            migrationBuilder.CreateIndex(
                name: "ux_group_members_one_group_per_subject",
                table: "study_group_members",
                columns: new[] { "student_id", "subject_id" },
                unique: true,
                filter: "left_on is null");

            migrationBuilder.CreateIndex(
                name: "ix_study_group_teachers_teacher",
                table: "study_group_teachers",
                column: "teacher_id");

            migrationBuilder.CreateIndex(
                name: "ix_study_groups_created_by",
                table: "study_groups",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_study_groups_subject_id",
                table: "study_groups",
                column: "subject_id");

            // ---- Guruh nomi bitta fan ichida takrorlanmaydi (§3.1) ----
            // IFODA indeksi (`lower(name)`) — EF Core uni modellay olmaydi,
            // shuning uchun xom SQL. Snapshot uni ko'rmaydi, ya'ni keyingi
            // `--autogenerate` unga TEGMAYDI ham. Arxivlangan guruhlar
            // tekshiruvdan chetda: o'tgan yilgi "Ingliz A" yangisini bloklamasin.
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX ux_study_groups_name_active "
                + "ON study_groups (subject_id, lower(name)) WHERE NOT is_archived;");

            // ---- `students.class_name` dan sinf a'zoligini to'ldirish ----
            // TARTIB MUHIM: jadval va uning unikal indekslari YARATILGANDAN
            // KEYIN — backfill "bitta faol a'zolik" qoidasiga tayanadi.
            migrationBuilder.Sql(MigrationSql.Read("study_groups_backfill.sql"));

            // ---- Baza darajasidagi grantlar ----
            // Eng oxirida: jadvallar mavjud bo'lgandan keyin.
            migrationBuilder.Sql(MigrationSql.Read("study_groups_guards.sql"));
        }

        /// <summary>
        /// Beshta jadval o'chadi, beshta check constraint va yettita ustun olib
        /// tashlanadi — <c>Up()</c> yaratganning AYNAN o'zi. Ifoda indeksi,
        /// backfill qatorlari va grantlar o'sha jadvallarning ichida edi, ya'ni
        /// alohida qadam talab qilmaydi.
        ///
        /// <para>
        /// <b>Orqaga qaytish MA'LUMOT YO'QOTADIMI?</b> Faqat SHU migratsiya
        /// keltirganini: guruhlar, a'zoliklar va backfill qatorlari.
        /// Migratsiyadan OLDIN mavjud bo'lgan birorta qiymat yo'qolmaydi —
        /// <c>students.class_name</c>, <c>sub_group</c>, jurnal, dars mavzulari
        /// va chorak baholari umuman o'zgartirilmagan.
        /// </para>
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "class_memberships");

            migrationBuilder.DropTable(
                name: "study_group_classes");

            migrationBuilder.DropTable(
                name: "study_group_members");

            migrationBuilder.DropTable(
                name: "study_group_teachers");

            migrationBuilder.DropTable(
                name: "study_groups");

            migrationBuilder.DropCheckConstraint(
                name: "ck_week_assignments_owner_kind",
                table: "week_assignments");

            migrationBuilder.DropCheckConstraint(
                name: "ck_schedule_templates_owner_kind",
                table: "schedule_templates");

            migrationBuilder.DropCheckConstraint(
                name: "ck_quarter_grades_owner_kind",
                table: "quarter_grades");

            migrationBuilder.DropCheckConstraint(
                name: "ck_lesson_notes_owner_kind",
                table: "lesson_notes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_journal_entries_owner_kind",
                table: "journal_entries");

            migrationBuilder.DropColumn(
                name: "owner_kind",
                table: "week_assignments");

            migrationBuilder.DropColumn(
                name: "is_groupable",
                table: "subjects");

            migrationBuilder.DropColumn(
                name: "group_lessons_enabled",
                table: "school_meta");

            migrationBuilder.DropColumn(
                name: "owner_kind",
                table: "schedule_templates");

            migrationBuilder.DropColumn(
                name: "owner_kind",
                table: "quarter_grades");

            migrationBuilder.DropColumn(
                name: "owner_kind",
                table: "lesson_notes");

            migrationBuilder.DropColumn(
                name: "owner_kind",
                table: "journal_entries");
        }
    }
}
