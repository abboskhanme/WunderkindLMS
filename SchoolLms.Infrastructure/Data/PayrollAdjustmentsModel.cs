using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;

namespace SchoolLms.Infrastructure.Data;

/// <summary>
/// Bonus / jarima (F11.01, F11.02) — EF konfiguratsiyasi
/// (<c>docs/modules/finance-parity.md</c> §3.2, Batch B, B2/B3).
///
/// <para>
/// <b>Nega alohida fayl?</b> <see cref="FinanceParityModel"/> va boshqalar
/// bilan bir xil sabab: <see cref="AppDbContext.OnModelCreating"/> konflikt
/// maydoniga aylanmasligi kerak.
/// </para>
/// </summary>
internal static class PayrollAdjustmentsModel
{
    public static void Apply(ModelBuilder b)
    {
        ConfigureAdjustmentReasons(b);
        ConfigurePayrollAdjustments(b);
    }

    // =====================================================================
    //  B2 — sabab katalogi (F11.02). Moliyaviy EMAS — to'liq CRUD.
    // =====================================================================

    private static void ConfigureAdjustmentReasons(ModelBuilder b)
    {
        b.Entity<AdjustmentReason>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.IsActive).HasDefaultValue(true);
            e.Property(x => x.Position).HasDefaultValue(0);

            // `payroll_adjustments.(reason_id, kind)` ning kompozit FK nishoni
            // (`StudyGroupModel.cs` dagi bilan bir xil naqsh — `unique (id, kind)`,
            // EF buni "alternate key" deb ataydi). Bu sabab bilan yozilgan
            // qatorning `kind`i sababning kind'idan ADASHIB QOLA OLMASLIGINI
            // baza darajasida kafolatlaydi — xizmat tekshiruvi buzilsa ham.
            e.HasAlternateKey(x => new { x.Id, x.Kind });

            // Bitta kind ichida nom takrorlanmaydi (masalan ikkita "Kechikish"
            // jarima sababi) — ro'yxatni chalkashtirmaslik uchun.
            e.HasIndex(x => new { x.Kind, x.Name }).IsUnique();

            e.ToTable(t => t.HasCheckConstraint(
                "ck_adjustment_reasons_kind", "kind in ('bonus','penalty')"));
        });
    }

    // =====================================================================
    //  B3 — bonus/jarima registri (F11.01). MOLIYAVIY, FAQAT QO'SHILADI.
    // =====================================================================

    private static void ConfigurePayrollAdjustments(ModelBuilder b)
    {
        b.Entity<PayrollAdjustment>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.Amount).HasPrecision(14, 2);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");

            // Aynan bitta identifikatsiya — RESTRICT: xodim o'chirilsa ham
            // (arxivlansa ham) pul tarixi yo'qolmasin.
            e.HasOne<Teacher>().WithMany().HasForeignKey(x => x.TeacherId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.CreatedBy)
                .OnDelete(DeleteBehavior.Restrict);

            // Kompozit FK: yozuvning sababi AYNAN shu yozuvning kind'iga tegishli
            // bo'lgan sabab bo'lishi shart (yuqoridagi izoh).
            e.HasOne<AdjustmentReason>().WithMany()
                .HasForeignKey(x => new { x.ReasonId, x.Kind })
                .HasPrincipalKey(r => new { r.Id, r.Kind })
                .OnDelete(DeleteBehavior.Restrict);

            // Storno — o'ziga havola (cash_handovers naqshi).
            e.HasOne<PayrollAdjustment>().WithMany().HasForeignKey(x => x.ReversalOf)
                .OnDelete(DeleteBehavior.Restrict);

            // Bitta yozuvni ikki marta storno qilib bo'lmaydi — to'liq unikal
            // indeks (`cash_handovers` dagi bilan bir xil sabab: NULL lar
            // bir-biridan farqli, ya'ni bu minglab oddiy qatorga xalaqit bermaydi).
            e.HasIndex(x => x.ReversalOf).IsUnique();

            // "Shu xodimning shu oydagi bonus/jarimalari" — payroll fill (F3.03,
            // HR-09) va ekrandagi filtr uchun asosiy so'rov.
            e.HasIndex(x => new { x.TeacherId, x.PeriodYear, x.PeriodMonth });
            e.HasIndex(x => new { x.UserId, x.PeriodYear, x.PeriodMonth });

            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_payroll_adjustments_amount", "amount > 0");
                t.HasCheckConstraint("ck_payroll_adjustments_kind", "kind in ('bonus','penalty')");
                // `hr_employees` dagi `ck_hr_employees_identity` bilan bir xil naqsh.
                t.HasCheckConstraint(
                    "ck_payroll_adjustments_identity", "num_nonnulls(teacher_id, user_id) = 1");
                t.HasCheckConstraint(
                    "ck_payroll_adjustments_period_month", "period_month between 1 and 12");
                t.HasCheckConstraint(
                    "ck_payroll_adjustments_period_year", "period_year between 2000 and 2100");
                t.HasCheckConstraint(
                    "ck_payroll_adjustments_reversal_not_self", "reversal_of is null or reversal_of <> id");
                // finance-parity §3.2 B3: `(reversal_of is null) = (reversal_reason is null)`.
                t.HasCheckConstraint(
                    "ck_payroll_adjustments_reversal",
                    "(reversal_of is null) = (reversal_reason is null)");
            });
        });
    }
}
