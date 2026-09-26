using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;

namespace SchoolLms.Infrastructure.Data;

/// <summary>
/// Maosh o'qituvchi bo'lmagan xodimlarga ham (docs/modules/employees-unified.md, migratsiya
/// <c>StaffSalary</c>): <c>users.phone / salary / salary_start_date</c> va
/// <c>expenses.employee_user_id</c>. Alohida fayl — <see cref="AppDbContext.OnModelCreating"/>
/// dagi qoida: o'zgarish qaysi migratsiyadan kelgan bo'lsa, o'sha faylda turadi.
/// </summary>
internal static class StaffSalaryModel
{
    public static void Apply(ModelBuilder b)
    {
        b.Entity<AppUser>(e =>
        {
            // Sukut qiymatlar bazada ham: eski qatorlar va xom SQL bilan qo'shilgan
            // akkauntlar ustunni to'ldirmasa ham NOT NULL buzilmaydi.
            e.Property(x => x.Phone).HasDefaultValue("");
            e.Property(x => x.Salary).HasPrecision(14, 2).HasDefaultValue(0m);
            e.Property(x => x.SalaryStartDate).HasDefaultValue("");

            e.ToTable(t => t.HasCheckConstraint("ck_users_salary", "salary >= 0"));
        });

        b.Entity<Expense>(e =>
        {
            // RESTRICT — `teacher_id` bilan bir xil sabab: berilgan maosh moliyaviy
            // tarix, xodim akkaunti bilan birga o'chib ketmasin.
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.EmployeeUserId)
                .OnDelete(DeleteBehavior.Restrict);

            // "Shu xodim shu davrda qancha oldi" — `(teacher_id, on_date)` ning ko'zgusi.
            e.HasIndex(x => new { x.EmployeeUserId, x.OnDate });

            e.ToTable(t =>
            {
                // `ck_expenses_teacher_only_salary` ning ko'zgusi.
                t.HasCheckConstraint("ck_expenses_employee_only_salary",
                    "employee_user_id is null or category = 'salary'");
                // Maosh BITTA odamga: o'qituvchiga yoki xodimga, ikkalasiga emas.
                t.HasCheckConstraint("ck_expenses_one_salary_recipient",
                    "num_nonnulls(teacher_id, employee_user_id) <= 1");
            });
        });
    }
}
