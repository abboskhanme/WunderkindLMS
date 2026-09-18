using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;

namespace SchoolLms.Infrastructure.Data;

/// <summary>
/// Tranzaksiya turi katalogi — EF konfiguratsiyasi. Migratsiya: <c>TransactionTypes</c>.
///
/// <para>
/// <b>Nega alohida fayl?</b> <see cref="CashBoxModel"/>, <see cref="ExpenseTemplateModel"/>
/// bilan bir xil sabab: <see cref="AppDbContext.OnModelCreating"/> konflikt
/// maydoniga aylanmasligi kerak, va o'zgarish qaysi migratsiyadan kelgan
/// bo'lsa — o'sha faylda turadi. Shu sababdan <see cref="CashBoxTransaction.TransactionTypeId"/>
/// ning FK konfiguratsiyasi ham (garchi entity'ning o'zi <c>CashBoxModel.cs</c>
/// da bo'lsa ham) SHU yerda — ustun bu migratsiya qo'shadi, <c>CashBoxes</c>
/// migratsiyasi emas.
/// </para>
/// </summary>
internal static class TransactionTypeModel
{
    public static void Apply(ModelBuilder b)
    {
        ConfigureTransactionTypes(b);
        ConfigureCashBoxTransactionType(b);
    }

    private static void ConfigureTransactionTypes(ModelBuilder b)
    {
        b.Entity<TransactionType>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.IsActive).HasDefaultValue(true);
            e.Property(x => x.IsSeeded).HasDefaultValue(false);
            e.Property(x => x.Position).HasDefaultValue(0);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");

            // Bitta kind ichida nom takrorlanmaydi — `AdjustmentReason` dagi
            // bilan bir xil naqsh (`ix_adjustment_reasons_kind_name`).
            e.HasIndex(x => new { x.Kind, x.Name }).IsUnique();

            // Ro'yxat so'rovining o'zi: "shu kindning faol turlari, tartib bo'yicha".
            e.HasIndex(x => new { x.Kind, x.IsActive, x.Position });

            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_transaction_types_kind", "kind in ('in','out')");
                t.HasCheckConstraint("ck_transaction_types_name", "btrim(name) <> ''");
            });
        });
    }

    /// <summary>
    /// <see cref="CashBoxTransaction.TransactionTypeId"/> — nullable, RESTRICT:
    /// ishlatilgan tur o'chirilsa ham tarixiy tranzaksiya yorlig'ini yo'qotmasin
    /// (<c>TransactionTypeService.DeleteAsync</c> shuning uchun oldindan
    /// tekshiradi va tushunarli 409 beradi — xom 23503 o'rniga).
    /// </summary>
    private static void ConfigureCashBoxTransactionType(ModelBuilder b)
    {
        b.Entity<CashBoxTransaction>(e =>
        {
            e.HasOne<TransactionType>().WithMany()
                .HasForeignKey(x => x.TransactionTypeId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(x => x.TransactionTypeId);
        });
    }
}
