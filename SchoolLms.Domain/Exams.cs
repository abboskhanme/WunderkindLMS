namespace SchoolLms.Domain;

// ===========================================================================
//  Admission, entrance test and block test — one question bank, one delivery
//  engine, one scoring engine.
//  Spec: docs/modules/admission-and-testing.md §2.1, §5.1–§5.11. Unit: A1.
//  Mapping: SchoolLms.Infrastructure/Data/ExamModel.cs. Migration: AdmissionAndExams.
// ===========================================================================
//
//  ONE BANK, TWO WAYS IN (§2.1)
//  ----------------------------
//  `delivery = online`  — the engine draws questions from `question_banks`,
//                         serves them on the public page and grades them.
//                         Admission uses it.
//  `delivery = manual`  — no questions; staff type per-subject points into
//                         the entry grid or import them. Block test uses it.
//  Both write the same `exam_participants` summary and the same
//  `exam_section_scores` rows. There is no second question table and no
//  second scoring path — do not add one.
//
//  `TestQuestion` (the homework quiz on `Assignment`) is a different, older
//  feature and is deliberately left alone (§2.1).
//
//  A CANDIDATE IS A LEAD (§2.2, changed 2026-09-22)
//  ------------------------------------------------
//  There is no candidates table. A lead taking the entrance test gets an
//  `exam_participants` row with `participant_kind = 'lead'`. Enrolling the
//  candidate (`POST /api/admin/leads/{id}/enrol`) DELETES the lead; the FK
//  from `exam_participants.lead_id` is `ON DELETE SET NULL`, so the sitting
//  and its score survive as an anonymous row. That is why a `lead`
//  participant may have a NULL `lead_id` — see `ExamModel.cs`.
//
//  TYPES — the `DailyAttendanceMark` / `TelegramLinkCode` pattern (§5)
//  -------------------------------------------------------------------
//      id / FK         -> text   (Guid.NewGuid().ToString())
//      timestamps      -> DateTime, Tashkent wall clock from AppClock.Now,
//                         mapped to `timestamp without time zone`. Not
//                         DateTimeOffset: these tables hold no money, and the
//                         public exam timer compares `serverNow` with
//                         `deadlineAt` in this same frame (§6).
//      calendar dates  -> text "YYYY-MM-DD" (like JournalEntry.Date)
//      points          -> decimal with an explicit numeric precision.
//
//  NOT FINANCIAL — FULL CRUD for `app_rw` (`exam_guards.sql`), no REVOKE.

/// <summary>
/// <see cref="Lead.AdmissionStatus"/> values (§2.2, §8.5). Closed list, enforced
/// by <c>ck_leads_admission_status</c>.
///
/// <para>
/// <b>There is no <c>enrolled</c> value, on purpose.</b> The spec's lifecycle
/// ends in <c>enrolled</c>, but since 2026-09-22 enrolment deletes the lead
/// (only a <see cref="LeadConversion"/> statistic survives), so no lead row can
/// ever carry that state. A reader that needs "was this candidate enrolled"
/// answers it from <c>lead_conversions</c> / the student, never from here.
/// The database rejects <c>'enrolled'</c> so nobody stores it by accident.
/// </para>
/// </summary>
public static class LeadAdmissionStatus
{
    /// <summary>An ordinary lead — not in the admission pipeline. Every existing row.</summary>
    public const string None = "none";

    /// <summary>A participant row was created for an admission exam.</summary>
    public const string Invited = "invited";

    /// <summary>The online attempt has started.</summary>
    public const string Testing = "testing";

    /// <summary>The attempt was graded.</summary>
    public const string Tested = "tested";

    /// <summary>Human decision on the candidate card — never automatic (§8.5).</summary>
    public const string Accepted = "accepted";

    /// <summary>Human decision on the candidate card — never automatic (§8.5).</summary>
    public const string Rejected = "rejected";

    /// <summary>Same list as the database CHECK.</summary>
    public static readonly IReadOnlyList<string> All = [None, Invited, Testing, Tested, Accepted, Rejected];

    public static bool IsValid(string? value) => value is not null && All.Contains(value);
}

/// <summary><see cref="Exam.Kind"/> values.</summary>
public static class ExamKind
{
    /// <summary>Entrance test for candidates (leads).</summary>
    public const string Admission = "admission";

    /// <summary>Block test for enrolled pupils.</summary>
    public const string Block = "block";

    public static readonly IReadOnlyList<string> All = [Admission, Block];
}

/// <summary><see cref="Exam.Delivery"/> values (§2.1).</summary>
public static class ExamDelivery
{
    /// <summary>The engine serves and grades questions drawn from a bank.</summary>
    public const string Online = "online";

    /// <summary>No questions; points are typed into the entry grid or imported.</summary>
    public const string Manual = "manual";

    public static readonly IReadOnlyList<string> All = [Online, Manual];
}

/// <summary>
/// <see cref="Exam.Status"/> values. Transitions are one-way and
/// service-enforced (§5.5): <c>draft → published → closed</c>, and
/// <c>draft | published → cancelled</c>.
/// </summary>
public static class ExamStatus
{
    public const string Draft = "draft";
    public const string Published = "published";
    public const string Closed = "closed";
    public const string Cancelled = "cancelled";

    public static readonly IReadOnlyList<string> All = [Draft, Published, Closed, Cancelled];
}

/// <summary><see cref="ExamParticipant.ParticipantKind"/> values.</summary>
public static class ExamParticipantKind
{
    /// <summary>A candidate — <see cref="ExamParticipant.LeadId"/>, NULL once the lead is deleted.</summary>
    public const string Lead = "lead";

    /// <summary>An enrolled pupil — <see cref="ExamParticipant.StudentId"/> is always set.</summary>
    public const string Student = "student";

    public static readonly IReadOnlyList<string> All = [Lead, Student];
}

/// <summary><see cref="ExamParticipant.Status"/> values.</summary>
public static class ExamParticipantStatus
{
    public const string Assigned = "assigned";
    public const string InProgress = "in_progress";
    public const string Finished = "finished";
    public const string Absent = "absent";
    public const string Cancelled = "cancelled";

    public static readonly IReadOnlyList<string> All = [Assigned, InProgress, Finished, Absent, Cancelled];
}

/// <summary><see cref="ExamAttempt.Status"/> values.</summary>
public static class ExamAttemptStatus
{
    public const string InProgress = "in_progress";
    public const string Finished = "finished";

    public static readonly IReadOnlyList<string> All = [InProgress, Finished];
}

/// <summary><see cref="ExamAttempt.FinishReason"/> values (§8.3).</summary>
public static class ExamFinishReason
{
    /// <summary>The candidate pressed "finish".</summary>
    public const string Manual = "manual";

    /// <summary>The deadline passed — the client's time-up path or the sweep.</summary>
    public const string Timer = "timer";

    /// <summary>Staff force-finished the attempt.</summary>
    public const string Admin = "admin";

    public static readonly IReadOnlyList<string> All = [Manual, Timer, Admin];
}

/// <summary>
/// Question bank — EduSchool "Test bazasi" (§5.1). At most one live
/// (non-archived) bank per grade × subject; that is how an exam picks a bank
/// without asking.
///
/// <para>
/// <b>No <c>QuestionsCount</c> column.</b> It is <c>count(*)</c> over
/// <see cref="Question"/>, computed by the list query — a stored counter would
/// drift from the rows. The bank's "ready / notEnough / unconfigured" state is
/// derived the same way (§3.1).
/// </para>
/// </summary>
public class QuestionBank
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>0–11 (0 = the preparatory "nol sinf").</summary>
    public int Grade { get; set; }

    /// <summary><see cref="Subject.Id"/>. <c>on delete restrict</c>.</summary>
    public string SubjectId { get; set; } = string.Empty;

    /// <summary>How many questions are drawn per sitting. NULL = not configured yet.</summary>
    public int? QuestionsPerTest { get; set; }

    /// <summary>Minutes for this bank's block, 1–600. NULL = not configured yet.</summary>
    public int? TimeLimitMin { get; set; }

    /// <summary>Points per correct answer, <c>numeric(6,2)</c>, &gt; 0. NULL = not configured yet.</summary>
    public decimal? PointsPerCorrect { get; set; }

    /// <summary>
    /// Archived banks drop out of the exam picker without destroying the exams
    /// that used them. Replaces EduSchool's dead <c>state:"draft"</c> field.
    /// </summary>
    public bool IsArchived { get; set; }

    public DateTime CreatedAt { get; set; } = AppClock.Now;

    /// <summary><see cref="AppUser.Id"/>; <c>on delete set null</c>.</summary>
    public string? CreatedByUserId { get; set; }
}

/// <summary>One question in a <see cref="QuestionBank"/> (§5.2).</summary>
public class Question
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary><see cref="QuestionBank.Id"/>; <c>on delete cascade</c>.</summary>
    public string BankId { get; set; } = string.Empty;

    /// <summary>Trimmed, non-empty (<c>ck_questions_text</c>).</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary><c>/uploads/&lt;guid&gt;.&lt;ext&gt;</c> from <c>POST /api/admin/uploads</c>.</summary>
    public string? ImageUrl { get; set; }

    /// <summary>Display order inside the bank.</summary>
    public int Order { get; set; }

    public DateTime CreatedAt { get; set; } = AppClock.Now;

    /// <summary>
    /// The 2–6 options, A–F. <b>Carries the answer key</b> (<see cref="QuestionOption.IsCorrect"/>):
    /// anything served to a candidate must be projected by hand onto a DTO
    /// without it — never <c>Include(q =&gt; q.Options)</c> and map later (§7.7).
    /// </summary>
    public List<QuestionOption> Options { get; set; } = new();
}

/// <summary>
/// One answer option (§5.3). Exactly one per question is correct: the database
/// stops two (<c>ux_question_options_one_correct</c>), the save transaction
/// stops zero. The 2–6 count is service-enforced.
/// </summary>
public class QuestionOption
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary><see cref="Question.Id"/>; <c>on delete cascade</c>.</summary>
    public string QuestionId { get; set; } = string.Empty;

    /// <summary>Trimmed, non-empty (<c>ck_question_options_text</c>).</summary>
    public string Text { get; set; } = string.Empty;

    public bool IsCorrect { get; set; }

    /// <summary>0 → A, 1 → B … 5 → F.</summary>
    public int Order { get; set; }
}

/// <summary>Exam type catalogue — EduSchool "Imtihon turi" (§5.4).</summary>
public class ExamType
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>Unique case- and whitespace-insensitively (<c>ux_exam_types_name</c>).</summary>
    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = AppClock.Now;
}

/// <summary>
/// An exam — an admission entrance test or a block test (§5.5).
/// A published or closed online exam always has a window (<see cref="OpensAt"/>,
/// <see cref="ClosesAt"/>) and a <see cref="TimeLimitMin"/> (<c>ck_exams_online_window</c>);
/// a draft may still lack them, because §8.1 fills the time limit in at publish.
/// Manual exams need none of the three.
/// </summary>
public class Exam
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    public string Title { get; set; } = string.Empty;

    /// <summary><see cref="ExamKind"/>. Immutable after create.</summary>
    public string Kind { get; set; } = ExamKind.Block;

    /// <summary><see cref="ExamDelivery"/>. Immutable after create.</summary>
    public string Delivery { get; set; } = ExamDelivery.Manual;

    /// <summary><see cref="ExamType.Id"/>; <c>on delete set null</c>.</summary>
    public string? ExamTypeId { get; set; }

    /// <summary>Admission only: the grade applied for, 0–11.</summary>
    public int? Grade { get; set; }

    /// <summary>"YYYY-MM-DD" — manual exams.</summary>
    public string? ExamDate { get; set; }

    /// <summary>Online only — window start.</summary>
    public DateTime? OpensAt { get; set; }

    /// <summary>Online only — window end; the deadline of every attempt is capped at it.</summary>
    public DateTime? ClosesAt { get; set; }

    /// <summary>
    /// Online only — total minutes for the sitting. Seeded from the sum of the
    /// banks' limits when sections are chosen, then editable (§5.6).
    /// </summary>
    public int? TimeLimitMin { get; set; }

    /// <summary><see cref="ExamStatus"/>.</summary>
    public string Status { get; set; } = ExamStatus.Draft;

    /// <summary><see cref="AppUser.Id"/>; <c>on delete set null</c>.</summary>
    public string? CreatedByUserId { get; set; }

    public DateTime CreatedAt { get; set; } = AppClock.Now;

    /// <summary>One section per subject; frozen once the exam is published (§5.5).</summary>
    public List<ExamSection> Sections { get; set; } = new();
}

/// <summary>
/// One subject inside an exam (§5.6). For online exams
/// <see cref="PointsPerCorrect"/> and <see cref="MaxScore"/> are <b>copied</b> from the
/// bank at publish, so editing the bank later never rescores a finished exam.
/// </summary>
public class ExamSection
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary><see cref="Exam.Id"/>; <c>on delete cascade</c>.</summary>
    public string ExamId { get; set; } = string.Empty;

    /// <summary><see cref="Subject.Id"/>; <c>on delete restrict</c>. Unique per exam.</summary>
    public string SubjectId { get; set; } = string.Empty;

    /// <summary><see cref="QuestionBank.Id"/>; <c>on delete restrict</c>. Required for online exams.</summary>
    public string? BankId { get; set; }

    /// <summary>Online: how many questions to draw.</summary>
    public int? QuestionCount { get; set; }

    /// <summary>Online: copied from the bank at publish. <c>numeric(6,2)</c>.</summary>
    public decimal? PointsPerCorrect { get; set; }

    /// <summary>
    /// Manual: the ceiling of the entry grid. Online: <c>QuestionCount × PointsPerCorrect</c>,
    /// written at publish. <c>numeric(6,2)</c>.
    /// </summary>
    public decimal MaxScore { get; set; }

    /// <summary>Question blocks appear in this order.</summary>
    public int Order { get; set; }
}

/// <summary>
/// One person on one exam, and the score summary of that sitting (§5.7).
/// Exactly one result per participant, so the summary lives here rather than in
/// a 1:1 results table.
///
/// <para>
/// <b>Pointers.</b> A <c>student</c> participant always has
/// <see cref="StudentId"/> and never <see cref="LeadId"/>. A <c>lead</c> participant
/// never has <see cref="StudentId"/>; its <see cref="LeadId"/> is set on insert and
/// becomes NULL when the lead is deleted (enrolment or the board's delete) — the
/// row is kept as an anonymous result.
/// </para>
/// </summary>
public class ExamParticipant
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary><see cref="Exam.Id"/>; <c>on delete cascade</c>.</summary>
    public string ExamId { get; set; } = string.Empty;

    /// <summary><see cref="ExamParticipantKind"/>.</summary>
    public string ParticipantKind { get; set; } = ExamParticipantKind.Student;

    /// <summary><see cref="Lead.Id"/>; <c>on delete set null</c> (see the class summary).</summary>
    public string? LeadId { get; set; }

    /// <summary><see cref="Student.Id"/>; <c>on delete cascade</c>.</summary>
    public string? StudentId { get; set; }

    /// <summary>Block tests: the pupil's class at assignment time; <c>on delete set null</c>.</summary>
    public string? ClassId { get; set; }

    /// <summary><see cref="ExamParticipantStatus"/>.</summary>
    public string Status { get; set; } = ExamParticipantStatus.Assigned;

    /// <summary>Online only.</summary>
    public int? CorrectCount { get; set; }

    /// <summary>Online only.</summary>
    public int? QuestionCount { get; set; }

    /// <summary><c>numeric(8,2)</c>.</summary>
    public decimal? TotalPoints { get; set; }

    /// <summary><c>numeric(8,2)</c>.</summary>
    public decimal? MaxPoints { get; set; }

    /// <summary><c>round(TotalPoints / MaxPoints × 100, 2)</c>, <c>numeric(5,2)</c>.</summary>
    public decimal? Percent { get; set; }

    public DateTime? ScoredAt { get; set; }

    /// <summary>NULL = graded by the engine. <c>on delete set null</c>.</summary>
    public string? ScoredByUserId { get; set; }

    public DateTime CreatedAt { get; set; } = AppClock.Now;
}

/// <summary>Points of one participant in one section (§5.8). PK (participant, section).</summary>
public class ExamSectionScore
{
    /// <summary><see cref="ExamParticipant.Id"/>; <c>on delete cascade</c>.</summary>
    public string ParticipantId { get; set; } = string.Empty;

    /// <summary><see cref="ExamSection.Id"/>; <c>on delete cascade</c>.</summary>
    public string SectionId { get; set; } = string.Empty;

    /// <summary>Online only.</summary>
    public int? CorrectCount { get; set; }

    /// <summary>Online only.</summary>
    public int? QuestionCount { get; set; }

    /// <summary>Manual: typed. Online: computed. 0 ≤ points ≤ <see cref="MaxPoints"/>.</summary>
    public decimal Points { get; set; }

    /// <summary>Copied from <see cref="ExamSection.MaxScore"/>.</summary>
    public decimal MaxPoints { get; set; }

    public DateTime UpdatedAt { get; set; } = AppClock.Now;
}

/// <summary>
/// The public entrance-test link (§5.9, §7.2). Modelled on
/// <see cref="TelegramLinkCode"/>: <b>the token itself is never stored</b>, only
/// its SHA-256, so a leaked database copy holds no live links. The URL is shown
/// once, at issue; re-issuing revokes this row and inserts a new one, so at most
/// one live link per participant exists (<c>ux_exam_invitations_live</c>).
/// </summary>
public class ExamInvitation
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary><see cref="ExamParticipant.Id"/>; <c>on delete cascade</c>.</summary>
    public string ParticipantId { get; set; } = string.Empty;

    /// <summary>SHA-256 hex of the token. Unique; lookups go through it.</summary>
    public string TokenHash { get; set; } = string.Empty;

    /// <summary>Last 6 characters of the token, for support ("…7fQ2xA").</summary>
    public string TokenHint { get; set; } = string.Empty;

    /// <summary>Defaults to <see cref="Exam.OpensAt"/>.</summary>
    public DateTime ValidFrom { get; set; }

    /// <summary>Defaults to <see cref="Exam.ClosesAt"/>. Must be after <see cref="ValidFrom"/>.</summary>
    public DateTime ValidUntil { get; set; }

    public DateTime IssuedAt { get; set; } = AppClock.Now;

    /// <summary><c>on delete set null</c>.</summary>
    public string? IssuedByUserId { get; set; }

    /// <summary>First successful <c>state</c> call.</summary>
    public DateTime? FirstOpenedAt { get; set; }

    public DateTime? RevokedAt { get; set; }

    /// <summary><c>on delete set null</c>.</summary>
    public string? RevokedByUserId { get; set; }
}

/// <summary>
/// The one online attempt of a participant (§5.10) — unique per participant;
/// a retake needs an admin reset (§8.3), which deletes this row.
/// </summary>
public class ExamAttempt
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary><see cref="ExamParticipant.Id"/>; <c>on delete cascade</c>. Unique.</summary>
    public string ParticipantId { get; set; } = string.Empty;

    /// <summary>
    /// The invitation the attempt was started with. <c>on delete no action</c>:
    /// an invitation that has an attempt cannot be deleted on its own, but the
    /// pair still cascades away together with the participant (see ExamModel.cs).
    /// </summary>
    public string InvitationId { get; set; } = string.Empty;

    public DateTime StartedAt { get; set; } = AppClock.Now;

    /// <summary><c>StartedAt + Exam.TimeLimitMin</c>, capped at <see cref="Exam.ClosesAt"/>.</summary>
    public DateTime DeadlineAt { get; set; }

    public DateTime? FinishedAt { get; set; }

    /// <summary><see cref="ExamFinishReason"/>; set together with <see cref="FinishedAt"/>.</summary>
    public string? FinishReason { get; set; }

    /// <summary><see cref="ExamAttemptStatus"/>.</summary>
    public string Status { get; set; } = ExamAttemptStatus.InProgress;

    /// <summary>
    /// SHA-256 of the value in the device cookie (§7.4). "Unlock device" sets it
    /// to the empty string (the column is NOT NULL): empty = not bound, and the
    /// next request re-binds to whatever device arrives.
    /// </summary>
    public string DeviceSessionHash { get; set; } = string.Empty;

    /// <summary>Coarse, e.g. "Chrome · Windows" — shown to an unauthenticated stranger.</summary>
    public string DeviceLabel { get; set; } = string.Empty;

    public string FirstIp { get; set; } = string.Empty;

    /// <summary>Truncated to 400 characters.</summary>
    public string UserAgent { get; set; } = string.Empty;

    /// <summary>Accepted <c>answer</c> calls — progress and the abuse ceiling (§7.6).</summary>
    public int AnsweredCount { get; set; }

    /// <summary>Set when the ceiling is hit; the attempt is NOT terminated.</summary>
    public bool AbuseFlagged { get; set; }
}

/// <summary>
/// One question of one attempt's paper (§5.11). The whole paper is inserted at
/// <c>start</c> with <see cref="SelectedOptionId"/> = NULL, so the question set and
/// order never change under the candidate and grading never re-draws.
///
/// <para>
/// <b>No <c>IsCorrect</c> column, on purpose</b>: correctness is computed at
/// grading time from <see cref="QuestionOption.IsCorrect"/>. Storing it here would
/// put the answer key one careless <c>SELECT *</c> away from the public endpoint.
/// </para>
/// </summary>
public class ExamAnswer
{
    /// <summary><see cref="ExamAttempt.Id"/>; <c>on delete cascade</c>. PK part.</summary>
    public string AttemptId { get; set; } = string.Empty;

    /// <summary><see cref="Question.Id"/>; <c>on delete restrict</c>. PK part.</summary>
    public string QuestionId { get; set; } = string.Empty;

    /// <summary>Denormalised so scoring is one GROUP BY; <c>on delete cascade</c>.</summary>
    public string SectionId { get; set; } = string.Empty;

    /// <summary>0-based position in this attempt's paper, running across sections. Unique per attempt.</summary>
    public int Order { get; set; }

    /// <summary><see cref="QuestionOption.Id"/>; <c>on delete restrict</c>. NULL = not answered yet.</summary>
    public string? SelectedOptionId { get; set; }

    /// <summary>Set together with <see cref="SelectedOptionId"/>.</summary>
    public DateTime? AnsweredAt { get; set; }
}
