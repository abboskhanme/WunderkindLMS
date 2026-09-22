using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;

namespace SchoolLms.Infrastructure.Data;

/// <summary>
/// Savdo va marketing (ariza formasi + yangiliklar) — EF konfiguratsiyasi.
/// Migratsiya: <c>SalesAndMarketing</c>. Spetsifikatsiya:
/// <c>docs/modules/sales-marketing.md</c> §4.
///
/// <para>
/// <b>Nega alohida fayl?</b> <see cref="TransactionTypeModel"/>,
/// <see cref="CashBoxModel"/> bilan bir xil sabab:
/// <see cref="AppDbContext.OnModelCreating"/> konflikt maydoniga
/// aylanmasligi kerak, va o'zgarish qaysi migratsiyadan kelgan bo'lsa —
/// o'sha faylda turadi. Shu sababdan <see cref="Lead"/> ning uchta YANGI
/// ustuni ham (<c>source</c>, <c>survey_id</c>, <c>created_at</c>) SHU
/// yerda sozlanadi: ularni qo'shayotgan migratsiya shu, doskani (kanban)
/// qurgan eski migratsiya emas.
/// </para>
///
/// <para>
/// <b>Ifoda indeksi shu yerda YO'Q — ataylab.</b> <c>ux_surveys_slug</c>
/// <c>lower(slug)</c> ustida turadi, EF Core esa ifoda indeksini modellay
/// olmaydi. U migratsiyada xom SQL bilan yaratiladi
/// (<c>ux_study_groups_name_active</c> naqshi): snapshot uni ko'rmaydi,
/// ya'ni keyingi <c>--autogenerate</c> unga tegmaydi ham.
/// </para>
/// </summary>
internal static class SalesMarketingModel
{
    public static void Apply(ModelBuilder b)
    {
        ConfigureSurveys(b);
        ConfigureSurveySubmissions(b);
        ConfigureNews(b);
        ConfigureLeadSource(b);
    }

    /// <summary>
    /// Ommaviy ariza formasi (§4.1). Slug'ning unikalligi bu yerda emas —
    /// yuqoridagi izoh.
    /// </summary>
    private static void ConfigureSurveys(ModelBuilder b)
    {
        b.Entity<Survey>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            // `HasSentinel(true)` — EF Core 8+ "sukut qiymati" tuzog'i: sentinel'siz
            // `false` CLR nolining o'zi, EF uni INSERT'dan TASHLAB KETADI va baza
            // DEFAULT'i (true) g'olib chiqadi — "o'quvchi ismi so'ralmasin" deb
            // yaratilgan ariza jimgina so'raydigan bo'lib yozilardi. 2026-09-17 dagi
            // `school_meta` xatosi; naqsh — StudentsParityModel.cs.
            e.Property(x => x.ShowStudentFirstNameInput).HasDefaultValue(true).HasSentinel(true);
            e.Property(x => x.ShowStudentLastNameInput).HasDefaultValue(true).HasSentinel(true);
            e.Property(x => x.ShowStudentPhoneNumberInput).HasDefaultValue(false);
            e.Property(x => x.ShowStudentGradeInput).HasDefaultValue(true).HasSentinel(true);
            e.Property(x => x.ShowStudentGenderInput).HasDefaultValue(true).HasSentinel(true);
            e.Property(x => x.IsActive).HasDefaultValue(true).HasSentinel(true);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");

            // Kanban ustuni o'chirilishi maktab uchun ODDIY ish, shuning uchun
            // SET NULL: ariza ishlashda davom etadi va lid Rule S 2-qadami
            // bo'yicha eng kichik `Order` li ustunga tushadi (§4.1).
            e.HasOne<LeadStage>().WithMany().HasForeignKey(x => x.StageId)
                .OnDelete(DeleteBehavior.SetNull);

            // Muallif — RESTRICT: arizani kim yaratgani yo'qolmasin.
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.CreatedBy)
                .OnDelete(DeleteBehavior.Restrict);

            // Admin ro'yxatining o'zi: "faol arizalar, yangisi tepada".
            e.HasIndex(x => new { x.IsActive, x.CreatedAt }).IsDescending(false, true)
                .HasDatabaseName("ix_surveys_active");

            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_surveys_name", "btrim(name) <> ''");

                // Havolaning shakli baza darajasida ham qulflanadi: slug
                // Instagram bio'siga tashlanadi va uni xizmat qatlamidagi
                // bitta xato regex buzsa, havola o'lik bo'lib chiqadi.
                t.HasCheckConstraint(
                    "ck_surveys_slug",
                    "slug ~ '^[a-z0-9]+(-[a-z0-9]+)*$' and length(slug) between 3 and 60");

                // Rule T (§2.4): dizayni MUZLATILGAN doska "noma'lum" jinsni
                // ham, "noma'lum" sinfni ham chiza olmaydi (`leads.gender` —
                // ikki qiymatli not null, `leads.target_grade` — not null int
                // va 0 allaqachon "nol sinf"). Shuning uchun bu ikki tugma
                // ekranda FAOL va O'CHIRILMAYDIGAN, bazada esa CHECK.
                t.HasCheckConstraint(
                    "ck_surveys_required_toggles",
                    "show_student_gender_input and show_student_grade_input");
            });
        });
    }

    /// <summary>Topshirilgan arizalar registri (§4.2).</summary>
    private static void ConfigureSurveySubmissions(ModelBuilder b)
    {
        b.Entity<SurveySubmission>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.Status).HasDefaultValue(SurveySubmissionStatus.Lead);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");

            // RESTRICT — D7 ning bazadagi asosi: topshiriqlari bor arizani
            // o'chirib bo'lmaydi. Xizmat qatlami buni OLDINDAN tekshiradi va
            // tushunarli 409 beradi (xom SQLSTATE 23503 o'rniga).
            e.HasOne<Survey>().WithMany().HasForeignKey(x => x.SurveyId)
                .OnDelete(DeleteBehavior.Restrict);

            // SET NULL — lidni doskadan o'chirish ota-ona YOZGAN ma'lumotning
            // isbotini o'chirmasin (§4.2).
            e.HasOne<Lead>().WithMany().HasForeignKey(x => x.LeadId)
                .OnDelete(DeleteBehavior.SetNull);

            // Registrning o'zi: "shu ariza bo'yicha topshiriqlar, yangisi tepada".
            // Bu kompozit indeks `survey_id` FK'si uchun ham yetarli, shuning
            // uchun EF alohida FK indeksi yaratmaydi.
            e.HasIndex(x => new { x.SurveyId, x.CreatedAt }).IsDescending(false, true)
                .HasDatabaseName("ix_survey_submissions_survey");

            // Takrorni aniqlash so'rovi (§2.6 4-qadam): shu ariza + shu telefon
            // kaliti + oxirgi 24 soat.
            e.HasIndex(x => new { x.SurveyId, x.ParentPhoneKey, x.CreatedAt })
                .IsDescending(false, false, true)
                .HasDatabaseName("ix_survey_submissions_dedupe");

            e.HasIndex(x => x.LeadId).HasDatabaseName("ix_survey_submissions_lead");

            e.ToTable(t =>
            {
                t.HasCheckConstraint(
                    "ck_survey_submissions_status",
                    "status in ('lead','duplicate')");

                t.HasCheckConstraint(
                    "ck_survey_submissions_gender",
                    "student_gender is null or student_gender in ('male','female')");

                // 0 — HAQIQIY sinf (nol sinf), shuning uchun oraliq 0 dan
                // boshlanadi va "noma'lum" uchun `null` ishlatiladi (§2.1).
                t.HasCheckConstraint(
                    "ck_survey_submissions_grade",
                    "student_grade is null or student_grade between 0 and 11");

                // Ommaviy endpoint bo'sh satrli "topshiriq" yozib ketmasin.
                t.HasCheckConstraint(
                    "ck_survey_submissions_parent",
                    "btrim(parent_first_name) <> '' and btrim(parent_phone) <> ''");
            });
        });
    }

    /// <summary>Yangiliklar (§4.3).</summary>
    private static void ConfigureNews(ModelBuilder b)
    {
        b.Entity<NewsItem>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.ForEmployee).HasDefaultValue(false);
            e.Property(x => x.ForParent).HasDefaultValue(false);
            e.Property(x => x.ForStudent).HasDefaultValue(false);
            e.Property(x => x.TelegramRecipientCount).HasDefaultValue(0);
            e.Property(x => x.TelegramSentCount).HasDefaultValue(0);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");

            // RESTRICT — `author_name` nusxasi bilan birga: kim e'lon qilgani
            // na o'chirish, na nomini o'zgartirish bilan qayta yozilmaydi.
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.AuthorId)
                .OnDelete(DeleteBehavior.Restrict);

            // Lentaning yagona so'rovi: "o'chirilmagan, e'lon qilingan,
            // yangisi tepada". Qismli (partial) indeks — yumshoq o'chirilgan
            // qatorlar indeksga umuman kirmaydi.
            e.HasIndex(x => x.PublishedAt).IsDescending()
                .HasFilter("deleted_at is null")
                .HasDatabaseName("ix_news_feed");

            // Jadval nomi ATAYLAB yozib qo'yilgan: entity `NewsItem` deb
            // ataladi (§4.6), ya'ni konvensiya nomni `DbSet` xossasidan
            // oladi — buni ko'rinmas bog'liqlik qilib qoldirmaymiz.
            e.ToTable("news", t =>
            {
                t.HasCheckConstraint("ck_news_title", "btrim(title) <> ''");
                t.HasCheckConstraint("ck_news_body", "btrim(body) <> ''");

                // Hech kimga ko'rinmaydigan yangilik — xatoning o'zi (N5).
                t.HasCheckConstraint(
                    "ck_news_audience",
                    "for_employee or for_parent or for_student");
            });
        });
    }

    /// <summary>
    /// <see cref="Lead"/> ning uchta YANGI ustuni (§4.4). Doskaning o'ziga
    /// (dizayni muzlatilgan <c>pages/admin/leads/*</c>) hech qanday ta'siri
    /// yo'q: uchovi ham qo'shimcha (additive) va eski ustunlar tegilmaydi.
    /// </summary>
    private static void ConfigureLeadSource(ModelBuilder b)
    {
        b.Entity<Lead>(e =>
        {
            // Bugungi HAR BIR qator `manual` bo'ladi, va bu qulaylik emas —
            // haqiqat: ularning hammasini xodim doskada o'z qo'li bilan
            // kiritgan (§4.4).
            e.Property(x => x.Source).HasDefaultValue(LeadSource.Manual);

            // `created_at` ga DEFAULT ATAYLAB QO'YILMAYDI: `default now()`
            // butun tarixni migratsiya vaqti bilan tamg'alardi. Eski qatorlar
            // `NULL` bo'lib qoladi = "sanasi noma'lum" (§4.4, Q7). Yangi
            // qatorlar qiymatni entity initsializatoridan oladi.

            // RESTRICT — `ck_leads_source_survey` hech qachon buzilmasin:
            // `set null` bo'lganda `source='survey'` li lidning `survey_id`
            // si NULL bo'lib qolib, CHECK'ni buzardi (§4.4).
            e.HasOne<Survey>().WithMany().HasForeignKey(x => x.SurveyId)
                .OnDelete(DeleteBehavior.Restrict);

            // Voronkaning "Manba" kesimi (SM-13): manba bo'yicha, yangisi tepada.
            e.HasIndex(x => new { x.Source, x.CreatedAt }).IsDescending(false, true)
                .HasDatabaseName("ix_leads_source");

            // Qismli indeks: `survey_id` li lidlar ozchilik bo'lib qoladi
            // (doskadagilar `manual`), ya'ni indeks ham kichik qoladi.
            // FK uchun ham shu indeks ishlatiladi.
            e.HasIndex(x => x.SurveyId)
                .HasFilter("survey_id is not null")
                .HasDatabaseName("ix_leads_survey");

            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_leads_source", "source in ('manual','survey')");

                // Ikki ustun bir-biridan uzoqlashib ketmasin: `survey` manbali
                // lidda ariza BO'LISHI SHART, `manual` da esa BO'LMASLIGI shart.
                t.HasCheckConstraint(
                    "ck_leads_source_survey",
                    "(source = 'survey') = (survey_id is not null)");
            });
        });
    }
}
