using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;

namespace SchoolLms.Infrastructure.Data;

/// <summary>
/// Tungi tekshiruv bayroqlari va <c>audit_log</c> ning <c>jsonb</c> ustunlari —
/// SPEC §4.6. Vazifa: P1-14.
///
/// <para>
/// <b>Nega alohida fayl?</b> <see cref="BillingModel"/> dagi bilan bir xil
/// sabab: Faza 1.C/1.D da bir nechta agent moliya kodini parallel yozadi,
/// <see cref="AppDbContext.OnModelCreating"/> esa ularning umumiy konflikt
/// maydoni. <see cref="AppDbContext"/> da faqat bitta chaqiruv qoladi.
/// </para>
///
/// <para>
/// <b>Nega constraint'lar EF modelida, xom SQL'da emas?</b> Modeldagi
/// constraint snapshot'ga tushadi, ya'ni kelgusi <c>--autogenerate</c> uni
/// "ortiqcha" deb DROP qilmaydi. Faqat EF UMUMAN ifodalay olmaydigan narsa
/// (<c>app_rw</c> uchun GRANT/REVOKE) migratsiyadagi xom SQL'da:
/// <c>Migrations/Sql/anomaly_guards.sql</c>.
/// </para>
/// </summary>
internal static class AnomalyModel
{
    public static void Apply(ModelBuilder b)
    {
        ConfigureAuditLogJson(b);
        ConfigureAnomalyFlags(b);
    }

    /// <summary>
    /// SPEC §4.6: <c>audit_log</c> dagi <c>before</c>/<c>after</c> —
    /// <b><c>jsonb</c></b>, oddiy matn emas.
    ///
    /// <para>
    /// <b>Nega bu muhim.</b> Faza 0 bu ikki ustunni <c>text</c> qilib ko'chirgan.
    /// Matnda saqlangan snapshot bo'yicha "kim 500 000 dan ko'p summani
    /// o'zgartirgan" degan savolga javob berib bo'lmaydi — <c>LIKE</c> bilan
    /// JSON qidirish jiddiy tekshiruv emas. <c>jsonb</c> bilan bu oddiy
    /// so'rov: <c>(after-&gt;&gt;'Amount')::numeric &gt; 500000</c>. Bundan
    /// tashqari <c>jsonb</c> yaroqsiz JSON'ni INSERT paytida rad etadi, ya'ni
    /// buzilgan snapshot bazaga umuman tushmaydi.
    /// </para>
    /// <para>
    /// CLR turi <c>string?</c> bo'lib qoladi (<c>AuditLog</c> entity'siga
    /// TEGILMAGAN — u <c>Entities.cs</c> da, P1-21 bilan konflikt maydoni).
    /// Npgsql <c>string</c> ni <c>jsonb</c> ga o'zi moslaydi.
    /// </para>
    /// </summary>
    private static void ConfigureAuditLogJson(ModelBuilder b)
    {
        b.Entity<AuditLog>(e =>
        {
            e.Property(x => x.Before).HasColumnType("jsonb");
            e.Property(x => x.After).HasColumnType("jsonb");
        });
    }

    private static void ConfigureAnomalyFlags(ModelBuilder b)
    {
        b.Entity<FinanceAnomalyFlag>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Amount).HasPrecision(14, 2);
            e.Property(x => x.Details).HasColumnType("jsonb");

            // Yopgan foydalanuvchi — FK. RESTRICT: bayroqni yopgan odamni
            // o'chirib, kim yopganini yo'qotib bo'lmaydi.
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.ResolvedBy)
                .OnDelete(DeleteBehavior.Restrict);

            // IDEMPOTENTLIK AYNAN SHU YERDA. Tungi tekshiruv har yurishda bir
            // xil hodisalarni qayta ko'radi; bu indeks bo'lmasa panel bir
            // hafta ichida yuzlab bir xil bayroq ko'rsatardi.
            e.HasIndex(x => new { x.Kind, x.RefId }).IsUnique()
                .HasDatabaseName("ux_finance_anomaly_flags_kind_ref");

            // Direktor panelining asosiy so'rovi: "yopilmaganlar, yangisidan".
            // Qisman indeks — yopilganlar (vaqt o'tishi bilan ko'pchilik)
            // indeksga umuman kirmaydi.
            e.HasIndex(x => x.OccurredAt)
                .HasFilter("resolved_at is null")
                .HasDatabaseName("ix_finance_anomaly_flags_unresolved");

            e.ToTable(t =>
            {
                t.HasCheckConstraint(
                    "ck_finance_anomaly_flags_kind",
                    "kind in ('shift_variance','fast_reversal','off_hours_payment','paid_without_allocation')");
                t.HasCheckConstraint(
                    "ck_finance_anomaly_flags_ref_type",
                    "ref_type in ('cash_shift','payment','invoice')");

                // SPEC §4.6 — "cannot be dismissed, only resolved with a
                // written reason". Uchta ustun BIRGA to'ladi yoki birgalikda
                // bo'sh qoladi, va sabab hech qachon bo'sh satr bo'lmaydi.
                // Ilova ham shuni tekshiradi (400), lekin ilova chetlab
                // o'tilishi mumkin — bu esa yo'q.
                t.HasCheckConstraint(
                    "ck_finance_anomaly_flags_resolution",
                    "(resolved_at is null) = (resolved_by is null) "
                    + "and (resolved_at is null) = (resolved_reason is null) "
                    + "and (resolved_reason is null or btrim(resolved_reason) <> '')");
            });
        });
    }
}
