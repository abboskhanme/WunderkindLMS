using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;

namespace SchoolLms.Infrastructure.Data;

/// <summary>
/// O'quv bo'limi pariteti — P2 sxemasining EF konfiguratsiyasi
/// (<c>docs/modules/students-parity.md</c> §3.3, Batch C, migratsiya
/// <c>StudentsParityP2</c>): sinf sig'imi (C-4), fan rangi va faolligi (F-3),
/// mo'ljaldagi sinf darajasi (S-9), sertifikatning bir nechta fani (Z-3),
/// turlangan joylashuvlar (L-2), jadval ko'rinishi sozlamalari (X-1),
/// shartnoma raqamlash rejimi (K-6) va topshiriq egasi (G-20).
///
/// <para>
/// Bundan tashqari, §3.3 da YO'Q, lekin oldingi slice'lar so'ragan bitta
/// ustun: <see cref="Room.IsActive"/> — xonani o'chirmasdan ro'yxatdan
/// olib qo'yish (<c>RoomTests.Sinf_korsatgan_xona_ochirilmaydi</c> dagi
/// izoh aynan shu yerni ko'rsatadi).
/// </para>
///
/// <para>
/// <b>Nega alohida fayl?</b> <see cref="StudentsParityModel"/> va
/// <see cref="StudyGroupModel"/> bilan bir xil sabab:
/// <see cref="AppDbContext.OnModelCreating"/> konflikt maydoniga
/// aylanmasligi kerak — P2 to'lqinida yana bir nechta slice parallel
/// ishlaydi va ularning har biri shu faylga emas, o'z faylига tegadi.
/// </para>
///
/// <para>
/// <b>HAMMASI QO'SHIMCHA.</b> Birorta mavjud ustun ko'chirilmadi,
/// o'chirilmadi va turi o'zgartirilmadi. Mavjud jadvalga qo'shilgan har bir
/// NOT NULL ustunning DEFAULT'i bugungi ma'noni SAQLAYDI:
/// <c>assignments.owner_kind = 'class'</c>, <c>subjects.is_active = true</c>,
/// <c>rooms.is_active = true</c>, <c>school_meta.contract_number_mode =
/// 'auto'</c>. Qolgan uchtasi (<c>classes.capacity</c>,
/// <c>subjects.color</c>, <c>students.target_grade</c>) —
/// <c>null</c> bo'la oladi, ya'ni "ko'rsatilmagan".
/// </para>
///
/// <para>
/// <b>Nega constraint va DEFAULT'lar EF modelida, xom SQL'da emas?</b>
/// Modeldagi narsa snapshot'ga tushadi, ya'ni keyingi
/// <c>--autogenerate</c> uni "ortiqcha" deb hisoblab DROP qilmaydi
/// (<see cref="ParityModel"/> dagi bir xil sabab). Xom SQL'da faqat EF
/// ifodalay olmaydigan narsa qoladi: <c>app_rw</c> uchun GRANT
/// (<c>Migrations/Sql/students_parity_p2_guards.sql</c>) va eski ustundan
/// to'ldirish (<c>Migrations/Sql/students_parity_p2_backfill.sql</c>).
/// </para>
/// </summary>
internal static class StudentsParityP2Model
{
    /// <summary>
    /// <c>assignments.owner_kind</c> check'ining ifodasi —
    /// <see cref="StudyGroupModel"/> dagi beshta dars jadvali bilan AYNAN
    /// bir xil satr. G-20 ning butun ma'nosi shunda: topshiriq ham dars
    /// bilan bir xil "ega" tushunchasini ishlatadi.
    /// </summary>
    private const string OwnerKindCheck = "owner_kind in ('class','group')";

    public static void Apply(ModelBuilder b)
    {
        ConfigureClassCapacity(b);
        ConfigureSubjectColumns(b);
        ConfigureStudentTargetGrade(b);
        ConfigureCertificateSubjects(b);
        ConfigureStudentLocations(b);
        ConfigureUserTableSettings(b);
        ConfigureSchoolMetaContractNumberMode(b);
        ConfigureAssignmentOwnerKind(b);
        ConfigureRoomIsActive(b);
    }

    // =====================================================================
    //  C-4 — sinf sig'imi
    // =====================================================================

    private static void ConfigureClassCapacity(ModelBuilder b) =>
        b.Entity<SchoolClass>().ToTable(t =>
            // null = chek yo'q (uch qiymatli mantiq CHECK'dan o'tkazadi).
            // Nolinchi sig'im esa "hech kim sig'maydi" degani — bunday sinf
            // ma'nosiz va u faqat ma'lumot kiritishdagi xato bo'lishi mumkin.
            t.HasCheckConstraint("ck_classes_capacity", "capacity is null or capacity > 0"));

    // =====================================================================
    //  F-3 — fan rangi va faolligi
    // =====================================================================

    private static void ConfigureSubjectColumns(ModelBuilder b)
    {
        b.Entity<Subject>(e =>
        {
            // MAVJUD HAR BIR FAN `true` oladi — `ADD COLUMN ... NOT NULL
            // DEFAULT true` eski qatorlarni shu qiymat bilan to'ldiradi.
            //
            // `HasSentinel(true)` — EF Core 8+ ning "sukut qiymati" tuzog'i
            // (`StudentStatus.IsActive` dagi bir xil izoh): sentinel'siz
            // `IsActive = false` CLR nolining o'zi bo'lib qoladi, EF uni
            // INSERT'dan TASHLAB KETADI va baza DEFAULT'i (true) g'olib
            // chiqadi — ya'ni "arxivlangan" fan jimgina faol bo'lib yozilardi.
            e.Property(x => x.IsActive).HasDefaultValue(true).HasSentinel(true);

            // Rang — `#RRGGBB` yoki umuman yo'q. `student_statuses.color`
            // bilan AYNAN bir xil ifoda: ikkita ekran bitta rang tanlagichni
            // baham ko'radi.
            e.ToTable(t => t.HasCheckConstraint(
                "ck_subjects_color", "color is null or color ~ '^#[0-9a-fA-F]{6}$'"));
        });
    }

    // =====================================================================
    //  S-9 — mo'ljaldagi sinf darajasi
    // =====================================================================

    private static void ConfigureStudentTargetGrade(ModelBuilder b) =>
        b.Entity<Student>().ToTable(t =>
            // §3.3: `check (target_grade between 0 and 11)`. 0 = maktabgacha
            // tayyorlov. null = ko'rsatilmagan va CHECK'dan o'tadi.
            t.HasCheckConstraint(
                "ck_students_target_grade", "target_grade between 0 and 11"));

    // =====================================================================
    //  Z-3 — sertifikatning bir nechta fani
    // =====================================================================

    private static void ConfigureCertificateSubjects(ModelBuilder b)
    {
        b.Entity<CertificateSubject>(e =>
        {
            // Kompozit kalit — bitta juftlik ikki marta yozilmaydi va
            // alohida `id` ustuni kerak emas (`study_group_classes` naqshi).
            e.HasKey(x => new { x.CertificateId, x.SubjectId });

            // Hujjat o'chirilsa fanlari ham ketadi: bog'lanish hujjatsiz
            // ma'nosiz. `certificates` ning o'zi o'quvchi bilan birga
            // cascade bo'ladi (ParityModel), ya'ni zanjir uzilmaydi.
            e.HasOne<Certificate>().WithMany().HasForeignKey(x => x.CertificateId)
                .OnDelete(DeleteBehavior.Cascade);

            // Fan ISHLATILGAN bo'lsa o'chirilmaydi — `certificates.type_id`
            // dagi bir xil qoida. Endi F-3 tufayli fanni o'chirishning
            // kerak ham emas: `is_active = false` yetadi.
            e.HasOne<Subject>().WithMany().HasForeignKey(x => x.SubjectId)
                .OnDelete(DeleteBehavior.Restrict);

            // "Shu fan bo'yicha sertifikati bor bolalar" filtri (§2.7.3) —
            // kompozit kalitning ikkinchi ustuni bo'yicha alohida indeks,
            // chunki kalitning o'zi faqat birinchi ustundan boshlab ishlaydi.
            e.HasIndex(x => x.SubjectId).HasDatabaseName("ix_certificate_subjects_subject");
        });
    }

    // =====================================================================
    //  L-2 — turlangan joylashuvlar
    // =====================================================================

    private static void ConfigureStudentLocations(ModelBuilder b)
    {
        b.Entity<StudentLocation>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");

            // §3.3: `numeric(9,6)`. `students.latitude/longitude` esa
            // `double precision` — ular TEGILMAGAN eski ustunlar va bu
            // jadval ularning o'rniga emas, yoniga qo'shiladi
            // (StudentLocations.cs dagi izoh).
            e.Property(x => x.Lat).HasPrecision(9, 6);
            e.Property(x => x.Lng).HasPrecision(9, 6);

            // O'quvchi o'chirilsa nuqtalari ham ketadi.
            e.HasOne<Student>().WithMany().HasForeignKey(x => x.StudentId)
                .OnDelete(DeleteBehavior.Cascade);

            // §3.3: `unique (student_id, kind)` — har turdan bittadan,
            // ya'ni "uchtagacha joylashuv".
            e.HasIndex(x => new { x.StudentId, x.Kind })
                .IsUnique().HasDatabaseName("ux_student_locations_student_kind");

            e.ToTable(t =>
            {
                t.HasCheckConstraint(
                    "ck_student_locations_kind", "kind in ('home','school','pickup')");

                // Koordinata dunyoning ichida bo'lsin: almashtirilgan
                // kenglik/uzunlik (Toshkent ~41.3, 69.2) aynan shu yerda
                // ushlanadi — 69 daraja kenglik Norvegiyaning shimoli.
                t.HasCheckConstraint(
                    "ck_student_locations_lat", "lat between -90 and 90");
                t.HasCheckConstraint(
                    "ck_student_locations_lng", "lng between -180 and 180");

                // Oraliq teskari bo'lmasin. Ikkalasi ham null bo'lishi
                // mumkin (vaqt ko'rsatilmagan).
                t.HasCheckConstraint(
                    "ck_student_locations_pickup_window",
                    "pickup_from is null or pickup_to is null or pickup_to >= pickup_from");
            });
        });
    }

    // =====================================================================
    //  X-1 — jadval ko'rinishi sozlamalari
    // =====================================================================

    private static void ConfigureUserTableSettings(ModelBuilder b)
    {
        b.Entity<UserTableSetting>(e =>
        {
            // §3.3: `pk (user_id, page)`.
            e.HasKey(x => new { x.UserId, x.Page });

            // Foydalanuvchi o'chirilsa sozlamalari ham ketadi — ular faqat
            // o'shanga tegishli (UserTableSettings.cs dagi izoh).
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // `jsonb` — yaroqsiz JSON INSERT paytida rad etiladi
            // (`audit_log` dagi bir xil sabab). Sukut `{}` = "hech narsa
            // o'zgartirilmagan", ya'ni ekran o'z sukut ko'rinishini beradi.
            e.Property(x => x.Settings).HasColumnType("jsonb").HasDefaultValueSql("'{}'::jsonb");
            e.Property(x => x.UpdatedAt).HasDefaultValueSql("now()");

            e.ToTable(t =>
                // Bo'sh ekran kaliti — kalitsiz qator, ya'ni uni hech qachon
                // qaytarib o'qib bo'lmaydi.
                t.HasCheckConstraint("ck_user_table_settings_page", "btrim(page) <> ''"));
        });
    }

    // =====================================================================
    //  K-6 — shartnoma raqamlash rejimi
    // =====================================================================

    private static void ConfigureSchoolMetaContractNumberMode(ModelBuilder b)
    {
        b.Entity<SchoolMeta>(e =>
        {
            // Mavjud qator(lar) `auto` oladi — §3.3 talabi va K-1
            // reyestrining bugungi kutilgan xatti-harakati.
            e.Property(x => x.ContractNumberMode).HasDefaultValue(ContractNumberMode.Auto);

            e.ToTable(t => t.HasCheckConstraint(
                "ck_school_meta_contract_number_mode",
                "contract_number_mode in ('auto','manual')"));
        });
    }

    // =====================================================================
    //  G-20 — topshiriq egasi (sinf yoki guruh)
    // =====================================================================

    private static void ConfigureAssignmentOwnerKind(ModelBuilder b)
    {
        b.Entity<Assignment>(e =>
        {
            // MAVJUD HAR BIR TOPSHIRIQ `'class'` oladi.
            // `ADD COLUMN ... NOT NULL DEFAULT 'class'` — PostgreSQL 11+ da
            // faqat katalog yozuvi, ya'ni `assignments` qayta YOZILMAYDI
            // (§3.1 ning 7-bandidagi bir xil izoh).
            e.Property(x => x.OwnerKind).HasDefaultValue(LessonOwnerKind.Class);

            e.ToTable(t => t.HasCheckConstraint("ck_assignments_owner_kind", OwnerKindCheck));
        });
    }

    // =====================================================================
    //  §3.3 dan tashqari — xonani faolsizlantirish
    // =====================================================================

    private static void ConfigureRoomIsActive(ModelBuilder b) =>
        // MAVJUD HAR BIR XONA `true` oladi. `HasSentinel(true)` —
        // `subjects.is_active` dagi bir xil tuzoq: usiz "faol emas" xona
        // jimgina faol bo'lib yozilardi.
        b.Entity<Room>().Property(x => x.IsActive).HasDefaultValue(true).HasSentinel(true);
}
