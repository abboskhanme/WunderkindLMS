using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;

namespace SchoolLms.Infrastructure.Data;

/// <summary>
/// <c>class_homeroom_teachers</c> — sinf rahbarlari, ko'p-ko'pga (<see cref="ClassHomeroomTeacher"/>).
/// Mavjud rahbarlar <c>teachers.homeroom_class</c> dan migratsiyada ko'chiriladi.
/// </summary>
internal static class HomeroomModel
{
    public static void Apply(ModelBuilder b)
    {
        b.Entity<ClassHomeroomTeacher>(e =>
        {
            e.ToTable("class_homeroom_teachers");
            e.HasKey(x => new { x.ClassId, x.TeacherId });

            // Sinf yoki o'qituvchi o'chirilsa (bitiruvchi sinf rollover'da) — bog'lanish ham ketadi.
            e.HasOne<SchoolClass>().WithMany().HasForeignKey(x => x.ClassId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Teacher>().WithMany().HasForeignKey(x => x.TeacherId).OnDelete(DeleteBehavior.Cascade);

            // "O'qituvchining sinflari" — PK teskari tartibda.
            e.HasIndex(x => x.TeacherId).HasDatabaseName("ix_class_homeroom_teachers_teacher");
        });
    }
}
