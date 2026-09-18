using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;

namespace SchoolLms.Infrastructure.Data;

/// <summary>
/// Kassalar (cash boxes) — EF konfiguratsiyasi. "Smena" o'rniga keladi (mijoz
/// javobi). Migratsiya: <c>CashBoxes</c>.
///
/// <para>
/// <b>Nega alohida fayl?</b> <see cref="BillingModel"/>, <see cref="FinanceParityModel"/>
/// bilan bir xil sabab: <see cref="AppDbContext.OnModelCreating"/> konflikt
/// maydoniga aylanmasligi kerak, va o'zgarish qaysi migratsiyadan kelgan
/// bo'lsa — o'sha faylda turadi.
/// </para>
///
/// <para>
/// <b>Nega check constraint'lar bu yerda, xom SQL'da emas?</b> Qolgan moliya
/// fayllaridagi bilan bir xil sabab: modeldagi constraint snapshot'ga tushadi,
/// keyingi <c>--autogenerate</c> uni DROP qilmaydi. Xom SQL
/// (<c>Migrations/Sql/cash_boxes_guards.sql</c>) da faqat EF ifodalay
/// olmaydigan yagona narsa — <c>cash_box_transactions</c> uchun GRANT/REVOKE
/// (SPEC §4.1: faqat qo'shiladi) va <c>cash_boxes</c> uchun DELETE'ni
/// REVOKE qilish (rename/deactivate — UPDATE qoladi).
/// </para>
/// </summary>
internal static class CashBoxModel
{
    public static void Apply(ModelBuilder b)
    {
        ConfigureCashBoxes(b);
        ConfigureCashBoxTransactions(b);
        ConfigurePaymentCashBox(b);
        ConfigureExpenseCashBox(b);
        ConfigureStudentRefundCashBox(b);
    }

    // =====================================================================
    //  Kassalar kataloqi — ODDIY jadval (rename/deactivate uchun UPDATE bor).
    // =====================================================================

    private static void ConfigureCashBoxes(ModelBuilder b)
    {
        b.Entity<CashBox>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");
            e.Property(x => x.IsDefault).HasDefaultValue(false);
            e.Property(x => x.IsActive).HasDefaultValue(true);

            // RESTRICT: mas'ul xodim o'chirilsa ham kassa tarixi (va nomi
            // bilan bog'liqligi) qolishi kerak — foydalanuvchini o'chirish
            // shu yerda to'xtaydi.
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.ResponsibleUserId)
                .OnDelete(DeleteBehavior.Restrict);

            // SPEC talabi: "Exactly one box must be the default — enforce it"
            // — qisman unikal indeks, faqat true qiymatlar orasida.
            e.HasIndex(x => x.IsDefault).IsUnique().HasFilter("is_default")
                .HasDatabaseName("ux_cash_boxes_one_default");
            e.HasIndex(x => x.ResponsibleUserId);
            e.HasIndex(x => x.IsActive);

            e.ToTable(t => t.HasCheckConstraint("ck_cash_boxes_name", "btrim(name) <> ''"));
        });
    }

    // =====================================================================
    //  Kassa harakatlari — FAQAT QO'SHILADI.
    // =====================================================================

    private static void ConfigureCashBoxTransactions(ModelBuilder b)
    {
        b.Entity<CashBoxTransaction>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.Amount).HasPrecision(14, 2);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");

            e.HasOne<CashBox>().WithMany().HasForeignKey(x => x.CashBoxId)
                .OnDelete(DeleteBehavior.Restrict);
            // Ko'chirish manzili — o'sha jadvalga IKKINCHI FK (ikkalasi ham
            // bitta ustunga tayanmaydi, ya'ni EF ikkita mustaqil munosabat
            // sifatida ko'radi; nomlar to'qnashmasin uchun quyida aniq
            // ko'rsatiladi).
            e.HasOne<CashBox>().WithMany().HasForeignKey(x => x.TransferToBoxId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_cash_box_transactions_cash_boxes_transfer_to_box_id");
            e.HasOne<Student>().WithMany().HasForeignKey(x => x.StudentId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.CreatedBy)
                .OnDelete(DeleteBehavior.Restrict);
            // Storno — o'ziga havola.
            e.HasOne<CashBoxTransaction>().WithMany().HasForeignKey(x => x.ReversalOf)
                .OnDelete(DeleteBehavior.Restrict);

            // "Shu kassaning harakatlari" — balans hisobidagi eng issiq so'rov.
            e.HasIndex(x => x.CashBoxId);
            // "Shu kassaga ko'chirilganlar" — manzil tomonidagi balans qo'shimchasi.
            e.HasIndex(x => x.TransferToBoxId);
            e.HasIndex(x => x.CreatedAt);
            e.HasIndex(x => x.StudentId);
            // Bitta amalni IKKI marta storno qilib bo'lmaydi — `cash_handovers`
            // dagi bilan bir xil qoida (to'liq unikal, QISMAN emas: NULL'lar
            // PostgreSQL'da bir-biridan farqli, ya'ni storno bo'lmagan
            // minglab qatorga xalaqit bermaydi).
            e.HasIndex(x => x.ReversalOf).IsUnique();

            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_cash_box_transactions_amount", "amount > 0");
                t.HasCheckConstraint("ck_cash_box_transactions_kind",
                    "kind in ('pay_in','pay_out','transfer','exchange')");
                t.HasCheckConstraint("ck_cash_box_transactions_method",
                    "method in ('cash','card','transfer','online')");
                t.HasCheckConstraint("ck_cash_box_transactions_to_method",
                    "to_method is null or to_method in ('cash','card','transfer','online')");
                t.HasCheckConstraint("ck_cash_box_transactions_status",
                    "status in ('posted','reversal')");
                t.HasCheckConstraint("ck_cash_box_transactions_reversal_not_self",
                    "reversal_of is null or reversal_of <> id");
                // `transfer` — manzil MAJBURIY; boshqa turlarda TAQIQLANGAN.
                t.HasCheckConstraint("ck_cash_box_transactions_transfer_shape",
                    "(kind = 'transfer') = (transfer_to_box_id is not null)");
                t.HasCheckConstraint("ck_cash_box_transactions_transfer_not_self",
                    "transfer_to_box_id is null or transfer_to_box_id <> cash_box_id");
                // `exchange` — ikkinchi usul MAJBURIY va birinchisidan FARQLI
                // bo'lishi shart (aks holda "ayirboshlash" hech narsani
                // o'zgartirmagan bo'lardi); boshqa turlarda TAQIQLANGAN.
                t.HasCheckConstraint("ck_cash_box_transactions_exchange_shape",
                    "(kind = 'exchange') = (to_method is not null)");
                t.HasCheckConstraint("ck_cash_box_transactions_exchange_methods_differ",
                    "to_method is null or to_method <> method");
            });
        });
    }

    // =====================================================================
    //  `payments` / `expenses` / `student_refunds` — NULLABLE cash_box_id.
    // =====================================================================
    //
    //  UCHALASI HAM NULL BO'LA OLADI — ATAYLAB. Eski qatorlar (smena orqali
    //  yozilgan) hech qaysi kassaga TAXMIN QILINMAYDI: "qaysidir kassadir"
    //  degan taxmin hisobotni orqaga qarab buzardi (xuddi
    //  `FinanceParityModel.ConfigureExpenseCashShift` dagi bilan bir xil
    //  qoida, endi kassaga ko'chirilgan).

    private static void ConfigurePaymentCashBox(ModelBuilder b)
    {
        b.Entity<Payment>(e =>
        {
            e.HasOne<CashBox>().WithMany().HasForeignKey(x => x.CashBoxId)
                .OnDelete(DeleteBehavior.Restrict);

            // Kassa bo'yicha chek raqami UZLUKSIZ (SPEC §4.2 qoidasining
            // ko'zgusi, endi smena o'rniga kassa): `CashBoxService.NextReceiptNoAsync`
            // shu indeksga tayanadi. TO'LIQ unikal (QISMAN emas) — eski
            // qatorlarda `cash_box_id` NULL, PostgreSQL esa NULL'larni
            // bir-biridan farqli deb hisoblaydi, ya'ni ular bu cheklovga
            // umuman kirmaydi.
            e.HasIndex(x => new { x.CashBoxId, x.ReceiptNo }).IsUnique()
                .HasDatabaseName("ix_payments_cash_box_id_receipt_no");
        });
    }

    private static void ConfigureExpenseCashBox(ModelBuilder b)
    {
        b.Entity<Expense>(e =>
        {
            e.HasOne<CashBox>().WithMany().HasForeignKey(x => x.CashBoxId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.CashBoxId);
        });
    }

    private static void ConfigureStudentRefundCashBox(ModelBuilder b)
    {
        b.Entity<StudentRefund>(e =>
        {
            e.HasOne<CashBox>().WithMany().HasForeignKey(x => x.CashBoxId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.CashBoxId);
        });
    }
}
