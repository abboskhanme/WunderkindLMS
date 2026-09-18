using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;

namespace SchoolLms.Infrastructure.Data;

/// <summary>
/// Rejalashtirilgan chiqim shabloni (F6.01, finance-parity.md §2.6) — EF
/// konfiguratsiyasi. Migratsiya: <c>ExpenseTemplates</c>.
///
/// <para>
/// <b>Nega alohida fayl?</b> <see cref="BillingModel"/>, <see cref="CashBoxModel"/>
/// bilan bir xil sabab (o'sha fayllarning boshidagi izoh): moliya modulini
/// bir nechta agent parallel qo'shadi, <see cref="AppDbContext.OnModelCreating"/>
/// ularning umumiy konflikt maydoniga aylanmasligi kerak.
/// </para>
///
/// <para>
/// <b>Baza darajasida CHECK yo'q toifa ustunida — ataylab.</b> Qolgan moliya
/// fayllarida check constraint'lar shu yerda modelga yoziladi (autogenerate
/// ularni DROP qilmasin uchun), lekin <c>category</c> bu qoidadan mustasno:
/// <c>expenses.category</c> ning o'zida ham DB CHECK yo'q (<c>BillingModel.
/// ConfigureExpenses</c> ga qarang) — yopiq ro'yxat <see cref="SchoolLms.
/// Application.Billing.Accounts"/> da, ilova qatlamida yashaydi. Ikkinchi,
/// mustaqil ro'yxat (bitta DB CHECK, bitta C# ro'yxati) ertami-kechmi
/// bir-biridan uzoqlashardi.
/// </para>
/// </summary>
internal static class ExpenseTemplateModel
{
    public static void Apply(ModelBuilder b)
    {
        b.Entity<ExpenseTemplate>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.Amount).HasPrecision(14, 2);
            e.Property(x => x.IsActive).HasDefaultValue(true);
            e.Property(x => x.CreatedAt).HasDefaultValueSql("now()");

            // Eslatma xizmati kuniga bir marta "bugun qaysi shablonlar
            // faol" so'raydi — shu ikkitasi bo'yicha filtrlaydi.
            e.HasIndex(x => new { x.IsActive, x.DayOfMonth });

            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_expense_templates_amount", "amount > 0");
                t.HasCheckConstraint("ck_expense_templates_name", "btrim(name) <> ''");
                // 1..28 — `BillingSettings.PaymentDueDay`/`OverdueAfterDay` bilan
                // bir xil chegara (`BillingModel.ConfigureSettings`): 29/30/31
                // hamma oyda bo'lavermaydi, fevral esa hech qachon.
                t.HasCheckConstraint("ck_expense_templates_day_of_month",
                    "day_of_month between 1 and 28");
            });
        });
    }
}
