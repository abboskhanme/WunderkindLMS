using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;

namespace SchoolLms.Infrastructure.Data;

/// <summary>Teacher certificates and named allowances (<c>TeacherAllowances</c> migration, docs/modules/teacher-allowances.md).</summary>
internal static class TeacherAllowancesModel
{
    public static void Apply(ModelBuilder b)
    {
        b.Entity<TeacherAllowance>(e =>
        {
            e.ToTable("teacher_allowances", t =>
            {
                t.HasCheckConstraint("ck_teacher_allowances_source", "source in ('certificate','ustama')");
                t.HasCheckConstraint("ck_teacher_allowances_kind", "amount_kind in ('percent','fixed')");
                t.HasCheckConstraint("ck_teacher_allowances_amount",
                    "amount >= 0 and (amount_kind <> 'percent' or amount <= 1000)");
                t.HasCheckConstraint("ck_teacher_allowances_from", "from_month ~ '^[0-9]{4}-(0[1-9]|1[0-2])$'");
                t.HasCheckConstraint("ck_teacher_allowances_to",
                    "to_month is null or (to_month ~ '^[0-9]{4}-(0[1-9]|1[0-2])$' and to_month >= from_month)");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Source).HasMaxLength(16);
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.AmountKind).HasMaxLength(16);
            e.Property(x => x.Amount).HasPrecision(14, 2);
            e.Property(x => x.FromMonth).HasMaxLength(7);
            e.Property(x => x.ToMonth).HasMaxLength(7);
            e.Property(x => x.Level).HasMaxLength(64);
            e.Property(x => x.Number).HasMaxLength(64);
            e.Property(x => x.FileUrl).HasMaxLength(500);
            e.Property(x => x.Note).HasMaxLength(1000);
            e.HasIndex(x => x.TeacherId).HasDatabaseName("ix_teacher_allowances_teacher");
            e.HasOne<Teacher>().WithMany().HasForeignKey(x => x.TeacherId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
