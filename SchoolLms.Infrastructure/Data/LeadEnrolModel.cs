using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;

namespace SchoolLms.Infrastructure.Data;

/// <summary>
/// Lid → o'quvchi statistikasi (<c>LeadConversions</c> migratsiyasi). Lidning o'zi
/// aylantirilganda o'chiriladi; bu jadval faqat SONni saqlaydi — shaxsiy ma'lumotsiz.
/// </summary>
internal static class LeadEnrolModel
{
    public static void Apply(ModelBuilder b)
    {
        b.Entity<LeadConversion>(e =>
        {
            e.ToTable("lead_conversions", t =>
                t.HasCheckConstraint("ck_lead_conversions_source", "source in ('manual','survey')"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasDefaultValueSql("gen_random_uuid()");
            e.Property(x => x.Source).HasDefaultValue(LeadSource.Manual);
            e.Property(x => x.ConvertedAt).HasDefaultValueSql("now()");

            // SET NULL: ariza o'chirilsa ham son qolishi kerak.
            e.HasOne<Survey>().WithMany().HasForeignKey(x => x.SurveyId)
                .OnDelete(DeleteBehavior.SetNull);

            // Voronka: manba bo'yicha sanash va ariza filtri.
            e.HasIndex(x => new { x.Source, x.SurveyId }).HasDatabaseName("ix_lead_conversions_source");
        });
    }
}
