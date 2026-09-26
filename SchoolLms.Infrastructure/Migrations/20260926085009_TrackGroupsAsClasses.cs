using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SchoolLms.Infrastructure.Migrations
{
    /// <summary>
    /// Yo'nalish guruhlari sinf kabi — docs/modules/track-groups-as-classes.md (2026-09-26).
    ///
    /// <para>
    /// Yo'nalish guruhining (<c>is_track</c>) fani YO'Q: <c>study_groups.subject_id</c> va uning
    /// a'zolardagi nusxasi (<c>study_group_members.subject_id</c>) NULL bo'la oladi.
    /// </para>
    ///
    /// <para><b>QO'LDA TAHRIRLANGAN — autogenerate nima qilmoqchi edi va nega qilinmadi:</b></para>
    /// <list type="bullet">
    ///   <item>EF <c>ak_study_groups_id_subject_id</c> unikal constraint'ini va unga qaragan kompozit
    ///     FK <c>fk_study_group_members_study_groups_group_id_subject_id</c> (ON UPDATE CASCADE) ni
    ///     O'CHIRMOQCHI edi — ular modeldan chiqdi, chunki EF kalit ustunining null bo'lishiga yo'l
    ///     qo'ymaydi. Ular BAZADA QOLADI: nullable ustunli unikal constraint va MATCH SIMPLE FK
    ///     PostgreSQL'da to'liq ishlaydi — oddiy guruh a'zosining fani guruh fanidan
    ///     uzoqlasha olmaydi, yo'nalish guruhi a'zosining (NULL) qatori esa tekshirilmaydi.
    ///     Kompozit FK indeksi (<c>ix_study_group_members_group_id_subject_id</c>) ham qoladi.</item>
    ///   <item>Guruhga bog'lanishni endi oddiy <c>group_id</c> FK ushlaydi (yangi).</item>
    /// </list>
    ///
    /// <para><b>MA'LUMOT:</b> mavjud yo'nalish guruhlarining (prod: 5 ta) tasodifiy fani tozalanadi
    /// (a'zolar nusxasi bilan) — a'zolar, sinflar, o'qituvchilar joyida qoladi.
    /// <c>ck_study_groups_subject</c>: yo'nalish ⇔ fansiz.</para>
    ///
    /// <para><b>Down():</b> fansiz guruhga (va a'zolariga) mavjud fanlardan birinchisi (id bo'yicha)
    /// qo'yiladi va ustunlar yana NOT NULL bo'ladi — asl tasodifiy fan tiklanmaydi.</para>
    /// </summary>
    public partial class TrackGroupsAsClasses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "subject_id",
                table: "study_groups",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "subject_id",
                table: "study_group_members",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddForeignKey(
                name: "fk_study_group_members_study_groups_group_id",
                table: "study_group_members",
                column: "group_id",
                principalTable: "study_groups",
                principalColumn: "id",
                onDelete: ReferentialAction.Cascade);

            // Yo'nalish guruhlari fansiz. Kompozit FK (ON UPDATE CASCADE) a'zolar nusxasini
            // o'zi NULL qiladi; ikkinchi UPDATE — shunchaki kafolat (idempotent).
            migrationBuilder.Sql("UPDATE study_groups SET subject_id = NULL WHERE is_track;");
            migrationBuilder.Sql(
                "UPDATE study_group_members m SET subject_id = NULL "
                + "FROM study_groups g WHERE m.group_id = g.id AND g.is_track AND m.subject_id IS NOT NULL;");

            migrationBuilder.AddCheckConstraint(
                name: "ck_study_groups_subject",
                table: "study_groups",
                sql: "(is_track and subject_id is null) or (not is_track and subject_id is not null)");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_study_groups_subject",
                table: "study_groups");

            // NOT NULL ga qaytishdan oldin fansiz qatorlarga fan kerak (asl fan tiklanmaydi).
            migrationBuilder.Sql(
                "UPDATE study_groups SET subject_id = (SELECT id FROM subjects ORDER BY id LIMIT 1) "
                + "WHERE subject_id IS NULL;");
            migrationBuilder.Sql(
                "UPDATE study_group_members m SET subject_id = g.subject_id "
                + "FROM study_groups g WHERE m.group_id = g.id AND m.subject_id IS NULL;");

            migrationBuilder.DropForeignKey(
                name: "fk_study_group_members_study_groups_group_id",
                table: "study_group_members");

            migrationBuilder.AlterColumn<string>(
                name: "subject_id",
                table: "study_groups",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "subject_id",
                table: "study_group_members",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);
        }
    }
}
