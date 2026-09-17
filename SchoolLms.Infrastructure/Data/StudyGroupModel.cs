using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;

namespace SchoolLms.Infrastructure.Data;

/// <summary>
/// O'quv guruhlari va sinf a'zoligining EF konfiguratsiyasi —
/// <c>docs/modules/students-parity.md</c> §3.1 (Batch A, migratsiya
/// <c>StudyGroupsAndMemberships</c>).
///
/// <para>
/// <b>Nega alohida fayl?</b> <see cref="ParityModel"/> bilan bir xil sabab:
/// <see cref="AppDbContext.OnModelCreating"/> umumiy fayl va konflikt maydoniga
/// aylanmasligi kerak. <see cref="AppDbContext"/> da bitta chaqiruv qoladi.
/// </para>
///
/// <para>
/// <b>EF ifodalay olmaydigan uch narsa migratsiyaning o'zida (xom SQL):</b>
/// </para>
/// <list type="number">
///   <item><c>ux_study_groups_name_active</c> — <c>lower(name)</c> ustidagi
///     IFODA indeksi. EF Core ifoda indeksini modellay olmaydi; indeks
///     snapshot'da yo'q, ya'ni keyingi <c>--autogenerate</c> uni ko'rmaydi va
///     unga TEGMAYDI.</item>
///   <item><c>study_group_members → study_groups (id, subject_id)</c> FK'sidagi
///     <c>ON UPDATE CASCADE</c>. EF modelida "update" harakati yo'q; migratsiya
///     operatsiyasi uni qo'llab-quvvatlaydi va u yerda qo'lda qo'yilgan.</item>
///   <item><c>class_memberships</c> backfill'i va <c>app_rw</c> grantlari
///     (<c>Migrations/Sql/study_groups_*.sql</c>).</item>
/// </list>
/// </summary>
internal static class StudyGroupModel
{
    /// <summary>Beshta dars jadvalidagi <c>owner_kind</c> check'ining ifodasi.</summary>
    private const string OwnerKindCheck = "owner_kind in ('class','group')";

    public static void Apply(ModelBuilder b)
    {
        ConfigureStudyGroups(b);
        ConfigureStudyGroupClasses(b);
        ConfigureStudyGroupTeachers(b);
        ConfigureStudyGroupMembers(b);
        ConfigureClassMemberships(b);
        ConfigureSubjectFlag(b);
        ConfigureLessonOwnerKind(b);
        ConfigureSchoolMetaFlag(b);
    }

    // =====================================================================
    //  1. study_groups
    // =====================================================================

    private static void ConfigureStudyGroups(ModelBuilder b)
    {
        b.Entity<StudyGroup>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");

            // `unique (id, subject_id)` — a'zolar jadvalidagi kompozit FK'ning
            // nishoni. EF buni "alternate key" deb ataydi va kalit ustunini
            // kuzatuv orqali o'zgartirishga yo'l qo'ymaydi: guruh fanini
            // almashtirish `ExecuteUpdate` bilan bo'ladi (StudyGroups.cs).
            e.HasAlternateKey(x => new { x.Id, x.SubjectId });

            e.Property(x => x.IsArchived).HasDefaultValue(false);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");

            // Fan ISHLATILGAN bo'lsa o'chirilmaydi — aks holda guruh fansiz
            // qolardi va "bitta fandan bitta guruh" qoidasi ma'nosini yo'qotardi.
            e.HasOne<Subject>().WithMany().HasForeignKey(x => x.SubjectId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.CreatedBy)
                .OnDelete(DeleteBehavior.Restrict);

            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_study_groups_name", "btrim(name) <> ''");
                t.HasCheckConstraint("ck_study_groups_gender", "gender in ('male','female')");
            });
        });
    }

    // =====================================================================
    //  2. study_group_classes — guruhni boqadigan sinflar
    // =====================================================================

    private static void ConfigureStudyGroupClasses(ModelBuilder b)
    {
        b.Entity<StudyGroupClass>(e =>
        {
            e.HasKey(x => new { x.GroupId, x.ClassId });

            e.HasOne<StudyGroup>().WithMany().HasForeignKey(x => x.GroupId)
                .OnDelete(DeleteBehavior.Cascade);

            // RESTRICT (§3.1): guruhni boqayotgan sinf o'chirilmaydi. Bugun
            // guruh yo'q, ya'ni bu hech bir mavjud oqimni o'zgartirmaydi; guruh
            // paydo bo'lgach sinfni o'chirish va yil yakunidagi rollover buni
            // oldindan tekshirishi kerak (G-11).
            e.HasOne<SchoolClass>().WithMany().HasForeignKey(x => x.ClassId)
                .OnDelete(DeleteBehavior.Restrict);

            // "Shu sinf qaysi guruhlarni boqadi" — PK teskari tartibda.
            e.HasIndex(x => x.ClassId).HasDatabaseName("ix_study_group_classes_class");
        });
    }

    // =====================================================================
    //  3. study_group_teachers
    // =====================================================================

    private static void ConfigureStudyGroupTeachers(ModelBuilder b)
    {
        b.Entity<StudyGroupTeacher>(e =>
        {
            e.HasKey(x => new { x.GroupId, x.TeacherId });

            e.HasOne<StudyGroup>().WithMany().HasForeignKey(x => x.GroupId)
                .OnDelete(DeleteBehavior.Cascade);

            // RESTRICT (§3.1) — guruhga biriktirilgan o'qituvchi o'chirilmaydi.
            e.HasOne<Teacher>().WithMany().HasForeignKey(x => x.TeacherId)
                .OnDelete(DeleteBehavior.Restrict);

            // "O'qituvchining guruhlari" — PK teskari tartibda.
            e.HasIndex(x => x.TeacherId).HasDatabaseName("ix_study_group_teachers_teacher");
        });
    }

    // =====================================================================
    //  4. study_group_members — sanali a'zolik
    // =====================================================================

    private static void ConfigureStudyGroupMembers(ModelBuilder b)
    {
        b.Entity<StudyGroupMember>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");

            // Kompozit FK: a'zodagi `subject_id` guruh fanidan uzoqlasha olmaydi.
            // `ON UPDATE CASCADE` migratsiyada qo'lda qo'yilgan (EF modelida
            // "update" harakati yo'q) — guruh fani o'zgarsa nusxa ham o'zgaradi
            // va quyidagi unikal indeks YANGI fan bo'yicha tekshiradi.
            e.HasOne<StudyGroup>().WithMany()
                .HasForeignKey(x => new { x.GroupId, x.SubjectId })
                .HasPrincipalKey(g => new { g.Id, g.SubjectId })
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne<Student>().WithMany().HasForeignKey(x => x.StudentId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.CreatedBy)
                .OnDelete(DeleteBehavior.Restrict);

            // Bitta guruhda bitta o'quvchining ko'pi bilan bitta faol a'zoligi.
            e.HasIndex(x => new { x.GroupId, x.StudentId }, "ux_group_members_one_active")
                .IsUnique().HasFilter("left_on is null");

            // MIJOZ SAVOLI Q1 ning "javob bo'lmasa" qarori — BAZA KAFOLATI:
            // bitta o'quvchi bitta fandan ko'pi bilan BITTA faol guruhda.
            // Mijoz "ha" desa aynan shu indeks olib tashlanadi (§5 Q1).
            e.HasIndex(x => new { x.StudentId, x.SubjectId }, "ux_group_members_one_group_per_subject")
                .IsUnique().HasFilter("left_on is null");

            // O'quvchi kartochkasidagi "Sinf va guruhlar" tarixi (G-10).
            e.HasIndex(x => x.StudentId, "ix_group_members_student");

            e.ToTable(t => t.HasCheckConstraint(
                "ck_study_group_members_period", "left_on is null or left_on >= joined_on"));
        });
    }

    // =====================================================================
    //  5. class_memberships — sinfdagi sanali a'zolik
    // =====================================================================

    private static void ConfigureClassMemberships(ModelBuilder b)
    {
        b.Entity<ClassMembership>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");

            e.HasOne<Student>().WithMany().HasForeignKey(x => x.StudentId)
                .OnDelete(DeleteBehavior.Cascade);

            // CASCADE — §3.1 dagi RESTRICT EMAS, ATAYLAB.
            //
            // Backfill har sinfli o'quvchiga qator yozadi. Bugungi ikki oqim
            // sinfni o'chiradi va a'zolik haqida hech narsa bilmaydi:
            //   · `ClassesController.Delete` — faqat FAOL o'quvchi bo'lsa rad
            //     etadi, arxivlangan o'quvchisi bor sinfni o'chiradi;
            //   · `AcademicYearController.Rollover` — 11-sinfni bitirganda
            //     sinf qatorini o'chiradi (moliyasi bor bitiruvchi esa
            //     arxivlanib QOLADI).
            // RESTRICT bilan ikkalasi ham 23503 bilan yiqilardi — rollover
            // butunlay. "Bugun ishlayotgan narsa o'zgarmaydi" qoidasi RESTRICT
            // ni hozircha taqiqlaydi. Bu ikki oqim a'zolikni yopadigan bo'lgach
            // (C1, §4.3) FK bitta kichik migratsiya bilan RESTRICT ga
            // almashtiriladi.
            e.HasOne<SchoolClass>().WithMany().HasForeignKey(x => x.ClassId)
                .OnDelete(DeleteBehavior.Cascade);

            // Yozgan foydalanuvchi o'chirilsa — tarix qoladi, muallif bo'shaydi.
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.CreatedBy)
                .OnDelete(DeleteBehavior.SetNull);

            // BAZA KAFOLATI: bitta o'quvchida ko'pi bilan BITTA faol sinf.
            e.HasIndex(x => x.StudentId, "ux_class_memberships_one_active")
                .IsUnique().HasFilter("left_on is null");

            // Sinf ro'yxati (roster) — faqat faol a'zolar.
            e.HasIndex(x => x.ClassId, "ix_class_memberships_class")
                .HasFilter("left_on is null");

            // O'quvchining to'liq sinf tarixi (G-10: "faol davr", kunlar soni)
            // va o'quvchi o'chirilganda CASCADE qidiruvi. Qisman indeks buni
            // qoplamaydi — u yopilgan qatorlarni ko'rmaydi.
            e.HasIndex(x => x.StudentId, "ix_class_memberships_student");

            e.ToTable(t => t.HasCheckConstraint(
                "ck_class_memberships_period", "left_on is null or left_on >= joined_on"));
        });
    }

    // =====================================================================
    //  6. subjects.is_groupable
    // =====================================================================

    private static void ConfigureSubjectFlag(ModelBuilder b) =>
        // Mavjud HAR BIR fan `false` oladi — guruhli fanni maktab o'zi belgilaydi.
        b.Entity<Subject>().Property(x => x.IsGroupable).HasDefaultValue(false);

    // =====================================================================
    //  7. owner_kind — beshta dars jadvali
    // =====================================================================

    /// <summary>
    /// <c>ADD COLUMN owner_kind text NOT NULL DEFAULT 'class'</c> — PostgreSQL
    /// 11+ da doimiy DEFAULT faqat katalogga yoziladi (jadval qayta
    /// yozilmaydi), ya'ni eng katta jadval <c>journal_entries</c> ham bir
    /// lahzada o'zgaradi va MAVJUD HAR BIR qator <c>'class'</c> ni o'qiydi.
    /// CHECK constraint jadvalni bir marta O'QIB tekshiradi (yozmaydi).
    /// </summary>
    private static void ConfigureLessonOwnerKind(ModelBuilder b)
    {
        OwnerKind<ScheduleTemplate>(b, "schedule_templates", x => x.OwnerKind);
        OwnerKind<WeekAssignment>(b, "week_assignments", x => x.OwnerKind);
        OwnerKind<JournalEntry>(b, "journal_entries", x => x.OwnerKind);
        OwnerKind<LessonNote>(b, "lesson_notes", x => x.OwnerKind);
        OwnerKind<QuarterGrade>(b, "quarter_grades", x => x.OwnerKind);
    }

    private static void OwnerKind<T>(ModelBuilder b, string table, Expression<Func<T, string>> property)
        where T : class =>
        b.Entity<T>(e =>
        {
            e.Property(property).HasDefaultValue(LessonOwnerKind.Class);
            e.ToTable(t => t.HasCheckConstraint($"ck_{table}_owner_kind", OwnerKindCheck));
        });

    // =====================================================================
    //  8. school_meta.group_lessons_enabled — cut-over o'chirgichi
    // =====================================================================

    private static void ConfigureSchoolMetaFlag(ModelBuilder b) =>
        // §4.3: guruh darslarini maktab cut-over kuni yoqadi, migratsiya emas.
        b.Entity<SchoolMeta>().Property(x => x.GroupLessonsEnabled).HasDefaultValue(false);
}
