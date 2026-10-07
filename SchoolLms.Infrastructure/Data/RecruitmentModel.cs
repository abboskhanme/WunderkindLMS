using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;

namespace SchoolLms.Infrastructure.Data;

/// <summary>Ishga qabul va adaptatsiya (Recruitment.cs).</summary>
internal static class RecruitmentModel
{
    /// <summary>The 11 adaptation steps the school starts with — renamed, moved or switched off in settings.</summary>
    private static readonly (string Title, string Description, int Day)[] Steps =
    [
        ("Hujjatlar va mehnat shartnomasi", "Shaxsiy hujjatlar topshiriladi, mehnat shartnomasi imzolanadi.", 1),
        ("Ish joyi va tizimga kirish", "Ish joyi tayyorlanadi, LMS login beriladi, Telegram botga ulanadi.", 1),
        ("Maktab bilan tanishuv", "Binolar, xavfsizlik qoidalari, ichki tartib va kiyinish qoidalari.", 2),
        ("Mentor biriktirish", "Tajribali hamkasb murabbiy (mentor) etib tayinlanadi.", 2),
        ("Rahbariyat va jamoa bilan tanishuv", "Direktor, o'rinbosarlar, metodbirlashma va bo'lim bilan tanishuv.", 3),
        ("O'quv dasturi va LMS treningi", "O'quv dasturi, dars jadvali, jurnal va baholash tartibi.", 5),
        ("Mentor darslarini kuzatish", "Kamida ikkita mentor darsini kuzatib, xulosa yoziladi.", 7),
        ("Mentor kuzatuvida birinchi darslar", "Birinchi mustaqil darslar mentor ishtirokida o'tiladi.", 10),
        ("Oraliq suhbat (HR)", "Ikki haftalik natijalar, qiyinchiliklar va yordam ehtiyoji muhokamasi.", 14),
        ("Dars tahlili (metodist)", "Metodist darsni kuzatadi va dars tahlili varag'ini to'ldiradi.", 21),
        ("Yakuniy baholash (direktor)", "Adaptatsiya natijalari va doimiy ishga o'tkazish qarori.", 30),
    ];

    public static void Apply(ModelBuilder b)
    {
        b.Entity<Vacancy>(e =>
        {
            e.ToTable("vacancies");
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Department).HasMaxLength(100);
            e.Property(x => x.Description).HasMaxLength(4000);
            e.Property(x => x.Requirements).HasMaxLength(4000);
            e.Property(x => x.Slug).HasMaxLength(80);
            e.Property(x => x.Status).HasMaxLength(20);
            e.HasIndex(x => x.Slug).IsUnique();
            e.ToTable(t => t.HasCheckConstraint("ck_vacancies_status", "status in ('open','closed')"));
        });

        b.Entity<JobApplicant>(e =>
        {
            e.ToTable("job_applicants");
            e.HasKey(x => x.Id);
            e.Property(x => x.FullName).HasMaxLength(200);
            e.Property(x => x.Phone).HasMaxLength(20);
            e.Property(x => x.Email).HasMaxLength(200);
            e.Property(x => x.Position).HasMaxLength(200);
            e.Property(x => x.Experience).HasMaxLength(2000);
            e.Property(x => x.About).HasMaxLength(4000);
            e.Property(x => x.CvPath).HasMaxLength(300);
            e.Property(x => x.CvName).HasMaxLength(200);
            e.Property(x => x.Source).HasMaxLength(20);
            e.Property(x => x.Stage).HasMaxLength(30);
            e.Property(x => x.RejectReason).HasMaxLength(1000);
            e.Property(x => x.RejectedAtStage).HasMaxLength(30);
            e.Property(x => x.HiredOn).HasColumnType("date");
            e.HasOne<Vacancy>().WithMany().HasForeignKey(x => x.VacancyId).OnDelete(DeleteBehavior.SetNull);
            e.HasIndex(x => new { x.Stage, x.StageChangedAt });
            e.HasIndex(x => x.Phone);
            e.ToTable(t => t.HasCheckConstraint("ck_job_applicants_stage",
                "stage in ('new','cv','test','demo','hr_interview','director_interview','onboarding','adapted','rejected')"));
        });

        b.Entity<ApplicantStageResult>(e =>
        {
            e.ToTable("applicant_stage_results");
            e.HasKey(x => x.Id);
            e.Property(x => x.Stage).HasMaxLength(30);
            e.Property(x => x.Comment).HasMaxLength(4000);
            e.HasOne<JobApplicant>().WithMany().HasForeignKey(x => x.ApplicantId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.ApplicantId, x.Stage }).IsUnique();
            e.ToTable(t => t.HasCheckConstraint("ck_applicant_stage_results_score", "score is null or (score >= 0 and score <= 100)"));
        });

        b.Entity<ApplicantEvent>(e =>
        {
            e.ToTable("applicant_events");
            e.HasKey(x => x.Id);
            e.Property(x => x.FromStage).HasMaxLength(30);
            e.Property(x => x.ToStage).HasMaxLength(30);
            e.Property(x => x.Note).HasMaxLength(1000);
            e.HasOne<JobApplicant>().WithMany().HasForeignKey(x => x.ApplicantId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.ApplicantId, x.At });
        });

        b.Entity<OnboardingStepTemplate>(e =>
        {
            e.ToTable("onboarding_step_templates");
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(1000);
            e.ToTable(t => t.HasCheckConstraint("ck_onboarding_step_templates_day", "due_day >= 1 and due_day <= 30"));
            e.HasData(Steps.Select((s, i) => new OnboardingStepTemplate
            {
                Id = new Guid($"7b1e0c00-0000-4000-8000-0000000000{i + 1:00}"),
                Position = i + 1, Title = s.Title, Description = s.Description, DueDay = s.Day, IsActive = true,
            }));
        });

        b.Entity<ApplicantOnboardingStep>(e =>
        {
            e.ToTable("applicant_onboarding_steps");
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(1000);
            e.Property(x => x.Comment).HasMaxLength(1000);
            e.Property(x => x.DueOn).HasColumnType("date");
            e.HasOne<JobApplicant>().WithMany().HasForeignKey(x => x.ApplicantId).OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.ApplicantId, x.Position });
        });
    }
}
