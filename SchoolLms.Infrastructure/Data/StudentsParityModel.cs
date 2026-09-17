using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;

namespace SchoolLms.Infrastructure.Data;

/// <summary>
/// O'quv bo'limi pariteti — P1 sxemasining EF konfiguratsiyasi
/// (<c>docs/modules/students-parity.md</c> §3.2, Batch B, migratsiya
/// <c>StudentsParityP1</c>): holat taglari (S-5), o'quvchi izohlari (S-11),
/// o'quvchining yangi maydonlari va vasiylik turi (S-8), shartnomalar
/// reyestri (K-1) va xonalar (R-1).
///
/// <para>
/// <b>Nega alohida fayl?</b> <see cref="ParityModel"/> va
/// <see cref="StudyGroupModel"/> bilan bir xil sabab:
/// <see cref="AppDbContext.OnModelCreating"/> konflikt maydoniga
/// aylanmasligi kerak — bu to'lqinda oltita slice parallel ishlaydi.
/// </para>
///
/// <para>
/// <b>Hammasi QO'SHIMCHA.</b> Yagona istisno —
/// <c>ck_student_guardians_relation</c> check constraint'i:
/// u KENGAYTIRILADI (eski uchta qiymat joyida qoladi), ya'ni migratsiyadagi
/// <c>drop</c> ma'lumotga emas, constraint'ga tegadi. Ro'yxatning o'zi
/// <see cref="GuardianModel"/> da — ta'rif qayerda bo'lsa, o'zgarish ham
/// o'sha yerda.
/// </para>
/// </summary>
internal static class StudentsParityModel
{
    public static void Apply(ModelBuilder b)
    {
        ConfigureStudentStatuses(b);
        ConfigureStudentColumns(b);
        ConfigureStudentComments(b);
        ConfigureStudentContracts(b);
        ConfigureRooms(b);
    }

    // =====================================================================
    //  S-5 — holat taglari
    // =====================================================================

    private static void ConfigureStudentStatuses(ModelBuilder b)
    {
        b.Entity<StudentStatus>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");

            // Nom — katalogning kaliti: ikkita "VIP" ro'yxatdagi guruhlashni
            // ikkiga bo'lardi.
            e.HasIndex(x => x.Name).IsUnique();

            e.Property(x => x.Position).HasDefaultValue(0);
            e.Property(x => x.IsDefault).HasDefaultValue(false);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");

            // `HasSentinel(true)` — EF Core 8+ ning "sukut qiymati" tuzog'i.
            // Sentinel'siz `IsActive = false` CLR nolining o'zi bo'lib qoladi,
            // EF uni INSERT'dan TASHLAB KETADI va baza DEFAULT'i (true)
            // g'olib chiqadi: "faol emas" holat jimgina faol bo'lib yozilardi.
            // Aynan shu xato 2026-09-17 da `school_meta` da uchradi
            // (docs/ASSUMPTIONS.md) — bu yerda u boshidanoq yopildi.
            e.Property(x => x.IsActive).HasDefaultValue(true).HasSentinel(true);

            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_student_statuses_name", "btrim(name) <> ''");
                t.HasCheckConstraint("ck_student_statuses_position", "position >= 0");
                // Rang — `#RRGGBB` yoki umuman yo'q (§3.2).
                t.HasCheckConstraint(
                    "ck_student_statuses_color", "color is null or color ~ '^#[0-9a-fA-F]{6}$'");
            });
        });
    }

    // =====================================================================
    //  S-8 — o'quvchining yangi ustunlari
    // =====================================================================

    private static void ConfigureStudentColumns(ModelBuilder b)
    {
        b.Entity<Student>(e =>
        {
            // Holat katalogdan o'chirilsa o'quvchi qoladi, tagi bo'shaydi:
            // holat — vaqtinchalik belgi, tarix emas.
            e.HasOne<StudentStatus>().WithMany().HasForeignKey(x => x.StatusId)
                .OnDelete(DeleteBehavior.SetNull);

            // Ro'yxatdagi `statusId` filtri (S-1).
            e.HasIndex(x => x.StatusId, "ix_students_status").HasDatabaseName("ix_students_status");

            // Til — null bo'lishi mumkin (ko'rsatilmagan), lekin yozilsa
            // ro'yxatdan bo'lishi shart. `null` CHECK'dan o'tadi (SQL uch
            // qiymatli mantiq), shuning uchun alohida `is null` shart kerak emas.
            e.ToTable(t => t.HasCheckConstraint(
                "ck_students_language", "language in ('uz','ru','en','kaa')"));
        });
    }

    // =====================================================================
    //  S-11 — o'quvchi izohlari
    // =====================================================================

    private static void ConfigureStudentComments(ModelBuilder b)
    {
        b.Entity<StudentComment>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");

            // O'quvchi o'chirilsa izohlari ham ketadi — izoh o'quvchisiz ma'nosiz.
            e.HasOne<Student>().WithMany().HasForeignKey(x => x.StudentId)
                .OnDelete(DeleteBehavior.Cascade);

            // Muallif o'chirilmaydi (certificates bilan bir xil qoida): "kim
            // yozgan" izohning yarmi.
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.CreatedBy)
                .OnDelete(DeleteBehavior.Restrict);

            // Kartochkadagi "izohlar" bloki — eng yangisi tepada.
            e.HasIndex(x => new { x.StudentId, x.CreatedAt }, "ix_student_comments_student")
                .IsDescending(false, true)
                .HasDatabaseName("ix_student_comments_student");

            e.ToTable(t =>
            {
                t.HasCheckConstraint(
                    "ck_student_comments_kind", "kind in ('positive','negative')");
                t.HasCheckConstraint("ck_student_comments_body", "btrim(body) <> ''");
            });
        });
    }

    // =====================================================================
    //  K-1 — o'quvchi shartnomalari
    // =====================================================================

    private static void ConfigureStudentContracts(ModelBuilder b)
    {
        b.Entity<StudentContract>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");

            e.HasOne<Student>().WithMany().HasForeignKey(x => x.StudentId)
                .OnDelete(DeleteBehavior.Cascade);

            // Shablon o'chirilsa yozuv qoladi: fayl va raqam baribir qo'lda
            // ham kiritilishi mumkin, shablon esa faqat "qayerdan hosil bo'ldi".
            e.HasOne<ContractTemplate>().WithMany().HasForeignKey(x => x.TemplateId)
                .OnDelete(DeleteBehavior.SetNull);

            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.CreatedBy)
                .OnDelete(DeleteBehavior.Restrict);

            // Raqam unikal — LEKIN faqat mavjud bo'lganda: raqamsiz (hali
            // rasmiylashtirilmagan) yozuv bir nechta bo'lishi mumkin.
            e.HasIndex(x => x.Number, "ux_student_contracts_number")
                .IsUnique().HasFilter("number is not null")
                .HasDatabaseName("ux_student_contracts_number");

            // Kartochkadagi "shartnomalar" tab'i — eng yangisi tepada.
            e.HasIndex(x => new { x.StudentId, x.SignedOn }, "ix_student_contracts_student")
                .IsDescending(false, true)
                .HasDatabaseName("ix_student_contracts_student");

            e.ToTable(t =>
            {
                t.HasCheckConstraint(
                    "ck_student_contracts_source", "source in ('generated','uploaded')");
                t.HasCheckConstraint(
                    "ck_student_contracts_period",
                    "ends_on is null or signed_on is null or ends_on >= signed_on");
                // Bo'sh satrli raqam yuqoridagi qisman unikal indeksni chetlab
                // o'tardi (`'' is not null`) va ikkinchi "raqamsiz" yozuvda
                // 23505 bergan bo'lardi. Ya'ni: yo raqam bor, yo null.
                t.HasCheckConstraint(
                    "ck_student_contracts_number", "number is null or btrim(number) <> ''");
            });
        });
    }

    // =====================================================================
    //  R-1 — xonalar (SPEC §3.3 shakli)
    // =====================================================================

    private static void ConfigureRooms(ModelBuilder b)
    {
        b.Entity<Room>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");

            e.HasIndex(x => x.Name).IsUnique();

            e.Property(x => x.Capacity).HasDefaultValue((short)30);
            e.Property(x => x.Kind).HasDefaultValue(RoomKind.Classroom);

            // `kind` uchun CHECK ATAYLAB yo'q — Rooms.cs dagi izohga qarang.
            e.ToTable(t => t.HasCheckConstraint("ck_rooms_name", "btrim(name) <> ''"));
        });
    }
}
