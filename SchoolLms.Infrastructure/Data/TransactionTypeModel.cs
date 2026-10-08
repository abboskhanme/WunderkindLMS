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

            // Salary recipient of a Chiqim (migration CashBoxSalaryRecipient, 2026-10-09). RESTRICT for the
            // reason `expenses.teacher_id` has it: paid salary is financial history.
            e.HasOne<Teacher>().WithMany().HasForeignKey(x => x.TeacherId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.EmployeeUserId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("fk_cash_box_transactions_users_employee_user_id");
            e.HasIndex(x => x.TeacherId);
            e.HasIndex(x => x.EmployeeUserId);

            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_cash_box_transactions_one_recipient",
                    "num_nonnulls(teacher_id, employee_user_id) <= 1");
                t.HasCheckConstraint("ck_cash_box_transactions_recipient_pay_out",
                    "(teacher_id is null and employee_user_id is null) or kind = 'pay_out'");
            });
        });
    }
}
