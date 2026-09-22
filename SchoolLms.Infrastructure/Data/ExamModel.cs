using Microsoft.EntityFrameworkCore;
using SchoolLms.Domain;

namespace SchoolLms.Infrastructure.Data;

/// <summary>
/// Admission, block test and seasonal assessment — EF configuration.
/// Migration: <c>AdmissionAndExams</c>. Spec:
/// <c>docs/modules/admission-and-testing.md</c> §5.
///
/// <para>
/// <b>Why a separate file?</b> The same reason as <see cref="SalesMarketingModel"/>
/// and <see cref="GuardianModel"/>: <see cref="AppDbContext.OnModelCreating"/>
/// must not become a conflict zone, and a change lives in the file of the
/// migration that introduced it. So the new <c>leads.admission_status</c> and
/// <c>school_meta.admission_show_answers_to_candidate</c> columns are configured
/// HERE, not in the models of the migrations that created those tables.
/// </para>
///
/// <para>
/// <b>Constraints live in the model, not in raw SQL</b>, so the snapshot knows
/// them and the next <c>--autogenerate</c> does not DROP them. The one exception
/// is <c>ux_exam_types_name</c> — an expression index on <c>lower(btrim(name))</c>,
/// which EF cannot model; the migration creates it with raw SQL (the
/// <c>ux_surveys_slug</c> pattern), the snapshot never sees it, and therefore a
/// later autogenerate never touches it either.
/// </para>
///
/// <para>
/// <b>First real FKs onto <c>subjects</c> and <c>classes</c></b> (§5.14). Before
/// this migration those tables had no inbound FK. <c>restrict</c> here means
/// <c>DELETE /api/admin/subjects/{id}</c> and <c>/classes/{id}</c> can now hit a
/// 23503 — the B4 unit adds the 409/400 guard clauses in those controllers.
/// </para>
/// </summary>
internal static class ExamModel
{
    /// <summary>
    /// Generation expression of <c>seasonal_marks.period_key</c> (§5.12). Every
    /// <c>int</c> is cast to <c>text</c> explicitly: <c>text || integer</c> resolves to
    /// the STABLE <c>textanycat</c>, and PostgreSQL only accepts IMMUTABLE
    /// expressions in a generated column. Mirrored in C# by
    /// <see cref="SeasonalMark.BuildPeriodKey"/>.
    /// </summary>
    internal const string PeriodKeySql =
        "case period_kind "
        + "when 'monthly' then 'M:' || year::text || '-' || lpad(month::text, 2, '0') "
        + "when 'quarterly' then 'Q:' || year::text || '-' || quarter::text "
        + "else 'Y:' || year::text end";

    public static void Apply(ModelBuilder b)
    {
        ConfigureQuestionBanks(b);
        ConfigureQuestions(b);
        ConfigureQuestionOptions(b);
        ConfigureExamTypes(b);
        ConfigureExams(b);
        ConfigureExamSections(b);
        ConfigureExamParticipants(b);
        ConfigureExamSectionScores(b);
        ConfigureExamInvitations(b);
        ConfigureExamAttempts(b);
        ConfigureExamAnswers(b);
        ConfigureSeasonalMarks(b);
        ConfigureLeadAdmissionStatus(b);
        ConfigureSchoolMetaFlag(b);
    }

    // =====================================================================
    //  Question bank (§5.1–§5.3)
    // =====================================================================

    private static void ConfigureQuestionBanks(ModelBuilder b)
    {
        b.Entity<QuestionBank>(e =>
        {
            e.ToTable("question_banks", t =>
            {
                // 0 is a real grade — the preparatory "nol sinf".
                t.HasCheckConstraint("ck_question_banks_grade", "grade between 0 and 11");
                t.HasCheckConstraint(
                    "ck_question_banks_questions_per_test",
                    "questions_per_test is null or questions_per_test >= 1");
                t.HasCheckConstraint(
                    "ck_question_banks_time_limit_min",
                    "time_limit_min is null or time_limit_min between 1 and 600");
                t.HasCheckConstraint(
                    "ck_question_banks_points_per_correct",
                    "points_per_correct is null or points_per_correct > 0");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.PointsPerCorrect).HasPrecision(6, 2);
            e.Property(x => x.IsArchived).HasDefaultValue(false);

            // RESTRICT — a subject with a bank cannot silently disappear (§5.14).
            e.HasOne<Subject>().WithMany().HasForeignKey(x => x.SubjectId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.CreatedByUserId)
                .OnDelete(DeleteBehavior.SetNull);

            // One LIVE bank per grade × subject: that is what the EduSchool list
            // shows and how an exam picks a bank without asking. Archived banks
            // step out of the way without destroying the exams that used them.
            e.HasIndex(x => new { x.Grade, x.SubjectId }).HasDatabaseName("ux_question_banks_live")
                .IsUnique()
                .HasFilter("not is_archived");
        });
    }

    private static void ConfigureQuestions(ModelBuilder b)
    {
        b.Entity<Question>(e =>
        {
            e.ToTable("questions", t => t.HasCheckConstraint("ck_questions_text", "btrim(text) <> ''"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Order).HasDefaultValue(0);

            e.HasOne<QuestionBank>().WithMany().HasForeignKey(x => x.BankId)
                .OnDelete(DeleteBehavior.Cascade);

            // The options go with their question. An ANSWERED question cannot be
            // deleted at all — `exam_answers.question_id` is RESTRICT (§5.2).
            e.HasMany(x => x.Options).WithOne().HasForeignKey(o => o.QuestionId)
                .OnDelete(DeleteBehavior.Cascade);

            // The bank detail screen: a bank's questions in display order.
            e.HasIndex(x => new { x.BankId, x.Order }).HasDatabaseName("ix_questions_bank_order");
        });
    }

    private static void ConfigureQuestionOptions(ModelBuilder b)
    {
        b.Entity<QuestionOption>(e =>
        {
            e.ToTable("question_options", t =>
                t.HasCheckConstraint("ck_question_options_text", "btrim(text) <> ''"));
            e.HasKey(x => x.Id);
            e.Property(x => x.IsCorrect).HasDefaultValue(false);
            e.Property(x => x.Order).HasDefaultValue(0);

            e.HasIndex(x => new { x.QuestionId, x.Order })
                .HasDatabaseName("ix_question_options_question_order");

            // At most ONE correct option per question, enforced by the database
            // (§5.3). "At least one" cannot be said declaratively across rows
            // without a deferred trigger — the save transaction checks it.
            e.HasIndex(x => x.QuestionId).HasDatabaseName("ux_question_options_one_correct")
                .IsUnique()
                .HasFilter("is_correct");
        });
    }

    // =====================================================================
    //  Exams (§5.4–§5.6)
    // =====================================================================

    private static void ConfigureExamTypes(ModelBuilder b)
    {
        b.Entity<ExamType>(e =>
        {
            e.ToTable("exam_types", t => t.HasCheckConstraint("ck_exam_types_name", "btrim(name) <> ''"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Description).HasDefaultValue("");

            // `HasSentinel(true)` — the EF Core 8+ default-value trap: without it
            // `IsActive = false` is the CLR default, EF leaves it out of the
            // INSERT and the database default (true) wins — a type created as
            // inactive would silently be active. Same fix as SalesMarketingModel
            // and StudentsParityModel.
            e.Property(x => x.IsActive).HasDefaultValue(true).HasSentinel(true);

            // Unique name (case- and whitespace-insensitive) — `ux_exam_types_name`
            // is an expression index, created in the migration with raw SQL.
        });
    }

    private static void ConfigureExams(ModelBuilder b)
    {
        b.Entity<Exam>(e =>
        {
            e.ToTable("exams", t =>
            {
                t.HasCheckConstraint("ck_exams_title", "btrim(title) <> ''");
                t.HasCheckConstraint("ck_exams_kind", "kind in ('admission','block')");
                t.HasCheckConstraint("ck_exams_delivery", "delivery in ('online','manual')");
                t.HasCheckConstraint("ck_exams_status", "status in ('draft','published','closed','cancelled')");
                t.HasCheckConstraint("ck_exams_grade", "grade is null or grade between 0 and 11");
                t.HasCheckConstraint("ck_exams_time_limit_min", "time_limit_min is null or time_limit_min >= 1");

                // An online exam a candidate can actually sit (published or
                // closed) always has its window and time limit. A draft may still
                // lack them: §8.1 fills `time_limit_min` in at publish "if it is
                // still null", and a draft can be cancelled before it was ever
                // configured. (§5.5 states this check without the status clause;
                // that would contradict §8.1 — docs/ASSUMPTIONS.md, 2026-09-22.)
                t.HasCheckConstraint(
                    "ck_exams_online_window",
                    "delivery <> 'online' or status in ('draft','cancelled') "
                    + "or (opens_at is not null and closes_at is not null and time_limit_min is not null)");
                t.HasCheckConstraint("ck_exams_window_order", "opens_at is null or closes_at > opens_at");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasDefaultValue(ExamStatus.Draft);

            e.HasOne<ExamType>().WithMany().HasForeignKey(x => x.ExamTypeId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.CreatedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasMany(x => x.Sections).WithOne().HasForeignKey(s => s.ExamId)
                .OnDelete(DeleteBehavior.Cascade);

            // The exam list filters by kind + status; the results list by date.
            e.HasIndex(x => new { x.Kind, x.Status }).HasDatabaseName("ix_exams_kind_status");
            e.HasIndex(x => x.ExamDate).HasDatabaseName("ix_exams_exam_date");
        });
    }

    private static void ConfigureExamSections(ModelBuilder b)
    {
        b.Entity<ExamSection>(e =>
        {
            e.ToTable("exam_sections", t =>
            {
                // Same shape as the bank's own settings — a section copies them at publish.
                t.HasCheckConstraint(
                    "ck_exam_sections_question_count",
                    "question_count is null or question_count >= 1");
                t.HasCheckConstraint(
                    "ck_exam_sections_points_per_correct",
                    "points_per_correct is null or points_per_correct > 0");
                // `>= 0`, not `> 0`: an online draft section has no ceiling until
                // publish writes `question_count × points_per_correct`.
                t.HasCheckConstraint("ck_exam_sections_max_score", "max_score >= 0");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.PointsPerCorrect).HasPrecision(6, 2);
            e.Property(x => x.MaxScore).HasPrecision(6, 2);
            e.Property(x => x.Order).HasDefaultValue(0);

            e.HasOne<Subject>().WithMany().HasForeignKey(x => x.SubjectId)
                .OnDelete(DeleteBehavior.Restrict);
            // RESTRICT — a bank that feeds an exam cannot be deleted (§6.2 → 409).
            e.HasOne<QuestionBank>().WithMany().HasForeignKey(x => x.BankId)
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(x => new { x.ExamId, x.SubjectId }).IsUnique()
                .HasDatabaseName("ux_exam_sections_exam_subject");
            e.HasIndex(x => new { x.ExamId, x.Order }).HasDatabaseName("ix_exam_sections_exam_order");
        });
    }

    // =====================================================================
    //  Participants, scores, invitations, attempts, answers (§5.7–§5.11)
    // =====================================================================

    private static void ConfigureExamParticipants(ModelBuilder b)
    {
        b.Entity<ExamParticipant>(e =>
        {
            e.ToTable("exam_participants", t =>
            {
                t.HasCheckConstraint("ck_exam_participants_kind", "participant_kind in ('lead','student')");

                // A `student` participant always points at a pupil; a `lead`
                // participant never does (the `ck_student_guardians_*` shape).
                t.HasCheckConstraint(
                    "ck_exam_participants_student_pointer",
                    "(participant_kind = 'student') = (student_id is not null)");

                // Only a `lead` participant may point at a lead — but it may also
                // point at NOTHING: §5.7 wrote `(participant_kind = 'lead') =
                // (lead_id is not null)`, which the `ON DELETE SET NULL` below
                // would violate the moment a candidate is enrolled (the enrol
                // endpoint deletes the lead) — every enrolment would fail on this
                // CHECK. "A new lead participant has a lead_id" is the service's
                // rule on insert.
                t.HasCheckConstraint(
                    "ck_exam_participants_lead_pointer",
                    "participant_kind = 'lead' or lead_id is null");

                t.HasCheckConstraint(
                    "ck_exam_participants_status",
                    "status in ('assigned','in_progress','finished','absent','cancelled')");
                t.HasCheckConstraint(
                    "ck_exam_participants_points",
                    "total_points is null or (total_points >= 0 and (max_points is null or total_points <= max_points))");
                t.HasCheckConstraint("ck_exam_participants_percent", "percent is null or percent between 0 and 100");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasDefaultValue(ExamParticipantStatus.Assigned);
            e.Property(x => x.TotalPoints).HasPrecision(8, 2);
            e.Property(x => x.MaxPoints).HasPrecision(8, 2);
            e.Property(x => x.Percent).HasPrecision(5, 2);

            e.HasOne<Exam>().WithMany().HasForeignKey(x => x.ExamId)
                .OnDelete(DeleteBehavior.Cascade);

            // SET NULL, not the CASCADE of §5.7 (changed 2026-09-22): enrolment
            // DELETES the lead, and the candidate's sitting — which exam, what
            // score — must survive it. The board's plain delete keeps working too.
            // The surviving row is anonymous by design: `lead_conversions` keeps no
            // personal data either (client decision, docs/ASSUMPTIONS.md).
            e.HasOne<Lead>().WithMany().HasForeignKey(x => x.LeadId)
                .OnDelete(DeleteBehavior.SetNull);

            // CASCADE — deleting a pupil is the "created by mistake" path; the
            // normal exit is archiving (§5.14).
            e.HasOne<Student>().WithMany().HasForeignKey(x => x.StudentId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne<SchoolClass>().WithMany().HasForeignKey(x => x.ClassId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.ScoredByUserId)
                .OnDelete(DeleteBehavior.SetNull);

            // Adding the same person twice is structurally impossible, which is
            // what makes `POST /exams/{id}/participants` idempotent (§6.3).
            e.HasIndex(x => new { x.ExamId, x.LeadId }).HasDatabaseName("ux_exam_participants_exam_lead")
                .IsUnique()
                .HasFilter("lead_id is not null");
            e.HasIndex(x => new { x.ExamId, x.StudentId }).HasDatabaseName("ux_exam_participants_exam_student")
                .IsUnique()
                .HasFilter("student_id is not null");

            e.HasIndex(x => new { x.ExamId, x.Status }).HasDatabaseName("ix_exam_participants_exam_status");
            e.HasIndex(x => x.LeadId).HasDatabaseName("ix_exam_participants_lead");
            e.HasIndex(x => x.StudentId).HasDatabaseName("ix_exam_participants_student");
        });
    }

    private static void ConfigureExamSectionScores(ModelBuilder b)
    {
        b.Entity<ExamSectionScore>(e =>
        {
            e.ToTable("exam_section_scores", t =>
                t.HasCheckConstraint("ck_exam_section_scores_points", "points >= 0 and points <= max_points"));
            e.HasKey(x => new { x.ParticipantId, x.SectionId });
            e.Property(x => x.Points).HasPrecision(8, 2).HasDefaultValue(0m);
            e.Property(x => x.MaxPoints).HasPrecision(8, 2);

            e.HasOne<ExamParticipant>().WithMany().HasForeignKey(x => x.ParticipantId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne<ExamSection>().WithMany().HasForeignKey(x => x.SectionId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureExamInvitations(ModelBuilder b)
    {
        b.Entity<ExamInvitation>(e =>
        {
            e.ToTable("exam_invitations", t =>
                t.HasCheckConstraint("ck_exam_invitations_window", "valid_until > valid_from"));
            e.HasKey(x => x.Id);

            e.HasOne<ExamParticipant>().WithMany().HasForeignKey(x => x.ParticipantId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.IssuedByUserId)
                .OnDelete(DeleteBehavior.SetNull);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.RevokedByUserId)
                .OnDelete(DeleteBehavior.SetNull);

            // Every public request resolves the token through its hash.
            e.HasIndex(x => x.TokenHash).IsUnique().HasDatabaseName("ux_exam_invitations_token_hash");

            // At most ONE live link per participant. Re-issuing revokes the old
            // row and inserts a new one, so who re-sent what stays on record.
            e.HasIndex(x => x.ParticipantId, "ux_exam_invitations_live")
                .HasDatabaseName("ux_exam_invitations_live")
                .IsUnique()
                .HasFilter("revoked_at is null");

            // The partial index above only covers live rows; the card's link
            // history and the cascade from `exam_participants` need all of them.
            // Both are NAMED model indexes — two unnamed `HasIndex(x => x.ParticipantId)`
            // calls would be merged into one by EF.
            e.HasIndex(x => x.ParticipantId, "ix_exam_invitations_participant")
                .HasDatabaseName("ix_exam_invitations_participant");

            // The expiry sweep.
            e.HasIndex(x => x.ValidUntil).HasDatabaseName("ix_exam_invitations_valid_until");
        });
    }

    private static void ConfigureExamAttempts(ModelBuilder b)
    {
        b.Entity<ExamAttempt>(e =>
        {
            e.ToTable("exam_attempts", t =>
            {
                t.HasCheckConstraint("ck_exam_attempts_status", "status in ('in_progress','finished')");
                t.HasCheckConstraint(
                    "ck_exam_attempts_finish_reason",
                    "finish_reason is null or finish_reason in ('manual','timer','admin')");
                // §8.3 writes these three in one transaction — they never disagree.
                t.HasCheckConstraint(
                    "ck_exam_attempts_finished",
                    "(status = 'finished') = (finished_at is not null) "
                    + "and (finished_at is null) = (finish_reason is null)");
                t.HasCheckConstraint("ck_exam_attempts_answered_count", "answered_count >= 0");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasDefaultValue(ExamAttemptStatus.InProgress);
            e.Property(x => x.AnsweredCount).HasDefaultValue(0);
            e.Property(x => x.AbuseFlagged).HasDefaultValue(false);

            e.HasOne<ExamParticipant>().WithMany().HasForeignKey(x => x.ParticipantId)
                .OnDelete(DeleteBehavior.Cascade);

            // NO ACTION, not the RESTRICT of §5.10. Both mean "an invitation with
            // an attempt cannot be deleted on its own", but RESTRICT is checked
            // immediately, while the participant's cascade is still running:
            // deleting a pupil (or an exam) removes the invitation and the attempt
            // in one statement, and RESTRICT could fire on the invitation before
            // the attempt is gone. NO ACTION checks at the end of the statement.
            e.HasOne<ExamInvitation>().WithMany().HasForeignKey(x => x.InvitationId)
                .OnDelete(DeleteBehavior.NoAction);

            // One attempt per participant, ever (§5.10). A second `start`
            // returns the existing attempt; a retake is an admin reset.
            e.HasIndex(x => x.ParticipantId).IsUnique().HasDatabaseName("ux_exam_attempts_participant");

            // The deadline sweep: `status = 'in_progress' and deadline_at < now`.
            e.HasIndex(x => new { x.Status, x.DeadlineAt }).HasDatabaseName("ix_exam_attempts_status_deadline");
        });
    }

    private static void ConfigureExamAnswers(ModelBuilder b)
    {
        b.Entity<ExamAnswer>(e =>
        {
            e.ToTable("exam_answers", t =>
                // Answers cannot be cleared once chosen (§7.8), so "answered" is
                // exactly "has an option and a time".
                t.HasCheckConstraint(
                    "ck_exam_answers_answered",
                    "(selected_option_id is null) = (answered_at is null)"));
            e.HasKey(x => new { x.AttemptId, x.QuestionId });

            e.HasOne<ExamAttempt>().WithMany().HasForeignKey(x => x.AttemptId)
                .OnDelete(DeleteBehavior.Cascade);
            // RESTRICT — an answered question (and a chosen option) stays, so a
            // finished paper can always be reviewed and regraded (§5.2).
            e.HasOne<Question>().WithMany().HasForeignKey(x => x.QuestionId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<QuestionOption>().WithMany().HasForeignKey(x => x.SelectedOptionId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ExamSection>().WithMany().HasForeignKey(x => x.SectionId)
                .OnDelete(DeleteBehavior.Cascade);

            // The paper's order is stable across reloads — one question per slot.
            e.HasIndex(x => new { x.AttemptId, x.Order }).IsUnique()
                .HasDatabaseName("ux_exam_answers_attempt_order");
        });
    }

    // =====================================================================
    //  Seasonal assessment (§5.12)
    // =====================================================================

    private static void ConfigureSeasonalMarks(ModelBuilder b)
    {
        b.Entity<SeasonalMark>(e =>
        {
            e.ToTable("seasonal_marks", t =>
            {
                t.HasCheckConstraint(
                    "ck_seasonal_marks_period_kind",
                    "period_kind in ('monthly','quarterly','yearly')");
                t.HasCheckConstraint("ck_seasonal_marks_month_kind", "(period_kind = 'monthly') = (month is not null)");
                t.HasCheckConstraint(
                    "ck_seasonal_marks_quarter_kind",
                    "(period_kind = 'quarterly') = (quarter is not null)");
                t.HasCheckConstraint("ck_seasonal_marks_month", "month is null or month between 1 and 12");
                t.HasCheckConstraint("ck_seasonal_marks_quarter", "quarter is null or quarter between 1 and 4");
                t.HasCheckConstraint("ck_seasonal_marks_year", "year between 2000 and 2100");
                t.HasCheckConstraint("ck_seasonal_marks_score", "score is null or (score >= 0 and score <= 100)");
                // EduSchool's inline editor refuses a 1–2 character comment.
                t.HasCheckConstraint(
                    "ck_seasonal_marks_comment",
                    "comment is null or char_length(btrim(comment)) >= 3");
                // An empty row is not a mark — `bulk` deletes instead (§6.6).
                t.HasCheckConstraint("ck_seasonal_marks_not_empty", "score is not null or comment is not null");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Score).HasPrecision(5, 2);

            // Generated by PostgreSQL, never written by the application. A
            // monthly row without a month makes the key NULL, so that mistake
            // surfaces as a NOT NULL violation on `period_key` (23502) before the
            // CHECKs run — rejected either way.
            e.Property(x => x.PeriodKey).HasComputedColumnSql(PeriodKeySql, stored: true);

            e.HasOne<Student>().WithMany().HasForeignKey(x => x.StudentId)
                .OnDelete(DeleteBehavior.Cascade);
            // RESTRICT — a class or subject with marks cannot vanish from under a
            // report (§5.14; the controllers turn it into a readable 400/409).
            e.HasOne<SchoolClass>().WithMany().HasForeignKey(x => x.ClassId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Subject>().WithMany().HasForeignKey(x => x.SubjectId)
                .OnDelete(DeleteBehavior.Restrict);
            e.HasOne<AppUser>().WithMany().HasForeignKey(x => x.CreatedByUserId)
                .OnDelete(DeleteBehavior.SetNull);

            // One mark per pupil, subject and period — including two YEARLY marks,
            // which a unique index over the nullable month/quarter columns would
            // let through (NULLs are distinct).
            e.HasIndex(x => new { x.StudentId, x.SubjectId, x.PeriodKey }).IsUnique()
                .HasDatabaseName("ux_seasonal_marks_student_subject_period");

            // The entry screen, the pivot and the coverage report all filter on this.
            e.HasIndex(x => new { x.ClassId, x.SubjectId, x.PeriodKey })
                .HasDatabaseName("ix_seasonal_marks_class_subject_period");
            e.HasIndex(x => x.PeriodKey).HasDatabaseName("ix_seasonal_marks_period_key");
        });
    }

    // =====================================================================
    //  Existing tables (§5.13)
    // =====================================================================

    /// <summary>
    /// <c>leads.admission_status</c>. Every existing lead becomes <c>none</c> —
    /// which is the truth: none of them has been in an admission exam.
    /// </summary>
    private static void ConfigureLeadAdmissionStatus(ModelBuilder b)
    {
        b.Entity<Lead>(e =>
        {
            e.Property(x => x.AdmissionStatus).HasDefaultValue(LeadAdmissionStatus.None);

            // The candidate list's only filter.
            e.HasIndex(x => x.AdmissionStatus).HasDatabaseName("ix_leads_admission_status");

            // No 'enrolled' — enrolment deletes the lead (2026-09-22), so a stored
            // 'enrolled' could only be a bug. See LeadAdmissionStatus.
            e.ToTable(t => t.HasCheckConstraint(
                "ck_leads_admission_status",
                "admission_status in ('none','invited','testing','tested','accepted','rejected')"));
        });
    }

    /// <summary>§7.7: the candidate does not see the answer key unless the school turns it on.</summary>
    private static void ConfigureSchoolMetaFlag(ModelBuilder b) =>
        b.Entity<SchoolMeta>().Property(x => x.AdmissionShowAnswersToCandidate).HasDefaultValue(false);
}
