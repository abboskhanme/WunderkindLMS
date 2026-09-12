using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <summary>
    /// Vasiylar (SPEC §3.2) va Telegram Mini App bog'lanishi (SPEC §6 Faza 3).
    ///
    /// <para>
    /// FAQAT QO'SHADI. <c>Up()</c> da birorta ham <c>DROP</c> yo'q — qo'lda o'qib
    /// tekshirilgan. Xususan <c>students.parent_phone</c> va uning yonidagi
    /// ota-ona ustunlariga TEGILMAGAN: P1-21 endigina katta "eski moliyani yopish"
    /// ishini tugatdi va ikkinchi retirement bu vazifaning ichida emas
    /// (docs/PENDING_WIRING.md, T3.3).
    /// </para>
    ///
    /// <para>
    /// Beshta jadval qo'shiladi: <c>guardians</c>, <c>student_guardians</c>,
    /// <c>telegram_accounts</c>, <c>telegram_link_codes</c>, <c>chat_reads</c>.
    /// Ularga uchta narsa EF modelidan (GuardianModel.cs) keladi va shuning uchun
    /// snapshot'da ko'rinadi — check constraint'lar, qisman unikal indekslar va
    /// <c>guardians.phone_key</c> generated stored column'i. Xom SQL'da faqat EF
    /// ifodalay olmaydigan ikki narsa qoldi:
    /// </para>
    /// <list type="number">
    ///   <item><c>Sql/guardians_backfill.sql</c> — mavjud <c>parent_phone</c>
    ///     qiymatlarini vasiylarga ko'chirish (bitta bog'lanish ham yo'qolmasin);</item>
    ///   <item><c>Sql/guardian_guards.sql</c> — <c>app_rw</c> uchun GRANT/REVOKE.
    ///     Busiz ilova birinchi so'rovda SQLSTATE 42501 bilan yiqiladi.</item>
    /// </list>
    ///
    /// <para>
    /// Ikkala fayl ham EMBEDDED RESOURCE — chop etilgan konteynerda manba papkasi yo'q.
    /// </para>
    /// </summary>
    public partial class GuardiansAndTelegramLink : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "chat_reads",
                columns: table => new
                {
                    user_id = table.Column<string>(type: "text", nullable: false),
                    channel = table.Column<string>(type: "text", nullable: false),
                    read_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_chat_reads", x => new { x.user_id, x.channel });
                    table.ForeignKey(
                        name: "fk_chat_reads_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "guardians",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: true),
                    full_name = table.Column<string>(type: "text", nullable: false),
                    phone = table.Column<string>(type: "text", nullable: false),
                    phone_key = table.Column<string>(type: "text", nullable: false, computedColumnSql: "right(regexp_replace(phone, '[^0-9]', '', 'g'), 9)", stored: true),
                    passport_url = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_guardians", x => x.id);
                    table.CheckConstraint("ck_guardians_full_name", "btrim(full_name) <> ''");
                    table.CheckConstraint("ck_guardians_phone", "btrim(phone) <> ''");
                    table.ForeignKey(
                        name: "fk_guardians_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "telegram_accounts",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    telegram_user_id = table.Column<long>(type: "bigint", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    username = table.Column<string>(type: "text", nullable: true),
                    display_name = table.Column<string>(type: "text", nullable: false),
                    linked_by_user_id = table.Column<string>(type: "text", nullable: true),
                    linked_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    last_seen_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_telegram_accounts", x => x.id);
                    table.CheckConstraint("ck_telegram_accounts_user_id", "telegram_user_id > 0");
                    table.ForeignKey(
                        name: "fk_telegram_accounts_users_linked_by_user_id",
                        column: x => x.linked_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "fk_telegram_accounts_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "telegram_link_codes",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    code_hash = table.Column<string>(type: "text", nullable: false),
                    user_id = table.Column<string>(type: "text", nullable: false),
                    created_by_user_id = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    expires_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false),
                    used_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: true),
                    used_by_telegram_user_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_telegram_link_codes", x => x.id);
                    table.CheckConstraint("ck_telegram_link_codes_used", "(used_at is null) = (used_by_telegram_user_id is null)");
                    table.CheckConstraint("ck_telegram_link_codes_window", "expires_at > created_at");
                    table.ForeignKey(
                        name: "fk_telegram_link_codes_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_telegram_link_codes_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "student_guardians",
                columns: table => new
                {
                    student_id = table.Column<string>(type: "text", nullable: false),
                    guardian_id = table.Column<string>(type: "text", nullable: false),
                    relation = table.Column<string>(type: "text", nullable: false),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_student_guardians", x => new { x.student_id, x.guardian_id });
                    table.CheckConstraint("ck_student_guardians_relation", "relation in ('parent','grandparent','trustee')");
                    table.ForeignKey(
                        name: "fk_student_guardians_guardians_guardian_id",
                        column: x => x.guardian_id,
                        principalTable: "guardians",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_student_guardians_students_student_id",
                        column: x => x.student_id,
                        principalTable: "students",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_guardians_phone_key",
                table: "guardians",
                column: "phone_key",
                unique: true,
                filter: "phone_key <> ''");

            migrationBuilder.CreateIndex(
                name: "ix_guardians_user_id",
                table: "guardians",
                column: "user_id",
                unique: true,
                filter: "user_id is not null");

            migrationBuilder.CreateIndex(
                name: "ix_student_guardians_guardian_id",
                table: "student_guardians",
                column: "guardian_id");

            migrationBuilder.CreateIndex(
                name: "ux_student_guardians_one_primary",
                table: "student_guardians",
                column: "student_id",
                unique: true,
                filter: "is_primary");

            migrationBuilder.CreateIndex(
                name: "ix_telegram_accounts_linked_by_user_id",
                table: "telegram_accounts",
                column: "linked_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_telegram_accounts_telegram_user_id",
                table: "telegram_accounts",
                column: "telegram_user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_telegram_accounts_user_id",
                table: "telegram_accounts",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_telegram_link_codes_code_hash",
                table: "telegram_link_codes",
                column: "code_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_telegram_link_codes_created_by_user_id",
                table: "telegram_link_codes",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_telegram_link_codes_user_id_expires_at",
                table: "telegram_link_codes",
                columns: new[] { "user_id", "expires_at" });

            // ---- Eski bog'lanishlarni ko'chirish (SPEC §3.2) ----
            // TARTIB MUHIM: indekslardan KEYIN. Backfill `on conflict do nothing`
            // ga tayanadi va `phone_key` bo'yicha guruhlaydi — ikkalasi ham
            // yuqoridagi unikal indeks va generated column bo'lmasa ishlamaydi.
            migrationBuilder.Sql(MigrationSql.Read("guardians_backfill.sql"));

            // ---- Baza darajasidagi grantlar ----
            // Eng oxirida: jadvallar mavjud bo'lgandan keyin.
            migrationBuilder.Sql(MigrationSql.Read("guardian_guards.sql"));
        }

        /// <summary>
        /// Beshta jadval o'chadi; grantlar ular bilan birga ketadi (alohida REVOKE
        /// kerak emas). <c>students.parent_phone</c> hech qachon o'zgartirilmagani
        /// uchun tiklanadigan narsa ham yo'q — orqaga qaytish MA'LUMOT YO'QOTMAYDI:
        /// vasiylar jadvali to'liq shu ustundan hosil qilingan.
        /// </summary>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "chat_reads");

            migrationBuilder.DropTable(
                name: "student_guardians");

            migrationBuilder.DropTable(
                name: "telegram_accounts");

            migrationBuilder.DropTable(
                name: "telegram_link_codes");

            migrationBuilder.DropTable(
                name: "guardians");
        }
    }
}
