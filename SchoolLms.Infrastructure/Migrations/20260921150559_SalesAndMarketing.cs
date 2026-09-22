using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <inheritdoc />
    // ===========================================================================
    //  Savdo va marketing: ommaviy ariza formasi (`/ariza/{slug}`) va
    //  yangiliklar. Spetsifikatsiya: docs/modules/sales-marketing.md §4,
    //  2026-09-21. Vazifa: SM-1 (butun modul shu migratsiyani kutadi).
    // ===========================================================================
    //
    //  UCHTA YANGI JADVAL: `surveys` (§4.1), `survey_submissions` (§4.2),
    //  `news` (§4.3) — uuid PK, `timestamptz`, snake_case.
    //
    //  `leads` GA UCHTA USTUN — FAQAT QO'SHIMCHA (§4.4)
    //  --------------------------------------------------
    //  `source`, `survey_id`, `created_at`. Birorta mavjud ustun o'zgartirilmaydi
    //  va o'chirilmaydi: kanban doskasining dizayni MUZLATILGAN (CLAUDE.md,
    //  `pages/admin/leads/*`) va u ilgarigi maydonlarni chizishda davom etadi.
    //  Uchala `ADD COLUMN` ham PostgreSQL 11+ da metama'lumot darajasida
    //  bajariladi — jadval qayta yozilmaydi.
    //
    //  `created_at` — NULLABLE VA ESKI QATORLAR TO'LDIRILMAYDI (§4.4, Q7)
    //  --------------------------------------------------------------------
    //  `not null default now()` bo'lganda tarixdagi har bir lid migratsiya
    //  vaqti bilan tamg'alanardi — fakt kabi o'qiladigan, lekin fakt bo'lmagan
    //  son. `NULL` = "migratsiyadan oldin yaratilgan, aniq sanasi noma'lum";
    //  voronkaning davr filtri o'sha qatorlarni yashirmasdan, sonini yozib
    //  ko'rsatadi. Yangi qatorlar qiymatni `Lead.CreatedAt` initsializatoridan
    //  oladi (`Broadcast.CreatedAt` naqshi), ya'ni birorta kontroller
    //  tahrirlanmaydi.
    //
    //  `source` — DEFAULT `'manual'`, VA HAR BIR MAVJUD QATOR SHUNI OLADI
    //  --------------------------------------------------------------------
    //  Bu qulaylik emas, haqiqat: bugungi har bir lidni xodim doskada o'z
    //  qo'li bilan kiritgan. `ck_leads_source_survey` esa ikki ustunni
    //  bog'lab turadi — `survey` manbali lidda ariza BO'LISHI SHART, `manual`
    //  da esa BO'LMASLIGI shart. Shu sababdan `survey_id` ning FK'si RESTRICT:
    //  `set null` bo'lganda arizani o'chirish o'sha CHECK'ni buzardi.
    //
    //  MOLIYAVIY EMAS — TO'LIQ CRUD, BIRORTA `REVOKE` YO'Q
    //  ------------------------------------------------------
    //  Uchala jadvalda ham summa, jurnal yozuvi yoki kvitansiya yo'q.
    //  `docs/SPEC.md` §4.1 bu yerga tegishli emas va bu nomlar
    //  `deploy/init-roles.sql` §5 ga QO'SHILMAYDI — sabab
    //  `sales_marketing_guards.sql` ning o'zida.
    //
    //  BU MIGRATSIYADA BIRORTA `DROP` YO'Q — qatorma-qator o'qilgan (§4.4).
    public partial class SalesAndMarketing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "created_at",
                table: "leads",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "source",
                table: "leads",
                type: "text",
                nullable: false,
                defaultValue: "manual");

            migrationBuilder.AddColumn<Guid>(
                name: "survey_id",
                table: "leads",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "news",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    title = table.Column<string>(type: "text", nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    image_url = table.Column<string>(type: "text", nullable: true),
                    for_employee = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    for_parent = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    for_student = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    author_id = table.Column<string>(type: "text", nullable: false),
                    author_name = table.Column<string>(type: "text", nullable: false),
                    telegram_sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    telegram_recipient_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    telegram_sent_count = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    deleted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_news", x => x.id);
                    table.CheckConstraint("ck_news_audience", "for_employee or for_parent or for_student");
                    table.CheckConstraint("ck_news_body", "btrim(body) <> ''");
                    table.CheckConstraint("ck_news_title", "btrim(title) <> ''");
                    table.ForeignKey(
                        name: "fk_news_users_author_id",
                        column: x => x.author_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "surveys",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    slug = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    subtitle = table.Column<string>(type: "text", nullable: true),
                    image_url = table.Column<string>(type: "text", nullable: true),
                    offer_url = table.Column<string>(type: "text", nullable: true),
                    thank_you_text = table.Column<string>(type: "text", nullable: true),
                    stage_id = table.Column<string>(type: "text", nullable: true),
                    show_student_first_name_input = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    show_student_last_name_input = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    show_student_phone_number_input = table.Column<bool>(type: "boolean", nullable: false, defaultValue: false),
                    show_student_grade_input = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    show_student_gender_input = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    created_by = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_surveys", x => x.id);
                    table.CheckConstraint("ck_surveys_name", "btrim(name) <> ''");
                    table.CheckConstraint("ck_surveys_required_toggles", "show_student_gender_input and show_student_grade_input");
                    table.CheckConstraint("ck_surveys_slug", "slug ~ '^[a-z0-9]+(-[a-z0-9]+)*$' and length(slug) between 3 and 60");
                    table.ForeignKey(
                        name: "fk_surveys_lead_stages_stage_id",
                        column: x => x.stage_id,
                        principalTable: "lead_stages",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_surveys_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "survey_submissions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()"),
                    survey_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lead_id = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false, defaultValue: "lead"),
                    parent_first_name = table.Column<string>(type: "text", nullable: false),
                    parent_last_name = table.Column<string>(type: "text", nullable: true),
                    parent_phone = table.Column<string>(type: "text", nullable: false),
                    parent_phone_key = table.Column<string>(type: "text", nullable: false),
                    student_first_name = table.Column<string>(type: "text", nullable: true),
                    student_last_name = table.Column<string>(type: "text", nullable: true),
                    student_phone = table.Column<string>(type: "text", nullable: true),
                    student_grade = table.Column<short>(type: "smallint", nullable: true),
                    student_gender = table.Column<string>(type: "text", nullable: true),
                    ip = table.Column<string>(type: "text", nullable: true),
                    user_agent = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_survey_submissions", x => x.id);
                    table.CheckConstraint("ck_survey_submissions_gender", "student_gender is null or student_gender in ('male','female')");
                    table.CheckConstraint("ck_survey_submissions_grade", "student_grade is null or student_grade between 0 and 11");
                    table.CheckConstraint("ck_survey_submissions_parent", "btrim(parent_first_name) <> '' and btrim(parent_phone) <> ''");
                    table.CheckConstraint("ck_survey_submissions_status", "status in ('lead','duplicate')");
                    table.ForeignKey(
                        name: "fk_survey_submissions_leads_lead_id",
                        column: x => x.lead_id,
                        principalTable: "leads",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_survey_submissions_surveys_survey_id",
                        column: x => x.survey_id,
                        principalTable: "surveys",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_leads_source",
                table: "leads",
                columns: new[] { "source", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_leads_survey",
                table: "leads",
                column: "survey_id",
                filter: "survey_id is not null");

            migrationBuilder.AddCheckConstraint(
                name: "ck_leads_source",
                table: "leads",
                sql: "source in ('manual','survey')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_leads_source_survey",
                table: "leads",
                sql: "(source = 'survey') = (survey_id is not null)");

            migrationBuilder.CreateIndex(
                name: "ix_news_author_id",
                table: "news",
                column: "author_id");

            migrationBuilder.CreateIndex(
                name: "ix_news_feed",
                table: "news",
                column: "published_at",
                descending: new bool[0],
                filter: "deleted_at is null");

            migrationBuilder.CreateIndex(
                name: "ix_survey_submissions_dedupe",
                table: "survey_submissions",
                columns: new[] { "survey_id", "parent_phone_key", "created_at" },
                descending: new[] { false, false, true });

            migrationBuilder.CreateIndex(
                name: "ix_survey_submissions_lead",
                table: "survey_submissions",
                column: "lead_id");

            migrationBuilder.CreateIndex(
                name: "ix_survey_submissions_survey",
                table: "survey_submissions",
                columns: new[] { "survey_id", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_surveys_active",
                table: "surveys",
                columns: new[] { "is_active", "created_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_surveys_created_by",
                table: "surveys",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_surveys_stage_id",
                table: "surveys",
                column: "stage_id");

            migrationBuilder.AddForeignKey(
                name: "fk_leads_surveys_survey_id",
                table: "leads",
                column: "survey_id",
                principalTable: "surveys",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // ---- Slug butun tizimda yagona (§4.1) ----
            // IFODA indeksi (`lower(slug)`) — EF Core uni modellay olmaydi,
            // shuning uchun xom SQL (`ux_study_groups_name_active` naqshi).
            // Snapshot uni ko'rmaydi, ya'ni keyingi `--autogenerate` unga
            // TEGMAYDI ham. `ck_surveys_slug` allaqachon faqat kichik harfga
            // ruxsat beradi — bu indeks o'sha qoida buzilgan taqdirda ham
            // `/ariza/Qabul-2027` va `/ariza/qabul-2027` bitta havolaga
            // aylanib qolishiga yo'l qo'ymaydi.
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX ux_surveys_slug ON surveys (lower(slug));");

            // ---- `app_rw` grantlari ----
            // ENG OXIRIDA — jadvallar mavjud bo'lgandan keyin. Ichida nima bor
            // va NEGA (to'liq CRUD, `REVOKE` yo'q) — `sales_marketing_guards.sql`
            // ning o'zida.
            migrationBuilder.Sql(MigrationSql.Read("sales_marketing_guards.sql"));
        }

        /// <summary>
        /// Uchta jadval o'chadi va <c>leads</c> dagi uchta ustun (ikkita
        /// CHECK va ikkita indeksi bilan) olib tashlanadi — <c>Up()</c>
        /// yaratganning AYNAN o'zi. Ifoda indeksi (<c>ux_surveys_slug</c>) va
        /// grantlar <c>surveys</c> jadvalining ichida edi, ya'ni alohida
        /// qadam talab qilmaydi.
        ///
        /// <para>
        /// <b>Orqaga qaytish MA'LUMOT YO'QOTADIMI?</b> Faqat SHU migratsiya
        /// keltirganini: arizalar, topshirilgan arizalar va yangiliklar.
        /// Migratsiyadan OLDIN mavjud bo'lgan birorta qiymat yo'qolmaydi —
        /// lidlarning o'zi (ismi, telefoni, bosqichi) tegilmaydi, faqat
        /// keyin qo'shilgan uchta ustun ketadi. Ariza orqali kelgan lidlar
        /// doskada QOLADI, lekin qaysi arizadan kelgani va qachon
        /// yaratilgani unutiladi.
        /// </para>
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_leads_surveys_survey_id",
                table: "leads");

            migrationBuilder.DropTable(
                name: "news");

            migrationBuilder.DropTable(
                name: "survey_submissions");

            migrationBuilder.DropTable(
                name: "surveys");

            migrationBuilder.DropIndex(
                name: "ix_leads_source",
                table: "leads");

            migrationBuilder.DropIndex(
                name: "ix_leads_survey",
                table: "leads");

            migrationBuilder.DropCheckConstraint(
                name: "ck_leads_source",
                table: "leads");

            migrationBuilder.DropCheckConstraint(
                name: "ck_leads_source_survey",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "created_at",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "source",
                table: "leads");

            migrationBuilder.DropColumn(
                name: "survey_id",
                table: "leads");
        }
    }
}
