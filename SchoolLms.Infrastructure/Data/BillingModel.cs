using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;

namespace SchoolLms.Infrastructure.Data;

/// <summary>
/// Moliya (billing) jadvallarining EF konfiguratsiyasi — SPEC §3.7 va §4. Vazifa: P1-04.
///
/// <para>
/// <b>Nega alohida fayl?</b> <see cref="AppDbContext.OnModelCreating"/> ga solingan 150 qator
/// konfiguratsiya Faza 1.C dagi beshta parallel agent uchun yana bitta konflikt maydoni
/// bo'lardi — <c>Billing.cs</c> ni <c>Entities.cs</c> dan ajratish sababi bilan aynan bir xil
/// sabab. <see cref="AppDbContext"/> da faqat bitta chaqiruv qoladi.
/// </para>
///
/// <para>
/// <b>Nega check constraint'lar EF modelida, xom SQL'da emas?</b> Modeldagi constraint
/// snapshot'ga tushadi, ya'ni kelgusi <c>--autogenerate</c> uni "ortiqcha" deb hisoblab
/// DROP qilmaydi. Xom SQL bilan qo'shilgan constraint EF uchun ko'rinmas bo'ladi va
/// keyingi migratsiyada jimgina yo'qolishi mumkin. Faqat EF UMUMAN ifodalay olmaydigan
/// narsalar (allocation trigger'i, GRANT/REVOKE) migratsiyadagi xom SQL'da qoladi —
/// <c>Migrations/Sql/billing_guards.sql</c>.
/// </para>
/// </summary>
internal static class BillingModel
{
    public static void Apply(ModelBuilder b)
    {
        ConfigureFeeCategories(b);
        ConfigureSubscriptions(b);
        ConfigureDiscounts(b);
        ConfigureInvoices(b);
        ConfigurePayments(b);
        ConfigureAllocations(b);
        ConfigureCashShifts(b);
        ConfigureExpenses(b);
        ConfigureLedger(b);
        ConfigureSettings(b);
    }

    private static void ConfigureFeeCategories(ModelBuilder b)
    {
        b.Entity<FeeCategory>(e =>
        {
            e.HasKey(x => x.Id);
            // Kod — mashina kaliti (accrual va hisobotlar shunga tayanadi), shuning uchun unikal.
            e.HasIndex(x => x.Code).IsUnique();
        });
    }

    private static void ConfigureSubscriptions(ModelBuilder b)
    {
        b.Entity<StudentSubscription>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.MonthlyAmount).HasPrecision(14, 2);

            // O'quvchi o'chirilsa obunasi ham ketadi — obuna "kelajakda nima hisoblanadi"
            // degani, tarix emas. Tarix `invoices` da qoladi va u RESTRICT.
            e.HasOne<Student>().WithMany().HasForeignKey(x => x.StudentId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne<FeeCategory>().WithMany().HasForeignKey(x => x.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.CreatedBy)
                .OnDelete(DeleteBehavior.Restrict);

            // Accrual har oy "shu o'quvchining shu toifadagi faol obunasi" ni qidiradi.
            e.HasIndex(x => new { x.StudentId, x.CategoryId, x.StartsOn });

            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_student_subscriptions_amount", "monthly_amount >= 0");
                t.HasCheckConstraint("ck_student_subscriptions_period", "ends_on is null or ends_on >= starts_on");
            });
        });
    }

    private static void ConfigureDiscounts(ModelBuilder b)
    {
        b.Entity<Discount>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Percent).HasPrecision(5, 2);
            e.Property(x => x.Amount).HasPrecision(14, 2);

            e.HasOne<Student>().WithMany().HasForeignKey(x => x.StudentId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne<FeeCategory>().WithMany().HasForeignKey(x => x.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.CreatedBy)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.ApprovedBy)
                .OnDelete(DeleteBehavior.Restrict);

            // Accrual: "shu o'quvchining TASDIQLANGAN chegirmalari" — eng issiq so'rov.
            e.HasIndex(x => new { x.StudentId, x.Status });
            // Admin UI dagi "tasdiq kutayotganlar" navbati.
            e.HasIndex(x => x.Status);

            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_discounts_percent", "percent >= 0 and percent <= 100");
                t.HasCheckConstraint("ck_discounts_amount", "amount >= 0");
                t.HasCheckConstraint("ck_discounts_period", "ends_on is null or ends_on >= starts_on");
                t.HasCheckConstraint("ck_discounts_status", "status in ('pending','approved','rejected')");
                // SPEC §4.5 — ikki qavatli nazorat. Ilova tekshiruvi chetlab o'tilsa ham baza to'xtatadi.
                t.HasCheckConstraint("ck_discounts_approver_differs", "approved_by is null or approved_by <> created_by");
                // Mijoz javobi (SPEC §8.1 Q5): tasdiqlangan chegirma TASDIQLOVCHISIZ bo'la olmaydi.
                t.HasCheckConstraint("ck_discounts_approved_has_approver", "status <> 'approved' or approved_by is not null");
            });
        });
    }

    private static void ConfigureInvoices(ModelBuilder b)
    {
        b.Entity<Invoice>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Amount).HasPrecision(14, 2);
            e.Property(x => x.Discount).HasPrecision(14, 2);

            // Hisob-faktura — moliyaviy tarix. O'quvchi qatori bilan birga o'chib ketmasin.
            e.HasOne<Student>().WithMany().HasForeignKey(x => x.StudentId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<FeeCategory>().WithMany().HasForeignKey(x => x.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            // Accrual idempotentligi SHU indeksga tayanadi: bir oy ikki marta hisoblanmaydi.
            e.HasIndex(x => new { x.StudentId, x.CategoryId, x.PeriodMonth }).IsUnique();
            // Qarzdorlar hisoboti: ochiq/qisman to'langanlar, muddati bo'yicha.
            e.HasIndex(x => new { x.Status, x.DueOn });
            e.HasIndex(x => x.PeriodMonth);

            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_invoices_amount", "amount >= 0");
                t.HasCheckConstraint("ck_invoices_discount", "discount >= 0 and discount <= amount");
                t.HasCheckConstraint("ck_invoices_status", "status in ('open','partial','paid','void')");
                // period_month DOIM oyning birinchi kuni — aks holda unikal indeks bir oyni
                // ikkiga bo'lib yuborardi (01 va 15 — bir xil oy, ikki xil qator).
                t.HasCheckConstraint("ck_invoices_period_first_day", "extract(day from period_month) = 1");
            });
        });
    }

    private static void ConfigurePayments(ModelBuilder b)
    {
        b.Entity<Payment>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Amount).HasPrecision(14, 2);

            e.HasOne<Student>().WithMany().HasForeignKey(x => x.StudentId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<CashShift>().WithMany().HasForeignKey(x => x.CashShiftId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.CashierId)
                .OnDelete(DeleteBehavior.Restrict);
            // Storno — o'ziga havola.
            e.HasOne<Payment>().WithMany().HasForeignKey(x => x.ReversalOf)
                .OnDelete(DeleteBehavior.Restrict);

            // SPEC §4.2 — smena ichida chek raqami uzluksiz va takrorlanmas.
            e.HasIndex(x => new { x.CashShiftId, x.ReceiptNo }).IsUnique();
            // Bitta to'lovni IKKI marta storno qilib bo'lmaydi — pulni ikki marta
            // "qaytarib" hisobotni bo'yashning eng oson yo'li shu edi.
            e.HasIndex(x => x.ReversalOf).IsUnique().HasFilter("reversal_of is not null");
            // O'quvchi kartochkasidagi to'lovlar tarixi.
            e.HasIndex(x => new { x.StudentId, x.ReceivedAt });
            // Z-hisobot: smena/kassir kesimi.
            e.HasIndex(x => new { x.CashierId, x.ReceivedAt });

            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_payments_amount", "amount > 0");
                // Mijoz javobi (SPEC §8.1 Q13): to'rtta YORLIQ. Provayder integratsiyasi YO'Q.
                t.HasCheckConstraint("ck_payments_method", "method in ('cash','card','transfer','online')");
                t.HasCheckConstraint("ck_payments_receipt_no", "receipt_no > 0");
                t.HasCheckConstraint("ck_payments_reversal_not_self", "reversal_of is null or reversal_of <> id");
            });
        });
    }

    private static void ConfigureAllocations(ModelBuilder b)
    {
        b.Entity<PaymentAllocation>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Amount).HasPrecision(14, 2);

            // RESTRICT ikkalasida ham — TASKS.md P1-04 qabul mezoni. CASCADE bo'lsa
            // to'lovni o'chirish taqsimotni ham olib ketardi; bizda esa hech narsa
            // o'chmaydi, shuning uchun "o'chirishga urinish" baland xato berishi kerak.
            e.HasOne<Payment>().WithMany().HasForeignKey(x => x.PaymentId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Invoice>().WithMany().HasForeignKey(x => x.InvoiceId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(x => x.PaymentId);
            // Hisob-faktura qoldig'i: Σ allocations — qarz hisobidagi eng issiq so'rov.
            e.HasIndex(x => x.InvoiceId);

            e.ToTable(t => t.HasCheckConstraint("ck_payment_allocations_amount", "amount > 0"));
        });
    }

    private static void ConfigureCashShifts(ModelBuilder b)
    {
        b.Entity<CashShift>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.OpeningFloat).HasPrecision(14, 2);
            e.Property(x => x.ExpectedCash).HasPrecision(14, 2);
            e.Property(x => x.CountedCash).HasPrecision(14, 2);

            // SPEC §4.2: nomuvofiqlikni ILOVA HISOBLAMAYDI va YOZMAYDI — baza hisoblaydi.
            // `stored` generated column'ga INSERT/UPDATE qilib bo'lmaydi, ya'ni smena
            // yopilgandan keyin uni "to'g'rilash" imkonsiz.
            e.Property(x => x.Variance)
                .HasColumnType("numeric(14,2)")
                .HasComputedColumnSql("counted_cash - expected_cash", stored: true);

            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.CashierId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.ClosedBy)
                .OnDelete(DeleteBehavior.Restrict);

            // SPEC §4.2: "kassir ochiq smenasi ustiga ikkinchisini ocha olmaydi" —
            // ilova tekshiruvi emas, BAZA kafolati (qisman unikal indeks).
            e.HasIndex(x => x.CashierId).IsUnique().HasFilter("status = 'open'")
                .HasDatabaseName("ux_cash_shifts_one_open_per_cashier");
            e.HasIndex(x => x.OpenedAt);

            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_cash_shifts_status", "status in ('open','closed')");
                t.HasCheckConstraint("ck_cash_shifts_opening_float", "opening_float >= 0");
                t.HasCheckConstraint("ck_cash_shifts_closed_at", "closed_at is null or closed_at >= opened_at");
                // SPEC §4.2: smenani SANALGAN naqdsiz yopib bo'lmaydi.
                t.HasCheckConstraint("ck_cash_shifts_close_requires_count",
                    "status <> 'closed' or (closed_at is not null and counted_cash is not null and expected_cash is not null)");
            });
        });
    }

    private static void ConfigureExpenses(ModelBuilder b)
    {
        b.Entity<Expense>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Amount).HasPrecision(14, 2);

            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.CreatedBy)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.ApprovedBy)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(x => x.OnDate);
            e.HasIndex(x => new { x.Category, x.OnDate });

            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_expenses_amount", "amount > 0");
                // SPEC §4.5 — chiqimda ham ikki qavatli nazorat.
                t.HasCheckConstraint("ck_expenses_approver_differs", "approved_by is null or approved_by <> created_by");
            });
        });
    }

    private static void ConfigureLedger(ModelBuilder b)
    {
        b.Entity<LedgerEntry>(e =>
        {
            e.HasKey(x => x.Id);
            // bigint identity — ilova qiymat bermaydi, baza beradi (tartib buzilmaydi).
            e.Property(x => x.Id).UseIdentityByDefaultColumn();
            e.Property(x => x.Amount).HasPrecision(14, 2);

            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.CreatedBy)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<LedgerEntry>().WithMany().HasForeignKey(x => x.ReversalOf)
                .OnDelete(DeleteBehavior.Restrict);

            // SPEC §3.7 dagi nom AYNAN saqlangan — hisobot so'rovlari va EXPLAIN
            // chiqishi hujjat bilan bir xil o'qilsin.
            e.HasIndex(x => new { x.EntryDate, x.Account })
                .HasDatabaseName("ledger_entries_date_account");
            // "Shu to'lovning yozuvlari" — storno va tekshiruv uchun.
            e.HasIndex(x => new { x.RefType, x.RefId });

            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_ledger_entries_amount", "amount > 0");
                t.HasCheckConstraint("ck_ledger_entries_direction", "direction in ('debit','credit')");
                t.HasCheckConstraint("ck_ledger_entries_reversal_not_self", "reversal_of is null or reversal_of <> id");
            });
        });
    }

    private static void ConfigureSettings(ModelBuilder b)
    {
        b.Entity<BillingSettings>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UpdatedBy)
                .OnDelete(DeleteBehavior.Restrict);

            e.ToTable(t =>
            {
                // 28 — har oyda mavjud bo'lgan eng katta kun. 30/31 qo'yilsa fevralda
                // muddat "yo'q kun" ga tushib qolardi.
                t.HasCheckConstraint("ck_billing_settings_due_day", "payment_due_day between 1 and 28");
                t.HasCheckConstraint("ck_billing_settings_overdue_day", "overdue_after_day between 1 and 28");
                t.HasCheckConstraint("ck_billing_settings_order", "overdue_after_day >= payment_due_day");
            });
        });
    }
}
