using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;

namespace SchoolLms.Infrastructure.Data;

/// <summary>
/// `docs/modules/existing-module-gaps.md` bo'yicha ikkinchi to'lqin sxemasining
/// EF konfiguratsiyasi: qarzdorlar ish oqimi (§3.5), sertifikatlar (§2.3),
/// arxivlash sabablari katalogi (§2.2), intizom sababining yangi ustunlari
/// (§6.3 4–5-qadam) va `school_meta` ning to'rtta bayrog'i (§5.5).
///
/// <para>
/// <b>Nega alohida fayl?</b> <see cref="BillingModel"/>, <c>AnomalyModel</c> va
/// <see cref="GuardianModel"/> bilan bir xil sabab:
/// <see cref="AppDbContext.OnModelCreating"/> umumiy fayl va u konflikt
/// maydoniga aylanmasligi kerak. Shu to'lqinda uchta agent ketma-ket ishlaydi;
/// <see cref="AppDbContext"/> da faqat bitta chaqiruv qoladi.
/// </para>
///
/// <para>
/// <b>Nega constraint va DEFAULT'lar EF modelida, xom SQL'da emas?</b> Modeldagi
/// narsa snapshot'ga tushadi, ya'ni keyingi <c>--autogenerate</c> uni "ortiqcha"
/// deb hisoblab DROP qilmaydi. Xom SQL bilan qo'shilgan constraint EF uchun
/// KO'RINMAS bo'ladi va keyingi migratsiyada jimgina yo'qolishi mumkin. Xom
/// SQL'da faqat EF umuman ifodalay olmaydigan narsa qoladi — <c>app_rw</c>
/// uchun GRANT (<c>Migrations/Sql/parity_wave2_guards.sql</c>) va katalog
/// qatorlari (<c>Migrations/Sql/parity_wave2_seed.sql</c>).
/// </para>
///
/// <para>
/// <b>DEFAULT'lar nega MUHIM.</b> Mavjud jadvalga NOT NULL ustun qo'shilganda
/// eski qatorlar AYNAN shu DEFAULT bilan to'ldiriladi. Ya'ni
/// <c>discipline_reasons.notify_parent</c> ning <c>false</c> qiymati — bu
/// "sozlama", balki mavjud har bir sababning qiymati (§6.3 ogohlantirishi), va
/// <c>school_meta</c> ning <c>true</c> bayroqlari bugungi xatti-harakatni
/// saqlab qoladi. DEFAULT'siz EF ularni CLR turining noli (false) bilan
/// to'ldirardi va ota-ona kabinetidagi o'zlashtirish JIMGINA yo'qolardi.
/// </para>
/// </summary>
internal static class ParityModel
{
    public static void Apply(ModelBuilder b)
    {
        ConfigureDebtorStatuses(b);
        ConfigureDebtorActions(b);
        ConfigureCertificateTypes(b);
        ConfigureCertificates(b);
        ConfigureStudentArchiveReasons(b);
        ConfigureDisciplineReasonColumns(b);
        ConfigureSchoolMetaFlags(b);
    }

    // =====================================================================
    //  §3.5 — Qarzdorlar ish oqimi
    // =====================================================================

    private static void ConfigureDebtorStatuses(ModelBuilder b)
    {
        b.Entity<DebtorStatus>(e =>
        {
            e.HasKey(x => x.Id);

            // Nom — bu ma'lumotnomaning kaliti: ikkita "Va'da berdi" bo'lsa
            // ro'yxatdagi guruhlash ikkiga bo'linadi. Seed ham shu indeksga
            // tayanadi (`ON CONFLICT (name) DO NOTHING`).
            e.HasIndex(x => x.Name).IsUnique();

            e.Property(x => x.IsActive).HasDefaultValue(true);

            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_debtor_statuses_name", "btrim(name) <> ''");
                t.HasCheckConstraint("ck_debtor_statuses_position", "position >= 0");
            });
        });
    }

    private static void ConfigureDebtorActions(ModelBuilder b)
    {
        b.Entity<DebtorAction>(e =>
        {
            e.HasKey(x => x.Id);

            // O'quvchi o'chirilsa amallar ham ketadi — bu jadval PULNI emas,
            // pul haqidagi SUHBATNI saqlaydi. To'lovlar tarixi `payments` da
            // qoladi va u RESTRICT.
            e.HasOne<Student>().WithMany().HasForeignKey(x => x.StudentId)
                .OnDelete(DeleteBehavior.Cascade);

            // Ma'lumotnoma qatori ISHLATILGAN bo'lsa — o'chirilmaydi. Katalogdan
            // chiqarish yo'li bitta: `is_active = false`. Aks holda eski amallar
            // "qaysi holat edi" degan savolga javobsiz qolardi.
            e.HasOne<DebtorStatus>().WithMany().HasForeignKey(x => x.StatusId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.CreatedBy)
                .OnDelete(DeleteBehavior.Restrict);

            // Qarzdorlar ro'yxatidagi eng issiq so'rov: "shu o'quvchining ENG
            // OXIRGI amali" — joriy holat, oxirgi amal sanasi va va'da sanasi
            // shundan olinadi. Shuning uchun `created_at` TESKARI tartibda.
            e.HasIndex(x => new { x.StudentId, x.CreatedAt }).IsDescending(false, true);

            // "Buzilgan va'da" tungi tekshiruvi (§3.5): o'tib ketgan va'dalar,
            // faqat tirik qatorlar. Qisman indeks — jadvalning katta qismi
            // (va'dasiz izohlar) unga umuman kirmaydi.
            e.HasIndex(x => x.PromisedOn)
                .HasFilter("promised_on is not null and deleted_at is null")
                .HasDatabaseName("ix_debtor_actions_open_promises");

            e.ToTable(t =>
                // Izohsiz amal — "kimdir nimadir qildi", ya'ni foydasiz qator.
                t.HasCheckConstraint("ck_debtor_actions_comment", "btrim(comment) <> ''"));
        });
    }

    // =====================================================================
    //  §2.3 — Sertifikatlar
    // =====================================================================

    private static void ConfigureCertificateTypes(ModelBuilder b)
    {
        b.Entity<CertificateType>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Name).IsUnique();

            e.Property(x => x.IsScored).HasDefaultValue(false);
            e.Property(x => x.IsActive).HasDefaultValue(true);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");

            e.ToTable(t => t.HasCheckConstraint("ck_certificate_types_name", "btrim(name) <> ''"));
        });
    }

    private static void ConfigureCertificates(ModelBuilder b)
    {
        b.Entity<Certificate>(e =>
        {
            e.HasKey(x => x.Id);

            // §2.3: `score numeric(6,2)` — IELTS 7.5 dan SAT 1600 gacha sig'adi.
            e.Property(x => x.Score).HasPrecision(6, 2);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");

            // O'quvchi o'chirilsa hujjatlari ham ketadi (§2.3 dagi `on delete cascade`).
            e.HasOne<Student>().WithMany().HasForeignKey(x => x.StudentId)
                .OnDelete(DeleteBehavior.Cascade);

            // Tur ISHLATILGAN bo'lsa o'chirilmaydi (§2.3 dagi `on delete restrict`) —
            // aks holda "IELTS" o'chirilishi bilan barcha IELTS sertifikatlari
            // turini yo'qotardi.
            e.HasOne<CertificateType>().WithMany().HasForeignKey(x => x.TypeId)
                .OnDelete(DeleteBehavior.Restrict);

            // Fan va o'qituvchi — QO'SHIMCHA ma'lumot. Ular o'chirilsa hujjat
            // qoladi, shunchaki havolasi bo'shaydi: hujjatning o'zi maktabda
            // emas, bolaning qo'lida.
            e.HasOne<Subject>().WithMany().HasForeignKey(x => x.SubjectId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne<Teacher>().WithMany().HasForeignKey(x => x.TeacherId)
                .OnDelete(DeleteBehavior.SetNull);

            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.CreatedBy)
                .OnDelete(DeleteBehavior.Restrict);

            // §2.3 dagi ikkala indeks. Birinchisi — o'quvchi kartochkasidagi
            // "sertifikatlar" bloki (eng yangisi tepada); ikkinchisi —
            // o'quvchilar ro'yxatidagi `certificateTypeIds` filtri
            // ("IELTS sertifikati bor har bir bolani ko'rsat").
            e.HasIndex(x => new { x.StudentId, x.IssuedOn }).IsDescending(false, true);
            e.HasIndex(x => x.TypeId);

            e.ToTable(t =>
            {
                // §2.3: `check (score is null) or (score >= 0)`.
                t.HasCheckConstraint("ck_certificates_score", "score is null or score >= 0");
                // §2.3: `check expires_on is null or expires_on >= issued_on`.
                t.HasCheckConstraint(
                    "ck_certificates_period", "expires_on is null or expires_on >= issued_on");
            });
        });
    }

    // =====================================================================
    //  §2.2 — Arxivlash sabablari katalogi
    // =====================================================================

    private static void ConfigureStudentArchiveReasons(ModelBuilder b)
    {
        b.Entity<StudentArchiveReason>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Name).IsUnique();
            e.Property(x => x.IsActive).HasDefaultValue(true);

            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_student_archive_reasons_name", "btrim(name) <> ''");
                t.HasCheckConstraint("ck_student_archive_reasons_position", "position >= 0");
            });
        });

        // O'quvchidagi havola. RESTRICT — ishlatilgan sababni o'chirib bo'lmaydi:
        // o'chirilsa, arxivlangan o'quvchi "nega ketgani" ni yo'qotardi va §2.2
        // ning butun maqsadi (guruhlab ko'rish) buzilardi. Katalogdan chiqarish
        // yo'li — `is_active = false`.
        //
        // Eski `students.archive_reason` (erkin matn) ustuniga TEGILMAGAN:
        // "Boshqa" tanlovi va uning yonidagi tafsilot §2.2 da ataylab saqlangan.
        b.Entity<Student>()
            .HasOne<StudentArchiveReason>().WithMany()
            .HasForeignKey(s => s.ArchiveReasonId)
            .OnDelete(DeleteBehavior.Restrict);
    }

    // =====================================================================
    //  §6.3 (4–5-qadam) — intizom sababining yangi ustunlari
    // =====================================================================

    private static void ConfigureDisciplineReasonColumns(ModelBuilder b)
    {
        b.Entity<DisciplineReason>(e =>
        {
            // MAVJUD HAR BIR SABAB `false` oladi — §6.3 ning talabi. DEFAULT
            // aynan shu ishni bajaradi: `ADD COLUMN ... NOT NULL DEFAULT false`
            // eski qatorlarni to'ldiradi.
            e.Property(x => x.NotifyParent).HasDefaultValue(false);

            // Mavjud sabablar faol bo'lib qoladi — sukut `true`.
            e.Property(x => x.IsActive).HasDefaultValue(true);
        });
    }

    // =====================================================================
    //  §5.5 — to'rtta umumiy sozlama bayrog'i
    // =====================================================================

    private static void ConfigureSchoolMetaFlags(ModelBuilder b)
    {
        b.Entity<SchoolMeta>(e =>
        {
            // Mijoz savoli Q4: "javob bo'lmasa — bloklangan".
            e.Property(x => x.ArchiveOnlyNonDebtorStudents).HasDefaultValue(true);

            // Ma'lumot sifati darvozalari — bugungi xatti-harakat: majburiy emas.
            e.Property(x => x.MakeAttendanceReasonRequired).HasDefaultValue(false);
            e.Property(x => x.IsStudentGradeRequired).HasDefaultValue(false);

            // Bugun ota-onalar o'zlashtirishni KO'RISHADI — migratsiya buni
            // o'zgartirmaydi.
            e.Property(x => x.ShowLearningProgressInParentDashboard).HasDefaultValue(true);
        });
    }
}
